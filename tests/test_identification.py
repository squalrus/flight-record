"""Recognition clients offline: response parsing, ACRCloud signing, multipart, env, tags."""

import base64
import hashlib
import hmac
import os
import time
from pathlib import Path

import pytest

from flightrec.env import load_env
from flightrec.identification import (
    ACRCloud,
    Attempt,
    AudD,
    IdentificationError,
    Identifier,
    Result,
    Service,
    multipart,
)
from flightrec.tags import read_id3_frames, read_tags

# AudD


def test_audd_match() -> None:
    raw = {
        "status": "success",
        "result": {"artist": "Kalax", "title": "Calling", "album": "III", "timecode": "01:02"},
    }
    r = AudD("token").parse(raw)
    assert (r.matched, r.artist, r.title, r.album) == (True, "Kalax", "Calling", "III")


def test_audd_no_match_and_error() -> None:
    assert AudD("t").parse({"status": "success", "result": None}).matched is False
    err = AudD("t").parse(
        {"status": "error", "error": {"error_code": 900, "error_message": "bad token"}}
    )
    assert err.matched is False and err.error == "900: bad token"


# ACRCloud


def test_acrcloud_match_scales_score() -> None:
    raw = {
        "status": {"code": 0, "msg": "Success"},
        "metadata": {
            "music": [
                {
                    "title": "False Metal",
                    "artists": [{"name": "Glowbug"}, {"name": "Josh Holland"}],
                    "album": {"name": "Headhunters"},
                    "score": 87,
                }
            ]
        },
    }
    r = ACRCloud("h", "k", "s").parse(raw)
    assert r.matched and r.artist == "Glowbug, Josh Holland" and r.album == "Headhunters"
    assert r.score == pytest.approx(0.87)


def test_acrcloud_no_match_and_error() -> None:
    assert (
        ACRCloud("h", "k", "s").parse({"status": {"code": 1001, "msg": "No result"}}).matched
        is False
    )
    err = ACRCloud("h", "k", "s").parse(
        {"status": {"code": 3001, "msg": "Missing/Invalid Access Key"}}
    )
    assert err.error == "3001: Missing/Invalid Access Key"


def test_acrcloud_signature() -> None:
    service = ACRCloud("https://identify-eu-west-1.acrcloud.com/", "key123", "secret456")
    assert service.host == "identify-eu-west-1.acrcloud.com"
    expected = base64.b64encode(
        hmac.new(
            b"secret456", b"POST\n/v1/identify\nkey123\naudio\n1\n1700000000", hashlib.sha1
        ).digest()
    ).decode()
    assert service.signature("1700000000") == expected


def test_network_failure_becomes_an_error_result(monkeypatch: pytest.MonkeyPatch) -> None:
    service = AudD("t")

    def fail(wav: bytes) -> dict:
        raise IdentificationError("URLError: no route")

    monkeypatch.setattr(service, "_send", fail)
    result = service.identify(b"RIFF")
    assert isinstance(result, Result) and not result.matched and "no route" in result.error


def test_multipart_body() -> None:
    body, content_type = multipart(
        {"api_token": "abc"}, {"file": ("c.wav", b"\x00\x01", "audio/wav")}
    )
    boundary = content_type.split("boundary=")[1]
    assert body.startswith(f"--{boundary}\r\n".encode())
    assert b'name="api_token"\r\n\r\nabc\r\n' in body
    assert b'filename="c.wav"\r\nContent-Type: audio/wav\r\n\r\n\x00\x01\r\n' in body
    assert body.endswith(f"--{boundary}--\r\n".encode())


def test_from_env(monkeypatch: pytest.MonkeyPatch) -> None:
    for key in ("AUDD_API_TOKEN", "ACRCLOUD_HOST", "ACRCLOUD_ACCESS_KEY", "ACRCLOUD_ACCESS_SECRET"):
        monkeypatch.delenv(key, raising=False)
    assert AudD.from_env() is None and ACRCloud.from_env() is None
    monkeypatch.setenv("AUDD_API_TOKEN", "x")
    monkeypatch.setenv("ACRCLOUD_HOST", "h")
    monkeypatch.setenv("ACRCLOUD_ACCESS_KEY", "k")
    assert isinstance(AudD.from_env(), AudD)
    assert ACRCloud.from_env() is None  # secret missing
    monkeypatch.setenv("ACRCLOUD_ACCESS_SECRET", "s")
    assert isinstance(ACRCloud.from_env(), ACRCloud)


# .env loader


