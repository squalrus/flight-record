# 06 · Message bridge to Unity

**Status:** done. File replay, the listening check (in time at a calibrated 80 ms), and live input with the clap test of Unity's native capture are measured in [RESULTS.md](RESULTS.md); D-101 is decided (keep the Python sidecar). · **Plan:** [docs/flightrec/experiments.md](../../docs/flightrec/experiments.md#06--message-bridge-to-unity) · **Depends on:** 0.3, 0.4

## Goal

Send the message contract from Python over UDP to a minimal Unity scene that flashes a square for each onset, scales a bar per band, and shows track and side text. Measure end-to-end latency and jitter, and test Unity-native capture latency.

## Success criteria

- Spawns visibly in time with the music after a calibrated offset
- Jitter fully absorbed by the 150-300 ms delay buffer
- Sidecar vs C# port decision recorded in `docs/decisions.md` (D-101), including a test of Unity-native capture (`Microphone`)

## What is here

| Piece | Where | What it does |
| --- | --- | --- |
| UDP sender | [flightrec/bridge.py](../../flightrec/bridge.py) | Sends contract messages to `127.0.0.1:9750`, one JSON message per datagram; never blocks or raises on send; reads the game's replies |
| Sender script | [send.py](send.py) | Runs the feature pipeline on a file (silent or `--listen`), live input, or a recorded log, and sends the stream. For files it also sends `track_identified` from tags, `side_state` every second, and `side_end`. Writes a timing CSV |
| Analysis | [analyze.py](analyze.py) | Joins the sender's and the scene's CSVs into stage latencies, jitter, late shares per delay size, and the native-capture comparison, as Markdown |
| Contract and sources (C#) | [unity/Assets/Scripts/Audio/](../../unity/Assets/Scripts/Audio/) | `Contract.Parse` (mirrors `flightrec.messages`), `IMessageSource` with UDP, replay, and null sources, and `EventScheduler`, the delay buffer. Reusable by the game (backlog 4.3) |
| Scene | [unity/Assets/Experiments/Bridge/](../../unity/Assets/Experiments/Bridge/) | `Bridge.unity` with `BridgeDemo` (visuals, overlay, CSV log, clock replies) and `MicrophoneProbe` (native capture test) |
| Tests | [test_bridge.py](test_bridge.py), [unity/Assets/Tests/EditMode/](../../unity/Assets/Tests/EditMode/) | Python: sender, analysis maths, and `send.py` against a stand-in game. Unity: JSON reader, contract parser, scheduler |

### How timing is measured

- **Python** stamps every message with when its sound happened (`event`) and when it left (`sent`) on `time.perf_counter()`. `event` is when the audio reached the computer, using the capture's earliest block arrival so delivery jitter is excluded. For live input it subtracts the reported input latency; with `--listen` it adds the reported output latency, so it is when the sound was heard.
- **Unity** stamps when each message arrived (`recv`), when the delay buffer made it due (`due`), and the frame that showed it (`shown`) on `Stopwatch` (`AudioClock`). `shown` is when `Update` ran, not when the frame reached the screen: display latency (vsync, compositor, monitor) is not included.
- **Lining up the clocks.** The two clocks do not share a zero: on the development machine they differed by about 34,344 s. The scene therefore sends `{"clock": now}` back four times a second. Offset = median(game time - Python arrival time). Its error is at most the clock sample's trip back plus Python's 2 ms polling, so `transport` slightly overstates the true one-way time.
- **Delay buffer.** The scene shows each message at `t + base + delay`, where `base` is the fastest arrival (receive time - `t`) in the last 10 s. No clock sharing is needed for that. A message is *late* if it arrived after it was due, which is jitter the buffer did not absorb.

## Install and run

Python: the shared venv (see [../README.md](../README.md#setup)); no extra packages. Unity: the project in `unity/` (Unity 6000.3.17f1).

**1. Start the scene**, either way:

- **Editor:** open `unity/` in Unity and the scene `Assets/Experiments/Bridge/Bridge.unity`, then press Play. If the scene is missing, run the menu **Flight Record → Experiments → Create Bridge Scene**.
- **Standalone player** (closer to the shipped game; the measured runs use it): run the menu **Flight Record → Experiments → Build Bridge Player**, or in batch mode with the editor closed:

  ```sh
  "C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe" -batchmode -quit -projectPath unity \
      -executeMethod FlightRecord.Experiments.Editor.BridgeSceneBuilder.BuildPlayer -logFile -
  unity/Builds/Bridge/Bridge.exe -screen-fullscreen 0 -screen-width 960 -screen-height 540 \
      -logdir "C:\github\flight-record\experiments\06-message-bridge\out"
  ```

  Player options: `-logdir DIR` (default: the player's data folder; the editor writes to `experiments/06-message-bridge/out/`), `-delay MS` (default 150), `-port N`, `-mic NAME`, `-replay LOG.jsonl` (play a recorded log with no Python), `-nullsource`.

**2. Send audio** from the repo root:

```sh
python experiments/06-message-bridge/send.py --wav "samples/_songs/song.mp3"            # silent
python experiments/06-message-bridge/send.py --wav "samples/_songs/song.mp3" --listen   # heard
python experiments/06-message-bridge/send.py --device 9                                 # live
python experiments/06-message-bridge/send.py --log samples/_local/logs/LOG.jsonl        # a log
```

`--seconds N` stops early. The sender prints a line a second, including the game's clock offset once the scene replies. Its CSV goes to `out/send-*.csv` (git-ignored) or `--out`.

**3. Analyze** the newest pair of logs (or name them with `--send` and `--game`):

```sh
python experiments/06-message-bridge/analyze.py
```

In the scene, **Up / Down** change the delay by 10 ms and **Left / Right** by 1 ms; **M** starts or stops the microphone probe; **R** resets the counters. The overlay shows the message rate, frame rate, delay, the buffer's base, how many messages arrived after they were due, the track and side, and the log path.

## Manual checks

These need ears, a microphone, and a person; record the outcomes in [RESULTS.md](RESULTS.md).

1. **Listening check and calibrated delay** (criterion 1). Start the scene, then `send.py --wav SONG --listen` with a song that has clear drums. Watch the flashes on the line against the hits you hear, and adjust the delay with the arrow keys until they line up. Note the delay where they look in time, and the range where they still feel in time, for two or three songs. Then run `analyze.py` and record the numbers.
2. **Live input and native capture** (criteria 2 and 3). Use one microphone for both: make it the Windows default recording device, and pass its index to the sender (`python tools/list_devices.py`; prefer the WASAPI entry). Start the scene and press **M**, then start `send.py --device N`. Clap sharply about 30 times, a second or more apart. Stop the sender, then the scene, and run `analyze.py`. "Native capture vs bridge" is how much later each clap arrived over the bridge than Unity's own `Microphone` saw it: positive means native capture is faster.

## Output

[RESULTS.md](RESULTS.md) from the [template](../RESULTS.template.md), and the D-101 decision in [docs/decisions.md](../../docs/decisions.md).
