"""Capture path tests: ring buffer, WAV I/O, and replay/null sources. No hardware needed."""

import subprocess
import sys
import time
from pathlib import Path

import numpy as np
import pytest

from flightrec.audio import (
    ArraySource,
    Capture,
    NullSource,
    OverrunError,
    RingBuffer,
    WavSource,
    open_source,
    read_wav,
    write_wav,
)

REPO_ROOT = Path(__file__).resolve().parent.parent


def ramp(n: int, channels: int = 2) -> np.ndarray:
    """Distinct value per frame and channel, so ordering bugs show up."""
    base = np.arange(n, dtype=np.float32)[:, np.newaxis]
    return base + np.arange(channels, dtype=np.float32) * 0.5


def collect(source: ArraySource) -> tuple[np.ndarray, list[int]]:
    blocks: list[np.ndarray] = []
    starts: list[int] = []

    def on_block(block: np.ndarray, start: int, t: float) -> None:
        blocks.append(block.copy())
        starts.append(start)

    source.start(on_block)
    assert source.wait(5.0)
    return np.concatenate(blocks), starts


# Ring buffer


def test_ring_latest_before_full_is_zero_padded() -> None:
    ring = RingBuffer(8, 2)
    ring.write(ramp(3))
    frames, end = ring.read_latest(5)
    assert end == 3
    np.testing.assert_array_equal(frames[:2], 0.0)
    np.testing.assert_array_equal(frames[2:], ramp(3))


def test_ring_wraps_and_keeps_order() -> None:
    ring = RingBuffer(8, 2)
    data = ramp(21)
    for i in range(0, 21, 3):
        ring.write(data[i : i + 3])
    frames, end = ring.read_latest(8)
    assert end == 21
    np.testing.assert_array_equal(frames, data[13:21])
    np.testing.assert_array_equal(ring.read_from(15, 4), data[15:19])


def test_ring_write_larger_than_capacity_keeps_tail() -> None:
    ring = RingBuffer(4, 2)
    ring.write(ramp(2))
    data = ramp(10)
    ring.write(data)
    assert ring.frames_written == 12
    np.testing.assert_array_equal(ring.read_latest(4)[0], data[6:])
    np.testing.assert_array_equal(ring.read_from(8, 4), data[6:])


def test_ring_read_from_errors() -> None:
    ring = RingBuffer(4, 1)
    ring.write(ramp(6, 1))
    with pytest.raises(OverrunError):
        ring.read_from(1, 2)
    with pytest.raises(ValueError):
        ring.read_from(4, 3)
    with pytest.raises(ValueError):
        ring.write(ramp(2, 2))


def test_ring_read_into_out_buffer() -> None:
    ring = RingBuffer(4, 2)
    ring.write(ramp(4))
    out = np.empty((4, 2), dtype=np.float32)
    frames, _ = ring.read_latest(4, out)
    assert frames is out


# WAV I/O


def test_wav_int16_round_trip(tmp_path: Path) -> None:
    t = np.arange(4800) / 48_000
    tone = 0.5 * np.sin(2 * np.pi * 440 * t)
    stereo = np.stack([tone, -tone], axis=1)
    write_wav(tmp_path / "tone.wav", 48_000, stereo)
    samplerate, frames = read_wav(tmp_path / "tone.wav")
    assert samplerate == 48_000
    assert frames.shape == (4800, 2)
    assert frames.dtype == np.float32
    np.testing.assert_allclose(frames, stereo, atol=1 / 32767)


def test_wav_mono_reads_as_2d(tmp_path: Path) -> None:
    write_wav(tmp_path / "mono.wav", 44_100, np.zeros(100), "float32")
    _, frames = read_wav(tmp_path / "mono.wav")
    assert frames.shape == (100, 1)


# Sources


