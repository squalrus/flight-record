# Changelog

User-visible changes, newest first. Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format and [semver](https://semver.org/) versioning.

## [0.2.0] — 2026-11-12
### Added
- **Replay tooling.** A shared `flightrec.audio` package gives live input, WAV replay, and silence one capture path: same block size, same ring buffer, real-time pacing. Experiments switch between live and recorded audio with `open_source(wav)`. Importing it does not load `sounddevice`, so replay runs without PortAudio or an interface. Experiment 01 will be the first experiment built on it. (`flightrec/audio/`)
- **Audio tools.** `list_devices.py` lists inputs by host API, `record_clip.py` records a clip to the untracked `samples/_local/` and reports peak, clipping, and input overflows (and warns when a clip is effectively silent), and `replay.py` feeds a WAV through the capture path and prints levels without playing anything aloud. (`tools/list_devices.py`, `tools/record_clip.py`, `tools/replay.py`)
- **Capture path tests.** Ring buffer wraparound and overruns, WAV round-trips, block padding, looping, real-time pacing, error propagation, and the replay CLI. (`tests/test_audio.py`)

### Changed
- **Shared code is an installable package.** `pyproject.toml` now defines `flightrec` (version read from `VERSION`), and `experiments/requirements.txt` installs it in editable mode, so the setup command is unchanged. (`pyproject.toml`, `experiments/requirements.txt`)
- **Docs.** `tools/README.md` has usage and an experiment snippet; `AGENTS.md` says shared code goes in `flightrec` and audio input goes through `flightrec.audio`. (`tools/README.md`, `AGENTS.md`)

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
