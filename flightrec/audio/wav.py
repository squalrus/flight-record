"""WAV file helpers built on ``scipy.io.wavfile`` (no extra dependency)."""

from __future__ import annotations

from pathlib import Path
from typing import Literal

import numpy as np
from scipy.io import wavfile


def read_wav(path: str | Path) -> tuple[int, np.ndarray]:
    """Read a WAV file as ``(samplerate, frames)``; frames are float32 in [-1, 1], 2-D.

    Supports 8-bit unsigned, 16/24/32-bit integer, and float PCM. Mono files come back
    with shape ``(n, 1)``.
    """
    samplerate, data = wavfile.read(path)
    if data.dtype == np.uint8:
        frames = (data.astype(np.float32) - 128.0) / 128.0
    elif data.dtype == np.int16:
        frames = data.astype(np.float32) / 32768.0
    elif data.dtype == np.int32:
        # scipy left-justifies 24-bit samples into int32, so one scale covers both.
        frames = (data / 2147483648.0).astype(np.float32)
    elif np.issubdtype(data.dtype, np.floating):
        frames = data.astype(np.float32)
    else:
        raise ValueError(f"unsupported WAV sample type {data.dtype}")
    if frames.ndim == 1:
        frames = frames[:, np.newaxis]
    return int(samplerate), frames


def write_wav(
    path: str | Path,
    samplerate: int,
    frames: np.ndarray,
    sample_format: Literal["int16", "float32"] = "int16",
) -> None:
    """Write float frames (``(n,)`` or ``(n, channels)``, nominal range [-1, 1]) to WAV."""
    frames = np.asarray(frames, dtype=np.float32)
    if sample_format == "int16":
        data = np.round(np.clip(frames, -1.0, 1.0) * 32767.0).astype(np.int16)
    elif sample_format == "float32":
        data = frames
    else:
        raise ValueError(f"unsupported sample_format {sample_format!r}")
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    wavfile.write(path, samplerate, data)
