"""Experiment 04 lookup flow, offline: auto pick, confirmation (number, catalog number,
barcode), remembered choices, fallbacks, and errors. A fake Discogs stands in for the API."""

import importlib.util
import sys
from pathlib import Path

import pytest

from flightrec.discogs import Candidate, DiscogsError, Selections

spec = importlib.util.spec_from_file_location(
    "lookup", Path(__file__).resolve().parent / "lookup.py"
)
lookup = importlib.util.module_from_spec(spec)
sys.modules["lookup"] = lookup  # dataclasses look their module up by name
spec.loader.exec_module(lookup)


def result(id_: int, title: str, fmt: list[str], catno: str) -> dict:
    return {"id": id_, "title": title, "format": fmt, "year": "2019", "country": "US",
            "label": ["Label"], "catno": catno}  # fmt: skip


RELEASES = {
    10: {"id": 10, "title": "III", "year": 2019, "formats": [{"name": "Vinyl"}],
         "labels": [{"name": "Label", "catno": "LBL-10"}],
         "tracklist": [{"position": "A1", "title": "Opening", "duration": "4:00"},
                       {"position": "A2", "title": "Calling", "duration": "3:55"},
                       {"position": "B1", "title": "Closing", "duration": ""}]},
    11: {"id": 11, "title": "III", "year": 2020, "formats": [{"name": "Vinyl"}],
         "tracklist": [{"position": "A1", "title": "Calling", "duration": "3:55"}]},
    20: {"id": 20, "title": "Starfish", "year": 2021, "formats": [{"name": "File"}],
         "tracklist": [{"position": "1", "title": "Special Berry", "duration": "3:10"}]},
}  # fmt: skip


class FakeDiscogs:
    def __init__(self, searches: dict[tuple, list[dict]], fail: bool = False) -> None:
        self.searches = searches
        self.fail = fail
        self.search_calls: list[dict] = []
        self.release_calls: list[int] = []

    def search_releases(self, **params) -> list[dict]:
        if self.fail:
            raise DiscogsError("HTTP 503 for /database/search")
        self.search_calls.append(params)
        return self.searches.get(tuple(sorted(params.items())), [])

    def release(self, release_id: int) -> dict:
        self.release_calls.append(release_id)
        return RELEASES[release_id]


def key(**params) -> tuple:
    return tuple(sorted(params.items()))


@pytest.fixture
def selections(tmp_path: Path) -> Selections:
    return Selections(tmp_path / "selections.json")


def test_clear_winner_is_picked_and_remembered(selections: Selections) -> None:
    fake = FakeDiscogs({key(artist="Kalax", release_title="III"): [
        result(10, "Kalax - III", ["Vinyl", "LP"], "LBL-10"),
        result(30, "Kalax - II", ["Vinyl"], "LBL-30"),
    ]})  # fmt: skip
    first = lookup.resolve(fake, selections, "Kalax", "III", "Calling")
    assert first.how == "auto" and first.release["id"] == 10
    assert first.track_side == "A"
    assert first.message.side == "A" and [t.pos for t in first.message.tracks] == ["A1", "A2"]
    assert first.side_lengths["B"].estimated  # B1 has no duration

    again = lookup.resolve(fake, selections, "Kalax", "III", "Calling")
    assert again.how == "remembered" and len(fake.search_calls) == 1  # no second search


def test_ambiguous_pressings_ask_once(selections: Selections) -> None:
    fake = FakeDiscogs({key(artist="Kalax", release_title="III"): [
        result(10, "Kalax - III", ["Vinyl", "LP"], "LBL-10"),
        result(11, "Kalax - III", ["Vinyl", "LP"], "LBL-11"),
    ]})  # fmt: skip
    asked: list[list[Candidate]] = []

    def pick_second(cands: list[Candidate]) -> Candidate:
        asked.append(cands)
        return cands[1]

    chosen = lookup.resolve(fake, selections, "Kalax", "III", confirm=pick_second)
    assert chosen.how == "confirmed" and len(asked) == 1
    remembered = lookup.resolve(fake, selections, "Kalax", "III", confirm=pick_second)
    assert remembered.how == "remembered" and len(asked) == 1  # never asked again
    assert remembered.release["id"] == chosen.release["id"]


def test_confirm_with_catalog_number_or_barcode(selections: Selections) -> None:
    both = [
        result(10, "Kalax - III", ["Vinyl"], "LBL-10"),
        result(11, "Kalax - III", ["Vinyl"], "LBL-11"),
    ]
    fake = FakeDiscogs({
        key(artist="Kalax", release_title="III"): both,
        key(artist="Kalax", barcode="0123456789012"): [both[0]],
    })  # fmt: skip
    by_catno = lookup.resolve(fake, selections, "Kalax", "III", confirm=lambda c: "lbl 11")
    assert by_catno.release["id"] == 11
    by_barcode = lookup.resolve(
        fake, Selections(selections.path.with_name("other.json")), "Kalax", "III",
        confirm=lambda c: "0123456789012",
    )  # fmt: skip
    assert by_barcode.release["id"] == 10


