# Changelog

User-visible changes, newest first. Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format and [semver](https://semver.org/) versioning.

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
