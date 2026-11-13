"""The message contract from docs/architecture.md: types, validation, JSON, and logs.

docs/architecture.md is authoritative; this module implements it. Every message carries
``v`` (schema version) and ``t`` (seconds on the sender's monotonic clock).

Rules:
- Unknown fields are accepted, never an error, and kept in ``extra`` so a log can be read
  and rewritten without losing anything. Adding a field is a compatible change.
- Changing or removing a field means bumping ``v`` (``SCHEMA_VERSION``) and updating the
  architecture doc in the same change. A message with a newer ``v`` than this module knows
  raises ``UnsupportedVersion``.
- An unknown ``type`` raises ``UnknownMessageType``; log readers can skip those instead.

Logs are JSON lines, one message per line, in the order they were sent. ``replay`` yields
them again at their original timing (optionally faster or slower).

Standard library only, so the Unity side can mirror it without porting dependencies.
"""

from __future__ import annotations

import dataclasses
import json
import math
import time
import types
import typing
from collections.abc import Callable, Iterable, Iterator
from dataclasses import dataclass, field
from pathlib import Path
from typing import IO, Any, ClassVar

SCHEMA_VERSION = 1


class ContractError(ValueError):
    """A message does not match the contract."""


class UnsupportedVersion(ContractError):
    """The message's ``v`` is newer than this code understands."""


class UnknownMessageType(ContractError):
    """The message's ``type`` is not part of the contract."""


def _range(lo: float | None = None, hi: float | None = None) -> dict[str, Any]:
    return {"range": (lo, hi)}


@dataclass(frozen=True)
class Message:
    """Base for every message. Subclasses set ``TYPE`` and add their fields."""

    TYPE: ClassVar[str] = ""

    t: float
    """Seconds on the sender's monotonic clock."""
    extra: dict[str, Any] = field(default_factory=dict, kw_only=True, compare=False, repr=False)
    """Fields this version of the contract does not define, kept as received."""

    def to_dict(self) -> dict[str, Any]:
        out: dict[str, Any] = {"v": SCHEMA_VERSION, "type": self.TYPE}
        for f in dataclasses.fields(self):
            if f.name == "extra":
                continue
            value = getattr(self, f.name)
            if isinstance(value, list):
                value = [v.to_dict() if isinstance(v, Track) else v for v in value]
            out[f.name] = value
        for key, value in self.extra.items():
            out.setdefault(key, value)
        return out

    def to_json(self) -> str:
        return json.dumps(self.to_dict(), separators=(",", ":"), allow_nan=False)


@dataclass(frozen=True)
class Track:
    """One track on a side, from Discogs. ``seconds`` is None when Discogs has no duration."""

    pos: str
    title: str
    seconds: float | None = None

    def to_dict(self) -> dict[str, Any]:
        return {"pos": self.pos, "title": self.title, "seconds": self.seconds}


@dataclass(frozen=True)
class FeaturesMessage(Message):
    """Real-time audio features, about 60 Hz (one per analysis hop in practice)."""

    TYPE: ClassVar[str] = "features"

    onset: bool
    onset_strength: float = field(metadata=_range(0, 1))
    bass: float = field(metadata=_range(0, 1))
    mid: float = field(metadata=_range(0, 1))
    treble: float = field(metadata=_range(0, 1))
    rms: float = field(metadata=_range(0, 1))
    tempo: float | None = field(default=None, metadata=_range(0, None))
    tempo_confidence: float = field(default=0.0, metadata=_range(0, 1))
    # Optional additions (v1, added by experiment 02; consumers may ignore them).
    onset_low: bool | None = None
    onset_mid: bool | None = None
    onset_high: bool | None = None
    beat: bool | None = None
    centroid_hz: float | None = field(default=None, metadata=_range(0, None))
    key: str | None = None
    key_confidence: float | None = field(default=None, metadata=_range(0, 1))
    chroma: list[float] | None = field(default=None, metadata={"length": 12, "range": (0, 1)})


