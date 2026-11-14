"""Write the synthesized reference song (``flightrec.reference_song``) as a WAV plus its truth.

Usage (from the repo root, with the venv active):
    python tools/make_reference_song.py                          # 128 BPM, A minor
    python tools/make_reference_song.py --bpm 100 --key "F major" --out samples/_local/ref.wav

Writes ``OUT`` (16-bit stereo WAV) and ``OUT`` with ``.json`` in place of ``.wav``: the
exact tempo, key, beat times, section boundaries, and every note's start time. Play it to
the scene like any file: ``python experiments/06-message-bridge/send.py --wav OUT --listen``.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from flightrec.audio import write_wav
from flightrec.reference_song import render

ROOT = Path(__file__).resolve().parents[1]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--bpm", type=float, default=128.0)
    parser.add_argument("--key", default="A minor", help='e.g. "A minor", "Bb major"')
    parser.add_argument("--samplerate", type=int, default=44_100)
    parser.add_argument("--out", type=Path, help="WAV path (default samples/_local/)")
    args = parser.parse_args(argv)

    frames, truth = render(args.bpm, args.key, samplerate=args.samplerate)
    name = f"reference-{args.bpm:g}bpm-{truth.key.replace(' ', '-')}.wav"
    out = args.out or ROOT / "samples" / "_local" / name
    write_wav(out, truth.samplerate, frames)
    out.with_suffix(".json").write_text(json.dumps(truth.to_dict(), indent=1), encoding="utf-8")
    print(f"wrote {out} ({truth.length:.1f} s, {truth.bpm:g} BPM, {truth.key}) and its .json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
