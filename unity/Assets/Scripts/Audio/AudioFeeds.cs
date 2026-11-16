using System;
using System.Collections.Generic;

namespace FlightRecord.Audio
{
    /// <summary>
    /// What the game reads audio from: contract messages released when they are due, so the
    /// game acts on each one as its sound is heard. Every input looks the same through it:
    /// <list type="bullet">
    /// <item>live bridge, replay log, or nothing: a message source plus the delay buffer
    /// (<see cref="BufferedFeed"/>);</item>
    /// <item>an audio file the game plays itself, analyzed ahead by the sidecar (D-012):
    /// <see cref="FilePlaybackFeed"/>.</item>
    /// </list>
    /// Call <see cref="Update"/> once a frame on the main thread.
    /// </summary>
    public interface IAudioFeed : IDisposable
    {
        string Description { get; }
        void Start();

        /// <summary>Move every message due by <paramref name="now"/> into <paramref name="output"/>, in t order.</summary>
        int Update(double now, List<ScheduledMessage> output);
    }

    /// <summary>
    /// A message source held in the <see cref="EventScheduler"/> delay buffer: for sound the
    /// game does not play (live input heard through the speakers, a replayed log, or none).
    /// </summary>
    public sealed class BufferedFeed : IAudioFeed
    {
        public IMessageSource Source { get; }
        public EventScheduler Scheduler { get; }

        public BufferedFeed(IMessageSource source, EventScheduler scheduler = null)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Scheduler = scheduler ?? new EventScheduler();
        }

        public string Description => Source.Description;
        public void Start() => Source.Start();

        public int Update(double now, List<ScheduledMessage> output)
        {
            while (Source.TryRead(out var received)) Scheduler.Add(received);
            return Scheduler.Release(now, output);
        }

