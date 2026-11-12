"""Fixed-size multichannel ring buffer for audio frames."""

from __future__ import annotations

import threading

import numpy as np


class OverrunError(RuntimeError):
    """Requested frames were already overwritten (the reader fell too far behind)."""


class RingBuffer:
    """Preallocated float32 ring of audio frames, shape ``(capacity, channels)``.

    Frame ``k`` (counted from the first write) lives at row ``k % capacity``. One writer
    (the audio callback) and any number of readers. ``write`` only copies into preallocated
    storage and never allocates, so it is safe in a real-time callback. Readers copy out
    under a short lock.
    """

    def __init__(self, capacity: int, channels: int) -> None:
        if capacity <= 0 or channels <= 0:
            raise ValueError("capacity and channels must be positive")
        self._buf = np.zeros((capacity, channels), dtype=np.float32)
        self._capacity = capacity
        self._channels = channels
        self._written = 0
        self._lock = threading.Lock()

    @property
    def capacity(self) -> int:
        return self._capacity

    @property
    def channels(self) -> int:
        return self._channels

    @property
    def frames_written(self) -> int:
        """Total frames written since creation (not capped at capacity)."""
        return self._written

    def write(self, block: np.ndarray) -> None:
        """Append ``block`` of shape ``(n, channels)``. Oldest frames are overwritten."""
        if block.ndim != 2 or block.shape[1] != self._channels:
            raise ValueError(f"expected shape (n, {self._channels}), got {block.shape}")
        n = block.shape[0]
        with self._lock:
            if n > self._capacity:
                self._store(self._written + n - self._capacity, block[n - self._capacity :])
            else:
                self._store(self._written, block)
            self._written += n

    def read_latest(self, n: int, out: np.ndarray | None = None) -> tuple[np.ndarray, int]:
        """Copy the most recent ``n`` frames, oldest first.

        Returns ``(frames, end_frame)`` where ``end_frame`` is the index one past the last
        frame returned. Before ``n`` frames have been written, the missing leading frames are
        zeros. Pass ``out`` (shape ``(n, channels)``, float32) to avoid allocating.
        """
        self._check_n(n)
        out = self._out(n, out)
        with self._lock:
            end = self._written
            self._load(end - n, out)
        return out, end

    def read_from(self, start: int, n: int, out: np.ndarray | None = None) -> np.ndarray:
        """Copy frames ``start .. start + n - 1`` for consumers that must not skip frames.

        Raises ``ValueError`` if those frames are not written yet and ``OverrunError`` if
        they were already overwritten.
        """
        self._check_n(n)
        if start < 0:
            raise ValueError("start must be non-negative")
        out = self._out(n, out)
        with self._lock:
            if start + n > self._written:
                raise ValueError(f"frames up to {start + n} requested, {self._written} written")
            if start < self._written - self._capacity:
                raise OverrunError(f"frame {start} was overwritten")
            self._load(start, out)
        return out

    def _store(self, frame: int, src: np.ndarray) -> None:
        pos = frame % self._capacity
        first = min(len(src), self._capacity - pos)
        self._buf[pos : pos + first] = src[:first]
        self._buf[: len(src) - first] = src[first:]

    def _load(self, frame: int, out: np.ndarray) -> None:
        # A negative frame maps to rows never written yet (still zeros) because n <= capacity.
        pos = frame % self._capacity
        first = min(len(out), self._capacity - pos)
        out[:first] = self._buf[pos : pos + first]
        out[first:] = self._buf[: len(out) - first]

    def _check_n(self, n: int) -> None:
        if not 0 < n <= self._capacity:
            raise ValueError(f"n must be in 1..{self._capacity}, got {n}")

    def _out(self, n: int, out: np.ndarray | None) -> np.ndarray:
        if out is None:
            return np.empty((n, self._channels), dtype=np.float32)
        if out.shape != (n, self._channels):
            raise ValueError(f"out must have shape {(n, self._channels)}, got {out.shape}")
        return out
