# 05 · Side tracking and level over

**Status:** not started · **Plan:** [docs/flightrec/experiments.md](../../docs/flightrec/experiments.md#05--side-tracking-and-level-over) · **Depends on:** 1.3, 2.1, 2.2

## Goal

State machine idle → needle_down → playing(track n) → run_out → side_end, combining Discogs durations, periodic re-fingerprinting, and local acoustic cues (needle drop, track gaps, run-out groove, needle lift).

## Success criteria

- Over 10+ full side playthroughs, `side_end` within a few seconds of the true end, no mid-side false positives
- Still reaches a sensible `side_end` with identification failing or durations missing
- Handles mid-side needle drops and 33 vs 45 rpm

## Install and run

_To be filled in by whoever builds this: setup steps, exact commands, how to run from a recorded WAV via `tools/replay.py`, and how to run live._

## Output

When finished, write `RESULTS.md` from [../RESULTS.template.md](../RESULTS.template.md) and add the decision to [docs/decisions.md](../../docs/decisions.md).
