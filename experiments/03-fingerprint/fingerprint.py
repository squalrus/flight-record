"""Experiment 03: identify clips cut from audio files with recognition services.

Usage (from the repo root, with the venv active):
    python experiments/03-fingerprint/fingerprint.py --dry-run          # plan and cost only
    python experiments/03-fingerprint/fingerprint.py --max-requests 60  # query configured services

Clips are cut from every file in ``--songs`` (default samples/_songs) at several
positions and lengths, optionally degraded with simulated vinyl, sent to each service
whose keys are in .env (AUDD_API_TOKEN; ACRCLOUD_HOST / ACCESS_KEY / ACCESS_SECRET), and
scored against the file's own tags. Every answer is cached in out/cache/, so re-runs and
re-scoring cost nothing; only uncached requests count toward ``--max-requests``.

Results are an upper bound for vinyl: clean digital clips are the best case, and the
"vinyl" condition is a rough simulation (hiss, rumble, crackle, wow, treble roll-off),
not a substitute for real records through the interface.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import re
import sys
import zlib
from collections import defaultdict
from dataclasses import asdict, dataclass, replace
from datetime import datetime
from difflib import SequenceMatcher
from pathlib import Path

import numpy as np
import soundfile
from scipy.signal import butter, sosfilt

from flightrec.console import safe_output
from flightrec.env import load_env
from flightrec.identification import SERVICES, Result, Service
from flightrec.tags import read_tags

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
AUDIO_SUFFIXES = {".mp3", ".flac", ".ogg", ".wav"}
OUTCOMES = ("correct", "other_version", "wrong", "no_match", "error")


@dataclass(frozen=True)
class Clip:
    path: Path
    artist: str
    title: str
    start_s: float
    length_s: float
    position: float
    condition: str

    @property
    def song(self) -> str:
        return f"{self.artist} - {self.title}"


# Planning and audio


def plan(
    songs: list[Path], lengths: list[float], positions: list[float], conditions: list[str]
) -> list[Clip]:
    clips = []
    for path in songs:
        tags = read_tags(path)
        duration = soundfile.info(str(path)).duration
        for length in lengths:
            for position in positions:
                start = min(max(0.0, position * duration - length / 2), max(0.0, duration - length))
                for condition in conditions:
                    clips.append(
                        Clip(path, tags.artist or "?", tags.title or path.stem, round(start, 2),
                             length, position, condition)
                    )  # fmt: skip
    return clips


def cut(clip: Clip) -> tuple[int, np.ndarray]:
    """Mono float32 audio for the clip."""
    info = soundfile.info(str(clip.path))
    sr = info.samplerate
    start = int(clip.start_s * sr)
    data, sr = soundfile.read(
        str(clip.path), start=start, stop=start + int(clip.length_s * sr), dtype="float32",
        always_2d=True,
    )  # fmt: skip
    return sr, data.mean(axis=1)


def simulate_vinyl(audio: np.ndarray, sr: int, seed: int) -> np.ndarray:
    """A rough vinyl stand-in: wow (0.15% at 33 1/3 rpm), treble roll-off above 12 kHz,
    rumble (30 dB below the music), hiss (45 dB below), and crackle (about 15 clicks/s)."""
    rng = np.random.default_rng(seed)
    n = len(audio)
    t = np.arange(n) / sr
    warped = t + 0.0015 / (2 * np.pi * 0.555) * np.sin(2 * np.pi * 0.555 * t)
    out = np.interp(warped * sr, np.arange(n), audio).astype(np.float64)
    out = sosfilt(butter(2, 12_000, "low", fs=sr, output="sos"), out)
    rms = float(np.sqrt(np.mean(out**2))) or 1e-4
    rumble = sosfilt(butter(2, 30, "low", fs=sr, output="sos"), rng.standard_normal(n))
    out += rumble / (np.sqrt(np.mean(rumble**2)) or 1) * rms * 10 ** (-30 / 20)
    out += rng.standard_normal(n) * rms * 10 ** (-45 / 20)
    clicks = np.zeros(n)
    where = rng.random(n) < 15 / sr
    clicks[where] = rng.uniform(-1, 1, where.sum()) * np.max(np.abs(out)) * 0.4
    out += sosfilt(butter(1, 3_000, "high", fs=sr, output="sos"), clicks)
    return np.clip(out, -1.0, 1.0).astype(np.float32)


def clip_wav(clip: Clip) -> bytes:
    """The clip as 16-bit mono WAV bytes, after its condition is applied."""
    sr, audio = cut(clip)
    if clip.condition == "vinyl":
        seed = zlib.crc32(f"{clip.path.name}|{clip.start_s}|{clip.length_s}".encode())
        audio = simulate_vinyl(audio, sr, seed)
    elif clip.condition != "clean":
        raise ValueError(f"unknown condition {clip.condition!r}")
    buffer = io.BytesIO()
    soundfile.write(buffer, audio, sr, format="WAV", subtype="PCM_16")
    return buffer.getvalue()


# Scoring


def normalize(text: str | None) -> str:
    """Lowercase, drop bracketed parts (feat., cover, remaster) and punctuation."""
    text = (text or "").casefold()
    text = re.sub(r"[\(\[][^\)\]]*[\)\]]", " ", text)
    text = re.sub(r"\b(feat|ft|featuring)\.?\s.*$", " ", text)
    return " ".join(re.findall(r"[a-z0-9]+", text))


def similar(a: str, b: str) -> bool:
    return bool(a and b) and (
        a == b or a in b or b in a or SequenceMatcher(None, a, b).ratio() >= 0.85
    )


def outcome(clip: Clip, result: Result) -> str:
    if result.error:
        return "error"
    if not result.matched:
        return "no_match"
    title_ok = similar(normalize(result.title), normalize(clip.title))
    artist_ok = similar(normalize(result.artist), normalize(clip.artist))
    if title_ok and artist_ok:
        return "correct"
    if title_ok:
        return "other_version"  # same song, other artist: a cover, remix, or the original
    return "wrong"


# Running with a cache


class Cache:
    def __init__(self, root: Path) -> None:
        self.root = root

    def _path(self, service: str, wav: bytes) -> Path:
        return self.root / service / f"{hashlib.sha1(wav).hexdigest()}.json"

    def get(self, service: str, wav: bytes) -> Result | None:
        path = self._path(service, wav)
        if not path.exists():
            return None
        return Result(**json.loads(path.read_text(encoding="utf-8")))

    def put(self, service: str, wav: bytes, result: Result) -> None:
        path = self._path(service, wav)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(asdict(result)), encoding="utf-8")


def run(
    clips: list[Clip], services: list[Service], cache: Cache, max_requests: int
) -> tuple[list[dict], int, int]:
    """Query every (clip, service); returns (rows, requests sent, skipped by the budget)."""
    rows = []
    sent = skipped = 0
    for clip in clips:
        wav = clip_wav(clip)
        for service in services:
            result = cache.get(service.name, wav)
            cached = result is not None
            if result is not None and result.raw:
                # Re-parse the cached raw response, so parser improvements reach old answers.
                result = replace(service.parse(result.raw), latency_s=result.latency_s)
            if result is None:
                if sent >= max_requests:
                    skipped += 1
                    continue
                result = service.identify(wav)
                sent += 1
                if not result.error:  # do not cache transient failures
                    cache.put(service.name, wav, result)
            rows.append(
                {
                    "service": service.name,
                    "song": clip.song,
                    "condition": clip.condition,
                    "length_s": clip.length_s,
                    "position": clip.position,
                    "start_s": clip.start_s,
                    "outcome": outcome(clip, result),
                    "artist": result.artist,
                    "title": result.title,
                    "album": result.album,
                    "score": result.score,
                    "duration_s": result.duration_s,
                    "offset_s": result.offset_s,
                    "latency_s": round(result.latency_s, 3),
                    "cached": cached,
                    "error": result.error,
                }
            )
    return rows, sent, skipped


def summarize(rows: list[dict]) -> None:
    groups: dict[tuple, list[dict]] = defaultdict(list)
    for row in rows:
        groups[(row["service"], row["condition"], row["length_s"])].append(row)
    print(f"\n{'service':<9} {'cond':<6} {'len':>4} {'n':>4}  {'correct':>7} {'other':>5} "
          f"{'wrong':>5} {'none':>5} {'error':>5}  {'median s':>8}")  # fmt: skip
    for (service, condition, length), group in sorted(groups.items()):
        n = len(group)
        counts = {o: sum(r["outcome"] == o for r in group) for o in OUTCOMES}
        latencies = [r["latency_s"] for r in group if r["outcome"] != "error"]
        median = f"{np.median(latencies):8.2f}" if latencies else "       -"
        print(
            f"{service:<9} {condition:<6} {length:>4g} {n:>4}  {counts['correct'] / n:7.0%} "
            f"{counts['other_version']:>5} {counts['wrong']:>5} {counts['no_match']:>5} "
            f"{counts['error']:>5}  {median}"
        )
    per_song: dict[str, list[str]] = defaultdict(list)
    for row in rows:
        per_song[row["song"]].append(row["outcome"])
    print("\nper song (correct / total):")
    for song, outcomes in sorted(per_song.items()):
        print(f"  {sum(o == 'correct' for o in outcomes):>3}/{len(outcomes):<3} {song}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Experiment 03: fingerprint clips.")
    parser.add_argument("--songs", type=Path, default=ROOT / "samples" / "_songs")
    parser.add_argument("--services", nargs="+", choices=sorted(SERVICES), default=sorted(SERVICES))
    parser.add_argument("--lengths", nargs="+", type=float, default=[5.0, 10.0, 15.0])
    parser.add_argument(
        "--positions", nargs="+", type=float, default=[0.15, 0.5, 0.85],
        help="clip centers as fractions of the song",
    )  # fmt: skip
    parser.add_argument(
        "--conditions", nargs="+", choices=["clean", "vinyl"], default=["clean", "vinyl"]
    )
    parser.add_argument(
        "--max-requests", type=int, default=50, help="uncached requests, all services"
    )
    parser.add_argument("--dry-run", action="store_true", help="plan and count; send nothing")
    parser.add_argument("--save-clips", type=Path, help="also write each clip WAV here (to listen)")
    args = parser.parse_args(argv)
    safe_output()

    load_env()
    songs = sorted(p for p in args.songs.iterdir() if p.suffix.lower() in AUDIO_SUFFIXES)
    if not songs:
        parser.error(f"no audio files in {args.songs}")
    clips = plan(songs, args.lengths, args.positions, args.conditions)
    configured = {name: SERVICES[name].from_env() for name in args.services}
    cache = Cache(HERE / "out" / "cache")

    print(f"{len(songs)} songs x {len(args.lengths)} lengths x {len(args.positions)} positions x "
          f"{len(args.conditions)} conditions = {len(clips)} clips")  # fmt: skip
    for name, service in configured.items():
        print(f"  {name}: {'keys set' if service else 'no keys in .env (skipped)'}")
    active = [s for s in configured.values() if s is not None]

    if args.save_clips:
        args.save_clips.mkdir(parents=True, exist_ok=True)
        for i, clip in enumerate(clips):
            name = f"{i:03d}-{clip.condition}-{clip.length_s:g}s-{clip.path.stem[:40]}.wav"
            (args.save_clips / name).write_bytes(clip_wav(clip))
        print(f"wrote {len(clips)} clips to {args.save_clips}")

    if args.dry_run or not active:
        names = [s.name for s in active] or list(args.services)
        wavs = [clip_wav(c) for c in clips]
        uncached = {name: sum(cache.get(name, w) is None for w in wavs) for name in names}
        total = sum(uncached.values())
        print(f"uncached requests needed: {uncached} (total {total}; ", end="")
        print(f"--max-requests {args.max_requests})")
        if not active and not args.dry_run:
            print("no services configured: copy .env.example to .env and add keys")
        return 0

    rows, sent, skipped = run(clips, active, cache, args.max_requests)
    out = HERE / "out" / f"fingerprint-{datetime.now():%Y%m%d-%H%M%S}.csv"
    out.parent.mkdir(parents=True, exist_ok=True)
    if rows:
        with out.open("w", newline="", encoding="utf-8") as fh:
            writer = csv.DictWriter(fh, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
        summarize(rows)
        print(f"\nresults: {out}")
    print(f"sent {sent} requests; {skipped} skipped by --max-requests (re-run to continue)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
