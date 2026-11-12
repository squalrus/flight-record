"""Experiment 01 runs end to end from a WAV with no interface connected (headless)."""

import csv
import subprocess
import sys
from pathlib import Path

import numpy as np

from flightrec.audio import write_wav

SCRIPT = Path(__file__).resolve().parent / "visualize.py"


def test_headless_replay_logs_stats(tmp_path: Path) -> None:
    sr = 48_000
    t = np.arange(int(1.6 * sr)) / sr
    tone = 0.5 * np.sin(2 * np.pi * 440 * t)
    write_wav(tmp_path / "tone.wav", sr, np.stack([tone, tone], axis=1))
    log = tmp_path / "run.csv"

    result = subprocess.run(
        [sys.executable, str(SCRIPT), "--wav", str(tmp_path / "tone.wav"), "--headless"]
        + ["--log", str(log)],
        capture_output=True,
        text=True,
        timeout=60,
        check=True,
    )

    assert "overflows 0" in result.stdout
    rows = list(csv.DictReader(log.open()))
    assert rows, result.stdout
    row = rows[0]
    assert float(row["fps"]) > 20
    assert -7.5 < float(row["peak_dbfs"]) < -5.5  # 0.5 amplitude is -6 dBFS
    assert int(row["clip_frames"]) == 0
    assert float(row["lat_p50_ms"]) >= 0
