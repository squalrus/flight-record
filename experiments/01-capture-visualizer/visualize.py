"""Experiment 01: capture and visualize, with stability and latency logging.

Shows a live waveform, log-frequency spectrum, and level meters from live input or a WAV,
and logs one CSV row per second (overflows, block gaps, clock drift, latency, fps, levels).

Usage (from the repo root, with the venv active):
    python experiments/01-capture-visualizer/visualize.py                  # live, FR_AUDIO_DEVICE
    python experiments/01-capture-visualizer/visualize.py --device 9
    python experiments/01-capture-visualizer/visualize.py --wav clip.wav   # replay a recording
    python experiments/01-capture-visualizer/visualize.py --headless --minutes 30

Esc or Q closes the window. Ctrl+C stops a headless run.

Latency is measured per displayed frame as
    driver-reported input latency + (time the frame was shown - arrival of newest block).
The block's arrival time is read before its frames, so a block arriving in between makes
the number slightly high, never low. Monitor scan-out (up to one refresh) is not included.
"""

from __future__ import annotations

import argparse
import csv
import sys
import time
from datetime import datetime
from pathlib import Path
from typing import Any

import numpy as np

from flightrec.audio import (
    FLOOR_DB,
    AudioSource,
    Capture,
    LogBands,
    Spectrum,
    channel_levels,
    open_source,
)

HERE = Path(__file__).resolve().parent
CLIP_DBFS = -0.1
LOG_FIELDS = [
    "elapsed_s",
    "audio_s",
    "blocks",
    "overflows",
    "max_gap_ms",
    "drift_ms",
    "lat_p50_ms",
    "lat_p95_ms",
    "lat_max_ms",
    "fps",
    "late_frames",
    "peak_dbfs",
    "clip_frames",
]


def parse_device(value: str) -> int | str:
    return int(value) if value.isdigit() else value


def sleep_until(due: float) -> None:
    while (delay := due - time.perf_counter()) > 0:
        time.sleep(delay)


def describe(source: AudioSource) -> str:
    path = getattr(source, "path", None)
    if path is not None:
        return f"replay {path.name}"
    return f"live device {getattr(source, 'device', None)!r}"


class Stats:
    """Per-frame measurements, summarized once per interval and for the whole run."""

    def __init__(self, frame_period: float) -> None:
        self.late_threshold = 1.5 * frame_period
        self.all_latency: list[float] = []
        self.total_frames = 0
        self.total_late = 0
        self.total_clip_frames = 0
        self.max_gap = 0.0
        self.prev_shown: float | None = None
        self.last: dict[str, Any] = {}
        self._reset_interval()

    def _reset_interval(self) -> None:
        self.latency: list[float] = []
        self.late = 0
        self.peak = FLOOR_DB
        self.clip_frames = 0

    def add_frame(self, shown: float, latency_ms: float, peak_db: float) -> None:
        if self.prev_shown is not None and shown - self.prev_shown > self.late_threshold:
            self.late += 1
            self.total_late += 1
        self.prev_shown = shown
        self.latency.append(latency_ms)
        self.all_latency.append(latency_ms)
        self.total_frames += 1
        self.peak = max(self.peak, peak_db)
        if peak_db >= CLIP_DBFS:
            self.clip_frames += 1
            self.total_clip_frames += 1

    def close_interval(self, elapsed: float, interval: float, capture: Capture) -> dict[str, Any]:
        lat = np.array(self.latency) if self.latency else np.array([np.nan])
        gap = capture.pop_max_gap()
        self.max_gap = max(self.max_gap, gap)
        row = {
            "elapsed_s": round(elapsed, 3),
            "audio_s": round(capture.frames / capture.samplerate, 3),
            "blocks": capture.blocks,
            "overflows": capture.source.overflows,
            "max_gap_ms": round(gap * 1000, 2),
            "drift_ms": round(capture.lag * 1000, 2),
            "lat_p50_ms": round(float(np.percentile(lat, 50)), 2),
            "lat_p95_ms": round(float(np.percentile(lat, 95)), 2),
            "lat_max_ms": round(float(np.max(lat)), 2),
            "fps": round(len(self.latency) / interval, 1),
            "late_frames": self.late,
            "peak_dbfs": round(self.peak, 1),
            "clip_frames": self.clip_frames,
        }
        self.last = row
        self._reset_interval()
        return row


class Renderer:
    """Experiment 01 layout over the shared panels in ``flightrec.viz`` (imports pygame)."""

    W, H = 1100, 660

    def __init__(self, title: str, bands: LogBands, channels: int) -> None:
        from flightrec import viz

        self.viz = viz
        self.win = viz.Window(title, (self.W, self.H))
        self.wave = viz.WaveformPanel(self.win, (16, 16, self.W - 172, 150))
        self.spec = viz.SpectrumPanel(self.win, (56, 196, self.W - 212, 340), bands)
        self.meters = viz.MeterPanel(self.win, (self.W - 140, 16, 124, 520), channels, CLIP_DBFS)

    def closed(self) -> bool:
        return self.win.closed()

    def close(self) -> None:
        self.win.close()

    def draw(
        self,
        frames: np.ndarray,
        band_db: np.ndarray,
        ch_rms: np.ndarray,
        ch_peak: np.ndarray,
        status: list[str],
    ) -> None:
        self.win.begin()
        self.wave.draw(frames)
        self.spec.draw(band_db)
        self.meters.draw(ch_rms, ch_peak)
        self.viz.status_lines(self.win, status, self.H - 64)
        self.win.end()


