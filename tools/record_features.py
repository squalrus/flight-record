"""Record the real-time feature stream to a message log (JSON lines, message contract).

Usage (from the repo root, with the venv active):
    python tools/record_features.py --device 9 --minutes 5 --save-audio   # live
    python tools/record_features.py --wav song.mp3                        # file, real time
    python tools/record_features.py --wav song.mp3 --offline              # file, fast

Writes LOG.jsonl (one ``features`` message per analysis hop, about 94 per second) and
LOG.meta.json (source, sample rate, settings, flightrec version) to samples/_local/logs/
by default, which git ignores. ``--save-audio`` also writes the captured audio as
LOG.wav, so a live recording can be re-analyzed later with different settings.

Features depend only on the audio, so recording the same audio live, in real time, or
offline gives the same log message for message. Play a log back with tools/play_log.py.
"""

from __future__ import annotations

import argparse
import json
import sys
import time
from datetime import datetime
from importlib.metadata import PackageNotFoundError, version
from pathlib import Path

import numpy as np

from flightrec.audio import (
    DEFAULT_GATE_DB,
    Capture,
    FeatureExtractor,
    HopReader,
    open_source,
    read_audio,
    write_wav,
)
from flightrec.messages import LogWriter

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_DIR = REPO_ROOT / "samples" / "_local" / "logs"


def parse_device(value: str) -> int | str:
    return int(value) if value.isdigit() else value


def flightrec_version() -> str:
    # The repo's VERSION file, not package metadata: an editable install records its
    # version once, at install time, and goes stale as VERSION is bumped.
    try:
        return (REPO_ROOT / "VERSION").read_text(encoding="utf-8").strip()
    except OSError:
        try:
            return version("flightrec")
        except PackageNotFoundError:
            return "unknown"


def record_offline(path: Path, log: LogWriter, fx_args: dict) -> tuple[int, float, np.ndarray]:
    samplerate, frames = read_audio(path)
    fx = FeatureExtractor(samplerate, **fx_args)
    padded = np.concatenate([np.zeros((fx.window, frames.shape[1]), np.float32), frames])
    for end in range(fx.hop, len(frames) + 1, fx.hop):
        log.write(fx.process(padded[end : end + fx.window], end).to_message())
    return samplerate, len(frames) / samplerate, frames


def record_live(args: argparse.Namespace, log: LogWriter, fx_args: dict) -> dict:
    source = open_source(
        args.wav,
        device=args.device,
        samplerate=args.samplerate,
        channels=args.channels,
        blocksize=args.blocksize,
    )
    sr = source.samplerate
    fx = FeatureExtractor(sr, **fx_args)
    limit = args.minutes * 60 if args.minutes else None
    # Keep the whole recording in the ring when saving audio; otherwise a few seconds.
    keep_s = (limit or getattr(source, "duration", 0) or 0) + 2 if args.save_audio else 0
    capture = Capture(source, seconds=max(3.0, 2 * fx.window / sr, keep_s))
    reader = HopReader(capture, fx.window, fx.hop)
    if args.save_audio and keep_s <= 2:
        raise SystemExit("--save-audio with live input needs --minutes")
    # A file's last block is zero-padded to a full block (as an interface delivers), which
    # would add hops past the end of the audio; stop at the file's last real frame instead.
    file_frames = len(source.frames) if args.wav is not None else None  # type: ignore[attr-defined]

    def drain() -> None:
        for window, end in reader.pending():
            if file_frames is None or end <= file_frames:
                log.write(fx.process(window, end).to_message())

    started = time.perf_counter()
    try:
        with capture:
            while not source.finished and not (limit and time.perf_counter() - started >= limit):
                drain()
                time.sleep(0.005)
            if source.finished:
                capture.wait(0)  # re-raises a replay error, if any
    except KeyboardInterrupt:
        pass
    drain()  # capture has stopped: analyze every hop that arrived, so log and audio match
    frames = capture.frames if file_frames is None else min(capture.frames, file_frames)
    audio = capture.ring.read_from(0, frames) if args.save_audio else None
    return {
        "samplerate": sr,
        "seconds": frames / sr,
        "audio": audio,
        "skipped_hops": reader.skipped_hops,
        "overflows": source.overflows,
        "input_latency_ms": round(source.input_latency * 1000, 1),
        "device": None if args.wav else repr(getattr(source, "device", None)),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--wav", type=Path, help="record from this audio file instead of live")
    parser.add_argument("--device", type=parse_device, help="input index or name")
    parser.add_argument("--offline", action="store_true", help="analyze --wav as fast as possible")
    parser.add_argument("--minutes", type=float, help="stop after this long")
    parser.add_argument("--out", type=Path, help="log path (default samples/_local/logs/...)")
    parser.add_argument("--save-audio", action="store_true", help="also save the audio as .wav")
    parser.add_argument("--samplerate", type=int, default=48_000, help="live only")
    parser.add_argument("--channels", type=int, default=2)
    parser.add_argument("--blocksize", type=int, default=512)
    parser.add_argument("--hop", type=int, default=512)
    parser.add_argument("--gate", type=float, default=DEFAULT_GATE_DB, help="trigger gate, dBFS")
    args = parser.parse_args(argv)
    if args.offline and args.wav is None:
        parser.error("--offline needs --wav")

    stamp = f"{datetime.now():%Y%m%d-%H%M%S}"
    name = f"{args.wav.stem}-{stamp}" if args.wav else f"live-{stamp}"
    out = args.out or DEFAULT_DIR / f"{name}.jsonl"
    fx_args = {"hop": args.hop, "gate_db": args.gate}
    meta: dict = {
        "created": datetime.now().isoformat(timespec="seconds"),
        "flightrec_version": flightrec_version(),
        "source": str(args.wav) if args.wav else "live",
        "mode": "offline" if args.offline else "real-time",
        "hop": args.hop,
        "gate_db": args.gate,
        "message_rate_hz": None,
    }
    print(f"recording to {out}")
    with LogWriter(out) as log:
        if args.offline:
            sr, seconds, frames = record_offline(args.wav, log, fx_args)
            audio = frames if args.save_audio else None
            meta.update(samplerate=sr, seconds=round(seconds, 3))
        else:
            info = record_live(args, log, fx_args)
            audio = info.pop("audio")
            meta.update({k: v for k, v in info.items() if k != "seconds"})
            meta["seconds"] = round(info["seconds"], 3)
            sr = info["samplerate"]
        count = log.count
    meta["messages"] = count
    meta["message_rate_hz"] = round(sr / args.hop, 2)
    if audio is not None:
        wav_path = out.with_suffix(".wav")
        write_wav(wav_path, sr, audio, "float32")  # float: re-analysis is bit-for-bit faithful
        meta["audio"] = wav_path.name
        print(f"audio saved to {wav_path}")
    out.with_suffix(".meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")
    print(
        f"{count} messages, {meta['seconds']:.1f} s of audio"
        + (f", skipped {meta['skipped_hops']} hops" if "skipped_hops" in meta else "")
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
