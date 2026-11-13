"""Score experiment 02's key and tempo against published values for a set of songs.

Usage (from the repo root, with the venv active):
    python experiments/02-onset-features/evaluate.py                    # samples/_songs/
    python experiments/02-onset-features/evaluate.py --songs DIR --truth FILE.csv

The truth CSV has columns ``file,key,bpm,source``: ``key`` like "F# minor" or "Ab major"
(also accepts the sharp/flat symbols), ``bpm`` one or more numbers separated by "/" (e.g.
"174/176" when sources disagree). Leave key or bpm blank if unknown. If the CSV does not
exist, a template listing every audio file in the folder is written for you to fill in.

For each song the whole file is analyzed offline (results are identical to live replay).
Reported per song:
- key: the key held for the most hops (after a warm-up) and its share of the song.
- tempo: the most common confident tempo (2-BPM bins, confidence >= 0.3) and its share.
Scoring, against any of the published values:
- key: exact / relative (same notes, other mode) / fifth (neighboring key, same mode) /
  parallel (same tonic, other mode) / wrong.
- tempo: exact (within 4%) / half-double (x2 or x0.5) / third (x2/3 or x3/2) / wrong.
"""

from __future__ import annotations

import argparse
import csv
import sys
import time
from collections import Counter
from concurrent.futures import ProcessPoolExecutor
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from flightrec.audio import FeatureExtractor, read_audio
from flightrec.audio.features import PITCH_CLASSES

ROOT = Path(__file__).resolve().parents[2]
AUDIO_SUFFIXES = {".mp3", ".flac", ".ogg", ".wav", ".aiff", ".aif"}
WARMUP_S = 10.0
SPELLINGS = {
    "C": 0, "B#": 0, "C#": 1, "DB": 1, "D": 2, "D#": 3, "EB": 3, "E": 4, "FB": 4,
    "F": 5, "E#": 5, "F#": 6, "GB": 6, "G": 7, "G#": 8, "AB": 8, "A": 9, "A#": 10,
    "BB": 10, "B": 11, "CB": 11,
}  # fmt: skip


def parse_key(text: str) -> tuple[int, str] | None:
    """'F# minor', 'F♯ min', 'Gbm', 'A major' -> (pitch class, 'major' | 'minor')."""
    t = text.strip().replace("♯", "#").replace("♭", "b")
    if not t:
        return None
    letter = t[0].upper()
    rest = t[1:]
    accidental = rest[:1] if rest[:1] in ("#", "b") else ""
    mode_text = rest[len(accidental) :].strip().lower()
    minor = mode_text.startswith("min") or mode_text == "m"
    pc = SPELLINGS.get((letter + accidental).upper())
    return None if pc is None else (pc, "minor" if minor else "major")


def key_name(key: tuple[int, str]) -> str:
    return f"{PITCH_CLASSES[key[0]]} {key[1]}"


def relative(key: tuple[int, str]) -> tuple[int, str]:
    pc, mode = key
    return ((pc + 9) % 12, "minor") if mode == "major" else ((pc + 3) % 12, "major")


def score_key(pred: tuple[int, str] | None, truth: tuple[int, str] | None) -> str:
    if truth is None:
        return "-"
    if pred is None:
        return "none"
    if pred == truth:
        return "exact"
    if pred == relative(truth):
        return "relative"
    if pred[1] == truth[1] and (pred[0] - truth[0]) % 12 in (5, 7):
        return "fifth"
    if pred[0] == truth[0]:
        return "parallel"
    return "wrong"


def score_tempo(pred: float | None, truths: list[float]) -> str:
    if not truths:
        return "-"
    if pred is None:
        return "none"
    for label, factors in (("exact", (1.0,)), ("half-double", (2.0, 0.5)), ("third", (2 / 3, 1.5))):
        if any(abs(pred - t * f) / (t * f) <= 0.04 for t in truths for f in factors):
            return label
    return "wrong"


@dataclass
class SongResult:
    file: str
    seconds: float
    key: tuple[int, str] | None
    key_share: float
    tempo: float | None
    tempo_share: float
    tempo_confidence: float
    process_s: float


