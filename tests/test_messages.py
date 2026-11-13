"""Message contract: every type round-trips, validation catches bad input, unknown fields
are kept, and logs replay at original timing."""

import json
import time
from pathlib import Path

import pytest

from flightrec.messages import (
    MESSAGE_TYPES,
    SCHEMA_VERSION,
    ContractError,
    FeaturesMessage,
    IdentificationFailed,
    LogWriter,
    ReleaseSelected,
    SideEnd,
    SideState,
    Track,
    TrackIdentified,
    UnknownMessageType,
    UnsupportedVersion,
    dumps,
    from_dict,
    loads,
    read_log,
    replay,
)

# The examples from docs/flightrec/architecture.md (release_selected gains the "t" every message
# carries; the doc's example omitted it).
DOC_EXAMPLES = [
    {"v": 1, "type": "features", "t": 123.456, "onset": True, "onset_strength": 0.82,
     "bass": 0.61, "mid": 0.34, "treble": 0.12, "rms": 0.40, "tempo": 118.0,
     "tempo_confidence": 0.7},
    {"v": 1, "type": "track_identified", "t": 132.0, "artist": "...", "title": "...",
     "album": "...", "confidence": 0.9},
    {"v": 1, "type": "release_selected", "t": 133.0, "discogs_id": 0, "side": "A",
     "tracks": [{"pos": "A1", "title": "...", "seconds": 215}]},
    {"v": 1, "type": "identification_failed", "t": 150.0, "reason": "no_match"},
    {"v": 1, "type": "side_state", "t": 140.0, "side": "A", "track_index": 1,
     "side_elapsed": 410.2, "side_expected": 1320.0, "side_confidence": 0.6},
    {"v": 1, "type": "side_end", "t": 1325.0, "cause": "run_out_groove"},
]  # fmt: skip


@pytest.mark.parametrize("example", DOC_EXAMPLES, ids=lambda e: e["type"])
def test_doc_examples_round_trip(example: dict) -> None:
    message = from_dict(example)
    assert type(message) is MESSAGE_TYPES[example["type"]]
    again = json.loads(dumps(message))
    for key, value in example.items():
        assert again[key] == value


def test_every_type_has_a_doc_example() -> None:
    assert {e["type"] for e in DOC_EXAMPLES} == set(MESSAGE_TYPES)


def test_constructed_messages_serialize_with_v_and_type() -> None:
    msg = SideEnd(t=5.0, cause="timer")
    data = json.loads(msg.to_json())
    assert data == {"v": SCHEMA_VERSION, "type": "side_end", "t": 5.0, "cause": "timer"}
    assert loads(msg.to_json()) == msg


def test_release_tracks_parse_to_track_objects() -> None:
    msg = from_dict(DOC_EXAMPLES[2])
    assert isinstance(msg, ReleaseSelected)
    assert msg.tracks == [Track("A1", "...", 215.0)]
    no_duration = dict(DOC_EXAMPLES[2], tracks=[{"pos": "B2", "title": "x", "seconds": None}])
    assert from_dict(no_duration).tracks[0].seconds is None  # Discogs often lacks durations


def test_optional_fields_default() -> None:
    msg = from_dict({"v": 1, "type": "track_identified", "t": 1.0, "artist": "a", "title": "b"})
    assert isinstance(msg, TrackIdentified)
    assert msg.album is None and msg.confidence == 0.0
    side = from_dict(
        {"v": 1, "type": "side_state", "t": 1.0, "side": "B", "track_index": 0, "side_elapsed": 0}
    )
    assert isinstance(side, SideState) and side.side_expected is None


# Unknown fields


def test_unknown_fields_are_ignored_and_kept() -> None:
    data = dict(DOC_EXAMPLES[3], retry_in=30, debug={"service": "x"})
    msg = from_dict(data)
    assert isinstance(msg, IdentificationFailed)
    assert msg.reason == "no_match"
    assert msg.extra == {"retry_in": 30, "debug": {"service": "x"}}
    assert json.loads(dumps(msg))["retry_in"] == 30  # survives a rewrite


def test_unknown_fields_do_not_affect_equality() -> None:
    plain = from_dict(DOC_EXAMPLES[5])
    noisy = from_dict(dict(DOC_EXAMPLES[5], something_new=1))
    assert plain == noisy


def test_features_optional_fields_from_experiment_02() -> None:
    data = dict(
        DOC_EXAMPLES[0],
        onset_low=True, onset_mid=False, onset_high=False, beat=True, centroid_hz=812.5,
        key="F# minor", key_confidence=0.81, chroma=[0.1] * 12,
    )  # fmt: skip
    msg = from_dict(data)
    assert isinstance(msg, FeaturesMessage)
    assert msg.key == "F# minor" and msg.chroma == [0.1] * 12
    assert msg.extra == {}


# Validation


@pytest.mark.parametrize(
    ("change", "error"),
    [
        ({"v": None}, "invalid 'v'"),
        ({"v": True}, "invalid 'v'"),
        ({"t": "soon"}, "must be a number"),
        ({"t": float("nan")}, "finite"),
        ({"onset": 1}, "true or false"),
        ({"bass": 1.5}, "outside"),
        ({"rms": -0.1}, "outside"),
        ({"tempo": -10.0}, "outside"),
        ({"chroma": [0.1] * 11}, "12 items"),
        ({"key": 5}, "must be a string"),
    ],
)
def test_features_validation(change: dict, error: str) -> None:
    with pytest.raises(ContractError, match=error):
        from_dict(dict(DOC_EXAMPLES[0], **change))