def test_unconfirmed_without_a_prompt_is_not_remembered(selections: Selections) -> None:
    fake = FakeDiscogs({key(artist="Kalax", release_title="III"): [
        result(10, "Kalax - III", ["Vinyl"], "A"), result(11, "Kalax - III", ["Vinyl"], "B"),
    ]})  # fmt: skip
    r = lookup.resolve(fake, selections, "Kalax", "III")
    assert r.how == "unconfirmed" and selections.get("Kalax", "III") is None


def test_falls_back_to_track_search_and_handles_no_vinyl(selections: Selections) -> None:
    fake = FakeDiscogs({key(artist="standards", track="Special Berry"): [
        result(20, "standards - Starfish", ["File", "MP3"], "none"),
    ]})  # fmt: skip
    r = lookup.resolve(fake, selections, "standards", "Fruit Island", "Special Berry")
    assert [list(c) for c in fake.search_calls] == [
        ["artist", "release_title"],
        ["artist", "track"],
    ]
    assert r.release["id"] == 20 and r.side_lengths == {} and r.message is None


def test_not_found_and_errors(selections: Selections) -> None:
    assert lookup.resolve(FakeDiscogs({}), selections, "Nobody", "Nothing").how == "not found"
    err = lookup.resolve(FakeDiscogs({}, fail=True), selections, "Kalax", "III")
    assert err.how == "error" and "503" in err.error


# Master releases: ties between pressings of one album resolve automatically


def pressing(id_: int, year: str, fmt: list[str], master: int = 77) -> dict:
    return {"id": id_, "title": "Kalax - III", "format": fmt, "year": year, "country": "AT",
            "label": ["Label"], "catno": "WAVE 054", "master_id": master}  # fmt: skip


class MasterDiscogs(FakeDiscogs):
    def __init__(self, searches, masters: dict[int, dict], fail_master: bool = False) -> None:
        super().__init__(searches)
        self.masters = masters
        self.fail_master = fail_master
        self.master_calls: list[int] = []

    def master(self, master_id: int) -> dict:
        self.master_calls.append(master_id)
        if self.fail_master:
            raise DiscogsError("HTTP 503 for /masters")
        return self.masters[master_id]


MASTER = {"id": 77, "main_release": 11, "tracklist": [
    {"position": "A1", "title": "Opening", "duration": "4:00"},
    {"position": "A2", "title": "Calling", "duration": "3:55"},
    {"position": "B1", "title": "Closing", "duration": "5:30"},
]}  # fmt: skip


def test_tied_pressings_of_one_master_resolve_to_the_main_release(selections: Selections) -> None:
    fake = MasterDiscogs(
        {key(artist="Kalax", release_title="III"): [
            pressing(10, "2019", ["Vinyl", "LP"]), pressing(11, "2019", ["Vinyl", "LP"]),
            pressing(12, "2021", ["Vinyl", "LP", "Repress"]),
        ]},
        {77: MASTER},
    )  # fmt: skip
    r = lookup.resolve(fake, selections, "Kalax", "III", "Calling")
    assert r.how == "auto" and r.via_master and r.release["id"] == 11  # the main release
    assert selections.get("Kalax", "III") == 11  # remembered, never asked


def test_without_the_main_release_the_earliest_vinyl_pressing_wins(selections: Selections) -> None:
    fake = MasterDiscogs(
        {key(artist="Kalax", release_title="III"): [
            pressing(10, "2021", ["Vinyl", "LP", "Repress"]), pressing(11, "2019", ["Vinyl", "LP"]),
        ]},
        {77: dict(MASTER, main_release=999)},
    )  # fmt: skip
    assert lookup.resolve(fake, selections, "Kalax", "III").release["id"] == 11


def test_different_masters_still_need_confirmation(selections: Selections) -> None:
    fake = MasterDiscogs(
        {key(artist="Kalax", release_title="III"): [
            pressing(10, "2019", ["Vinyl", "LP"], master=77),
            pressing(11, "2019", ["Vinyl", "LP"], master=78),
        ]},
        {},
    )  # fmt: skip
    r = lookup.resolve(fake, selections, "Kalax", "III")
    assert r.how == "unconfirmed" and not r.via_master and fake.master_calls == []


def test_master_failure_falls_back_to_asking(selections: Selections) -> None:
    fake = MasterDiscogs(
        {key(artist="Kalax", release_title="III"): [
            pressing(10, "2019", ["Vinyl"]), pressing(11, "2019", ["Vinyl"]),
        ]},
        {},
        fail_master=True,
    )  # fmt: skip
    assert lookup.resolve(fake, selections, "Kalax", "III").how == "unconfirmed"


def test_missing_durations_are_filled_from_the_master(selections: Selections) -> None:
    RELEASES[10] = dict(RELEASES[10], master_id=77)  # B1 "Closing" has no duration
    fake = MasterDiscogs({key(artist="Kalax", release_title="III"): [
        result(10, "Kalax - III", ["Vinyl", "LP"], "LBL-10"),
        result(30, "Kalax - II", ["Vinyl"], "X"),
    ]}, {77: MASTER})  # fmt: skip
    try:
        r = lookup.resolve(fake, selections, "Kalax", "III", "Calling")
        closing = next(t for t in r.tracks if t.title == "Closing")
        assert closing.seconds == 330.0 and r.filled_from_master == 1
        assert not r.side_lengths["B"].estimated
    finally:
        RELEASES[10] = {k: v for k, v in RELEASES[10].items() if k != "master_id"}
