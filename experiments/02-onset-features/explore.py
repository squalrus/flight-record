"""Experiment 02 explorer: see what can be pulled out of the music, live or from a WAV.

Panels: scrolling spectrogram; trigger lanes (low / mid / high band onsets, full-band
onsets, predicted beats); energy traces (bass / mid / treble / loudness); brightness
(spectral centroid); tempo with confidence and a beat pulse; chroma with the estimated key;
L/R meters. Every hop's features are logged as JSON lines in the message-contract format.

Usage (from the repo root, with the venv active):
    python experiments/02-onset-features/explore.py --device 9            # live mic
    python experiments/02-onset-features/explore.py --wav clip.wav --loop # replay
    python experiments/02-onset-features/explore.py --wav clip.wav --offline
    python experiments/02-onset-features/explore.py --device 9 --headless --minutes 5
    python experiments/02-onset-features/explore.py --wav song.mp3 --listen --identify acrcloud

--identify adds an identification panel (experiment 03): the last few seconds of audio go
to the service on a background thread, and each answer is logged as a contract message.
--offline analyzes a WAV as fast as possible with no window (same results as replay,
since analysis depends only on the audio). Esc or Q closes the window.
"""

from __future__ import annotations

import argparse
import sys
import time
from collections import Counter, deque
from collections.abc import Sequence
from dataclasses import replace
from datetime import datetime
from pathlib import Path
from typing import Any

import numpy as np

from flightrec.audio import (
    BAND_NAMES,
    DEFAULT_GATE_DB,
    Capture,
    FeatureExtractor,
    Features,
    HopReader,
    LogBands,
    channel_levels,
    open_source,
    read_audio,
    to_db,
)
from flightrec.console import safe_output
from flightrec.discogs import (
    Check,
    Discogs,
    Lookup,
    Progress,
    ReleaseTracker,
    Selections,
    agreement,
    find_track,
    progress,
)
from flightrec.env import load_env
from flightrec.identification import SERVICES, Attempt, Identifier
from flightrec.messages import LogWriter

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
LANES = (*BAND_NAMES, "all", "beat")


def parse_device(value: str) -> int | str:
    return int(value) if value.isdigit() else value


def sleep_until(due: float) -> None:
    while (delay := due - time.perf_counter()) > 0:
        time.sleep(delay)


class Summary:
    """Counts and running values for the end-of-run report."""

    def __init__(self) -> None:
        self.counts: Counter[str] = Counter()
        self.hops = 0
        self.centroids: list[float] = []
        self.last: Features | None = None
        self.process_s = 0.0

    def add(self, f: Features) -> None:
        self.hops += 1
        self.last = f
        for name in (*BAND_NAMES, "all"):
            if f.onsets[name]:
                self.counts[name] += 1
        if f.beat:
            self.counts["beat"] += 1
        if f.centroid_hz > 0:
            self.centroids.append(f.centroid_hz)

    def report(self, audio_s: float, hop_s: float, skipped: int = 0) -> None:
        minutes = max(audio_s / 60.0, 1e-9)
        rates = ", ".join(
            f"{name} {self.counts[name]} ({self.counts[name] / minutes:.0f}/min)" for name in LANES
        )
        last = self.last
        per_hop = self.process_s / max(self.hops, 1)
        print(f"audio {audio_s:.1f} s, {self.hops} hops, skipped {skipped}")
        print(f"triggers: {rates}")
        if last is not None:
            tempo = f"{last.tempo:.1f} BPM" if last.tempo else "none"
            print(
                f"tempo {tempo} (confidence {last.tempo_confidence:.2f}), "
                f"key {last.key or 'none'} (confidence {last.key_confidence:.2f}), "
                f"median brightness {np.median(self.centroids or [0]):.0f} Hz"
            )
        print(f"analysis {per_hop * 1000:.2f} ms/hop ({per_hop / hop_s * 100:.1f}% of real time)")


