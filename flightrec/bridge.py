"""Send contract messages to the game over UDP: one JSON message per datagram.

The game listens on ``DEFAULT_PORT`` on the same machine (local only, D-009). Datagrams
are the contract's JSON (``Message.to_json()``), so the game parses exactly what a log
holds. UDP suits a 94 Hz feature stream: a lost or late datagram is better skipped than
retried, and there is no connection to set up or lose.

The game may send small JSON datagrams back to the sender's address (experiment 06 uses
them as clock samples for latency measurement). They are not contract messages;
``UdpSender.replies()`` returns them with their arrival time.
"""

from __future__ import annotations

import json
import socket
import time
from typing import Any

from flightrec.messages import Message

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 9750
MAX_DATAGRAM = 65_507
"""Largest UDP payload over IPv4. Contract messages are far smaller (features: ~0.5 KB)."""


class UdpSender:
    """Send contract messages to ``host:port``. Never blocks and never raises on send.

    A send that fails (nobody listening, buffer full) is counted in ``errors`` and
    dropped, because the game must not stall on the audio side and vice versa.
    """

    def __init__(self, host: str = DEFAULT_HOST, port: int = DEFAULT_PORT) -> None:
        self.address = (host, port)
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._sock.setblocking(False)
        self._sock.bind(("", 0))  # a fixed local port, so replies can find us
        if hasattr(socket, "SIO_UDP_CONNRESET"):
            # Windows: otherwise a send while the game is not running makes later reads
            # fail with WSAECONNRESET.
            self._sock.ioctl(socket.SIO_UDP_CONNRESET, False)
        self.sent = 0
        self.errors = 0
        self.bytes = 0

    @property
    def local_port(self) -> int:
        return self._sock.getsockname()[1]

    def send(self, message: Message) -> float | None:
        """Send one message; the ``perf_counter()`` time it left, or None if it failed."""
        data = message.to_json().encode("utf-8")
        if len(data) > MAX_DATAGRAM:
            raise ValueError(f"{message.TYPE} message is {len(data)} bytes; too big for UDP")
        try:
            self._sock.sendto(data, self.address)
        except OSError:
            self.errors += 1
            return None
        sent = time.perf_counter()
        self.sent += 1
        self.bytes += len(data)
        return sent

    def replies(self) -> list[tuple[float, dict[str, Any]]]:
        """Datagrams the game sent back since the last call, as (arrival time, JSON object).

        Arrival is ``perf_counter()`` when read, so call this often for accurate times.
        Anything that is not a JSON object is ignored.
        """
        out: list[tuple[float, dict[str, Any]]] = []
        for _ in range(10_000):  # bounded, so a flood cannot hold up the caller
            try:
                data = self._sock.recv(MAX_DATAGRAM)
            except (BlockingIOError, InterruptedError):
                break
            except OSError:
                # An earlier send to a closed port, reported late; the next read is fine.
                continue
            arrived = time.perf_counter()
            try:
                obj = json.loads(data)
            except (UnicodeDecodeError, ValueError):
                continue
            if isinstance(obj, dict):
                out.append((arrived, obj))
        return out

    def close(self) -> None:
        self._sock.close()

    def __enter__(self) -> UdpSender:
        return self

    def __exit__(self, *exc: object) -> None:
        self.close()
