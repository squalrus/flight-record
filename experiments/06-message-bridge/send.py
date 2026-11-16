"""Experiment 06: send the message contract to the Unity bridge scene over UDP.

Usage (from the repo root, with the venv active):
    python experiments/06-message-bridge/send.py --wav song.mp3 --listen   # file, heard
    python experiments/06-message-bridge/send.py --wav song.mp3            # file, silent
    python experiments/06-message-bridge/send.py --device 9                # live input
    python experiments/06-message-bridge/send.py --log LOG.jsonl           # a recorded log

Runs the same feature pipeline as the explorer and the recorder and sends one
``features`` message per analysis hop (about 94 per second). For files it also sends what
a file can tell without fingerprinting (D-012): ``track_identified`` from the tags,
``side_state`` once a second with the exact time left, and ``side_end`` at the end.

Live input is identified by default when recognition keys are set (experiments 03-04):
every so often the last 10 s go to ACRCloud, then AudD on a miss, on a background thread;
a match is sent as ``track_identified`` and looked up on Discogs, whose release goes out
as ``release_selected`` with the cover image's cached path (``cover_path``). Both are
re-sent every 10 s, since UDP may drop them. ``--no-identify`` turns it off,
``--identify`` turns it on for files, ``--no-discogs`` skips the release lookup. The game
never waits on any of it (D-004).

Every message sent is written to a timing CSV (default
experiments/06-message-bridge/out/send-*.csv): its audio time ``t``, when the sound it
came from happened (``event``), and when it left (``sent``), on the ``perf_counter()``
clock. The scene sends clock samples back, logged as ``clock`` rows, so analyze.py can put
both sides' times on one clock.

``event`` is when the audio reached the computer (live input, minus the reported input
latency), when it was heard (``--listen``, plus the reported output latency), or when a
silent replay delivered it. ``--listen`` plays only files, never live input (D-011).
"""

from __future__ import annotations

import argparse
import csv
import statistics
import sys
import time
from dataclasses import replace
from datetime import datetime
from pathlib import Path

from flightrec.audio import (
    DEFAULT_GATE_DB,
    Capture,
    FeatureExtractor,
    HopReader,
    open_source,
)
from flightrec.bridge import DEFAULT_HOST, DEFAULT_PORT, UdpSender
from flightrec.console import safe_output
from flightrec.discogs import Discogs, Lookup, ReleaseTracker, Selections, release_year
from flightrec.env import load_env
from flightrec.identification import SERVICES, Attempt, Identifier, Service, service_chain
from flightrec.messages import (
    FeaturesMessage,
    Message,
    ReleaseSelected,
    SideEnd,
    SideState,
    TrackIdentified,
    read_log,
    replay,
)
from flightrec.tags import Tags, read_tags

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
DISCOGS_DATA = ROOT / "samples" / "_local" / "discogs"
COLUMNS = ["kind", "type", "t", "onset", "event", "sent", "game", "arrived"]