class Explorer:
    """The window layout (imports pygame through flightrec.viz)."""

    W, H = 1280, 800

    def __init__(
        self,
        title: str,
        channels: int,
        freqs: np.ndarray,
        identify: bool = False,
        discogs: bool = False,
    ) -> None:
        from flightrec import viz

        self.viz = viz
        # Taller window for the identification (and release) panels along the bottom.
        self.H = 1010 if discogs else (960 if identify else 800)
        self.win = viz.Window(title, (self.W, self.H))
        self.identification = None
        self.release = None
        if discogs:
            self.identification = viz.IdentificationPanel(self.win, (16, 746, 560, 200))
            self.release = viz.ReleasePanel(self.win, (592, 746, self.W - 608, 200))
        elif identify:
            self.identification = viz.IdentificationPanel(self.win, (16, 746, self.W - 32, 150))
        left_w, right_x = 860, 892
        right_w = self.W - right_x - 16
        self.bands = LogBands(freqs, 160, 30.0, 16_000.0)
        self.spectrogram = viz.SpectrogramPanel(
            self.win, (16, 16, left_w, 240), 160, title="spectrogram 30 Hz - 16 kHz"
        )
        self.lanes = viz.LanesPanel(
            self.win, (16, 272, left_w, 180), LANES, seconds=6.0, title="triggers (last 6 s)"
        )
        self.energy = viz.TracePanel(
            self.win,
            (16, 468, left_w, 150),
            {"bass": viz.LANE["low"], "mid": viz.LANE["mid"], "treble": viz.LANE["high"],
             "loud": viz.HOLD},
            0.0, 1.0, seconds=10.0, title="energy 0..1 (last 10 s)",
        )  # fmt: skip
        self.bright = viz.TracePanel(
            self.win,
            (16, 634, left_w, 96),
            {"centroid": viz.HUM},
            150.0, 8000.0, seconds=10.0, log=True, title="brightness (Hz, log)", fmt="{:.0f}",
        )  # fmt: skip
        self.tempo = viz.TempoPanel(self.win, (right_x, 16, right_w, 116), title="tempo")
        self.chroma = viz.ChromaPanel(self.win, (right_x, 148, right_w, 250), title="chroma / key")
        self.meters = viz.MeterPanel(self.win, (right_x, 414, 150, 290), channels)
        self.stats_x = right_x + 170
        self.columns: list[np.ndarray] = []

    def add(self, f: Features, magnitude: np.ndarray, hop_s: float) -> None:
        self.columns.append(self.bands.compute(to_db(magnitude)))
        onset_t = f.t - hop_s  # onset flags refer to the previous hop
        for name in (*BAND_NAMES, "all"):
            if f.onsets[name]:
                self.lanes.add(onset_t, name, f.strengths[name])
        if f.beat:
            self.lanes.add(f.t, "beat")
        self.energy.add(f.t, {"bass": f.bass, "mid": f.mid, "treble": f.treble, "loud": f.rms})
        if f.centroid_hz > 0:
            self.bright.add(f.t, {"centroid": f.centroid_hz})

    def draw(
        self,
        f: Features,
        frames: np.ndarray,
        beat: bool,
        stats: list[str],
        status: list[str],
        identifier: Identifier | None = None,
        audio_t: float = 0.0,
        tracker: ReleaseTracker | None = None,
        prog: Progress | None = None,
        checks: Sequence[Check] = (),
    ) -> None:
        win = self.win
        win.begin()
        self.spectrogram.push(self.columns)
        self.columns = []
        self.spectrogram.draw()
        self.lanes.draw(f.t)
        self.energy.draw(f.t)
        self.bright.draw(f.t)
        self.tempo.draw(f.tempo, f.tempo_confidence, beat)
        self.chroma.draw(f.chroma, f.key, f.key_confidence)
        rms, peak = channel_levels(frames)
        self.meters.draw(rms, peak)
        for i, line in enumerate(stats):
            win.text(line, (self.stats_x, 420 + i * 20))
        if self.identification is not None and identifier is not None:
            self.identification.draw(identifier, audio_t)
        if self.release is not None and tracker is not None:
            self.release.draw(tracker, prog, checks)
        self.viz.status_lines(win, status, self.H - 56)
        win.end()


def open_log(args: argparse.Namespace) -> LogWriter | None:
    if args.no_log:
        return None
    path = args.log or HERE / "out" / f"features-{datetime.now():%Y%m%d-%H%M%S}.jsonl"
    print(f"logging to {path}")
    return LogWriter(path)


