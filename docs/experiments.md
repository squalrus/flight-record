# Experiments: proof-out plan

Each experiment is a small standalone Python project under `/experiments/NN-name`. The goal is to retire the riskiest assumptions cheaply before touching the Unity merge. Each has a goal, a method, success criteria, and what it unblocks.

Order matters: 01 and 02 prove the audio pipeline, 03 and 04 prove identification, 05 proves "the side is over", 06 proves the bridge to Unity, 07 proves local co-op play, 08 ties it together.

---

## 01 · Capture and visualize

**Goal:** Prove live audio from the real hardware chain can be captured and analyzed with measurable latency.

**Method**
- List audio devices; capture stereo line-in with `sounddevice` into a ring buffer (small blocks, e.g. 256-512 samples).
- Run an FFT every ~16 ms; draw a live spectrum and a waveform (Pygame or matplotlib).
- Log capture-to-display latency.

**Success criteria**
- Stable capture for 30+ minutes with no dropouts.
- Spectrum visibly tracks the music; no clipping, no hum visible.
- Measured capture-to-display latency documented (target: under 50 ms before any deliberate game delay).

**Unblocks:** everything.

---

## 02 · Onset and feature extraction

**Goal:** Turn live audio into the feature stream the game needs, and see how well it holds up across genres and sources.

**Method**
- Spectral-flux onset detection on a short window; per-frame bass/mid/treble energy; rolling RMS.
- Live tempo estimate from autocorrelation of the onset envelope over the last several seconds, with a confidence value.
- Compare live onsets against offline `librosa` analysis of the same recording as ground truth.
- Emit features in the message-contract format (print to console or log file).

**Success criteria**
- Onset detection catches the large majority of audible beats on a varied test set (suggested: 10 tracks spanning rock, funk, electronic, jazz, quiet acoustic) with an acceptable false-positive rate. Record the actual numbers.
- Tempo estimate converges within ~10 seconds on steady-tempo material and reports low confidence on rubato or ambient material.
- Quiet-to-loud transitions are visible in RMS with no manual tuning per track (adaptive thresholds work).
- Runs in real time on a laptop with low CPU use; note Raspberry Pi 3 B+ numbers as a secondary data point.

**Unblocks:** the level generator design, difficulty mapping.

---

## 03 · Fingerprint a clip

**Goal:** Learn how reliably real vinyl gets identified, and how long it takes.

**Method**
- Record 10-15 second clips from the interface at several points in each of 20+ records (common pressings and a few obscure ones; include a live album and a compilation).
- Send clips to one or two recognition services (candidates: ACRCloud, AudD; ShazamKit only if a suitable client path exists). Shazam has no official public REST API, and unofficial wrappers are a ToS gray area.
- Log artist/title/album returned, confidence, latency, and whether it is correct.

**Success criteria**
- A hit rate and median time-to-identify, per genre and per record condition, written up.
- A clear decision: which service, clip length, and retry cadence. Also a decision on what the game does when the answer is "no match".

**Unblocks:** 04, 05, and the decision on whether identification is a core feature or a bonus.

---

## 04 · Discogs release lookup

**Goal:** Go from artist and album to a specific pressing with a side-by-side tracklist and durations.

**Method**
- Use the Discogs API (free token, roughly 60 requests/min authenticated) to search by artist and album and fetch release details.
- Parse vinyl positions (A1, A2, B1…) into sides; compute per-side durations where durations exist.
- Handle missing durations and multiple pressings: present the top candidates and let the player confirm once (catalog number or barcode from the sleeve), then cache the choice locally.

**Success criteria**
- For the 20-record test set: correct release found automatically or with one confirmation, side durations available for most.
- A documented fallback when durations are missing (e.g. estimate from the fingerprinted track lengths or from elapsed time).
- Local cache means a known record never triggers the lookup flow again.

**Unblocks:** 05.

---

## 05 · Side tracking and "level over"

**Goal:** Reliably know when a side has ended, without trusting any single signal.

**Method**
- Combine three signals:
  1. Expected side length and track list from Discogs.
  2. Periodic re-fingerprinting to confirm which track is playing and to detect track changes.
  3. Local acoustic cues: needle drop (sudden onset from silence), gaps between tracks, run-out groove (drop to surface noise plus a rhythmic thump), needle lift.
- Maintain a state machine: idle → needle_down → playing(track n) → run_out → side_end.
- Log all three signals against real playthroughs and compare.

**Success criteria**
- Over 10+ full side playthroughs, `side_end` fires within a few seconds of the true end with no false positives mid-side.
- The system still reaches a sensible `side_end` when identification fails entirely (acoustic cues alone) and when Discogs durations are missing.
- Pitch/speed drift and needle-drop position are handled (a side that starts mid-way, or plays 33 vs 45 rpm, does not break the state machine).

**Unblocks:** boss timing, level-complete flow.

---

## 06 · Message bridge to Unity

**Goal:** Prove the Python modules can drive a Unity scene live with acceptable latency.

**Method**
- Send the message contract from the Python experiments over UDP (or OSC) to a minimal Unity scene.
- Unity scene: spawn a sprite on each onset, scale a bar per band, show track and side state text.
- Measure end-to-end latency and jitter (audio event to on-screen spawn).

**Success criteria**
- Spawns visibly in time with the music after a calibrated offset.
- Jitter small enough that the delay buffer (150-300 ms) fully absorbs it.
- Decision recorded: sidecar process vs port to C#. Include a test of Unity-native capture (`Microphone` / `AudioSource.GetSpectrumData`) to see whether its latency is acceptable.

**Unblocks:** the Unity fork plan.

---

## 07 · Local co-op prototype

**Goal:** Prove two to four players on one machine can play the audio-driven game together on a shared screen, with difficulty that stays fair as player count changes.

**Method**
- Add multi-device joining (gamepads, optionally a shared keyboard) with per-player ships, scores, and lives on a shared playfield.
- Drive the level from a replayed feature stream so runs are repeatable and the audio hardware is not needed.
- Play the same recorded stream with 1, 2, 3, and 4 players; log spawn counts, damage taken, deaths, and clear/fail outcome.
- Try mid-side join and leave.

**Success criteria**
- Players can join and leave mid-side without breaking the level or the generator.
- Per-player-count difficulty is playtested and documented: a 1-player run and a 4-player run of the same stream both feel fair, with density caps never exceeded.
- Ships are distinguishable at a glance during dense waves.
- Frame rate stays stable at the maximum supported player count on the target machine.
- The maximum player count and supported input devices are decided and recorded (D-110, D-111).

**Unblocks:** co-op rules and the final player-count design.

---

## 08 · End-to-end vertical slice

**Goal:** One full side of one record played start to finish.

**Method:** Put it all together in the forked Unity project: capture → features → generator → side tracking → boss → level complete, with identification running and failing gracefully.

**Success criteria**
- A complete side is playable and fun enough to want to flip the record.
- Every failure mode from experiments 03-05 (no match, wrong pressing, missing durations) leaves the game playable.

---

## Cross-cutting tasks

- Assemble a small, licensing-safe test corpus (your own records; keep clips short, don't commit full tracks).
- A shared `tools/` script to list devices, record clips, and replay recordings through the pipeline, so experiments can be re-run without the turntable.
- Write up results in each experiment folder (`RESULTS.md`) so decisions are traceable.
