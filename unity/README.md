# Unity project

The game: a copy of **InfinitySpace** (a top-down space shooter, from the private `squalrus/infinity-space` repo at **v0.35.3, commit `899a07e`**), brought in on 2026-11-12 to be reworked into Flight Record: horizontal scrolling (D-016), local co-op, and audio-driven levels (see [../docs/unity/unity-fork-plan.md](../docs/unity/unity-fork-plan.md)). It is a copy rather than a submodule because the gameplay changes are large; the original game continues separately.

## Open it

- **Unity 6000.3.17f1** (Unity 6). Add this `unity/` folder in Unity Hub (Add → Add project from disk) and open it with that editor version. The first open rebuilds `Library/` and takes a few minutes.
- **Git LFS required.** Images (PNG) and fonts (TTF / OTF) are stored with Git LFS. Run `git lfs install` once per machine, then `git lfs pull` if a clone shows tiny placeholder files instead of images.
- Serialization is Force Text, so scenes, prefabs, and assets are text and diff and merge in git. Commit `.meta` files with their assets.

## Identity

Renamed from InfinitySpace in `ProjectSettings/ProjectSettings.asset`, so the two games do not collide: product name **Flight Record** (window title, and the save folder `AppData/LocalLow/Squalrus Games/Flight Record/`), application id **com.SqualrusGames.FlightRecord**, a new product GUID, and version **0.12.0**, kept equal to the repo's `VERSION`. The code was renamed too (backlog 4.6): the C# namespace is `FlightRecord`, the input actions asset is `Assets/Input/FlightRecord.inputactions` (same GUID, so references hold), the editor menus are under **Flight Record**, and the title screen reads FLIGHT RECORD. The credits name InfinitySpace as the game this one is based on.

## What came over

| Folder | Contents |
| --- | --- |
| `Assets/` | Scripts, prefabs, scenes, input actions, UI (UI Toolkit and TextMesh Pro), shaders, configs, art |
| `Assets/Art/Backgrounds/` | Nebula backgrounds (about 445 MB of PNGs), made by the project owner |
| `Assets/Art/Fonts/` | Orbitron and Share Tech Mono (each with its `License!.txt`, redistribution allowed), and the project's icon font |
| `Packages/`, `ProjectSettings/` | Package manifest and lock file; project settings |
| [`../docs/unity/flight-record/`](../docs/unity/flight-record/) | Reference from InfinitySpace, kept with the repo's other docs: `AUDIT.md` (its modernization audit), `DESIGN_DOCUMENT.md`, `conventions.md` (its coding-agent notes), and `design/` (reference images and screenshots) |

## Added for Flight Record

| Folder | Contents |
| --- | --- |
| `Assets/Scripts/Audio/` | `FlightRecord.Audio` assembly: the message contract parser (mirrors `flightrec.messages`), message sources behind `IMessageSource` (UDP bridge, replay, null), `EventScheduler` (the delay buffer), and `IAudioFeed`, what the game reads: `BufferedFeed` (a source in the delay buffer) or `FilePlaybackFeed` (a file the game plays through `AudioSourcePlayback`, with the sidecar's analysis released at the playback position; D-012) |
| `Assets/Scripts/Systems/GameAudio.cs` | The game's audio input: creates itself when `Main` loads, owns the `IAudioFeed`, and publishes each message as it is heard (`GameAudio.Instance.Released`, `Latest`, `BeatPulse`). **F3** toggles a debug overlay (source, rate, bands, onsets, beat, tempo, key, track, side). Source: command line in players (`-audiofile`, `-replay`, `-nullsource`, `-bridge`, `-port`, `-delay`, `-lead`), or the menu **Flight Record → Audio Source** in the editor; default the live bridge. With no audio the game plays as before. The parallax layers swell slightly on beats (`ParallaxBackground._beatPulse`) |
| `Assets/Scripts/UI/NowPlayingHud.cs`, `IncomingWarningHud.cs` | Added to the HUD document at runtime by `VectorHudController`. **Now playing** (bottom left, styles `.now-playing` in `HUD.uss`): cover art (embedded in a played file, read by `FlightRecord.Audio.Id3Tags`; otherwise a drawn record), title, artist, genre, album, and time left on the track, side, and album (album needs a Discogs track list). **Incoming warning**: a marker on the right edge where the next `FormationSpawner` group will enter, for the last 2.5 s before it spawns, flashing on the beat (a steady blink without music) |
| `Assets/Tests/EditMode/` | Edit-mode tests for `FlightRecord.Audio` (Window → General → Test Runner, or `-runTests -testPlatform EditMode` in batch mode) |
| `Assets/Experiments/Bridge/` | Experiment 06's scene and its builder (menu **Flight Record → Experiments**); see [../experiments/06-message-bridge/README.md](../experiments/06-message-bridge/README.md) |

**Not brought over:** generated folders (`Library/`, `Logs/`, `UserSettings/`), InfinitySpace's backlog, changelog, and release history, and its Android build workflow (Flight Record targets desktop first).

## Rules

- Never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/`, builds, or IDE project files; `../.gitignore` covers them.
- New large binaries (textures, audio, models) go through LFS. Check `../.gitattributes` covers the file type before committing.
- Read the audit in [../docs/unity/unity-fork-plan.md](../docs/unity/unity-fork-plan.md) Step 0 before refactoring; [../docs/unity/flight-record/AUDIT.md](../docs/unity/flight-record/AUDIT.md) covers code health and the file inventory.
- Gameplay changes are one backlog item each (Phase 4 in [../BACKLOG.md](../BACKLOG.md)), so each can be played and tested on its own.
- Keep audio integration behind an interface with live, replay, null, and file sources (backlog 4.3).
