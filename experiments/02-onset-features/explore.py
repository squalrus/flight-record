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

--offline analyzes a WAV as fast as possible with no window (same results as replay,
since analysis depends only on the audio). Esc or Q closes the window.
"""

from __future__ import annotations

import argparse
import json
import sys
import time
from collections import Counter, deque
from datetime import datetime
from pathlib import Path
from typing import IO, Any

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

HERE = Path(__file__).resolve().parent
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

    def __init__(self, title: str, channels: int, freqs: np.ndarray) -> None:
        from flightrec import viz

        self.viz = viz
        self.win = viz.Window(title, (self.W, self.H))
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
        self, f: Features, frames: np.ndarray, beat: bool, stats: list[str], status: list[str]
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
        self.viz.status_lines(win, status, self.H - 56)
        win.end()


def open_log(args: argparse.Namespace) -> IO[str] | None:
    if args.no_log:
        return None
    path = args.log or HERE / "out" / f"features-{datetime.now():%Y%m%d-%H%M%S}.jsonl"
    path.parent.mkdir(parents=True, exist_ok=True)
    print(f"logging to {path}")
    return path.open("w", encoding="utf-8")


def write(log: IO[str] | None, f: Features) -> None:
    if log is not None:
        log.write(json.dumps(f.message(), separators=(",", ":")) + "\n")


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
    capture = Capture(source, seconds=max(3.0, 2 * fx.window / sr))
    reader = HopReader(capture, fx.window, fx.hop)
    hop_s = fx.hop / sr
    summary = Summary()
    log = open_log(args)
    desc = f"replay {args.wav.name}" if args.wav else f"live device {source.device!r}"  # type: ignore[attr-defined]
    view = (
        None
        if args.headless
        else Explorer(f"Flight Record · 02 · {desc}", source.channels, fx.freqs)
    )
    print(f"{desc}: {sr} Hz, hop {fx.hop} ({hop_s * 1000:.1f} ms), window {fx.window}")

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
                    view.draw(latest, frames, beat_seen, stats, status)
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
        if log is not None:
            log.close()
    summary.report(capture.frames / sr, hop_s, reader.skipped_hops)
    return 0


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
    args = parser.parse_args(argv)
    if (args.loop or args.offline) and args.wav is None:
        parser.error("--loop and --offline need --wav")
    if args.listen and (args.wav is None or args.offline):
        parser.error("--listen needs --wav and real-time replay (never live input or --offline)")
    if args.no_gate:
        args.gate = None
    return run_offline(args) if args.offline else run_live(args)


if __name__ == "__main__":
    sys.exit(main())