@dataclass(frozen=True)
class TrackIdentified(Message):
    """A fingerprint match for what is playing."""

    TYPE: ClassVar[str] = "track_identified"

    artist: str
    title: str
    album: str | None = None
    confidence: float = field(default=0.0, metadata=_range(0, 1))


@dataclass(frozen=True)
class ReleaseSelected(Message):
    """The pressing chosen on Discogs and the tracks on the side being played."""

    TYPE: ClassVar[str] = "release_selected"

    discogs_id: int = field(metadata=_range(0, None))
    side: str
    tracks: list[Track] = field(default_factory=list)


@dataclass(frozen=True)
class IdentificationFailed(Message):
    """Identification gave up for now; the game carries on without it (D-004)."""

    TYPE: ClassVar[str] = "identification_failed"

    reason: str
    """E.g. "no_match", "timeout", "network", "no_api_key"."""


@dataclass(frozen=True)
class SideState(Message):
    """Where playback is on the current side."""

    TYPE: ClassVar[str] = "side_state"

    side: str
    track_index: int = field(metadata=_range(0, None))
    """0-based index into the side's tracks (``ReleaseSelected.tracks``)."""
    side_elapsed: float = field(metadata=_range(0, None))
    side_expected: float | None = field(default=None, metadata=_range(0, None))
    """Expected side length in seconds, or None when unknown."""
    side_confidence: float = field(default=0.0, metadata=_range(0, 1))


@dataclass(frozen=True)
class SideEnd(Message):
    """The side is over: time for the boss and the end of the level."""

    TYPE: ClassVar[str] = "side_end"

    cause: str
    """E.g. "run_out_groove", "needle_lift", "timer", "file_end"."""


MESSAGE_TYPES: dict[str, type[Message]] = {
    cls.TYPE: cls
    for cls in (
        FeaturesMessage,
        TrackIdentified,
        ReleaseSelected,
        IdentificationFailed,
        SideState,
        SideEnd,
    )
}


# Parsing and validation


def from_dict(data: dict[str, Any]) -> Message:
    """Validate a decoded message and build its class. Unknown fields go to ``extra``."""
    if not isinstance(data, dict):
        raise ContractError(f"message must be an object, got {type(data).__name__}")
    v = data.get("v")
    if not isinstance(v, int) or isinstance(v, bool) or v < 1:
        raise ContractError(f"missing or invalid 'v': {v!r}")
    if v > SCHEMA_VERSION:
        raise UnsupportedVersion(f"schema v{v} is newer than supported v{SCHEMA_VERSION}")
    kind = data.get("type")
    cls = MESSAGE_TYPES.get(kind)  # type: ignore[arg-type]
    if cls is None:
        raise UnknownMessageType(f"unknown message type {kind!r}")

    hints = typing.get_type_hints(cls)
    known = {f.name: f for f in dataclasses.fields(cls) if f.name != "extra"}
    kwargs: dict[str, Any] = {}
    for name, f in known.items():
        if name not in data:
            if f.default is dataclasses.MISSING and f.default_factory is dataclasses.MISSING:
                raise ContractError(f"{kind}: missing required field '{name}'")
            continue
        kwargs[name] = _check(kind, name, data[name], hints[name], f.metadata)
    extra = {k: val for k, val in data.items() if k not in known and k not in ("v", "type")}
    return cls(**kwargs, extra=extra)


def _check(kind: str, name: str, value: Any, hint: Any, meta: Any) -> Any:
    where = f"{kind}.{name}"
    args = typing.get_args(hint)
    origin = typing.get_origin(hint)
    if origin in (types.UnionType, typing.Union):  # every union here is "X | None"
        if value is None:
            return None
        (hint,) = [a for a in args if a is not type(None)]
        args = typing.get_args(hint)
        origin = typing.get_origin(hint)
    if origin is list:
        if not isinstance(value, list):
            raise ContractError(f"{where} must be a list")
        length = meta.get("length")
        if length is not None and len(value) != length:
            raise ContractError(f"{where} must have {length} items, got {len(value)}")
        (item,) = args
        if item is Track:
            return [_track(where, i, x) for i, x in enumerate(value)]
        return [_scalar(f"{where}[{i}]", x, item, meta) for i, x in enumerate(value)]
    return _scalar(where, value, hint, meta)


