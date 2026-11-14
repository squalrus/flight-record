"""Key and tempo on the generated songs in ``samples/_gen/``, scored against ``truth.csv``.

The truth file holds measured values where the generator missed what it was asked for (see
``samples/_gen/README.md``). Songs whose audio is missing, or is only a Git LFS pointer
(cloned without ``git lfs pull``), are skipped. Known misses are marked ``xfail`` with the
reason, so a fix shows up as an unexpected pass.
"""

from __future__ import annotations

import sys
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

import pytest

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import evaluate  # noqa: E402  (a script in this folder, not a package)

SONGS = HERE.parents[1] / "samples" / "_gen"
TRUTH = evaluate.load_truth(SONGS / "truth.csv")

KNOWN_TEMPO_MISSES = {
    "Watching_the_Galaxy_Sleep.mp3": "ambient, almost no percussive onsets: the tracker "
    "reports 160 for an 80 BPM grid",
}
KNOWN_KEY_MISSES = {
    "Vanguard_of_the_Sunlit_Rim.mp3": "detector reads Eb major for the stated Bb major "
    "(a fifth away); the stated key is not yet confirmed by ear",
}


def has_audio(path: Path) -> bool:
    if not path.exists() or path.stat().st_size < 1024:
        return False
    with path.open("rb") as fh:
        return not fh.read(64).startswith(b"version https://git-lfs")


@pytest.fixture(scope="module")
def results() -> dict[str, evaluate.SongResult]:
    present = [SONGS / name for name in TRUTH if has_audio(SONGS / name)]
    if not present:
        pytest.skip("no generated songs present (git lfs pull?)")
    with ProcessPoolExecutor() as pool:
        return {r.file: r for r in pool.map(evaluate.analyze, present)}


def _result(results: dict, name: str) -> evaluate.SongResult:
    if name not in results:
        pytest.skip(f"{name} not present")
    return results[name]


def _mark(name: str, misses: dict[str, str]) -> object:
    if name in misses:
        return pytest.param(name, marks=pytest.mark.xfail(reason=misses[name], strict=False))
    return name


@pytest.mark.parametrize(
    "name", [_mark(n, KNOWN_TEMPO_MISSES) for n, row in TRUTH.items() if row["bpm"]]
)
def test_tempo(results: dict, name: str) -> None:
    r = _result(results, name)
    truths = [float(b) for b in TRUTH[name]["bpm"].split("/")]
    assert evaluate.score_tempo(r.tempo, truths) == "exact", f"measured {r.tempo}"


@pytest.mark.parametrize(
    "name", [_mark(n, KNOWN_KEY_MISSES) for n, row in TRUTH.items() if row["key"]]
)
def test_key(results: dict, name: str) -> None:
    r = _result(results, name)
    pred = evaluate.key_name(r.key) if r.key else None
    assert evaluate.score_key(r.key, evaluate.parse_key(TRUTH[name]["key"])) == "exact", (
        f"measured {pred}"
    )
