"""Play a message log back at its original timing, printing each message as it is due.

Usage: python tools/play_log.py LOG.jsonl [--speed 2] [--type features --type side_end]
                                 [--every 0.5] [--summary]

Logs are JSON lines in the message contract (flightrec.messages), e.g. the explorer's
experiments/02-onset-features/out/features-*.jsonl. Unknown message types are skipped.
``--every`` thins high-rate messages (features) to at most one printed line per interval;
timing is unchanged. ``--summary`` skips playback and just counts what is in the log.
"""

from __future__ import annotations

import argparse
import os
import sys
from collections import Counter
from pathlib import Path

from flightrec.messages import FeaturesMessage, Message, read_log, replay


def describe(m: Message) -> str:
    if isinstance(m, FeaturesMessage):
        hits = "".join(
            c if flag else "."
            for c, flag in (("L", m.onset_low), ("M", m.onset_mid), ("H", m.onset_high))
        )
        tempo = f"{m.tempo:5.1f}" if m.tempo else "  ---"
        return (
            f"onset {'*' if m.onset else ' '} {hits}  beat {'*' if m.beat else ' '}  "
            f"bass {m.bass:.2f} mid {m.mid:.2f} treble {m.treble:.2f} rms {m.rms:.2f}  "
            f"tempo {tempo} ({m.tempo_confidence:.2f})  key {m.key or '-'}"
        )
    fields = {k: v for k, v in m.to_dict().items() if k not in ("v", "type", "t")}
    return " ".join(f"{k}={v}" for k, v in fields.items())


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("log", type=Path)
    parser.add_argument("--speed", type=float, default=1.0, help="2 = twice as fast")
    parser.add_argument("--type", action="append", dest="types", help="only these types")
    parser.add_argument(
        "--every", type=float, default=0.25, help="print features at most this often"
    )
    parser.add_argument("--summary", action="store_true", help="count only, no playback")
    args = parser.parse_args(argv)

    messages = [m for m in read_log(args.log) if not args.types or m.TYPE in args.types]
    if not messages:
        print("no messages")
        return 0
    span = messages[-1].t - messages[0].t
    counts = Counter(m.TYPE for m in messages)
    print(f"{args.log.name}: {len(messages)} messages over {span:.1f} s: {dict(counts)}")
    if args.summary:
        return 0

    t0 = messages[0].t
    last_printed: dict[str, float] = {}
    try:
        for m in replay(messages, speed=args.speed):
            is_event = isinstance(m, FeaturesMessage) and (m.onset or m.beat)
            if (
                isinstance(m, FeaturesMessage)
                and not is_event
                and m.t - last_printed.get(m.TYPE, -1e9) < args.every
            ):
                continue
            last_printed[m.TYPE] = m.t
            print(f"{m.t - t0:8.2f}  {m.TYPE:<22} {describe(m)}", flush=True)
    except KeyboardInterrupt:
        pass
    except OSError:
        # Output was closed early (e.g. piped into `head`); Windows reports EINVAL rather
        # than BrokenPipeError. Point stdout at devnull so the exit-time flush is quiet.
        os.dup2(os.open(os.devnull, os.O_WRONLY), sys.stdout.fileno())
    return 0


if __name__ == "__main__":
    sys.exit(main())
