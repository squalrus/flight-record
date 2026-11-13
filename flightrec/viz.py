"""Pygame window and panels shared by the experiment visualizers.

Importing this module imports pygame, so import it only when a window is wanted; headless
runs, analysis code, and tests must not need it (see decision D-010).

Each panel owns a rectangle and draws itself with ``draw(...)``. Static parts (grids,
labels) are rendered once into a cached background surface.
"""

from __future__ import annotations

import os
import time
from collections import deque
from collections.abc import Sequence
from typing import Any

import numpy as np

os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")
import pygame  # noqa: E402

from flightrec.audio.spectrum import LogBands  # noqa: E402

Color = tuple[int, int, int]

BG: Color = (12, 14, 18)
PANEL: Color = (18, 21, 27)
GRID: Color = (40, 44, 52)
TEXT: Color = (200, 205, 214)
DIM: Color = (120, 126, 138)
ACCENT: Color = (90, 170, 255)
HOLD: Color = (230, 230, 240)
HUM: Color = (255, 170, 60)
CHANNEL: list[Color] = [(80, 200, 255), (255, 110, 200)]
LANE: dict[str, Color] = {
    "low": (255, 120, 80),
    "mid": (120, 220, 140),
    "high": (120, 180, 255),
    "all": (230, 230, 240),
    "beat": (255, 210, 90),
}
GOOD: Color = (80, 200, 120)
WARN: Color = (230, 200, 60)
BAD: Color = (240, 80, 70)


def _frac(value: float, lo: float, hi: float) -> float:
    """Position of ``value`` between ``lo`` and ``hi``, clamped to 0..1 (plain float:
    pygame rejects NumPy scalars in rects)."""
    return min(1.0, max(0.0, (float(value) - lo) / (hi - lo)))


class Window:
    """A pygame window with fonts, event handling, and text helpers."""

    def __init__(self, title: str, size: tuple[int, int] = (1100, 660)) -> None:
        pygame.init()
        self.size = size
        self.screen = pygame.display.set_mode(size)
        pygame.display.set_caption(title)
        mono = "consolas,menlo,dejavusansmono,monospace"
        self.font = pygame.font.SysFont(mono, 14)
        self.big = pygame.font.SysFont(mono, 30, bold=True)
        self.keys: list[int] = []
        """Keys pressed since the last ``closed()`` call (pygame key codes)."""

    def closed(self) -> bool:
        """Process events; True if the window was closed or Esc/Q pressed."""
        self.keys = []
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                return True
            if event.type == pygame.KEYDOWN:
                if event.key in (pygame.K_ESCAPE, pygame.K_q):
                    return True
                self.keys.append(event.key)
        return False

    def begin(self) -> None:
        self.screen.fill(BG)

    def end(self) -> None:
        pygame.display.flip()

    def text(
        self,
        text: str,
        pos: tuple[float, float],
        color: Color = TEXT,
        big: bool = False,
        surface: Any = None,
    ) -> None:
        font = self.big if big else self.font
        (surface or self.screen).blit(font.render(text, True, color), pos)

    def save(self, path: str) -> None:
        pygame.image.save(self.screen, path)

    def close(self) -> None:
        pygame.quit()


class Panel:
    """Base: a rectangle on a window with an optional title."""

    def __init__(self, win: Window, rect: tuple[int, int, int, int], title: str = "") -> None:
        self.win = win
        self.rect = pygame.Rect(rect)
        self.title = title

    def frame(self) -> None:
        pygame.draw.rect(self.win.screen, PANEL, self.rect)
        pygame.draw.rect(self.win.screen, GRID, self.rect, 1)
        if self.title:
            self.win.text(self.title, (self.rect.left + 6, self.rect.top + 4), DIM)


class WaveformPanel(Panel):
    """One lane per channel showing the latest frames."""

    def draw(self, frames: np.ndarray) -> None:
        self.frame()
        channels = frames.shape[1]
        lane_h = self.rect.height / channels
        idx = np.linspace(0, len(frames) - 1, self.rect.width - 2).astype(int)
        xs = np.arange(len(idx)) + self.rect.left + 1
        for ch in range(channels):
            mid = self.rect.top + lane_h * (ch + 0.5)
            pygame.draw.line(self.win.screen, GRID, (self.rect.left, mid), (self.rect.right, mid))
            ys = mid - np.clip(frames[idx, ch], -1.0, 1.0) * (lane_h / 2 - 2)
            points = np.column_stack([xs, ys]).tolist()
            pygame.draw.lines(self.win.screen, CHANNEL[ch % len(CHANNEL)], False, points)


