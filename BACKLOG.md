# Backlog

Ordered. Work top to bottom; each item lists dependencies, the deliverable, and "done when". Success criteria for the numbered experiments live in [experiments.md](docs/experiments.md). Tick the box and link the `RESULTS.md` when an item is done.

Legend: 🔌 needs the physical hardware (turntable, interface) · 🌐 needs an API key · 🖥️ no hardware needed

## Phase 0: Foundations (no hardware needed)

- [ ] **0.1 🖥️ Repo scaffolding.** Shared `experiments/requirements.txt`, `ruff` config, `.env.example`, a simple CI workflow that runs lint and unit tests.
  *Done when:* a fresh clone can `pip install -r experiments/requirements.txt` and run `ruff check` and `pytest` on Windows, macOS, or Linux.
- [ ] **0.2 🖥️ Replay tooling (`tools/`).** (a) `list_devices.py` lists audio devices; (b) `record_clip.py` records N seconds from a chosen device to WAV; (c) `replay.py` plays a WAV through the same code path as live capture (same block size, same ring buffer).
  *Done when:* experiment 01 code can run from a WAV file with no interface connected.
- [ ] **0.3 🖥️ Message contract library.** A small Python module that defines, validates, and serializes the messages in [architecture.md](docs/architecture.md), plus JSON-lines logging/replay of message streams.
  *Done when:* unit tests cover every message type, unknown fields are ignored, and a message log can be played back at original timing.
- [ ] **0.4 🖥️ Unity project audit.** Fork the existing top-down shooter into `unity/` and fill in Step 0 of [unity-fork-plan.md](docs/unity-fork-plan.md). Needs access to the original project.
  *Done when:* the audit section lists Unity version, pipeline, input system, camera, enemy/bullet/wave authoring, and any networking.

## Phase 1: Audio pipeline

- [ ] **1.1 🔌 Experiment 01: capture and visualize.** See [experiments.md](docs/experiments.md#01--capture-and-visualize). Depends on 0.2.
  *Done when:* `experiments/01-capture-visualizer/RESULTS.md` records stability over 30+ minutes and measured latency.
- [ ] **1.2 🖥️ Build a small test corpus.** Short clips (10-30 s) across rock, funk, electronic, jazz, quiet acoustic, recorded from your own records with `record_clip.py`. Describe them in `samples/README.md`.
  *Done when:* at least 10 clips exist with source/genre/notes, and none are full tracks.
- [ ] **1.3 🖥️ Experiment 02: onset and features.** Develop against the corpus (no hardware needed after 1.2); confirm live on hardware. Depends on 0.3, 1.2.
  *Done when:* `RESULTS.md` has onset recall/false-positive numbers per clip, tempo convergence times, and CPU use.
- [ ] **1.4 🖥️ Feature stream recorder.** Save the real-time feature stream to a message log so the Unity generator can be developed from recordings. Depends on 0.3, 1.3.
  *Done when:* a recorded log replays identically through `replay.py`.

## Phase 2: Identification

- [ ] **2.1 🔌🌐 Experiment 03: fingerprint a clip.** Depends on 1.2; needs API keys for the services under test. Decide the service, clip length, and retry cadence.
  *Done when:* hit rate and median time-to-identify are tabulated per genre in `RESULTS.md`, and a decision entry is added to [decisions.md](docs/decisions.md).
- [ ] **2.2 🌐 Experiment 04: Discogs lookup.** Depends on 2.1 for artist/album inputs (can start with hard-coded queries). Includes position parsing (A1, A2, B1…), duration handling, local cache, and the one-time confirmation flow.
  *Done when:* the 20-record set resolves to a correct release automatically or with one confirmation, and the cache prevents repeat lookups.

## Phase 3: Side tracking

- [ ] **3.1 🔌 Experiment 05: side tracking and "level over".** Depends on 1.3, 2.1, 2.2. Implement the state machine idle → needle_down → playing(track n) → run_out → side_end using the three signals.
  *Done when:* across 10+ full side playthroughs, `side_end` fires within a few seconds of the true end with no mid-side false positives, including runs where identification fails.

## Phase 4: Unity bridge

- [ ] **4.1 🖥️ Experiment 06: message bridge.** Depends on 0.3, 0.4. Python sends messages over UDP/OSC to a minimal Unity scene; measure latency and jitter. Include the Unity-native capture latency test.
  *Done when:* the sidecar-vs-C# decision is recorded in [decisions.md](docs/decisions.md).
- [ ] **4.2 🖥️ Game: vertical scrolling conversion.** See [unity-fork-plan.md](docs/unity-fork-plan.md) Step 2. Hand-authored waves first.
- [ ] **4.3 🖥️ Game: audio source abstraction.** Live bridge, replay, and null sources behind one interface. Depends on 4.1.
- [ ] **4.4 🖥️ Game: audio-driven level generator.** See Step 3. Deterministic given a feature stream and seed; unit-tested against recorded logs from 1.4.
- [ ] **4.5 🖥️ Game: side-aware flow.** Boss and level-complete driven by `side_state` and `side_end`. Depends on 3.1.

## Phase 5: Local multiplayer

- [ ] **5.1 🖥️ Experiment 07: local co-op prototype.** See [experiments.md](docs/experiments.md#07--local-co-op-prototype). Depends on 4.2; use replayed feature streams (1.4) once 4.4 exists. Join/leave via controllers, shared screen, per-player ship/score/lives, difficulty scaling from player count.
  *Done when:* success criteria in the plan are met and D-110 (max players, devices) and D-111 (scaling) are recorded in [decisions.md](docs/decisions.md).
- [ ] **5.2 🖥️ Co-op rules and polish.** Shared vs individual lives, friendly fire, revive (D-107), per-player scoring and end-of-level stats.

## Phase 6: Vertical slice

- [ ] **6.1 🔌 Experiment 08: end-to-end slice.** One full side, start to finish, with identification failure modes exercised.

## Parallel track: product decisions

- [ ] Resolve open decisions in [risks-and-open-questions.md](docs/risks-and-open-questions.md) as they become blocking.
- [ ] Name check and trademark search per [naming.md](docs/naming.md).
- [ ] Choose a license (see [decisions.md](docs/decisions.md), D-104).
