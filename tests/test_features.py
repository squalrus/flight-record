"""Feature extraction on synthetic signals with known answers (tempo, key, band triggers)."""

import numpy as np
import pytest

from flightrec.audio.features import (
    ChromaKey,
    FeatureExtractor,
    OnsetDetector,
    energy01,
    extract,
)

SR = 48_000
RNG = np.random.default_rng(1)


def silence(seconds: float) -> np.ndarray:
    return np.zeros(int(seconds * SR), dtype=np.float32)


def place(signal: np.ndarray, burst: np.ndarray, times: np.ndarray) -> np.ndarray:
    for t in times:
        i = int(t * SR)
        n = min(len(burst), len(signal) - i)
        signal[i : i + n] += burst[:n]
    return signal


def decay(n: int, tau: float) -> np.ndarray:
    return np.exp(-np.arange(n) / (tau * SR))


def kick() -> np.ndarray:
    """60 Hz thump, 150 ms."""
    n = int(0.15 * SR)
    return (0.8 * np.sin(2 * np.pi * 60 * np.arange(n) / SR) * decay(n, 0.04)).astype(np.float32)


def hat() -> np.ndarray:
    """High-passed noise tick, 40 ms."""
    n = int(0.04 * SR)
    noise = np.diff(RNG.standard_normal(n + 1))  # differencing pushes energy up
    return (0.3 * noise * decay(n, 0.008)).astype(np.float32)


def click() -> np.ndarray:
    """Broadband click, 20 ms."""
    n = int(0.02 * SR)
    return (0.6 * RNG.standard_normal(n) * decay(n, 0.004)).astype(np.float32)


def onset_times(frames: np.ndarray, band: str) -> np.ndarray:
    return np.array([f.t for f in extract(frames, SR) if f.onsets[band]])


def near(times: np.ndarray, targets: np.ndarray, tol: float = 0.06) -> int:
    return sum(bool(np.any(np.abs(times - t) < tol)) for t in targets)


# Onsets / triggers


def test_click_train_onsets_found_once_each() -> None:
    times = np.arange(0.5, 6.0, 0.5)
    found = onset_times(place(silence(6.5), click(), times), "all")
    assert near(found, times) == len(times)
    assert len(found) <= len(times) + 1  # no burst of false positives


def test_kicks_trigger_low_band_and_hats_trigger_high_band() -> None:
    kicks = np.arange(0.5, 6.0, 0.5)
    hats = kicks + 0.25
    audio = place(place(silence(6.5), kick(), kicks), hat(), hats)
    low = onset_times(audio, "low")
    high = onset_times(audio, "high")
    assert near(low, kicks) >= len(kicks) - 1
    assert near(high, hats) >= len(hats) - 1
    assert near(low, hats, tol=0.04) <= 1  # hats barely touch the low band
    assert near(high, kicks, tol=0.04) <= 1  # kicks barely touch the high band


def test_adaptive_threshold_survives_a_quiet_to_loud_change() -> None:
    times = np.arange(0.5, 8.0, 0.5)
    audio = place(silence(8.5), click(), times)
    audio[: 4 * SR] *= 0.05  # first half ~26 dB quieter
    found = onset_times(audio, "all")
    assert near(found, times) >= len(times) - 1


def rumble(seconds: float, level_db: float) -> np.ndarray:
    """Steady low-frequency noise (room / HVAC rumble) at roughly ``level_db`` in the low
    band. Measured on a laptop mic in a quiet room: about -58 dBFS median."""
    from scipy.signal import butter, sosfilt

    noise = sosfilt(
        butter(4, 120, "low", fs=SR, output="sos"), RNG.standard_normal(int(seconds * SR))
    )
    rms = np.sqrt(np.mean(noise**2))
    return (noise / rms * 10 ** (level_db / 20)).astype(np.float32)


def test_room_rumble_does_not_trigger() -> None:
    feats = list(extract(rumble(20.0, -58.0), SR))  # measured quiet-room level
    assert sum(f.onsets["low"] for f in feats) <= 1  # ~100 per 20 s without the gate
    assert sum(f.beat for f in feats) == 0


def test_louder_rumble_rarely_triggers() -> None:
    feats = list(extract(rumble(20.0, -55.0), SR))  # 3 dB louder than measured
    assert sum(f.onsets["low"] for f in feats) <= 6


def test_gate_off_shows_why_it_exists() -> None:
    feats = list(extract(rumble(20.0, -58.0), SR, gate_db=None))
    assert sum(f.onsets["low"] for f in feats) > 20


def test_kicks_over_rumble_still_trigger() -> None:
    kicks = np.arange(0.5, 10.0, 0.5)
    audio = place(silence(10.5), kick(), kicks) + rumble(10.5, -55.0)
    low = onset_times(audio, "low")
    assert near(low, kicks) >= len(kicks) - 1
    assert len(low) <= len(kicks) + 2


def test_onset_detector_refractory_and_strength() -> None:
    det = OnsetDetector(hop_rate=100, refractory_s=0.1)
    values = [0.0] * 50 + [1.0, 0.0, 0.0, 0.9, 0.0] + [0.0] * 20
    hits = [det.update(v) for v in values]
    onsets = [i for i, (hit, _) in enumerate(hits) if hit]
    assert onsets == [51]  # one hop late; the second peak 30 ms later is suppressed
    assert hits[51][1] > 0.0