def _scalar(where: str, value: Any, hint: Any, meta: Any) -> Any:
    if hint is bool:
        if not isinstance(value, bool):
            raise ContractError(f"{where} must be true or false, got {value!r}")
        return value
    if hint is int:
        if not isinstance(value, int) or isinstance(value, bool):
            raise ContractError(f"{where} must be an integer, got {value!r}")
    elif hint is float:
        if not isinstance(value, int | float) or isinstance(value, bool):
            raise ContractError(f"{where} must be a number, got {value!r}")
        if not math.isfinite(value):
            raise ContractError(f"{where} must be finite, got {value!r}")
        value = float(value)
    elif hint is str:
        if not isinstance(value, str):
            raise ContractError(f"{where} must be a string, got {value!r}")
        return value
    lo, hi = meta.get("range", (None, None))
    if (lo is not None and value < lo) or (hi is not None and value > hi):
        raise ContractError(f"{where} = {value} is outside {lo}..{hi}")
    return value


def _track(where: str, i: int, value: Any) -> Track:
    if not isinstance(value, dict):
        raise ContractError(f"{where}[{i}] must be an object")
    for key in ("pos", "title"):
        if not isinstance(value.get(key), str):
            raise ContractError(f"{where}[{i}].{key} must be a string")
    seconds = value.get("seconds")
    if seconds is not None:
        seconds = _scalar(f"{where}[{i}].seconds", seconds, float, {"range": (0, None)})
    return Track(value["pos"], value["title"], seconds)


def loads(text: str) -> Message:
    """Parse one JSON message."""
    try:
        data = json.loads(text)
    except json.JSONDecodeError as exc:
        raise ContractError(f"not valid JSON: {exc}") from exc
    return from_dict(data)


def dumps(message: Message) -> str:
    """Serialize one message to compact JSON (no NaN or infinity)."""
    return message.to_json()


# Logs and replay


class LogWriter:
    """Append messages to a JSON-lines log, flushing each line so a crash loses nothing."""

    def __init__(self, path: str | Path) -> None:
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._fh: IO[str] = self.path.open("w", encoding="utf-8", newline="\n")
        self.count = 0

    def write(self, message: Message) -> None:
        self._fh.write(message.to_json() + "\n")
        self._fh.flush()
        self.count += 1

    def close(self) -> None:
        self._fh.close()

    def __enter__(self) -> LogWriter:
        return self

    def __exit__(self, *exc: object) -> None:
        self.close()


def read_log(path: str | Path, skip_unknown: bool = True) -> Iterator[Message]:
    """Yield the messages in a JSON-lines log, in order.

    Blank lines are ignored. With ``skip_unknown`` (the default), message types this
    version does not know are skipped instead of raising, so newer logs still replay.
    Other contract errors raise with the line number.
    """
    with Path(path).open(encoding="utf-8") as fh:
        for number, line in enumerate(fh, 1):
            if not line.strip():
                continue
            try:
                yield loads(line)
            except UnknownMessageType:
                if not skip_unknown:
                    raise
            except ContractError as exc:
                raise ContractError(f"{path}:{number}: {exc}") from exc


def replay(
    messages: Iterable[Message],
    speed: float = 1.0,
    clock: Callable[[], float] = time.perf_counter,
    sleep: Callable[[float], None] = time.sleep,
) -> Iterator[Message]:
    """Yield messages at their original timing, relative to the first message's ``t``.

    ``speed`` 2.0 plays twice as fast. A message whose ``t`` goes backwards (e.g. two
    senders' clocks) is yielded immediately. ``clock`` and ``sleep`` are injectable so
    tests can check timing without waiting.
    """
    if speed <= 0:
        raise ValueError("speed must be positive")
    start_wall: float | None = None
    start_t = 0.0
    for message in messages:
        if start_wall is None:
            start_wall, start_t = clock(), message.t
        else:
            due = start_wall + (message.t - start_t) / speed
            while (delay := due - clock()) > 0:
                sleep(delay)
        yield message
