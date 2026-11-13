"""Real-time musical features from consecutive audio hops.

``FeatureExtractor.process`` is fed one analysis window per hop (``hop`` new frames each
time) and returns a ``Features`` snapshot: per-band onsets (triggers), band energies,
loudness, brightness, tempo with confidence and beat phase, and chroma with an estimated
key. It only depends on the audio it is given, so the same recording always gives the same
features: live input, replay, and tests all behave identically.

All of it is numpy only (no librosa), cheap enough to run every hop on a laptop.
"""

from __future__ import annotations

from collections import deque
from collections.abc import Iterator
from dataclasses import dataclass, field

import numpy as np

from flightrec.audio.capture import Capture
from flightrec.audio.ring import OverrunError

BAND_NAMES = ("low", "mid", "high")
DEFAULT_BANDS: dict[str, tuple[float, float]] = {
    "low": (20.0, 150.0),  # kick drum, bass
    "mid": (150.0, 2_000.0),  # snare body, vocals, guitars, keys
    "high": (2_000.0, 16_000.0),  # hi-hats, cymbals, sibilance
}
PITCH_CLASSES = ("C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B")

# Key profiles as (major, minor), index 0 = tonic.
KEY_PROFILES: dict[str, tuple[list[float], list[float]]] = {
    # Albrecht & Shanahan (2013), pitch-class distributions from a pop/rock corpus. Default:
    # on 5 songs with published keys it got 2 exact vs 0 for Krumhansl-Kessler, which put
    # 3 of 5 a fifth too high (see experiments/02-onset-features/README.md).
    "albrecht": (
        [0.238, 0.006, 0.111, 0.006, 0.137, 0.094, 0.016, 0.214, 0.009, 0.080, 0.008, 0.081],
        [0.220, 0.006, 0.104, 0.123, 0.019, 0.103, 0.012, 0.214, 0.062, 0.022, 0.061, 0.052],
    ),
    # Krumhansl & Kessler (1982), probe-tone ratings.
    "krumhansl": (
        [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88],
        [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17],
    ),
}

DEFAULT_GATE_DB = -45.0
"""Bands quieter than this never trigger. Chosen from a laptop mic in a quiet room, where
the low band's noise peaked at -46 dBFS while music through speakers sat around -30.
Line-level input from the real interface is louder, so this mostly matters for mics."""

ENERGY_FLOOR_DB = -60.0
"""Energies map linearly from this dBFS (0.0) to 0 dBFS (1.0)."""


def energy01(db: float) -> float:
    """Map a dBFS level onto 0..1 for display and the message contract."""
    return min(1.0, max(0.0, (db - ENERGY_FLOOR_DB) / -ENERGY_FLOOR_DB))


def _db(amplitude: float) -> float:
    return 20.0 * float(np.log10(max(amplitude, 1e-12)))


@dataclass
class Features:
    """Snapshot after one hop. ``t`` is audio time in seconds at the end of the window."""

    t: float
    onsets: dict[str, bool]
    """Trigger per band in ``BAND_NAMES`` plus ``"all"`` (full band)."""
    strengths: dict[str, float]
    """0..1 onset strength per band, relative to that band's recent peaks."""
    bass: float
    mid: float
    treble: float
    rms: float
    """0..1, see ``energy01``."""
    rms_db: float
    centroid_hz: float
    """Spectral centroid: higher means brighter. 0 for silence."""
    tempo: float | None
    tempo_confidence: float
    beat: bool
    """A predicted beat (from tempo and phase) falls in this hop."""
    chroma: np.ndarray = field(repr=False)
    """12 pitch-class weights C..B, max 1 (smoothed over a few seconds)."""
    key: str | None
    key_confidence: float

    @property
    def onset(self) -> bool:
        return self.onsets["all"]

    def message(self) -> dict[str, object]:
        """The architecture doc's ``features`` message, plus extra fields (allowed:
        consumers ignore unknown fields)."""
        return {
            "v": 1,
            "type": "features",
            "t": round(self.t, 4),
            "onset": self.onsets["all"],
            "onset_strength": round(self.strengths["all"], 3),
            "bass": round(self.bass, 3),
            "mid": round(self.mid, 3),
            "treble": round(self.treble, 3),
            "rms": round(self.rms, 3),
            "tempo": None if self.tempo is None else round(self.tempo, 1),
            "tempo_confidence": round(self.tempo_confidence, 3),
            "onset_low": self.onsets["low"],
            "onset_mid": self.onsets["mid"],
            "onset_high": self.onsets["high"],
            "beat": self.beat,
            "centroid_hz": round(self.centroid_hz, 1),
            "key": self.key,
            "key_confidence": round(self.key_confidence, 3),
            "chroma": [round(float(c), 3) for c in self.chroma],
        }


