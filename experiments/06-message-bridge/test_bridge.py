"""Experiment 06: the UDP sender, the analysis maths, and send.py against a stand-in game."""

from __future__ import annotations

import csv
import importlib.util
import json
import socket
import subprocess
import sys
import threading
import time
from pathlib import Path

import numpy as np
import pytest

from flightrec.audio import write_wav
from flightrec.bridge import UdpSender
from flightrec.messages import FeaturesMessage, loads

HERE = Path(__file__).resolve().parent


def load(name: str):
    spec = importlib.util.spec_from_file_location(f"exp06_{name}", HERE / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module  # dataclasses look the module up while building
    spec.loader.exec_module(module)
    return module


analyze = load("analyze")


def receiver() -> socket.socket:
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(("127.0.0.1", 0))
    sock.settimeout(2.0)
    return sock


def features(t: float, onset: bool = False) -> FeaturesMessage:
    return FeaturesMessage(
        t=t, onset=onset, onset_strength=0.5, bass=0.1, mid=0.2, treble=0.3, rms=0.4
    )


# The sender


def test_sends_contract_json_and_reads_replies():
    game = receiver()
    with UdpSender("127.0.0.1", game.getsockname()[1]) as sender:
        message = features(1.25, onset=True)
        assert sender.send(message) is not None
        data, address = game.recvfrom(65_536)
        assert loads(data.decode()) == message
        assert address[1] == sender.local_port

        game.sendto(b'{"clock": 12.5}', address)
        game.sendto(b"not json", address)
        game.sendto(b"[1, 2]", address)
        deadline = time.perf_counter() + 2.0
        replies: list = []
        while not replies and time.perf_counter() < deadline:
            replies = sender.replies()
            time.sleep(0.01)
        assert [r for _, r in replies] == [{"clock": 12.5}]
        assert sender.sent == 1 and sender.errors == 0
    game.close()


def test_sending_with_nobody_listening_never_raises():
    game = receiver()
    port = game.getsockname()[1]
    game.close()  # nothing on the port now
    with UdpSender("127.0.0.1", port) as sender:
        for i in range(5):
            sender.send(features(i * 0.01))
        time.sleep(0.05)
        assert sender.replies() == []
        assert sender.sent + sender.errors == 5


# Analysis maths


def test_percentile_is_nearest_rank():
    values = list(range(1, 101))
    assert analyze.percentile(values, 50) == 50
    assert analyze.percentile(values, 99) == 99
    assert analyze.percentile(values, 100) == 100
    assert analyze.percentile([7.0], 95) == 7.0


def test_clock_offset_is_the_median_difference():
    clocks = [(10.0 + d, 0.0) for d in (0.0010, 0.0012, 0.0011, 0.0500)]  # one outlier
    offset, spread = analyze.clock_offset(clocks)
    assert offset == pytest.approx(10.00115)
    assert spread < 0.05
    with pytest.raises(ValueError):
        analyze.clock_offset([])


def test_jitter_and_late_fraction():
    received = [
        analyze.Received("features", t, t + 0.003 + extra, None, 0.0)
        for t, extra in ((0.0, 0.0), (0.01, 0.002), (0.02, 0.030), (0.03, 0.0))
    ]
    jit = analyze.jitter(received)
    assert jit == pytest.approx([0.0, 0.002, 0.030, 0.0])
    late = analyze.late_fraction(jit, (0, 5, 50))
    assert late == {0: 0.5, 5: 0.25, 50: 0.0}


def test_pair_nearest_matches_within_the_window():
    probe = [1.0, 2.0, 5.0]
    bridge = [1.05, 1.9, 3.0]
    assert analyze.pair_nearest(probe, bridge, window=0.2) == pytest.approx([0.05, -0.1])


def test_stage_rows_join_by_type_and_time():
    sent = [
        analyze.Sent("features", 1.0, True, event=100.0, sent=100.004),
        analyze.Sent("features", 1.0107, False, event=100.0107, sent=100.0150),
    ]
    offset = 50.0  # game clock = sender clock + 50 s
    received = [
        analyze.Received("features", 1.0, 150.005, 150.200, 150.0),
        analyze.Received("features", 1.0107, 150.016, None, 150.0),
        analyze.Received("side_state", 9.0, 151.0, None, 150.0),  # never sent: ignored
    ]
    stages = analyze.stage_rows(sent, received, offset)
    assert stages["onsets"]["analysis"] == pytest.approx([0.004])
    assert stages["onsets"]["transport"] == pytest.approx([0.001])
    assert stages["onsets"]["hold"] == pytest.approx([0.195])
    assert stages["onsets"]["end to end"] == pytest.approx([0.200])
    assert len(stages["all"]["transport"]) == 2
    assert len(stages["all"]["hold"]) == 1


# send.py end to end, with a stand-in for the Unity scene


def click_track(path: Path, seconds: float, sr: int = 48_000) -> None:
    audio = np.zeros(int(seconds * sr), dtype=np.float32)
    n = int(0.02 * sr)
    burst = 0.6 * np.random.default_rng(3).standard_normal(n) * np.exp(-np.arange(n) / 192)
    for t in np.arange(0.2, seconds, 0.5):
        i = int(t * sr)
        audio[i : i + n] += burst[: len(audio) - i]
    write_wav(path, sr, np.stack([audio, audio], axis=1))


class FakeGame(threading.Thread):
    """Receives like the scene: logs each message and replies with clock samples."""

    def __init__(self, csv_path: Path) -> None:
        super().__init__(daemon=True)
        self.sock = receiver()
        self.sock.settimeout(0.05)
        self.csv_path = csv_path
        self.stop = threading.Event()
        self.types: list[str] = []

    @property
    def port(self) -> int:
        return self.sock.getsockname()[1]

    def run(self) -> None:
        with self.csv_path.open("w", newline="", encoding="utf-8") as fh:
            out = csv.writer(fh)
            out.writerow(["kind", "type", "t", "recv", "shown", "delay_ms"])
            while not self.stop.is_set():
                try:
                    data, address = self.sock.recvfrom(65_536)
                except TimeoutError:
                    continue
                recv = time.perf_counter()
                m = json.loads(data)
                self.types.append(m["type"])
                out.writerow(["msg", m["type"], m["t"], recv, recv + 0.001, 0])
                self.sock.sendto(json.dumps({"clock": time.perf_counter()}).encode(), address)
        self.sock.close()


def test_send_file_to_a_stand_in_game_and_analyze(tmp_path, capsys):
    wav = tmp_path / "clicks.wav"
    click_track(wav, 1.5)
    game_csv, send_csv = tmp_path / "game.csv", tmp_path / "send.csv"
    game = FakeGame(game_csv)
    game.start()
    try:
        result = subprocess.run(
            [sys.executable, str(HERE / "send.py"), "--wav", str(wav), "--port", str(game.port),
             "--out", str(send_csv)],
            capture_output=True, text=True, timeout=60,
        )  # fmt: skip
    finally:
        time.sleep(0.1)
        game.stop.set()
        game.join()
    assert result.returncode == 0, result.stderr
    assert "features" in game.types and "side_state" in game.types
    assert game.types[-1] == "side_end"

    assert analyze.main(["--send", str(send_csv), "--game", str(game_csv)]) == 0
    report = capsys.readouterr().out
    assert "Clock offset" in report and "Onset messages only" in report and "| Late |" in report
