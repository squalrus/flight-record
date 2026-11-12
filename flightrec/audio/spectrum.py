"""Spectrum, band, and level analysis on frames read from the capture ring buffer.

Run these on an analysis or display thread, never in the audio callback.
"""

from __future__ import annotations

import numpy as np

FLOOR_DB = -120.0
"""Value used for silence instead of -inf, so results stay finite and drawable."""


def to_db(x: np.ndarray | float, floor: float = FLOOR_DB) -> np.ndarray | float:
    """Amplitude (1.0 = full scale) to dBFS, clamped at ``floor``."""
    db = 20.0 * np.log10(np.maximum(x, 10.0 ** (floor / 20.0)))
    return float(db) if np.ndim(db) == 0 else db


def level_dbfs(frames: np.ndarray) -> tuple[float, float]:
    """RMS and peak of ``frames`` (all channels together) in dBFS."""
    rms = float(np.sqrt(np.mean(np.square(frames, dtype=np.float64))))
    peak = float(np.max(np.abs(frames))) if frames.size else 0.0
    return to_db(rms), to_db(peak)


def channel_levels(frames: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Per-channel RMS and peak in dBFS, each shape ``(channels,)``."""
    rms = np.sqrt(np.mean(np.square(frames, dtype=np.float64), axis=0))
    peak = np.max(np.abs(frames), axis=0)
    return to_db(rms), to_db(peak)


class Spectrum:
    """Hann-windowed magnitude spectrum of the channel mix, in dBFS.

    Scaled so a full-scale sine reads 0 dBFS at its bin. ``freqs`` holds the bin centers.
    """

    def __init__(self, n_fft: int, samplerate: int) -> None:
        if n_fft < 16 or n_fft & (n_fft - 1):
            raise ValueError("n_fft must be a power of two >= 16")
        self.n_fft = n_fft
        self.samplerate = samplerate
        self.window = np.hanning(n_fft).astype(np.float32)
        self._scale = 2.0 / float(np.sum(self.window))
        self.freqs = np.fft.rfftfreq(n_fft, 1.0 / samplerate)
        self._mono = np.empty(n_fft, dtype=np.float32)

    def compute(self, frames: np.ndarray) -> np.ndarray:
        """Magnitude in dBFS per bin for ``frames`` of shape ``(n_fft, channels)``."""
        if frames.shape[0] != self.n_fft:
            raise ValueError(f"expected {self.n_fft} frames, got {frames.shape[0]}")
        np.mean(frames, axis=1, out=self._mono)
        self._mono *= self.window
        magnitude = np.abs(np.fft.rfft(self._mono)) * self._scale
        return to_db(magnitude)


class LogBands:
    """Group spectrum bins into ``n`` log-spaced bands between ``fmin`` and ``fmax``.

    Each band reports its loudest bin. Low bands narrower than one bin use the nearest
    bin, so every band has a value.
    """

    def __init__(
        self, freqs: np.ndarray, n: int, fmin: float = 20.0, fmax: float | None = None
    ) -> None:
        fmax = min(fmax or float(freqs[-1]), float(freqs[-1]))
        self.edges = np.geomspace(fmin, fmax, n + 1)
        self.centers = np.sqrt(self.edges[:-1] * self.edges[1:])
        lo = np.searchsorted(freqs, self.edges[:-1], side="left")
        hi = np.searchsorted(freqs, self.edges[1:], side="right")
        nearest = np.clip(np.searchsorted(freqs, self.centers), 0, len(freqs) - 1)
        empty = hi <= lo
        lo[empty] = nearest[empty]
        hi[empty] = nearest[empty] + 1
        self._lo = lo
        self._hi = hi

    def compute(self, spectrum_db: np.ndarray) -> np.ndarray:
        """Band levels in dB, shape ``(n,)``."""
        return np.array([spectrum_db[a:b].max() for a, b in zip(self._lo, self._hi, strict=True)])