class OnsetDetector:
    """Adaptive-threshold peak picking on an onset-strength signal (one value per hop).

    A hop is an onset when its value is a local maximum, exceeds ``median + k * MAD`` of the
    last ``window_s`` seconds (so the threshold follows quiet and loud passages), reaches
    ``min_relative`` of the largest value in that window (so faint leakage next to real hits,
    e.g. a hi-hat's low-frequency smear, does not trigger), clears ``min_value``, and is at
    least ``refractory_s`` after the previous onset. Peaks need one hop of look-ahead, so
    each detection refers to the *previous* hop.

    ``gate_db`` (optional) is an absolute level gate: a band below it never triggers. The
    adaptive threshold alone also adapts to steady room noise, whose level in a narrow band
    (few FFT bins) jumps by +/-10 dB hop to hop and would otherwise read as hits.
    """

    def __init__(
        self,
        hop_rate: float,
        window_s: float = 1.0,
        k: float = 2.5,
        refractory_s: float = 0.08,
        min_value: float = 0.02,
        min_relative: float = 0.3,
        gate_db: float | None = None,
    ) -> None:
        self.history: deque[float] = deque(maxlen=max(3, round(window_s * hop_rate)))
        self.k = k
        self.refractory = max(1, round(refractory_s * hop_rate))
        self.min_value = min_value
        self.min_relative = min_relative
        self.gate_db = gate_db
        self._prev = 0.0
        self._prev2 = 0.0
        self._prev_level = -200.0
        self._since = self.refractory

    def update(self, value: float, level_db: float = 0.0) -> tuple[bool, float]:
        """Add this hop's value and the band's level (dBFS); return (onset at previous hop,
        its 0..1 strength). Nothing triggers while the level is below ``gate_db``."""
        hist = np.fromiter(self.history, dtype=float) if self.history else np.zeros(1)
        median = float(np.median(hist))
        mad = float(np.median(np.abs(hist - median)))
        peak = float(hist.max())
        candidate = self._prev
        self._since += 1
        # A hit's level peaks a hop or so after its onset-strength peak, so gate on both.
        loud_enough = self.gate_db is None or max(self._prev_level, level_db) >= self.gate_db
        onset = (
            loud_enough
            and candidate > self._prev2
            and candidate >= value
            and candidate > median + self.k * mad
            and candidate > self.min_value
            and candidate >= self.min_relative * peak
            and self._since > self.refractory
        )
        if onset:
            self._since = 0
        strength = min(1.0, candidate / peak) if peak > 0 else 0.0
        self.history.append(value)
        self._prev2, self._prev = self._prev, value
        self._prev_level = level_db
        return onset, strength if onset else 0.0


