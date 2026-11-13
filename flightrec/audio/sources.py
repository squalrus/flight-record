"""Audio sources. Live, replay, and null sources deliver blocks the same way.

Every source calls ``callback(block, start_frame, t)`` from its own thread:

- ``block``: float32 array of shape ``(blocksize, channels)``. It is only valid during the
  call (sources reuse the buffer), so copy what you keep, e.g. into a ``RingBuffer``.
- ``start_frame``: index of the block's first frame since ``start()``.
- ``t``: ``time.perf_counter()`` when the block was delivered. It is monotonic like
  ``time.monotonic()`` but high-resolution everywhere; ``time.monotonic()`` ticks only
  every ~15.6 ms on Windows before Python 3.13, which is too coarse for audio timing.
  Compare ``t`` only with other ``perf_counter()`` readings.

Replay paces blocks like an audio interface: block ``k`` arrives once its last frame
"has been captured", ``(k + 1) * blocksize / samplerate`` seconds after start.
"""

from __future__ import annotations

import os
import threading
import time
from abc import ABC, abstractmethod
from collections.abc import Callable
from pathlib import Path
from typing import Any

import numpy as np

from flightrec.audio.wav import read_audio

BlockCallback = Callable[[np.ndarray, int, float], None]

DEFAULT_SAMPLERATE = 48_000
DEFAULT_CHANNELS = 2
DEFAULT_BLOCKSIZE = 512


class AudioSource(ABC):
    """Base class for sources. Use as a context manager to guarantee ``stop()``."""

    samplerate: int
    channels: int
    blocksize: int

    @abstractmethod
    def start(self, callback: BlockCallback) -> None:
        """Begin delivering blocks to ``callback``."""

    @abstractmethod
    def stop(self) -> None:
        """Stop delivering blocks. Safe to call more than once."""

    @property
    def finished(self) -> bool:
        """True once a finite source has delivered its last block (live never finishes)."""
        return False

    @property
    def input_latency(self) -> float:
        """Seconds between sound reaching the input and its block being delivered, as
        reported by the driver. Zero for replay and null sources."""
        return 0.0

    @property
    def overflows(self) -> int:
        """Callbacks that reported dropped input audio. Always zero for non-live sources."""
        return 0

    @property
    def output_latency(self) -> float:
        """Seconds between a block being delivered and it being heard. Non-zero only for
        ``PlaybackSource``; displays delay by this much to stay in sync with the sound."""
        return 0.0

    def wait(self, timeout: float | None = None) -> bool:
        """Block until the source finishes or ``timeout`` passes; True if finished."""
        if timeout is not None:
            time.sleep(timeout)
        return self.finished

    def __enter__(self) -> AudioSource:
        return self

    def __exit__(self, *exc: object) -> None:
        self.stop()


class LiveSource(AudioSource):
    """Capture from an input device with ``sounddevice`` (needs PortAudio and hardware)."""

    def __init__(
        self,
        device: int | str | None = None,
        samplerate: int = DEFAULT_SAMPLERATE,
        channels: int = DEFAULT_CHANNELS,
        blocksize: int = DEFAULT_BLOCKSIZE,
        latency: str | float = "low",
    ) -> None:
        self.device = device
        self.samplerate = samplerate
        self.channels = channels
        self.blocksize = blocksize
        self.latency = latency
        self._overflows = 0
        self._input_latency = 0.0
        self._stream: Any = None
        self._callback: BlockCallback | None = None
        self._frame = 0

    def start(self, callback: BlockCallback) -> None:
        import sounddevice as sd  # deferred: needs PortAudio, which replay/tests do not

        self._callback = callback
        self._frame = 0
        self._stream = sd.InputStream(
            device=self.device,
            samplerate=self.samplerate,
            blocksize=self.blocksize,
            channels=self.channels,
            dtype="float32",
            latency=self.latency,
            callback=self._on_audio,
        )
        self._stream.start()
        self._input_latency = float(self._stream.latency)

    @property
    def input_latency(self) -> float:
        return self._input_latency

    @property
    def overflows(self) -> int:
        return self._overflows

    def _on_audio(self, indata: np.ndarray, frames: int, time_info: Any, status: Any) -> None:
        if status.input_overflow:
            self._overflows += 1
        assert self._callback is not None
        self._callback(indata, self._frame, time.perf_counter())
        self._frame += frames

    def stop(self) -> None:
        if self._stream is not None:
            self._stream.stop()
            self._stream.close()
            self._stream = None


