# Unity Fork Plan

The starting point is an existing Unity **top-down space shooter**. This repo's Unity project is a fork of it, reworked into a **vertically scrolling shoot 'em up** with **local (same-machine) multiplayer** and **audio-driven level generation**.

> Details of the existing project (Unity version, input system, render pipeline, netcode if any, how enemies and levels are authored) were not available when this was written. The first task below is to fill those in.

## Step 0: Audit the existing project

Document, in this file, before changing anything:

- Unity version, render pipeline (URP/Built-in), input system (old/new)
- How the camera works and what "top-down" assumes (free 2D movement? rotation-based aiming? world-space bounds?)
- How enemies, bullets, and waves are defined (prefabs, ScriptableObjects, hand-placed in scenes?)
- Whether any networking exists today (this project is local-only: remove or leave disabled any networking code, do not extend it)
- What is reusable as-is: ship controller, weapons, health, pickups, UI, VFX, audio

## Step 1: Fork and branch

- Fork the repo; keep a branch tracking upstream for useful fixes.
- Put the project under `/unity` (or add it as a submodule if the original should stay separate).
- Add a Unity-appropriate `.gitignore` and enable Git LFS for large art and audio assets.

## Step 2: Convert to vertical scrolling

Typical changes, to be adjusted after the audit:

- **Camera and playfield:** fixed portrait-oriented playfield (or a fixed-width column in a landscape window). Scroll by moving the world (or camera) along Y at a controllable speed, driven by the speed parameter from the audio features.
- **Player ship:** movement mostly constrained to the playfield rectangle; fixed facing (up). Decide whether the ship can move vertically within the screen or only horizontally.
- **Enemy movement:** replace top-down chasing and orbiting behaviors with patterns suited to vertical shooters: enter from the top, sweeping paths, formations, turrets that track the player, side entries.
- **Bullets and patterns:** keep bullet logic; rework pattern emitters toward spreads, aimed shots, rings, and sweeping streams that can be triggered from a beat grid.
- **Backgrounds:** parallax scrolling layers; speed linked to scroll speed.
- **Bounds and despawning:** enemies and bullets despawn past the bottom edge (and sides); spawn above the top edge.

## Step 3: Audio-driven level generator

A `LevelGenerator` component consumes the message contract (see architecture) and produces a **spawn schedule**:

- Onset → candidate spawn event at `t + lead_time`.
- Bass / mid / treble weights select the enemy type and formation.
- RMS and tempo set density caps, scroll speed, and bullet-pattern complexity.
- A fairness layer enforces minimum spacing, maximum simultaneous enemies, and guaranteed breathing room.
- Track changes and `side_state` drive stage transitions; `side_end` (or expected remaining time reaching a threshold) triggers the boss.

The generator should be deterministic given the same feature stream and seed, so live and recorded-replay behavior match and so the generator is unit-testable from recorded feature logs.

**Replay tooling:** record feature streams to disk and play them back into Unity. This makes level tuning possible without a turntable and makes bugs reproducible.

## Step 4: Local multiplayer

- No networking (decision D-009): one game instance, one shared screen, several controllers.
- Use the Input System's `PlayerInputManager` (or the fork's existing input layer, per the audit) to join and leave players by button press, with a separate device and control scheme per player.
- Shared camera and playfield; the playfield is sized so up to the maximum player count stays readable (maximum is open, D-110).
- Per-player ship, score, and lives; ships need clear visual identity (color, marker) so players can tell themselves apart in busy waves.
- Scale difficulty with player count: spawn budget, enemy health, and bullet density, all still capped by the fairness layer (D-111).
- Handle mid-side join/leave: a joining player enters at a safe spawn; leaving removes the ship without affecting the level.
- Decide co-op rules: shared lives or individual, friendly fire, revive (D-107).

## Step 5: Integrating the audio modules

Two paths, decided by experiment 06:

1. **Sidecar:** Python processes send the message contract over UDP/OSC to the Unity host. Fastest to build and iterate; adds a second program to run.
2. **Native:** port capture, features, and side tracking to C#. Single program; capture latency in Unity (`Microphone`) must be acceptable, or use a native plugin or a library such as NAudio/PortAudio bindings.

Either way, keep the module boundary and message contract. The game should run with a **replay source** (recorded features) or a **null source** (no audio) so development never requires the hardware.

## Step 6: Boss and level flow

- Boss spawn, phases, and defeat conditions tied to remaining side time where available.
- Level-complete screen: score, per-player stats, record/side info (and album art if identified).
- "Flip the record" prompt to start side B.
- Fallback flow when the side end is detected before the boss is defeated (time-out finish) or the boss is defeated early (victory lap until the side ends).

## Milestones

1. Audit complete; fork builds and runs.
2. Vertical scrolling playable with hand-authored waves.
3. Replay-driven generator playing from recorded feature streams.
4. Live bridge (experiment 06) driving the generator.
5. Local co-op (experiment 07).
6. Vertical slice: one full side, end to end (experiment 08).
