"""Experiment 06: latency and jitter from the sender's and the scene's timing logs.

Usage:
    python experiments/06-message-bridge/analyze.py                      # newest pair in out/
    python experiments/06-message-bridge/analyze.py --send out/send-X.csv --game out/game-Y.csv

Reports, as Markdown ready for RESULTS.md:

- **Stages** per message: analysis (sound to sent), transport (sent to received by the
  game), hold (received to shown on a frame, which includes the scene's delay buffer), and
  end to end (sound to shown).
- **Jitter**: each message's arrival relative to its audio time, minus the earliest such
  arrival. A delay buffer of D ms absorbs every message whose jitter is under D.
- **Native capture** (when the scene's microphone probe ran): for each clap the probe saw,
  how much later (positive) or earlier the same onset arrived over the bridge, both on the
  game's clock.

The sender's clock (``perf_counter()``) and the game's (``Stopwatch``) are lined up from
the clock samples the scene sends back: offset = median(game time - arrival time), which
is off by at most the one-way loopback time (well under a millisecond).
"""

from __future__ import annotations

import argparse
import csv
import math
import statistics
import sys
from collections.abc import Iterable, Sequence
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
LATE_DELAYS_MS = (0, 5, 10, 20, 50, 100, 150)


@dataclass(frozen=True)
class Sent:
    type: str
    t: float
    onset: bool
    event: float
    sent: float


@dataclass(frozen=True)
class Received:
    type: str
    t: float
    recv: float
    shown: float | None
    delay_ms: float
    due: float | None = None


def _num(value: str) -> float | None:
    return float(value) if value not in ("", None) else None


def read_send(path: Path) -> tuple[list[Sent], list[tuple[float, float]]]:
    """Messages sent (that left the socket) and (game time, arrival time) clock samples."""
    sent: list[Sent] = []
    clocks: list[tuple[float, float]] = []
    with path.open(newline="", encoding="utf-8") as fh:
        for row in csv.DictReader(fh):
            if row["kind"] == "clock":
                clocks.append((float(row["game"]), float(row["arrived"])))
            elif row["kind"] == "msg" and row["sent"]:
                sent.append(
                    Sent(
                        row["type"],
                        float(row["t"]),
                        row["onset"] == "1",
                        float(row["event"]),
                        float(row["sent"]),
                    )
                )
    return sent, clocks


def read_game(path: Path) -> tuple[list[Received], list[float]]:
    """Messages the scene received, and the times its microphone probe saw an onset."""
    received: list[Received] = []
    mic: list[float] = []
    with path.open(newline="", encoding="utf-8") as fh:
        for row in csv.DictReader(fh):
            if row["kind"] == "mic":
                mic.append(float(row["shown"]))
            elif row["kind"] == "msg":
                received.append(
                    Received(
                        row["type"],
                        float(row["t"]),
                        float(row["recv"]),
                        _num(row["shown"]),
                        float(row["delay_ms"] or 0),
                        _num(row.get("due", "")),
                    )
                )
    return received, mic


def clock_offset(clocks: Sequence[tuple[float, float]]) -> tuple[float, float]:
    """(median, spread) of game time minus sender time; spread is the interquartile range."""
    if not clocks:
        raise ValueError("no clock samples: was the scene running and replying?")
    diffs = sorted(game - arrived for game, arrived in clocks)
    if len(diffs) < 4:
        return statistics.median(diffs), 0.0
    q = statistics.quantiles(diffs, n=4)
    return statistics.median(diffs), q[2] - q[0]


def percentile(values: Sequence[float], p: float) -> float:
    """Nearest-rank percentile (0..100) of ``values``, which must not be empty."""
    ordered = sorted(values)
    rank = max(0, min(len(ordered) - 1, math.ceil(p / 100 * len(ordered)) - 1))
    return ordered[rank]


def summarize(values: Sequence[float]) -> dict[str, float]:
    """Percentiles in milliseconds of values given in seconds."""
    ms = [v * 1000 for v in values]
    return {
        "n": len(ms),
        "min": min(ms),
        "p50": percentile(ms, 50),
        "p95": percentile(ms, 95),
        "p99": percentile(ms, 99),
        "max": max(ms),
    }


def key(type_: str, t: float) -> tuple[str, float]:
    return type_, round(t, 4)


def jitter(received: Iterable[Received]) -> list[float]:
    """Arrival (receive time minus audio time) minus the earliest arrival, in seconds."""
    arrival = [r.recv - r.t for r in received]
    if not arrival:
        return []
    base = min(arrival)
    return [a - base for a in arrival]


def late_fraction(jitters: Sequence[float], delays_ms: Iterable[float]) -> dict[float, float]:
    """Share of messages a delay buffer of each size would still show late."""
    n = len(jitters) or 1
    return {d: sum(j * 1000 > d + 1e-6 for j in jitters) / n for d in delays_ms}


def pair_nearest(
    probe: Sequence[float], bridge: Sequence[float], window: float = 0.3
) -> list[float]:
    """For each probe time, the nearest bridge time within ``window`` s, as bridge - probe."""
    out: list[float] = []
    for p in probe:
        best = min(bridge, key=lambda b: abs(b - p), default=None)
        if best is not None and abs(best - p) <= window:
            out.append(best - p)
    return out


