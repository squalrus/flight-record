"""Experiment 04: from artist and album (and the identified track) to a Discogs release,
its vinyl sides, and their durations.

Usage (from the repo root, with the venv active; DISCOGS_TOKEN in .env.local or .env):
    python experiments/04-discogs-lookup/lookup.py --artist Kalax --album III --track Calling
    python experiments/04-discogs-lookup/lookup.py --songs samples/_songs           # batch, tags
    python experiments/04-discogs-lookup/lookup.py --songs samples/_songs --confirm # ask once

Flow per record: a remembered choice wins; otherwise search (artist + album, then artist +
track, then free text), score the results (title, artist, vinyl, year), and pick
automatically when one clearly wins. When it is ambiguous, ``--confirm`` lists the top
candidates and asks once: a number, or a catalog number or barcode from the sleeve.
Confirmed and automatic choices are remembered, so a known record never runs the lookup
flow again. Without ``--confirm`` the top result is used and flagged "unconfirmed".

API responses are cached under samples/_local/discogs/ (git-ignored).
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from flightrec.console import safe_output
from flightrec.discogs import Candidate, Discogs, Lookup, Selections, find_track, resolve
from flightrec.env import load_env
from flightrec.messages import LogWriter
from flightrec.tags import read_tags

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "samples" / "_local" / "discogs"
AUDIO_SUFFIXES = {".mp3", ".flac", ".ogg", ".wav"}


def ask(candidates: list[Candidate]) -> Candidate | str | None:
    """Interactive confirmation: a number, a catalog number / barcode, Enter = #1, s = skip."""
    print("  Which pressing is it?")
    for i, c in enumerate(candidates, 1):
        print(f"    {i}. {c.describe()}")
    answer = input("  number, catalog no. or barcode from the sleeve [Enter = 1, s = skip]: ")
    answer = answer.strip()
    if not answer:
        return candidates[0]
    if answer.lower() == "s":
        return None
    if answer.isdigit() and 1 <= int(answer) <= len(candidates):
        return candidates[int(answer) - 1]
    return answer


def mmss(seconds: float | None) -> str:
    if seconds is None:
        return "  ?:??"
    return f"{int(seconds // 60):3d}:{int(seconds % 60):02d}"


def report(result: Lookup) -> None:
    query = f"{result.artist} / {result.album or '?'}" + (
        f" / {result.track}" if result.track else ""
    )
    print(f"\n{query}")
    if result.release is None:
        print(f"  {result.how}" + (f": {result.error}" if result.error else ""))
        return
    r = result.release
    formats = ", ".join(f.get("name", "") for f in r.get("formats") or [])
    labels = ", ".join(
        f"{lab.get('name', '?')} {lab.get('catno', '')}".strip() for lab in r.get("labels") or []
    )
    how = f"{result.how} (same master)" if result.via_master else result.how
    print(f"  {how}: #{r.get('id')} {r.get('title')} ({r.get('year') or '?'}, "
          f"{r.get('country') or '?'}) [{formats}] {labels}")  # fmt: skip
    if result.filled_from_master:
        print(f"  {result.filled_from_master} durations filled from the master release")
    about = " > ".join(x for x in (", ".join(result.genres), ", ".join(result.styles)) if x)
    community = f"   have {result.have} / want {result.want}" if result.have is not None else ""
    print(f"  {about or 'no genres'}   year {result.year or '?'}{community}")
    if result.barcodes or result.matrix:
        print(f"  barcode {', '.join(result.barcodes) or '-'}   matrix/runout: "
              f"{'; '.join(result.matrix) or '-'}")  # fmt: skip
    if not result.side_lengths:
        print(f"  no vinyl sides ({len(result.tracks)} tracks: CD or digital positions)")
    for side, length in result.side_lengths.items():
        note = " (estimated)" if length.estimated else ""
        print(f"  side {side}: {length.total} tracks, {mmss(length.seconds)}"
              f"  durations {length.known}/{length.total}{note}")  # fmt: skip
    if result.track:
        hit = find_track(result.tracks, result.track)
        if hit is None:
            where = "not in this tracklist"
        elif hit.side is None:
            where = f"track {hit.position or '?'} (no vinyl side)"
        else:
            where = f"side {hit.side}, {hit.position} ({mmss(hit.seconds).strip()})"
        print(f"  '{result.track}': {where}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Experiment 04: Discogs release lookup.")
    parser.add_argument("--artist")
    parser.add_argument("--album")
    parser.add_argument("--track", help="the identified track, to find its side")
    parser.add_argument("--songs", type=Path, help="look up every song file's tags")
    parser.add_argument("--confirm", action="store_true", help="ask when the choice is unclear")
    parser.add_argument("--any-format", action="store_true", help="do not prefer vinyl")
    parser.add_argument("--log", type=Path, help="write release_selected messages here")
    parser.add_argument("--forget", action="store_true", help="ignore remembered choices")
    args = parser.parse_args(argv)
    safe_output()
    if not args.songs and not args.artist:
        parser.error("give --artist (with --album and/or --track) or --songs DIR")

    load_env()
    discogs = Discogs.from_env(cache_dir=DATA / "http")
    if discogs is None:
        parser.error("DISCOGS_TOKEN is not set in .env.local or .env")
    selections = Selections(
        DATA / ("selections-scratch.json" if args.forget else "selections.json")
    )

    queries: list[tuple[str, str | None, str | None]] = []
    if args.songs:
        for path in sorted(p for p in args.songs.iterdir() if p.suffix.lower() in AUDIO_SUFFIXES):
            tags = read_tags(path)
            if tags.artist:
                queries.append((tags.artist, tags.album, tags.title))
    else:
        queries.append((args.artist, args.album, args.track))

    log = LogWriter(args.log) if args.log else None
    tally: dict[str, int] = {}
    try:
        for artist, album, track in queries:
            result = resolve(
                discogs, selections, artist, album, track,
                confirm=ask if args.confirm else None, prefer_vinyl=not args.any_format,
            )  # fmt: skip
            report(result)
            tally[result.how] = tally.get(result.how, 0) + 1
            if log is not None and result.message is not None:
                log.write(result.message)
    finally:
        if log is not None:
            log.close()
    print(f"\n{len(queries)} lookups: {tally}; {discogs.requests} API requests, "
          f"{discogs.cache_hits} from cache")  # fmt: skip
    return 0


if __name__ == "__main__":
    sys.exit(main())
