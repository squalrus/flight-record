"""Audio recognition clients (architecture module 2): send a clip, get artist / title.

Two services, both over plain HTTPS with the standard library (no SDK dependency):

- ``AudD``: one API token (``AUDD_API_TOKEN``); POST the clip to https://api.audd.io/.
- ``ACRCloud``: identification project host, access key, and secret (``ACRCLOUD_HOST``,
  ``ACRCLOUD_ACCESS_KEY``, ``ACRCLOUD_ACCESS_SECRET``); HMAC-SHA1 signed POST to
  ``https://HOST/v1/identify``.

Response parsing follows each service's documented response format; keep raw responses
(``Result.raw``) so parsing can be checked against real answers. Identification is an
optional enhancement (decision D-004): every caller must handle "no match" and errors.
Keys come from the environment (see ``flightrec.env``); never log them.

``Identifier`` runs a service periodically on a background thread so live displays and
the game never wait on the network, and turns each attempt into a contract message.
"""

from __future__ import annotations

import base64
import hashlib
import hmac
import json
import os
import queue
import threading
import time
import urllib.error
import urllib.request
import uuid
from collections.abc import Callable
from dataclasses import dataclass, field
from typing import Any

from flightrec.messages import IdentificationFailed, Message, TrackIdentified


class IdentificationError(RuntimeError):
    """The service could not be asked (network, auth, quota) as opposed to "no match"."""


@dataclass(frozen=True)
class Result:
    """One identification attempt."""

    service: str
    matched: bool
    artist: str | None = None
    title: str | None = None
    album: str | None = None
    score: float | None = None
    """Service confidence on a 0..1 scale, if it reports one."""
    duration_s: float | None = None
    """Length of the identified track (ACRCloud ``duration_ms``)."""
    offset_s: float | None = None
    """Where in the track the clip ended (ACRCloud ``play_offset_ms``, AudD ``timecode``)."""
    recording_id: str | None = None
    """The service's stable id for the recording (ACRCloud ``acrid``): a track change shows
    up as a different id, with no fuzzy title matching."""
    label: str | None = None
    genres: list[str] = field(default_factory=list)
    release_date: str | None = None
    isrc: str | None = None
    upc: str | None = None
    """Barcode of the release the service matched, when its project has external ids on."""
    alt_albums: list[str] = field(default_factory=list)
    """Albums of the service's other (lower-ranked) matches: more names to try on Discogs."""
    link: str | None = None
    """A shareable "listen on..." link (AudD ``song_link``)."""
    latency_s: float = 0.0
    error: str | None = None
    raw: dict[str, Any] = field(default_factory=dict, repr=False)


def multipart(
    fields: dict[str, str], files: dict[str, tuple[str, bytes, str]]
) -> tuple[bytes, str]:
    """Encode a multipart/form-data body; returns (body, content type)."""
    boundary = uuid.uuid4().hex
    parts: list[bytes] = []
    for name, value in fields.items():
        parts.append(
            f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n'
            f"{value}\r\n".encode()
        )
    for name, (filename, data, content_type) in files.items():
        parts.append(
            f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"; '
            f'filename="{filename}"\r\nContent-Type: {content_type}\r\n\r\n'.encode()
            + data
            + b"\r\n"
        )
    parts.append(f"--{boundary}--\r\n".encode())
    return b"".join(parts), f"multipart/form-data; boundary={boundary}"


def _post(url: str, body: bytes, content_type: str, timeout: float) -> dict[str, Any]:
    request = urllib.request.Request(
        url, data=body, method="POST", headers={"Content-Type": content_type}
    )
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        raise IdentificationError(f"HTTP {exc.code} from {url.split('?')[0]}") from exc
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
        raise IdentificationError(f"{type(exc).__name__}: {exc}") from exc