def run(args: argparse.Namespace) -> int:
    source = open_source(
        args.wav,
        device=args.device,
        samplerate=args.samplerate,
        channels=args.channels,
        blocksize=args.blocksize,
        loop=args.loop,
    )
    sr = source.samplerate
    capture = Capture(source, seconds=max(2.0, 4 * args.fft / sr))
    spectrum = Spectrum(args.fft, sr)
    bands = LogBands(spectrum.freqs, args.bands, 20.0, min(20_000.0, sr / 2))
    window = np.empty((args.fft, source.channels), dtype=np.float32)
    period = 1.0 / args.fps
    stats = Stats(period)
    desc = describe(source)

    log_file = None
    writer = None
    if not args.no_log:
        log_path = args.log or HERE / "out" / f"run-{datetime.now():%Y%m%d-%H%M%S}.csv"
        log_path.parent.mkdir(parents=True, exist_ok=True)
        log_file = log_path.open("w", newline="")
        writer = csv.DictWriter(log_file, fieldnames=LOG_FIELDS)
        writer.writeheader()
        print(f"logging to {log_path}")

    view = (
        None if args.headless else Renderer(f"Flight Record · 01 · {desc}", bands, source.channels)
    )
    print(
        f"{desc}: {sr} Hz, {source.channels} ch, block {source.blocksize} "
        f"({1000 * source.blocksize / sr:.1f} ms), FFT {args.fft} ({1000 * args.fft / sr:.0f} ms)"
    )

    limit = args.minutes * 60 if args.minutes else None
    started = time.perf_counter()
    try:
        with capture:
            next_frame = next_log = started
            next_log += 1.0
            while True:
                now = time.perf_counter()
                if view is not None and view.closed():
                    break
                if source.finished or (limit is not None and now - started >= limit):
                    break
                t_block = capture.last_t
                if t_block is None:
                    time.sleep(0.001)
                    continue

                frames, _ = capture.latest(args.fft, window)
                band_db = bands.compute(spectrum.compute(frames))
                ch_rms, ch_peak = channel_levels(frames)
                if view is not None:
                    last = stats.last
                    status = [
                        f"{desc}   {sr} Hz   block {source.blocksize}   FFT {args.fft}   "
                        f"interface latency {source.input_latency * 1000:.1f} ms",
                        f"latency p50 {last.get('lat_p50_ms', 0):.1f} / p95 "
                        f"{last.get('lat_p95_ms', 0):.1f} ms   overflows {source.overflows}   "
                        f"max gap {last.get('max_gap_ms', 0):.1f} ms   drift "
                        f"{capture.lag * 1000:+.1f} ms   fps {last.get('fps', 0):.0f}   "
                        f"{now - started:6.0f} s",
                    ]
                    view.draw(frames, band_db, ch_rms, ch_peak, status)
                shown = time.perf_counter()
                latency_ms = (shown - t_block + source.input_latency) * 1000
                stats.add_frame(shown, latency_ms, float(np.max(ch_peak)))

                if shown >= next_log:
                    row = stats.close_interval(shown - started, 1.0, capture)
                    if writer is not None:
                        writer.writerow(row)
                        log_file.flush()
                    next_log += 1.0

                next_frame += period
                if next_frame < time.perf_counter() - period:  # fell behind: resync
                    next_frame = time.perf_counter()
                sleep_until(next_frame)
            if source.finished:
                capture.wait(0)  # re-raises a replay error, if any
    except KeyboardInterrupt:
        pass
    finally:
        if view is not None:
            view.close()
        if log_file is not None:
            log_file.close()

    elapsed = time.perf_counter() - started
    lat = np.array(stats.all_latency) if stats.all_latency else np.array([np.nan])
    print(
        f"done: {elapsed:.1f} s, {capture.frames / sr:.1f} s of audio, {capture.blocks} blocks, "
        f"overflows {source.overflows}, max block gap {stats.max_gap * 1000:.1f} ms, "
        f"drift {capture.lag * 1000:+.1f} ms"
    )
    print(
        f"latency ms: p50 {np.percentile(lat, 50):.1f}, p95 {np.percentile(lat, 95):.1f}, "
        f"max {np.max(lat):.1f}  (interface {source.input_latency * 1000:.1f})   "
        f"frames {stats.total_frames}, late {stats.total_late}, clipped {stats.total_clip_frames}"
    )
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Experiment 01: capture and visualize.")
    parser.add_argument(
        "--wav", type=Path, help="replay this audio file (WAV, MP3, FLAC, OGG) instead of live"
    )
    parser.add_argument("--device", type=parse_device, help="input index or name")
    parser.add_argument("--samplerate", type=int, default=48_000, help="live only")
    parser.add_argument("--channels", type=int, default=2)
    parser.add_argument("--blocksize", type=int, default=512)
    parser.add_argument("--fft", type=int, default=2048, help="FFT size (power of two)")
    parser.add_argument("--bands", type=int, default=96, help="spectrum bars")
    parser.add_argument("--fps", type=float, default=60.0)
    parser.add_argument("--minutes", type=float, help="stop after this long")
    parser.add_argument("--loop", action="store_true", help="loop the WAV")
    parser.add_argument("--headless", action="store_true", help="no window; analyze and log")
    parser.add_argument("--log", type=Path, help="CSV path (default out/run-*.csv)")
    parser.add_argument("--no-log", action="store_true", help="do not write a CSV")
    args = parser.parse_args(argv)
    if args.loop and args.wav is None:
        parser.error("--loop needs --wav")
    return run(args)


if __name__ == "__main__":
    sys.exit(main())
