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
from flightrec.audio.spectrum import (
    FLOOR_DB,
    LogBands,
    Spectrum,
    channel_levels,
    level_dbfs,
    to_db,
)
from flightrec.audio.wav import read_wav, write_wav

__all__ = [
    "FLOOR_DB",
    "ArraySource",
    "AudioSource",
    "BlockCallback",
    "Capture",
    "LogBands",
    "Spectrum",
    "channel_levels",
    "level_dbfs",
    "to_db",
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
