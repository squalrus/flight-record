# Unity Fork Plan

The starting point is an existing Unity **top-down space shooter**. This repo's Unity project is a fork of it, reworked into a **vertically scrolling shoot 'em up** with **local (same-machine) multiplayer** and **audio-driven level generation**.

The existing project is **InfinitySpace** (v0.35.3), copied into `unity/` on 2026-11-12. Step 0 below records what it is; Steps 2 onward are the plan for changing it.

## Step 0: Audit the existing project (done 2026-11-13)

The checklist was:

- Unity version, render pipeline (URP/Built-in), input system (old/new)
- How the camera works and what "top-down" assumes (free 2D movement? rotation-based aiming? world-space bounds?)
- How enemies, bullets, and waves are defined (prefabs, ScriptableObjects, hand-placed in scenes?)
- Whether any networking exists today (this project is local-only: remove or leave disabled any networking code, do not extend it)
- What is reusable as-is: ship controller, weapons, health, pickups, UI, VFX, audio

The findings come from reading the code, settings, scenes, and prefabs of InfinitySpace v0.35.3, about 6,700 lines of C# in 61 scripts. [infinityspace/AUDIT.md](infinityspace/AUDIT.md) is InfinitySpace's own earlier code-health audit, and complements this one; its design document and reference images are alongside it in [infinityspace/](infinityspace/).

### Engine and settings

| Area | Finding |
| --- | --- |
| Unity | **6000.3.17f1** (Unity 6) |
| Render pipeline | **Built-in.** No URP or HDRP asset in Graphics or Quality settings. 2D Sprite and Tilemap packages. |
| Input | **New Input System only** (1.11.0, `activeInputHandler: 1`). One asset, `InfinitySpace.inputactions` (now `FlightRecord.inputactions`, backlog 4.6), with one map "Player": Move (value), Fire (value), Pause, ShipManagement. Control schemes: Keyboard&Mouse and Gamepad. A single `PlayerInput` component (message callbacks such as `OnMove` and `OnFire`) is on the player. There is **no `PlayerInputManager`** and no generated C# input class. |
| Camera | Orthographic, size 20. Default window 1024x768 landscape, with all orientations allowed (a holdover from mobile). |
| UI | **UI Toolkit** (UXML / USS) for every screen and the HUD (`VectorHudController`), plus TextMesh Pro fonts. Ship health bars are uGUI `Image`s. Gamepad UI navigation (`GamepadUiNavigation`) and a rumble setting exist. |
| Audio | **None.** No `AudioSource`, `AudioClip`, or mixer anywhere. That suits Flight Record: the music plays in the room and is never routed through the game (D-001). |
| Scenes | `Start` (menus) → `Main` (one sector; reloaded for each new sector) → `GameOver`. |
| Persistence | `GameControl` singleton (`DontDestroyOnLoad`). Profile, lifetime stats, and settings go to `stats.json` in `persistentDataPath` (JSON). |
| Platforms | Desktop plus an Android heritage: `FloatingJoystick`, `TouchControls`, autorotation, and InfinitySpace's Android build workflow (not brought over). |

### Camera and what "top-down" assumes

- **The camera follows one ship.** `CameraFollow` centers on a single `Target` (the player) every frame, with additive size offsets for speed zoom and screen shake. `BackgroundTiler` keeps a 3x3 grid of background tiles around the camera, so space is endless in every direction, and the BG Deep / Mid / Fore layers give parallax.
- **Free 2D flight with physics.** `PlayerController` turns the ship to face the stick direction (`Atan2`), then pushes it with `AddForce`, which burns fuel. Momentum carries on, and when there is no input the rotation freezes.
- **Aiming by rotation.** Shots leave the ship's `Cannon` along its facing, and projectiles inherit the ship's velocity.
- **World bounds are a square arena,** 50 units by default. An asteroid-belt ring marks the edge but is only decoration.

### Enemies, bullets, and "waves"

- **Classes.**
  - `DestroyableObject` (health and damage, `ICanTakeDamage`) is the base for `Asteroid`, `Collectible` (`Part`, `Relic`), and `Missile`.
  - `Ship` (health, cannon, ammunition, fire power with variance, cooldown, speed, rotation speed, parts dropped) is the base for `Player`. `Player` adds fuel, the parts economy, upgrades, homing rockets, and scans.
  - `Enemy` is a separate component next to `Ship`: radar range, firing range, an orbit radius and speed, and idle patrol. `Probe` and `Satellite` extend it. `Triceptor` overrides it: it spins on its own axis and fires fixed cannons in volleys.
