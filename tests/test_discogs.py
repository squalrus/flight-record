"""Discogs lookup offline: positions, durations, sides, scoring, caches, rate limits.

Fixtures follow the documented shape of Discogs API responses (search results and
releases); no network.
"""

import json
from pathlib import Path

import pytest

from flightrec.discogs import (
    USER_AGENT,
    Candidate,
    Discogs,
    DiscogsError,
    Selections,
    candidate_from_search,
    clear_winner,
    find_side,
    parse_duration,
    parse_position,
    release_message,
    score_candidates,
    side_length,
    sides,
    tracks_from_release,
)
from flightrec.messages import loads

RELEASE = {
    "id": 123,
    "title": "III",
    "tracklist": [
        {"position": "", "type_": "heading", "title": "Side One", "duration": ""},
        {"position": "A1", "type_": "track", "title": "Opening", "duration": "4:05"},
        {"position": "A2", "type_": "track", "title": "Calling", "duration": "3:55"},
        {"position": "A3", "type_": "track", "title": "Night Drive", "duration": ""},
        {
            "position": "", "type_": "index", "title": "Suite",
            "sub_tracks": [
                {"position": "B1a", "type_": "track", "title": "Suite I", "duration": "2:00"},
                {"position": "B1b", "type_": "track", "title": "Suite II", "duration": "2:30"},
            ],
        },
        {"position": "B2", "type_": "track", "title": "Closing", "duration": "6:15"},
    ],
}  # fmt: skip


# Parsing


@pytest.mark.parametrize(
    ("position", "side"),
    [
        ("A1", "A"), ("B12", "B"), ("A", "A"), ("AA1", "AA"), ("A1a", "A"), ("A-1", "A"),
        ("A.1", "A"), ("a1", "A"), ("C3", "C"), ("1", None), ("2-03", None), ("CD1-4", None),
        ("", None), ("Video", None), ("AB1", None),
    ],
)  # fmt: skip
def test_parse_position(position: str, side: str | None) -> None:
    assert parse_position(position) == side


@pytest.mark.parametrize(
    ("text", "seconds"),
    [("3:45", 225.0), ("12:05", 725.0), ("1:02:03", 3723.0), ("", None), ("?", None),
     ("0:00", None), (None, None), ("3.45", None)],
)  # fmt: skip
def test_parse_duration(text: str | None, seconds: float | None) -> None:
    assert parse_duration(text) == seconds


def test_tracklist_skips_headings_and_expands_index_tracks() -> None:
    tracks = tracks_from_release(RELEASE)
    assert [t.position for t in tracks] == ["A1", "A2", "A3", "B1a", "B1b", "B2"]
    grouped = sides(tracks)
    assert list(grouped) == ["A", "B"]
    assert [t.title for t in grouped["B"]] == ["Suite I", "Suite II", "Closing"]


def test_side_length_complete_and_estimated() -> None:
    grouped = sides(tracks_from_release(RELEASE))
    b = side_length(grouped["B"])
    assert (b.seconds, b.known, b.total, b.estimated) == (645.0, 3, 3, False)
    a = side_length(grouped["A"])  # A3 has no duration: filled with the median (240 s)
    assert a.estimated and a.known == 2 and a.total == 3
    assert a.seconds == pytest.approx(245 + 235 + 240)
    none = side_length([t.__class__(t.position, t.title, t.side, None) for t in grouped["A"]])
    assert none.seconds is None and not none.estimated


def test_find_side_and_release_message() -> None:
    tracks = tracks_from_release(RELEASE)
    assert find_side(tracks, "Calling (feat. Frankmusik)") == "A"
    assert find_side(tracks, "Something Else Entirely") is None
    msg = release_message(RELEASE, "A", t=12.5, tracks=tracks)
    assert msg.discogs_id == 123 and msg.side == "A"
    assert [(t.pos, t.seconds) for t in msg.tracks] == [("A1", 245.0), ("A2", 235.0), ("A3", None)]
    assert loads(msg.to_json()) == msg  # valid against the contract


# Choosing a release