class ArraySource(AudioSource):
    """Deliver an in-memory array of frames in blocks, paced in real time or as fast as possible.

    The last partial block is zero-padded so every block has ``blocksize`` frames, as from
    an audio interface. With ``loop=True`` playback wraps around and never finishes.
    """

    def __init__(
        self,
        frames: np.ndarray,
        samplerate: int,
        blocksize: int = DEFAULT_BLOCKSIZE,
        channels: int | None = None,
        realtime: bool = True,
        loop: bool = False,
    ) -> None:
        frames = np.asarray(frames, dtype=np.float32)
        if frames.ndim == 1:
            frames = frames[:, np.newaxis]
        if len(frames) == 0:
            raise ValueError("no frames to play")
        self.frames = _match_channels(frames, channels)
        self.samplerate = samplerate
        self.channels = self.frames.shape[1]
        self.blocksize = blocksize
        self.realtime = realtime
        self.loop = loop
        self.error: BaseException | None = None
        """Exception raised by the callback, if any; it also stops the source."""
        self._block = np.zeros((blocksize, self.channels), dtype=np.float32)
        self._stop = threading.Event()
        self._done = threading.Event()
        self._thread: threading.Thread | None = None

    @property
    def duration(self) -> float:
        """Length of the array in seconds."""
        return len(self.frames) / self.samplerate

    @property
    def finished(self) -> bool:
        return self._done.is_set()

    def start(self, callback: BlockCallback) -> None:
        if self._thread is not None:
            raise RuntimeError("source already started")
        self._thread = threading.Thread(
            target=self._run, args=(callback,), name=type(self).__name__, daemon=True
        )
        self._thread.start()

    def wait(self, timeout: float | None = None) -> bool:
        done = self._done.wait(timeout)
        if self.error is not None:
            raise self.error
        return done

    def stop(self) -> None:
        self._stop.set()
        thread = self._thread
        if thread is not None and thread is not threading.current_thread():
            thread.join()

    def _run(self, callback: BlockCallback) -> None:
        total = len(self.frames)
        block_seconds = self.blocksize / self.samplerate
        t0 = time.perf_counter()
        k = 0
        try:
            while not self._stop.is_set():
                start = k * self.blocksize
                if not self.loop and start >= total:
                    break
                self._fill(start % total)
                if self.realtime and self._sleep_until(t0 + (k + 1) * block_seconds):
                    break
                callback(self._block, start, time.perf_counter())
                k += 1
        except BaseException as exc:  # surfaced to the caller through wait()
            self.error = exc
        finally:
            self._done.set()

    def _sleep_until(self, due: float) -> bool:
        """Sleep until ``perf_counter() >= due``; True if stopped first.

        Loops because OS waits can return slightly early (coarse timers on Windows).
        """
        while (delay := due - time.perf_counter()) > 0:
            if self._stop.wait(delay):
                return True
        return False

    def _fill(self, pos: int) -> None:
        """Copy ``blocksize`` frames from ``pos`` into the reusable block buffer."""
        total = len(self.frames)
        filled = 0
        while filled < self.blocksize:
            take = min(self.blocksize - filled, total - pos)
            self._block[filled : filled + take] = self.frames[pos : pos + take]
            filled += take
            pos = 0
            if not self.loop:
                self._block[filled:] = 0.0
                break


class WavSource(ArraySource):
    """Replay an audio file through the capture path (no audio is played out loud).

    Despite the name, any format ``read_audio`` supports works: WAV, MP3, FLAC, OGG. The
    whole file is decoded into memory first (a 7-minute stereo MP3 is about 140 MB).
    """

    def __init__(
        self,
        path: str | Path,
        blocksize: int = DEFAULT_BLOCKSIZE,
        channels: int | None = None,
        realtime: bool = True,
        loop: bool = False,
    ) -> None:
        samplerate, frames = read_audio(path)
        self.path = Path(path)
        super().__init__(frames, samplerate, blocksize, channels, realtime, loop)