def test_missing_required_field() -> None:
    data = dict(DOC_EXAMPLES[1])
    del data["artist"]
    with pytest.raises(ContractError, match="missing required field 'artist'"):
        from_dict(data)


def test_side_state_types() -> None:
    with pytest.raises(ContractError, match="integer"):
        from_dict(dict(DOC_EXAMPLES[4], track_index=1.5))
    with pytest.raises(ContractError, match="outside"):
        from_dict(dict(DOC_EXAMPLES[4], track_index=-1))
    with pytest.raises(ContractError, match="side must be a string, got None"):
        from_dict(dict(DOC_EXAMPLES[4], side=None))  # required, not nullable
    with pytest.raises(ContractError, match="side_elapsed must be a number, got None"):
        from_dict(dict(DOC_EXAMPLES[4], side_elapsed=None))
    assert from_dict(dict(DOC_EXAMPLES[4], side_expected=None)).side_expected is None


def test_bad_tracks() -> None:
    with pytest.raises(ContractError, match="title must be a string"):
        from_dict(dict(DOC_EXAMPLES[2], tracks=[{"pos": "A1"}]))
    with pytest.raises(ContractError, match="seconds"):
        from_dict(dict(DOC_EXAMPLES[2], tracks=[{"pos": "A1", "title": "x", "seconds": -1}]))


def test_newer_version_and_unknown_type() -> None:
    with pytest.raises(UnsupportedVersion):
        from_dict(dict(DOC_EXAMPLES[5], v=SCHEMA_VERSION + 1))
    with pytest.raises(UnknownMessageType):
        from_dict({"v": 1, "type": "laser_show", "t": 0.0})
    with pytest.raises(ContractError, match="not valid JSON"):
        loads("{nope")


def test_serialization_rejects_nan() -> None:
    with pytest.raises(ValueError):
        dumps(SideState(t=1.0, side="A", track_index=0, side_elapsed=float("nan")))


# Logs and replay


def test_log_round_trip_skips_unknown_types(tmp_path: Path) -> None:
    path = tmp_path / "run.jsonl"
    messages = [from_dict(e) for e in DOC_EXAMPLES]
    with LogWriter(path) as log:
        for m in messages:
            log.write(m)
    with path.open("a", encoding="utf-8") as fh:  # a newer sender's message, and a blank line
        fh.write('{"v":1,"type":"laser_show","t":2000.0}\n\n')

    assert list(read_log(path)) == messages
    with pytest.raises(UnknownMessageType):
        list(read_log(path, skip_unknown=False))


def test_log_errors_report_the_line(tmp_path: Path) -> None:
    path = tmp_path / "bad.jsonl"
    path.write_text(dumps(SideEnd(t=1.0, cause="timer")) + '\n{"v":1,"type":"side_end"}\n')
    with pytest.raises(ContractError, match=r"bad\.jsonl:2"):
        list(read_log(path))


class FakeClock:
    def __init__(self) -> None:
        self.now = 100.0
        self.slept: list[float] = []

    def __call__(self) -> float:
        return self.now

    def sleep(self, seconds: float) -> None:
        self.slept.append(seconds)
        self.now += seconds


def test_replay_reproduces_original_timing() -> None:
    times = [10.0, 10.0, 10.5, 12.0, 11.0, 13.0]  # 11.0 goes backwards: no wait
    messages = [SideEnd(t=t, cause="timer") for t in times]
    clock = FakeClock()
    yielded_at = [(m.t, clock.now) for m in replay(messages, clock=clock, sleep=clock.sleep)]
    assert [at - 100.0 for _, at in yielded_at] == [0.0, 0.0, 0.5, 2.0, 2.0, 3.0]


def test_replay_speed() -> None:
    messages = [SideEnd(t=t, cause="timer") for t in (0.0, 1.0, 3.0)]
    clock = FakeClock()
    list(replay(messages, speed=2.0, clock=clock, sleep=clock.sleep))
    assert clock.now - 100.0 == pytest.approx(1.5)
    with pytest.raises(ValueError):
        list(replay(messages, speed=0))


def test_play_log_cli(tmp_path: Path) -> None:
    import subprocess
    import sys

    path = tmp_path / "run.jsonl"
    with LogWriter(path) as log:
        for e in DOC_EXAMPLES:
            log.write(from_dict(e))
    script = Path(__file__).resolve().parents[1] / "tools" / "play_log.py"
    summary = subprocess.run(
        [sys.executable, str(script), str(path), "--summary"], capture_output=True, text=True
    )
    assert summary.returncode == 0, summary.stderr
    assert "6 messages" in summary.stdout
    played = subprocess.run(
        [sys.executable, str(script), str(path), "--speed", "10000"]
        + ["--type", "side_end", "--type", "side_state"],  # doc examples span ~20 minutes
        capture_output=True,
        text=True,
        timeout=60,
    )
    assert played.returncode == 0, played.stderr
    assert "cause=run_out_groove" in played.stdout
    assert "features" not in played.stdout


def test_replay_real_clock_is_never_early() -> None:
    messages = [SideEnd(t=t, cause="timer") for t in (5.0, 5.05, 5.1, 5.2)]
    start = time.perf_counter()
    for m in replay(messages):
        assert time.perf_counter() - start >= m.t - 5.0
