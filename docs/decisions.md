# Decisions

A short log of decisions that have been made and decisions still open. Add an entry whenever an experiment or discussion settles something; link evidence (a `RESULTS.md`) when there is any. Newest decisions go at the bottom of each section.

Statuses: **Decided**, **Provisional** (working assumption, to be validated by an experiment), **Open**.

## Decided / provisional

### D-001 · Split the audio in hardware — Decided
Audio is split at line level: one leg to speakers, one to a USB audio interface feeding the computer. The player hears the source with zero added latency, and any source works (turntable via phono preamp, CD, MP3 player). The game never sits in the audible path. See [hardware.md](hardware.md). Audio *files* played by the computer itself are the one exception (D-012).

### D-002 · Real-time analysis, not pre-analysis — Decided
The game must work for any record in real time. No hand-authored or pre-computed beat maps. Live features plus a deliberate render delay (150-300 ms, user-calibrated) give the generator lead time. See [architecture.md](architecture.md).

### D-003 · Genre and structure: vertical scrolling shoot 'em up, one side = one level — Decided
Chosen over an endless-runner/platformer concept. The existing Unity project is a top-down space shooter, so this iteration is a fork converted to vertical scrolling with local multiplayer. The side ends with a boss.

### D-004 · Identification is optional enhancement — Decided
Fingerprinting and Discogs improve structure (track changes, side length) but never gate play. Acoustic cues and live analysis alone must still produce a playable level and a side end.

### D-005 · Prototype in Python, then merge into Unity — Decided
Experiments are standalone Python projects. Whether audio modules stay a sidecar or are ported to C# is decided by experiment 06 (see D-101).

### D-006 · Multiplayer authority: host owns the audio and spawns — Superseded by D-009
Originally a host-authoritative networked design (host generates the level and replicates a spawn schedule to remote clients). Superseded when multiplayer was scoped to local-only.

### D-007 · Develop on a laptop; Raspberry Pi is secondary — Provisional
The owned Pi 3 Model B+ has no audio input and limited headroom. Use it only as an optional data point for analysis experiments; a Pi 4/5 is the realistic target for a standalone console version.

### D-008 · Working title: Flight Record — Provisional
Pending a trademark and availability check. See [naming.md](naming.md).

### D-009 · Multiplayer is local only — Decided
Same machine, one shared screen, several controllers. No networking, no netcode library, no host/client roles, no audio streaming. All players hear the source through the room's speakers via the hardware split (D-001). This removes spawn-schedule replication, game-time synchronization, network join/leave, and host migration from scope, and replaces experiment 07 with a local co-op prototype.

### D-010 · Pygame for experiment visualizers — Provisional
Experiment 01 draws its waveform, spectrum, and meters with Pygame rather than matplotlib: it redraws at 60 fps with simple 2-D primitives, while matplotlib animation is built for plots, not live displays. Pygame is an experiment-only dependency (`experiments/01-capture-visualizer/requirements.txt`). Every visualizer has a `--headless` mode, so analysis, logging, and CI never need it. Revisit if a later experiment needs plotting features Pygame lacks.

### D-011 · Dev-only playback when replaying files — Decided
The explorer's `--listen` plays a replayed **file** through the computer's speakers so recordings can be explored by ear. This is a deliberate, narrow exception to D-001: the product's audible path stays in hardware, and `--listen` is refused for live input. The output stream's callback drives replay, so speakers and analysis share the sound card's clock, and the display waits out the reported output latency to stay in sync. Code: `PlaybackSource` in `flightrec/audio/sources.py`.

### D-012 · Audio files are a supported input — Provisional
Alongside line-in (vinyl, CD, any line-level source via the interface) and a microphone, the game accepts audio files (MP3, FLAC, OGG, WAV) that the player owns. For files, the computer *is* the source, so the game plays them itself (extending D-011 from development to the product), and line-in keeps the hardware path from D-001. Files run through the same analysis pipeline as live input, with three advantages: analysis can run a few hundred ms ahead of playback, so no deliberate render delay or calibration is needed (D-002's real-time pipeline is kept, just fed early); tags give artist and title without fingerprinting; and the file length gives exact time remaining, so side tracking is trivial. What one level means for a file or an album is still D-108. Today, files work for replay, evaluation, and `--listen` in the experiments; in-game file playback is part of the audio source abstraction (backlog 4.3).

## Open

| ID | Question | Decided by |
| --- | --- | --- |
| D-101 | Sidecar (Python) vs port audio modules to C# | Experiment 06 |
| D-102 | Which recognition service, clip length, retry cadence | Experiment 03 |
| D-104 | License for the repository | Owner |
| D-106 | Playfield: true portrait vs fixed-width column in landscape | Unity fork work |
| D-107 | Co-op rules (shared vs individual lives, friendly fire, revive) | Playtesting |
| D-108 | What a "level" means for non-vinyl sources (per track, per N minutes, per album) | Design |
| D-109 | Public distribution plan (personal, itch.io, Steam), which affects legal items | Owner |
| D-110 | Maximum player count and supported input devices (gamepads only, or shared keyboard too) | Experiment 07 |
| D-111 | How difficulty and screen layout scale with player count | Experiment 07 + playtesting |

D-103 (netcode library) and D-105 (remote players hearing the music) were withdrawn by D-009 and their IDs are not reused.
