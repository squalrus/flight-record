"""The capture path: a source feeding a ring buffer. Identical for live, replay, and null."""

from __future__ import annotations

import numpy as np

from flightrec.audio.ring import RingBuffer
from flightrec.audio.sources import AudioSource


class Capture:
    """Connect ``source`` to a ``RingBuffer`` holding the last ``seconds`` of audio.

    Analysis code reads from ``ring`` (``read_latest`` for FFT windows, ``read_from`` for
    gap-free sequential processing) on its own thread, never inside the audio callback.

    Timing diagnostics, all from block arrival times:

    - ``lag``: how late the latest block arrived versus an ideal clock started at the first
      block. Over long runs this also shows drift between the device's sample clock and the
      computer's clock. ``max_lag`` is its maximum.
    - ``pop_max_gap()``: longest interval between consecutive blocks since the last call.
      A gap far above the block period means delivery stalled.
    """

    def __init__(self, source: AudioSource, seconds: float = 10.0) -> None:
        self.source = source
        self.ring = RingBuffer(max(1, int(seconds * source.samplerate)), source.channels)
        self.blocks = 0
        self.last_t: float | None = None
        """``time.perf_counter()`` when the most recent block arrived."""
        self.lag = 0.0
        self.max_lag = 0.0
        self._max_gap = 0.0
        self._t_first: float | None = None
        self._sr = float(source.samplerate)

    @property
    def block_period(self) -> float:
        """Nominal seconds between blocks."""
        return self.source.blocksize / self._sr

    def pop_max_gap(self) -> float:
        """Longest gap between block arrivals since the previous call, then reset.

        Unsynchronized with the audio thread, so a gap that lands exactly during the reset
        can be lost; fine for once-a-second logging.
        """
        gap, self._max_gap = self._max_gap, 0.0
        return gap

    @property
    def samplerate(self) -> int:
        return self.source.samplerate

    @property
    def channels(self) -> int:
        return self.source.channels

    @property
    def frames(self) -> int:
        """Frames captured so far."""
        return self.ring.frames_written

    def start(self) -> Capture:
        self.source.start(self._on_block)
        return self

    def stop(self) -> None:
        self.source.stop()

    def wait(self, timeout: float | None = None) -> bool:
        """Wait for a finite source to finish; True if it did."""
        return self.source.wait(timeout)

    def latest(self, n: int, out: np.ndarray | None = None) -> tuple[np.ndarray, int]:
        """The most recent ``n`` frames and the end frame index; see ``RingBuffer``."""
        return self.ring.read_latest(n, out)

    def __enter__(self) -> Capture:
        return self.start()

    def __exit__(self, *exc: object) -> None:
        self.stop()

    def _on_block(self, block: np.ndarray, start_frame: int, t: float) -> None:
        # Runs on the audio thread: copy into preallocated storage, no allocation.
        self.ring.write(block)
        end_frame = start_frame + len(block)
        if self._t_first is None:
            self._t_first = t - end_frame / self._sr
        self.lag = t - (self._t_first + end_frame / self._sr)
        if self.lag > self.max_lag:
            self.max_lag = self.lag
        if self.last_t is not None and t - self.last_t > self._max_gap:
            self._max_gap = t - self.last_t
        self.last_t = t
        self.blocks += 1
