"""Experiment 03 harness, offline: planning, clips, vinyl simulation, scoring, cache, budget.

No network: a fake service stands in for AudD / ACRCloud.
"""

import importlib.util
import io
import sys
from pathlib import Path

import numpy as np
import pytest
import soundfile

from flightrec.identification import Result, Service

spec = importlib.util.spec_from_file_location(
    "fingerprint", Path(__file__).resolve().parent / "fingerprint.py"
)
fp = importlib.util.module_from_spec(spec)
sys.modules["fingerprint"] = fp  # dataclasses look their module up by name
spec.loader.exec_module(fp)

SR = 44_100


@pytest.fixture
def songs(tmp_path: Path) -> list[Path]:
    """Two 40 s 'songs' named in the store pattern, so tags fall back to the filename."""
    paths = []
    for i, (artist, title) in enumerate([("Kalax", "Calling"), ("Glowbug", "False Metal")]):
        t = np.arange(40 * SR) / SR
        audio = 0.3 * np.sin(2 * np.pi * (220 + 110 * i) * t)
        path = tmp_path / f"{artist} - Album - 0{i + 1} {title}.wav"
        soundfile.write(str(path), np.stack([audio, audio], 1), SR)
        paths.append(path)
    return paths


class FakeService(Service):
    """'Identifies' clean clips of 10 s or more; counts calls."""

    name = "fake"

    def __init__(self, answers: dict[int, tuple[str, str]]) -> None:
        super().__init__()
        self.answers = answers
        self.calls = 0

    def identify(self, wav: bytes) -> Result:
        self.calls += 1
        data, sr = soundfile.read(io.BytesIO(wav))
        if len(data) < 10 * sr:
            return Result(self.name, False)
        key = round(float(np.abs(np.fft.rfft(data[:sr])).argmax()))
        artist, title = self.answers.get(key, ("Someone", "Else"))
        return Result(self.name, True, artist=artist, title=title, latency_s=0.5)


def test_plan_counts_and_clip_bounds(songs: list[Path]) -> None:
    clips = fp.plan(songs, [5.0, 15.0], [0.0, 0.5, 1.0], ["clean", "vinyl"])
    assert len(clips) == 2 * 2 * 3 * 2
    for clip in clips:
        assert 0.0 <= clip.start_s <= 40.0 - clip.length_s
    assert clips[0].artist == "Kalax" and clips[0].title == "Calling"


def test_clip_wav_is_mono_16bit_and_right_length(songs: list[Path]) -> None:
    clip = fp.plan(songs[:1], [10.0], [0.5], ["clean"])[0]
    info = soundfile.info(io.BytesIO(fp.clip_wav(clip)))
    assert info.channels == 1 and info.subtype == "PCM_16"
    assert info.frames == 10 * SR


def test_vinyl_simulation_is_audible_but_keeps_the_music() -> None:
    t = np.arange(5 * SR) / SR
    music = (0.3 * np.sin(2 * np.pi * 440 * t)).astype(np.float32)
    worn = fp.simulate_vinyl(music, SR, seed=1)
    assert np.array_equal(worn, fp.simulate_vinyl(music, SR, seed=1))  # repeatable
    # Wow shifts timing, so compare spectra rather than subtracting waveforms.
    power = np.abs(np.fft.rfft(worn)) ** 2
    freqs = np.fft.rfftfreq(len(worn), 1 / SR)
    tone = power[(freqs > 430) & (freqs < 450)].sum() / power.sum()
    assert 0.8 < tone < 0.9999  # mostly still the music, with a real noise floor
    assert abs(freqs[np.argmax(power)] - 440) < 2  # pitch survives the wow
    assert power[freqs < 40].sum() > 0  # rumble present


@pytest.mark.parametrize(
    ("artist", "title", "expected"),
    [
        ("Kalax", "Calling", "correct"),
        ("KALAX feat. Frankmusik", "Calling (Radio Edit)", "correct"),
        ("Alice Deejay", "Better Off Alone", "other_version"),
        ("Someone", "Else", "wrong"),
    ],
)
def test_outcome_scoring(artist: str, title: str, expected: str) -> None:
    truth_title = "Better Off Alone [Caspro Cover]" if expected == "other_version" else "Calling"
    truth_artist = "Caspro" if expected == "other_version" else "Kalax"
    clip = fp.Clip(Path("x.mp3"), truth_artist, truth_title, 0.0, 10.0, 0.5, "clean")
    assert fp.outcome(clip, Result("s", True, artist=artist, title=title)) == expected
    assert fp.outcome(clip, Result("s", False)) == "no_match"
    assert fp.outcome(clip, Result("s", False, error="HTTP 401")) == "error"


def test_run_caches_and_respects_the_budget(songs: list[Path], tmp_path: Path) -> None:
    clips = fp.plan(songs, [5.0, 10.0], [0.5], ["clean"])  # 4 clips
    answers = {220: ("Kalax", "Calling"), 330: ("Glowbug", "False Metal")}
    service = FakeService(answers)
    cache = fp.Cache(tmp_path / "cache")

    rows, sent, skipped = fp.run(clips, [service], cache, max_requests=3)
    assert (sent, skipped, len(rows)) == (3, 1, 3)

    rows, sent, skipped = fp.run(clips, [service], cache, max_requests=3)
    assert (sent, skipped) == (1, 0)  # only the one not yet cached
    assert service.calls == 4
    outcomes = {(r["song"], r["length_s"]): r["outcome"] for r in rows}
    assert outcomes[("Kalax - Calling", 10.0)] == "correct"
    assert outcomes[("Kalax - Calling", 5.0)] == "no_match"
    assert sum(r["cached"] for r in rows) == 3


def test_errors_are_not_cached(songs: list[Path], tmp_path: Path) -> None:
    class Down(Service):
        name = "down"

        def identify(self, wav: bytes) -> Result:
            return Result(self.name, False, error="HTTP 503")

    clips = fp.plan(songs[:1], [5.0], [0.5], ["clean"])
    cache = fp.Cache(tmp_path / "cache")
    rows, sent, _ = fp.run(clips, [Down()], cache, max_requests=5)
    assert rows[0]["outcome"] == "error"
    assert not (tmp_path / "cache").exists()


def test_dry_run_sends_nothing(songs: list[Path], capsys: pytest.CaptureFixture) -> None:
    assert fp.main(["--songs", str(songs[0].parent), "--dry-run", "--lengths", "5"]) == 0
    out = capsys.readouterr().out
    assert "2 songs x 1 lengths x 3 positions x 2 conditions = 12 clips" in out
    assert "uncached requests needed" in out