class TempoEstimator:
    """Tempo from the autocorrelation of the onset-strength envelope.

    Every ``update_s`` it autocorrelates the last ``history_s`` seconds, weights lags toward
    ~120 BPM (one-octave log-Gaussian, to pick between half and double tempo), and refines
    the best lag. Confidence is the normalized autocorrelation at that lag (0..1). It also
    finds the beat phase, so ``update`` can flag predicted beats.
    """

    def __init__(
        self,
        hop_rate: float,
        history_s: float = 8.0,
        min_bpm: float = 60.0,
        max_bpm: float = 200.0,
        update_s: float = 0.5,
        min_history_s: float = 4.0,
        beat_min_confidence: float = 0.3,
    ) -> None:
        self.hop_rate = hop_rate
        self.beat_min_confidence = beat_min_confidence
        self.env: deque[float] = deque(maxlen=round(history_s * hop_rate))
        self.min_lag = max(1, int(np.floor(60.0 * hop_rate / max_bpm)))
        self.max_lag = int(np.ceil(60.0 * hop_rate / min_bpm))
        self.update_every = max(1, round(update_s * hop_rate))
        self.min_len = round(min_history_s * hop_rate)
        self.bpm: float | None = None
        self.confidence = 0.0
        self._hop = 0
        self._next_beat: float | None = None

    def update(self, value: float) -> bool:
        """Add this hop's onset strength; return True if a predicted beat falls in it.

        Beats are only reported while confidence is at least ``beat_min_confidence``, so
        noise or rubato does not produce a confident-looking pulse.
        """
        self.env.append(value)
        self._hop += 1
        if self._hop % self.update_every == 0 and len(self.env) >= self.min_len:
            self._estimate()
        latest = self._hop - 1  # index of the hop just added
        if self._next_beat is not None and latest >= self._next_beat:
            period = 60.0 * self.hop_rate / self.bpm if self.bpm else 0.0
            while period > 0 and self._next_beat <= latest:
                self._next_beat += period
            return self.confidence >= self.beat_min_confidence
        return False

    def _estimate(self) -> None:
        # Smooth over a few hops first: a hit that straddles a hop boundary splits its
        # energy across two hops, and when the beat period is not a whole number of hops
        # that alternates beat to beat, which makes half tempo look as strong as the truth.
        x = np.convolve(np.asarray(self.env, dtype=float), [1, 2, 3, 2, 1], mode="same") / 9
        x = x - x.mean()
        n = len(x)
        spectrum = np.fft.rfft(x, 2 * n)
        ac = np.fft.irfft(spectrum * np.conj(spectrum))[:n]
        if ac[0] <= 1e-12:
            self.bpm, self.confidence, self._next_beat = None, 0.0, None
            return
        ac = ac / ac[0]
        hi = min(self.max_lag, n - 2)
        lags = np.arange(self.min_lag, hi + 1)
        bpms = 60.0 * self.hop_rate / lags
        weight = np.exp(-0.5 * np.log2(bpms / 120.0) ** 2)
        best = int(lags[np.argmax(ac[lags] * weight)])
        # Parabolic refinement of the peak position.
        a, b, c = ac[best - 1], ac[best], ac[best + 1]
        denom = a - 2 * b + c
        shift = 0.5 * (a - c) / denom if denom < 0 else 0.0
        lag = best + float(np.clip(shift, -0.5, 0.5))
        self.bpm = 60.0 * self.hop_rate / lag
        self.confidence = float(np.clip(b, 0.0, 1.0))
        self._next_beat = self._phase(x, lag)

    def _phase(self, x: np.ndarray, period: float) -> float:
        """Absolute hop index of the next beat, from a comb over the recent envelope."""
        n = len(x)
        beats = max(1, int((n - 1) // period))
        offsets = np.arange(int(np.ceil(period)))
        scores = np.zeros(len(offsets))
        for k in range(beats):
            idx = np.round(n - 1 - offsets - k * period).astype(int)
            valid = idx >= 0
            scores[valid] += x[idx[valid]]
        last_beat = self._hop - 1 - int(offsets[np.argmax(scores)])
        return last_beat + period


class ChromaKey:
    """12-bin chroma from a magnitude spectrum, smoothed, and an estimated key.

    Bins between ``fmin`` and ``fmax`` are folded onto pitch classes (equal temperament,
    A4 = 440 Hz). Chroma is averaged with time constant ``time_constant_s``; the key is the
    major or minor profile (``KEY_PROFILES[profile]``) that best correlates with it, and
    confidence is that correlation.
    """

    def __init__(
        self,
        freqs: np.ndarray,
        fmin: float = 100.0,
        fmax: float = 4_200.0,
        time_constant_s: float = 8.0,
        min_level_db: float = -60.0,
        profile: str = "albrecht",
    ) -> None:
        use = (freqs >= fmin) & (freqs <= fmax)
        self._bins = np.flatnonzero(use)
        midi = 69.0 + 12.0 * np.log2(freqs[use] / 440.0)
        self._pc = np.round(midi).astype(int) % 12
        self.time_constant = time_constant_s
        self.min_level = 10.0 ** (min_level_db / 20.0)
        self.smoothed = np.zeros(12)
        self.chroma = np.zeros(12)
        self.key: str | None = None
        self.confidence = 0.0
        profiles = []
        names = []
        major, minor = KEY_PROFILES[profile]
        for mode, weights in (("major", major), ("minor", minor)):
            for tonic in range(12):
                p = np.roll(np.asarray(weights, dtype=float), tonic)
                profiles.append((p - p.mean()) / p.std())
                names.append(f"{PITCH_CLASSES[tonic]} {mode}")
        self._profiles = np.array(profiles)
        self._names = names

    def update(self, magnitude: np.ndarray, dt: float) -> None:
        """Fold ``magnitude`` (linear, per bin) into chroma and re-estimate the key."""
        mag = magnitude[self._bins]
        if mag.max(initial=0.0) < self.min_level:
            return  # silence: keep the last estimate
        chroma = np.sqrt(np.bincount(self._pc, weights=mag**2, minlength=12))
        alpha = min(1.0, dt / self.time_constant)
        self.smoothed += alpha * (chroma - self.smoothed)
        top = self.smoothed.max()
        self.chroma = self.smoothed / top if top > 0 else self.smoothed
        if self.smoothed.std() <= 1e-12:
            return
        z = (self.smoothed - self.smoothed.mean()) / self.smoothed.std()
        scores = self._profiles @ z / 12.0
        best = int(np.argmax(scores))
        self.key = self._names[best]
        self.confidence = float(np.clip(scores[best], 0.0, 1.0))


class FeatureExtractor:
    """Compute ``Features`` hop by hop.

    Call ``process`` with the latest ``window`` frames (shape ``(window, channels)``) each
    time ``hop`` new frames have arrived. Onsets, energies, and brightness use the last
    ``n_fft`` frames; chroma uses the whole window (``chroma_fft``, finer pitch resolution)
    every ``chroma_every`` hops.
    """

    def __init__(
        self,
        samplerate: int,
        hop: int = 512,
        n_fft: int = 2048,
        chroma_fft: int = 8192,
        chroma_every: int = 4,
        bands: dict[str, tuple[float, float]] | None = None,
        gate_db: float | None = DEFAULT_GATE_DB,
    ) -> None:
        self.samplerate = samplerate
        self.hop = hop
        self.n_fft = n_fft
        self.chroma_fft = chroma_fft
        self.window = max(n_fft, chroma_fft)
        self.chroma_every = chroma_every
        self.hop_rate = samplerate / hop
        self.freqs = np.fft.rfftfreq(n_fft, 1.0 / samplerate)
        self._win = np.hanning(n_fft)
        self._scale = 2.0 / self._win.sum()
        self._cwin = np.hanning(chroma_fft)
        self._cscale = 2.0 / self._cwin.sum()
        bands = bands or DEFAULT_BANDS
        self._band_bins = {
            name: np.flatnonzero((self.freqs >= lo) & (self.freqs < hi))
            for name, (lo, hi) in bands.items()
        }
        self._all_bins = np.flatnonzero((self.freqs >= 20.0) & (self.freqs <= 16_000.0))
        self._prev_log: np.ndarray | None = None
        self.detectors = {name: OnsetDetector(self.hop_rate) for name in (*BAND_NAMES, "all")}
        self.gate_db = gate_db
        self.tempo = TempoEstimator(self.hop_rate)
        self.chroma = ChromaKey(np.fft.rfftfreq(chroma_fft, 1.0 / samplerate))
        self.hops = 0
        self.last_magnitude = np.zeros(len(self.freqs))

    @property
    def gate_db(self) -> float | None:
        """Trigger level gate in dBFS for every band (None = off); adjustable live."""
        return self._gate_db

    @gate_db.setter
    def gate_db(self, value: float | None) -> None:
        self._gate_db = value
        for detector in self.detectors.values():
            detector.gate_db = value

    def process(self, window: np.ndarray, end_frame: int) -> Features:
        """Analyze the window ending at audio frame ``end_frame``."""
        mono = window.mean(axis=1) if window.ndim == 2 else window
        frames = mono[-self.n_fft :]
        mag = np.abs(np.fft.rfft(frames * self._win)) * self._scale
        self.last_magnitude = mag  # bins at self.freqs; for spectrogram displays

        # Onset strength: positive change of log-compressed magnitude, averaged per band.
        log = np.log1p(1000.0 * mag)
        if self._prev_log is None:
            self._prev_log = log
        rise = np.maximum(0.0, log - self._prev_log)
        self._prev_log = log
        power = mag**2
        band_db = {
            name: _db(float(np.sqrt(power[bins].sum()))) for name, bins in self._band_bins.items()
        }
        onsets: dict[str, bool] = {}
        strengths: dict[str, float] = {}
        band_values = []
        for name in BAND_NAMES:
            bins = self._band_bins[name]
            value = float(rise[bins].mean()) if len(bins) else 0.0
            band_values.append(value)
            onsets[name], strengths[name] = self.detectors[name].update(value, band_db[name])
        # Full band = equal weight per band. A plain mean over all bins is dominated by the
        # many high-frequency bins, so hi-hats would outweigh kicks and tempo/beat phase
        # would lock onto the offbeat.
        full = float(np.mean(band_values))
        loudest = max(band_db[name] for name in BAND_NAMES)
        onsets["all"], strengths["all"] = self.detectors["all"].update(full, loudest)
        beat = self.tempo.update(full)

        levels = {name: energy01(db) for name, db in band_db.items()}
        total = float(power[self._all_bins].sum())
        centroid = (
            float((self.freqs[self._all_bins] * power[self._all_bins]).sum() / total)
            if total > 1e-14
            else 0.0
        )
        rms_db = _db(float(np.sqrt(np.mean(frames**2))))

        if self.hops % self.chroma_every == 0:
            cframes = mono[-self.chroma_fft :]
            if len(cframes) == self.chroma_fft:
                cmag = np.abs(np.fft.rfft(cframes * self._cwin)) * self._cscale
                self.chroma.update(cmag, self.chroma_every / self.hop_rate)
        self.hops += 1

        return Features(
            t=end_frame / self.samplerate,
            onsets=onsets,
            strengths=strengths,
            bass=levels.get("low", 0.0),
            mid=levels.get("mid", 0.0),
            treble=levels.get("high", 0.0),
            rms=energy01(rms_db),
            rms_db=rms_db,
            centroid_hz=centroid,
            tempo=self.tempo.bpm,
            tempo_confidence=self.tempo.confidence,
            beat=beat,
            chroma=self.chroma.chroma.copy(),
            key=self.chroma.key,
            key_confidence=self.chroma.confidence,
        )


def extract(frames: np.ndarray, samplerate: int, **kwargs: object) -> Iterator[Features]:
    """Run a ``FeatureExtractor`` over a whole array, hop by hop (offline and tests).

    The first windows are zero-padded at the front, as in live capture.
    """
    if frames.ndim == 1:
        frames = frames[:, np.newaxis]
    fx = FeatureExtractor(samplerate, **kwargs)  # type: ignore[arg-type]
    padded = np.concatenate([np.zeros((fx.window, frames.shape[1]), frames.dtype), frames])
    for end in range(fx.hop, len(frames) + 1, fx.hop):
        yield fx.process(padded[end : end + fx.window], end)


class HopReader:
    """Pull consecutive analysis windows from a ``Capture``, one per ``hop`` new frames.

    Call ``pending()`` from the analysis loop; it yields ``(window, end_frame)`` for every
    hop that has arrived since the last call. If analysis falls so far behind that frames
    were overwritten, it skips ahead to the newest hop and counts ``skipped_hops``.
    """

    def __init__(self, capture: Capture, window: int, hop: int) -> None:
        if capture.ring.capacity < window + 4 * hop:
            raise ValueError("capture ring is too small for this window")
        self.capture = capture
        self.window = window
        self.hop = hop
        self.end = 0
        self.skipped_hops = 0
        self._out = np.zeros((window, capture.channels), dtype=np.float32)

    def pending(self) -> Iterator[tuple[np.ndarray, int]]:
        while self.capture.frames >= self.end + self.hop:
            self.end += self.hop
            start = self.end - self.window
            try:
                if start >= 0:
                    self.capture.ring.read_from(start, self.window, self._out)
                else:
                    self._out[:-start] = 0.0
                    self._out[-start:] = self.capture.ring.read_from(0, self.end)
            except OverrunError:
                newest = (self.capture.frames // self.hop) * self.hop
                self.skipped_hops += (newest - self.end) // self.hop + 1
                self.end = newest - self.hop
                continue
            yield self._out, self.end