def test_load_env(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    for key in ("FR_A", "FR_B", "FR_C", "FR_D", "FR_E"):
        monkeypatch.delenv(key, raising=False)
    monkeypatch.setenv("FR_E", "from shell")
    env = tmp_path / ".env"
    env.write_text(
        "# comment\n\nFR_A=plain\nFR_B=\"quoted value\"\nexport FR_C='single'\n"
        "FR_D=value # trailing comment\nFR_E=from file\nnot a pair\n"
    )
    loaded = load_env(env)
    assert os.environ["FR_A"] == "plain"
    assert os.environ["FR_B"] == "quoted value"
    assert os.environ["FR_C"] == "single"
    assert os.environ["FR_D"] == "value"
    assert os.environ["FR_E"] == "from shell"  # the shell wins
    assert sorted(loaded) == ["FR_A", "FR_B", "FR_C", "FR_D"]
    assert load_env(tmp_path / "missing.env") == []


# Tags


def id3_frame(fid: str, text: str) -> bytes:
    body = b"\x03" + text.encode("utf-8")
    return fid.encode() + len(body).to_bytes(4, "big") + b"\x00\x00" + body


def test_read_id3_tags(tmp_path: Path) -> None:
    frames = id3_frame("TPE1", "Kalax") + id3_frame("TIT2", "Calling (feat. Frankmusik)")
    frames += id3_frame("TBPM", "90") + id3_frame("TKEY", "C#")
    size = len(frames)
    syncsafe = bytes([(size >> 21) & 0x7F, (size >> 14) & 0x7F, (size >> 7) & 0x7F, size & 0x7F])
    path = tmp_path / "song.mp3"
    path.write_bytes(b"ID3\x03\x00\x00" + syncsafe + frames + b"\xff\xfb" + b"\x00" * 64)

    assert read_id3_frames(path)["TPE1"] == ["Kalax"]
    tags = read_tags(path)
    assert (tags.artist, tags.title, tags.bpm, tags.key) == (
        "Kalax",
        "Calling (feat. Frankmusik)",
        "90",
        "C#",
    )


def test_tags_fall_back_to_store_filename(tmp_path: Path) -> None:
    path = tmp_path / "Glowbug - Headhunters - 03 False Metal.mp3"
    path.write_bytes(b"\xff\xfb" + b"\x00" * 64)  # no ID3 header
    tags = read_tags(path)
    assert (tags.artist, tags.album, tags.title) == ("Glowbug", "Headhunters", "False Metal")


# Background identifier


class SlowFake(Service):
    """Matches after ``match_after`` calls; takes ``delay`` seconds per call."""

    name = "fake"

    def __init__(self, delay: float = 0.0, match_after: int = 0, fail: bool = False) -> None:
        super().__init__()
        self.delay = delay
        self.match_after = match_after
        self.fail = fail
        self.calls = 0
        self.clip_lengths: list[int] = []

    def identify(self, wav: bytes) -> Result:
        import io

        import soundfile

        self.calls += 1
        self.clip_lengths.append(soundfile.info(io.BytesIO(wav)).frames)
        time.sleep(self.delay)
        if self.fail:
            return Result(self.name, False, error="HTTP 503")
        if self.calls > self.match_after:
            return Result(
                self.name, True, artist="Kalax", title="Calling", score=0.9, latency_s=self.delay
            )
        return Result(self.name, False)


def clip_source(samplerate: int = 8000):
    import numpy as np

    return lambda seconds: (samplerate, np.zeros(int(seconds * samplerate), dtype=np.float32))


def drive(identifier: Identifier, until: float, step: float = 0.5) -> list:
    """Advance audio time, letting each background attempt finish before moving on."""
    attempts = []
    t = 0.0
    while t <= until:
        attempts += identifier.update(t)
        identifier.wait(5)
        t += step
    return attempts + identifier.update(t)


def test_identifier_schedule_retry_then_recheck() -> None:
    service = SlowFake(match_after=2)  # no match, no match, then match
    ident = Identifier(service, clip_source(), clip_s=10, retry_s=8, recheck_s=30, max_requests=10)
    attempts = drive(ident, until=70)
    # Intervals count from when each answer arrived (one 0.5 s step after the request):
    # t = 10 (first full clip); answer at 10.5 -> retry at 18.5; answer at 19 -> 27 (match);
    # answer at 27.5 -> recheck at 57.5.
    assert [a.clip_end_t for a in attempts] == [10.0, 18.5, 27.0, 57.5]
    assert [a.t for a in attempts] == [10.5, 19.0, 27.5, 58.0]
    assert [a.result.matched for a in attempts] == [False, False, True, True]
    assert ident.current is not None and ident.current.result.title == "Calling"
    assert service.clip_lengths[0] == 10 * 8000  # the last 10 s of audio


def test_identifier_never_blocks_the_caller() -> None:
    service = SlowFake(delay=0.5)
    ident = Identifier(service, clip_source(), clip_s=1, max_requests=3)
    start = time.perf_counter()
    assert ident.update(1.0) == []  # starts the request...
    assert time.perf_counter() - start < 0.2  # ...and returns immediately
    assert ident.busy and ident.status(1.0) == "identifying"
    ident.wait(5)
    finished = ident.update(1.1)
    assert len(finished) == 1 and finished[0].t == 1.1 and finished[0].clip_end_t == 1.0


def test_identifier_respects_the_request_cap() -> None:
    service = SlowFake(fail=True)
    ident = Identifier(service, clip_source(), clip_s=1, retry_s=1, max_requests=3)
    attempts = drive(ident, until=30)
    assert service.calls == 3 and len(attempts) == 3
    assert ident.exhausted and ident.status(30) == "limit reached"
    assert ident.seconds_until_next(30) is None


def test_attempts_become_contract_messages() -> None:
    from flightrec.messages import IdentificationFailed, TrackIdentified, loads

    matched = Attempt(
        10.0,
        11.2,
        Result(
            "acrcloud",
            True,
            artist="Kalax",
            title="Calling",
            album="III",
            score=0.95,
            latency_s=1.2,
        ),
    )
    msg = matched.to_message()
    assert isinstance(msg, TrackIdentified) and msg.confidence == 0.95 and msg.t == 11.2
    assert msg.extra["service"] == "acrcloud" and msg.extra["clip_end_t"] == 10.0
    assert loads(msg.to_json()) == msg

    no_score = Attempt(10.0, 12.0, Result("audd", True, artist="a", title="b")).to_message()
    assert no_score.confidence == 1.0

    none = Attempt(10.0, 11.0, Result("audd", False)).to_message()
    assert isinstance(none, IdentificationFailed) and none.reason == "no_match"
    err = Attempt(10.0, 11.0, Result("audd", False, error="HTTP 503")).to_message()
    assert err.reason == "error" and err.extra["error"] == "HTTP 503"
