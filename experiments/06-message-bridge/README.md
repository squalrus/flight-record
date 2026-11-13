# 06 · Message bridge to Unity

**Status:** not started · **Plan:** [docs/flightrec/experiments.md](../../docs/flightrec/experiments.md#06--message-bridge-to-unity) · **Depends on:** 0.3, 0.4

## Goal

Send the message contract from Python over UDP/OSC to a minimal Unity scene that spawns a sprite per onset, scales a bar per band, and shows track/side text. Measure end-to-end latency and jitter, and test Unity-native capture latency.

## Success criteria

- Spawns visibly in time with the music after a calibrated offset
- Jitter fully absorbed by the 150-300 ms delay buffer
- Sidecar vs C# port decision recorded in `docs/decisions.md` (D-101)

## Install and run

_To be filled in by whoever builds this: setup steps, exact commands, how to run from a recorded WAV via `tools/replay.py`, and how to run live._

## Output

When finished, write `RESULTS.md` from [../RESULTS.template.md](../RESULTS.template.md) and add the decision to [docs/decisions.md](../../docs/decisions.md).
