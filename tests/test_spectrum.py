"""Spectrum, band, and level analysis on synthetic signals, plus Capture timing stats."""

import numpy as np
import pytest

from flightrec.audio import (
    FLOOR_DB,
    ArraySource,
    Capture,
    LogBands,
    Spectrum,
    channel_levels,
    level_dbfs,
    to_db,
)

SR = 48_000
N_FFT = 4096


def sine(freq: float, amplitude: float = 1.0, n: int = N_FFT, channels: int = 2) -> np.ndarray:
    t = np.arange(n) / SR
    mono = (amplitude * np.sin(2 * np.pi * freq * t)).astype(np.float32)
    return np.repeat(mono[:, np.newaxis], channels, axis=1)


def bin_freq(k: int) -> float:
    return k * SR / N_FFT


def test_full_scale_sine_reads_zero_dbfs_at_its_bin() -> None:
    spectrum = Spectrum(N_FFT, SR)
    db = spectrum.compute(sine(bin_freq(85)))  # ~996 Hz, centered on bin 85
    assert int(np.argmax(db)) == 85
    assert db[85] == pytest.approx(0.0, abs=0.05)


def test_half_amplitude_is_minus_six_db() -> None:
    db = Spectrum(N_FFT, SR).compute(sine(bin_freq(200), amplitude=0.5))
    assert db[200] == pytest.approx(-6.02, abs=0.05)


def test_silence_is_floor() -> None:
    db = Spectrum(N_FFT, SR).compute(np.zeros((N_FFT, 2), dtype=np.float32))
    assert np.all(db == FLOOR_DB)


def test_spectrum_rejects_wrong_length_and_bad_size() -> None:
    with pytest.raises(ValueError):
        Spectrum(N_FFT, SR).compute(np.zeros((100, 2), dtype=np.float32))
    with pytest.raises(ValueError):
        Spectrum(1000, SR)


def test_log_bands_put_tone_in_the_right_band() -> None:
    spectrum = Spectrum(N_FFT, SR)
    bands = LogBands(spectrum.freqs, 64, fmin=20.0, fmax=20_000.0)
    levels = bands.compute(spectrum.compute(sine(bin_freq(85))))
    loudest = int(np.argmax(levels))
    assert bands.edges[loudest] <= bin_freq(85) <= bands.edges[loudest + 1]
    assert levels.shape == (64,)
    assert np.all(np.isfinite(levels))
    assert np.allclose(np.diff(np.log(bands.edges)), np.log(bands.edges[1] / bands.edges[0]))


def test_low_bands_narrower_than_a_bin_still_get_values() -> None:
    spectrum = Spectrum(1024, SR)  # 47 Hz bins: many 20-100 Hz bands contain no bin
    bands = LogBands(spectrum.freqs, 120)
    levels = bands.compute(spectrum.compute(sine(60.0, n=1024)))
    assert np.all(np.isfinite(levels))
    assert levels[0] > -40.0  # 60 Hz energy reaches the lowest bands via nearest bin


def test_levels_of_a_full_scale_sine() -> None:
    frames = sine(bin_freq(85), n=SR)
    rms, peak = level_dbfs(frames)
    assert rms == pytest.approx(-3.01, abs=0.02)
    assert peak == pytest.approx(0.0, abs=0.01)
    ch_rms, ch_peak = channel_levels(frames * np.array([1.0, 0.5], dtype=np.float32))
    assert ch_rms == pytest.approx([-3.01, -9.03], abs=0.02)
    assert ch_peak == pytest.approx([0.0, -6.02], abs=0.02)


def test_to_db_clamps_silence() -> None:
    assert to_db(0.0) == FLOOR_DB
    assert to_db(1.0) == 0.0


def test_capture_tracks_block_gaps_and_lag() -> None:
    samplerate, blocksize = 8_000, 400  # 50 ms blocks
    capture = Capture(ArraySource(np.zeros((4 * blocksize, 1)), samplerate, blocksize), 1.0)
    assert capture.block_period == pytest.approx(0.05)
    with capture:
        assert capture.wait(5.0)
    gap = capture.pop_max_gap()
    assert 0.045 < gap < 0.5
    assert capture.pop_max_gap() == 0.0
    assert capture.max_lag >= 0.0


def test_capture_timing_with_a_late_first_block() -> None:
    """Deterministic arrival times: the first block is 40 ms late, the rest on schedule,
    then one block 30 ms late. CI schedulers can do exactly this."""
    source = ArraySource(np.zeros((400, 1)), 8_000, 400)  # never started; 50 ms blocks
    capture = Capture(source, 1.0)
    block = np.zeros((400, 1), dtype=np.float32)
    arrivals = [0.090, 0.100, 0.150, 0.200, 0.280, 0.300]  # due at 0.05, 0.10, 0.15, ...
    for k, t in enumerate(arrivals):
        capture._on_block(block, k * 400, t)

    assert capture.lag == pytest.approx(-0.040)  # last block on time; first was 40 ms late
    assert capture.max_lag == pytest.approx(0.040)  # spread: 40 ms late vs on time
    assert capture.pop_max_gap() == pytest.approx(0.080)  # 0.200 -> 0.280
    assert capture.blocks == 6
