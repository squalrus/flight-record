"""Audio capture: sources, ring buffer, and the capture path shared by live and replay.

Importing this package does not import ``sounddevice``; only ``LiveSource.start`` does, so
replay and tests run on machines without PortAudio or an audio interface.
"""

from flightrec.audio.capture import Capture
from flightrec.audio.features import (
    BAND_NAMES,
    DEFAULT_GATE_DB,
    FeatureExtractor,
    Features,
    HopReader,
    extract,
)
from flightrec.audio.ring import OverrunError, RingBuffer
from flightrec.audio.sources import (
    ArraySource,
    AudioSource,
    BlockCallback,
    LiveSource,
    NullSource,
    PlaybackSource,
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
from flightrec.audio.wav import read_audio, read_wav, write_wav

__all__ = [
    "BAND_NAMES",
    "DEFAULT_GATE_DB",
    "FeatureExtractor",
    "Features",
    "HopReader",
    "extract",
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
    "PlaybackSource",
    "RingBuffer",
    "WavSource",
    "env_device",
    "open_source",
    "read_audio",
    "read_wav",
    "write_wav",
]
