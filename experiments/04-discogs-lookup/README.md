# 04 · Discogs release lookup

**Status:** not started · **Plan:** [docs/experiments.md](../../docs/experiments.md#04--discogs-release-lookup) · **Depends on:** `DISCOGS_TOKEN` in `.env`

## Goal

From artist and album, find the pressing, parse vinyl positions (A1, A2, B1…) into sides, compute side durations where present, handle multiple pressings with a one-time player confirmation, and cache locally.

## Success criteria

- 20-record set resolves to the correct release automatically or with one confirmation
- Documented fallback for missing durations
- Local cache prevents repeat lookups

## Install and run

_To be filled in by whoever builds this: setup steps, exact commands, how to run from a recorded WAV via `tools/replay.py`, and how to run live._

## Output

When finished, write `RESULTS.md` from [../RESULTS.template.md](../RESULTS.template.md) and add the decision to [docs/decisions.md](../../docs/decisions.md).