def write(log: LogWriter | None, f: Features) -> None:
    if log is not None:
        log.write(f.to_message())


def adjust_gate(fx: FeatureExtractor, keys: list[int], pygame: Any) -> None:
    """``[`` lowers and ``]`` raises the trigger gate by 3 dB; lowering below -90 turns it
    off and raising from off starts at -90."""
    for key in keys:
        if key == pygame.K_RIGHTBRACKET:
            fx.gate_db = -90.0 if fx.gate_db is None else min(0.0, fx.gate_db + 3.0)
        elif key == pygame.K_LEFTBRACKET and fx.gate_db is not None:
            fx.gate_db = None if fx.gate_db <= -90.0 else fx.gate_db - 3.0


def run_offline(args: argparse.Namespace) -> int:
    samplerate, frames = read_audio(args.wav)
    fx = FeatureExtractor(samplerate, hop=args.hop, gate_db=args.gate)
    summary = Summary()
    log = open_log(args)
    padded = np.concatenate([np.zeros((fx.window, frames.shape[1]), np.float32), frames])
    try:
        for end in range(fx.hop, len(frames) + 1, fx.hop):
            t0 = time.perf_counter()
            f = fx.process(padded[end : end + fx.window], end)
            summary.process_s += time.perf_counter() - t0
            summary.add(f)
            write(log, f)
    finally:
        if log is not None:
            log.close()
    summary.report(len(frames) / samplerate, fx.hop / samplerate)
    return 0


