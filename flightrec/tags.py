"""Read basic ID3v2 text tags (artist, title, album, BPM, key) from MP3 files.

Just enough of ID3v2.3/2.4 for text frames, with no dependency. Used for answer keys in
the experiments (e.g. who a song is by, for scoring fingerprint matches) and, later, for
identifying audio files without fingerprinting (decision D-012).
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

_ENCODINGS = {0: "latin-1", 1: "utf-16", 2: "utf-16-be", 3: "utf-8"}


@dataclass(frozen=True)
class Tags:
    artist: str | None = None
    title: str | None = None
    album: str | None = None
    bpm: str | None = None
    key: str | None = None


def _syncsafe(b: bytes) -> int:
    return (b[0] & 0x7F) << 21 | (b[1] & 0x7F) << 14 | (b[2] & 0x7F) << 7 | (b[3] & 0x7F)


def read_id3_frames(path: str | Path) -> dict[str, list[str]]:
    """All ID3v2 text frames (``T***``) as ``{frame_id: [values]}``; empty if none."""
    with Path(path).open("rb") as fh:
        head = fh.read(10)
        if len(head) < 10 or head[:3] != b"ID3":
            return {}
        version = head[3]
        data = fh.read(_syncsafe(head[6:10]))
    frames: dict[str, list[str]] = {}
    i = 0
    while i + 10 <= len(data):
        fid = data[i : i + 4]
        if not fid.strip(b"\0") or not fid.isalnum():
            break
        size = (
            _syncsafe(data[i + 4 : i + 8]) if version == 4 else int.from_bytes(data[i + 4 : i + 8])
        )
        body = data[i + 10 : i + 10 + size]
        i += 10 + size
        name = fid.decode("latin-1")
        if not name.startswith("T") or not body:
            continue
        try:
            text = body[1:].decode(_ENCODINGS.get(body[0], "latin-1"))
        except UnicodeDecodeError:
            continue
        frames.setdefault(name, []).extend(v for v in text.split("\0") if v.strip())
    return frames


def read_tags(path: str | Path) -> Tags:
    """Artist, title, album, BPM, and key from an MP3's ID3v2 tags.

    Falls back to the "Artist - Album - NN Title" filename pattern that many stores use
    when the tags lack artist or title.
    """
    frames = read_id3_frames(path) if Path(path).suffix.lower() == ".mp3" else {}

    def first(fid: str) -> str | None:
        values = frames.get(fid)
        return values[0].strip() if values else None

    artist, title, album = first("TPE1"), first("TIT2"), first("TALB")
    if not (artist and title):
        parts = Path(path).stem.split(" - ")
        if len(parts) >= 2:
            artist = artist or parts[0].strip()
            title = title or re.sub(r"^\d+\s+", "", parts[-1]).strip()
            if len(parts) >= 3:
                album = album or parts[1].strip()
    return Tags(artist=artist, title=title, album=album, bpm=first("TBPM"), key=first("TKEY"))
