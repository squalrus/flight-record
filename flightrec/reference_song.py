"""A synthesized reference song whose tempo, key, onsets, and sections are exact by construction.

Generated music (``samples/_gen/``) does not reliably hit the tempo or key it was asked
for, so this module renders a song from code instead: drums on a fixed grid, a bass line on
the chord roots, triads from one diatonic scale, and a lead line, arranged in sections that
add and remove layers. ``render`` returns the audio and a ``Truth`` that lists every drum
hit, beat, and section boundary in seconds, so tests can score the feature pipeline against
known answers. Rendering is deterministic (the noise for the drums is seeded).

Write one to disk with ``python tools/make_reference_song.py``.
"""

from __future__ import annotations

from dataclasses import asdict, dataclass, field
from typing import Any

import numpy as np

PITCH_CLASSES = ("C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B")
SCALES = {"major": (0, 2, 4, 5, 7, 9, 11), "minor": (0, 2, 3, 5, 7, 8, 10)}
DRUMS = ("kick", "snare", "hat")
LAYERS = (*DRUMS, "bass", "chords", "lead")
PROGRESSION = (0, 3, 4, 0)
"""Scale degrees of the chord roots, one chord per bar: i-iv-v-i (minor) or I-IV-V-I."""
LEAD_PATTERN = (0, 2, 4, 2, 4, 5, 4, 2)
"""Scale degrees of the lead, one per beat, over two bars."""


@dataclass(frozen=True)
class Section:
    name: str
    bars: int
    layers: frozenset[str]


def section(name: str, bars: int, *layers: str) -> Section:
    """A section with the given layers (names from ``LAYERS``)."""
    unknown = set(layers) - set(LAYERS)
    if unknown:
        raise ValueError(f"unknown layers {sorted(unknown)}")
    return Section(name, bars, frozenset(layers))


DEFAULT_SECTIONS = (
    section("drums", 8, *DRUMS),
    section("bass", 8, *DRUMS, "bass"),
    section("chords", 8, *DRUMS, "bass", "chords"),
    section("peak", 8, *LAYERS),
    section("breakdown", 8, "chords"),
    section("return", 8, *DRUMS, "bass", "chords"),
    section("outro", 4, "kick", "chords"),
)
"""52 bars, 97.5 s of music at 128 BPM: layers enter one at a time, a drumless breakdown, a
return, and a short outro, followed by silence."""


@dataclass
class SectionTruth:
    name: str
    start: float
    end: float
    layers: list[str]


@dataclass
class Truth:
    """Exact answers for a rendered song. Times are seconds from the start of the audio."""

    bpm: float
    key: str
    """Like "A minor": the scale every note is drawn from."""
    samplerate: int
    length: float
    """Total length, including the silence after the last bar."""
    music_end: float
    """When the last bar ends; only the decay of the last notes and silence follow."""
    beats: list[float]
    """Every beat of every bar, in every section (drums or not)."""
    sections: list[SectionTruth]
    hits: dict[str, list[float]] = field(default_factory=dict)
    """Start time of every note per layer (drums, bass, chord changes, lead)."""

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


def parse_key(key: str) -> tuple[int, str]:
    """ "A minor" or "Bb major" -> (pitch class, mode)."""
    name, _, mode = key.strip().partition(" ")
    aliases = {"Db": "C#", "D#": "Eb", "Gb": "F#", "G#": "Ab", "A#": "Bb"}
    name = aliases.get(name, name)
    if name not in PITCH_CLASSES or mode not in SCALES:
        raise ValueError(f"key must look like 'A minor' or 'Bb major', not {key!r}")
    return PITCH_CLASSES.index(name), mode


def _hz(midi: float) -> float:
    return 440.0 * 2.0 ** ((midi - 69) / 12)


def _midi(tonic: int, scale: tuple[int, ...], degree: int, octave: int) -> int:
    """MIDI note of a scale degree (may exceed 6: wraps into the next octave)."""
    o, d = divmod(degree, len(scale))
    return 12 * (octave + 1 + o) + tonic + scale[d]


