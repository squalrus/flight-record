"""Audio capture: sources, ring buffer, and the capture path shared by live and replay.

Importing this package does not import ``sounddevice``; only ``LiveSource.start`` does, so
replay and tests run on machines without PortAudio or an audio interface.
"""

from flightrec.audio.capture import Capture
from flightrec.audio.ring import OverrunError, RingBuffer
from flightrec.audio.sources import (
    ArraySource,
    AudioSource,
    BlockCallback,
    LiveSource,
    NullSource,
    WavSource,
    env_device,
    open_source,
)
from flightrec.audio.wav import read_wav, write_wav

__all__ = [
    "ArraySource",
    "AudioSource",
    "BlockCallback",
    "Capture",
    "LiveSource",
    "NullSource",
    "OverrunError",
    "RingBuffer",
    "WavSource",
    "env_device",
    "open_source",
    "read_wav",
    "write_wav",
]
