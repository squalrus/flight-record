# Changelog

User-visible changes, newest first. Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format and [semver](https://semver.org/) versioning.

## [0.8.0] — 2026-11-12
### Added
- **Discogs release lookup (experiment 04).** From artist, album, and the identified track to a release, its vinyl sides, and their durations. Search tries the barcode (UPC) first, then the album as given and without bracketed editions ("(Deluxe Edition)"), then the recognition service's alternate albums, then the track title. Results are scored on title, artist, vinyl, and the service's label and year. Ties between pressings of one master release are picked automatically (the main release, or the earliest vinyl pressing), and missing durations are filled from the master's tracklist. A release found only by track title is trusted only if its label or year agrees. Choices are remembered locally, and API responses are cached and rate limited. First pass on 7 songs: all resolved automatically to a release containing the track; full durations for 4 of 6 vinyl releases. Backlog item 2.2 stays open: the 20-record vinyl set needs real records. (`flightrec/discogs.py`, `experiments/04-discogs-lookup/lookup.py`)
- **Release panel in the explorer.** `explore.py --identify ... --discogs` shows the release (with its cover), side and position, time left on the track and the side, Discogs genres and styles, original year, community have / want, and the side's tracklist with the playing track highlighted. An agreement line checks the identification against the release (track, artist, duration, label, year). Keys **1-5** pick a pressing when it is ambiguous; **F** forgets a remembered pick and looks it up again. Lookups and cover downloads run on a background thread, and each lookup is logged as a `release_selected` message. (`flightrec/viz.py`, `experiments/02-onset-features/explore.py`)
- **Playback position and countdowns.** The time into the track comes from the recognition service's position (`play_offset_ms` / `timecode`) plus the audio captured since, re-anchored on every re-check. The track length comes from the service, falling back to the tracklist; side progress uses the tracklist, with estimates marked. A service's "Pt. I" of what the vinyl lists as one long track matches that track (exact timing for part 1), and prefixed titles like "Of Mind - Nocturne" match "Nocturne". (`flightrec/discogs.py`)
- **More from each identification.** From ACRCloud: track length and position, the recording id (`acrid`), label, genres, release date, ISRC and UPC (when turned on in the project), and the albums of its other matches. From AudD: position, label, release date, and a listen link. All of them are optional fields on the contract's `track_identified`; `release_selected` gains `album`, `year`, `genres`, and `styles`. (`flightrec/identification.py`, `flightrec/messages.py`, `docs/architecture.md`)
- **`run.py` shortcuts.** `python run.py list`, `python run.py <song>` (by any word from artist, title, or file name), and `python run.py mic`, with `--plain`, `--audd`, `--quiet`, and `--device`. Song names come from the files' tags at run time, and the script switches to the project's `.venv` by itself. (`run.py`)
- **Tests.** Discogs parsing, scoring, master releases, hints, guard, part and prefixed titles, progress, agreement, cover art, and caching; the lookup flow; new recognition fields; and the shortcuts. 204 tests in total. (`tests/test_discogs.py`, `experiments/04-discogs-lookup/test_lookup.py`, `tests/test_identification.py`, `tests/test_run.py`)

### Changed
- **Live-input scripts read `.env.local` / `.env` at startup,** so `FR_AUDIO_DEVICE` works without passing `--device`. (`experiments/02-onset-features/explore.py`, `experiments/01-capture-visualizer/visualize.py`, `tools/record_clip.py`, `tools/record_features.py`)
- **The fingerprint harness re-parses cached responses,** so parser improvements reach old answers, and its CSV includes track length and position. (`experiments/03-fingerprint/fingerprint.py`)
- **Docs.** The experiment 03 and 04 READMEs cover the new fields, hints, agreement checks, cover art, the duration fallback, and findings (master releases resolving ties, a UPC Discogs does not list, part-titled and prefixed tracks). The README and the experiment 02 README cover the shortcuts and the release panel. (`experiments/03-fingerprint/README.md`, `experiments/04-discogs-lookup/README.md`, `experiments/02-onset-features/README.md`, `README.md`)

### Fixed
- **Console output no longer crashes on unusual characters** (e.g. a zero-width space in a Discogs title) on Windows code pages. (`flightrec/console.py`)
- **A digital distributor credited as the label** (DistroKid, CD Baby, ...) no longer counts as a label disagreement. (`flightrec/discogs.py`)

## [0.7.0] — 2026-11-12
### Added
- **Live identification in the explorer.** `explore.py --identify acrcloud|audd` adds a panel showing what is playing: status, the current match with album, confidence, and response time, recent attempts, request count, and a countdown to the next try. Requests run on a background thread so the window never waits, retry until a match and then re-check for track changes, and stop at `--max-identify`. Each attempt is logged as a contract `track_identified` or `identification_failed` message. Works with live input, file replay, and `--listen`. (`experiments/02-onset-features/explore.py`, `flightrec/viz.py`)
- **Recognition clients.** ACRCloud (signed `/v1/identify`) and AudD clients over the standard library, a background `Identifier` with retry, re-check, and request-cap scheduling based on audio time, and a WAV encoder for clips. No match and errors are results, never exceptions (D-004). (`flightrec/identification.py`)
- **Experiment 03 batch harness.** `fingerprint.py` cuts clips from a folder of songs at several positions and lengths, clean or with simulated vinyl (wow, treble roll-off, rumble, hiss, crackle), sends them to the configured services, and scores each answer against the file's tags (correct / other version / wrong / no match / error). `--dry-run` counts requests first; answers are cached so re-runs are free; `--max-requests` caps spending. First pass, one 10 s clean clip per song: ACRCloud 7/7 at 0.84 s median, AudD 7/7 at 1.77 s. Backlog 2.1 stays open: real-vinyl results need the clips from 1.2. (`experiments/03-fingerprint/fingerprint.py`)
- **Key loading and tags.** `load_env()` reads untracked `.env.local` then `.env` (shell variables win, empty values skipped); a small ID3v2 reader gets artist, title, album, BPM, and key from MP3s, with a filename fallback. (`flightrec/env.py`, `flightrec/tags.py`)
- **Tests.** 31 offline tests: response parsing for both services, ACRCloud signing, multipart bodies, the `.env` loader, ID3 tags, identifier scheduling and non-blocking behavior, contract messages, clip cutting, the vinyl simulation, scoring, caching, and the request budget. (`tests/test_identification.py`, `experiments/03-fingerprint/test_fingerprint.py`)

### Changed
- **Docs.** The experiment 03 README covers setup, batch and live runs, and the first findings (including a "which release" mismatch that matters for experiment 04). AGENTS.md documents `.env.local` and never reading or printing key values; the experiment 02 README covers `--identify`. (`experiments/03-fingerprint/README.md`, `AGENTS.md`, `experiments/02-onset-features/README.md`)

## [0.6.0] — 2026-11-12
### Added
- **Feature stream recorder.** `record_features.py` records the real-time feature stream to a message-contract log from live input, a file in real time, or a file offline, so the Unity level generator can be built and tested from recordings. Each log gets a `.meta.json` (source, sample rate, analysis settings, version, skipped hops, dropped input, input latency), and `--save-audio` keeps the captured audio as 32-bit float so a live session can be re-analyzed exactly after tuning. Logs default to the git-ignored `samples/_local/logs/`. Recording the same audio live, in real time, or offline gives the same log message for message; a 10 s live mic recording matched a re-analysis of its saved audio across all 931 messages. (`tools/record_features.py`)
- **Tests.** Real-time and offline recordings of a file are identical, saved audio re-records to the same log, the metadata is complete, and a recorded log replays at its exact original timing. (`tests/test_record_features.py`)

### Changed
- **Docs.** The tools README covers recording and playing logs and notes that these logs feed the Unity generator (backlog 4.4); the samples README notes where logs are stored. (`tools/README.md`, `samples/README.md`)

## [0.5.0] — 2026-11-12
### Added
- **Message contract library.** All six messages from the architecture doc (`features`, `track_identified`, `release_selected`, `identification_failed`, `side_state`, `side_end`) as typed, validated classes: field types, ranges, required fields, and finite numbers are checked. Unknown fields are accepted and kept, a newer schema version is rejected, and unknown types can be skipped. Logs are JSON lines that replay at their original timing (or faster or slower). Standard library only, so Unity can mirror it. (`flightrec/messages.py`)
- **`play_log.py`.** Plays a message log back at original timing from the command line, with type filtering, thinning for high-rate features, and a summary mode. (`tools/play_log.py`)
- **Tests.** 34 contract tests: every doc example round-trips, bad input of each kind is rejected with a clear message, unknown fields survive a rewrite, log errors name the line, and replay timing is exact with a fake clock and never early with the real one. (`tests/test_messages.py`)

### Changed
- **Explorer logs use the contract.** `Features.to_message()` builds a validated `features` message instead of a hand-built dict, and every logged line now parses back through the contract. (`flightrec/audio/features.py`, `experiments/02-onset-features/explore.py`)
- **Contract docs.** The architecture doc gains a field-rules table (required and optional fields, ranges, nulls) and documents experiment 02's optional `features` fields. `release_selected`'s example now carries the `t` every message needs. AGENTS.md says to build messages with `flightrec.messages`, never hand-written dicts. (`docs/architecture.md`, `AGENTS.md`, `tools/README.md`, `experiments/02-onset-features/README.md`)

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
