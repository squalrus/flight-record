# 03 · Fingerprint a clip

**Status:** not started · **Plan:** [docs/experiments.md](../../docs/experiments.md#03--fingerprint-a-clip) · **Depends on:** 1.2 (clips), API keys in `.env`

## Goal

Send 10-15 s clips from 20+ records to one or two recognition services (ACRCloud, AudD; ShazamKit only with a suitable client path). Log artist/title/album, confidence, latency, correctness.

## Success criteria

- Hit rate and median time-to-identify per genre and record condition
- A decision on service, clip length, retry cadence, and the no-match behavior

## Install and run

_To be filled in by whoever builds this: setup steps, exact commands, how to run from a recorded WAV via `tools/replay.py`, and how to run live._

## Output

When finished, write `RESULTS.md` from [../RESULTS.template.md](../RESULTS.template.md) and add the decision to [docs/decisions.md](../../docs/decisions.md).