- **Enemies are prefabs:** Probe I, Probe II, Satellite IV, Triceptor, Triceptor LT. They find the player within radar range, turn to face it, orbit it, and fire within firing range.
- **Bullets** are one `Missile` prefab, plus `HomingRocket`. `Ship.FirePrimaryWeapon` spawns them from the `Cannon` transform, parented under `ProjectileContainer`. Homing is optional (`SetHoming`). The only bullet pattern beyond single aimed shots is Triceptor's fixed-cannon volley; there are no spread, ring, or stream emitters.
- **There are no waves.**
  - A level is a **sector**: a `Sector` prefab (L0-L3, Triceptor LT) that lists `EntitySpawn` entries (prefab, count, spread). It spawns every enemy and asteroid **at once, at random positions** in the square when the sector starts.
  - `SectorSpawner` chooses the sector prefab and objective (`SectorType`: ClearHostiles, Relic, LargeThreat) by sector number, with weights, from the `SectorTypeTable` ScriptableObject.
  - To clear a sector you meet its objective, then reach a portal at a random spot, within a 600 s time limit that otherwise fails the run. Difficulty is a ladder of sector tiers by sector number.

### Networking

**None.** There are no networking packages or code. The `UnityWebRequest` modules are Unity's default built-ins and go unused, so there is nothing to remove (D-009).

### Single-player assumptions (to change for co-op)

- `PlayerController.PlayerTransform` is a static field holding the one player; every `Enemy` reads it to choose its target.
- `GameObject.Find("Player")`, `FindFirstObjectByType<PlayerController>()` and `FindFirstObjectByType<PlayerInput>()` all assume a single player, as do the camera's single target and the one-player HUD.
- `GameControl` keeps a single run's and a single profile's statistics.

### What is reusable

| As-is or nearly | Needs rework for Flight Record |
| --- | --- |
| `DestroyableObject` / `ICanTakeDamage` (health, damage), `DamageNumber`, `ExplosionEffect`, `BeamEffect` | `PlayerController`: free flight becomes screen-bounded movement with a fixed upward facing |
| `Ship` weapons (cannon, cooldown, ammunition, fire power), `Missile` including homing (aimed "up" instead of along the ship's facing) | `CameraFollow` and `BackgroundTiler`: a camera that scrolls on its own, frames all players, and vertical parallax |
| Enemy prefabs and art; Triceptor's volley as the start of pattern emitters | `Enemy` AI: orbit and patrol become entry paths, formations, and turrets |
| `Collectible` / `Part` pickups with attraction (the base for power-ups and drops); the parts economy and upgrade screens are undecided (D-112) | `Sector`, `SectorSpawner`, `SectorTypeTable`: random arena spawning becomes a spawn schedule from the `LevelGenerator` (Step 3) |
| UI Toolkit screens (menus, pause, settings, game over, statistics, credits) and gamepad navigation | Portal, timeout, and sector flow become side flow (boss, side end; Step 6) |
| `GameControl` JSON persistence pattern, the profile and lifetime-stats screens | Fuel: removed, since levels last as long as a record side (D-013); ammunition infinite by default (D-014) |
| Nebula backgrounds, fonts, VFX | Touch controls, autorotation, and Android settings (desktop first) |

## Step 1: Bring the project in (done 2026-11-12)

- Copied into `unity/`: `Assets/`, `Packages/`, and `ProjectSettings/` from `squalrus/infinity-space` at v0.35.3 (`899a07e`). It is a copy rather than a submodule because the gameplay changes are large; the original continues separately.
- Renamed: product name "Flight Record", application id `com.SqualrusGames.FlightRecord`, a new product GUID, and the version kept equal to the repo's `VERSION`. Saves no longer collide with InfinitySpace's.
- Renamed in code (backlog 4.6): C# namespace `InfinitySpace` → `FlightRecord` (the game code and the `FlightRecord.Audio` bridge now share a root), the input actions asset → `FlightRecord.inputactions` (same GUID), editor menus, and the title screen. The credits name InfinitySpace as the original.
- Unity entries in `.gitignore`; Git LFS for images and fonts (`.gitattributes`); `.vscode/settings.json` keeps VS Code out of Unity's generated folders. See [../../unity/README.md](../../unity/README.md).
- Opened and run in Unity 6000.3.17f1 on the development machine.

## Step 2: Convert to vertical scrolling

The direction is an arcade shooter (D-013): points, wave-clear combos, power-ups, and drops replace fuel and resource management. Each change below is its own backlog item (Phase 4 in [../../BACKLOG.md](../../BACKLOG.md)). The general shape:

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

1. Audit complete; fork builds and runs. **Done** (2026-11-13).
2. Vertical scrolling playable with hand-authored waves.
3. Replay-driven generator playing from recorded feature streams.
4. Live bridge (experiment 06) driving the generator.
5. Local co-op (experiment 07).
6. Vertical slice: one full side, end to end (experiment 08).
