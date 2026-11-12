# Risks and Open Questions

## Technical risks

| Risk | Why it matters | Mitigation / experiment |
| --- | --- | --- |
| Vinyl surface noise breaks fingerprinting | Recognition services are tuned for clean digital audio; obscure pressings and live recordings may never match | Measure hit rate (03). Game must be fully playable without identification. |
| Wrong pressing selected on Discogs | Dozens of releases per album, with different tracklists and durations | Let the player confirm once, cache locally (04). |
| Missing or wrong durations | Side length estimate is unreliable | Combine with re-fingerprinting and acoustic cues; never trust duration alone (05). |
| Run-out groove detection false positives/negatives | Ending a level too early is worse than too late | Measure over many playthroughs; prefer a conservative trigger plus a fallback timer (05). |
| Live tempo estimation is unstable | Rubato, ambient, or changing tempos give bad tempo | Expose confidence; map tempo to gameplay only when confident (02). |
| Unity audio-capture latency | `Microphone` capture is known to add latency | Prove or avoid with the sidecar path (06). |
| Ground loops / hum | Sources through splitters can introduce buzz that confuses analysis | Isolators; hardware test checklist. |
| Difficulty does not scale fairly with player count | Audio-driven density is unpredictable, and more ships change how hard a wave is | Player count as a generator input, capped by the fairness layer; playtest 1-4 players on the same recorded stream (07). |
| Difficulty is unpredictable | Music of any density becomes the level | Fairness layer with density caps and breathing room. |
| Raspberry Pi 3 B+ is too weak for the full game | Real-time analysis plus rendering | Develop on a laptop; use the Pi for analysis-only experiments. |

## Legal and terms-of-service notes

(Not legal advice; verify before shipping anything public.)

- **Shazam:** no official public REST API. ShazamKit is the sanctioned route but is oriented to Apple platforms and Android. Unofficial wrappers can violate terms and break without notice. Prefer a commercial recognition API with clear terms for anything public.
- **Recognition APIs (ACRCloud, AudD, etc.):** check free-tier limits, commercial-use terms, and what audio they retain.
- **Discogs API:** requires a token, has rate limits, and has terms around caching and displaying data and images. Review before shipping cached data or album art.
- **Album art and track metadata:** displaying cover art and titles in a public build has licensing implications.
- **Recorded audio samples in the repo:** do not commit full copyrighted tracks. Keep short clips for testing, or keep them out of version control.
- **No audio streaming.** Play is local-only (D-009) and the music is heard live through the room's speakers, so no streaming or broadcasting rights issue arises. Revisit only if networked play is ever added.
- **Name and trademark:** run a search before settling on a name (see naming.md).

## Open decisions

1. Is record identification automatic, player-confirmed, or hybrid? (Current lean: automatic with one-time player confirmation for the pressing.)
2. Sidecar vs native port of the audio modules (decided by experiment 06).
3. Maximum player count and supported input devices: gamepads only, or a shared keyboard too (experiment 07).
4. How difficulty and screen layout scale with player count (experiment 07 and playtesting).
5. Playfield: true portrait, or a fixed-width column in landscape?
6. Co-op rules: shared vs individual lives, friendly fire, revives.
7. Is a standalone Pi "console" version a goal, or a nice-to-have?
8. What does a non-vinyl source (CD, MP3) use as its "level"? Per track, per N minutes, or per album?
9. Public distribution plan: personal project, itch.io release, or something else? This affects the legal items above.

## Assumptions to validate early

- A hardware split at line level gives a clean-enough signal for analysis on typical home setups.
- A 150-300 ms game delay is enough lead time to place spawns fairly and still feel synchronized.
- Players will tolerate, or enjoy, a game whose pacing they do not control.