def run_live(args: argparse.Namespace) -> int:
    source = open_source(
        args.wav,
        device=args.device,
        samplerate=args.samplerate,
        channels=args.channels,
        blocksize=args.blocksize,
        loop=args.loop,
        listen=args.listen,
    )
    sr = source.samplerate
    fx = FeatureExtractor(sr, hop=args.hop, gate_db=args.gate)
    clip_s = args.identify_clip if args.identify else 0.0
    capture = Capture(source, seconds=max(3.0, 2 * fx.window / sr, clip_s + 1.0))
    reader = HopReader(capture, fx.window, fx.hop)
    hop_s = fx.hop / sr
    summary = Summary()
    log = open_log(args)
    identifier = None
    if args.identify:
        identifier = Identifier(
            args.service,
            lambda seconds: (sr, capture.latest(int(seconds * sr))[0]),
            clip_s=args.identify_clip,
            retry_s=args.identify_retry,
            recheck_s=args.identify_recheck,
            max_requests=args.max_identify,
        )
    tracker = None
    if args.discogs:
        data = ROOT / "samples" / "_local" / "discogs"
        tracker = ReleaseTracker(args.discogs_client, Selections(data / "selections.json"))
    desc = f"replay {args.wav.name}" if args.wav else f"live device {source.device!r}"  # type: ignore[attr-defined]
    view = (
        None
        if args.headless
        else Explorer(
            f"Flight Record · 02 · {desc}",
            source.channels,
            fx.freqs,
            identifier is not None,
            tracker is not None,
        )
    )
    print(f"{desc}: {sr} Hz, hop {fx.hop} ({hop_s * 1000:.1f} ms), window {fx.window}")
    if identifier is not None:
        print(
            f"identifying with {args.identify}: {args.identify_clip:g} s clips, retry "
            f"{args.identify_retry:g} s, recheck {args.identify_recheck:g} s, "
            f"at most {args.max_identify} requests"
        )

    period = 1.0 / args.fps
    limit = args.minutes * 60 if args.minutes else None
    started = time.perf_counter()
    latest: Features | None = None
    pending: deque[tuple[Features, np.ndarray]] = deque()
    beat_seen = False
    try:
        with capture:
            next_frame = started
            while True:
                if view is not None:
                    if view.win.closed():
                        break
                    adjust_gate(fx, view.win.keys, view.viz.pygame)
                    if tracker is not None:
                        pg = view.viz.pygame
                        for key in view.win.keys:  # 1-5 confirm an ambiguous pressing
                            if pg.K_1 <= key <= pg.K_5:
                                tracker.confirm(key - pg.K_1)
                            elif key == pg.K_f:  # forget a wrong pick and look up again
                                tracker.forget()
                if source.finished or (limit and time.perf_counter() - started >= limit):
                    break
                for window, end in reader.pending():
                    t0 = time.perf_counter()
                    f = fx.process(window, end)
                    summary.process_s += time.perf_counter() - t0
                    summary.add(f)
                    write(log, f)
                    pending.append((f, fx.last_magnitude))
                # Show features once they can be heard: with --listen the speakers lag the
                # analysis by the output latency; otherwise that is zero and nothing waits.
                heard_t = capture.frames / sr - source.output_latency
                while pending and pending[0][0].t <= heard_t:
                    f, magnitude = pending.popleft()
                    if view is not None:
                        view.add(f, magnitude, hop_s)
                    beat_seen |= f.beat
                    latest = f
                audio_t = capture.frames / sr
                if identifier is not None:
                    for attempt in identifier.update(audio_t):
                        if log is not None:
                            log.write(attempt.to_message())
                        print(describe_attempt(attempt))
                        r = attempt.result
                        if tracker is not None and r.matched and r.artist:
                            tracker.request(
                                r.artist, r.album, r.title,
                                alt_albums=r.alt_albums, label=r.label, barcode=r.upc,
                                year=release_year(r.release_date),
                            )  # fmt: skip
                prog = None
                checks: list[Check] = []
                if tracker is not None:
                    found = tracker.poll()
                    if found is not None:
                        if log is not None and found.message is not None:
                            log.write(replace(found.message, t=round(audio_t, 4)))
                        print(describe_lookup(found))
                    match = identifier.current if identifier is not None else None
                    if match is not None:
                        res = match.result
                        prog = progress(
                            tracker.current, res.title, res.offset_s, match.clip_end_t,
                            audio_t, res.duration_s,
                        )  # fmt: skip
                        checks = agreement(tracker.current, res)
                if view is not None and latest is not None:
                    per_hop = summary.process_s / max(summary.hops, 1)
                    stats = [
                        f"hops      {summary.hops}",
                        f"skipped   {reader.skipped_hops}",
                        f"analysis  {per_hop * 1000:.2f} ms/hop",
                        f"          {per_hop / hop_s * 100:.1f}% real time",
                        f"loudness  {latest.rms_db:6.1f} dBFS",
                        f"bright    {latest.centroid_hz:6.0f} Hz",
                        f"overflows {source.overflows}",
                        "gate      "
                        + ("off" if fx.gate_db is None else f"{fx.gate_db:.0f} dBFS")
                        + "  [ ]",
                    ]
                    status = [
                        f"{desc}   {sr} Hz   hop {fx.hop}   "
                        f"interface latency {source.input_latency * 1000:.1f} ms   "
                        f"audio {latest.t:7.1f} s"
                    ]
                    frames, _ = capture.latest(fx.n_fft)
                    view.draw(
                        latest,
                        frames,
                        beat_seen,
                        stats,
                        status,
                        identifier,
                        audio_t,
                        tracker,
                        prog,
                        checks,
                    )
                    beat_seen = False
                next_frame += period
                if next_frame < time.perf_counter() - period:
                    next_frame = time.perf_counter()
                sleep_until(next_frame)
            if source.finished:
                capture.wait(0)
    except KeyboardInterrupt:
        pass
    finally:
        if view is not None:
            view.win.close()
        if identifier is not None and identifier.busy:
            identifier.wait(timeout=5.0)  # keep an answer that is already on its way
            for attempt in identifier.update(capture.frames / sr):
                if log is not None:
                    log.write(attempt.to_message())
                print(describe_attempt(attempt))
        if log is not None:
            log.close()
    summary.report(capture.frames / sr, hop_s, reader.skipped_hops)
    if identifier is not None:
        outcomes = Counter(
            "match" if a.result.matched else ("error" if a.result.error else "no match")
            for a in identifier.history
        )
        print(f"identification: {identifier.requests} requests, {dict(outcomes)}")
    return 0


def release_year(date: str | None) -> int | None:
    """'2013-05-28' -> 2013."""
    return int(date[:4]) if date and date[:4].isdigit() else None


