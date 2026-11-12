# 01 · Capture and visualize

**Status:** not started · **Plan:** [docs/experiments.md](../../docs/experiments.md#01--capture-and-visualize) · **Depends on:** 0.2 (replay tooling)

## Goal

Capture stereo line-in with `sounddevice` into a ring buffer, run an FFT every ~16 ms, draw a live spectrum and waveform, and log capture-to-display latency.

## Success criteria

- Stable capture for 30+ minutes with no dropouts
- Spectrum visibly tracks the music; no clipping or hum visible
- Measured capture-to-display latency documented (target: under 50 ms)

## Install and run

_To be filled in by whoever builds this: setup steps, exact commands, how to run from a recorded WAV via `tools/replay.py`, and how to run live._

## Output

When finished, write `RESULTS.md` from [../RESULTS.template.md](../RESULTS.template.md) and add the decision to [docs/decisions.md](../../docs/decisions.md).