def search_result(id_: int, title: str, fmt: list[str], year: str | None = "2019") -> dict:
    return {"id": id_, "title": title, "format": fmt, "year": year, "country": "US",
            "label": ["Label"], "catno": f"CAT{id_}"}  # fmt: skip


def test_candidate_from_search_splits_artist_and_title() -> None:
    c = candidate_from_search(search_result(1, "Kalax (2) - III", ["Vinyl", "LP"]))
    assert (c.artist, c.title, c.is_vinyl, c.catno) == ("Kalax (2)", "III", True, "CAT1")


def test_scoring_prefers_matching_vinyl_and_detects_a_clear_winner() -> None:
    results = [
        search_result(1, "Kalax - III", ["File", "MP3"]),
        search_result(2, "Kalax - III", ["Vinyl", "LP"]),
        search_result(3, "Kalax - II", ["Vinyl", "LP"]),
        search_result(4, "Someone - III", ["Vinyl"]),
    ]
    ranked = score_candidates([candidate_from_search(r) for r in results], "Kalax", "III")
    assert [c.id for c in ranked[:2]] == [2, 1]
    assert clear_winner(ranked)


def test_two_equal_pressings_need_confirmation() -> None:
    results = [
        search_result(5, "Kalax - III", ["Vinyl", "LP"]),
        search_result(6, "Kalax - III", ["Vinyl", "LP", "Repress"]),
    ]
    ranked = score_candidates([candidate_from_search(r) for r in results], "Kalax", "III")
    assert not clear_winner(ranked)
    assert not clear_winner([])


def test_selections_persist(tmp_path: Path) -> None:
    path = tmp_path / "selections.json"
    s = Selections(path)
    assert s.get("Kalax", "III") is None
    s.put("Kalax", "III", 2, how="confirmed")
    again = Selections(path)
    assert again.get("KALAX", "iii") == 2  # normalized keys
    assert json.loads(path.read_text())[Selections.key("Kalax", "III")]["how"] == "confirmed"


# Client


class FakeTransport:
    def __init__(self, responses: list[tuple[int, dict]]) -> None:
        self.responses = list(responses)
        self.calls: list[tuple[str, dict]] = []

    def __call__(self, url: str, headers: dict) -> tuple[int, dict, bytes]:
        self.calls.append((url, headers))
        status, body = self.responses.pop(0)
        return status, {}, json.dumps(body).encode()


def client(transport: FakeTransport, tmp_path: Path | None = None) -> Discogs:
    return Discogs("tok", tmp_path, transport=transport, min_interval_s=0, sleep=lambda s: None)


def test_client_headers_cache_and_no_token_in_url(tmp_path: Path) -> None:
    transport = FakeTransport([(200, {"results": [search_result(1, "Kalax - III", ["Vinyl"])]})])
    discogs = client(transport, tmp_path)
    first = discogs.search_releases(artist="Kalax", release_title="III")
    second = discogs.search_releases(artist="Kalax", release_title="III")
    assert first == second and len(transport.calls) == 1
    assert (discogs.requests, discogs.cache_hits) == (1, 1)
    url, headers = transport.calls[0]
    assert headers["Authorization"] == "Discogs token=tok"
    assert headers["User-Agent"] == USER_AGENT
    assert "tok" not in url and "type=release" in url and "artist=Kalax" in url
    for cached in tmp_path.iterdir():
        assert "tok" not in cached.read_text()


def test_client_backs_off_on_429_then_succeeds() -> None:
    slept: list[float] = []
    transport = FakeTransport([(429, {}), (429, {}), (200, RELEASE)])
    discogs = Discogs("tok", None, transport=transport, min_interval_s=0, sleep=slept.append)
    assert discogs.release(123)["id"] == 123
    assert discogs.requests == 3 and slept == [5.0, 10.0]


def test_client_errors() -> None:
    with pytest.raises(DiscogsError, match="not found"):
        client(FakeTransport([(404, {})])).release(9)
    with pytest.raises(DiscogsError, match="HTTP 401"):
        client(FakeTransport([(401, {})])).release(9)


def test_candidate_describe_is_readable() -> None:
    c = Candidate(7, "III", "Kalax", "2019", "US", ["Vinyl", "LP"], "Label", "CAT7")
    assert c.describe() == "Kalax - III  [2019 | US | Vinyl, LP | Label | CAT7]  (#7)"


