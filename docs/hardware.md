# Hardware

Goal: hear the source live with zero added latency, and give the computer a copy to analyze, for any make and model of turntable or other source.

## Signal chain

```
turntable ──► phono preamp ──┐
CD player ───────────────────┼──► line-level splitter ──► speakers / amp
MP3 player ──────────────────┘            │
                                          └──► USB audio interface (line in) ──► computer
```

Only a turntable outputs phono level (very weak, needs RIAA equalization). Everything else is already line level. So the design point is one **line-level split**, with a phono preamp ahead of it for turntables that lack a built-in preamp.

## Split options

| Option | Notes |
| --- | --- |
| Receiver/amp with **Rec Out / Tape Out** | Built-in splitter. Signal is taken after input selection but before the volume knob, so the computer sees a consistent level. Best option if you already own one. |
| Small analog mixer (e.g. Behringer Xenyx class) | Several inputs, a main out to speakers and a separate out feeding the interface. Flexible, handles multiple sources. |
| Passive stereo RCA Y-cables | Cheapest. Works fine. If you hear hum or buzz, add a ground-loop isolator. |

## USB audio interface

- Needs a **stereo line-in**. A Behringer UCA202-class box is enough for a first prototype.
- Check the input connectors: stereo RCA, or a two-input interface with RCA-to-1/4" adapters.
- Use a class-compliant interface so it works without drivers on Windows, macOS, and Linux.
- Prefer 44.1 or 48 kHz, 16-bit or better; nothing exotic is required for this use.

## Turntables

- **USB turntables** (e.g. Audio-Technica AT-LP120X, AT-LP60X-USB class) show up as an audio input, but you lose the splitter-friendly line-level path unless they also expose RCA out. For the "any source" goal, prefer using the RCA output into the splitter.
- **Built-in preamp:** many turntables have a Phono/Line switch. Set it to Line and skip the external preamp.
- **No preamp:** add an external phono preamp (a basic Behringer PP400-class unit is enough to start).

## Computers

- **Development and first playable:** a laptop. Plenty of headroom for FFT, rendering, and the Unity game.
- **Raspberry Pi 3 Model B+** (already owned): has no audio input, so it needs the same USB interface. It is fine for prototyping the audio analysis on its own, but the full game plus analysis will likely strain it. A Pi 4/5 is the realistic target if a standalone "console" version is wanted later.

## Controllers for local co-op

Multiplayer is same-machine (see D-009), so players need input devices, not a network.

- USB or Bluetooth gamepads, one per player (class-compliant Xbox-style pads work with Unity's Input System without drivers).
- A powered USB hub if the laptop runs short of ports once the audio interface and several controllers are connected.
- Keyboard-sharing for extra players is possible but is an open question (D-110).

## Shopping list for the first experiments

- [ ] USB audio interface with stereo line-in
- [ ] Splitter (Rec Out receiver you already have, or Y-cables, or a small mixer)
- [ ] Phono preamp, if the turntable has no built-in one
- [ ] RCA cables / adapters as needed
- [ ] Ground-loop isolator (only if hum appears)
- [ ] Speakers or amp
- [ ] Gamepads (one per player) and a powered USB hub, if needed

## Test checklist

- Level: input is strong but not clipping on loud passages (peak meter stays below full scale).
- Noise: with the needle on a silent groove, noise floor is well below music level.
- Hum: no 50/60 Hz buzz when the source is idle.
- Channels: left and right are not swapped; mono sources arrive on both channels.