class _Renderer:
    def __init__(self, samplerate: int, length: float, seed: int) -> None:
        self.sr = samplerate
        self.out = np.zeros(int(round(length * samplerate)), dtype=np.float64)
        self.rng = np.random.default_rng(seed)

    def _t(self, seconds: float) -> np.ndarray:
        return np.arange(int(seconds * self.sr)) / self.sr

    def add(self, start: float, sound: np.ndarray) -> None:
        i = int(round(start * self.sr))
        n = min(len(sound), len(self.out) - i)
        if n > 0:
            self.out[i : i + n] += sound[:n]

    def kick(self) -> np.ndarray:
        t = self._t(0.35)
        freq = 48.0 + 110.0 * np.exp(-t / 0.03)
        phase = 2 * np.pi * np.cumsum(freq) / self.sr
        return 0.9 * np.sin(phase) * np.exp(-t / 0.12)

    def snare(self) -> np.ndarray:
        t = self._t(0.25)
        noise = np.diff(self.rng.standard_normal(len(t) + 1))  # tilt toward highs
        body = np.sin(2 * np.pi * 185.0 * t) * np.exp(-t / 0.04)
        return 0.22 * noise * np.exp(-t / 0.07) + 0.3 * body

    def hat(self) -> np.ndarray:
        t = self._t(0.06)
        noise = np.diff(np.diff(self.rng.standard_normal(len(t) + 2)))
        return 0.05 * noise * np.exp(-t / 0.015)

    def tone(
        self,
        midi: int,
        seconds: float,
        harmonics: tuple[float, ...],
        attack: float,
        decay: float | None,
        amp: float,
    ) -> np.ndarray:
        t = self._t(seconds)
        f = _hz(midi)
        wave = sum(a * np.sin(2 * np.pi * f * (k + 1) * t) for k, a in enumerate(harmonics))
        env = np.minimum(1.0, t / attack)
        if decay is not None:
            env = env * np.exp(-t / decay)
        release = min(0.02, seconds / 4)
        env = env * np.minimum(1.0, (seconds - t) / release)
        return amp * wave * env


def render(
    bpm: float = 128.0,
    key: str = "A minor",
    sections: tuple[Section, ...] = DEFAULT_SECTIONS,
    samplerate: int = 44_100,
    tail: float = 2.0,
    seed: int = 7,
) -> tuple[np.ndarray, Truth]:
    """Render the song: float32 stereo frames ``(n, 2)`` in [-1, 1], and its ``Truth``."""
    if bpm <= 0 or not sections:
        raise ValueError("bpm must be positive and sections non-empty")
    tonic, mode = parse_key(key)
    scale = SCALES[mode]
    beat = 60.0 / bpm
    bar = 4 * beat
    music_end = sum(s.bars for s in sections) * bar
    r = _Renderer(samplerate, music_end + tail, seed)
    kick, snare, hat = r.kick(), r.snare(), r.hat()
    hits: dict[str, list[float]] = {name: [] for name in LAYERS}
    beats: list[float] = []
    truth_sections: list[SectionTruth] = []

    bar_index = 0
    start = 0.0
    for part in sections:
        truth_sections.append(
            SectionTruth(
                part.name,
                start,
                start + part.bars * bar,
                [x for x in LAYERS if x in part.layers],
            )
        )
        for _ in range(part.bars):
            t0 = bar_index * bar
            root = PROGRESSION[bar_index % len(PROGRESSION)]
            for b in range(4):
                tb = t0 + b * beat
                beats.append(tb)
                if "kick" in part.layers:
                    r.add(tb, kick)
                    hits["kick"].append(tb)
                if "snare" in part.layers and b in (1, 3):
                    r.add(tb, snare)
                    hits["snare"].append(tb)
                if "bass" in part.layers:
                    note = _midi(tonic, scale, root, 2)
                    r.add(tb, r.tone(note, 0.9 * beat, (1.0, 0.5, 0.25), 0.005, 0.25, 0.3))
                    hits["bass"].append(tb)
                if "lead" in part.layers:
                    degree = LEAD_PATTERN[(bar_index % 2) * 4 + b]
                    note = _midi(tonic, scale, degree, 5)
                    r.add(tb, r.tone(note, 0.8 * beat, (1.0, 0.3, 0.15), 0.01, 0.3, 0.1))
                    hits["lead"].append(tb)
            if "hat" in part.layers:
                for e in range(8):
                    r.add(t0 + e * beat / 2, hat)
                    hits["hat"].append(t0 + e * beat / 2)
            if "chords" in part.layers:
                for third in range(3):
                    note = _midi(tonic, scale, root + 2 * third, 4)
                    r.add(t0, r.tone(note, bar, (1.0, 0.35, 0.1), 0.03, None, 0.07))
                hits["chords"].append(t0)
            bar_index += 1
        start += part.bars * bar

    peak = float(np.max(np.abs(r.out))) or 1.0
    mono = (r.out * (0.89 / peak)).astype(np.float32)  # peak at about -1 dBFS
    frames = np.stack([mono, mono], axis=1)
    truth = Truth(
        bpm=bpm,
        key=f"{PITCH_CLASSES[tonic]} {mode}",
        samplerate=samplerate,
        length=len(mono) / samplerate,
        music_end=music_end,
        beats=beats,
        sections=truth_sections,
        hits={k: v for k, v in hits.items() if v},
    )
    return frames, truth
