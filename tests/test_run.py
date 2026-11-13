"""run.py shortcuts: short names, matching, and the explore.py command it builds."""

import importlib.util
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("run", ROOT / "run.py")
run = importlib.util.module_from_spec(spec)
sys.modules["run"] = run
spec.loader.exec_module(run)

CATALOG = [
    ("kalax", Path("Kalax - III - 13 Calling.mp3"), "Kalax - Calling"),
    (
        "olllam",
        Path("the olllam - elllegy - 03 stream of silllver.mp3"),
        "the olllam - stream of silllver",
    ),
    (
        "standards",
        Path("standards - Fruit Island - 04 Special Berry.mp3"),
        "standards - Special Berry",
    ),
]


def test_pick_by_short_name_title_or_file() -> None:
    assert [s[0] for s in run.pick("kalax", CATALOG)] == ["kalax"]
    assert [s[0] for s in run.pick("silllver", CATALOG)] == ["olllam"]
    assert [s[0] for s in run.pick("fruit island", CATALOG)] == ["standards"]
    assert run.pick("zzz", CATALOG) == []


def test_commands(monkeypatch) -> None:
    calls = []
    monkeypatch.setattr(run, "songs", lambda: CATALOG)
    monkeypatch.setattr(run.subprocess, "call", lambda cmd, cwd: calls.append(cmd) or 0)

    run.main(["kalax"])
    assert calls[-1][2:] == [
        "--wav",
        "Kalax - III - 13 Calling.mp3",
        "--listen",
        "--identify",
        "acrcloud",
        "--discogs",
    ]
    run.main(["mic", "--device", "9", "--audd"])
    assert calls[-1][2:] == ["--device", "9", "--identify", "audd", "--discogs"]
    run.main(["standards", "--plain", "--quiet", "--", "--loop"])
    assert calls[-1][2:] == ["--wav", "standards - Fruit Island - 04 Special Berry.mp3", "--loop"]
    assert run.main(["zzz"]) == 2 and len(calls) == 3


def test_switches_to_the_project_venv(tmp_path: Path) -> None:
    assert run.venv_python(tmp_path) is None
    exe = tmp_path / ".venv" / "Scripts" / "python.exe"
    exe.parent.mkdir(parents=True)
    exe.write_bytes(b"")
    assert run.venv_python(tmp_path) == exe
    assert run.needs_venv(sys.executable, exe)  # the test runner is some other Python
    assert not run.needs_venv(str(exe), exe)
    assert not run.needs_venv(sys.executable, None)
