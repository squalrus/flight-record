"""Experiment 02 explorer runs from a WAV with no interface or Pygame (offline and replay)."""

import json
import subprocess
import sys
from pathlib import Path

import numpy as np

from flightrec.audio import write_wav
from flightrec.messages import FeaturesMessage, read_log

SCRIPT = Path(__file__).resolve().parent / "explore.py"
SR = 48_000


def click_track(path: Path, seconds: float, bpm: float = 120.0) -> None:
    audio = np.zeros(int(seconds * SR), dtype=np.float32)
    rng = np.random.default_rng(2)
    n = int(0.02 * SR)
    burst = 0.6 * rng.standard_normal(n) * np.exp(-np.arange(n) / (0.004 * SR))
    for t in np.arange(0.2, seconds, 60.0 / bpm):
        i = int(t * SR)
        audio[i : i + n] += burst[: len(audio) - i]
    write_wav(path, SR, np.stack([audio, audio], axis=1))


def run(*args: str) -> str:
    result = subprocess.run(
        [sys.executable, str(SCRIPT), *args], capture_output=True, text=True, timeout=120
    )
    assert result.returncode == 0, result.stderr
    return result.stdout


def test_offline_finds_tempo_and_logs_contract_messages(tmp_path: Path) -> None:
    click_track(tmp_path / "clicks.wav", 12.0)
    log = tmp_path / "features.jsonl"
    out = run("--wav", str(tmp_path / "clicks.wav"), "--offline", "--log", str(log))

    assert "tempo 1" in out  # 119.x / 120.x BPM
    raw = [json.loads(line) for line in log.read_text().splitlines()]
    assert raw[0]["v"] == 1 and raw[0]["type"] == "features"
    messages = list(read_log(log))  # every line validates against the contract
    assert len(messages) == 12 * SR // 512
    assert all(isinstance(m, FeaturesMessage) for m in messages)
    assert abs(messages[-1].tempo - 120.0) < 2.5
    assert sum(m.onset for m in messages) >= 20  # 24 clicks


def test_headless_replay_runs_the_live_path(tmp_path: Path) -> None:
    click_track(tmp_path / "clicks.wav", 1.5)
    out = run("--wav", str(tmp_path / "clicks.wav"), "--headless", "--no-log")
    assert "skipped 0" in out
    assert "triggers:" in out