class SpectrumPanel(Panel):
    """Log-frequency bars in dBFS with a falling peak hold and 50/60 Hz hum ticks."""

    def __init__(
        self,
        win: Window,
        rect: tuple[int, int, int, int],
        bands: LogBands,
        db_range: tuple[float, float] = (-100.0, 0.0),
    ) -> None:
        super().__init__(win, rect)
        self.bands = bands
        self.lo, self.hi = db_range
        self.bar_w = self.rect.width / len(bands.centers)
        self.hold = np.full(len(bands.centers), self.lo)
        self.last = time.perf_counter()
        self.background = self._background()

    def x_for(self, f: float) -> float:
        lo, hi = np.log(self.bands.edges[0]), np.log(self.bands.edges[-1])
        return float(self.rect.left + (np.log(f) - lo) / (hi - lo) * self.rect.width)

    def y_for(self, db: float) -> float:
        return self.rect.bottom - _frac(db, self.lo, self.hi) * self.rect.height

    def _background(self) -> Any:
        surface = pygame.Surface(self.win.size, pygame.SRCALPHA)
        r = self.rect
        for db in range(int(self.lo), int(self.hi) + 1, 20):
            y = self.y_for(db)
            pygame.draw.line(surface, GRID, (r.left, y), (r.right, y))
            self.win.text(f"{db:>4}", (r.left - 44, y - 8), DIM, surface=surface)
        for f in (20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000):
            if self.bands.edges[0] <= f <= self.bands.edges[-1]:
                x = self.x_for(f)
                pygame.draw.line(surface, GRID, (x, r.top), (x, r.bottom))
                label = f"{f // 1000}k" if f >= 1000 else str(f)
                self.win.text(label, (x - 8, r.bottom + 4), DIM, surface=surface)
        for f in (50, 60):
            x = self.x_for(f)
            pygame.draw.line(surface, HUM, (x, r.top), (x, r.top + 10), 2)
        self.win.text("dBFS", (r.left - 44, r.top - 22), DIM, surface=surface)
        self.win.text("hum 50/60", (self.x_for(50) - 4, r.top - 22), HUM, surface=surface)
        return surface

    def draw(self, band_db: np.ndarray) -> None:
        now = time.perf_counter()
        dt, self.last = now - self.last, now
        screen = self.win.screen
        screen.blit(self.background, (0, 0))
        self.hold = np.maximum(band_db, self.hold - 20.0 * dt)  # falls 20 dB/s
        width = max(1.0, self.bar_w - 1)
        for i, (db, hold) in enumerate(zip(band_db, self.hold, strict=True)):
            x = self.rect.left + i * self.bar_w
            top = self.y_for(db)
            if top < self.rect.bottom:
                pygame.draw.rect(screen, ACCENT, (x, top, width, self.rect.bottom - top))
            y = self.y_for(hold)
            pygame.draw.line(screen, HOLD, (x, y), (x + width, y))


class MeterPanel(Panel):
    """Per-channel peak fill, RMS tick, peak readout, and a clip light held for 1 s."""

    def __init__(
        self,
        win: Window,
        rect: tuple[int, int, int, int],
        channels: int,
        clip_dbfs: float = -0.1,
        db_range: tuple[float, float] = (-100.0, 0.0),
    ) -> None:
        super().__init__(win, rect)
        self.channels = channels
        self.clip_dbfs = clip_dbfs
        self.lo, self.hi = db_range
        self.clip_until = np.zeros(channels)

    def draw(self, ch_rms: np.ndarray, ch_peak: np.ndarray) -> None:
        screen = self.win.screen
        now = time.perf_counter()
        width = self.rect.width / self.channels
        top = self.rect.top + 30
        height = self.rect.bottom - top
        for ch in range(self.channels):
            x = self.rect.left + ch * width + 6
            w = width - 12
            pygame.draw.rect(screen, GRID, (x, top, w, height), 1)
            peak = float(ch_peak[ch])
            frac = _frac(peak, self.lo, self.hi)
            color = GOOD if peak < -12 else WARN if peak < -3 else BAD
            pygame.draw.rect(
                screen, color, (x + 1, top + height * (1 - frac), w - 2, height * frac)
            )
            y = top + height * (1 - _frac(ch_rms[ch], self.lo, self.hi))
            pygame.draw.line(screen, HOLD, (x, y), (x + w, y), 2)
            if peak >= self.clip_dbfs:
                self.clip_until[ch] = now + 1.0
            clip = (240, 60, 50) if self.clip_until[ch] > now else GRID
            pygame.draw.rect(screen, clip, (x, self.rect.top, w, 20))
            self.win.text(f"{peak:6.1f}", (x - 2, self.rect.bottom + 4))