class PlaybackSource(WavSource):
    """Replay a file *and* play it through the speakers, for exploring recordings by ear.

    A development-only exception to "the audible path never touches the computer"
    (decision D-011): it only ever plays files, never live input. The output stream's
    callback drives delivery, so every block reaches the speakers and the capture path at
    the same moment on the sound card's clock, and the two cannot drift apart. The sound
    is heard ``output_latency`` later, so displays should lag by that much.
    """

    def __init__(
        self,
        path: str | Path,
        blocksize: int = DEFAULT_BLOCKSIZE,
        channels: int | None = None,
        loop: bool = False,
        device: int | str | None = None,
    ) -> None:
        super().__init__(path, blocksize, channels, realtime=True, loop=loop)
        self.device = device
        self._stream: Any = None
        self._out_callback: BlockCallback | None = None
        self._pos = 0
        self._output_latency = 0.0
        self._stop_exc: type[BaseException] = StopIteration

    @property
    def output_latency(self) -> float:
        return self._output_latency

    def start(self, callback: BlockCallback) -> None:
        import sounddevice as sd  # deferred: needs PortAudio

        if self._stream is not None:
            raise RuntimeError("source already started")
        self._out_callback = callback
        self._stop_exc = sd.CallbackStop
        self._stream = sd.OutputStream(
            device=self.device,
            samplerate=self.samplerate,
            blocksize=self.blocksize,
            channels=self.channels,
            dtype="float32",
            latency="low",  # the default ("high") is ~200 ms on Windows
            callback=self._on_output,
            finished_callback=self._done.set,
        )
        self._stream.start()
        self._output_latency = float(self._stream.latency)

    def _on_output(self, outdata: np.ndarray, frames: int, time_info: Any, status: Any) -> None:
        total = len(self.frames)
        if not self.loop and self._pos >= total:
            outdata.fill(0.0)
            raise self._stop_exc
        try:
            self._fill(self._pos % total)
            outdata[:] = self._block
            assert self._out_callback is not None
            self._out_callback(self._block, self._pos, time.perf_counter())
        except BaseException as exc:
            self.error = exc
            outdata.fill(0.0)
            self._done.set()
            raise self._stop_exc from exc
        self._pos += self.blocksize

    def stop(self) -> None:
        if self._stream is not None:
            self._stream.stop()
            self._stream.close()
            self._stream = None
        self._done.set()


class NullSource(ArraySource):
    """Endless silence at real-time pace, for running consumers with no audio at all."""

    def __init__(
        self,
        samplerate: int = DEFAULT_SAMPLERATE,
        channels: int = DEFAULT_CHANNELS,
        blocksize: int = DEFAULT_BLOCKSIZE,
        realtime: bool = True,
    ) -> None:
        silence = np.zeros((blocksize, channels), dtype=np.float32)
        super().__init__(silence, samplerate, blocksize, None, realtime, loop=True)


def env_device() -> int | str | None:
    """Input device from ``FR_AUDIO_DEVICE`` (index or name), or None for the default."""
    value = os.environ.get("FR_AUDIO_DEVICE", "").strip()
    if not value:
        return None
    return int(value) if value.isdigit() else value


def open_source(
    wav: str | Path | None = None,
    *,
    device: int | str | None = None,
    samplerate: int = DEFAULT_SAMPLERATE,
    channels: int = DEFAULT_CHANNELS,
    blocksize: int = DEFAULT_BLOCKSIZE,
    realtime: bool = True,
    loop: bool = False,
    listen: bool = False,
) -> AudioSource:
    """Replay ``wav`` if given, otherwise capture live. The one switch experiments need.

    For replay, ``channels`` adapts the file (mono is duplicated, stereo is averaged down)
    and ``samplerate`` is ignored: the file's own rate is used. ``listen`` also plays the
    file through the speakers (``PlaybackSource``); it is refused for live input, whose
    audible path must stay in hardware.
    """
    if listen:
        if wav is None:
            raise ValueError("listen only works when replaying a file, never with live input")
        return PlaybackSource(wav, blocksize, channels, loop)
    if wav is not None:
        return WavSource(wav, blocksize, channels, realtime, loop)
    return LiveSource(
        device if device is not None else env_device(), samplerate, channels, blocksize
    )


def _match_channels(frames: np.ndarray, channels: int | None) -> np.ndarray:
    have = frames.shape[1]
    if channels is None or channels == have:
        return frames
    if have == 1:
        # Mono sources arrive on both channels of a stereo interface.
        return np.repeat(frames, channels, axis=1)
    if channels == 1:
        return frames.mean(axis=1, keepdims=True)
    raise ValueError(f"cannot map {have} channels to {channels}")
