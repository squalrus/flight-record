"""Discogs release lookup (architecture module 2, after identification).

From artist and album (and optionally the identified track), find the release, parse its
tracklist into vinyl sides with durations, and remember which release the player meant.

- ``Discogs``: a small API client over the standard library. Token auth (``DISCOGS_TOKEN``),
  a generic User-Agent with no personal details, requests spaced to stay under the rate
  limit, backoff on HTTP 429, and a local response cache so a known record never costs a
  second request.
- ``parse_position`` / ``parse_duration`` / ``tracks_from_release`` / ``sides``: vinyl
  positions (A1, B2, AA1, A1a, a bare "A" for a one-track side) into sides; CD and digital
  positions ("3", "2-03") have no side. Missing durations are estimated and flagged.
- ``score_candidates`` / ``Selections``: rank search results, pick automatically when one
  clearly wins, otherwise ask once and remember the answer.

Identification is optional (decision D-004): every caller must cope with no result.
Discogs data has terms around caching and display; see docs/risks-and-open-questions.md.
"""

from __future__ import annotations

import dataclasses
import hashlib
import json
import os
import queue
import re
import statistics
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Callable
from dataclasses import dataclass, field
from difflib import SequenceMatcher
from pathlib import Path
from typing import Any

from flightrec.messages import ReleaseSelected
from flightrec.messages import Track as MessageTrack

API = "https://api.discogs.com"
USER_AGENT = "FlightRecordExperiments/0.7"  # generic: no personal or account details

Transport = Callable[[str, dict[str, str]], tuple[int, dict[str, str], bytes]]


class DiscogsError(RuntimeError):
    """The API could not be reached or refused the request (as opposed to no results)."""


def _urllib_transport(url: str, headers: dict[str, str]) -> tuple[int, dict[str, str], bytes]:
    request = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            return response.status, dict(response.headers), response.read()
    except urllib.error.HTTPError as exc:
        return exc.code, dict(exc.headers or {}), exc.read() if exc.fp else b""
    except (urllib.error.URLError, TimeoutError) as exc:
        raise DiscogsError(f"{type(exc).__name__}: {exc}") from exc