class SpectrogramPanel(Panel):
    """Scrolling spectrogram: one column per pushed spectrum, newest at the right."""

    STOPS = np.array(
        [[0, 0, 4], [40, 11, 84], [101, 21, 110], [159, 42, 99], [212, 72, 66],
         [245, 125, 21], [250, 193, 39], [252, 255, 164]], dtype=float
    )  # fmt: skip

    def __init__(
        self,
        win: Window,
        rect: tuple[int, int, int, int],
        n_bands: int,
        db_range: tuple[float, float] = (-90.0, -10.0),
        title: str = "",
    ) -> None:
        super().__init__(win, rect, title)
        self.lo, self.hi = db_range
        self.image = pygame.Surface((self.rect.width, self.rect.height))
        self.image.fill(self.STOPS[0].astype(int).tolist())
        rows = np.arange(self.rect.height)[::-1]  # bottom row = lowest band
        self._band_for_row = np.minimum(rows * n_bands // self.rect.height, n_bands - 1)
        x = np.linspace(0, 1, len(self.STOPS))
        lut_x = np.linspace(0, 1, 256)
        self._lut = np.stack(
            [np.interp(lut_x, x, self.STOPS[:, c]) for c in range(3)], axis=1
        ).astype(np.uint8)

    def push(self, columns: Sequence[np.ndarray]) -> None:
        """Append spectra (band dB arrays), oldest first."""
        n = min(len(columns), self.rect.width)
        if n == 0:
            return
        self.image.scroll(-n, 0)
        block = np.array(columns[-n:])[:, self._band_for_row]  # (n, height)
        idx = np.clip((block - self.lo) / (self.hi - self.lo) * 255, 0, 255).astype(np.uint8)
        rgb = self._lut[idx]  # (n, height, 3) matches pygame's (x, y) layout
        strip = pygame.surfarray.make_surface(rgb)
        self.image.blit(strip, (self.rect.width - n, 0))

    def draw(self) -> None:
        self.win.screen.blit(self.image, self.rect.topleft)
        pygame.draw.rect(self.win.screen, GRID, self.rect, 1)
        if self.title:
            self.win.text(self.title, (self.rect.left + 6, self.rect.top + 4), HOLD)


class LanesPanel(Panel):
    """Scrolling event timeline: one lane per name, ticks at event times, newest right.

    A box at the left of each lane flashes when that lane fires.
    """

    def __init__(
        self,
        win: Window,
        rect: tuple[int, int, int, int],
        names: Sequence[str],
        seconds: float = 6.0,
        title: str = "",
    ) -> None:
        super().__init__(win, rect, title)
        self.names = list(names)
        self.seconds = seconds
        self.events: deque[tuple[float, int, float]] = deque()
        self.counts = dict.fromkeys(self.names, 0)
        self.last_fire = dict.fromkeys(self.names, -1e9)

    def add(self, t: float, name: str, strength: float = 1.0) -> None:
        self.events.append((t, self.names.index(name), strength))
        self.counts[name] += 1
        self.last_fire[name] = t

    def draw(self, now_t: float) -> None:
        self.frame()
        screen = self.win.screen
        while self.events and self.events[0][0] < now_t - self.seconds:
            self.events.popleft()
        label_w = 120
        lane_h = (self.rect.height - 22) / len(self.names)
        left = self.rect.left + label_w
        width = self.rect.right - 6 - left
        for i, name in enumerate(self.names):
            y = self.rect.top + 22 + i * lane_h
            color = LANE.get(name, ACCENT)
            fresh = now_t - self.last_fire[name] < 0.1
            box = (self.rect.left + 6, y + 3, 14, lane_h - 6)
            pygame.draw.rect(screen, color if fresh else GRID, box)
            self.win.text(
                f"{name:<5}{self.counts[name]:>5}", (self.rect.left + 26, y + lane_h / 2 - 8)
            )
            pygame.draw.line(screen, GRID, (left, y + lane_h), (left + width, y + lane_h))
        for t, lane, strength in self.events:
            x = left + width - (now_t - t) / self.seconds * width
            y = self.rect.top + 22 + lane * lane_h
            color = LANE.get(self.names[lane], ACCENT)
            h = (lane_h - 6) * (0.35 + 0.65 * min(1.0, strength))
            mid = y + lane_h / 2
            pygame.draw.line(screen, color, (x, mid - h / 2), (x, mid + h / 2), 3)


class TracePanel(Panel):
    """Scrolling line traces of one or more series over the last ``seconds``."""

    def __init__(
        self,
        win: Window,
        rect: tuple[int, int, int, int],
        series: dict[str, Color],
        lo: float,
        hi: float,
        seconds: float = 10.0,
        log: bool = False,
        title: str = "",
        fmt: str = "{:.2f}",
    ) -> None:
        super().__init__(win, rect, title)
        self.series = series
        self.lo, self.hi = (np.log(lo), np.log(hi)) if log else (lo, hi)
        self.log = log
        self.seconds = seconds
        self.fmt = fmt
        self.data: deque[tuple[float, dict[str, float]]] = deque()

    def add(self, t: float, values: dict[str, float]) -> None:
        self.data.append((t, values))

    def draw(self, now_t: float) -> None:
        self.frame()
        while self.data and self.data[0][0] < now_t - self.seconds:
            self.data.popleft()
        if len(self.data) < 2:
            return
        r = self.rect
        ts = np.array([t for t, _ in self.data])
        xs = r.right - 2 - (now_t - ts) / self.seconds * (r.width - 4)
        legend_x = r.right - 8
        for name, color in reversed(list(self.series.items())):
            vals = np.array([v.get(name, np.nan) for _, v in self.data], dtype=float)
            if self.log:
                vals = np.log(np.maximum(vals, 1e-9))
            fracs = np.clip((vals - self.lo) / (self.hi - self.lo), 0, 1)
            ys = r.bottom - 4 - fracs * (r.height - 24)
            ok = np.isfinite(ys)
            if ok.sum() >= 2:
                pygame.draw.lines(
                    self.win.screen, color, False, np.column_stack([xs[ok], ys[ok]]).tolist(), 2
                )
            latest = self.data[-1][1].get(name)
            label = f"{name} {self.fmt.format(latest) if latest is not None else '-'}"
            surf = self.win.font.render(label, True, color)
            legend_x -= surf.get_width() + 12
            self.win.screen.blit(surf, (legend_x + 12, r.top + 4))


class ChromaPanel(Panel):
    """12 pitch-class bars (C..B) with the estimated key; scale notes are highlighted."""

    NAMES = ("C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B")
    MAJOR_STEPS = (0, 2, 4, 5, 7, 9, 11)
    MINOR_STEPS = (0, 2, 3, 5, 7, 8, 10)

    def draw(self, chroma: np.ndarray, key: str | None, confidence: float) -> None:
        self.frame()
        r = self.rect
        in_scale: set[int] = set()
        if key:
            tonic_name, mode = key.split()
            tonic = self.NAMES.index(tonic_name)
            steps = self.MAJOR_STEPS if mode == "major" else self.MINOR_STEPS
            in_scale = {(tonic + s) % 12 for s in steps}
        label = f"{key}  ({confidence:.2f})" if key else "key: listening..."
        self.win.text(label, (r.left + 8, r.top + 22), HOLD, big=True)
        top = r.top + 64
        height = r.bottom - 24 - top
        bar_w = (r.width - 16) / 12
        for pc in range(12):
            x = r.left + 8 + pc * bar_w
            h = height * min(1.0, max(0.0, float(chroma[pc])))
            color = ACCENT if pc in in_scale else GRID
            pygame.draw.rect(self.win.screen, color, (x + 2, top + height - h, bar_w - 4, h))
            self.win.text(self.NAMES[pc], (x + 4, r.bottom - 20), TEXT if pc in in_scale else DIM)


class TempoPanel(Panel):
    """Tempo readout, confidence bar, and a pulse that flashes on predicted beats."""

    def __init__(self, win: Window, rect: tuple[int, int, int, int], title: str = "") -> None:
        super().__init__(win, rect, title)
        self.pulse = 0.0
        self.last = time.perf_counter()

    def draw(self, tempo: float | None, confidence: float, beat: bool) -> None:
        self.frame()
        now = time.perf_counter()
        self.pulse = 1.0 if beat else max(0.0, self.pulse - (now - self.last) * 5.0)
        self.last = now
        r = self.rect
        text = f"{tempo:5.1f} BPM" if tempo else "  --- BPM"
        self.win.text(text, (r.left + 8, r.top + 22), HOLD, big=True)
        if tempo:  # half / double time are equally valid readings of most music
            alt = f"x2 {tempo * 2:.0f}  /2 {tempo / 2:.0f}"
            self.win.text(alt, (r.left + 200, r.top + 34), DIM)
        bar = (r.left + 8, r.top + 64, (r.width - 90) * min(1.0, confidence), 10)
        pygame.draw.rect(self.win.screen, GRID, (r.left + 8, r.top + 64, r.width - 90, 10), 1)
        color = GOOD if confidence >= 0.5 else WARN if confidence >= 0.25 else BAD
        pygame.draw.rect(self.win.screen, color, bar)
        self.win.text(f"confidence {confidence:.2f}", (r.left + 8, r.top + 80), DIM)
        radius = 18 + 10 * self.pulse
        center = (r.right - 40, r.top + r.height / 2 + 6)
        shade = int(60 + 195 * self.pulse)
        pygame.draw.circle(self.win.screen, (shade, int(shade * 0.82), 90), center, radius)


def status_lines(win: Window, lines: Sequence[str], top: float) -> None:
    for i, line in enumerate(lines):
        win.text(line, (16, top + i * 20))


class IdentificationPanel(Panel):
    """What is playing, from a background ``flightrec.identification.Identifier``: status,
    the current match (artist, title, album, confidence, latency), and recent attempts."""

    STATUS_COLORS: dict[str, Color] = {
        "listening": DIM,
        "identifying": WARN,
        "matched": GOOD,
        "no match": DIM,
        "error": BAD,
        "limit reached": BAD,
    }

    def draw(self, identifier: Any, audio_t: float) -> None:
        self.frame()
        r = self.rect
        win = self.win
        status = identifier.status(audio_t)
        spinner = "|/-\\"[int(time.perf_counter() * 8) % 4] if status == "identifying" else ""
        next_s = identifier.seconds_until_next(audio_t)
        header = f"{identifier.service.name}: {status} {spinner}"
        win.text(header, (r.left + 6, r.top + 4), self.STATUS_COLORS.get(status, TEXT))
        counts = f"requests {identifier.requests}/{identifier.max_requests}"
        if next_s is not None:
            counts += f"   next in {next_s:4.1f} s"
        win.text(counts, (r.left + 300, r.top + 4), DIM)

        current = identifier.current
        if current is None:
            win.text("no match yet", (r.left + 8, r.top + 28), DIM, big=True)
        else:
            res = current.result
            win.text(f"{res.artist} - {res.title}"[:56], (r.left + 8, r.top + 28), HOLD, big=True)
            detail = f"{res.album or '(no album)'}"
            conf = "n/a" if res.score is None else f"{res.score:.2f}"
            detail += f"   confidence {conf}   {res.latency_s:.2f} s   at {current.t:5.1f} s"
            win.text(detail[:110], (r.left + 8, r.top + 66), TEXT)

        y = r.top + 90
        for attempt in reversed(identifier.history[-3:]):
            res = attempt.result
            if res.matched:
                line, color = f"match     {res.artist} - {res.title}", GOOD
            elif res.error:
                line, color = f"error     {res.error}", BAD
            else:
                line, color = "no match", DIM
            win.text(f"{attempt.t:7.1f} s  {line}"[:120], (r.left + 8, y), color)
            y += 16
