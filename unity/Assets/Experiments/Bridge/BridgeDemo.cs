using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FlightRecord.Audio;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlightRecord.Experiments
{
    /// <summary>
    /// Experiment 06's scene: receive the message contract, hold it in the delay buffer, and
    /// show it. A square flashes on the hit line for each onset (left, middle, right for low,
    /// mid, high), bars show bass / mid / treble / loudness, the background pulses on beats,
    /// and text shows the track and side. Everything is built in code, so the scene file holds
    /// only a camera and this component.
    ///
    /// Keys: Up / Down change the delay by 10 ms, Left / Right by 1 ms (with a file: the
    /// output latency); Space pauses a file; M toggles the microphone probe (Unity-native
    /// capture test); R resets the counters.
    ///
    /// File mode (backlog 4.3, D-012): the scene plays the file itself and the sidecar
    /// analyzes the same file, <c>send.py --wav FILE</c> started before or after the scene.
    /// Messages are released when playback reaches them (<see cref="FilePlaybackFeed"/>).
    ///
    /// Each released message is logged to a CSV (kind, type, t, recv, due, shown, delay_ms)
    /// with its times on <see cref="AudioClock"/>, for experiments/06-message-bridge/analyze.py.
    /// The scene replies to the sender with clock samples so the two logs can be lined up.
    ///
    /// Command line (player builds): -replay LOG.jsonl, -nullsource, -file AUDIO, -lead MS,
    /// -port N, -delay MS, -mic NAME, -logdir DIR. In the editor, set Audio File instead.
    /// </summary>
    public sealed class BridgeDemo : MonoBehaviour
    {
        [SerializeField] int port = UdpMessageSource.DefaultPort;
        [SerializeField] float delayMs = 150f;
        [Tooltip("Play this audio file (WAV, OGG, MP3) and sync the sidecar's analysis to it; empty for the bridge.")]
        [SerializeField] string audioFile = "";
        [Tooltip("How far the sidecar's analysis runs ahead of file playback.")]
        [SerializeField] float leadMs = 500f;
        [Tooltip("Microphone for the native capture test; empty for the system default.")]
        [SerializeField] string microphoneDevice = "";
        [SerializeField] bool logToCsv = true;

        const float HitLineY = -3f;
        static readonly Color Background = new Color(0.04f, 0.04f, 0.07f);
        static readonly Color[] BandColors =
        {
            new Color(1f, 0.35f, 0.3f), new Color(0.4f, 1f, 0.5f), new Color(0.4f, 0.6f, 1f), Color.white,
        };

        IAudioFeed _feed;
        UdpMessageSource _udp;
        readonly EventScheduler _scheduler = new EventScheduler();
        FilePlaybackFeed _file;
        AudioSourcePlayback _playback;
        readonly List<ScheduledMessage> _released = new List<ScheduledMessage>();
        MicrophoneProbe _mic;
        string _micStatus = "off (M to start)";
        StreamWriter _log;
        string _logPath = "off";
        double _nextFlush, _nextClock;

        Sprite _square, _bar;
        readonly Transform[] _bars = new Transform[4];
        Camera _camera;
        float _beat;
        readonly List<(SpriteRenderer Renderer, float Born)> _flashes = new List<(SpriteRenderer, float)>();

        string _track = "track: unidentified", _side = "side: unknown", _end = "";
        int _shown, _late, _rate, _countThisSecond;
        double _worstLate, _secondStart;
        float _fps;
        GUIStyle _style;

        void Start()
        {
            Application.runInBackground = true; // keep receiving while the terminal has focus
            ReadCommandLine(out string replay, out bool nullSource, out string logDir);
            _scheduler.DelaySeconds = delayMs / 1000.0;

            if (replay != null) _feed = new BufferedFeed(new ReplayMessageSource(replay), _scheduler);
            else if (nullSource) _feed = new BufferedFeed(new NullMessageSource(), _scheduler);
            else if (!string.IsNullOrWhiteSpace(audioFile))
            {
                _udp = new UdpMessageSource(port);
                _playback = new AudioSourcePlayback(gameObject.AddComponent<AudioSource>(), audioFile.Trim().Trim('"'));
                _feed = _file = new FilePlaybackFeed(_udp, _playback, leadMs / 1000.0);
            }
            else _feed = new BufferedFeed(_udp = new UdpMessageSource(port), _scheduler);
            _feed.Start();

            BuildVisuals();
            if (logToCsv) OpenLog(logDir);
            _secondStart = AudioClock.Now;
        }

        void ReadCommandLine(out string replay, out bool nullSource, out string logDir)
        {
            replay = logDir = null;
            nullSource = false;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "-replay": replay = next; break;
                    case "-nullsource": nullSource = true; break;
                    case "-port": port = int.Parse(next, CultureInfo.InvariantCulture); break;
                    case "-delay": delayMs = float.Parse(next, CultureInfo.InvariantCulture); break;
                    case "-file": audioFile = next; break;
                    case "-lead": leadMs = float.Parse(next, CultureInfo.InvariantCulture); break;
                    case "-mic": microphoneDevice = next; break;
                    case "-logdir": logDir = next; break;
                }
            }
        }

        void OpenLog(string dir)
        {
            // In the editor, next to the Python side's logs; in a player, its data folder.
            dir ??= Application.isEditor
                ? Path.Combine(Application.dataPath, "..", "..", "experiments", "06-message-bridge", "out")
                : Application.persistentDataPath;
            Directory.CreateDirectory(dir);
            _logPath = Path.GetFullPath(Path.Combine(dir, $"game-{DateTime.Now:yyyyMMdd-HHmmss}.csv"));
            _log = new StreamWriter(_logPath);
            _log.WriteLine("kind,type,t,recv,due,shown,delay_ms");
        }

        void Update()
        {
            double now = AudioClock.Now;
            _released.Clear();
            _feed.Update(now, _released);
            foreach (var m in _released) Show(m);

            if (_mic != null && _mic.Poll(now, out double seen))
            {
                Flash(3, 2.4f);
                Log("mic", "clap", 0, seen, seen, seen);
            }
            if (_udp != null && _udp.HasSender && now >= _nextClock)
            {
                _nextClock = now + 0.25;
                _udp.Reply("{\"clock\":" + AudioClock.Now.ToString("R", CultureInfo.InvariantCulture) + "}");
            }
            if (now >= _nextFlush) { _nextFlush = now + 1.0; _log?.Flush(); }
            if (now - _secondStart >= 1.0)
            {
                _rate = _countThisSecond;
                _countThisSecond = 0;
                _secondStart = now;
            }
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);

            HandleKeys();
            Animate();
        }

        void Show(ScheduledMessage s)
        {
            _countThisSecond++;
            _shown++;
            // Arrived after it was due: jitter the delay buffer (or the file lead) did not absorb.
            double lateBy = s.Received.Received - s.Due;
            if (lateBy > 0)
            {
                _late++;
                _worstLate = Math.Max(_worstLate, lateBy);
            }
            Log("msg", s.Message.Type, s.Message.T, s.Received.Received, s.Due, s.Released);

            switch (s.Message)
            {
                case FeaturesMessage f:
                    SetBar(0, f.Bass);
                    SetBar(1, f.Mid);
                    SetBar(2, f.Treble);
                    SetBar(3, f.Rms);
                    if (f.Beat) _beat = 1f;
                    if (f.Onset)
                    {
                        bool any = f.OnsetLow || f.OnsetMid || f.OnsetHigh;
                        if (f.OnsetLow) Flash(0, -2.5f);
                        if (f.OnsetMid || !any) Flash(1, 0f);
                        if (f.OnsetHigh) Flash(2, 2.5f);
                    }
                    break;
                case TrackIdentified t:
                    _track = $"track: {t.Artist} - {t.Title}" + (t.Album != null ? $" ({t.Album})" : "");
                    break;
                case IdentificationFailed f:
                    _track = $"track: unidentified ({f.Reason})";
                    break;
                case ReleaseSelected r:
                    _track += $"  ·  release {r.DiscogsId}, side {r.Side}";
                    break;
                case SideState side:
                    string expected = side.SideExpected is double e ? Clock(e) : "?";
                    string left = side.SideExpected is double e2 ? $", {Clock(Math.Max(0, e2 - side.SideElapsed))} left" : "";
                    _side = $"side {side.Side}, track {side.TrackIndex + 1}: {Clock(side.SideElapsed)} / {expected}{left}";
                    break;
                case SideEnd end:
                    _end = $"SIDE END ({end.Cause})";
                    break;
            }
        }

        double DelaySetting => _playback != null ? _playback.Clock.OutputLatency : _scheduler.DelaySeconds;

        static string Clock(double seconds) => $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";

        void Log(string kind, string type, double t, double recv, double due, double shown)
        {
            if (_log == null) return;
            var c = CultureInfo.InvariantCulture;
            _log.WriteLine(string.Join(",", kind, type, t.ToString("F6", c), recv.ToString("F6", c),
                due.ToString("F6", c), shown.ToString("F6", c), (DelaySetting * 1000).ToString("F1", c)));
        }

        void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            double step = kb.upArrowKey.wasPressedThisFrame ? 10 : kb.downArrowKey.wasPressedThisFrame ? -10
                : kb.rightArrowKey.wasPressedThisFrame ? 1 : kb.leftArrowKey.wasPressedThisFrame ? -1 : 0;
            if (step != 0 && _playback != null) _playback.Clock.OutputLatency = Math.Max(0, _playback.Clock.OutputLatency + step / 1000);
            else if (step != 0) _scheduler.DelaySeconds = Math.Max(0, _scheduler.DelaySeconds + step / 1000);
            if (kb.spaceKey.wasPressedThisFrame && _playback != null)
            {
                if (_playback.Paused) _playback.Resume();
                else _playback.Pause();
            }
            if (kb.rKey.wasPressedThisFrame) { _shown = _late = 0; _worstLate = 0; }
            if (kb.mKey.wasPressedThisFrame) ToggleMicrophone();
        }

        void ToggleMicrophone()
        {
            if (_mic != null)
            {
                _mic.Dispose();
                _mic = null;
                _micStatus = "off (M to start)";
                return;
            }
            try
            {
                _mic = new MicrophoneProbe(microphoneDevice);
                _micStatus = $"on: {_mic.Device ?? "default device"} at {_mic.SampleRate} Hz";
            }
            catch (Exception e)
            {
                _micStatus = $"failed: {e.Message}";
            }
        }

        // Visuals

        void BuildVisuals()
        {
            _camera = Camera.main;
            if (_camera != null)
            {
                _camera.orthographic = true;
                _camera.orthographicSize = 5;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = Background;
            }
            var white = Texture2D.whiteTexture;
            _square = Sprite.Create(white, new Rect(0, 0, white.width, white.height), new Vector2(0.5f, 0.5f), white.width);
            _bar = Sprite.Create(white, new Rect(0, 0, white.width, white.height), new Vector2(0.5f, 0f), white.width);

            var line = NewSprite("Hit line", _square, new Color(1, 1, 1, 0.35f));
            line.transform.position = new Vector3(0, HitLineY, 0);
            line.transform.localScale = new Vector3(9f, 0.04f, 1);

            for (int i = 0; i < 4; i++)
            {
                var bar = NewSprite($"Bar {i}", _bar, BandColors[i]);
                bar.transform.position = new Vector3(-7.5f + i * 0.6f, -4.5f, 0);
                _bars[i] = bar.transform;
                SetBar(i, 0);
            }
        }

        SpriteRenderer NewSprite(string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.color = color;
            return r;
        }

        void SetBar(int i, float value) => _bars[i].localScale = new Vector3(0.45f, 0.02f + 3.5f * value, 1);

        void Flash(int band, float x)
        {
            var r = NewSprite("Onset", _square, BandColors[band]);
            r.transform.position = new Vector3(x, HitLineY, 0);
            _flashes.Add((r, Time.unscaledTime));
        }

        void Animate()
        {
            const float life = 0.25f;
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                var (r, born) = _flashes[i];
                float age = (Time.unscaledTime - born) / life;
                if (age >= 1) { Destroy(r.gameObject); _flashes.RemoveAt(i); continue; }
                r.transform.localScale = Vector3.one * Mathf.Lerp(1.4f, 0.6f, age);
                var c = r.color;
                c.a = 1 - age;
                r.color = c;
            }
            _beat = Mathf.MoveTowards(_beat, 0, Time.unscaledDeltaTime * 6f);
            if (_camera != null) _camera.backgroundColor = Color.Lerp(Background, new Color(0.2f, 0.2f, 0.3f), _beat);
        }

        void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
            string timing;
            if (_file != null)
            {
                string pos = _playback.Position(AudioClock.Now) is double p ? $"{Clock(p)} / {Clock(_playback.Length)}" : "not started";
                string state = _playback.Error ?? (!_playback.Ready ? "loading" : !_file.Started ? "waiting for the sidecar"
                    : _playback.Paused ? "paused" : _playback.Finished ? "finished" : "playing");
                string ahead = _file.Headroom is double h ? $"{h * 1000:F0} ms (min {_file.MinHeadroom.Value * 1000:F0})" : "-";
                timing = $"file {Path.GetFileName(_playback.Path)}: {state}, {pos}  ·  Space pauses\n" +
                         $"output latency {_playback.Clock.OutputLatency * 1000:F0} ms (Up/Down ±10, Left/Right ±1)  ·  " +
                         $"analysis ahead {ahead}  ·  stale {_file.Stale}  ·  restarts {_file.Restarts}";
            }
            else
            {
                string baseText = _scheduler.Base is double b ? $"{b * 1000:F1} ms" : "waiting";
                timing = $"delay {_scheduler.DelaySeconds * 1000:F0} ms (Up/Down ±10, Left/Right ±1)  ·  base {baseText}";
            }
            string latePct = _shown > 0 ? $"{100.0 * _late / _shown:F2}%" : "-";
            string counters = _udp != null
                ? $"received {_udp.Received}  invalid {_udp.Invalid}  skipped {_udp.Skipped}  rejected {_udp.Rejected}"
                : "";
            string text =
                "Flight Record · experiment 06 · message bridge\n" +
                $"{_feed.Description}  ·  {_rate} messages/s  ·  {_fps:F0} fps\n" +
                (counters.Length > 0 ? counters + "\n" : "") +
                timing + "\n" +
                $"shown {_shown}  ·  arrived after due {_late} ({latePct})  ·  worst {_worstLate * 1000:F1} ms  ·  R resets\n" +
                $"{_track}\n{_side}\n{_end}\n" +
                $"microphone probe: {_micStatus}" + (_mic != null ? $", {_mic.Onsets} onsets" : "") + "\n" +
                $"log: {_logPath}";
            GUI.Label(new Rect(12, 8, Screen.width - 24, Screen.height - 16), text, _style);
        }

        void OnDestroy()
        {
            _feed?.Dispose();
            _playback?.Dispose();
            _mic?.Dispose();
            _log?.Dispose();
            _log = null;
        }
    }
}