# Live use: ReleaseTracker and progress


class FakeAPI:
    """Search results and releases from dicts; counts calls."""

    def __init__(self, results: list[dict], releases: dict[int, dict]) -> None:
        self.results = results
        self.releases = releases
        self.searches = 0

    def search_releases(self, **params) -> list[dict]:
        self.searches += 1
        return self.results if "release_title" in params else []

    def release(self, release_id: int) -> dict:
        return self.releases[release_id]


def two_pressings() -> FakeAPI:
    second = dict(RELEASE, id=124)
    return FakeAPI(
        [
            search_result(123, "Kalax - III", ["Vinyl", "LP"]),
            search_result(124, "Kalax - III", ["Vinyl", "LP"]),
        ],
        {123: RELEASE, 124: second},
    )


def finish(tracker):
    tracker.wait(5)
    return tracker.poll()


def test_tracker_looks_up_in_the_background(tmp_path: Path) -> None:
    from flightrec.discogs import ReleaseTracker

    api = FakeAPI([search_result(123, "Kalax - III", ["Vinyl"])], {123: RELEASE})
    tracker = ReleaseTracker(api, Selections(tmp_path / "s.json"))
    assert tracker.request("Kalax, Frankmusik", "III", "Calling")  # first artist is searched
    assert tracker.busy
    lookup = finish(tracker)
    assert lookup.how == "auto" and lookup.artist == "Kalax" and lookup.track_side == "A"
    assert not tracker.request("Kalax", "III", "Calling")  # same record and track: no-op
    assert tracker.poll() is None


def test_tracker_confirm_picks_and_remembers(tmp_path: Path) -> None:
    from flightrec.discogs import ReleaseTracker

    selections = Selections(tmp_path / "s.json")
    tracker = ReleaseTracker(two_pressings(), selections)
    tracker.request("Kalax", "III", "Calling")
    assert finish(tracker).how == "unconfirmed"
    assert not tracker.confirm(7)  # out of range
    assert tracker.confirm(1)
    chosen = finish(tracker)
    assert chosen.how == "confirmed" and chosen.release["id"] == 124
    assert selections.get("Kalax", "III") == 124
    assert not tracker.confirm(0)  # already confirmed

    again = ReleaseTracker(two_pressings(), selections)
    again.request("Kalax", "III", "Calling")
    assert finish(again).how == "remembered"


def test_primary_artist() -> None:
    from flightrec.discogs import primary_artist

    assert primary_artist("Kalax, Frankmusik") == "Kalax"
    assert primary_artist("Glowbug feat. Josh Holland") == "Glowbug"
    assert primary_artist("standards") == "standards"


def lookup_for(release: dict, track: str):
    from flightrec.discogs import Lookup, complete

    return complete(Lookup("Kalax", "III", track), release)


def test_progress_track_and_side() -> None:
    from flightrec.discogs import progress

    lookup = lookup_for(RELEASE, "Calling")
    # At audio t=100 the clip ended 60 s into "Calling" (A2, 3:55); now it is t=110.
    p = progress(lookup, "Calling", offset_s=60.0, clip_end_t=100.0, audio_t=110.0)
    assert p.track.position == "A2" and p.into_track == 70.0
    assert p.track_length == 235.0 and p.track_left == 165.0
    assert p.side == "A" and p.side_elapsed == 245.0 + 70.0  # A1 (4:05) + 70 s
    # A3 has no duration: filled with the release's median track length (235 s).
    assert p.side_length == 245.0 + 235.0 + 235.0 and p.side_estimated
    assert p.side_left == 715.0 - 315.0


def test_progress_uses_service_track_length_when_discogs_has_none() -> None:
    from flightrec.discogs import progress

    lookup = lookup_for(RELEASE, "Night Drive")  # A3, no duration on Discogs
    p = progress(
        lookup, "Night Drive", offset_s=30.0, clip_end_t=0.0, audio_t=0.0, track_seconds=300.0
    )
    assert p.track_length == 300.0 and p.track_left == 270.0
    assert p.side_elapsed == 245.0 + 235.0 + 30.0
    assert p.side_length == 245.0 + 235.0 + 300.0  # the real length replaces the estimate


