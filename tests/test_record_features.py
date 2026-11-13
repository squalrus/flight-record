"""Backlog 1.4: a recorded feature log replays identically.

1. Recording the same audio through the real-time capture path and offline gives the
   same log, message for message (features depend only on the audio).
2. The log replays at its original timing.
"""

import json
import subprocess
import sys
from pathlib import Path

import numpy as np

from flightrec.audio import write_wav
from flightrec.messages import FeaturesMessage, read_log, replay

SCRIPT = Path(__file__).resolve().parents[1] / "tools" / "record_features.py"
SR = 48_000


def music(seconds: float) -> np.ndarray:
    """Kicks, offbeat noise ticks, and a chord: enough to exercise every feature."""
    rng = np.random.default_rng(5)
    n = int(seconds * SR)
    t = np.arange(n) / SR
    audio = 0.1 * sum(np.sin(2 * np.pi * f * t) for f in (261.6, 329.6, 392.0))
    k = int(0.15 * SR)
    kick = 0.7 * np.sin(2 * np.pi * 60 * np.arange(k) / SR) * np.exp(-np.arange(k) / (0.04 * SR))
    h = int(0.03 * SR)
    hat = 0.2 * np.diff(rng.standard_normal(h + 1)) * np.exp(-np.arange(h) / (0.006 * SR))
    for beat in np.arange(0.1, seconds - 0.2, 0.5):
        i, j = int(beat * SR), int((beat + 0.25) * SR)
        audio[i : i + k] += kick
        audio[j : j + h] += hat
    return np.stack([audio, 0.9 * audio], axis=1).astype(np.float32)


def record(*args: str) -> str:
    result = subprocess.run(
        [sys.executable, str(SCRIPT), *args], capture_output=True, text=True, timeout=120
    )
    assert result.returncode == 0, result.stderr
    return result.stdout


def test_real_time_and_offline_logs_are_identical(tmp_path: Path) -> None:
    clip = tmp_path / "clip.wav"
    write_wav(clip, SR, music(3.0), "float32")
    live_log = tmp_path / "live.jsonl"
    offline_log = tmp_path / "offline.jsonl"

    record("--wav", str(clip), "--out", str(live_log))  # real time, through Capture
    record("--wav", str(clip), "--offline", "--out", str(offline_log))

    live = list(read_log(live_log))
    offline = list(read_log(offline_log))
    assert len(offline) == 3 * SR // 512
    assert live == offline  # message for message, every field
    assert any(m.onset for m in live) and any(m.onset_low for m in live)
    assert all(isinstance(m, FeaturesMessage) for m in live)


def test_meta_and_saved_audio(tmp_path: Path) -> None:
    clip = tmp_path / "clip.wav"
    write_wav(clip, SR, music(1.0), "float32")
    log = tmp_path / "run.jsonl"
    record("--wav", str(clip), "--offline", "--save-audio", "--out", str(log))

    meta = json.loads(log.with_suffix(".meta.json").read_text())
    assert meta["samplerate"] == SR
    assert meta["messages"] == SR // 512
    assert meta["message_rate_hz"] == round(SR / 512, 2)
    assert meta["mode"] == "offline"
    assert (tmp_path / meta["audio"]).exists()

    # The saved audio re-records to the same log.
    again = tmp_path / "again.jsonl"
    record("--wav", str(tmp_path / meta["audio"]), "--offline", "--out", str(again))
    assert list(read_log(again)) == list(read_log(log))


def test_recorded_log_replays_at_original_timing(tmp_path: Path) -> None:
    clip = tmp_path / "clip.wav"
    write_wav(clip, SR, music(2.0), "float32")
    log = tmp_path / "run.jsonl"
    record("--wav", str(clip), "--offline", "--out", str(log))
    messages = list(read_log(log))

    now = [0.0]

    def clock() -> float:
        return now[0]

    def sleep(seconds: float) -> None:
        now[0] += seconds

    yielded = [(m.t, clock()) for m in replay(messages, clock=clock, sleep=sleep)]
    t0 = messages[0].t
    assert all(abs(at - (t - t0)) < 1e-9 for t, at in yielded)
    assert yielded[-1][1] == messages[-1].t - t0