class Service:
    """Base: ``identify(wav_bytes)`` returns a ``Result`` and never raises for no-match."""

    name = ""

    def __init__(self, timeout: float = 20.0) -> None:
        self.timeout = timeout

    @classmethod
    def from_env(cls) -> Service | None:
        """An instance if its keys are set in the environment, else None."""
        raise NotImplementedError

    def identify(self, wav: bytes) -> Result:
        start = time.perf_counter()
        try:
            raw = self._send(wav)
        except IdentificationError as exc:
            return Result(self.name, False, error=str(exc), latency_s=time.perf_counter() - start)
        result = self.parse(raw)
        return Result(**{**result.__dict__, "latency_s": time.perf_counter() - start})

    def _send(self, wav: bytes) -> dict[str, Any]:
        raise NotImplementedError

    def parse(self, raw: dict[str, Any]) -> Result:
        raise NotImplementedError


class AudD(Service):
    name = "audd"
    url = "https://api.audd.io/"

    def __init__(self, api_token: str, timeout: float = 20.0) -> None:
        super().__init__(timeout)
        self.api_token = api_token

    @classmethod
    def from_env(cls) -> AudD | None:
        token = os.environ.get("AUDD_API_TOKEN", "").strip()
        return cls(token) if token else None

    def _send(self, wav: bytes) -> dict[str, Any]:
        body, content_type = multipart(
            {"api_token": self.api_token}, {"file": ("clip.wav", wav, "audio/wav")}
        )
        return _post(self.url, body, content_type, self.timeout)

    def parse(self, raw: dict[str, Any]) -> Result:
        """``{"status": "success", "result": {artist, title, album, ...} | null}`` or
        ``{"status": "error", "error": {"error_code", "error_message"}}``."""
        if raw.get("status") != "success":
            err = raw.get("error") or {}
            message = f"{err.get('error_code', '?')}: {err.get('error_message', 'unknown error')}"
            return Result(self.name, False, error=message, raw=raw)
        found = raw.get("result")
        if not found:
            return Result(self.name, False, raw=raw)
        return Result(
            self.name,
            True,
            artist=found.get("artist"),
            title=found.get("title"),
            album=found.get("album"),
            offset_s=_timecode(found.get("timecode")),
            label=found.get("label") or None,
            release_date=found.get("release_date") or None,
            link=found.get("song_link") or None,
            raw=raw,
        )


def _timecode(text: str | None) -> float | None:
    """AudD's position in the track, "mm:ss" or "hh:mm:ss", in seconds."""
    parts = (text or "").split(":")
    if not text or not all(p.isdigit() for p in parts) or len(parts) > 3:
        return None
    seconds = 0
    for part in parts:
        seconds = seconds * 60 + int(part)
    return float(seconds)


