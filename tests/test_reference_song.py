"""The synthesized reference song: its truth is consistent, and the feature pipeline finds it.

The pipeline thresholds sit below what was measured on 2026-11-13 (A minor at 128 BPM: key
held 99%, tempo 128 throughout, 175 of 176 kicks found 24-30 ms after the hit, no onsets
away from a note or in the tail), so a regression fails while ordinary noise does not.
"""

from __future__ import annotations

import json
import subprocess
import sys
from collections import Counter
from pathlib import Path

import numpy as np
import pytest

from flightrec.audio import FeatureExtractor, read_audio
from flightrec.reference_song import (
    DEFAULT_SECTIONS,
    LAYERS,
    Truth,
    parse_key,
    render,
    section,
)

ROOT = Path(__file__).resolve().parents[1]
WARMUP_S = 10.0


def features(frames: np.ndarray, samplerate: int) -> list:
    fx = FeatureExtractor(samplerate)
    padded = np.concatenate([np.zeros((fx.window, frames.shape[1]), np.float32), frames])
    return [
        fx.process(padded[end : end + fx.window], end)
        for end in range(fx.hop, len(frames) + 1, fx.hop)
    ]


def share(counter: Counter, value: object) -> float:
    return counter[value] / max(sum(counter.values()), 1)


# --- the song itself ---------------------------------------------------------------------


def test_truth_matches_the_arrangement() -> None:
    frames, truth = render(120.0, "A minor")
    bars = sum(s.bars for s in DEFAULT_SECTIONS)
    assert truth.music_end == pytest.approx(bars * 2.0)  # a bar is 2 s at 120 BPM
    assert len(truth.beats) == 4 * bars
    assert np.allclose(np.diff(truth.beats), 0.5)
    assert frames.shape == (round(truth.length * truth.samplerate), 2)
    assert np.max(np.abs(frames)) <= 1.0
    starts = [s.start for s in truth.sections]
    assert starts[0] == 0.0
    for a, b in zip(truth.sections, truth.sections[1:], strict=False):
        assert a.end == pytest.approx(b.start)
    # Every kick is on a beat, only in sections that have kicks, and snares are on 2 and 4.
    in_kick_sections = [
        t
        for t in truth.beats
        for s in truth.sections
        if s.start <= t < s.end and "kick" in s.layers
    ]
    assert truth.hits["kick"] == in_kick_sections
    assert all(round((t / 0.5) % 4) in (1, 3) for t in truth.hits["snare"])
    breakdown = next(s for s in truth.sections if s.name == "breakdown")
    assert not [t for t in truth.hits["kick"] if breakdown.start <= t < breakdown.end]


def test_render_is_deterministic_and_silent_after_the_music() -> None:
    a, truth = render(140.0, "C major", sections=(section("all", 4, *LAYERS),))
    b, _ = render(140.0, "C major", sections=(section("all", 4, *LAYERS),))
    assert np.array_equal(a, b)
    after = a[int((truth.music_end + 0.5) * truth.samplerate) :]
    assert np.max(np.abs(after)) < 1e-3


def test_keys_parse_and_reject_nonsense() -> None:
    assert parse_key("A minor") == (9, "minor")
    assert parse_key("Bb major") == (10, "major")
    assert parse_key("A# major") == (10, "major")
    assert render(120, "G# minor", sections=(section("x", 1, "chords"),))[1].key == "Ab minor"
    for bad in ("H minor", "A dorian", "minor"):
        with pytest.raises(ValueError):
            parse_key(bad)
    with pytest.raises(ValueError):
        section("x", 1, "cowbell")


def test_tool_writes_wav_and_truth(tmp_path: Path) -> None:
    out = tmp_path / "ref.wav"
    subprocess.run(
        [sys.executable, str(ROOT / "tools" / "make_reference_song.py"), "--bpm", "150",
         "--key", "E minor", "--out", str(out)],
        check=True, capture_output=True, cwd=ROOT,
    )  # fmt: skip
    samplerate, frames = read_audio(out)
    data = json.loads(out.with_suffix(".json").read_text(encoding="utf-8"))
    assert (data["bpm"], data["key"], data["samplerate"]) == (150.0, "E minor", samplerate)
    assert len(frames) / samplerate == pytest.approx(data["length"], abs=1e-3)


# --- the pipeline on it ------------------------------------------------------------------


@pytest.fixture(scope="module")
def minor_128() -> tuple[Truth, list]:
    frames, truth = render(128.0, "A minor")
    return truth, features(frames, truth.samplerate)


def test_pipeline_finds_key_and_tempo(minor_128: tuple[Truth, list]) -> None:
    truth, feats = minor_128
    settled = [f for f in feats if f.t >= WARMUP_S]
    keys = Counter(f.key for f in settled if f.key)
    tempos = Counter(
        round(f.tempo / 2) * 2 for f in settled if f.tempo and f.tempo_confidence >= 0.3
    )
    assert share(keys, truth.key) >= 0.9
    assert share(tempos, 128) >= 0.9


def test_pipeline_finds_every_kick_soon_after_it(minor_128: tuple[Truth, list]) -> None:
    truth, feats = minor_128
    onsets = np.array([f.t for f in feats if f.onset])
    lags = []
    for hit in truth.hits["kick"]:
        near = onsets[(onsets >= hit - 0.01) & (onsets <= hit + 0.08)] - hit
        if len(near):
            lags.append(near.min())
    assert len(lags) >= 0.98 * len(truth.hits["kick"])
    assert max(lags) <= 0.04  # window and hop latency, about 25 ms here


def test_pipeline_has_no_onsets_away_from_notes(minor_128: tuple[Truth, list]) -> None:
    truth, feats = minor_128
    notes = np.sort(np.concatenate([np.asarray(v) for v in truth.hits.values()]))
    stray = [
        f.t for f in feats if f.onset and not ((notes >= f.t - 0.08) & (notes <= f.t + 0.01)).any()
    ]
    assert len(stray) <= 2
    assert not [f for f in feats if f.onset and f.t > truth.music_end + 1.0]


def test_pipeline_sees_the_layers_change(minor_128: tuple[Truth, list]) -> None:
    truth, feats = minor_128

    def mean(name: str, attr: str) -> float:
        s = next(s for s in truth.sections if s.name == name)
        return float(np.mean([getattr(f, attr) for f in feats if s.start + 1 <= f.t < s.end]))

    assert mean("bass", "mid") > mean("drums", "mid") + 0.2  # the bass line's harmonics
    assert mean("peak", "treble") > mean("chords", "treble") + 0.03  # the lead
    assert mean("breakdown", "bass") < 0.1  # no kick, no bass
    assert mean("breakdown", "rms_db") < mean("peak", "rms_db") - 4


def test_pipeline_finds_a_major_key_at_another_tempo() -> None:
    frames, truth = render(100.0, "F major", sections=(section("all", 16, *LAYERS),))
    settled = [f for f in features(frames, truth.samplerate) if f.t >= WARMUP_S]
    keys = Counter(f.key for f in settled if f.key)
    tempos = Counter(
        round(f.tempo / 2) * 2 for f in settled if f.tempo and f.tempo_confidence >= 0.3
    )
    assert keys.most_common(1)[0][0] == "F major"
    assert share(tempos, 100) >= 0.9
