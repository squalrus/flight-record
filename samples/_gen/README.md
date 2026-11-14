# Generated songs

Songs the project owner generated with AI (Google Gemini), for testing the feature pipeline and level generation on whole tracks with known structure. Unlike `samples/_songs/`, these are the owner's own material.

**Tests.** `experiments/02-onset-features/test_generated_songs.py` scores the pipeline's tempo and key on every song against `truth.csv`, and skips songs whose audio is missing or only an LFS pointer. Known misses are marked as expected failures. For exact answers (every drum hit, beat, and section boundary), `tests/test_reference_song.py` uses the synthesized reference song from `flightrec.reference_song` instead; `python tools/make_reference_song.py` writes it as a WAV with its truth as JSON.

**Identification.** These songs are unreleased, so ACRCloud, AudD, and Discogs will never match them. That makes them the test case for the "no match" path the game must handle (`identification_failed` with `no_match`, then play on from the audio alone), not for identification itself. Files sent with `send.py` still get `track_identified` from their tags.

**Rights.** The generator's summary of Google's Generative AI Additional Terms: Google claims no ownership of the outputs, and they may be used and distributed, including commercially, within the Prohibited Use Policy.

## Stated vs measured

The generator does not reliably hit the tempo or key it was asked for, so every value here is measured, and `truth.csv` holds the best-known value with its source. `truth.csv` feeds `python experiments/02-onset-features/evaluate.py --songs samples/_gen`. Keys are left blank there until confirmed.

| File | Length | Genre / purpose | Stated | True tempo | Pipeline's key (held) | Pipeline's tempo (held, confidence) |
| --- | --- | --- | --- | --- | --- | --- |
| `Vector_Seven_Engaged.mp3` | 3:00 | Cyberpunk / dark synthwave | D minor, 120 | **about 126** | Eb minor (68%) | 126 (69%, 0.44) |
| `Trench_Run_Finale.mp3` | 3:00 | Chiptune metal / Nintendocore | E minor, 150 | 150.0 | D minor (64%) | 150 (58%, 0.46) |
| `Watching_the_Galaxy_Sleep.mp3` | 3:00 | Space ambient / cinematic electronic | F major, 80 | 80.0 | E major (63%) | 160 (25%, 0.46) |
| `Vanguard_of_the_Sunlit_Rim.mp3` | 3:00 | Cinematic orchestral / space opera | Bb major, 120 | 120.0 | Eb major (28%) | 120 (72%, 0.47) |
| `Constant_Velocity.mp3` | 2:51 | **Tempo test track** | 128 | **135.0** | A minor (83%) | 134 (95%, 0.70) |
| `Before_the_Silence.mp3` | 2:01 | **Key test piece** | A minor, 100 | **102.0** | A major (100%) | 102 (100%, 0.75) |