class Discogs:
    """Discogs API client with a response cache and rate limiting.

    ``cache_dir`` holds one JSON file per request URL (the token is never part of it).
    Releases do not change often, so cached answers are reused indefinitely; delete the
    folder to refresh.
    """

    def __init__(
        self,
        token: str,
        cache_dir: str | Path | None = None,
        transport: Transport | None = None,
        min_interval_s: float = 1.05,
        sleep: Callable[[float], None] = time.sleep,
    ) -> None:
        self.token = token
        self.cache_dir = Path(cache_dir) if cache_dir else None
        self.transport = transport or _urllib_transport
        self.min_interval_s = min_interval_s
        self.sleep = sleep
        self.requests = 0
        self.cache_hits = 0
        self._last = 0.0

    @classmethod
    def from_env(cls, cache_dir: str | Path | None = None) -> Discogs | None:
        token = os.environ.get("DISCOGS_TOKEN", "").strip()
        return cls(token, cache_dir) if token else None

    def get(self, path: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
        query = urllib.parse.urlencode(
            {k: v for k, v in sorted((params or {}).items()) if v not in (None, "")}
        )
        url = f"{API}{path}" + (f"?{query}" if query else "")
        cached = self._cache_path(url)
        if cached is not None and cached.exists():
            self.cache_hits += 1
            return json.loads(cached.read_text(encoding="utf-8"))

        status, body = self._fetch(url)
        if status == 404:
            raise DiscogsError(f"not found: {path}")
        if status != 200:
            raise DiscogsError(f"HTTP {status} for {path}")
        data = json.loads(body.decode("utf-8"))
        if cached is not None:
            cached.parent.mkdir(parents=True, exist_ok=True)
            cached.write_text(json.dumps(data), encoding="utf-8")
        return data

    def _fetch(self, url: str) -> tuple[int, bytes]:
        """GET with rate limiting and backoff on HTTP 429; returns (status, body)."""
        headers = {"User-Agent": USER_AGENT, "Authorization": f"Discogs token={self.token}"}
        for attempt in range(4):
            wait = self._last + self.min_interval_s - time.monotonic()
            if wait > 0:
                self.sleep(wait)
            status, _, body = self.transport(url, headers)
            self._last = time.monotonic()
            self.requests += 1
            if status == 429 and attempt < 3:
                self.sleep(min(60.0, 5.0 * 2**attempt))  # rate limited: back off and retry
                continue
            break
        return status, body

    def image(self, url: str) -> bytes:
        """Download an image (e.g. a release cover) once; cached next to the API cache, in
        an ``images`` folder. Discogs image terms apply to anything shown publicly."""
        path = None
        if self.cache_dir is not None:
            path = (
                self.cache_dir.parent / "images" / f"{hashlib.sha1(url.encode()).hexdigest()}.img"
            )
            if path.exists():
                self.cache_hits += 1
                return path.read_bytes()
        status, body = self._fetch(url)
        if status != 200 or not body:
            raise DiscogsError(f"HTTP {status} for an image")
        if path is not None:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(body)
        return body

    def _cache_path(self, url: str) -> Path | None:
        if self.cache_dir is None:
            return None
        return self.cache_dir / f"{hashlib.sha1(url.encode()).hexdigest()}.json"

    def search_releases(self, **params: Any) -> list[dict[str, Any]]:
        """``/database/search`` for releases. Useful params: ``artist``, ``release_title``,
        ``track``, ``catno``, ``barcode``, ``format`` (e.g. "Vinyl"), ``q``."""
        params = {"type": "release", "per_page": 25, **params}
        return self.get("/database/search", params).get("results", [])

    def release(self, release_id: int) -> dict[str, Any]:
        return self.get(f"/releases/{int(release_id)}")

    def master(self, master_id: int) -> dict[str, Any]:
        """A master release: the album that groups all its pressings. Has ``main_release``
        (the release Discogs considers canonical) and a tracklist, often with durations."""
        return self.get(f"/masters/{int(master_id)}")


# Tracklists


@dataclass(frozen=True)
class Track:
    position: str
    title: str
    side: str | None
    """Vinyl side letter(s) ("A", "B", "AA"), or None for CD / digital / unknown."""
    seconds: float | None


def parse_duration(text: str | None) -> float | None:
    """'3:45' -> 225, '1:02:03' -> 3723, '' / '?' -> None."""
    text = (text or "").strip()
    if not re.fullmatch(r"\d{1,3}(:\d{1,2}){1,2}", text):
        return None
    seconds = 0
    for part in text.split(":"):
        seconds = seconds * 60 + int(part)
    return float(seconds) if seconds > 0 else None


_VINYL = re.compile(r"^([A-Z]{1,2})[\s.\-]?(\d*)[\s.\-]?([a-z]?)$")


def parse_position(position: str | None) -> str | None:
    """The vinyl side of a Discogs position: 'A1' -> 'A', 'B' -> 'B', 'AA2' -> 'AA',
    'A1a' / 'A-1' / 'a1' -> 'A'. CD and digital positions ('3', '2-03', 'CD1-4') and
    anything unrecognized -> None."""
    pos = (position or "").strip()
    if not pos or re.match(r"^(cd|dvd|disc)", pos, re.IGNORECASE):
        return None
    lower = re.match(r"^([a-z]{1,2})(\d.*)$", pos)  # some releases write sides lowercase
    if lower:
        pos = lower.group(1).upper() + lower.group(2)
    match = _VINYL.match(pos)
    if not match:
        return None
    letters = match.group(1)
    if len(letters) == 2 and letters[0] != letters[1]:
        return None  # "AB" etc. are not sides
    return letters


def tracks_from_release(release: dict[str, Any]) -> list[Track]:
    """Playable tracks in order: ``track`` entries, plus the sub-tracks of ``index``
    entries (medleys); ``heading`` entries are skipped."""
    tracks = []
    for item in release.get("tracklist") or []:
        kind = item.get("type_", "track")
        if kind == "heading":
            continue
        if kind == "index" and item.get("sub_tracks"):
            subs = item["sub_tracks"]
            if all(s.get("position") for s in subs):
                for sub in subs:
                    tracks.append(_track(sub))
                continue
        tracks.append(_track(item))
    return tracks


def _track(item: dict[str, Any]) -> Track:
    position = (item.get("position") or "").strip()
    return Track(
        position=position,
        title=(item.get("title") or "").strip(),
        side=parse_position(position),
        seconds=parse_duration(item.get("duration")),
    )


def sides(tracks: list[Track]) -> dict[str, list[Track]]:
    """Group vinyl tracks by side, in tracklist order. Non-vinyl tracks are left out."""
    grouped: dict[str, list[Track]] = {}
    for track in tracks:
        if track.side is not None:
            grouped.setdefault(track.side, []).append(track)
    return grouped


@dataclass(frozen=True)
class SideLength:
    seconds: float | None
    known: int
    total: int
    estimated: bool
    """True when some durations were missing and filled with the median known length."""


def side_length(tracks: list[Track], fallback_track_s: float | None = None) -> SideLength:
    """Total side length. Missing durations are filled with the median of the known ones
    on this release (passed in as ``fallback_track_s``) or on this side; with no durations
    at all the length is None."""
    known = [t.seconds for t in tracks if t.seconds is not None]
    missing = len(tracks) - len(known)
    if missing == 0:
        return SideLength(sum(known), len(known), len(tracks), False)
    filler = fallback_track_s or (statistics.median(known) if known else None)
    if filler is None:
        return SideLength(None, 0, len(tracks), False)
    return SideLength(sum(known) + missing * filler, len(known), len(tracks), True)


# Choosing a release


def _norm(text: str | None) -> str:
    text = (text or "").casefold()
    text = re.sub(r"\(\d+\)", " ", text)  # Discogs disambiguates artists as "Name (2)"
    text = re.sub(r"[\(\[][^\)\]]*[\)\]]", " ", text)
    return " ".join(re.findall(r"[a-z0-9]+", text))


def _sim(a: str | None, b: str | None) -> float:
    """1.0 for the same normalized text; near-misses shrink sharply (cubed), so "II" vs
    "III" (0.8 as plain string similarity) counts as 0.51, not as nearly the same album."""
    a, b = _norm(a), _norm(b)
    if not a or not b:
        return 0.0
    return 1.0 if a == b else SequenceMatcher(None, a, b).ratio() ** 3


@dataclass
class Candidate:
    id: int
    title: str
    artist: str
    year: str | None
    country: str | None
    formats: list[str]
    label: str | None
    catno: str | None
    master_id: int | None = None
    """The master release grouping this pressing with the album's other pressings."""
    score: float = 0.0
    reasons: list[str] = field(default_factory=list)

    @property
    def is_vinyl(self) -> bool:
        return "Vinyl" in self.formats

    def describe(self) -> str:
        fmt = ", ".join(self.formats[:3]) or "?"
        bits = [self.year or "????", self.country or "?", fmt, self.label or "?", self.catno or "?"]
        return f"{self.artist} - {self.title}  [{' | '.join(bits)}]  (#{self.id})"


def candidate_from_search(result: dict[str, Any]) -> Candidate:
    """Search results title releases as "Artist - Title"."""
    artist, _, title = (result.get("title") or "").partition(" - ")
    if not title:
        artist, title = "", artist
    labels = result.get("label") or []
    return Candidate(
        id=int(result["id"]),
        title=title.strip(),
        artist=artist.strip(),
        year=str(result["year"]) if result.get("year") else None,
        country=result.get("country"),
        formats=list(result.get("format") or []),
        label=labels[0] if labels else None,
        catno=result.get("catno") or None,
        master_id=int(result["master_id"]) if result.get("master_id") else None,
    )


def same_label(a: str | None, b: str | None) -> bool:
    """Labels match if one name contains the other ("Electronic Purification" vs
    "Electronic Purification Records") or they are nearly identical."""
    a, b = _norm(a), _norm(b)
    return bool(a and b) and (a in b or b in a or _sim(a, b) >= 0.8)


def tied(ranked: list[Candidate], margin: float = 0.75) -> list[Candidate]:
    """The candidates within ``margin`` of the top score (the ones a person would weigh)."""
    return [c for c in ranked if ranked and ranked[0].score - c.score < margin]


def common_master(ranked: list[Candidate]) -> int | None:
    """The master id if every tied candidate is a pressing of the same master, else None."""
    masters = {c.master_id for c in tied(ranked)}
    return masters.pop() if len(masters) == 1 and None not in masters else None


def pick_pressing(
    ranked: list[Candidate], master: dict[str, Any], prefer_vinyl: bool = True
) -> Candidate:
    """Among tied pressings of one master: the master's main release if it is one of them
    (and vinyl, when vinyl is preferred), else the earliest tied pressing (vinyl first)."""
    group = tied(ranked)
    main = next((c for c in group if c.id == master.get("main_release")), None)
    if main is not None and (main.is_vinyl or not prefer_vinyl):
        return main
    pool = (
        [c for c in group if c.is_vinyl]
        if prefer_vinyl and any(c.is_vinyl for c in group)
        else group
    )
    return min(pool, key=lambda c: (c.year or "9999", c.id))


def score_candidates(
    candidates: list[Candidate],
    artist: str,
    album: str | None,
    prefer_vinyl: bool = True,
    label: str | None = None,
    year: int | None = None,
) -> list[Candidate]:
    """Score and sort: album title match (0..3), artist match (0..2), vinyl (up to +1 when
    preferred, scaled by the title match so it cannot rescue the wrong album), the label
    and release year named by the recognition service (+0.5 each), a known year (+0.2)."""
    for c in candidates:
        c.reasons = []
        title_sim = _sim(c.title, album) if album else 0.5
        artist_sim = _sim(c.artist, artist)
        c.score = 3 * title_sim + 2 * artist_sim
        c.reasons.append(f"title {title_sim:.2f}, artist {artist_sim:.2f}")
        if prefer_vinyl and c.is_vinyl:
            c.score += title_sim
            c.reasons.append("vinyl")
        if label and same_label(c.label, label):
            c.score += 0.5
            c.reasons.append("label")
        if year and near_year(c.year, year):
            c.score += 0.5
            c.reasons.append("year")
        if c.year:
            c.score += 0.2
    return sorted(candidates, key=lambda c: c.score, reverse=True)


def near_year(candidate_year: str | int | None, year: int | None) -> bool:
    """True when both years are known and within one year of each other."""
    if not candidate_year or not year:
        return False
    try:
        return abs(int(str(candidate_year)[:4]) - int(year)) <= 1
    except ValueError:
        return False


def without_brackets(name: str | None) -> str | None:
    """'Altered State (Deluxe Edition)' -> 'Altered State': editions, remasters, and
    other bracketed qualifiers that a store adds but Discogs often does not."""
    if not name:
        return None
    stripped = re.sub(r"\s*[\(\[][^\)\]]*[\)\]]", "", name).strip()
    return stripped or None


def corroborated(candidate: Candidate, label: str | None, year: int | None) -> bool:
    """True when the candidate's label or year agrees with the recognition service."""
    return (bool(label) and same_label(candidate.label, label)) or near_year(candidate.year, year)


def clear_winner(ranked: list[Candidate], margin: float = 0.75) -> bool:
    """True when the top candidate can be chosen without asking."""
    if not ranked:
        return False
    if len(ranked) == 1:
        return ranked[0].score >= 3.5
    return ranked[0].score >= 4.0 and ranked[0].score - ranked[1].score >= margin


class Selections:
    """The player's confirmed choices: (artist, album) -> release id, in a local JSON file.
    Once a record is confirmed, the lookup flow never runs for it again."""

    def __init__(self, path: str | Path) -> None:
        self.path = Path(path)
        self.data: dict[str, dict[str, Any]] = (
            json.loads(self.path.read_text(encoding="utf-8")) if self.path.exists() else {}
        )

    @staticmethod
    def key(artist: str, album: str | None) -> str:
        return f"{_norm(artist)}|{_norm(album)}"

    def get(self, artist: str, album: str | None) -> int | None:
        entry = self.data.get(self.key(artist, album))
        return int(entry["release_id"]) if entry else None

    def forget(self, artist: str, album: str | None) -> bool:
        """Drop a remembered choice (e.g. a wrong automatic pick); True if there was one."""
        if self.data.pop(self.key(artist, album), None) is None:
            return False
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text(json.dumps(self.data, indent=2) + "\n", encoding="utf-8")
        return True

    def put(self, artist: str, album: str | None, release_id: int, how: str) -> None:
        self.data[self.key(artist, album)] = {
            "release_id": int(release_id),
            "how": how,
            "when": time.strftime("%Y-%m-%dT%H:%M:%S"),
        }
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text(json.dumps(self.data, indent=2) + "\n", encoding="utf-8")


_PART = re.compile(
    r"[\s,:;\-–]*[\(\[]?\b(?:pt|pts|part|parts)\b\.?\s*([ivxlc]+|\d+)\b"
    r"(?:\s*(?:-|–|&|,|to)\s*(?:[ivxlc]+|\d+)\b)*[\)\]]?\s*$",
    re.IGNORECASE,
)
_ROMAN = {"i": 1, "v": 5, "x": 10, "l": 50, "c": 100}


def split_part(title: str | None) -> tuple[str, int | None]:
    """'Simulacra, Pt. I' -> ('Simulacra', 1); 'Song (Part 2)' -> ('Song', 2);
    'Song (Parts I-III)' -> ('Song', 1); titles without a part -> (title, None)."""
    text = (title or "").strip()
    match = _PART.search(text)
    if not match or match.start() == 0:
        return text, None
    token = match.group(1).lower()
    if token.isdigit():
        number = int(token)
    else:
        values = [_ROMAN[ch] for ch in token]
        number = sum(-v if i + 1 < len(values) and v < values[i + 1] else v
                     for i, v in enumerate(values))  # fmt: skip
    return text[: match.start()].strip(" ,:;-–"), number


def match_track(tracks: list[Track], title: str | None) -> tuple[Track | None, int | None]:
    """The track best matching ``title``, and the part number when it only matched as a
    part: a recognition service may know "Simulacra, Pt. I" as its own track while the
    vinyl has one long "Simulacra". Returns (None, None) without a good match.

    Order matters: an exact (or near-exact) title first; then parts, so "Simulacra, Pt. II"
    keeps its part number; then containment, so "Of Mind - Nocturne" finds "Nocturne".
    """

    def best_by(score: Callable[[Track], float]) -> Track | None:
        best, best_sim = None, 0.0
        for track in tracks:
            sim = score(track)
            if sim > best_sim:
                best, best_sim = track, sim
        return best if best_sim >= 0.8 else None

    exact = best_by(lambda tr: _sim(tr.title, title))
    if exact is not None:
        return exact, None
    base, part = split_part(title)
    if part is not None:
        whole = best_by(lambda tr: _sim(split_part(tr.title)[0], base))
        if whole is not None:
            return whole, part
    contained = best_by(lambda tr: 0.85 if _contains(tr.title, title) else 0.0)
    return contained, None


def _contains(a: str | None, b: str | None) -> bool:
    """One normalized title contains the other as whole words, and the shorter is long
    enough (5+ characters) not to match by accident."""
    a, b = _norm(a), _norm(b)
    short, long = sorted((a, b), key=len)
    return len(short) >= 5 and f" {short} " in f" {long} "


def find_track(tracks: list[Track], title: str | None) -> Track | None:
    """The track best matching ``title`` (directly or as one of its parts), or None."""
    return match_track(tracks, title)[0]


def find_side(tracks: list[Track], title: str | None) -> str | None:
    """The vinyl side holding the track best matching ``title`` (None if no good match or
    the track has no vinyl side)."""
    track = find_track(tracks, title)
    return track.side if track is not None else None


def release_message(
    release: dict[str, Any], side: str, t: float, tracks: list[Track] | None = None
) -> ReleaseSelected:
    """The contract ``release_selected`` message for one side of a release."""
    tracks = tracks if tracks is not None else tracks_from_release(release)
    on_side = sides(tracks).get(side, [])
    return ReleaseSelected(
        t=t,
        discogs_id=int(release["id"]),
        side=side,
        tracks=[MessageTrack(tr.position, tr.title, tr.seconds) for tr in on_side],
    )


# The lookup flow


Confirm = Callable[[list[Candidate]], "Candidate | str | None"]


@dataclass
class Lookup:
    artist: str
    album: str | None
    track: str | None
    how: str = "not found"
    """remembered / auto / confirmed / unconfirmed / not found / error"""
    release: dict[str, Any] | None = None
    candidates: list[Candidate] = field(default_factory=list)
    tracks: list[Track] = field(default_factory=list)
    side_lengths: dict[str, SideLength] = field(default_factory=dict)
    track_side: str | None = None
    message: ReleaseSelected | None = None
    error: str | None = None
    via_master: bool = False
    """Chosen automatically because every tied candidate is a pressing of one master."""
    filled_from_master: int = 0
    """Track durations missing on the release and taken from its master's tracklist."""
    via_barcode: bool = False
    """Found directly by the barcode (UPC) the recognition service reported."""
    searched_album: str | None = None
    """The album name that found the candidates (the given one, or an alternate)."""
    genres: list[str] = field(default_factory=list)
    styles: list[str] = field(default_factory=list)
    """Discogs styles ("Synthwave", "Math Rock"): finer than genres, good for theming."""
    year: int | None = None
    """Original year: the master's when it was fetched, else the release's."""
    label: str | None = None
    catno: str | None = None
    barcodes: list[str] = field(default_factory=list)
    matrix: list[str] = field(default_factory=list)
    """Matrix / runout codes etched in the dead wax: they identify the exact pressing."""
    have: int | None = None
    want: int | None = None
    """Discogs community counts: how many own / want this release."""
    cover_url: str | None = None
    """The release's primary image (150 px thumbnail when available), else its master's."""
    cover: bytes | None = None
    """The downloaded cover image, filled in by ``ReleaseTracker`` for live displays."""


def search(
    discogs: Discogs,
    artist: str,
    album: str | None,
    track: str | None,
    alt_albums: list[str] | tuple[str, ...] = (),
) -> tuple[list[dict], str | None]:
    """Artist + album (as given, then without bracketed editions like "(Deluxe Edition)"),
    then each alternate album likewise, then artist + track, then free text: the first that
    finds anything. Returns (results, the album name that found
    them), so candidates are scored against the name that matched."""
    attempts: list[tuple[dict[str, str], str | None]] = []
    names: list[str] = []
    for name in [album, *alt_albums]:
        for variant in (name, without_brackets(name)):
            if variant and variant not in names:
                names.append(variant)
    for name in names:
        attempts.append(({"artist": artist, "release_title": name}, name))
    if track:
        # Found by track title: no album name to score against (the given one just failed).
        attempts.append(({"artist": artist, "track": track}, None))
    attempts.append(({"q": f"{artist} {album or track or ''}".strip()}, album))
    for params, name in attempts:
        results = discogs.search_releases(**params)
        if results:
            return results, name
    return [], album


def resolve(
    discogs: Discogs,
    selections: Selections,
    artist: str,
    album: str | None,
    track: str | None = None,
    confirm: Confirm | None = None,
    prefer_vinyl: bool = True,
    alt_albums: list[str] | tuple[str, ...] = (),
    label: str | None = None,
    barcode: str | None = None,
    year: int | None = None,
) -> Lookup:
    """Find the release for (artist, album, track); see the module docstring for the flow.

    Hints from the recognition service improve it: ``barcode`` (UPC) is searched first and
    identifies the release directly; ``alt_albums`` are tried when the album is not on
    Discogs; ``label`` and ``year`` favor candidates that agree. A release found only by
    track title (no album matched) is picked automatically only if its label or year
    agrees; otherwise it is "unconfirmed", so a lone wrong hit is never remembered.
    """
    result = Lookup(artist, album, track)
    try:
        release_id = selections.get(artist, album)
        by_barcode = [] if release_id or not barcode else discogs.search_releases(barcode=barcode)
        if release_id is not None:
            result.how = "remembered"
        elif by_barcode:
            release_id = candidate_from_search(by_barcode[0]).id
            result.how, result.via_barcode = "auto", True
            selections.put(artist, album, release_id, "barcode")
        else:
            found, searched = search(discogs, artist, album, track, alt_albums)
            result.searched_album = searched
            ranked = score_candidates(
                [candidate_from_search(r) for r in found],
                artist,
                searched,
                prefer_vinyl,
                label,
                year,
            )
            result.candidates = ranked
            if not ranked:
                return result
            master = None
            master_id = None if clear_winner(ranked) else common_master(ranked)
            if master_id is not None:
                try:
                    master = discogs.master(master_id)
                except DiscogsError:
                    master = None  # fall back to asking
            if clear_winner(ranked):
                chosen, result.how = ranked[0], "auto"
            elif master is not None:
                # Pressings of one album share sides and running order, so for side tracking
                # any of them will do: no need to ask.
                chosen = pick_pressing(ranked, master, prefer_vinyl)
                result.how, result.via_master = "auto", True
            elif confirm is not None:
                answer = confirm(ranked[:5])
                chosen = _from_answer(discogs, artist, answer, ranked)
                result.how = "confirmed" if chosen else "not found"
                if chosen is None:
                    return result
            else:
                chosen, result.how = ranked[0], "unconfirmed"
            if (
                result.how == "auto"
                and searched is None
                and (label or year)
                and not corroborated(chosen, label, year)
            ):
                # Found only by track title, and nothing else agrees: do not trust or keep it.
                result.how, result.via_master = "unconfirmed", False
            release_id = chosen.id
            if result.how in ("auto", "confirmed"):
                selections.put(artist, album, release_id, result.how)
        release = discogs.release(release_id)
    except DiscogsError as exc:
        result.how, result.error = "error", str(exc)
        return result

    return complete(result, release, discogs)


def complete(result: Lookup, release: dict[str, Any], discogs: Discogs | None = None) -> Lookup:
    """Fill in a lookup from a fetched release: tracks, sides and their lengths, the side
    holding the identified track, and the contract message for that side.

    With ``discogs``, durations missing on the release are filled from its master's
    tracklist (matched by title), which often has them when a given pressing does not.
    """
    result.release = release
    result.tracks = tracks_from_release(release)
    result.genres = list(release.get("genres") or [])
    result.styles = list(release.get("styles") or [])
    result.year = int(release["year"]) if release.get("year") else None
    labels = release.get("labels") or []
    if labels:
        result.label = labels[0].get("name")
        result.catno = labels[0].get("catno") or None
    for ident in release.get("identifiers") or []:
        kind, value = (ident.get("type") or "").lower(), (ident.get("value") or "").strip()
        if value and kind == "barcode":
            result.barcodes.append(value)
        elif value and kind.startswith("matrix"):
            result.matrix.append(value)
    community = release.get("community") or {}
    result.have, result.want = community.get("have"), community.get("want")
    result.cover_url = cover_url(release)
    master_id = release.get("master_id")
    if discogs is not None and master_id and any(t.seconds is None for t in result.tracks):
        try:
            master = discogs.master(master_id)
        except DiscogsError:
            master = {}
        master_tracks = tracks_from_release(master)
        if master.get("year"):
            result.year = int(master["year"])  # the original year, not this pressing's
        result.genres = result.genres or list(master.get("genres") or [])
        result.styles = result.styles or list(master.get("styles") or [])
        result.cover_url = result.cover_url or cover_url(master)
        filled = []
        for track in result.tracks:
            match = find_track(master_tracks, track.title) if track.seconds is None else None
            if match is not None and match.seconds is not None:
                filled.append(dataclasses.replace(track, seconds=match.seconds))
                result.filled_from_master += 1
            else:
                filled.append(track)
        result.tracks = filled
    grouped = sides(result.tracks)
    known = [t.seconds for t in result.tracks if t.seconds is not None]
    median = sorted(known)[len(known) // 2] if known else None
    result.side_lengths = {s: side_length(ts, median) for s, ts in grouped.items()}
    result.track_side = find_side(result.tracks, result.track) if result.track else None
    side = result.track_side or next(iter(grouped), None)
    if side is not None:
        result.message = dataclasses.replace(
            release_message(release, side, t=0.0, tracks=result.tracks),
            album=release.get("title"),
            year=result.year,
            genres=result.genres or None,
            styles=result.styles or None,
        )
    return result


def cover_url(item: dict[str, Any]) -> str | None:
    """The primary image of a release or master: the 150 px thumbnail if listed, else the
    full image; the first image when none is marked primary."""
    images = item.get("images") or []
    if not images:
        return None
    image = next((i for i in images if i.get("type") == "primary"), images[0])
    return image.get("uri150") or image.get("uri") or None


def _from_answer(
    discogs: Discogs, artist: str, answer: Candidate | str | None, ranked: list[Candidate]
) -> Candidate | None:
    """A chosen candidate, or a catalog number / barcode typed from the sleeve."""
    if answer is None or isinstance(answer, Candidate):
        return answer
    text = answer.strip()
    typed = _alnum(text)  # sleeves and Discogs punctuate catalog numbers differently
    for c in ranked:
        if c.catno and _alnum(c.catno) == typed:
            return c
    key = "barcode" if typed.isdigit() else "catno"
    found = discogs.search_releases(artist=artist, **{key: typed if key == "barcode" else text})
    return candidate_from_search(found[0]) if found else None


def _alnum(text: str) -> str:
    return "".join(ch for ch in text.casefold() if ch.isalnum())


# Live use: background lookups and playback progress


class ReleaseTracker:
    """Look up the release for each identification on a background thread, so a live
    display never waits on Discogs.

    ``request(artist, album, title)`` starts a lookup (ignored if it is the same record and
    track as the current one); ``poll()`` returns a finished ``Lookup`` once. When a
    lookup is "unconfirmed", ``confirm(i)`` picks candidate ``i`` (0-based), fetches it,
    and remembers the choice, so the question never comes up again for that record.
    """

    def __init__(self, discogs: Discogs, selections: Selections, prefer_vinyl: bool = True):
        self.discogs = discogs
        self.selections = selections
        self.prefer_vinyl = prefer_vinyl
        self.current: Lookup | None = None
        self.query: tuple[str, str | None, str | None] | None = None
        self._busy = False
        self._done: queue.Queue[Lookup] = queue.Queue()
        self._thread: threading.Thread | None = None

    @property
    def busy(self) -> bool:
        return self._busy

    def request(
        self,
        artist: str,
        album: str | None,
        title: str | None,
        alt_albums: list[str] | tuple[str, ...] = (),
        label: str | None = None,
        barcode: str | None = None,
        year: int | None = None,
    ) -> bool:
        """Start a lookup; False if it is the current query or one is already running.
        ``alt_albums``, ``label``, ``barcode``, and ``year`` are hints from the recognition
        service."""
        artist = primary_artist(artist)
        query = (artist, album, title)
        if self._busy or query == self.query:
            return False
        self.query = query
        hints = {"alt_albums": alt_albums, "label": label, "barcode": barcode, "year": year}
        self._last = (artist, album, title, hints)
        self._start(lambda: resolve(
            self.discogs, self.selections, artist, album, title,
            prefer_vinyl=self.prefer_vinyl, **hints,
        ))  # fmt: skip
        return True

    def forget(self) -> bool:
        """Forget the remembered choice for the current record and look it up again (to
        undo a wrong automatic pick). False when there is nothing to redo."""
        last = getattr(self, "_last", None)
        if self._busy or last is None:
            return False
        artist, album, title, hints = last
        self.selections.forget(artist, album)
        self.query = None
        return self.request(artist, album, title, **hints)

    def confirm(self, index: int) -> bool:
        """Choose candidate ``index`` of an unconfirmed lookup; False if not applicable."""
        lookup = self.current
        if self._busy or lookup is None or lookup.how != "unconfirmed":
            return False
        if not 0 <= index < min(5, len(lookup.candidates)):
            return False
        chosen = lookup.candidates[index]

        def work() -> Lookup:
            result = Lookup(lookup.artist, lookup.album, lookup.track, how="confirmed",
                            candidates=lookup.candidates)  # fmt: skip
            try:
                release = self.discogs.release(chosen.id)
            except DiscogsError as exc:
                result.how, result.error = "error", str(exc)
                return result
            self.selections.put(lookup.artist, lookup.album, chosen.id, "confirmed")
            return complete(result, release, self.discogs)

        self._start(work)
        return True

    def poll(self) -> Lookup | None:
        try:
            lookup = self._done.get_nowait()
        except queue.Empty:
            return None
        self.current = lookup
        self._busy = False
        return lookup

    def wait(self, timeout: float | None = None) -> None:
        if self._thread is not None:
            self._thread.join(timeout)

    def _start(self, work: Callable[[], Lookup]) -> None:
        self._busy = True

        def run() -> None:
            try:
                lookup = work()
                if lookup.cover_url and lookup.cover is None:
                    try:
                        lookup.cover = self.discogs.image(lookup.cover_url)
                    except DiscogsError:
                        lookup.cover = None  # no cover is fine
                self._done.put(lookup)
            except Exception as exc:  # never let the thread die silently
                artist, album, title = self.query or ("", None, None)
                self._done.put(Lookup(artist, album, title, how="error", error=repr(exc)))

        self._thread = threading.Thread(target=run, name="discogs", daemon=True)
        self._thread.start()


def primary_artist(artist: str) -> str:
    """The first credited artist ("Kalax, Frankmusik" -> "Kalax") for searching."""
    for sep in (", ", " feat. ", " ft. ", " & ", " x "):
        if sep in artist:
            return artist.split(sep, 1)[0].strip()
    return artist.strip()


@dataclass(frozen=True)
class Progress:
    """Where playback is, from the last identification and the release's tracklist.

    Times are seconds; any can be None when unknown. ``side_estimated`` is True when some
    durations on the side were filled in.
    """

    track: Track | None
    into_track: float | None
    track_length: float | None
    track_left: float | None
    side: str | None
    side_elapsed: float | None
    side_length: float | None
    side_left: float | None
    side_estimated: bool
    part: int | None = None
    """Set when the service identified a part ("Pt. II") of what the tracklist lists as one
    track. Part 1 starts with the track, so timing is exact; for later parts the position
    within the whole track is unknown (``into_track`` and the times left are None)."""


def progress(
    lookup: Lookup | None,
    title: str | None,
    offset_s: float | None,
    clip_end_t: float,
    audio_t: float,
    track_seconds: float | None = None,
) -> Progress:
    """Playback position now (``audio_t``), given that at audio time ``clip_end_t`` the
    identified track ``title`` was ``offset_s`` seconds in. ``track_seconds`` (the
    recognition service's track length) wins over the tracklist's, which is often missing.
    """
    into = None if offset_s is None else max(0.0, offset_s + (audio_t - clip_end_t))
    track, part = match_track(lookup.tracks, title) if lookup and lookup.tracks else (None, None)
    if part is not None:
        # The service knows only a part; the tracklist's whole-track length is the one that
        # counts, and the service's position is only from the track start for part 1.
        track_seconds = None
        if part != 1:
            into = None
    length = track_seconds or (track.seconds if track else None)
    left = None if length is None or into is None else max(0.0, length - into)
    side = track.side if track else None
    if lookup is None or track is None or side is None:
        return Progress(track, into, length, left, side, None, None, None, False, part)
    on_side = sides(lookup.tracks)[side]
    known = [t.seconds for t in lookup.tracks if t.seconds is not None]
    filler = statistics.median(known) if known else None
    before = on_side[: on_side.index(track)]
    missing_before = [t for t in before if t.seconds is None]
    estimated = bool(missing_before)
    if missing_before and filler is None:
        elapsed = None
    else:
        elapsed = sum(t.seconds if t.seconds is not None else filler for t in before)  # type: ignore[misc]
        elapsed = None if into is None else elapsed + into
    total = lookup.side_lengths.get(side) or side_length(on_side, filler)
    side_total = total.seconds
    if track_seconds and track.seconds is None and side_total is not None and filler:
        side_total += track_seconds - filler  # replace this track's estimate with the real length
    estimated = estimated or total.estimated
    side_left = None if side_total is None or elapsed is None else max(0.0, side_total - elapsed)
    return Progress(
        track, into, length, left, side, elapsed, side_total, side_left, estimated, part
    )


DISTRIBUTORS = {
    "distrokid", "cdbaby", "tunecore", "amuse", "routenote", "ditto", "dittomusic",
    "unitedmasters", "awal", "onerpm", "symphonic", "believe", "landr", "stem",
}  # fmt: skip
"""Digital distributors that recognition services often report in place of a label
(normalized: lowercase, no spaces)."""


@dataclass(frozen=True)
class Check:
    name: str
    ok: bool | None
    """True / False, or None when one side has no data to compare."""
    detail: str = ""


def agreement(lookup: Lookup | None, result: Any) -> list[Check]:
    """How well a recognition result and the chosen release corroborate each other.

    ``result`` is a ``flightrec.identification.Result``. Checks: the identified track is
    in the tracklist; the artist matches; the service's track length is within 5 s of the
    tracklist's; the labels match; the service's release year is within a year of the
    release's or the original. Independent sources agreeing is the confirmation.
    """
    if lookup is None or lookup.release is None or result is None:
        return []
    checks = []
    track, part = match_track(lookup.tracks, result.title)
    where = (track.position + (" (part)" if part else "")) if track else "not listed"
    checks.append(Check("track", track is not None, where))
    artists = [a.get("name", "") for a in lookup.release.get("artists") or []] or [lookup.artist]
    artist_ok = any(_sim(a, primary_artist(result.artist or "")) >= 0.8 for a in artists)
    checks.append(Check("artist", artist_ok))
    if part is not None:
        checks.append(Check("duration", None, "part"))  # a part is shorter by design
    elif track is not None and track.seconds and result.duration_s:
        diff = result.duration_s - track.seconds
        checks.append(Check("duration", abs(diff) <= 5.0, f"{diff:+.0f} s"))
    else:
        checks.append(Check("duration", None, "missing"))
    if result.label and _norm(result.label).replace(" ", "") in DISTRIBUTORS:
        # Digital releases are often credited to their distributor, not a label.
        checks.append(Check("label", None, "distributor"))
    elif result.label and lookup.label:
        checks.append(Check("label", same_label(result.label, lookup.label)))
    else:
        checks.append(Check("label", None, "missing"))
    year = None
    if result.release_date and result.release_date[:4].isdigit():
        year = int(result.release_date[:4])
    years = {y for y in (lookup.year, (lookup.release or {}).get("year")) if y}
    if year and years:
        checks.append(Check("year", any(abs(year - int(y)) <= 1 for y in years), str(year)))
    else:
        checks.append(Check("year", None, "missing"))
    return checks