def test_progress_without_a_lookup_or_offset() -> None:
    from flightrec.discogs import progress

    p = progress(None, "Calling", offset_s=50.0, clip_end_t=10.0, audio_t=12.0, track_seconds=200.0)
    assert p.track is None and p.track_left == 148.0 and p.side is None
    q = progress(lookup_for(RELEASE, "Calling"), "Calling", offset_s=None, clip_end_t=0, audio_t=5)
    assert q.into_track is None and q.track_left is None and q.side_elapsed is None


# Recognition hints, release details, agreement


def test_search_tries_alternate_albums_and_scores_against_them(tmp_path: Path) -> None:
    from flightrec.discogs import resolve

    class API(FakeAPI):
        def search_releases(self, **params) -> list[dict]:
            self.searches += 1
            if params.get("release_title") == "Special Berry":
                return [search_result(55, "Standards - Special Berry", ["Vinyl", '7"'])]
            return []

    api = API([], {55: dict(RELEASE, id=55, title="Special Berry")})
    r = resolve(
        api,
        Selections(tmp_path / "s.json"),
        "standards",
        "Starfish",
        "Calling",
        alt_albums=["Special Berry"],
    )
    assert r.searched_album == "Special Berry" and r.how == "auto" and r.release["id"] == 55


def test_barcode_finds_the_release_directly(tmp_path: Path) -> None:
    from flightrec.discogs import resolve

    class API(FakeAPI):
        def search_releases(self, **params) -> list[dict]:
            self.searches += 1
            return [search_result(124, "Kalax - III", ["Vinyl"])] if params.get("barcode") else []

    selections = Selections(tmp_path / "s.json")
    r = resolve(
        API([], {124: dict(RELEASE, id=124)}),
        selections,
        "Kalax",
        "III",
        "Calling",
        barcode="0123456789012",
    )
    assert r.via_barcode and r.release["id"] == 124 and selections.get("Kalax", "III") == 124


def test_label_breaks_a_tie() -> None:
    a = candidate_from_search(dict(search_result(1, "Kalax - III", ["Vinyl"]), label=["Other"]))
    b = candidate_from_search(
        dict(search_result(2, "Kalax - III", ["Vinyl"]), label=["Electronic Purification Records"])
    )
    ranked = score_candidates([a, b], "Kalax", "III", label="Electronic Purification")
    assert ranked[0].id == 2 and "label" in ranked[0].reasons


def test_release_details_reach_the_lookup_and_message() -> None:
    from flightrec.discogs import Lookup, complete

    release = dict(
        RELEASE,
        genres=["Electronic"],
        styles=["Synthwave", "Synth-pop"],
        year=2021,
        labels=[{"name": "Electronic Purification Records", "catno": "WAVE 054"}],
        identifiers=[
            {"type": "Barcode", "value": "9 120061 000000"},
            {"type": "Matrix / Runout", "value": "WAVE054-A1 ETCHED"},
        ],
        community={"have": 104, "want": 76},
    )
    r = complete(Lookup("Kalax", "III", "Calling"), release)
    assert (r.genres, r.styles, r.year) == (["Electronic"], ["Synthwave", "Synth-pop"], 2021)
    assert (r.label, r.catno, r.have, r.want) == (
        "Electronic Purification Records",
        "WAVE 054",
        104,
        76,
    )
    assert r.barcodes == ["9 120061 000000"] and r.matrix == ["WAVE054-A1 ETCHED"]
    assert r.message.styles == ["Synthwave", "Synth-pop"] and r.message.year == 2021
    assert loads(r.message.to_json()) == r.message


