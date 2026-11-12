"""Replay a WAV file through the same capture path as live input (nothing is played aloud).

Usage: python tools/replay.py CLIP.wav [--fast] [--loop] [--blocksize 512]

Blocks are delivered at real-time pace by default, exactly as an audio interface would,
into the same ring buffer live capture uses. Prints levels while it runs.
"""

from __future__ import annotations

import argparse
import time
from pathlib import Path

import numpy as np

from flightrec.audio import Capture, WavSource


def level_db(x: np.ndarray) -> tuple[float, float]:
    """RMS and peak of ``x`` in dBFS."""
    rms = float(np.sqrt(np.mean(np.square(x, dtype=np.float64))))
    peak = float(np.max(np.abs(x)))
    return tuple(20 * np.log10(v) if v > 0 else float("-inf") for v in (rms, peak))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("wav", type=Path, help="WAV file to replay")
    parser.add_argument("--blocksize", type=int, default=512)
    parser.add_argument("--channels", type=int, help="adapt channels (default: the file's)")
    parser.add_argument("--fast", action="store_true", help="as fast as possible, no pacing")
    parser.add_argument("--loop", action="store_true", help="repeat until Ctrl+C")
    parser.add_argument("--every", type=float, default=1.0, help="report interval in seconds")
    args = parser.parse_args(argv)
    if args.fast and args.loop:
        parser.error("--fast with --loop would never finish")

    source = WavSource(args.wav, args.blocksize, args.channels, not args.fast, args.loop)
    capture = Capture(source, seconds=max(args.every, 1.0))
    window = min(capture.ring.capacity, max(1, int(args.every * source.samplerate)))
    print(
        f"{args.wav.name}: {source.duration:.1f} s, {source.samplerate} Hz, "
        f"{source.channels} ch, blocks of {source.blocksize}"
    )

    started = time.monotonic()
    try:
        with capture:
            while not capture.wait(args.every):
                frames, end = capture.latest(window)
                rms, peak = level_db(frames)
                pos = (end / source.samplerate) % source.duration
                print(f"  {pos:7.1f} s   rms {rms:6.1f} dBFS   peak {peak:6.1f} dBFS")
    except KeyboardInterrupt:
        pass
    elapsed = time.monotonic() - started

    seconds = capture.frames / source.samplerate
    print(
        f"done: {capture.blocks} blocks, {seconds:.1f} s of audio in {elapsed:.1f} s, "
        f"max block lag {capture.max_lag * 1000:.1f} ms"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
