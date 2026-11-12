# Architecture

## Modules

Four loosely coupled modules, so the experiments can be built in Python first and either kept as a sidecar or ported to C# later.

```
 line-level source ──► [hardware splitter] ──► speakers (zero latency)
                              │
                              ▼
                      USB audio interface
                              │
        ┌─────────────────────┴──────────────────────┐
        ▼                                            ▼
 1. Capture + Analysis                      2. Identification
    ring buffer, FFT, onsets,                  clip → fingerprint API → artist/title
    bands, RMS, tempo                          artist/album → Discogs → release, tracklist
        │                                            │
        └──────────────► 3. Session / Side Tracker ◄─┘
                           which track, elapsed on side,
                           expected remaining, run-out detection
                                       │
                                       ▼
                              4. Game (Unity)
                           level generator, scrolling shooter,
                           local co-op (one machine)
```

## Message contract

Modules talk over a small, versioned message format (JSON over UDP, or OSC). Keep it boring so Unity can consume it directly.

**Features, ~60 Hz**
```json
{ "v": 1, "type": "features", "t": 123.456,
  "onset": true, "onset_strength": 0.82,
  "bass": 0.61, "mid": 0.34, "treble": 0.12,
  "rms": 0.40, "tempo": 118.0, "tempo_confidence": 0.7 }
```

**Identification events, as they happen**
```json
{ "v": 1, "type": "track_identified", "t": 132.0,
  "artist": "...", "title": "...", "album": "...", "confidence": 0.9 }
{ "v": 1, "type": "release_selected", "discogs_id": 0,
  "side": "A", "tracks": [ { "pos": "A1", "title": "...", "seconds": 215 } ] }
{ "v": 1, "type": "identification_failed", "t": 150.0, "reason": "no_match" }
```

**Side state**
```json
{ "v": 1, "type": "side_state", "t": 140.0,
  "side": "A", "track_index": 1, "side_elapsed": 410.2,
  "side_expected": 1320.0, "side_confidence": 0.6 }
{ "v": 1, "type": "side_end", "t": 1325.0, "cause": "run_out_groove" }
```

Every message carries `v` (schema version) and `t` (seconds on the sender's monotonic clock). Unknown fields must be ignored.

## Latency model

- **Audible path:** source → splitter → speakers. Never touches the computer, so there is no audio latency to manage.
- **Analysis path:** interface → capture buffer → features. Capture buffer plus FFT window sets the floor (roughly 20-50 ms with a small buffer).
- **Game delay:** the game deliberately renders events 150-300 ms after the audio that caused them, so spawns can be placed ahead of the player and scroll into place in time with the music. This delay is a user-calibrated setting (like input offset in rhythm games).
- Because the analysis path is deterministic, calibration is: play a click track or a clap, measure the offset between what you hear and what the game shows, store the offset.

## Identification is asynchronous and optional

Identification runs on its own schedule (clip every N seconds until matched, then periodically to detect track changes). Results arrive late, sometimes wrong, sometimes never. The game consumes them as hints and corrects itself when new information arrives.

## Multiplayer (local only)

Multiplayer is same-machine co-op (decision D-009). There is one process, one clock, and one audio feed, so none of the usual networked-game machinery is needed: no host/client roles, no replication of spawns, no game-time offset estimation, no netcode library.

- The game reads one feature stream and one set of side events, and runs one level generator.
- All players share the same playfield and camera. Per-player state is limited to ship, input device, score, and lives.
- Player count is an input to the generator (see difficulty scaling in the Unity fork plan), not a reason to change the message contract.
- Input uses the engine's multi-device support (e.g. Unity Input System `PlayerInputManager`); the audio modules neither know nor care how many players exist.
- Audio is never routed through the game (see Latency model); players hear the source live through the room's speakers.

## Where code lives

| Concern | Prototype | Long-term |
| --- | --- | --- |
| Capture, FFT, onsets | Python (`sounddevice`, `numpy`, `librosa` for offline checks) | Sidecar process, or C# port if Unity capture latency is acceptable |
| Identification | Python | Python sidecar or Unity-side HTTP client |
| Side tracking | Python | Likely C# in the game, consuming events |
| Game, generation, local co-op | Unity | Unity |
