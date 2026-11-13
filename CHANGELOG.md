# Changelog

User-visible changes, newest first. Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format and [semver](https://semver.org/) versioning.

## [0.4.0] — 2026-11-12
### Added
- **Experiment 02 explorer.** A live window to see what the music offers: scrolling spectrogram, trigger lanes (low / mid / high band onsets, full band, predicted beats), energy and brightness traces, tempo with x2 and /2 readings and a beat pulse, 12-note chroma with the estimated key, and level meters. It runs live, from a file, `--offline` (fast, identical results), or `--headless`, and logs every hop as JSON lines in the message-contract format. `[` and `]` adjust the trigger gate live. Backlog item 1.3 stays open: measured results need the clip corpus and the hardware. (`experiments/02-onset-features/explore.py`, `experiments/02-onset-features/requirements.txt`)
- **Real-time feature extraction.** Per-band onset triggers with adaptive thresholds and a level gate, band energies, loudness, spectral centroid, tempo with confidence and beat phase, and chroma with an estimated key, all numpy only, at about 0.5 ms per 10.7 ms hop. (`flightrec/audio/features.py`)
- **Key and tempo evaluation.** `evaluate.py` analyzes a folder of songs in parallel and scores key (exact / relative / fifth / parallel / wrong) and tempo (exact / half-double / two-thirds / wrong) against published values in a CSV. First pass on 7 songs: tempo 5 of 6 right up to half/double time, key 2 of 5 exact. (`experiments/02-onset-features/evaluate.py`)
- **Audio files as input.** `--wav` now takes MP3, FLAC, and OGG as well as WAV, in both experiments and `tools/replay.py`, via the `soundfile` package. Decision D-012 makes files a supported input for the game too, with the computer playing them. (`flightrec/audio/wav.py`, `pyproject.toml`, `experiments/requirements.txt`)
- **`--listen` for file replay.** Plays the file through the speakers in sync with the analysis: the output stream drives replay, and the display waits out the reported output latency. Files only, never live input (decision D-011). (`flightrec/audio/sources.py`)
- **Shared visualizer panels.** Window, waveform, spectrum, meters, spectrogram, trigger lanes, traces, chroma, and tempo panels, used by both experiments. (`flightrec/viz.py`)
- **Tests.** 25 feature tests on synthetic signals with known answers (tempo at 90-175 BPM, beats on kicks not hats, C major and A minor keys, room rumble not triggering), MP3/FLAC/OGG reading, playback sync, and the explorer offline and as a live replay. (`tests/test_features.py`, `tests/test_audio.py`, `experiments/02-onset-features/test_explore.py`)

### Changed
- **Key profiles.** Key estimation uses the Albrecht-Shanahan pop/rock profiles by default. Krumhansl-Kessler put 3 of 5 songs a fifth too high and got none exact; Albrecht-Shanahan got 2 exact. Krumhansl-Kessler remains selectable. (`flightrec/audio/features.py`)
- **Experiment 01 uses the shared panels.** Its window looks the same; the drawing code moved to `flightrec.viz`. (`experiments/01-capture-visualizer/visualize.py`)
- **Docs for file input.** AGENTS.md's audible-path rule now allows audio files (decisions D-011, D-012) and stays strict for live input. The concept, architecture, backlog 4.3 (file source in game), samples README, tools README, and experiment 02 plan and README cover files, the explorer, key/chroma, and the first findings. (`AGENTS.md`, `docs/`, `BACKLOG.md`, `samples/README.md`, `tools/README.md`, `experiments/02-onset-features/README.md`)

### Fixed
- **Full songs kept out of git.** `samples/_songs/` (full tracks for evaluation, and their published keys) is now git-ignored, like `samples/_local/`. (`.gitignore`)

## [0.3.0] — 2026-11-12
### Added
- **Experiment 01 visualizer.** A live waveform, log-frequency spectrum, and L/R level meters (clip light, 50/60 Hz hum markers) from live input or a WAV, with a `--headless` mode for long stability runs. It logs one CSV row per second: dropped input, block gaps, clock drift, capture-to-display latency percentiles, fps, peak level, and clipping. The success criteria still need the real hardware chain, so backlog item 1.1 stays open. A first laptop-mic run measured 28.5 ms median, 39.1 ms p95 latency, with no overflows. (`experiments/01-capture-visualizer/visualize.py`, `experiments/01-capture-visualizer/requirements.txt`)
- **Spectrum analysis.** Windowed FFT in dBFS (a full-scale sine reads 0 dBFS), log-spaced bands, and per-channel RMS and peak levels, reusable by later experiments. (`flightrec/audio/spectrum.py`)
- **Capture timing diagnostics.** `Capture` now reports the latest block's lag (clock drift over long runs) and the longest gap between blocks, and sources report the driver's input latency. (`flightrec/audio/capture.py`, `flightrec/audio/sources.py`)
- **Tests.** Spectrum and level accuracy on synthetic tones, timing diagnostics, and an end-to-end headless run of experiment 01 from a WAV, with no interface or Pygame needed. (`tests/test_spectrum.py`, `experiments/01-capture-visualizer/test_capture_visualizer.py`)

### Changed
- **Experiment 01 docs.** The README covers how to run, what each logged column means, how latency is defined, and what a microphone can and cannot stand in for. D-010 records Pygame as the provisional choice for experiment visualizers. (`experiments/01-capture-visualizer/README.md`, `docs/decisions.md`)

## [0.2.0] — 2026-11-12
### Added
- **Replay tooling.** A shared `flightrec.audio` package gives live input, WAV replay, and silence one capture path: same block size, same ring buffer, real-time pacing. Experiments switch between live and recorded audio with `open_source(wav)`. Importing it does not load `sounddevice`, so replay runs without PortAudio or an interface. Experiment 01 will be the first experiment built on it. (`flightrec/audio/`)
- **Audio tools.** `list_devices.py` lists inputs by host API, `record_clip.py` records a clip to the untracked `samples/_local/` and reports peak, clipping, and input overflows (and warns when a clip is effectively silent), and `replay.py` feeds a WAV through the capture path and prints levels without playing anything aloud. (`tools/list_devices.py`, `tools/record_clip.py`, `tools/replay.py`)
- **Capture path tests.** Ring buffer wraparound and overruns, WAV round-trips, block padding, looping, real-time pacing, error propagation, and the replay CLI. (`tests/test_audio.py`)

### Changed
- **Shared code is an installable package.** `pyproject.toml` now defines `flightrec` (version read from `VERSION`), and `experiments/requirements.txt` installs it in editable mode, so the setup command is unchanged. (`pyproject.toml`, `experiments/requirements.txt`)
- **Docs.** `tools/README.md` has usage and an experiment snippet; `AGENTS.md` says shared code goes in `flightrec`, audio input goes through `flightrec.audio`, and audio timing uses `time.perf_counter()` (`time.monotonic()` is too coarse on Windows before Python 3.13). (`tools/README.md`, `AGENTS.md`)

## [0.1.0] — 2026-11-12
### Added
- **Repo scaffolding.** Shared Python dependencies in `experiments/requirements.txt` (`sounddevice`, `numpy`, `scipy`, plus `ruff` and `pytest`), `ruff` and `pytest` configuration in `pyproject.toml`, and a smoke test suite in `tests/`. A GitHub Actions workflow runs lint, format check, and tests on Windows, macOS, and Linux with Python 3.11 and 3.13. (`experiments/requirements.txt`, `pyproject.toml`, `tests/test_environment.py`, `.github/workflows/ci.yml`)

### Changed
- **Experiment setup docs.** `experiments/README.md` now has install and check commands in place of the "to be created" placeholder. (`experiments/README.md`)

## [0.0.1] — 2026-11-12
### Added
- **Project docs seed.** Initial concept, architecture, decisions, experiments plan, hardware notes, naming, risks, and Unity fork plan, plus per-experiment READMEs and agent instructions. (`docs/`, `experiments/`, `AGENTS.md`, `README.md`, …)
- **Changelog and version tracking.** Releases are recorded here and the current version lives in `VERSION`. (`CHANGELOG.md`, `VERSION`, `CLAUDE.md`)

### Changed
- **Backlog moved to the repo root.** `docs/backlog.md` is now `BACKLOG.md`; its links and every reference to it were updated. (`BACKLOG.md`, `AGENTS.md`, `README.md`, `experiments/README.md`)
