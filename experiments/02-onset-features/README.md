# 02 · Onset and feature extraction

**Status:** in progress. The explorer and feature extraction run live, from WAV, and offline; measured results still need the test corpus (1.2) and the real hardware. · **Plan:** [docs/experiments.md](../../docs/experiments.md#02--onset-and-feature-extraction) · **Depends on:** 1.2 (test corpus) for results, 0.3 (message contract) for log replay

## Goal

Turn audio into the signals the game could use, and see what the music actually offers: per-band triggers, band energies, loudness, brightness, tempo with beat phase, and chroma with an estimated key. Emit them in the message-contract format. Compare against offline `librosa` as ground truth.

## Success criteria

- Onsets catch the large majority of audible beats on 10 varied clips, with an acceptable false-positive rate (record actual numbers)
- Tempo converges within ~10 s on steady material; low confidence on rubato/ambient
- Adaptive thresholds handle quiet-to-loud transitions without per-track tuning
- Key matches the published key on clips where it is known (record hit rate, counting relative major/minor separately)
- Real-time on a laptop at low CPU (Pi 3 B+ numbers as a secondary data point)

## Install and run

From the repo root, with the venv active (see [../README.md](../README.md#setup)):

```sh
python -m pip install -r experiments/02-onset-features/requirements.txt   # adds pygame

python experiments/02-onset-features/explore.py --device 9                      # live window
python experiments/02-onset-features/explore.py --wav "samples/_songs/song.mp3" --listen
python experiments/02-onset-features/explore.py --wav samples/_local/clip.wav --loop
python experiments/02-onset-features/explore.py --wav "samples/_songs/song.mp3" --offline
python experiments/02-onset-features/explore.py --device 9 --headless --minutes 5
python experiments/02-onset-features/evaluate.py                                # score key + tempo
```

- **Files:** `--wav` takes WAV, MP3, FLAC, or OGG. Quote paths with spaces.
- **`--listen`** also plays the file through the speakers, in sync: the speakers' output stream drives replay, and the display waits out the reported output latency (about 90 ms here). It works for files only, never live input (D-011).
- **Window controls:** Esc or Q closes it. `[` and `]` lower and raise the trigger level gate by 3 dB; below -90 dBFS it turns off.
- **Analysis depends only on the audio,** so `--offline` gives exactly the same features as a live replay of the same WAV, just faster. Use it to compare clips or settings.
- **Logging:** every hop is written as one contract `features` message (via `flightrec.messages`) per line to `out/features-*.jsonl` (untracked). Use `--log PATH` to choose the file, or `--no-log` to skip it. Play a log back at original timing with `python tools/play_log.py out/features-....jsonl` (add `--speed 4` to go faster).
- **Summary:** each run ends with trigger counts and rates per lane, plus the final tempo, key and brightness, and the CPU cost per hop.

## What the window shows

| Panel | What it is | What to look for |
| --- | --- | --- |
| Spectrogram | 30 Hz-16 kHz, log frequency, about 9 s of history | Song sections, drum patterns, sustained notes |
| Triggers | Onsets in the **low** (20-150 Hz: kick, bass), **mid** (150 Hz-2 kHz: snare body, vocals, guitars), and **high** (2-16 kHz: hats, cymbals) bands, the full band (**all**), and predicted **beats**. Tick height shows strength. Counts on the left | Which lanes fire on which instruments; how often (summary rates) |
| Energy | bass / mid / treble / loudness on a 0..1 scale (-60..0 dBFS) | Verse vs chorus, drops, builds |
| Brightness | Spectral centroid, log scale | Dull vs bright passages, distortion, hats-heavy sections |
| Tempo | BPM, with x2 and /2 (half and double time are equally valid readings), confidence bar, beat pulse | Convergence time; confidence on steady vs loose material |
| Chroma / key | 12 pitch classes; the estimated key, with its scale notes highlighted | Whether the key settles and matches the known key |
| Meters, stats | Peak/RMS per channel, analysis cost, gate, overflows | Headroom; CPU |

## How each feature works

All of it is numpy only (no librosa in the live path), in [flightrec/audio/features.py](../../flightrec/audio/features.py), about 0.5 ms per 10.7 ms hop (around 5% of real time) on the development laptop.

- **Triggers:** spectral flux (frame-to-frame rise of log-compressed magnitude), averaged per band. A hop is an onset when it is a local peak above the band's recent median + 2.5 x MAD and at least 30% of the band's recent peak, outside an 80 ms refractory window, and the band is above the **level gate** (-45 dBFS by default). The full band weights the three bands equally. Detections are one hop (10.7 ms) late because peak picking needs one hop of look-ahead.
- **Tempo:** the autocorrelation of the full-band onset envelope over the last 8 s, smoothed over 5 hops and re-estimated every 0.5 s, searching 60-200 BPM. The lag weighting leans toward 120 BPM to choose between half and double time. Confidence is the normalized autocorrelation at the chosen lag. Beat phase comes from a comb over the envelope, and beats are only reported at confidence 0.3 or higher.
- **Key:** chroma from an 8192-point FFT (100 Hz-4.2 kHz folded onto 12 pitch classes), averaged over about 8 s, then correlated with the 24 Albrecht-Shanahan (2013) major/minor key profiles, which come from a pop/rock corpus. Confidence is that correlation. Krumhansl-Kessler profiles are available as `profile="krumhansl"`; see the evaluation below for why they are not the default.
- **Brightness:** the power-weighted mean frequency (spectral centroid).

## Findings so far (laptop mic; not results)

These come from the built-in laptop mic (Realtek, WASAPI, 48 kHz), with music through the laptop's own speakers, on 2026-11-12. They are not results for this experiment; see "Testing without the hardware" in [../01-capture-visualizer/README.md](../01-capture-visualizer/README.md#testing-without-the-hardware).

- **Quiet room, music paused (30 s):** the low band's noise ran at -58 dBFS median, -46 dBFS max. Without a gate it fired 152 times (304/min), because a band only a few FFT bins wide jumps about +/-10 dB from hop to hop, and an adaptive threshold adapts to that too. With the -45 dBFS gate it fired 6 times, and no beats were predicted once confidence gating was added. The mid and high bands stayed silent either way.
- **"Audrey, Start The Revolution!" (Anberlin), published at F major, 172 BPM (86 half-time), 30 s:**
  - **Tempo:** 86 BPM from about 10 s onward, the half-time reading, within 0.3 BPM. Confidence was modest (0.2-0.4).
  - **Key:** read as Ab minor, which is wrong. The clip's spectrum explains it: energy below 100 Hz sat 17 dB above everything else (61.5 Hz peak), and a steady 208 Hz (Ab3) resonance stood about 8 dB above any musical note. The actual musical peaks (D3, F4, G4, Bb4, C5, D5, F5) are all in F major. This is the speaker-to-room-to-mic path, not the key method; re-check on line-in before changing anything.
  - **Bands:** nothing above about 1 kHz got through this path (30+ dB down), so the high lane never fired. The low lane fired about 5 times a second, in line with eighth notes at 172 BPM.

## Key and tempo against published values (files, first pass)

`evaluate.py` analyzes every song in `samples/_songs/` (git-ignored) offline and scores it against `samples/_songs/truth.csv`. The published values come from Spotify's analysis as listed by SongBPM, Songdata, Musicstax, and GetSongBPM (Tunebat's database is also Spotify's), plus Tunebat and Chordify for one song. These are estimates themselves, not ground truth. The set is 7 songs (synthwave, metal, electronic, post-rock), 34.5 minutes in total: 5 with a published key and 6 with a published BPM. Run on 2026-11-12.

| Song | Published | Detected key | Detected tempo |
| --- | --- | --- | --- |
| Arcane, "Unturning" | E minor, 81 | **E minor** (exact) | 166 (double) |
| Glowbug, "False Metal" | Eb major, 150 | Bb major (fifth) | **150** |
| Kalax, "Calling" | C# major, 90 | **C# major** (exact) | **90** |
| Master Boot Record, "CLS.NFO" | ?, 140 | C# minor | **140** |
| standards, "Special Berry" | A major, 91 | E major (fifth) | 122 (wrong; 4/3) |
| the olllam, "Stream of Silllver" | F# minor, 174/176 | A major (relative) | 88 (half) |

- **Tempo:** 3 exact, 2 half/double (both count as right for gameplay), 1 wrong (a 4/3 reading). The most common confident tempo held for 32-97% of each song; in "Stream of Silllver" some sections read 116 (2/3 of 174, a triplet-feel reading).
- **Key:** 2 exact, 2 a fifth too high, 1 relative major/minor. With Krumhansl-Kessler profiles it was 0 exact: 3 a fifth too high (always exactly +7 semitones), 1 relative, 1 parallel. Switching to Albrecht-Shanahan fixed 2 of those. Narrowing the frequency range (100-1000 / 2000 Hz, 50 Hz floor), amplitude or log weighting, and subtracting 3rd harmonics did not consistently help.
- **Caveat:** 5-6 songs is far too few to tune on. The next step is 10-20 songs across genres, then only keep changes that improve the whole set.

## Output

When finished, write `RESULTS.md` from [../RESULTS.template.md](../RESULTS.template.md) and add the decision to [docs/decisions.md](../../docs/decisions.md).
