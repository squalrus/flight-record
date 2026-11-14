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
from flightrec.env import load_env
from flightrec.messages import (
    FeaturesMessage,
    Message,
    SideEnd,
    SideState,
    TrackIdentified,
    read_log,
    replay,
)
from flightrec.tags import Tags, read_tags

HERE = Path(__file__).resolve().parent
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
    capture = Capture(source, seconds=max(3.0, 2 * fx.window / sr))
    reader = HopReader(capture, fx.window, fx.hop)
    file_frames = len(source.frames) if args.wav is not None else None  # type: ignore[attr-defined]
    duration = file_frames / sr if file_frames is not None else 0.0
    status = Status(sender, log)
    last: dict[str, float] = {}
    tags = read_tags(args.wav) if args.wav is not None else Tags()

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
                log.clocks(sender)
                status.tick(t)
                time.sleep(0.002)
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
    args = parser.parse_args(argv)
    if args.listen and args.wav is None:
        parser.error("--listen needs --wav: live input is never played by the computer")
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
