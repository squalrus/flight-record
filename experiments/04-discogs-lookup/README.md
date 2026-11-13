# 04 · Discogs release lookup

**Status:** in progress. Lookup, side parsing, the one-time confirmation, the local cache, and a live panel work; the 20-record vinyl test set still needs real records. · **Plan:** [docs/experiments.md](../../docs/experiments.md#04--discogs-release-lookup) · **Depends on:** `DISCOGS_TOKEN` in `.env.local` or `.env`

## Goal

From artist and album, find the pressing, parse vinyl positions (A1, A2, B1…) into sides, compute side durations where present, handle multiple pressings with a one-time player confirmation, and cache locally.

## Success criteria

- 20-record set resolves to the correct release automatically or with one confirmation
- Documented fallback for missing durations
- Local cache prevents repeat lookups

## Setup

Get a free personal access token at [discogs.com/settings/developers](https://www.discogs.com/settings/developers) ("Generate new token"; not the app consumer key / secret) and put it in `.env.local` as `DISCOGS_TOKEN=...`. Both env files are git-ignored. Requests send a generic User-Agent with no personal details.

## Run

From the repo root, with the venv active:

```sh
python experiments/04-discogs-lookup/lookup.py --artist Kalax --album III --track Calling
python experiments/04-discogs-lookup/lookup.py --songs samples/_songs            # every song's tags
python experiments/04-discogs-lookup/lookup.py --songs samples/_songs --confirm  # ask when unclear

# Live: identify what is playing, then show where on the record it is
python experiments/02-onset-features/explore.py --wav "samples/_songs/song.mp3" --listen --identify acrcloud --discogs
```

Each record goes through these steps:

1. **A remembered choice wins.** Confirmed and automatic choices are stored in `samples/_local/discogs/selections.json`, so a known record never runs the lookup flow again.
2. **Search:** artist + album, then the album without bracketed editions ("Altered State (Deluxe Edition)" becomes "Altered State"), then the recognition service's alternate albums the same way, then artist + track (this rescues albums Discogs does not list under that name), then free text.
3. **Score the results:** album title match (exact, or sharply discounted near-misses, so "II" never beats "III"), artist match, vinyl format (scaled by the title match), the label and year the recognition service reported, and a known year.
4. **Choose.** A release found *only* by track title, where no album matched, is trusted automatically only if its label or year agrees with the identification. Otherwise it is "unconfirmed" and never remembered: a lone wrong hit, like a 2021 digital *Nocturne* for TesseracT's 2013 *Altered State*, cannot stick. Otherwise: pick automatically when one candidate clearly wins. If the top candidates tie but are all pressings of the same **master release** (the album that groups every pressing), pick automatically too. Pressings of one album share sides and running order, so for side tracking any of them will do. The master's main release is used when it is among them, otherwise the earliest tied vinyl pressing. Only ties across different masters ask once: `--confirm` in the script, or keys **1-5** in the explorer's release panel. In the script you can also type a catalog number or barcode from the sleeve; punctuation is ignored. Without an answer the top candidate is used and marked **unconfirmed** (not remembered).
5. **Fetch the release, fill missing durations from its master's tracklist** (matched by title), **and parse it:** vinyl positions `A1`, `B12`, `AA1`, `A1a`, `A-1`, `a1`, or a bare `A` for a one-track side. CD and digital positions (`3`, `2-03`) have no side. Medley sub-tracks are expanded and headings skipped. The identified track is matched by title. Failing that, "Simulacra, Pt. I" matches a single "Simulacra" (the vinyl joins the parts), and "Of Mind - Nocturne" matches "Nocturne" (one title contains the other; at least 5 characters, so short titles do not match by accident).

API responses are cached under `samples/_local/discogs/http/` (git-ignored): re-runs cost no requests. Requests are spaced to stay under the rate limit, with backoff on HTTP 429.

In the explorer, `--discogs` adds a **release panel** next to the identification panel. It shows the release (title, year, country, format, catalog number), the side and position of the playing track, the time left on the track and on the side, and the side's tracklist with the current track highlighted. When the pressing is ambiguous (now only across different master releases), it lists candidates as year, country, catalog number, and distinguishing format details (Limited Edition, Repress, a color) to pick with 1-5. Lookups run on a background thread. Each one is logged as a contract `release_selected` message.

Code: [flightrec/discogs.py](../../flightrec/discogs.py) (client, parsing, scoring, `resolve`, `ReleaseTracker`, `progress`).

## Recognition hints, release details, and agreement

When a lookup comes from a live identification, the recognition service's extras sharpen it:

- **Barcode first.** If ACRCloud reports a UPC (it does when the project has external IDs turned on in the ACRCloud console), a Discogs barcode search identifies the release directly.
- **Alternate albums.** ACRCloud often returns more than one match. Their album names are tried when the main album is not on Discogs. For example, "Special Berry" came back on *Starfish* (which turns out to be track A3 on *Fruit Island*) with the single *Special Berry* as an alternate.
- **Label.** A candidate on the label the service named gets a bonus. "Electronic Purification" matches "Electronic Purification Records", so one name containing the other counts.

Each lookup also carries release details:

- **Genres and styles:** Discogs styles are finer than any recognition service's genres, e.g. Synthwave / Synth-pop, Math Rock, Chiptune / Indie Pop, Prog Rock.
- **The original year** (from the master), label and catalog number, and community have / want counts.
- **Barcodes and matrix / runout codes:** the codes etched in the dead wax identify the exact pressing; a future way to confirm a record.

The genres, styles, and year also go into the `release_selected` message.

**Agreement:** the release panel shows how well the identification and the release corroborate each other:

- **track:** the identified track is in the tracklist (with its position).
- **artist:** the artists match.
- **duration:** the service's track length is within 5 s of the tracklist's.
- **label:** the labels match.
- **year:** the service's release year is within a year of the release's or the original.

In the explorer, **F** forgets the remembered choice for the current record and looks it up again, to undo a wrong automatic pick. Each check shows ok, no, or "-" when a side has no data. A label credited to a digital distributor (DistroKid, CD Baby, TuneCore, and so on), as self-released digital music often is, shows "-" rather than a false "no". Independent sources agreeing is the confirmation. On "Special Berry": track, artist, label, and year agree, and duration is "-" because that release lists no durations.

**Cover art:** the release panel shows the release's primary image as a thumbnail, falling back to the master's image. It is downloaded once on the background thread and cached in `samples/_local/discogs/images/` (git-ignored). Showing cover art locally is fine; Discogs' terms and image licensing apply to anything public (see [docs/risks-and-open-questions.md](../../docs/risks-and-open-questions.md)).

**Barcodes in practice:** with ISRC and UPC turned on in ACRCloud, "Special Berry" came back with UPC 0634457029385. Discogs has no release with that barcode (it is most likely the digital *Starfish* release), so the alternate-album and track-title fallbacks found the vinyl. A barcode helps only when the matched release is one Discogs lists.

## Fallback when durations are missing

Discogs durations are often absent: in the first pass, only 2 of 6 vinyl releases had any. In order:

1. **Discogs durations,** where present: the release's own, then its master release's (matched by track title).
2. **The recognition service's track length.** ACRCloud reports `duration_ms` for the identified track (logged as `track_seconds`). It replaces that track's missing or estimated duration.
3. **The position in the track.** ACRCloud's `play_offset_ms` / AudD's `timecode` (logged as `offset_seconds`) plus the audio time since the clip gives the time left on the current track, with or without Discogs.
4. **The release's median track length** fills other missing durations. The side length is then marked **estimated** in the panel and in `SideLength.estimated`.
5. **No durations at all:** side length is unknown. Side tracking (experiment 05) falls back to re-identification and acoustic cues: gaps between tracks, the run-out groove.

## Findings so far (not results)

The first pass, on 2026-11-12, looked up the 7 songs in `samples/_songs` by their tags (artist, album, title). The table shows results with master-release resolution:

| Song | Outcome | Release | Durations |
| --- | --- | --- | --- |
| Arcane, "Unturning" | auto | a file-only release: no vinyl sides | n/a |
| Caspro, "Better Off Alone" | auto (same master, 2 tied) | *Pastel*, vinyl, side A, A1 | 9 / 9 (all from the master) |
| Glowbug, "False Metal" | auto | *Headhunters*, vinyl, side A, A3 | 0 / 11 |
| Kalax, "Calling" | auto (same master, 5 tied) | *III*, 2019 vinyl, side C, C13 (positions run on across sides) | 15 / 15 |
| Master Boot Record, "CLS.NFO" | auto (same master) | vinyl, side B, B2 | 7 / 7 (all from the master) |
| standards, "Special Berry" | auto (same master, 6 tied) | *Fruit Island*, vinyl, side A, A4 | 0 / 12 |
| the olllam, "Stream of Silllver" | auto | *Elllegy*, 2LP, side B, B1, 6:43 (matches the file's 402.9 s) | 8 / 8 |

- **Every lookup found a release containing the track,** and the track's side and position.
- **Master releases resolve the ambiguity:** 4 of 7 had several tied pressings, always of a single master. Grouping by master turned all of them into automatic picks, so no confirmation was needed for this set. Without it, Kalax's five pressings (one catalog number, differing only by year and "Repress") would each have needed a question.
- **Durations:** the master's tracklist filled every missing duration for Caspro and Master Boot Record. Full durations went from 2 to 4 of the 6 vinyl releases. Glowbug and standards have none on the release or the master, so the identified track's length from ACRCloud is the fallback there.
- **"Which release":** the recognition services named *Starfish* for "Special Berry". Discogs has no such album for that artist, so the track-title fallback found the track on a *Fruit Island* pressing instead.
- **Requests:** 17 for the whole first pass (searches, releases, and 4 masters); re-runs come from the cache. Live in the explorer, a remembered record cost no requests.

## Output

When finished, write `RESULTS.md` from [../RESULTS.template.md](../RESULTS.template.md) and add the decision to [docs/decisions.md](../../docs/decisions.md).