class ACRCloud(Service):
    name = "acrcloud"
    uri = "/v1/identify"

    def __init__(self, host: str, access_key: str, access_secret: str, timeout: float = 20.0):
        super().__init__(timeout)
        self.host = host.removeprefix("https://").removeprefix("http://").rstrip("/")
        self.access_key = access_key
        self.access_secret = access_secret

    @classmethod
    def from_env(cls) -> ACRCloud | None:
        values = [
            os.environ.get(k, "").strip()
            for k in ("ACRCLOUD_HOST", "ACRCLOUD_ACCESS_KEY", "ACRCLOUD_ACCESS_SECRET")
        ]
        return cls(*values) if all(values) else None

    def signature(self, timestamp: str) -> str:
        """Base64 HMAC-SHA1 over the documented string-to-sign."""
        to_sign = "\n".join(["POST", self.uri, self.access_key, "audio", "1", timestamp])
        digest = hmac.new(self.access_secret.encode(), to_sign.encode(), hashlib.sha1).digest()
        return base64.b64encode(digest).decode("ascii")

    def _send(self, wav: bytes) -> dict[str, Any]:
        timestamp = str(int(time.time()))
        body, content_type = multipart(
            {
                "access_key": self.access_key,
                "sample_bytes": str(len(wav)),
                "timestamp": timestamp,
                "signature": self.signature(timestamp),
                "data_type": "audio",
                "signature_version": "1",
            },
            {"sample": ("clip.wav", wav, "audio/wav")},
        )
        return _post(f"https://{self.host}{self.uri}", body, content_type, self.timeout)

    def parse(self, raw: dict[str, Any]) -> Result:
        """``{"status": {"code": 0 | 1001 | ..., "msg"}, "metadata": {"music": [{title,
        artists: [{name}], album: {name}, score (0..100)}]}}``; code 1001 is no match."""
        status = raw.get("status") or {}
        code = status.get("code")
        if code == 1001:
            return Result(self.name, False, raw=raw)
        if code != 0:
            return Result(self.name, False, error=f"{code}: {status.get('msg')}", raw=raw)
        music = (raw.get("metadata") or {}).get("music") or []
        if not music:
            return Result(self.name, False, raw=raw)
        top = music[0]
        artists = ", ".join(a.get("name", "") for a in top.get("artists") or []) or None
        score = top.get("score")
        duration_ms, offset_ms = top.get("duration_ms"), top.get("play_offset_ms")
        album = (top.get("album") or {}).get("name")
        ids = top.get("external_ids") or {}
        alt_albums = []
        for other in music[1:]:
            name = (other.get("album") or {}).get("name")
            if name and name != album and name not in alt_albums:
                alt_albums.append(name)
        return Result(
            self.name,
            True,
            artist=artists,
            title=top.get("title"),
            album=album,
            score=None if score is None else float(score) / 100.0,
            duration_s=float(duration_ms) / 1000.0 if duration_ms else None,
            offset_s=float(offset_ms) / 1000.0 if offset_ms is not None else None,
            recording_id=top.get("acrid") or None,
            label=top.get("label") or None,
            genres=[g["name"] for g in top.get("genres") or [] if g.get("name")],
            release_date=top.get("release_date") or None,
            isrc=_first(ids.get("isrc")),
            upc=_first(ids.get("upc")),
            alt_albums=alt_albums,
            raw=raw,
        )


def _first(value: Any) -> str | None:
    """ACRCloud external ids are a string or a list of strings."""
    if isinstance(value, list):
        value = value[0] if value else None
    return str(value) if value else None


SERVICES: dict[str, type[Service]] = {"audd": AudD, "acrcloud": ACRCloud}


def encode_wav(audio: Any, samplerate: int) -> bytes:
    """Mono 16-bit WAV bytes for a clip (``audio``: float array, 1-D or (n, channels))."""
    import io

    import numpy as np
    import soundfile

    data = np.asarray(audio, dtype=np.float32)
    if data.ndim == 2:
        data = data.mean(axis=1)
    buffer = io.BytesIO()
    soundfile.write(buffer, np.clip(data, -1.0, 1.0), samplerate, format="WAV", subtype="PCM_16")
    return buffer.getvalue()


@dataclass(frozen=True)
class Attempt:
    """One background identification: the clip ended at audio time ``clip_end_t``, the
    answer arrived at ``t`` (also audio time), and ``result`` is what the service said."""

    clip_end_t: float
    t: float
    result: Result

    def to_message(self) -> Message:
        """The contract message for this attempt: ``track_identified`` or
        ``identification_failed`` (reason ``no_match`` or ``error``)."""
        r = self.result
        extra = {
            "service": r.service,
            "latency_s": round(r.latency_s, 3),
            "clip_end_t": self.clip_end_t,
        }
        if r.matched:
            # AudD reports no score; its matches are logged at confidence 1.0.
            confidence = 1.0 if r.score is None else min(1.0, max(0.0, r.score))
            if r.link:
                extra["link"] = r.link
            return TrackIdentified(
                t=self.t, artist=r.artist or "", title=r.title or "", album=r.album,
                confidence=confidence, track_seconds=r.duration_s, offset_seconds=r.offset_s,
                recording_id=r.recording_id, label=r.label, genres=r.genres or None,
                release_date=r.release_date, isrc=r.isrc, upc=r.upc,
                alt_albums=r.alt_albums or None, extra=extra,
            )  # fmt: skip
        if r.error:
            return IdentificationFailed(t=self.t, reason="error", extra={**extra, "error": r.error})
        return IdentificationFailed(t=self.t, reason="no_match", extra=extra)


