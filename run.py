"""Shortcuts for the explorer: play a song by a short name, or listen on the mic.

    python run.py list            # your songs and their short names
    python run.py reference       # the synthesized reference song (tools/make_reference_song.py)
    python run.py kalax           # replay that song: speakers + identification + Discogs
    python run.py silllver        # any part of the artist, title, or file name works
    python run.py mic             # listen on FR_AUDIO_DEVICE (or --device N) instead

Options (before any song name or after it):
    --plain         just the analysis: no identification, no Discogs
    --audd          identify with AudD instead of ACRCloud
    --quiet         replay without playing through the speakers
    --device N      input device for mic mode (default: FR_AUDIO_DEVICE in .env.local)
Anything after "--" is passed to explore.py as is, e.g. ``python run.py kalax -- --loop``.

Song names come from the files' tags at run time, so no song titles live in the code.

No need to activate the venv: started with any Python, this re-runs itself with the
project's ``.venv`` interpreter (and everything it starts uses that one too).
"""

from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
EXPLORE = ROOT / "experiments" / "02-onset-features" / "explore.py"
SONGS = ROOT / "samples" / "_songs"
SONG_DIRS = (
    (SONGS, "*"),
    (ROOT / "samples" / "_gen", "*"),
    (ROOT / "samples" / "_local", "reference-*"),
)
"""(folder, file pattern): your songs, the generated songs, and reference songs from
``tools/make_reference_song.py``."""
AUDIO = {".mp3", ".flac", ".ogg", ".wav"}


def venv_python(root: Path = ROOT) -> Path | None:
    """The project's virtual-environment interpreter, if one exists."""
    for candidate in (root / ".venv" / "Scripts" / "python.exe", root / ".venv" / "bin" / "python"):
        if candidate.exists():
            return candidate
    return None


def needs_venv(current: str, venv: Path | None) -> bool:
    """True when running under some other Python while the project has a venv."""
    if venv is None:
        return False
    try:
        return Path(current).resolve() != venv.resolve()
    except OSError:
        return True


def slug(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "", text.casefold())


def songs() -> list[tuple[str, Path, str]]:
    """(short name, path, "Artist - Title") for each song, short names unique."""
    from flightrec.tags import read_tags

    found = []
    used: set[str] = set()
    paths = [
        p
        for folder, pattern in SONG_DIRS
        if folder.is_dir()
        for p in sorted(folder.glob(pattern))
        if p.suffix.lower() in AUDIO
    ]
    for path in paths:
        tags = read_tags(path)
        if not tags.artist:  # untagged (generated and reference songs): use the file name
            title = tags.title or path.stem.replace("_", " ")
            name = slug(path.stem)
            label = title
        else:
            title = tags.title or path.stem
            name = slug(tags.artist.removeprefix("the ").removeprefix("The ")) or slug(path.stem)
            label = f"{tags.artist} - {title}"
        if name in used:
            name = f"{name}-{slug(title)[:12]}"
        used.add(name)
        found.append((name, path, label))
    return found


def pick(query: str, catalog: list[tuple[str, Path, str]]) -> list[tuple[str, Path, str]]:
    """Exact short name first, then any song whose name, title, or file contains the query."""
    q = slug(query)
    exact = [s for s in catalog if s[0] == q]
    if exact:
        return exact
    return [s for s in catalog if q and (q in s[0] or q in slug(s[2]) or q in slug(s[1].stem))]


def main(argv: list[str]) -> int:
    passthrough: list[str] = []
    if "--" in argv:
        i = argv.index("--")
        argv, passthrough = argv[:i], argv[i + 1 :]
    flags = {a for a in argv if a in ("--plain", "--audd", "--quiet")}
    device = None
    if "--device" in argv:
        i = argv.index("--device")
        if i + 1 >= len(argv):
            print("--device needs a number or name")
            return 2
        device = argv[i + 1]
        argv = argv[:i] + argv[i + 2 :]
    words = [a for a in argv if not a.startswith("--")]
    catalog = songs()

    if not words or words[0] in ("list", "help", "-h"):
        print(__doc__.split("Options")[0].rstrip())
        folders = ", ".join(str(d.relative_to(ROOT)) for d, _ in SONG_DIRS)
        print(f"\nsongs in {folders}:")
        for name, _, label in catalog:
            print(f"  python run.py {name:<20} {label}")
        if not catalog:
            print("  (none: put audio files in samples/_songs)")
        print("  python run.py mic                   listen on the microphone / line in")
        return 0

    command = [sys.executable, str(EXPLORE)]
    if words[0] == "mic":
        if device is not None:
            command += ["--device", device]
        what = f"device {device}" if device else "FR_AUDIO_DEVICE (or the default input)"
    else:
        matches = pick(" ".join(words), catalog)
        if len(matches) != 1:
            problem = "several songs match" if matches else "no song matches"
            print(f"{problem} '{' '.join(words)}':")
            for name, _, label in matches or catalog:
                print(f"  {name:<20} {label}")
            return 2
        name, path, label = matches[0]
        command += ["--wav", str(path)]
        if "--quiet" not in flags:
            command.append("--listen")
        what = label
    if "--plain" not in flags:
        command += ["--identify", "audd" if "--audd" in flags else "acrcloud", "--discogs"]
    command += passthrough
    print(f"> {what}")
    return subprocess.call(command, cwd=ROOT)


if __name__ == "__main__":
    venv = venv_python()
    if needs_venv(sys.executable, venv):
        # Re-run with the project's interpreter, which has flightrec and its dependencies.
        sys.exit(subprocess.call([str(venv), str(Path(__file__).resolve()), *sys.argv[1:]]))
    if venv is None:
        print("note: no .venv found; using this Python (see experiments/README.md#setup)")
    sys.exit(main(sys.argv[1:]))
