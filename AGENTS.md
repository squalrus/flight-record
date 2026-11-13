# AGENTS.md

Instructions for coding agents (and humans) working in this repo. Read this first, then the docs it points to.

## What this project is

**Flight Record** (working title): a local-multiplayer (same machine, shared screen), vertically scrolling shoot 'em up whose levels are generated live from audio playing on a real turntable or any line-level source. One side of a vinyl record is one level. See [README.md](README.md) for the one-paragraph pitch.

The repo is in the **pre-prototype** phase. Right now the work is small, standalone **Python experiments** that retire risk, followed by a merge into a **fork of an existing Unity top-down space shooter** that gets converted to vertical scrolling with local multiplayer.

## Read these before coding

| Task type | Read |
| --- | --- |
| Anything | [docs/concept-and-design.md](docs/concept-and-design.md), [docs/decisions.md](docs/decisions.md) |
| Audio, identification, side tracking | [docs/architecture.md](docs/architecture.md) (**message contract is authoritative**), [docs/experiments.md](docs/experiments.md) |
| What to do next | [BACKLOG.md](BACKLOG.md) |
| Unity work | [docs/unity-fork-plan.md](docs/unity-fork-plan.md) |
| Hardware questions | [docs/hardware.md](docs/hardware.md) |
| Legal/ToS, unresolved choices | [docs/risks-and-open-questions.md](docs/risks-and-open-questions.md) |

## Current state and what to do next

1. Work through [BACKLOG.md](BACKLOG.md) **in order**. Pick the first unchecked item whose dependencies are done.
2. Each experiment lives in `experiments/NN-name/` with its own `README.md`, code, and a `RESULTS.md` written from [experiments/RESULTS.template.md](experiments/RESULTS.template.md). An experiment is **not done** until `RESULTS.md` records the measured numbers and a decision.
3. When a decision is made (library, approach, service), add an entry to [docs/decisions.md](docs/decisions.md).

## Hard constraints

- **The game must work without identification.** Fingerprinting and Discogs are enhancements. Every code path that consumes them needs a "no match / unknown / missing" fallback. Never block gameplay on a network call.
- **The audible signal path never touches the computer** for live input (line-in, mic). Audio is split in hardware; do not route live audio out through the game. The only exception is audio **files**, which the computer plays itself (decisions D-011 for development, D-012 for the product); never extend that to live input.
- **The message contract is versioned.** Messages carry `v` and `t`. Add fields freely (consumers must ignore unknown fields); changing or removing a field means bumping `v` and updating [docs/architecture.md](docs/architecture.md) in the same change.
- **Multiplayer is local only.** One machine, one game instance, one shared screen, several controllers. Do not add networking, netcode libraries, or audio streaming (decision D-009). Every player hears the source through the room's speakers via the hardware split.
- **Hardware must not be required to develop.** Everything downstream of capture must run from a recorded file (replay source) or a null source. Build the replay path early (see backlog item 0.2).
- **Use real recordings for tests, never invented numbers.** Results docs report measured values with the recording set and conditions they came from.

## Secrets and credentials

- API keys and tokens (Discogs, ACRCloud, AudD, etc.) go in environment variables or an untracked `.env` file. **Never commit them.** Provide a committed `.env.example` listing the variable names with empty values.
- Do not paste keys into docs, issues, logs, or `RESULTS.md`.

## Copyright and samples

- Do **not** commit full copyrighted tracks. `samples/` holds short test clips only (target: 10-30 seconds each), small in size, and only audio the owner has the right to keep. If in doubt, keep it out of git and describe it in `samples/README.md`.
- Album art and Discogs data have licensing and terms-of-service implications; see the risks doc before caching or shipping them.

## Conventions

**Python experiments**
- Python 3.11+. One virtual environment per experiment, or the shared `experiments/requirements.txt`; document whichever you use.
- Core libraries: `sounddevice`, `numpy`, `scipy`; `librosa` only for **offline** ground-truth comparison, not in the real-time path. Real-time code must not allocate in the audio callback; push samples into a ring buffer and do the analysis elsewhere.
- Format with `ruff format`, lint with `ruff check`. Type hints on public functions.
- Each experiment has a `README.md` with: how to install, how to run, what it measures, and the success criteria copied from [docs/experiments.md](docs/experiments.md).
- Keep analysis code in importable modules (not only scripts) so it can be reused by the replay tool and ported to C# later. Shared code lives in the `flightrec` package at the repo root (installed in editable mode by `experiments/requirements.txt`). Audio input goes through `flightrec.audio` (`open_source` + `Capture`) so every experiment runs the same from live input or a WAV; see [tools/README.md](tools/README.md).
- Time audio with `time.perf_counter()`, not `time.monotonic()`: before Python 3.13, `monotonic()` on Windows ticks only every ~15.6 ms, which breaks pacing and latency numbers. Block timestamps from `flightrec.audio` are `perf_counter()` values. Keep the module boundary from the architecture doc: capture/analysis, identification, side tracking, game.

**Unity** (once the fork is in `unity/`)
- Do the audit in [docs/unity-fork-plan.md](docs/unity-fork-plan.md) Step 0 and record findings **before** refactoring.
- Never commit `Library/`, `Temp/`, `Logs/`, or `UserSettings/` (see `.gitignore`). Commit `.meta` files. Large art/audio goes through Git LFS (see `.gitattributes`).
- Keep audio-integration code behind an interface with at least three sources: live bridge, replay, and null.

**Git**
- Small, focused commits with imperative subject lines. One experiment or one feature per branch/PR when practical.
- Don't rewrite history on `main`.

## Testing and verification

- Unit-test pure logic (onset detection on synthetic signals, Discogs position parsing, side-state machine transitions on recorded event logs).
- Anything involving hardware or live services needs a documented manual check in the experiment's `README.md`, plus a recorded sample so others can reproduce without the hardware.
- Before saying an experiment is done: the success criteria in `docs/experiments.md` are met **or** explicitly marked unmet in `RESULTS.md` with the reason.

## Things to ask the human about (do not guess)

- Anything marked as an open decision in [docs/risks-and-open-questions.md](docs/risks-and-open-questions.md) that blocks the current task.
- Choosing a recognition service or license.
- Details of the existing Unity project (Unity version, render pipeline, input system, existing networking). These have **not been documented yet**; ask for them or inspect the fork, do not assume.
- Buying or configuring hardware, or anything requiring the physical turntable.

## Glossary

- **Side:** one side of a vinyl record. The unit of a level.
- **Run-out groove:** the end-of-side groove; produces surface noise and a rhythmic thump, signalling the side is over.
- **Onset:** the start of a percussive event (a beat or hit) detected in the audio.
- **Fingerprinting:** identifying a song from a short audio clip via a recognition service.
- **Local co-op:** two or more players on one machine sharing one screen, each with their own controller.
- **Replay source:** a recorded feature or audio stream played back in place of live capture.
