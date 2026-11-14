# Backlog

Ordered. Work top to bottom; each item lists dependencies, the deliverable, and "done when". Success criteria for the numbered experiments live in [experiments.md](docs/flightrec/experiments.md). Tick the box and link the `RESULTS.md` when an item is done.

Legend: 🔌 needs the physical hardware (turntable, interface) · 🌐 needs an API key · 🖥️ no hardware needed

## Phase 1: Audio pipeline

- [ ] **1.1 🔌 Experiment 01: capture and visualize.** See [experiments.md](docs/flightrec/experiments.md#01--capture-and-visualize). Depends on 0.2.
  *Done when:* `experiments/01-capture-visualizer/RESULTS.md` records stability over 30+ minutes and measured latency.
- [ ] **1.2 🖥️ Build a small test corpus.** Short clips (10-30 s) across rock, funk, electronic, jazz, quiet acoustic, recorded from your own records with `record_clip.py`. Describe them in `samples/README.md`.
  *Done when:* at least 10 clips exist with source/genre/notes, and none are full tracks.
- [ ] **1.3 🖥️ Experiment 02: onset and features.** Per-band triggers, energies, loudness, brightness, tempo, and chroma/key, with a live explorer window (`experiments/02-onset-features/explore.py`, in progress). Exploring needs nothing else; the measured results need the corpus (1.2), and a message-log replay needs 0.3. Confirm live on hardware.
  *Done when:* `RESULTS.md` has onset recall/false-positive numbers per clip, tempo convergence times, key accuracy against known keys, and CPU use.

## Phase 2: Identification

- [ ] **2.1 🔌🌐 Experiment 03: fingerprint a clip.** Depends on 1.2; needs API keys for the services under test. Decide the service, clip length, and retry cadence.
  *Done when:* hit rate and median time-to-identify are tabulated per genre in `RESULTS.md`, and a decision entry is added to [decisions.md](docs/decisions.md).
- [ ] **2.2 🌐 Experiment 04: Discogs lookup.** Depends on 2.1 for artist/album inputs (can start with hard-coded queries). Includes position parsing (A1, A2, B1…), duration handling, local cache, and the one-time confirmation flow.
  *Done when:* the 20-record set resolves to a correct release automatically or with one confirmation, and the cache prevents repeat lookups.

## Phase 3: Side tracking

- [ ] **3.1 🔌 Experiment 05: side tracking and "level over".** Depends on 1.3, 2.1, 2.2. Implement the state machine idle → needle_down → playing(track n) → run_out → side_end using the three signals.
  *Done when:* across 10+ full side playthroughs, `side_end` fires within a few seconds of the true end with no mid-side false positives, including runs where identification fails.

## Phase 4: Unity game

Game items are one change each, so each one can be played and tested on its own. The audit in [unity-fork-plan.md](docs/unity/unity-fork-plan.md) Step 0 names the classes involved. The direction is arcade over resource management (D-013).

- [ ] **4.7 🖥️ Game: desktop first.** Remove `FloatingJoystick`, `TouchControls`, autorotation, and the Android-only settings; fix a landscape window.
  *Done when:* keyboard and gamepad still drive every screen, and nothing references the removed touch code.
- [ ] **4.8 🖥️ Game: remove fuel.** Thrust stops burning fuel; remove the fuel stat, HUD gauge, fuel pickups and upgrades, and the out-of-fuel failure (D-013).
  *Done when:* a run cannot end for lack of fuel, and no fuel UI or save field remains (old saves still load).
- [ ] **4.9 🖥️ Game: ammo skeleton and power-ups.** Ammo is infinite by default, behind a setting that can turn the limit back on, so the rule can be decided by play (D-014). Power-up pickups change the weapon for a while (spread, rapid fire, homing) and are the main way to get stronger.
  *Done when:* the default run never runs out of shots, the limited mode still works, and at least two power-ups can be picked up, show on the HUD, and expire.
- [ ] **4.10 🖥️ Game: fixed facing and screen-bounded movement.** The ship always faces up and moves directly from the stick within the playfield (no rotation, no momentum); shots go straight up. Settle D-106 (portrait vs fixed-width column) here.
  *Done when:* the ship cannot leave the visible playfield, feels responsive on keyboard and gamepad, and D-106 is recorded.
- [ ] **4.11 🖥️ Game: scrolling camera and vertical parallax.** Replace `CameraFollow` and the 3x3 `BackgroundTiler` with a camera that scrolls up at a set speed (later driven by audio) and parallax layers that scroll at their own rates. Depends on 4.10.
  *Done when:* scroll speed is one tunable value, the background never shows seams or gaps, and changing the speed mid-run is smooth.
- [ ] **4.12 🖥️ Game: enemy entry paths and formations.** Replace orbit and patrol AI with paths (enter from the top or sides, sweep, exit) and formations that move as a group; turrets track the nearest player. Despawn below and beside the screen. Depends on 4.11.
  *Done when:* each existing enemy prefab can fly at least two paths and one formation, set up as data (a path or formation asset), not code.
- [ ] **4.13 🖥️ Game: bullet-pattern emitters.** Generalize Triceptor's volley into emitters: aimed, spread, ring, and sweeping stream, each with rate and speed settings and a trigger call the generator can drive from beats later.
  *Done when:* each pattern can be put on any enemy as data and fired by a trigger, with a pooled projectile cap so dense patterns hold frame rate.
- [ ] **4.2 🖥️ Game: hand-authored waves.** A `Wave` asset (formation, path, enemy types, timing) and a hand-authored sequence that replaces `Sector`, `SectorSpawner`, and `SectorTypeTable` random arena spawning. See [unity-fork-plan.md](docs/unity/unity-fork-plan.md) Step 2. Depends on 4.10, 4.11, 4.12.
  *Done when:* a few minutes of hand-authored waves play start to finish in the scrolling view, and a wave knows when every one of its enemies is destroyed (needed by 4.15).
- [ ] **4.14 🖥️ Game: points and high score.** Points per enemy type, a running score on the HUD, and a saved high score.
  *Done when:* every kill adds its enemy's points, and the high score persists across runs.
- [ ] **4.15 🖥️ Game: wave-clear combos.** Destroying every enemy in a wave pays a bonus and raises a combo multiplier; letting one escape resets it. Depends on 4.2, 4.14.
  *Done when:* clearing consecutive waves visibly raises the multiplier and the bonus, and one escape resets it.
- [ ] **4.16 🖥️ Game: expanded drops.** A drop table per enemy and wave (power-ups, points, shields or extra lives, bombs), with pickups attracted to the nearest ship. Depends on 4.9.
  *Done when:* drop tables are data, and each drop type has a pickup, an effect, and a HUD cue.
- [ ] **4.17 🖥️ Game: new enemies.** Design and make new enemy types for vertical play (art, prefab, path, and pattern), filling roles the old roster lacks: fodder swarms, tanks, turrets, and dive-bombers. Depends on 4.12, 4.13.
  *Done when:* at least three new enemies play in hand-authored waves, each with a distinct role.
- [ ] **4.3 🖥️ Game: audio source abstraction.** Live bridge, replay, null, and **file** sources behind one interface. The file source plays the file in-game and feeds analysis a little ahead of playback (D-012). Depends on 4.1.
- [ ] **4.4 🖥️ Game: audio-driven level generator.** See Step 3. Deterministic given a feature stream and seed; unit-tested against recorded logs from 1.4.
- [ ] **4.18 🖥️ Game: content templates by genre, tempo, and energy.** Authored sets of waves, enemies, patterns, and palettes, chosen by genre (from identification when known), BPM, and energy, with the live features filling in timing and density inside the template (D-015). Unknown genre falls back to a template chosen from features alone. Depends on 4.4, 1.3, 2.2.
  *Done when:* the same record replayed twice picks the same template, different genres and tempos pick visibly different ones, and an unidentified record still plays.
- [ ] **4.5 🖥️ Game: side flow and boss.** Replace the sector, portal, and 600 s timeout flow with side flow: waves until the side ends, a boss driven by `side_state` and `side_end` (or remaining time), then level complete and a "flip the record" prompt. See Step 6. Depends on 3.1.
  *Done when:* a replayed side runs from needle-down to level complete with no sector, portal, or timeout code left in the run.

## Phase 5: Local multiplayer

- [ ] **5.3 🖥️ Game: multiplayer-ready refactor.** Remove the single-player assumptions the audit lists: the static `PlayerController.PlayerTransform`, `Find("Player")`, `FindFirstObjectByType<PlayerController>()` and `<PlayerInput>()`, the camera's single target, and the one-player HUD. Add a player registry that enemies target through, per-player run stats, and `PlayerInputManager` for joining. Depends on 4.10, 4.11.
  *Done when:* one player plays exactly as before, and a second player can join from a second device with their own ship and HUD.
- [ ] **5.1 🖥️ Experiment 07: local co-op prototype.** See [experiments.md](docs/flightrec/experiments.md#07--local-co-op-prototype). Depends on 4.2, 5.3; use replayed feature streams (1.4) once 4.4 exists. Join/leave via controllers, shared screen, per-player ship/score/lives, difficulty scaling from player count.
  *Done when:* success criteria in the plan are met and D-110 (max players, devices) and D-111 (scaling) are recorded in [decisions.md](docs/decisions.md).
- [ ] **5.2 🖥️ Co-op rules and polish.** Shared vs individual lives, friendly fire, revive (D-107), per-player scoring and end-of-level stats.

## Phase 6: Vertical slice

- [ ] **6.1 🔌 Experiment 08: end-to-end slice.** One full side, start to finish, with identification failure modes exercised.

## Parallel track: product decisions

- [ ] Resolve open decisions in [risks-and-open-questions.md](docs/risks-and-open-questions.md) as they become blocking.
- [ ] Name check and trademark search per [naming.md](docs/naming.md).
- [ ] Choose a license (see [decisions.md](docs/decisions.md), D-104).
