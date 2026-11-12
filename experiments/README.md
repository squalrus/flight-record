# Experiments

Each folder is a standalone project that retires one risk. The plan, methods, and success criteria are in [../docs/experiments.md](../docs/experiments.md). The ordered task list is [../BACKLOG.md](../BACKLOG.md).

| Folder | Experiment | Needs hardware | Needs API key |
| --- | --- | --- | --- |
| `01-capture-visualizer` | Capture live audio, visualize, measure latency | yes (or replay) | no |
| `02-onset-features` | Onsets, band energy, RMS, live tempo | replay OK | no |
| `03-fingerprint` | Identify clips via recognition service(s) | clips from hardware | yes |
| `04-discogs-lookup` | Artist/album to pressing, tracklist, side durations | no | yes (Discogs token) |
| `05-side-tracking` | Know when a side is over | yes for validation | optional |
| `06-message-bridge` | Python → Unity live bridge | no | no |

Experiments 07 (local co-op prototype) and 08 (end-to-end slice) live in the Unity project rather than here.

## Rules for every experiment

1. Own folder with a `README.md`: install, run, what it measures, success criteria (copied from the plan).
2. Importable modules, not just scripts, so code can be reused and later ported.
3. Must run from a recorded file via `tools/replay.py` wherever possible.
4. Finish by writing `RESULTS.md` from [RESULTS.template.md](RESULTS.template.md). Record measured numbers, the recordings used, and a decision.
5. Secrets in env vars or an untracked `.env`; never commit keys.

## Setup

Shared dependencies are in [requirements.txt](requirements.txt) (Python 3.11+). From the repo root:

```sh
python -m venv .venv
# Windows: .venv\Scripts\activate    macOS/Linux: source .venv/bin/activate
python -m pip install -r experiments/requirements.txt

ruff check .            # lint
ruff format --check .   # format check (drop --check to apply)
pytest                  # tests under tests/, experiments/, tools/
```

On Linux, `sounddevice` needs the PortAudio system library (`sudo apt install libportaudio2`). CI runs the same three commands on Windows, macOS, and Linux for Python 3.11 and 3.13 ([.github/workflows/ci.yml](../.github/workflows/ci.yml)). An experiment that needs more packages lists them in its own README.
