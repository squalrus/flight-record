# 02 · Onset and feature extraction

**Status:** not started · **Plan:** [docs/experiments.md](../../docs/experiments.md#02--onset-and-feature-extraction) · **Depends on:** 0.3 (message contract), 1.2 (test corpus)

## Goal

Spectral-flux onset detection, per-frame bass/mid/treble energy, rolling RMS, and a live autocorrelation tempo estimate with confidence. Emit features in the message-contract format. Compare against offline `librosa` as ground truth.

## Success criteria

- Onsets catch the large majority of audible beats on 10 varied clips, with an acceptable false-positive rate (record actual numbers)
- Tempo converges within ~10 s on steady material; low confidence on rubato/ambient
- Adaptive thresholds handle quiet-to-loud transitions without per-track tuning
- Real-time on a laptop at low CPU (Pi 3 B+ numbers as a secondary data point)

## Install and run

_To be filled in by whoever builds this: setup steps, exact commands, how to run from a recorded WAV via `tools/replay.py`, and how to run live._

## Output

When finished, write `RESULTS.md` from [../RESULTS.template.md](../RESULTS.template.md) and add the decision to [docs/decisions.md](../../docs/decisions.md).
