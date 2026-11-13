# 01 · Capture and visualize

**Status:** in progress. The code runs live and from WAV; measurements still need the real hardware chain. · **Plan:** [docs/flightrec/experiments.md](../../docs/flightrec/experiments.md#01--capture-and-visualize) · **Depends on:** 0.2 (replay tooling)

## Goal

Capture stereo line-in with `sounddevice` into a ring buffer, run an FFT every ~16 ms, draw a live spectrum and waveform, and log capture-to-display latency.

## Success criteria

- Stable capture for 30+ minutes with no dropouts
- Spectrum visibly tracks the music; no clipping or hum visible
- Measured capture-to-display latency documented (target: under 50 ms)

## Install and run

From the repo root, with the venv active (see [../README.md](../README.md#setup)):

```sh
python -m pip install -r experiments/01-capture-visualizer/requirements.txt   # adds pygame

python tools/list_devices.py                                       # pick an input
python experiments/01-capture-visualizer/visualize.py --device 9   # live window
python experiments/01-capture-visualizer/visualize.py --wav samples/_local/clip.wav --loop
python experiments/01-capture-visualizer/visualize.py --device 9 --headless --minutes 30
```

The device defaults to `FR_AUDIO_DEVICE`. Esc or Q closes the window, and Ctrl+C stops a headless run. `--headless` needs no Pygame and no display. Each run prints a summary and writes one CSV row per second to `out/run-*.csv`, which is untracked (`--log PATH` to choose, `--no-log` to skip).

Useful options: `--fft 8192` gives about 6 Hz bins, enough to tell 50 Hz from 60 Hz hum; the default 2048 has 23 Hz bins but reacts faster. `--blocksize 256` gives smaller blocks and lower latency. `--fps` sets the redraw rate.

## What the window shows

- **Waveform:** the latest FFT window per channel (L cyan, R magenta).
- **Spectrum:** 96 log-spaced bands from 20 Hz to 20 kHz in dBFS, with a falling peak-hold line. Orange ticks mark 50 and 60 Hz, for spotting mains hum.
- **Meters:** peak fill per channel, a white tick for RMS, and a clip light held for 1 s when the peak reaches -0.1 dBFS.
- **Status:** interface latency, latency p50/p95 over the last second, overflows, longest block gap, clock drift, fps.

## What gets measured

| CSV column | Meaning | For criterion |
| --- | --- | --- |
| `overflows` | Driver callbacks that reported dropped input (cumulative) | no dropouts |
| `max_gap_ms` | Longest gap between block arrivals in that second. Compare with the block period (10.7 ms at 512 / 48 kHz); WASAPI often delivers in pairs, so about 2x is normal | no dropouts |
| `drift_ms` | Arrival of the latest block versus an ideal clock from the first block. Slow steady change is the device and computer clocks drifting apart, not a fault | stability |
| `lat_p50_ms`, `lat_p95_ms`, `lat_max_ms` | Capture-to-display latency per frame (see below) | latency |
| `fps`, `late_frames` | Redraw rate, and frames that took over 1.5x the frame period | display keeps up |
| `peak_dbfs`, `clip_frames` | Loudest peak in the second, and frames whose window reached -0.1 dBFS | no clipping |

**Latency** per displayed frame = the driver-reported input latency + (time the frame was flipped − arrival of the newest block in it). The arrival time is read before the frames, so a block landing in between makes the number slightly high, never low. It does **not** include monitor scan-out and pixel response, which add up to one refresh (16.7 ms at 60 Hz) plus the display's own lag. With `--headless` the same number measures capture-to-analysis.

## Testing without the hardware

A laptop microphone exercises the same code path, so these can be checked now:

- 30-minute stability: `--headless --minutes 30` on the mic. Overflows, gaps, and drift do not depend on the source.
- Software latency: everything except the interface's own latency, which the driver reports per device. **Re-measure on the USB interface**, since this number changes with it.
- The display tracks sound: speak, clap, or play music near the mic.
- Replay: any WAV from `tools/record_clip.py`, looped.

Only the real chain can show these: line-level clipping and headroom, hum and ground loops (needle on a silent groove), channel order, and the interface's actual latency. Results from the mic are not results for this experiment; record them in `RESULTS.md` only as a baseline, labeled as such.

First mic check (laptop Realtek mic, WASAPI device, 48 kHz, 512-frame blocks, 10 s): 0 overflows, latency p50 28.5 ms / p95 39.1 ms / max 45.1 ms including 22.0 ms reported interface latency, 60 fps, 0 late frames.

## Output

When finished, write `RESULTS.md` from [../RESULTS.template.md](../RESULTS.template.md) and add the decision to [docs/decisions.md](../../docs/decisions.md).
