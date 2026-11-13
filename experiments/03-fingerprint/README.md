# 03 · Fingerprint a clip

**Status:** in progress. The harness and live identification work against ACRCloud and AudD; real-vinyl results still need the clips from 1.2. · **Plan:** [docs/experiments.md](../../docs/experiments.md#03--fingerprint-a-clip) · **Depends on:** 1.2 (vinyl clips) for results, API keys in `.env.local` or `.env`

## Goal

Send 10-15 s clips from 20+ records to one or two recognition services (ACRCloud, AudD; ShazamKit only with a suitable client path). Log artist/title/album, confidence, latency, correctness.

## Success criteria

- Hit rate and median time-to-identify per genre and record condition
- A decision on service, clip length, retry cadence, and the no-match behavior

## Setup

1. Sign up for the services under test and check their current free-tier limits, commercial terms, and what audio they keep (see [docs/risks-and-open-questions.md](../../docs/risks-and-open-questions.md)).
2. Copy `.env.example` to `.env.local` (or `.env`; both are git-ignored) and fill in `ACRCLOUD_HOST`, `ACRCLOUD_ACCESS_KEY`, `ACRCLOUD_ACCESS_SECRET`, and/or `AUDD_API_TOKEN`. The ACRCloud host is your project's identify host, such as `identify-eu-west-1.acrcloud.com`. Never paste keys into docs, issues, or logs.

## Run

From the repo root, with the venv active:

```sh
# Batch: clips cut from every song in samples/_songs, scored against the files' tags
python experiments/03-fingerprint/fingerprint.py --dry-run                 # plan and count, send nothing
python experiments/03-fingerprint/fingerprint.py --services acrcloud --lengths 10 --positions 0.5 --conditions clean --max-requests 7
python experiments/03-fingerprint/fingerprint.py --max-requests 60         # everything, both services

# Live: the explorer identifies what is playing, in its own panel
python experiments/02-onset-features/explore.py --wav "samples/_songs/song.mp3" --listen --identify acrcloud
python experiments/02-onset-features/explore.py --device 9 --identify audd   # music in the room
```

- **Batch** (`fingerprint.py`) cuts clips at `--positions` (fractions of the song, default 0.15 / 0.5 / 0.85) and `--lengths` (default 5 / 10 / 15 s), under two `--conditions`. **clean** is the file as-is. **vinyl** is a rough simulation: wow at 33 1/3 rpm, treble roll-off, rumble, hiss, and crackle. It is not a substitute for real records.
- **Scoring:** answers are checked against the files' tags (or the "Artist - Album - NN Title" filename). Outcomes are **correct**, **other_version** (same title, different artist: a cover, remix, or the original), **wrong**, **no_match**, and **error**.
- **Caching and limits:** every answer is cached in `out/cache/` (git-ignored), so re-runs and re-scoring are free. Only uncached requests count toward `--max-requests`, and errors are not cached. Results go to `out/fingerprint-*.csv`, and `--save-clips DIR` writes the clips so you can listen to them.
- **Live** (`explore.py --identify`) sends the last `--identify-clip` seconds (default 10) on a background thread, so the window never waits. Until something matches it retries every `--identify-retry` seconds (default 8); after a match it re-checks every `--identify-recheck` seconds (default 30), to notice track changes. It stops after `--max-identify` requests (default 20). Intervals count from when the previous answer arrived. Each attempt is logged as a contract `track_identified` or `identification_failed` message in the session log. AudD reports no score, so its matches are logged at confidence 1.0.

Code: clients and the background `Identifier` are in [flightrec/identification.py](../../flightrec/identification.py), the `.env` loader in [flightrec/env.py](../../flightrec/env.py), and the tag reader in [flightrec/tags.py](../../flightrec/tags.py).

## Findings so far (clean digital clips; not results)

From 7 songs (synthwave, metal, electronic, post-rock, mostly smaller artists), one 10 s clean clip from the middle of each, on 2026-11-12:

| Service | Correct | Median time | Notes |
| --- | --- | --- | --- |
| ACRCloud | 7 / 7 | 0.84 s | Score 1.0 on every match, album included |
| AudD | 7 / 7 | 1.77 s | No confidence score; same answers as ACRCloud |

- **Covers:** both identified Caspro's cover of "Better Off Alone" as the cover, not the Alice Deejay original.
- **Album:** "Special Berry" came back on *Starfish*, while the file is from *Fruit Island*. The same recording is on more than one release, which is the pressing problem experiment 04 (Discogs) has to solve even for clean files.
- **Live:** a headless replay of Kalax's "Calling" with `--identify acrcloud` matched 1.27 s after the first 10 s clip ended.
- **What is still open:** shorter clips (5 s), the simulated-vinyl condition, positions near the start and end, and above all real vinyl through the interface, where surface noise is the main risk.

## Output

When finished, write `RESULTS.md` from [../RESULTS.template.md](../RESULTS.template.md) and add the decision (D-102: service, clip length, retry cadence, no-match behavior) to [docs/decisions.md](../../docs/decisions.md).