        public void Dispose() => Source.Dispose();
    }

    /// <summary>The game's own playback of an audio file (an <c>AudioSource</c> in the game; a fake in tests).</summary>
    public interface IPlayback
    {
        /// <summary>The file is loaded and can start.</summary>
        bool Ready { get; }

        /// <summary>Length of the file in seconds, once <see cref="Ready"/>.</summary>
        double Length { get; }

        /// <summary>Start playing now from <paramref name="fromSeconds"/> into the file.</summary>
        void Play(double fromSeconds);

        void Stop();

        /// <summary>Seconds into the file being heard at <paramref name="now"/>, or null before it starts.</summary>
        double? Position(double now);
    }

    /// <summary>
    /// An audio file the game plays itself while the sidecar analyzes the same file
    /// (<c>send.py --wav FILE</c>), decision D-012. The file's samples exist before they are
    /// heard, so analysis runs <see cref="LeadSeconds"/> ahead of playback and each message is
    /// released when playback reaches its <c>t</c>: screen and sound share the playback clock,
    /// with no delay buffer or calibration beyond the output latency.
    ///
    /// Sync: the first message fixes when the file's start is heard (its arrival - t + lead);
    /// playback starts then, or at once partway in if the sidecar started first. A jump back in
    /// t means the sidecar restarted, so playback restarts with it. If no message comes within
    /// <see cref="WaitSeconds"/> the file plays anyway (the game never waits on analysis), and a
    /// sidecar that joins late has its stale <c>features</c> dropped.
    ///
    /// Pure logic with explicit times, so it is unit-tested without Unity running.
    /// </summary>
    public sealed class FilePlaybackFeed : IAudioFeed
    {
        /// <summary>How far analysis runs ahead of what is heard.</summary>
        public double LeadSeconds { get; set; }

        /// <summary>How long a loaded file waits for the sidecar before playing without it.</summary>
        public double WaitSeconds { get; set; } = 3.0;

        /// <summary>A jump back in t larger than this means the sidecar restarted.</summary>
        public double RestartSeconds { get; set; } = 1.0;

        /// <summary>
        /// <c>features</c> that arrive more than this long after their sound was heard (a sidecar
        /// that joined late) are dropped, not released. A game hitch never drops on-time ones.
        /// </summary>
        public double StaleSeconds { get; set; } = 0.25;

        public IMessageSource Source { get; }
        public IPlayback Playback { get; }

        public bool Started { get; private set; }
        public int Pending => _pending.Count;
        public int Restarts { get; private set; }

        /// <summary><c>features</c> dropped because they arrived well after their sound was heard.</summary>
        public int Stale { get; private set; }

        /// <summary>Messages that arrived after their sound was heard (released at once).</summary>
        public int Late { get; private set; }

        /// <summary>
        /// How far ahead of playback the latest message arrived (t - position on arrival), or
        /// null before playback. Near <see cref="LeadSeconds"/> when in step; shrinking means the
        /// sidecar's clock is falling behind the sound card's.
        /// </summary>
        public double? Headroom { get; private set; }

        /// <summary>The smallest <see cref="Headroom"/> since playback started.</summary>
        public double? MinHeadroom { get; private set; }

        readonly List<ReceivedMessage> _pending = new List<ReceivedMessage>();
        double? _anchor; // when t = 0 is heard, on AudioClock
        double _lastT = double.NegativeInfinity;
        double? _readySince;

        public FilePlaybackFeed(IMessageSource source, IPlayback playback, double leadSeconds = 0.5)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Playback = playback ?? throw new ArgumentNullException(nameof(playback));
            LeadSeconds = leadSeconds;
        }

        public string Description => $"file playback, analysis from {Source.Description}";
        public void Start() => Source.Start();

        public int Update(double now, List<ScheduledMessage> output)
        {
            while (Source.TryRead(out var received)) Add(received, now);
            if (!Started) TryStart(now);
            if (!Started) return 0;
            if (!(Playback.Position(now) is double position)) return 0;

            int count = 0, released = 0;
            while (count < _pending.Count && _pending[count].Message.T <= position + 1e-6) count++;
            for (int i = 0; i < count; i++)
            {
                var m = _pending[i];
                double heard = now - (position - m.Message.T);
                if (m.Message is FeaturesMessage && m.Received - heard > StaleSeconds)
                {
                    Stale++;
                    continue;
                }
                if (m.Received > heard) Late++;
                output.Add(new ScheduledMessage(m, heard, now));
                released++;
            }
            _pending.RemoveRange(0, count);
            return released;
        }

        void Add(ReceivedMessage m, double now)
        {
            double t = m.Message.T;
            if (t < _lastT - RestartSeconds) Restart();
            _lastT = Math.Max(_lastT, t);
            _anchor ??= m.Received - t + LeadSeconds;

            if (Started && Playback.Position(now) is double position)
            {
                Headroom = t - position;
                MinHeadroom = MinHeadroom is double h ? Math.Min(h, Headroom.Value) : Headroom;
            }

            int i = _pending.Count;
            while (i > 0 && _pending[i - 1].Message.T > t) i--;
            _pending.Insert(i, m);
        }

        void TryStart(double now)
        {
            if (!Playback.Ready) return;
            _readySince ??= now;
            if (_anchor is double anchor)
            {
                if (now < anchor) return;
                double from = now - anchor;
                if (from >= Playback.Length) return; // the sidecar is past the end; wait for a restart
                Playback.Play(from);
                Started = true;
            }
            else if (now - _readySince.Value >= WaitSeconds)
            {
                _anchor = now; // no analysis yet: play from the start, and a late sidecar lines up by t
                Playback.Play(0);
                Started = true;
            }
        }

        void Restart()
        {
            Restarts++;
            Playback.Stop();
            Started = false;
            _pending.Clear();
            _anchor = null;
            _lastT = double.NegativeInfinity;
            Headroom = MinHeadroom = null;
        }

        public void Dispose()
        {
            Playback.Stop();
            Source.Dispose();
        }
    }
}