def describe_lookup(lookup: Lookup) -> str:
    if lookup.release is None:
        return f"  discogs: {lookup.how}" + (f" ({lookup.error})" if lookup.error else "")
    rel = lookup.release
    track = find_track(lookup.tracks, lookup.track)
    where = f", {track.position}" if track else ""
    return (
        f"  discogs: {lookup.how} #{rel.get('id')} {rel.get('title')} ({rel.get('year') or '?'})"
        f"{where}"
    )


def describe_attempt(attempt: Attempt) -> str:
    r = attempt.result
    what = (
        f"{r.artist} - {r.title} ({r.album or 'no album'})"
        if r.matched
        else (f"error: {r.error}" if r.error else "no match")
    )
    return f"  [{attempt.t:6.1f} s] {r.service}: {what}   {r.latency_s:.2f} s"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Experiment 02 explorer.")
    parser.add_argument(
        "--wav", type=Path, help="replay this audio file (WAV, MP3, FLAC, OGG) instead of live"
    )
    parser.add_argument("--device", type=parse_device, help="input index or name")
    parser.add_argument("--samplerate", type=int, default=48_000, help="live only")
    parser.add_argument("--channels", type=int, default=2)
    parser.add_argument("--blocksize", type=int, default=512)
    parser.add_argument("--hop", type=int, default=512, help="analysis hop in frames")
    parser.add_argument(
        "--gate",
        type=float,
        default=DEFAULT_GATE_DB,
        help=f"trigger level gate in dBFS (default {DEFAULT_GATE_DB:g}; [ and ] adjust live)",
    )
    parser.add_argument("--no-gate", action="store_true", help="disable the trigger level gate")
    parser.add_argument("--fps", type=float, default=60.0)
    parser.add_argument("--minutes", type=float, help="stop after this long")
    parser.add_argument("--loop", action="store_true", help="loop the file")
    parser.add_argument(
        "--listen",
        action="store_true",
        help="also play the file through the speakers, in sync (files only; see D-011)",
    )
    parser.add_argument("--headless", action="store_true", help="no window; analyze and log")
    parser.add_argument("--offline", action="store_true", help="analyze --wav as fast as possible")
    parser.add_argument("--log", type=Path, help="JSON-lines path (default out/features-*.jsonl)")
    parser.add_argument("--no-log", action="store_true", help="do not write a log")
    ident = parser.add_argument_group("identification (experiment 03; keys in .env.local / .env)")
    ident.add_argument("--identify", choices=sorted(SERVICES), help="identify what is playing")
    ident.add_argument("--identify-clip", type=float, default=10.0, help="seconds per request")
    ident.add_argument("--identify-retry", type=float, default=8.0, help="seconds between tries")
    ident.add_argument(
        "--identify-recheck", type=float, default=30.0, help="seconds between checks after a match"
    )
    ident.add_argument("--max-identify", type=int, default=20, help="requests per session")
    ident.add_argument(
        "--discogs", action="store_true", help="look up each match on Discogs (experiment 04)"
    )
    args = parser.parse_args(argv)
    load_env()  # .env.local / .env: API keys and FR_AUDIO_DEVICE
    safe_output()
    if args.discogs and not args.identify:
        parser.error("--discogs needs --identify")
    if args.identify:
        if args.offline:
            parser.error("--identify works with live input or real-time replay, not --offline")
        args.service = SERVICES[args.identify].from_env()
        if args.service is None:
            parser.error(f"no {args.identify} keys found in .env.local or .env")
        if args.discogs:
            cache = ROOT / "samples" / "_local" / "discogs" / "http"
            args.discogs_client = Discogs.from_env(cache_dir=cache)
            if args.discogs_client is None:
                parser.error("DISCOGS_TOKEN is not set in .env.local or .env")
    if (args.loop or args.offline) and args.wav is None:
        parser.error("--loop and --offline need --wav")
    if args.listen and (args.wav is None or args.offline):
        parser.error("--listen needs --wav and real-time replay (never live input or --offline)")
    if args.no_gate:
        args.gate = None
    return run_offline(args) if args.offline else run_live(args)


if __name__ == "__main__":
    sys.exit(main())
