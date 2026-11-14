# Results: 06 · Message bridge to Unity

- **Date:** 2026-11-13 (runs 1-3)
- **Author / agent:** Claude Code (run 1, and the analysis of runs 2 and 3); the project owner (run 2 by ear, run 3's claps)
- **Commit:** `af84a94` plus the uncommitted experiment 06 code
- **Plan:** [docs/flightrec/experiments.md § 06](../../docs/flightrec/experiments.md#06--message-bridge-to-unity)

## Setup

- **Computer:** laptop, Intel Core i7-8565U (1.8 GHz), Windows 11 Home (10.0.26300), GeForce GTX 1050 Ti Max-Q and Intel UHD 620.
- **Python:** 3.13.13, numpy 2.5.4, sounddevice 0.5.6. Feature pipeline at hop 512, block 512, window 2048, gate -50 dBFS (defaults). File at 44.1 kHz.
- **Unity:** 6000.3.17f1, quality level "Good" (vSync on in the project settings). Run 1: standalone Windows player (`Bridge.exe`), windowed 960x540, frames at a median interval of 11.7 ms (about 85 fps), p95 16.7 ms, max 20.0 ms. Run 2: the editor's Play mode, median frame interval 11.7 ms, with occasional hitches up to 53 ms. Frame intervals are measured from the scene log.
- **Audio out (run 2):** the laptop's default output device through `--listen` (D-011).
- **Transport:** UDP on loopback (127.0.0.1:9750), one JSON message per datagram, both programs on the same machine.

## Recordings / data used

| Run | Input | Length | Where |
| --- | --- | --- | --- |
| 1 | "Calling (feat. Frankmusik)", Kalax, *III*: electronic / synthwave, steady drums. MP3 file, replayed silently (no `--listen`). | 90 s of the song (`--seconds 90`) | `samples/_songs/` (not committed) |
| 2a | Listening check, `--listen`: a first song with the delay at 150 ms throughout (heard as in time) | 171.5 s | `samples/_songs/` (not committed; song not recorded in the logs) |
| 2b | Listening check, `--listen`: the delay stepped down from 150 to 80 ms while listening | 69.4 s | as above |
| 2c | Listening check, `--listen`: a second song at 80 ms throughout | 86.4 s | as above |
| 3 | Live microphone: about 12 s of music playing in the room, then claps a second or two apart, with the microphone probe on | 59.1 s (claps from 14 to 57 s) | Not kept (live input) |

Logs (git-ignored, in `experiments/06-message-bridge/out/`): run 1 `send-kalax-silent.csv` with `game-20261113-121930.csv`; run 2a `send-20261113-123856.csv` with `game-20261113-123810.csv`; runs 2b and 2c `send-20261113-124944.csv` and `send-20261113-125135.csv`, both with `game-20261113-124348.csv`; run 3 `send-20261113-162951.csv` with `game-20261113-162921.csv`. The scene ran on through 2b and 2c, so `analyze.py` keeps only the scene rows inside each sender run.

## Method actually followed

Run 1: the player was started, then `send.py --wav SONG --seconds 90`, then the player was closed and `analyze.py` was run on the two logs. In a silent replay, `event` is when the replay delivered the audio, as an interface would. So "analysis" is Python's share of the latency and excludes any input latency.

Run 2: the scene in the editor's Play mode, then `send.py --wav SONG --listen`. The owner watched the onset flashes against the drums and adjusted the delay with the arrow keys. With `--listen`, `event` is when the sound was heard: delivery plus the output stream's reported latency. So "analysis" is negative (the message leaves before the sound is heard) and "end to end" is how long after the sound a flash was drawn. Display latency (the time from `Update` to light on the screen) is not in any number.

Run 3: the scene in the editor's Play mode with the microphone probe on (**M**), reading the Windows default recording device with Unity's `Microphone`; `send.py --device N` on the same microphone; the delay left at 150 ms. Music played in the room for the first 12 s, then the owner clapped. For live input, `event` is when the block arrived minus the device's reported input latency, so "analysis" includes that latency. The probe stamps the first frame on which a clap's samples are visible, with no analysis; "bridge - native" pairs each probe onset with the nearest bridge onset (within 300 ms), both on the game's clock.

## Measurements

### Run 1: file, silent, 150 ms delay

- Messages: **7,849 sent, 7,849 received, 0 lost**, at 87.2 per second over 90.0 s. 442 of them carried onsets.
- Clock offset (game - sender): -34,344,344.285 ms, interquartile range 1.420 ms over 359 samples. Python's `perf_counter()` and .NET's `Stopwatch` do not share a zero, so the clock samples are needed.

Per stage, all messages (ms):

| Stage | n | min | p50 | p95 | p99 | max |
| --- | --: | --: | --: | --: | --: | --: |
| analysis (audio delivered → sent) | 7849 | 1.53 | 10.22 | 17.29 | 18.66 | 38.15 |
| transport (sent → received) | 7849 | -0.60 | 1.60 | 1.67 | 2.35 | 7.74 |
| hold (received → shown, includes the 150 ms buffer) | 7849 | 119.40 | 143.67 | 151.15 | 152.89 | 156.59 |
| end to end (audio delivered → shown) | 7849 | 150.81 | 154.97 | 159.27 | 160.42 | 164.19 |
| shown - due (wait for the next frame) | 7849 | 0.00 | 1.44 | 5.74 | 6.75 | 9.54 |

Onset messages only (ms):

| Stage | n | min | p50 | p95 | p99 | max |
| --- | --: | --: | --: | --: | --: | --: |
| analysis | 442 | 1.98 | 10.31 | 17.53 | 18.87 | 38.15 |
| transport | 442 | 1.13 | 1.60 | 1.68 | 2.23 | 2.40 |
| end to end | 442 | 153.17 | 154.98 | 159.29 | 160.35 | 162.15 |
| shown - due | 442 | 0.00 | 1.34 | 5.60 | 6.89 | 7.49 |

Jitter: each `features` message's arrival relative to its audio time, minus the earliest arrival (ms):

| n | min | p50 | p95 | p99 | max |
| --: | --: | --: | --: | --: | --: |
| 7750 | 0.00 | 8.73 | 15.80 | 17.20 | 36.65 |

Share of `features` messages a delay buffer of each size would still show late:

| Delay buffer (ms) | 0 | 5 | 10 | 20 | 50 | 100 | 150 |
| --- | --: | --: | --: | --: | --: | --: | --: |
| Late | 99.99% | 77.74% | 41.45% | 0.22% | 0.00% | 0.00% | 0.00% |

### Run 2: files heard through `--listen`, delay calibrated by ear

Heard to drawn, for onset messages (ms; negative = drawn before the sound was heard, before display latency):

| Run | Delay | n onsets | min | p50 | p95 | p99 | max |
| --- | --- | --: | --: | --: | --: | --: | --: |
| 2a | 150 ms | 849 | 61.32 | 63.78 | 67.17 | 70.41 | 92.00 |
| 2c | 80 ms | 406 | -9.49 | -6.82 | -3.98 | 0.91 | 12.03 |

- **By ear:** 150 ms "seemed correct"; 80 ms "seemed even better", on both songs. 80 ms is where the measurements put the flash on the sound: -7 ms before display latency, which on a 60 Hz panel adds at least part of a frame.
- **Why 80 ms:** the output stream plays about 78 ms after a block is delivered (analysis p50 -78.70 ms in run 2c), so the sound is heard about 78 ms after Python has sent its message. The delay buffer only has to cover that plus a few ms of transport and frame wait.
- **Messages:** 2a 14,960 sent and received; 2b 6,058 and 6,058; 2c 7,541 and 7,541. No losses.
- **Late at 80 ms:** of 12,551 messages shown while the delay was 80 ms (2b and 2c), 2 arrived after they were due, by 3.3 and 2.3 ms.
- **Transport:** p50 1.61-1.82 ms, p99 3.56-3.95 ms across the three runs.
- **Jitter** (features, ms):

| Run | n | p50 | p95 | p99 | max | Late at 50 ms buffer | at 100 ms |
| --- | --: | --: | --: | --: | --: | --: | --: |
| 2a | 14772 | 12.33 | 21.60 | 23.52 | 70.09 | 0.03% | 0.00% |
| 2b | 5982 | 12.19 | 21.47 | 23.21 | 49.37 | 0.00% | 0.00% |
| 2c | 7446 | 12.33 | 21.74 | 23.43 | 83.37 | 0.12% | 0.00% |

Jitter is higher than in run 1 (p99 about 23 ms vs 17 ms) and its maximum reaches 83 ms. The `--listen` output stream drives block delivery, and the editor's frame hitches (up to 53 ms; one 92 ms wait for a frame in 2c) add to the tail.

### Run 3: live microphone, claps, Unity-native capture

- Messages: **5,538 sent, 5,538 received, 0 lost** over 59.1 s (93.8 per second). Clock offset -49,301,108.405 ms, interquartile range 1.410 ms over 235 samples.
- The music did not reach the probe: it saw no onsets in the first 12 s (the bridge saw about 5 per second), so all 22 probe onsets are claps, one about every 2 s from 14 to 56 s. Every one has a bridge onset within 33 ms. The bridge also reported a second onset on some claps (its detector is more sensitive than the probe's peak threshold).

Per stage, all messages (ms):

| Stage | n | min | p50 | p95 | p99 | max |
| --- | --: | --: | --: | --: | --: | --: |
| analysis (sound at the microphone → sent, includes the reported input latency) | 5538 | 23.32 | 38.08 | 43.98 | 45.67 | 70.03 |
| transport | 5538 | -0.09 | 1.71 | 1.77 | 3.42 | 13.05 |
| hold (includes the 150 ms buffer) | 5538 | 108.55 | 140.22 | 147.87 | 151.49 | 173.63 |
| end to end (sound → shown) | 5538 | 168.65 | 179.99 | 183.65 | 186.70 | 212.68 |
| shown - due | 5538 | 0.00 | 2.09 | 5.19 | 8.48 | 34.05 |

Onset messages: end to end p50 180.32 ms, p99 186.46 ms (n = 102, music and claps).

Jitter (features, ms): p50 13.56, p95 18.71, p99 20.17, max 43.56 (n = 5,538).

| Delay buffer (ms) | 0 | 5 | 10 | 20 | 50 | 100 | 150 |
| --- | --: | --: | --: | --: | --: | --: | --: |
| Late | 99.98% | 99.46% | 84.38% | 1.30% | 0.00% | 0.00% | 0.00% |

**Native capture vs bridge** (22 claps, all matched; positive = the bridge delivered the analyzed onset later than Unity's `Microphone` made the raw samples visible, ms):

| n | min | p50 | p95 | p99 | max |
| --: | --: | --: | --: | --: | --: |
| 22 | -6.52 | 7.64 | 23.07 | 32.14 | 32.14 |

## Success criteria

| Criterion (from the plan) | Result | Met? |
| --- | --- | --- |
| Spawns visibly in time with the music after a calibrated offset | Run 2: in time by ear on two songs at a calibrated 80 ms; measured heard-to-drawn p50 -6.8 ms, p99 +0.9 ms. | Met (file input heard through the computer) |
| Jitter fully absorbed by the 150-300 ms delay buffer | Worst jitter 36.65 ms (run 1, file), 83.37 ms (run 2, file heard), 43.56 ms (run 3, live microphone). 0.00% late at 100 ms and above in every run (and at 50 ms for live input); no messages lost. | Met |
| Sidecar vs C# decision recorded (D-101), with a test of Unity-native capture | Run 3: Unity's `Microphone` makes a clap's raw samples visible a median 7.6 ms (p95 23.1 ms) before the bridge delivers the analyzed onset. Keep the Python sidecar ([D-101](../../docs/decisions.md)). | Met |

## Observations and failure cases

- **Jitter comes from the Python side, not the transport.** Transport is about 1.6 ms with a spread of about 0.1 ms between p50 and p95. This figure includes the clock sample's return trip and Python's 2 ms polling, so the true one-way time is less. Analysis jitter (p99 18.7 ms, max 38 ms) comes from processing hops in bursts after the loop's sleeps and from the replay's own block pacing. The worst case is still well inside a 150 ms buffer.
- The negative transport minimum (-0.60 ms) is the clock-offset error, consistent with its 1.42 ms interquartile range: a single message cannot arrive before it is sent.
- Frames ran faster than a 60 Hz refresh even with vSync on in the quality settings. `shown` is when `Update` ran, so the time until pixels reach the screen is not in these numbers.
- No message was lost or arrived out of order enough to matter on loopback; the scheduler re-sorts by `t` anyway.
- **For files, the computer's own output latency sets the delay.** With `--listen`, about 78 ms of output buffering means 80 ms puts the visuals on the sound. That is below the 150-300 ms the architecture doc plans. For files (D-012) the game could read the output latency and set the delay itself, with no calibration. For live input the sound is heard *before* it is captured, so visuals always trail it.
- **For live input, the delay can be far below 150 ms.** In run 3 a flash was drawn about 180 ms after the sound at a 150 ms delay: roughly 25 ms for the fastest message to arrive (input latency, analysis, transport) plus the delay. Live jitter peaked at 44 ms and nothing would have been late at a 50 ms buffer, so a 50 ms delay would put flashes about 80 ms after the sound (plus display latency). The 150-300 ms in the architecture doc was meant to give the generator lead time; for live input that lead time costs the same amount of lag behind the music, so the game should use the smallest delay that absorbs jitter and get lead time some other way (for example, scrolling spawns in from the top of the screen).
- **Native capture saves little.** Unity sees the raw samples a median 7.6 ms sooner than the bridge delivers a finished onset. A C# port would still have to buffer an analysis window and run the same analysis, which is most of the bridge's 38 ms, so its real saving would be smaller than 7.6 ms. The probe's times are also frame-granular (frames about 12 ms apart in the editor), which is the size of the difference itself.
- One scene session can span several sender runs, and every file starts at `t = 0`. `analyze.py` therefore keeps only the scene rows inside the sender run's time span.

## Decision

**D-101: keep the audio modules in a Python sidecar** that sends the message contract over loopback UDP (provisional; see [docs/decisions.md](../../docs/decisions.md)). Transport is under 2 ms with no losses in about 42,000 messages, jitter is absorbed by a 50-100 ms buffer, and Unity-native capture would save under 8 ms at the median. The sidecar also keeps one Python codebase for analysis, identification, and Discogs. Revisit if shipping the game with a Python process turns out to be impractical (distribution is still open, D-109).

## Follow-ups

- The game's delay default and calibration (backlog 4.3): for file input, derive the delay from the reported output latency (about 80 ms here); for live input, default to the smallest delay that absorbs jitter (about 50 ms measured here) instead of 150 ms, and let calibration adjust it. Update the architecture doc's latency model when 4.3 settles this.
- The C# contract parser, `IMessageSource` (UDP, replay, null), and `EventScheduler` are the start of backlog 4.3 (audio source abstraction). The file source (D-012) is the part still missing.