def test_agreement_checks() -> None:
    from flightrec.discogs import Lookup, agreement, complete
    from flightrec.identification import Result

    release = dict(
        RELEASE,
        year=2019,
        artists=[{"name": "Kalax (2)"}],
        labels=[{"name": "Electronic Purification Records"}],
    )
    lookup = complete(Lookup("Kalax", "III", "Calling"), release)
    good = Result(
        "acrcloud",
        True,
        artist="Kalax, Frankmusik",
        title="Calling",
        duration_s=236.0,
        label="Electronic Purification",
        release_date="2019-03-25",
    )
    checks = {c.name: c for c in agreement(lookup, good)}
    assert all(c.ok for c in checks.values()), checks
    assert checks["track"].detail == "A2" and checks["duration"].detail == "+1 s"

    bad = Result(
        "acrcloud",
        True,
        artist="Someone",
        title="Calling",
        duration_s=300.0,
        label="Some Other Records",
        release_date="2010-01-01",
    )
    checks = {c.name: c.ok for c in agreement(lookup, bad)}
    assert checks == {
        "track": True,
        "artist": False,
        "duration": False,
        "label": False,
        "year": False,
    }

    unknown = Result("audd", True, artist="Kalax", title="Closing")
    checks = {c.name: c.ok for c in agreement(lookup, unknown)}
    assert checks["duration"] is None and checks["label"] is None and checks["year"] is None
    assert agreement(None, good) == []


# Cover art


def test_cover_url_prefers_the_primary_thumbnail() -> None:
    from flightrec.discogs import cover_url

    images = [
        {"type": "secondary", "uri": "https://img/back.jpg", "uri150": "https://img/back150.jpg"},
        {"type": "primary", "uri": "https://img/front.jpg", "uri150": "https://img/front150.jpg"},
    ]
    assert cover_url({"images": images}) == "https://img/front150.jpg"
    assert cover_url({"images": [{"uri": "https://img/only.jpg"}]}) == "https://img/only.jpg"
    assert cover_url({}) is None


def test_image_download_is_cached(tmp_path: Path) -> None:
    calls = []

    def transport(url: str, headers: dict) -> tuple[int, dict, bytes]:
        calls.append(url)
        return 200, {}, b"\xff\xd8jpeg-bytes"

    discogs = Discogs("tok", tmp_path / "http", transport=transport, min_interval_s=0)
    assert discogs.image("https://img/front150.jpg") == b"\xff\xd8jpeg-bytes"
    assert discogs.image("https://img/front150.jpg") == b"\xff\xd8jpeg-bytes"
    assert len(calls) == 1 and (tmp_path / "images").is_dir()


def test_tracker_fetches_the_cover_in_the_background(tmp_path: Path) -> None:
    from flightrec.discogs import ReleaseTracker

    class API(FakeAPI):
        def image(self, url: str) -> bytes:
            if "broken" in url:
                raise DiscogsError("HTTP 404 for an image")
            return b"cover:" + url.encode()

    with_cover = dict(RELEASE, images=[{"type": "primary", "uri150": "https://img/150.jpg"}])
    api = API([search_result(123, "Kalax - III", ["Vinyl"])], {123: with_cover})
    tracker = ReleaseTracker(api, Selections(tmp_path / "s.json"))
    tracker.request("Kalax", "III", "Calling")
    lookup = finish(tracker)
    assert (
        lookup.cover_url == "https://img/150.jpg" and lookup.cover == b"cover:https://img/150.jpg"
    )

    broken = dict(RELEASE, images=[{"type": "primary", "uri150": "https://img/broken.jpg"}])
    api2 = API([search_result(123, "Kalax - III", ["Vinyl"])], {123: broken})
    tracker2 = ReleaseTracker(api2, Selections(tmp_path / "s2.json"))
    tracker2.request("Kalax", "III", "Calling")
    lookup2 = finish(tracker2)
    assert lookup2.how == "auto" and lookup2.cover is None  # no cover, lookup still fine


def test_a_distributor_is_not_a_label_conflict() -> None:
    from flightrec.discogs import Lookup, agreement, complete
    from flightrec.identification import Result

    release = dict(RELEASE, labels=[{"name": "Electronic Purification Records"}])
    lookup = complete(Lookup("Kalax", "III", "Calling"), release)
    for credited in ("DISTROKID", "CD Baby", "TuneCore"):
        result = Result("acrcloud", True, artist="Kalax", title="Calling", label=credited)
        label = next(c for c in agreement(lookup, result) if c.name == "label")
        assert label.ok is None and label.detail == "distributor"