# Tempo


@pytest.mark.parametrize("bpm", [90.0, 120.0, 133.0, 150.0, 175.0])
def test_tempo_converges_on_a_click_train(bpm: float) -> None:
    period = 60.0 / bpm
    audio = place(silence(12.0), click(), np.arange(0.2, 12.0, period))
    feats = list(extract(audio, SR))
    final = feats[-1]
    assert final.tempo == pytest.approx(bpm, rel=0.02)
    assert final.tempo_confidence > 0.5
    # Converges within ~10 s: estimates in the last 2 s are all right.
    late = [f.tempo for f in feats if f.t > 10.0]
    assert all(t is not None and abs(t - bpm) / bpm < 0.03 for t in late)


def test_predicted_beats_line_up_with_clicks() -> None:
    times = np.arange(0.2, 12.0, 0.5)
    feats = list(extract(place(silence(12.0), click(), times), SR))
    beats = np.array([f.t for f in feats if f.beat and f.t > 8.0])
    assert len(beats) >= 6
    assert near(beats, times, tol=0.05) >= len(beats) - 1


def test_beats_lock_to_kicks_not_offbeat_hats() -> None:
    """Full-band onset strength weights bands equally; a per-bin mean let the many
    high-frequency bins make hi-hats dominate, putting the beat on the offbeat."""
    kicks = np.arange(0.2, 12.0, 0.5)
    hats = kicks + 0.25
    audio = place(place(silence(12.0), kick(), kicks), hat(), hats)
    feats = list(extract(audio, SR))
    assert feats[-1].tempo == pytest.approx(120.0, rel=0.02)
    beats = np.array([f.t for f in feats if f.beat and f.t > 6.0])
    assert near(beats, kicks, tol=0.06) >= len(beats) - 1
    assert near(beats, hats, tol=0.06) <= 1


def test_noise_has_low_tempo_confidence() -> None:
    noise = (0.2 * RNG.standard_normal(12 * SR)).astype(np.float32)
    feats = list(extract(noise, SR))
    assert feats[-1].tempo_confidence < 0.3
    assert sum(f.beat for f in feats) <= 2  # no confident-looking pulse from noise


# Chroma and key


def note(midi: int, seconds: float, amp: float = 0.15) -> np.ndarray:
    """A tone with a few decaying harmonics, like a plucked or keyed instrument."""
    f = 440.0 * 2 ** ((midi - 69) / 12)
    t = np.arange(int(seconds * SR)) / SR
    return sum(amp / h * np.sin(2 * np.pi * f * h * t) for h in range(1, 5)).astype(np.float32)


def chord(midis: list[int], seconds: float) -> np.ndarray:
    return sum(note(m, seconds) for m in midis)


def progression(chords: list[list[int]], seconds: float = 1.5, repeats: int = 3) -> np.ndarray:
    return np.concatenate([chord(c, seconds) for _ in range(repeats) for c in chords])


def test_chroma_of_a440_peaks_at_a() -> None:
    final = list(extract(note(69, 3.0), SR))[-1]
    assert int(np.argmax(final.chroma)) == 9  # A


def test_key_c_major_from_i_iv_v_i() -> None:
    c, f, g = [60, 64, 67], [53, 57, 60], [55, 59, 62]
    final = list(extract(progression([c, f, g, c]), SR))[-1]
    assert final.key == "C major"
    assert final.key_confidence > 0.6


def test_key_a_minor_from_i_iv_v_i() -> None:
    am, dm, e = [57, 60, 64], [50, 53, 57], [52, 56, 59]  # E major has G#
    final = list(extract(progression([am, dm, e, am]), SR))[-1]
    assert final.key == "A minor"


def test_key_holds_through_silence() -> None:
    ck = ChromaKey(np.fft.rfftfreq(8192, 1 / SR))
    ck.update(np.zeros(4097), 0.1)
    assert ck.key is None


# Energy and brightness


def test_band_energy_follows_the_band() -> None:
    final = list(extract(note(33, 2.0, amp=0.5), SR))[-1]  # A1 = 55 Hz, low band
    assert final.bass > final.treble + 0.3


def test_centroid_tracks_brightness() -> None:
    dull = list(extract(note(48, 2.0), SR))[-1].centroid_hz  # C3
    bright = list(extract(note(84, 2.0), SR))[-1].centroid_hz  # C6
    assert 100 < dull < 400 < 1000 < bright < 3000


def test_energy01_mapping() -> None:
    assert energy01(-60.0) == 0.0
    assert energy01(-30.0) == pytest.approx(0.5)
    assert energy01(3.0) == 1.0


def test_message_matches_contract_fields() -> None:
    fx = FeatureExtractor(SR)
    msg = fx.process(np.zeros((fx.window, 2), dtype=np.float32), fx.window).message()
    for key in ("v", "type", "t", "onset", "onset_strength", "bass", "mid", "treble", "rms"):
        assert key in msg
    assert msg["type"] == "features"
    assert msg["v"] == 1