def analyze(path: Path) -> SongResult:
    samplerate, frames = read_audio(path)
    fx = FeatureExtractor(samplerate)
    padded = np.concatenate([np.zeros((fx.window, frames.shape[1]), np.float32), frames])
    keys: Counter[str] = Counter()
    tempos: Counter[float] = Counter()
    confidences: list[float] = []
    t0 = time.perf_counter()
    for end in range(fx.hop, len(frames) + 1, fx.hop):
        f = fx.process(padded[end : end + fx.window], end)
        if f.t < WARMUP_S:
            continue
        if f.key:
            keys[f.key] += 1
        if f.tempo and f.tempo_confidence >= 0.3:
            tempos[round(f.tempo / 2) * 2] += 1
            confidences.append(f.tempo_confidence)
    process_s = time.perf_counter() - t0
    key, key_n = keys.most_common(1)[0] if keys else (None, 0)
    tempo, tempo_n = tempos.most_common(1)[0] if tempos else (None, 0)
    return SongResult(
        file=path.name,
        seconds=len(frames) / samplerate,
        key=parse_key(key) if key else None,
        key_share=key_n / max(sum(keys.values()), 1),
        tempo=float(tempo) if tempo is not None else None,
        tempo_share=tempo_n / max(sum(tempos.values()), 1),
        tempo_confidence=float(np.median(confidences)) if confidences else 0.0,
        process_s=process_s,
    )


def load_truth(path: Path) -> dict[str, dict[str, str]]:
    with path.open(newline="", encoding="utf-8") as fh:
        return {row["file"]: row for row in csv.DictReader(fh)}


def write_template(path: Path, files: list[Path]) -> None:
    with path.open("w", newline="", encoding="utf-8") as fh:
        writer = csv.writer(fh)
        writer.writerow(["file", "key", "bpm", "source"])
        for f in files:
            writer.writerow([f.name, "", "", ""])


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Score key and tempo against published values.")
    parser.add_argument("--songs", type=Path, default=ROOT / "samples" / "_songs")
    parser.add_argument("--truth", type=Path, help="CSV (default SONGS/truth.csv)")
    parser.add_argument("--jobs", type=int, default=None, help="parallel processes")
    args = parser.parse_args(argv)

    files = sorted(p for p in args.songs.iterdir() if p.suffix.lower() in AUDIO_SUFFIXES)
    if not files:
        parser.error(f"no audio files in {args.songs}")
    truth_path = args.truth or args.songs / "truth.csv"
    if not truth_path.exists():
        write_template(truth_path, files)
        print(f"wrote a template to {truth_path}; fill in key and bpm to get scores")
    truth = load_truth(truth_path)

    started = time.perf_counter()
    with ProcessPoolExecutor(max_workers=args.jobs) as pool:
        results = list(pool.map(analyze, files))

    key_scores: Counter[str] = Counter()
    tempo_scores: Counter[str] = Counter()
    print(f"\n{'song':<44} {'pred key':<10} {'held':>4}  {'truth':<10} {'score':<9}"
          f" {'tempo':>5} {'held':>4} {'conf':>4}  {'truth':<9} score")  # fmt: skip
    for r in results:
        row = truth.get(r.file, {})
        true_key = parse_key(row.get("key", "") or "")
        true_bpm = [float(b) for b in (row.get("bpm", "") or "").split("/") if b.strip()]
        ks = score_key(r.key, true_key)
        ts = score_tempo(r.tempo, true_bpm)
        if ks != "-":
            key_scores[ks] += 1
        if ts != "-":
            tempo_scores[ts] += 1
        print(
            f"{r.file[:44]:<44} {key_name(r.key) if r.key else '-':<10} {r.key_share:4.0%}  "
            f"{key_name(true_key) if true_key else '-':<10} {ks:<9} "
            f"{r.tempo or 0:5.0f} {r.tempo_share:4.0%} {r.tempo_confidence:4.2f}  "
            f"{'/'.join(f'{b:g}' for b in true_bpm) or '-':<9} {ts}"
        )
    audio = sum(r.seconds for r in results)
    cpu = sum(r.process_s for r in results)
    print(f"\nkey:   {dict(key_scores) or 'no truth yet'}")
    print(f"tempo: {dict(tempo_scores) or 'no truth yet'}")
    wall = time.perf_counter() - started
    print(
        f"{len(results)} songs, {audio / 60:.1f} min of audio in {wall:.0f} s"
        f" (analysis {cpu / audio * 100:.1f}% of real time per song)"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