def test_array_source_delivers_every_frame_in_full_blocks() -> None:
    data = ramp(1000)
    source = ArraySource(data, 48_000, blocksize=256, realtime=False)
    out, starts = collect(source)
    assert starts == [0, 256, 512, 768]
    assert out.shape == (1024, 2)
    np.testing.assert_array_equal(out[:1000], data)
    np.testing.assert_array_equal(out[1000:], 0.0)  # last block zero-padded


def test_array_source_loop_wraps() -> None:
    data = ramp(300)
    source = ArraySource(data, 48_000, blocksize=256, realtime=False, loop=True)
    seen: list[np.ndarray] = []

    def on_block(block: np.ndarray, start: int, t: float) -> None:
        seen.append(block.copy())
        if len(seen) == 3:
            source.stop()

    source.start(on_block)
    source.wait(5.0)
    out = np.concatenate(seen)[:768]
    np.testing.assert_array_equal(out, data[np.arange(768) % 300])


def test_wav_source_mono_to_stereo(tmp_path: Path) -> None:
    mono = np.linspace(-0.5, 0.5, 512)
    write_wav(tmp_path / "mono.wav", 48_000, mono, "float32")
    source = WavSource(tmp_path / "mono.wav", blocksize=512, channels=2, realtime=False)
    out, _ = collect(source)
    assert out.shape == (512, 2)
    np.testing.assert_array_equal(out[:, 0], out[:, 1])


def test_realtime_replay_is_paced_like_an_interface() -> None:
    samplerate, blocksize = 8_000, 400  # 50 ms blocks
    source = ArraySource(np.zeros((4 * blocksize, 1)), samplerate, blocksize, realtime=True)
    times: list[float] = []
    started = time.monotonic()
    source.start(lambda block, start, t: times.append(t - started))
    assert source.wait(5.0)
    # Block k is due at (k + 1) * 50 ms; it must never arrive early.
    for k, t in enumerate(times):
        assert t >= (k + 1) * 0.05 - 0.005


def test_callback_error_surfaces_from_wait() -> None:
    source = ArraySource(np.zeros((512, 1)), 48_000, 256, realtime=False)

    def boom(block: np.ndarray, start: int, t: float) -> None:
        raise RuntimeError("boom")

    source.start(boom)
    with pytest.raises(RuntimeError, match="boom"):
        source.wait(5.0)


def test_null_source_is_silent_and_endless() -> None:
    capture = Capture(NullSource(blocksize=256, realtime=False), seconds=0.1)
    with capture:
        deadline = time.monotonic() + 5.0
        while capture.frames < 48_000 and time.monotonic() < deadline:
            time.sleep(0.01)
    assert capture.frames >= 48_000
    np.testing.assert_array_equal(capture.latest(1024)[0], 0.0)


# Capture path


def test_capture_from_wav_matches_file_tail(tmp_path: Path) -> None:
    data = np.random.default_rng(0).uniform(-0.9, 0.9, (5000, 2)).astype(np.float32)
    write_wav(tmp_path / "noise.wav", 48_000, data, "float32")
    capture = Capture(open_source(tmp_path / "noise.wav", blocksize=512, realtime=False), 0.05)
    with capture:
        assert capture.wait(5.0)
    frames, end = capture.latest(2000)
    assert end == 5120  # 10 full blocks, last one padded
    np.testing.assert_array_equal(frames[:-120], data[3120:])
    assert capture.blocks == 10


def test_audio_package_does_not_import_sounddevice() -> None:
    code = "import sys, flightrec.audio; sys.exit('sounddevice' in sys.modules)"
    assert subprocess.run([sys.executable, "-c", code], check=False).returncode == 0


def test_replay_cli_runs(tmp_path: Path) -> None:
    write_wav(tmp_path / "clip.wav", 48_000, np.full((9600, 2), 0.25))
    result = subprocess.run(
        [
            sys.executable,
            str(REPO_ROOT / "tools" / "replay.py"),
            str(tmp_path / "clip.wav"),
            "--fast",
        ],
        capture_output=True,
        text=True,
        check=True,
    )
    assert "done: 19 blocks" in result.stdout