def stage_rows(sent: Sequence[Sent], received: Sequence[Received], offset: float) -> dict:
    """Per-stage latencies for every message received, and for onsets only."""
    by_key = {key(s.type, s.t): s for s in sent}
    stages: dict[str, dict[str, list[float]]] = {"all": {}, "onsets": {}}
    for r in received:
        s = by_key.get(key(r.type, r.t))
        if s is None:
            continue
        values = {
            "analysis": s.sent - s.event,
            "transport": (r.recv - offset) - s.sent,
        }
        if r.shown is not None:
            values["hold"] = r.shown - r.recv
            values["end to end"] = (r.shown - offset) - s.event
        if r.shown is not None and r.due is not None:
            values["shown - due"] = r.shown - r.due
        groups = ["all", "onsets"] if s.onset else ["all"]
        for group in groups:
            for name, v in values.items():
                stages[group].setdefault(name, []).append(v)
    return stages


def within_run(
    sent: Sequence[Sent],
    offset: float,
    received: Sequence[Received],
    mic: Sequence[float],
    margin: float = 2.0,
) -> tuple[list[Received], list[float]]:
    """Keep only what the scene logged while this sender ran.

    One scene session can span several sender runs (one song after another), and every
    file starts at t = 0, so matching by (type, t) alone would mix runs. ``offset`` is game
    time minus sender time.
    """
    if not sent:
        return list(received), list(mic)
    start = min(s.sent for s in sent) + offset - margin
    end = max(s.sent for s in sent) + offset + margin
    return (
        [r for r in received if start <= r.recv <= end],
        [m for m in mic if start <= m <= end],
    )


def table(rows: dict[str, dict[str, float]]) -> str:
    lines = [
        "| Stage | n | min | p50 | p95 | p99 | max |",
        "| --- | --: | --: | --: | --: | --: | --: |",
    ]
    for name, s in rows.items():
        lines.append(
            f"| {name} | {s['n']:.0f} | {s['min']:.2f} | {s['p50']:.2f} | {s['p95']:.2f} "
            f"| {s['p99']:.2f} | {s['max']:.2f} |"
        )
    return "\n".join(lines)


def newest(pattern: str) -> Path:
    found = sorted((HERE / "out").glob(pattern), key=lambda p: p.stat().st_mtime)
    if not found:
        raise SystemExit(f"no {pattern} in {HERE / 'out'}; pass --send and --game")
    return found[-1]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--send", type=Path, help="sender CSV (default: newest out/send-*.csv)")
    parser.add_argument("--game", type=Path, help="scene CSV (default: newest out/game-*.csv)")
    args = parser.parse_args(argv)
    send_path = args.send or newest("send-*.csv")
    game_path = args.game or newest("game-*.csv")
    sent, clocks = read_send(send_path)
    received, mic = read_game(game_path)

    print(f"sender log: {send_path.name}, game log: {game_path.name}\n")
    offset, spread = clock_offset(clocks)
    received, mic = within_run(sent, offset, received, mic)
    lost = len(sent) - len({key(r.type, r.t) for r in received})
    delays = sorted({r.delay_ms for r in received})
    print(
        f"- Messages: {len(sent)} sent, {len(received)} received, {max(lost, 0)} lost; "
        f"scene delay setting(s): {', '.join(f'{d:g} ms' for d in delays) or 'n/a'}"
    )
    print(
        f"- Clock offset (game - sender): {offset * 1000:+.3f} ms, "
        f"IQR {spread * 1000:.3f} ms over {len(clocks)} samples"
    )
    if received:
        span = received[-1].recv - received[0].recv
        print(f"- Received over {span:.1f} s: {len(received) / max(span, 1e-9):.1f} messages/s")

    stages = stage_rows(sent, received, offset)
    for group, title in (("all", "All messages"), ("onsets", "Onset messages only")):
        if stages[group]:
            print(f"\n**{title}** (ms)\n")
            print(table({name: summarize(v) for name, v in stages[group].items()}))

    features = [r for r in received if r.type == "features"]
    jit = jitter(features)
    if jit:
        print("\n**Jitter** (features arrival minus the earliest arrival, ms)\n")
        print(table({"jitter": summarize(jit)}))
        late = late_fraction(jit, LATE_DELAYS_MS)
        print("\n| Delay buffer (ms) | " + " | ".join(f"{d:g}" for d in late) + " |")
        print("| --- |" + " --: |" * len(late))
        print("| Late | " + " | ".join(f"{v:.2%}" for v in late.values()) + " |")

    if mic:
        onset_keys = {key(s.type, s.t) for s in sent if s.onset}
        bridge_onsets = [r.recv for r in features if key(r.type, r.t) in onset_keys]
        diffs = pair_nearest(mic, bridge_onsets)
        print(
            f"\n**Native capture vs bridge** ({len(mic)} probe onsets, {len(diffs)} matched; "
            "positive = bridge later than Unity's Microphone, ms)\n"
        )
        if diffs:
            print(table({"bridge - native": summarize(diffs)}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