# Parts: the service knows "Simulacra, Pt. I", the vinyl has one long "Simulacra"


@pytest.mark.parametrize(
    ("title", "expected"),
    [
        ("Simulacra, Pt. I", ("Simulacra", 1)),
        ("Simulacra Pt. II", ("Simulacra", 2)),
        ("Simulacra (Part 3)", ("Simulacra", 3)),
        ("Simulacra - Part IV", ("Simulacra", 4)),
        ("Simulacra (Parts I-III)", ("Simulacra", 1)),
        ("Simulacra, Pts. IX", ("Simulacra", 9)),
        ("Simulacra", ("Simulacra", None)),
        ("Part Time Lover", ("Part Time Lover", None)),
        ("Part 1", ("Part 1", None)),  # a title that is only a part marker stays as is
    ],
)
def test_split_part(title: str, expected: tuple) -> None:
    from flightrec.discogs import split_part

    assert split_part(title) == expected


SUITE = {
    "id": 900,
    "title": "Solaris",
    "year": 2026,
    "tracklist": [
        {"position": "A1", "title": "Simulacra", "duration": "18:00"},
        {"position": "B1", "title": "Other", "duration": "10:00"},
    ],
}


def test_part_matches_the_whole_track_and_times_part_one_exactly() -> None:
    from flightrec.discogs import Lookup, agreement, complete, match_track, progress
    from flightrec.identification import Result

    lookup = complete(Lookup("The Ocean", "Solaris", "Simulacra, Pt. I"), SUITE)
    track, part = match_track(lookup.tracks, "Simulacra, Pt. I")
    assert track.position == "A1" and part == 1 and lookup.track_side == "A"

    # Part 1: 120 s into the part is 120 s into the track; the vinyl track is 18:00 long.
    p = progress(lookup, "Simulacra, Pt. I", 120.0, 0.0, 10.0, track_seconds=400.0)
    assert p.part == 1 and p.into_track == 130.0
    assert p.track_length == 1080.0 and p.track_left == 950.0  # not the part's 400 s
    part_one = Result(
        "acrcloud", True, artist="The Ocean", title="Simulacra, Pt. I", duration_s=400.0
    )
    checks = {c.name: c for c in agreement(lookup, part_one)}
    assert checks["track"].ok and checks["track"].detail == "A1 (part)"
    assert checks["duration"].ok is None and checks["duration"].detail == "part"


def test_later_parts_find_the_track_but_not_the_position() -> None:
    from flightrec.discogs import Lookup, complete, progress

    lookup = complete(Lookup("The Ocean", "Solaris", "Simulacra, Pt. II"), SUITE)
    p = progress(lookup, "Simulacra, Pt. II", 60.0, 0.0, 5.0, track_seconds=300.0)
    assert p.track.position == "A1" and p.part == 2 and p.side == "A"
    assert p.into_track is None and p.track_left is None and p.side_left is None
    assert p.track_length == 1080.0


# The TesseracT case: "(Deluxe Edition)" album, prefixed title, a lone wrong track-title hit


class SearchByParams(FakeAPI):
    """Search results keyed by the params used; releases by id."""

    def __init__(self, by_params: dict[tuple, list[dict]], releases: dict[int, dict]) -> None:
        super().__init__([], releases)
        self.by_params = by_params
        self.calls: list[dict] = []

    def search_releases(self, **params) -> list[dict]:
        self.calls.append(params)
        return self.by_params.get(tuple(sorted(params.items())), [])


ALTERED = {
    "id": 4000,
    "title": "Altered State",
    "year": 2013,
    "labels": [{"name": "Century Media"}],
    "tracklist": [
        {"position": "A1", "title": "Of Matter - Proxy", "duration": "6:40"},
        {"position": "B1", "title": "Nocturne", "duration": "4:40"},
    ],
}


