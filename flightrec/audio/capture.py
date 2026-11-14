"""The capture path: a source feeding a ring buffer. Identical for live, replay, and null."""

from __future__ import annotations

import numpy as np

from flightrec.audio.ring import RingBuffer
from flightrec.audio.sources import AudioSource


class Capture:
    """Connect ``source`` to a ``RingBuffer`` holding the last ``seconds`` of audio.

    Analysis code reads from ``ring`` (``read_latest`` for FFT windows, ``read_from`` for
    gap-free sequential processing) on its own thread, never inside the audio callback.

    Timing diagnostics, all from block arrival times. Each block's *offset* is its arrival
    time minus the audio time it ends at (frames / samplerate); a perfectly steady source
    has a constant offset.

    - ``lag``: the latest block's offset relative to the first block's, in seconds. Signed:
      negative if the first block happened to arrive late. A steady trend over a long run is
      drift between the device's sample clock and the computer's clock.
    - ``max_lag``: spread between the latest- and earliest-arriving blocks (max offset minus
      min offset), so always >= 0 and independent of how the first block landed.
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
        self.offset: float | None = None
        """The latest block's offset: its arrival ``perf_counter()`` minus its end time in
        the audio (frames / samplerate). None until the first block."""
        self._max_gap = 0.0
        self._first_offset: float | None = None
        self._min_offset = 0.0
        self._max_offset = 0.0
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
    def base_offset(self) -> float | None:
        """The smallest block offset so far: the earliest any block arrived, relative to
        its audio time. ``base_offset + frame / samplerate`` is when audio frame ``frame``
        reached the computer on the ``perf_counter()`` clock, without delivery jitter.
        None until the first block."""
        return None if self._first_offset is None else self._min_offset

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
        offset = t - end_frame / self._sr
        if self._first_offset is None:
            self._first_offset = self._min_offset = self._max_offset = offset
        elif offset < self._min_offset:
            self._min_offset = offset
        elif offset > self._max_offset:
            self._max_offset = offset
        self.offset = offset
        self.lag = offset - self._first_offset
        self.max_lag = self._max_offset - self._min_offset
        if self.last_t is not None and t - self.last_t > self._max_gap:
            self._max_gap = t - self.last_t
        self.last_t = t
        self.blocks += 1
