using System;
using System.Collections.Generic;

namespace FlightRecord.Audio
{
    /// <summary>
    /// A smooth playback position from the coarse one an audio engine reports. Unity's
    /// <c>AudioSource.timeSamples</c> moves in steps of one mixer buffer (about 10-20 ms) and
    /// is read once a frame, so it always trails the true position by up to a buffer plus a
    /// frame. Like the delay buffer's base, the best estimate is the largest
    /// (reported - now) seen recently: the reading that trailed least. Position at any moment
    /// is then now + that offset - <see cref="OutputLatency"/> (mixer to speaker).
    ///
    /// A pause freezes the position; a resume, seek, or restart (a reading far from the
    /// estimate) starts the window again. Pure logic with explicit times, so it is unit-tested
    /// without Unity running.
    /// </summary>
    public sealed class PlaybackClock
    {
        /// <summary>Seconds from the mixer's position to the sound leaving the speakers.</summary>
        public double OutputLatency { get; set; }

        /// <summary>How far back the best reading is kept.</summary>
        public double WindowSeconds { get; set; } = 2.0;

        /// <summary>A reading further than this from the estimate is a seek or restart.</summary>
        public double JumpSeconds { get; set; } = 0.1;

        public bool Playing { get; private set; }

        readonly LinkedList<(double At, double Offset)> _max = new LinkedList<(double, double)>();
        double? _paused; // reported position while not playing

        /// <summary>Feed one reading: the engine's position in seconds, and whether it is playing.</summary>
        public void Observe(double now, bool playing, double reported)
        {
            if (!playing)
            {
                Playing = false;
                _paused = reported;
                _max.Clear();
                return;
            }
            Playing = true;
            _paused = null;
            double offset = reported - now;
            if (_max.Count > 0 && Math.Abs(offset - _max.First.Value.Offset) > JumpSeconds) _max.Clear();

            // Windowed maximum with a monotonic queue: values decrease from front to back.
            while (_max.Count > 0 && _max.Last.Value.Offset <= offset) _max.RemoveLast();
            _max.AddLast((now, offset));
            while (_max.First.Value.At < now - WindowSeconds) _max.RemoveFirst();
        }

        /// <summary>Seconds into the file heard at <paramref name="now"/>, or null before any reading.</summary>
        public double? Position(double now)
        {
            if (_paused is double p) return Math.Max(0, p - OutputLatency);
            if (_max.Count == 0) return null;
            return Math.Max(0, now + _max.First.Value.Offset - OutputLatency);
        }

        public void Reset()
        {
            _max.Clear();
            _paused = null;
            Playing = false;
        }
    }
}
