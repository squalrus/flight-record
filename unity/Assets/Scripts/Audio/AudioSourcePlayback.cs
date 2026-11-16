using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace FlightRecord.Audio
{
    /// <summary>
    /// <see cref="IPlayback"/> on a Unity <see cref="AudioSource"/>: loads an audio file the
    /// player owns (WAV, OGG, or MP3; Unity cannot decode FLAC at runtime) and plays it, so the
    /// music mixes with the game's sound effects and follows its volume and pause (D-012).
    /// The position comes from a <see cref="PlaybackClock"/> over <c>timeSamples</c>.
    /// Main thread only.
    /// </summary>
    public sealed class AudioSourcePlayback : IPlayback, IDisposable
    {
        public string Path { get; }
        public AudioSource Source { get; }
        public PlaybackClock Clock { get; } = new PlaybackClock();

        /// <summary>Why loading failed, or null.</summary>
        public string Error { get; private set; }

        public bool Paused { get; private set; }
        public bool Finished { get; private set; }

        UnityWebRequest _request;
        AudioClip _clip;
        bool _started;

        public AudioSourcePlayback(AudioSource source, string path)
        {
            Source = source ? source : throw new ArgumentNullException(nameof(source));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Source.playOnAwake = false;
            Source.loop = false;
            Clock.OutputLatency = DefaultOutputLatency();

            var type = AudioTypeFor(path);
            if (type == AudioType.UNKNOWN)
            {
                Error = $"{System.IO.Path.GetExtension(path)} files cannot be played (use WAV, OGG, or MP3)";
                return;
            }
            if (!File.Exists(path))
            {
                Error = $"file not found: {path}";
                return;
            }
            _request = UnityWebRequestMultimedia.GetAudioClip(new Uri(System.IO.Path.GetFullPath(path)).AbsoluteUri, type);
            if (_request.downloadHandler is DownloadHandlerAudioClip handler)
            {
                handler.streamAudio = false; // decode it all up front: seeking and timeSamples stay exact
                handler.compressed = false;
            }
            _request.SendWebRequest();
        }

        public static AudioType AudioTypeFor(string path)
        {
            switch (System.IO.Path.GetExtension(path).ToLowerInvariant())
            {
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".mp3": return AudioType.MPEG;
                default: return AudioType.UNKNOWN;
            }
        }

        /// <summary>The mixer's buffering (buffer size x buffer count); device latency is extra, set by ear.</summary>
        public static double DefaultOutputLatency()
        {
            AudioSettings.GetDSPBufferSize(out int length, out int count);
            int rate = AudioSettings.outputSampleRate;
            return rate > 0 ? (double)length * count / rate : 0;
        }

        public bool Ready
        {
            get
            {
                if (_clip != null) return true;
                if (_request == null || !_request.isDone) return false;
                if (_request.result != UnityWebRequest.Result.Success)
                    Error = $"could not load {System.IO.Path.GetFileName(Path)}: {_request.error}";
                else
                {
                    _clip = DownloadHandlerAudioClip.GetContent(_request);
                    _clip.name = System.IO.Path.GetFileNameWithoutExtension(Path);
                    Source.clip = _clip;
                }
                _request.Dispose();
                _request = null;
                return _clip != null;
            }
        }

        public double Length => _clip != null ? _clip.length : 0;

        public void Play(double fromSeconds)
        {
            if (!Ready) return;
            Clock.Reset();
            Source.timeSamples = Math.Min(_clip.samples - 1, Math.Max(0, (int)(fromSeconds * _clip.frequency)));
            Source.Play();
            _started = true;
            Paused = Finished = false;
        }

        public void Stop()
        {
            if (Source) Source.Stop();
            Clock.Reset();
            _started = Paused = Finished = false;
        }

        public void Pause()
        {
            if (!_started || Finished) return;
            Source.Pause();
            Paused = true;
        }

        public void Resume()
        {
            if (!Paused) return;
            Source.UnPause();
            Paused = false;
        }

        public double? Position(double now)
        {
            if (!_started || _clip == null) return null;
            if (!Finished && !Paused && !Source.isPlaying) Finished = true; // ran off the end
            double reported = Finished ? _clip.length + Clock.OutputLatency : (double)Source.timeSamples / _clip.frequency;
            Clock.Observe(now, Source.isPlaying && !Paused && !Finished, reported);
            return Clock.Position(now);
        }

        public void Dispose()
        {
            Stop();
            _request?.Dispose();
            _request = null;
            if (_clip != null) UnityEngine.Object.Destroy(_clip);
            _clip = null;
        }
    }
}