Measured on 2026-11-13. "True tempo" is the autocorrelation of a whole-song onset envelope (spectral flux at 128-sample hops) at lags of 4 and 16 beats. It is a different method from the pipeline's tracker, and the two lags agree within 0.05 BPM for every song except Vector Seven (126.0 and 124.1), whose grid is the least steady. The pipeline's columns come from `evaluate.py` (experiment 02's pipeline at its defaults; tempo in 2-BPM bins).

- **Tempo:** four songs are exactly on a grid at their stated tempo or another one. Constant Velocity is at 135, not 128, and Before the Silence at 102, not 100. Vector Seven's tempo was a generation parameter of 120, but the audio runs at about 126 (check against a 120 BPM metronome: they drift a beat apart about every 10 s). The pipeline matched the true tempo on five songs and doubled the ambient one.
- **Keys:** the stated keys of the first three songs were inferred by the generator, not set, so they are guesses. Vanguard's B♭ major was a parameter; the pipeline's E♭ major is a fifth away and unexplained until checked by ear. Before the Silence was asked for A natural minor, but its chroma has C# (10.5%) well above C (6.3%) and G# at 10.3%: the tonic A is right, the mode is major. Only 64% of its energy is on the A natural minor notes.

## Test tracks

### Constant Velocity (tempo)

Asked for: strict 4/4 at 128 BPM, kick on every beat, snare on 2 and 4, eighth-note hi-hats, no tempo changes, 16-bar sections that add or remove one layer, and a clean ending. It is 135.0 BPM, so a 16-bar section lasts 28.44 s, not the generator's stated 30 s. Section starts below are computed at 135 BPM from the first sound at 0.12 s; the 30 s window measurements agree with the layer changes. The last sound is at 169.0 s, followed by digital silence.

| Section | Start (computed) | Layers (stated) | Measured over 30 s windows |
| --- | --- | --- | --- |
| 1 | 0:00 | Drums | -32 dB, 3.0 onsets/s, little mid or treble |
| 2 | 0:28.6 | + bass | -21 dB, about 9.6 onsets/s |
| 3 | 0:57.0 | + chords | mid and treble rise |
| 4 | 1:25.4 | + lead (peak) | highest mid and treble |
| 5 | 1:53.9 | - lead | mid and treble fall |
| 6 | 2:22.3 | - chords, then final kick and chord | -26 dB, 6.9 onsets/s |

Use it for: tempo and beat-phase accuracy against a steady grid, onset recall on a known pattern (kick on every beat), and layer changes as energy steps.

### Before the Silence (key)

Asked for: A natural minor only, Am-Dm-Em-Am one chord per bar, roots in the bass, clean in-tune instruments, at 100 BPM. The audio is A major at 102.0 BPM (see above). The pipeline held A major for 100% of the song, in every 20 s window. Until it is checked by ear, use it as a test of a steady tonic, not of the minor mode.

## Songs

### Vector Seven Engaged

| Section | Time | What happens | Useful for testing |
| --- | --- | --- | --- |
| Intro and build | 0:00-0:48 | Four-on-the-floor kick, distorted synth bass, fast arpeggios | Steady onsets and beat tracking from the first bar |
| Climax | 0:48-1:19 | Heavy percussion, lead synth | Peak energy; dense onsets |
| Slowdown | 1:19-2:08 | Half-time beat, sub bass, dark pads | Tempo halving (does the tracker report 63 or hold 126?), low-band energy, sparse onsets |
| Return and outro | 2:08-3:00 | Back to full tempo, pumping bass | Recovery after the half-time section; the song's end (`side_end` with `file_end`) |

### Trench Run Finale

| Section | Time | What happens | Useful for testing |
| --- | --- | --- | --- |
| Intro and main theme | 0:00-0:38 | 8-bit square and triangle leads over metal guitars and double-kick drums | Fast, dense onsets from the start; treble-heavy chiptune against a busy low end |
| Riff-heavy assault | 0:38-1:04 | Palm-muted riffs, cascading pulse-wave lines | Sustained high onset rate |
| Slowdown | 1:04-1:55 | Half-time feel, down-tuned power chords, sub bass, reverb-heavy leads | Tempo halving at 150 (75?), low-band energy |
| Climax and outro | 1:55-3:00 | Guitar and synth duels, double-bass drums | Peak density; the song's end |

### Watching the Galaxy Sleep

Low-energy case: few or no percussive onsets. The grid is exactly 80 BPM, but the pipeline's tracker held a tempo only 25% of the time, at 160.

| Section | Time | What happens | Useful for testing |
| --- | --- | --- | --- |
| Opening | 0:00-0:24 | Sub-bass drone, shimmering pads | Near-silent onset stream: does the game stay calm instead of spawning at noise? Low band only |
| Melodic section | 0:24-1:12 | Slow synth lead melody | Pitched, soft onsets; mid band |
| Peak | 1:12-2:00 | Layered synth strings, shimmering textures | Rising energy without drums: intensity from RMS and bands, not onsets |
| Slowdown and outro | 2:00-3:00 | Near-silence, soft synth plucks | Sparse plucks against silence; a quiet ending that should not look like a run-out groove |

### Vanguard of the Sunlit Rim

The generator gave the section times as "0:16, 0:64, 0:96, 1:28", which are seconds written as minutes:seconds. Read as 16, 64, 96, and 128 s, they fall on 8, 32, 48, and 64 bars at 120 BPM. The times below use that reading; check them by ear.

| Section | Time | What happens | Useful for testing |
| --- | --- | --- | --- |
| Fanfare and opening | 0:00-0:16 | Trumpets, horns, sweeping strings | Onsets from brass attacks rather than drums |
| Heroic build | 0:16-1:04 | Unison strings, timpani, ostinatos, brass calls | Timpani as the low-band pulse; rising energy |
| Triumphant peak | 1:04-1:36 | Full orchestra | Dense, wide-band energy with soft attacks |
| Space battle | 1:36-2:08 | Fast strings, sharp brass stabs | Brass stabs as clear onsets; a combat-section template |
| Grand finale | 2:08-3:00 | Reprise of the main theme, closing chords, chimes | Sustained chords, then a soft tail of chimes before the end |
