"""Smoke tests: the shared environment from experiments/requirements.txt is usable.

These do not import sounddevice, because it needs the PortAudio system library at import
time, which CI runners may not have. Hardware-facing code is covered by manual checks.
"""

import sys

import numpy as np
from scipy import signal


def test_python_version() -> None:
    assert sys.version_info >= (3, 11)


def test_numpy_scipy_fft_finds_tone() -> None:
    sample_rate = 48_000
    t = np.arange(sample_rate) / sample_rate
    tone = np.sin(2 * np.pi * 440.0 * t)

    freqs, power = signal.periodogram(tone, fs=sample_rate)

    assert abs(freqs[np.argmax(power)] - 440.0) < 1.0
