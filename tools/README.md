# Tools

Command-line helpers built on the shared `flightrec.audio` package. Install the shared requirements first (see [../experiments/README.md](../experiments/README.md#setup)), then run from anywhere:

| Script | Purpose |
| --- | --- |
| `list_devices.py` | List audio input devices with channels, default sample rate, and host API (`--all` adds outputs) |
| `record_clip.py` | Record N seconds from a device to WAV through the live capture path |
| `replay.py` | Feed an audio file (WAV, MP3, FLAC, OGG) through the same capture path as live input, at real-time pace, and print levels |
| `record_features.py` | Record the feature stream to a message log (+ `.meta.json`, and the audio with `--save-audio`) from live input, a file in real time, or a file `--offline`; logs go to `samples/_local/logs/` |
| `play_log.py` | Play a message log (JSON lines, message contract) back at its original timing; `--speed`, `--type`, `--every`, `--summary` |

```sh
python tools/list_devices.py
python tools/record_clip.py --seconds 20 --device 9      # default device: FR_AUDIO_DEVICE
python tools/replay.py samples/_local/clip-20261112-120000.wav
python tools/replay.py clip.wav --fast                  # no pacing, for quick checks
python tools/record_features.py --device 9 --minutes 5 --save-audio
python tools/record_features.py --wav "samples/_songs/song.mp3" --offline
python tools/play_log.py samples/_local/logs/live-20261112-184001.jsonl --speed 4
```

- `record_features.py` writes one contract `features` message per analysis hop (about 94 per second) plus a `.meta.json` noting source, sample rate, settings, and version. Features depend only on the audio, so the same audio recorded live, in real time, or offline gives the same log, message for message. `--save-audio` saves the captured audio as 32-bit float WAV, so a live session can be re-analyzed exactly after tuning. These logs are what the Unity level generator is developed and tested against (backlog 4.4).

- `record_clip.py` writes to `samples/_local/` by default, which is untracked. Keep clips to 10-30 s, never commit full tracks, and describe the ones you keep in [../samples/README.md](../samples/README.md). It reports peak level, clipped samples, and input overflows.
- `replay.py` plays nothing out loud. The audible path never touches the computer; replay only stands in for the interface.
- On Windows each device appears once per host API (MME, DirectSound, WASAPI, WDM-KS). Prefer the WASAPI entry.

## Using the capture path in an experiment

Experiments take an optional WAV path and get the same pipeline either way:

```python
from flightrec.audio import Capture, open_source

source = open_source(args.wav, blocksize=512)  # WAV -> replay; None -> live (FR_AUDIO_DEVICE)
with Capture(source, seconds=10) as capture:
    while not capture.wait(1 / 60):  # ~60 Hz analysis loop
        frames, end_frame = capture.latest(2048)  # newest 2048 frames, oldest first
        ...  # FFT, onsets, drawing
```

Every source delivers float32 blocks of `blocksize` frames into a `RingBuffer`. Do analysis on your own thread, never in the audio callback. Use `capture.ring.read_from(start, n)` when you must process every frame without gaps. `NullSource` gives endless silence when you need no audio at all.

Keep these dependency-light and cross-platform: development happens on Windows, and the Pi is Linux.
