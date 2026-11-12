# Concept and Design

## Pitch

Put a record on. Your ship launches. The music is the level: bass hits spawn heavy enemies, hi-hats spawn swarms and pickups, loud choruses ramp the intensity, quiet verses give you room to breathe. When the side runs out, a boss arrives and the level ends.

Works with any source that can produce a line-level signal: vinyl, CD, MP3 player, cassette. Vinyl is the flagship because a side has a natural beginning and end.

## Core loop

1. Player drops the needle (or presses play).
2. Game detects audio and starts a run.
3. Live audio features drive spawns. The game scrolls vertically; the player pilots a ship in the lower part of the screen.
4. Game identifies the track and the release; it learns the side's tracklist and expected length.
5. Track changes act as stage transitions or checkpoints.
6. Near the end of the side (or on run-out groove detection) the boss appears. Defeating it, or reaching the run-out, completes the level.
7. Score screen. Flip the record; side B is the next level.

## Audio-to-gameplay mapping (starting point, to be tuned)

| Audio feature | Gameplay effect |
| --- | --- |
| Onset / beat hit | Spawn event (enemy, bullet burst), fire timing for enemy patterns |
| Bass energy | Large enemies, turrets, screen-shake hits, heavy bullet patterns |
| Mid energy | Standard enemy formations |
| Treble / hi-hat | Small fast enemies, swarms, collectibles |
| Rolling loudness (RMS) | Difficulty and scroll speed |
| Estimated tempo | Scroll speed, enemy pattern timing grid |
| Quiet passage | Calm section: pickups, fewer enemies, recovery |
| Track change | Stage transition, checkpoint, brief intermission |
| Last track ending / run-out groove | Boss encounter, level complete |

## Why it has to work in real time

The source is arbitrary and unknown in advance, so there is no pre-analysis pass and no hand-authored beat map. Every decision uses a short rolling window of recent audio plus a deliberate delay (see architecture). Identification and Discogs data add coarse knowledge (which track, how long the side is) that live analysis cannot provide on its own.

## Design principles

- **Graceful degradation.** If the song cannot be identified, the game still plays using live analysis only. Identification adds structure; it must never gate play.
- **Source agnostic.** No turntable-specific assumptions in the game. Everything arrives as audio plus metadata.
- **Fair generation.** Spawns are scheduled with enough lead time that players can read them. Density is capped regardless of how chaotic the music is.
- **Hear it live.** Audio monitoring is hardware-side; the game never sits in the audible signal path.

## Multiplayer intent

Local, same-machine co-op only (decision D-009). There is no networked play.

- Two to four players share one screen and one playfield, each with their own controller. The exact maximum and supported devices are open (D-110).
- There is one game instance and one audio feed, so every player fights the same wave at the same moment by construction. No synchronization problem exists.
- Everyone in the room hears the record through the speakers on the hardware split, so the game never has to handle audio for players.
- Co-op is the primary mode; competitive score modes are secondary.
- Difficulty and screen layout should scale with player count (D-111): more players means more enemies or tougher ones, within the density caps.
- Players can join and leave mid-side by pressing a button on a controller; the side keeps going.

## Out of scope for the first milestone

- Networked or online play of any kind
- Pressing-specific tuning or per-record custom content
- Real-time tempo-locked pattern authoring tools
- Mobile builds