class Identifier:
    """Identify what is playing, periodically, without ever blocking the caller.

    Call ``update(audio_t)`` from the analysis or display loop with the current audio time
    (seconds of audio captured). When an attempt is due and none is running, it takes the
    last ``clip_s`` seconds from ``get_clip(seconds)`` (returns ``(samplerate, audio)``),
    sends them to ``service`` on a background thread, and returns finished ``Attempt`` s
    on later calls. Scheduling uses audio time, so replays behave like live input:

    - until something matches: retry ``retry_s`` seconds of audio after each answer;
    - after a match: re-check ``recheck_s`` seconds later, to notice track changes;
    - after ``max_requests`` attempts: stop (``exhausted``), to protect paid quotas.

    Intervals count from when the previous answer arrived, so a slow service is never
    asked again before it has answered.
    """

    def __init__(
        self,
        service: Service,
        get_clip: Callable[[float], tuple[int, Any]],
        clip_s: float = 10.0,
        retry_s: float = 8.0,
        recheck_s: float = 30.0,
        max_requests: int = 20,
    ) -> None:
        self.service = service
        self.get_clip = get_clip
        self.clip_s = clip_s
        self.retry_s = retry_s
        self.recheck_s = recheck_s
        self.max_requests = max_requests
        self.requests = 0
        self.next_due = clip_s
        self.history: list[Attempt] = []
        self.current: Attempt | None = None
        """The latest successful match, if any."""
        self._busy = False
        self._busy_since = 0.0
        self._done: queue.Queue[tuple[float, Result]] = queue.Queue()
        self._thread: threading.Thread | None = None

    @property
    def busy(self) -> bool:
        return self._busy

    @property
    def exhausted(self) -> bool:
        return self.requests >= self.max_requests and not self._busy

    def status(self, audio_t: float) -> str:
        if self._busy:
            return "identifying"
        if self.exhausted:
            return "limit reached"
        if not self.history:
            return "listening"
        last = self.history[-1].result
        return "matched" if last.matched else ("error" if last.error else "no match")

    def seconds_until_next(self, audio_t: float) -> float | None:
        if self._busy or self.exhausted:
            return None
        return max(0.0, self.next_due - audio_t)

    def update(self, audio_t: float) -> list[Attempt]:
        finished = []
        while True:
            try:
                clip_end_t, result = self._done.get_nowait()
            except queue.Empty:
                break
            attempt = Attempt(clip_end_t, audio_t, result)
            self.history.append(attempt)
            if result.matched:
                self.current = attempt
            self.next_due = audio_t + (self.recheck_s if result.matched else self.retry_s)
            self._busy = False
            finished.append(attempt)
        if not self._busy and self.requests < self.max_requests and audio_t >= self.next_due:
            samplerate, audio = self.get_clip(self.clip_s)
            wav = encode_wav(audio, samplerate)
            self._busy = True
            self.requests += 1
            self._thread = threading.Thread(
                target=self._work, args=(wav, audio_t), name="identify", daemon=True
            )
            self._thread.start()
        return finished

    def _work(self, wav: bytes, clip_end_t: float) -> None:
        try:
            result = self.service.identify(wav)
        except Exception as exc:  # identify() should not raise; never let the thread die
            result = Result(self.service.name, False, error=f"{type(exc).__name__}: {exc}")
        self._done.put((clip_end_t, result))

    def wait(self, timeout: float | None = None) -> None:
        """Wait for a running attempt to finish (for shutdown and tests)."""
        if self._thread is not None:
            self._thread.join(timeout)
