using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FlightRecord.Audio;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FlightRecord
{
    /// <summary>
    /// The game's audio input: owns the <see cref="IAudioFeed"/> while the game scene runs and
    /// hands each due contract message to the game (<see cref="Released"/>), with the latest
    /// state kept for polling (<see cref="Latest"/>, <see cref="BeatPulse"/>). Nothing in the
    /// game depends on it yet beyond a subtle background pulse; the level generator (backlog
    /// 4.4) builds on it. With no audio arriving the game plays exactly as before.
    ///
    /// Created by itself when the game scene loads, so no scene changes are needed, and kept
    /// across the scene's reloads between sectors so a file keeps playing; destroyed when
    /// another scene (menu, game over) loads. F3 toggles a debug overlay.
    ///
    /// Source, in order of precedence:
    /// <list type="bullet">
    /// <item>command line (player builds): <c>-audiofile PATH</c>, <c>-replay LOG.jsonl</c>,
    /// <c>-nullsource</c>, <c>-bridge</c>, plus <c>-port N</c>, <c>-delay MS</c>, <c>-lead MS</c>;</item>
    /// <item>the choice saved by the editor menu <b>Flight Record → Audio Source</b>
    /// (<see cref="GameAudioSettings"/>);</item>
    /// <item>the live bridge on UDP 9750 (the sidecar's <c>send.py</c>).</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class GameAudio : MonoBehaviour
    {
        public const string GameSceneName = "Main";
        const string OverlayPref = "FlightRecord.AudioOverlay";

        public static GameAudio Instance { get; private set; }

        /// <summary>1 on a beat, fading to 0 within a quarter second; 0 with no audio.</summary>
        public static float BeatPulse => Instance != null ? Instance._beat : 0f;

        /// <summary>A beat was heard in the last two seconds: <see cref="BeatPulse"/> is following music.</summary>
        public static bool HasBeat => Instance != null && AudioClock.Now - Instance._lastBeatAt < 2.0;

        /// <summary>Every contract message, as its sound is heard.</summary>
        public event Action<ScheduledMessage> Released;

        public IAudioFeed Feed { get; private set; }
        public FeaturesMessage Latest { get; private set; }
        public TrackIdentified Track { get; private set; }
        public SideState Side { get; private set; }
        public SideEnd End { get; private set; }
        public ReleaseSelected Release { get; private set; }

        /// <summary>The latest identification miss or error since the last match; null after a match.</summary>
        public IdentificationFailed Unidentified { get; private set; }

        /// <summary>Tags and cover art read from the file the game plays; null for other sources.</summary>
        public Id3Tags FileTags { get; private set; }

        /// <summary>The game's own playback of a file; null for other sources.</summary>
        public AudioSourcePlayback Playback => _playback;

        /// <summary>When the last message was released, on <see cref="AudioClock"/>; null before any.</summary>
        public double? LastReleased { get; private set; }

        public bool OverlayVisible { get; set; }

        GameAudioSettings _settings;
        UdpMessageSource _udp;
        EventScheduler _scheduler;
        FilePlaybackFeed _file;
        AudioSourcePlayback _playback;
        bool _pausedByGame;
        readonly List<ScheduledMessage> _released = new List<ScheduledMessage>();

        float _beat;
        readonly float[] _onsetFlash = new float[3];
        int _released1s, _rate, _total;
        double _rateStart;
        double _lastT, _lastTAt = double.NegativeInfinity, _lastBeatAt = double.NegativeInfinity;
        string _error;
        GUIStyle _style, _small;

        // ---- Lifetime ----

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            if (scene.name == GameSceneName)
            {
                if (Instance == null) new GameObject("Game Audio").AddComponent<GameAudio>();
            }
            else if (Instance != null)
            {
                Destroy(Instance.gameObject);
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true; // keep receiving while the sidecar's terminal has focus
            OverlayVisible = PlayerPrefs.GetInt(OverlayPref, 0) == 1;

            _settings = GameAudioSettings.Resolve(Environment.GetCommandLineArgs());
            try
            {
                Feed = CreateFeed(_settings);
                Feed.Start();
            }
            catch (Exception e)
            {
                // A busy port or a bad path must never stop the game: carry on without audio.
                _error = e.Message;
                Debug.LogWarning($"[GameAudio] {_settings.Describe()} unavailable, playing without audio: {e.Message}");
                Feed?.Dispose();
                _udp = null;
                _file = null;
                _playback = null;
                Feed = new BufferedFeed(new NullMessageSource());
            }
            _rateStart = AudioClock.Now;
        }

        IAudioFeed CreateFeed(GameAudioSettings s)
        {
            switch (s.Mode)
            {
                case GameAudioSettings.SourceMode.None:
                    return new BufferedFeed(new NullMessageSource());
                case GameAudioSettings.SourceMode.Replay:
                    return new BufferedFeed(new ReplayMessageSource(s.Path), _scheduler = new EventScheduler(s.DelaySeconds));
                case GameAudioSettings.SourceMode.File:
                    FileTags = Id3Tags.Read(s.Path);
                    _udp = new UdpMessageSource(s.Port);
                    _playback = new AudioSourcePlayback(gameObject.AddComponent<AudioSource>(), s.Path);
                    return _file = new FilePlaybackFeed(_udp, _playback, s.LeadSeconds);
                default:
                    _udp = new UdpMessageSource(s.Port);
                    return new BufferedFeed(_udp, _scheduler = new EventScheduler(s.DelaySeconds));
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Feed?.Dispose();
            _playback?.Dispose();
        }

        // ---- Per frame ----

        void Update()
        {
            double now = AudioClock.Now;
            FollowGamePause();

            _released.Clear();
            Feed.Update(now, _released);
            foreach (var m in _released) Handle(m, now);

            if (now - _rateStart >= 1.0)
            {
                _rate = _released1s;
                _released1s = 0;
                _rateStart = now;
            }

            float dt = Time.unscaledDeltaTime;
            _beat = Mathf.MoveTowards(_beat, 0f, dt * 4f);
            for (int i = 0; i < _onsetFlash.Length; i++) _onsetFlash[i] = Mathf.MoveTowards(_onsetFlash[i], 0f, dt * 5f);

            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame)
            {
                OverlayVisible = !OverlayVisible;
                PlayerPrefs.SetInt(OverlayPref, OverlayVisible ? 1 : 0);
            }
        }

        /// <summary>The pause menu stops time; a file the game plays stops with it.</summary>
        void FollowGamePause()
        {
            if (_playback == null) return;
            bool paused = Time.timeScale == 0f;
            if (paused && !_playback.Paused && _file.Started) { _playback.Pause(); _pausedByGame = true; }
            else if (!paused && _pausedByGame) { _playback.Resume(); _pausedByGame = false; }
        }

        void Handle(ScheduledMessage s, double now)
        {
            _released1s++;
            _total++;
            LastReleased = now;
            _lastT = s.Message.T;
            _lastTAt = now;
            switch (s.Message)
            {
                case FeaturesMessage f:
                    Latest = f;
                    if (f.Beat) { _beat = 1f; _lastBeatAt = now; }
                    if (f.Onset)
                    {
                        bool any = f.OnsetLow || f.OnsetMid || f.OnsetHigh;
                        if (f.OnsetLow) _onsetFlash[0] = 1f;
                        if (f.OnsetMid || !any) _onsetFlash[1] = 1f;
                        if (f.OnsetHigh) _onsetFlash[2] = 1f;
                    }
                    break;
                case TrackIdentified t:
                    if (Track == null || !SameTrack(Track, t)) Release = null; // a new track may be on another record
                    Track = t;
                    Unidentified = null;
                    break;
                case ReleaseSelected r: Release = r; break;
                case IdentificationFailed miss: if (Track == null) Unidentified = miss; break;
                case SideState side: Side = side; End = null; break;
                case SideEnd end: End = end; break;
            }
            Released?.Invoke(s);
        }

        static bool SameTrack(TrackIdentified a, TrackIdentified b) =>
            a.RecordingId != null && b.RecordingId != null
                ? a.RecordingId == b.RecordingId
                : string.Equals(a.Artist, b.Artist, StringComparison.OrdinalIgnoreCase)
                  && string.Equals(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Seconds into the audio stream being heard now (the contract's <c>t</c>), or null with
        /// no audio. A file the game plays gives its playback position; otherwise the latest
        /// message's t, carried forward for up to a second while messages keep coming.
        /// </summary>
        public double? AudioTime(double now)
        {
            if (_playback != null && _file.Started) return _playback.Position(now);
            if (LastReleased == null) return null;
            return _lastT + Math.Min(now - _lastTAt, 1.0);
        }

        // ---- Debug overlay (F3) ----

        static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.6f);
        static readonly Color[] BandColors =
        {
            new Color(1f, 0.35f, 0.3f), new Color(0.4f, 1f, 0.5f), new Color(0.4f, 0.6f, 1f), Color.white,
        };

        void OnGUI()
        {
            if (!OverlayVisible) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, normal = { textColor = Color.white } };
            _small ??= new GUIStyle(_style) { fontSize = 12 };

            const float width = 380f, pad = 10f;
            float height = 330f;
            var panel = new Rect(Screen.width - width - 12f, 60f, width, height);
            Fill(panel, PanelColor);
            float x = panel.x + pad, y = panel.y + pad, w = width - 2 * pad;

            Line(ref y, x, w, "<b>Audio</b>  <color=#999>(F3 hides)</color>", _style);
            Line(ref y, x, w, $"{_settings.Describe()}", _small);
            Line(ref y, x, w, Status(), _small);
            Line(ref y, x, w, $"{_rate} msg/s  ·  {_total} total  ·  {Timing()}", _small);
            y += 4;

            var f = Latest;
            string tempo = f?.Tempo is float bpm && bpm > 0 ? $"{bpm:F0} BPM ({f.TempoConfidence:P0})" : "tempo -";
            string key = f?.Key != null ? $"{f.Key} ({f.KeyConfidence:P0})" : "key -";
            Line(ref y, x, w, $"{tempo}  ·  {key}", _style);

            // Bars: bass, mid, treble, loudness.
            string[] labels = { "bass", "mid", "treble", "rms" };
            float[] values = { f?.Bass ?? 0, f?.Mid ?? 0, f?.Treble ?? 0, f?.Rms ?? 0 };
            for (int i = 0; i < 4; i++)
            {
                GUI.Label(new Rect(x, y, 60, 18), labels[i], _small);
                var track = new Rect(x + 60, y + 4, w - 60, 10);
                Fill(track, new Color(1, 1, 1, 0.1f));
                Fill(new Rect(track.x, track.y, track.width * Mathf.Clamp01(values[i]), track.height), BandColors[i]);
                y += 18;
            }
            y += 4;

            // Onset lights (low, mid, high) and the beat.
            string[] lights = { "low", "mid", "high" };
            for (int i = 0; i < 3; i++)
            {
                var c = BandColors[i];
                c.a = 0.15f + 0.85f * _onsetFlash[i];
                Fill(new Rect(x + i * 70, y, 16, 16), c);
                GUI.Label(new Rect(x + i * 70 + 20, y - 1, 50, 18), lights[i], _small);
            }
            Fill(new Rect(x + 220, y, 16, 16), new Color(1, 1, 1, 0.15f + 0.85f * _beat));
            GUI.Label(new Rect(x + 240, y - 1, 60, 18), "beat", _small);
            y += 24;

            string track1 = Track != null ? $"{Track.Artist} - {Track.Title}" : "track: unidentified";
            Line(ref y, x, w, track1, _small);
            string side = Side == null ? "side: unknown"
                : $"side {Side.Side}: {Clock(Side.SideElapsed)} / " + (Side.SideExpected is double e ? Clock(e) : "?");
            if (End != null) side += $"  ·  <b>SIDE END</b> ({End.Cause})";
            Line(ref y, x, w, side, _small);

            if (_udp != null)
                Line(ref y, x, w, $"udp {_udp.Received} in  ·  invalid {_udp.Invalid}  ·  skipped {_udp.Skipped}  ·  rejected {_udp.Rejected}", _small);
            if (_file != null)
                Line(ref y, x, w, $"late {_file.Late}  ·  stale {_file.Stale}  ·  restarts {_file.Restarts}", _small);
        }

        string Status()
        {
            if (_error != null) return $"<color=#f88>unavailable: {_error}</color>";
            if (_playback != null)
            {
                if (_playback.Error != null) return $"<color=#f88>{_playback.Error}</color>";
                if (!_playback.Ready) return "loading file…";
                if (!_file.Started) return "waiting for send.py --wav (plays anyway after 3 s)";
                string pos = _playback.Position(AudioClock.Now) is double p ? $"{Clock(p)} / {Clock(_playback.Length)}" : "";
                string state = _playback.Paused ? "paused" : _playback.Finished ? "finished" : "playing";
                return $"{state} {Path.GetFileName(_playback.Path)}  {pos}";
            }
            if (_settings.Mode == GameAudioSettings.SourceMode.None) return "no audio (game runs as before)";
            if (LastReleased == null) return _udp != null ? "waiting for send.py on the bridge" : "waiting for messages";
            double age = AudioClock.Now - LastReleased.Value;
            return age > 1.0 ? $"<color=#fc6>no messages for {age:F0} s</color>" : "receiving";
        }

        string Timing()
        {
            if (_file != null)
                return _file.Headroom is double h ? $"ahead {h * 1000:F0} ms (min {_file.MinHeadroom.Value * 1000:F0})" : "ahead -";
            if (_scheduler != null)
                return $"delay {_scheduler.DelaySeconds * 1000:F0} ms";
            return "";
        }

        static void Line(ref float y, float x, float w, string text, GUIStyle style)
        {
            GUI.Label(new Rect(x, y, w, 20), text, style);
            y += style.fontSize + 6;
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static string Clock(double seconds) => $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";
    }

    /// <summary>
    /// Which audio source the game uses. Player builds take it from the command line; in the
    /// editor (where there is none) the menu <b>Flight Record → Audio Source</b> saves it in
    /// PlayerPrefs. Default: the live bridge.
    /// </summary>
    public sealed class GameAudioSettings
    {
        public enum SourceMode { Bridge, File, Replay, None }

        public const string ModePref = "FlightRecord.AudioSource.Mode";
        public const string PathPref = "FlightRecord.AudioSource.Path";

        public SourceMode Mode = SourceMode.Bridge;
        public string Path;
        public int Port = UdpMessageSource.DefaultPort;
        public double DelaySeconds = 0.15;
        public double LeadSeconds = 0.5;

        public string Describe()
        {
            switch (Mode)
            {
                case SourceMode.File: return $"file: {System.IO.Path.GetFileName(Path)} (analysis from UDP {Port})";
                case SourceMode.Replay: return $"replay: {System.IO.Path.GetFileName(Path)}";
                case SourceMode.None: return "source: none";
                default: return $"live bridge: UDP 127.0.0.1:{Port}";
            }
        }

        public static GameAudioSettings Resolve(string[] args)
        {
            var s = new GameAudioSettings();
            if (Enum.TryParse(PlayerPrefs.GetString(ModePref, nameof(SourceMode.Bridge)), out SourceMode saved)) s.Mode = saved;
            s.Path = PlayerPrefs.GetString(PathPref, "");

            var c = CultureInfo.InvariantCulture;
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "-audiofile": s.Mode = SourceMode.File; s.Path = next; break;
                    case "-replay": s.Mode = SourceMode.Replay; s.Path = next; break;
                    case "-nullsource": s.Mode = SourceMode.None; break;
                    case "-bridge": s.Mode = SourceMode.Bridge; break;
                    case "-port": s.Port = int.Parse(next, c); break;
                    case "-delay": s.DelaySeconds = double.Parse(next, c) / 1000; break;
                    case "-lead": s.LeadSeconds = double.Parse(next, c) / 1000; break;
                }
            }
            if ((s.Mode == SourceMode.File || s.Mode == SourceMode.Replay) && string.IsNullOrWhiteSpace(s.Path))
                s.Mode = SourceMode.Bridge;
            s.Path = s.Path?.Trim().Trim('"');
            return s;
        }
    }
}