def test_album_search_retries_without_edition_brackets(tmp_path: Path) -> None:
    from flightrec.discogs import resolve

    api = SearchByParams(
        {
            (("artist", "TesseracT"), ("release_title", "Altered State")): [
                dict(
                    search_result(4000, "TesseracT - Altered State", ["Vinyl", "LP"], "2013"),
                    label=["Century Media"],
                ),
            ]
        },
        {4000: ALTERED},
    )
    r = resolve(
        api,
        Selections(tmp_path / "s.json"),
        "TesseracT",
        "Altered State (Deluxe Edition)",
        "Of Mind - Nocturne",
        label="Century Media",
        year=2013,
    )
    assert [c.get("release_title") for c in api.calls[:2]] == [
        "Altered State (Deluxe Edition)",
        "Altered State",
    ]
    assert r.how == "auto" and r.release["id"] == 4000 and r.searched_album == "Altered State"
    assert r.track_side == "B"  # "Of Mind - Nocturne" found "Nocturne" by containment


def test_a_lone_track_title_hit_that_nothing_corroborates_is_not_trusted(tmp_path: Path) -> None:
    from flightrec.discogs import resolve

    wrong = dict(
        search_result(27710979, "TesseracT - Nocturne", ["File"], "2021"), label=["Kscope"]
    )
    api = SearchByParams(
        {(("artist", "TesseracT"), ("track", "Of Mind - Nocturne")): [wrong]},
        {
            27710979: dict(
                ALTERED, id=27710979, title="Nocturne", year=2021, labels=[{"name": "Kscope"}]
            )
        },
    )
    selections = Selections(tmp_path / "s.json")
    r = resolve(
        api,
        selections,
        "TesseracT",
        "Altered State (Deluxe Edition)",
        "Of Mind - Nocturne",
        label="Century Media",
        year=2013,
    )
    assert (
        r.how == "unconfirmed"
        and selections.get("TesseracT", "Altered State (Deluxe Edition)") is None
    )

    # The same hit with an agreeing label is trusted.
    agreeing = dict(wrong, label=["Century Media"])
    api2 = SearchByParams(
        {(("artist", "TesseracT"), ("track", "Of Mind - Nocturne")): [agreeing]},
        {27710979: ALTERED},
    )
    r2 = resolve(
        api2,
        Selections(tmp_path / "s2.json"),
        "TesseracT",
        "Altered State (Deluxe Edition)",
        "Of Mind - Nocturne",
        label="Century Media",
        year=2013,
    )
    assert r2.how == "auto"


def test_year_hint_breaks_a_tie() -> None:
    old = candidate_from_search(search_result(1, "TesseracT - Altered State", ["Vinyl"], "2013"))
    new = candidate_from_search(search_result(2, "TesseracT - Altered State", ["Vinyl"], "2021"))
    ranked = score_candidates([new, old], "TesseracT", "Altered State", year=2013)
    assert ranked[0].id == 1 and "year" in ranked[0].reasons


def test_containment_needs_a_long_enough_title() -> None:
    from flightrec.discogs import Track, match_track

    tracks = [Track("A1", "Nocturne", "A", 280.0), Track("A2", "Exile", "A", 300.0)]
    assert match_track(tracks, "Of Mind - Nocturne") == (tracks[0], None)
    assert match_track([Track("A1", "Go", "A", 100.0)], "Let It Go Now")[0] is None  # too short


def test_tracker_forget_redoes_a_wrong_pick(tmp_path: Path) -> None:
    from flightrec.discogs import ReleaseTracker

    selections = Selections(tmp_path / "s.json")
    selections.put("TesseracT", "Altered State", 27710979, "auto")  # a wrong remembered pick
    api = SearchByParams(
        {
            (("artist", "TesseracT"), ("release_title", "Altered State")): [
                dict(
                    search_result(4000, "TesseracT - Altered State", ["Vinyl"], "2013"),
                    label=["Century Media"],
                ),
            ]
        },
        {27710979: dict(ALTERED, id=27710979, title="Nocturne"), 4000: ALTERED},
    )
    tracker = ReleaseTracker(api, selections)
    tracker.request("TesseracT", "Altered State", "Nocturne", label="Century Media", year=2013)
    assert finish(tracker).release["id"] == 27710979  # remembered (wrongly)
    assert tracker.forget()
    redone = finish(tracker)
    assert redone.how == "auto" and redone.release["id"] == 4000
    assert selections.get("TesseracT", "Altered State") == 4000