class TimingLog:
    """The sender's half of the measurement: one CSV row per message and clock sample."""

    def __init__(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        self.path = path
        self._fh = path.open("w", newline="", encoding="utf-8")
        self._csv = csv.writer(self._fh)
        self._csv.writerow(COLUMNS)
        self.offsets: list[float] = []
        """Recent game-clock minus sender-clock samples, newest last."""

    def message(self, m: Message, event: float, sent: float | None) -> None:
        onset = int(m.onset) if isinstance(m, FeaturesMessage) else ""
        self._csv.writerow(["msg", m.TYPE, f"{m.t:.6f}", onset, f"{event:.6f}", _f(sent), "", ""])

    def clocks(self, sender: UdpSender) -> None:
        for arrived, reply in sender.replies():
            game = reply.get("clock")
            if isinstance(game, (int, float)):
                self._csv.writerow(["clock", "", "", "", "", "", f"{game:.6f}", f"{arrived:.6f}"])
                self.offsets = [*self.offsets[-49:], game - arrived]

    def close(self) -> None:
        self._fh.close()


def _f(value: float | None) -> str:
    return "" if value is None else f"{value:.6f}"


class Status:
    """One status line a second, so a run can be watched without the scene."""

    def __init__(self, sender: UdpSender, log: TimingLog) -> None:
        self.sender, self.log = sender, log
        self.next = time.perf_counter() + 1.0
        self.last_sent = 0
        self.onsets = 0

    def tick(self, t: float) -> None:
        now = time.perf_counter()
        if now < self.next:
            return
        self.next = now + 1.0
        rate = self.sender.sent - self.last_sent
        self.last_sent = self.sender.sent
        game = (
            f"game clock offset {statistics.median(self.log.offsets) * 1000:+.2f} ms"
            if self.log.offsets
            else "no reply from the game yet"
        )
        print(
            f"t {t:7.1f} s  sent {rate:3d}/s  onsets {self.onsets:5d}  "
            f"send errors {self.sender.errors}  {game}",
            flush=True,
        )


def file_messages(tags: Tags, t: float, duration: float, side: str, last: dict) -> list[Message]:
    """Track and side messages a file provides without identification, when due."""
    out: list[Message] = []
    if t - last.get("track", -1e9) >= 10.0:  # repeated: UDP may drop, the game may start late
        last["track"] = t
        if tags.artist and tags.title:
            out.append(
                TrackIdentified(
                    t=t,
                    artist=tags.artist,
                    title=tags.title,
                    album=tags.album,
                    genres=[tags.genre] if tags.genre else None,
                    confidence=1.0,
                    track_seconds=round(duration, 3),
                    offset_seconds=round(t, 3),
                )
            )
    if t - last.get("side", -1e9) >= 1.0:
        last["side"] = t
        out.append(
            SideState(
                t=t,
                side=side,
                track_index=0,
                side_elapsed=round(t, 3),
                side_expected=round(duration, 3),
                side_confidence=1.0,
            )
        )
    return out


class Identification:
    """Identify what is playing and send it to the game, never blocking the send loop.

    ``update(audio_t)`` returns the messages to send now: each identification answer
    (``track_identified`` or ``identification_failed``), each Discogs release
    (``release_selected`` with ``cover_path``), and every ``RESEND_S`` the current track and
    release again with a fresh ``t`` (a message with an old ``t`` would look like a restart
    to the game's delay buffer; ``clip_end_t`` keeps the track position right).
    """

    RESEND_S = 10.0

    def __init__(
        self,
        identifier: Identifier,
        tracker: ReleaseTracker | None,
        discogs: Discogs | None,
    ) -> None:
        self.identifier = identifier
        self.tracker = tracker
        self.discogs = discogs
        self.track: TrackIdentified | None = None
        self.release: ReleaseSelected | None = None
        self._resent = -1e9

    def update(self, audio_t: float) -> list[Message]:
        t = round(audio_t, 4)
        out: list[Message] = []
        for attempt in self.identifier.update(audio_t):
            msg = attempt.to_message()
            print(describe_attempt(attempt), flush=True)
            out.append(msg)
            r = attempt.result
            if not (isinstance(msg, TrackIdentified) and r.matched):
                continue
            if self.track is None or _track_key(self.track) != _track_key(msg):
                self.release = None  # a new track may be on another record
            self.track = msg
            self._resent = audio_t  # just sent: resend RESEND_S from now
            if self.tracker is not None and r.artist:
                self.tracker.request(
                    r.artist, r.album, r.title,
                    alt_albums=r.alt_albums, label=r.label, barcode=r.upc,
                    year=release_year(r.release_date),
                )  # fmt: skip
        if self.tracker is not None:
            found = self.tracker.poll()
            if found is not None:
                print(describe_lookup(found), flush=True)
                if found.message is not None:
                    self.release = replace(found.message, t=t, cover_path=self._cover(found))
                    out.append(self.release)
                    self._resent = audio_t
        if (self.track or self.release) and audio_t - self._resent >= self.RESEND_S:
            self._resent = audio_t
            out.extend(replace(m, t=t) for m in (self.track, self.release) if m is not None)
        return out

    def _cover(self, found: Lookup) -> str | None:
        if self.discogs is None or not found.cover or not found.cover_url:
            return None
        path = self.discogs.image_path(found.cover_url)
        return str(path) if path is not None and path.exists() else None

    def finish(self, audio_t: float) -> list[Message]:
        """Wait briefly for an answer already on its way, so it is not lost at the end."""
        if self.identifier.busy:
            self.identifier.wait(timeout=5.0)
        return self.update(audio_t)


def _track_key(m: TrackIdentified) -> tuple[str | None, ...]:
    return (m.recording_id,) if m.recording_id else (m.artist.lower(), m.title.lower())


def describe_attempt(attempt: Attempt) -> str:
    r = attempt.result
    took = f"{r.latency_s:.1f} s"
    if r.matched:
        left = ""
        if r.duration_s is not None and r.offset_s is not None:
            left = f", {max(0.0, r.duration_s - r.offset_s):.0f} s left"
        return f"identified ({r.service}, {took}): {r.artist} - {r.title} [{r.album or '?'}]{left}"
    if r.error:
        return f"identification error ({r.service}, {took}): {r.error}"
    return f"no match ({r.service}, {took})"


def describe_lookup(found: Lookup) -> str:
    if found.release is None or found.message is None:
        return f"discogs: {found.how}" + (f" ({found.error})" if found.error else "")
    m = found.message
    cover = "with cover" if found.cover else "no cover"
    return (
        f"discogs: {m.album} ({m.year or '?'}), side {m.side}, {len(found.tracks)} tracks, "
        f"release {m.discogs_id}, {found.how}, {cover}"
    )


def make_identification(
    args: argparse.Namespace, capture: Capture, samplerate: int
) -> Identification | None:
    """The identification for this run, or None when it is off or has no keys."""
    if not args.identify:
        return None
    service: Service | None = service_chain(args.services)
    if service is None:
        print(f"identification off: no keys for {', '.join(args.services)} in .env.local or .env")
        return None
    identifier = Identifier(
        service,
        lambda seconds: (samplerate, capture.latest(int(seconds * samplerate))[0]),
        clip_s=args.identify_clip,
        max_requests=args.max_identify,
    )
    discogs = tracker = None
    if args.discogs:
        discogs = Discogs.from_env(cache_dir=DISCOGS_DATA / "http")
        if discogs is None:
            print("discogs off: DISCOGS_TOKEN is not set in .env.local or .env")
        else:
            tracker = ReleaseTracker(discogs, Selections(DISCOGS_DATA / "selections.json"))
    print(
        f"identifying with {service.name}: {args.identify_clip:g} s clips, "
        f"up to {args.max_identify} requests" + (", then Discogs" if tracker else ""),
        flush=True,
    )
    return Identification(identifier, tracker, discogs)


def run_audio(args: argparse.Namespace, sender: UdpSender, log: TimingLog) -> None:
    source = open_source(
        args.wav,
        device=args.device,
        samplerate=args.samplerate,
        channels=args.channels,
        blocksize=args.blocksize,
        listen=args.listen,
    )
    sr = source.samplerate
    fx = FeatureExtractor(sr, hop=args.hop, gate_db=args.gate)
    clip_s = args.identify_clip if args.identify else 0.0
    capture = Capture(source, seconds=max(3.0, 2 * fx.window / sr, clip_s + 1.0))
    reader = HopReader(capture, fx.window, fx.hop)
    file_frames = len(source.frames) if args.wav is not None else None  # type: ignore[attr-defined]
    duration = file_frames / sr if file_frames is not None else 0.0
    status = Status(sender, log)
    last: dict[str, float] = {}
    tags = read_tags(args.wav) if args.wav is not None else Tags()
    ident = make_identification(args, capture, sr)
    if ident is not None:
        tags = Tags()  # identification answers what is playing; don't also send the tags

    def send(m: Message) -> None:
        # When the sound behind this message happened, on the perf_counter() clock.
        # Read after start: the latencies are known once the stream is open.
        shift = source.output_latency - source.input_latency
        event = (capture.base_offset or 0.0) + m.t + shift
        log.message(m, event, sender.send(m))

    started = time.perf_counter()
    t = 0.0
    try:
        with capture:
            desc = f"file {args.wav.name}" if args.wav else f"live device {source.device!r}"  # type: ignore[attr-defined]
            print(
                f"sending {desc} to {args.host}:{args.port}  "
                f"(input latency {source.input_latency * 1000:.1f} ms, "
                f"output latency {source.output_latency * 1000:.1f} ms)",
                flush=True,
            )
            while not source.finished and not (
                args.seconds and time.perf_counter() - started >= args.seconds
            ):
                for window, end in reader.pending():
                    if file_frames is not None and end > file_frames:
                        continue
                    m = fx.process(window, end).to_message()
                    t = m.t
                    status.onsets += m.onset
                    send(m)
                    if args.wav is not None:
                        for extra in file_messages(tags, t, duration, args.side, last):
                            send(extra)
                if ident is not None:
                    for m in ident.update(capture.frames / sr):
                        send(m)
                log.clocks(sender)
                status.tick(t)
                time.sleep(0.002)
            if ident is not None:
                for m in ident.finish(capture.frames / sr):
                    send(m)
            if source.finished:
                capture.wait(0)  # re-raises a replay error, if any
                send(SideEnd(t=round(duration, 4), cause="file_end"))
    except KeyboardInterrupt:
        pass
    time.sleep(0.2)
    log.clocks(sender)
    print(f"skipped hops {reader.skipped_hops}, input overflows {source.overflows}")


def run_log(args: argparse.Namespace, sender: UdpSender, log: TimingLog) -> None:
    messages = list(read_log(args.log))
    print(f"sending {len(messages)} messages from {args.log.name} to {args.host}:{args.port}")
    status = Status(sender, log)
    started = time.perf_counter()
    try:
        for m in replay(messages, speed=args.speed):
            if args.seconds and time.perf_counter() - started >= args.seconds:
                break
            now = time.perf_counter()
            log.message(m, now, sender.send(m))  # no audio: the replay is the event
            if isinstance(m, FeaturesMessage):
                status.onsets += m.onset
            log.clocks(sender)
            status.tick(m.t)
    except KeyboardInterrupt:
        pass
    time.sleep(0.2)
    log.clocks(sender)


def parse_device(value: str) -> int | str:
    return int(value) if value.isdigit() else value


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    src = parser.add_mutually_exclusive_group()
    src.add_argument("--wav", type=Path, help="send features from this audio file")
    src.add_argument("--log", type=Path, help="send a recorded message log")
    parser.add_argument("--device", type=parse_device, help="live input index or name")
    parser.add_argument("--listen", action="store_true", help="also play --wav aloud (D-011)")
    parser.add_argument("--host", default=DEFAULT_HOST)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--seconds", type=float, help="stop after this long")
    parser.add_argument("--side", default="A", help="side label sent for files")
    parser.add_argument("--speed", type=float, default=1.0, help="--log playback speed")
    parser.add_argument("--out", type=Path, help="timing CSV (default out/send-*.csv)")
    parser.add_argument("--samplerate", type=int, default=48_000, help="live only")
    parser.add_argument("--channels", type=int, default=2)
    parser.add_argument("--blocksize", type=int, default=512)
    parser.add_argument("--hop", type=int, default=512)
    parser.add_argument("--gate", type=float, default=DEFAULT_GATE_DB, help="trigger gate, dBFS")
    ident = parser.add_argument_group("identification (experiments 03-04)")
    ident.add_argument(
        "--identify",
        action=argparse.BooleanOptionalAction,
        default=None,
        help="identify what is playing (default: on for live input when keys are set)",
    )
    ident.add_argument(
        "--services",
        nargs="+",
        choices=sorted(SERVICES),
        default=["acrcloud", "audd"],
        help="recognition services, tried in order until one matches",
    )
    ident.add_argument("--identify-clip", type=float, default=10.0, help="seconds per request")
    ident.add_argument("--max-identify", type=int, default=20, help="requests per run")
    ident.add_argument(
        "--discogs",
        action=argparse.BooleanOptionalAction,
        default=True,
        help="look up each match on Discogs for the track list and cover",
    )
    args = parser.parse_args(argv)
    if args.listen and args.wav is None:
        parser.error("--listen needs --wav: live input is never played by the computer")
    if args.identify is None:
        args.identify = args.wav is None and args.log is None  # live input
    elif args.identify and args.log is not None:
        parser.error("--identify needs audio: a message log has none")
    safe_output()
    load_env()  # FR_AUDIO_DEVICE

    out = args.out or HERE / "out" / f"send-{datetime.now():%Y%m%d-%H%M%S}.csv"
    log = TimingLog(out)
    try:
        with UdpSender(args.host, args.port) as sender:
            if args.log:
                run_log(args, sender, log)
            else:
                run_audio(args, sender, log)
            print(
                f"sent {sender.sent} messages ({sender.bytes / 1024:.0f} KB), "
                f"{sender.errors} send errors"
            )
    finally:
        log.close()
    print(f"timing log: {log.path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
