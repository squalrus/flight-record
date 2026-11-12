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

Shared dependencies: `requirements.txt` in this folder (to be created in backlog item 0.1).
