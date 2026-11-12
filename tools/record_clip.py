"""Record N seconds from an input device to a WAV file, through the live capture path.

Usage: python tools/record_clip.py [--seconds 20] [--device NAME_OR_INDEX] [--out PATH]

Clips default to samples/_local/ (untracked). Keep test clips to 10-30 s and never
commit full tracks; see samples/README.md.
"""

from __future__ import annotations

import argparse
import sys
import time
from datetime import datetime
from pathlib import Path

import numpy as np

from flightrec.audio import Capture, LiveSource, env_device, write_wav

REPO_ROOT = Path(__file__).resolve().parent.parent
CLIP_LIMIT_SECONDS = 30.0
CLIP_THRESHOLD = 0.999
SILENCE_DBFS = -90.0  # far below any real input's noise floor; lower means no signal at all


def parse_device(value: str) -> int | str:
    return int(value) if value.isdigit() else value


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--seconds", type=float, default=20.0, help="length (default 20)")
    parser.add_argument(
        "--device", type=parse_device, default=None, help="index or name (default FR_AUDIO_DEVICE)"
    )
    parser.add_argument("--samplerate", type=int, default=48_000)
    parser.add_argument("--channels", type=int, default=2)
    parser.add_argument("--blocksize", type=int, default=512)
    parser.add_argument("--out", type=Path, help="output WAV (default samples/_local/clip-*.wav)")
    parser.add_argument("--float", action="store_true", help="write 32-bit float instead of 16-bit")
    args = parser.parse_args(argv)

    if args.seconds <= 0:
        parser.error("--seconds must be positive")
    if args.seconds > CLIP_LIMIT_SECONDS:
        print(
            f"warning: {args.seconds:g} s is longer than the {CLIP_LIMIT_SECONDS:g} s clip "
            "guideline; do not commit full tracks.",
            file=sys.stderr,
        )
    out = args.out or (
        REPO_ROOT / "samples" / "_local" / f"clip-{datetime.now():%Y%m%d-%H%M%S}.wav"
    )
    target = int(round(args.seconds * args.samplerate))

    source = LiveSource(
        args.device if args.device is not None else env_device(),
        args.samplerate,
        args.channels,
        args.blocksize,
    )
    # Ring holds the whole clip so every frame can be read back in order.
    capture = Capture(source, seconds=args.seconds + 1.0)
    print(f"Recording {args.seconds:g} s from device {source.device!r} ...", file=sys.stderr)
    with capture:
        while capture.frames < target:
            time.sleep(0.05)
    frames = capture.ring.read_from(0, target)

    write_wav(out, args.samplerate, frames, "float32" if args.float else "int16")
    peak = float(np.max(np.abs(frames)))
    clipped = int(np.count_nonzero(np.abs(frames) >= CLIP_THRESHOLD))
    peak_db = 20 * np.log10(peak) if peak > 0 else float("-inf")
    print(f"Wrote {out}")
    print(f"peak {peak_db:.1f} dBFS, clipped samples {clipped}, input overflows {source.overflows}")
    if peak_db < SILENCE_DBFS:
        print(
            f"warning: peak is below {SILENCE_DBFS:g} dBFS, so the clip is effectively silent. "
            "Check that the input is not muted, its level is up, and the device is the right one.",
            file=sys.stderr,
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
