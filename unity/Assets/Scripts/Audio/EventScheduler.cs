using System;
using System.Collections.Generic;

namespace FlightRecord.Audio
{
    /// <summary>A message released by the <see cref="EventScheduler"/>.</summary>
    public readonly struct ScheduledMessage
    {
        public readonly ReceivedMessage Received;

        /// <summary>When it was due on <see cref="AudioClock"/>: t + base + delay.</summary>
        public readonly double Due;

        /// <summary>When it was actually released; later than <see cref="Due"/> if it arrived late.</summary>
        public readonly double Released;

        public ScheduledMessage(ReceivedMessage received, double due, double released)
        {
            Received = received;
            Due = due;
            Released = released;
        }

        public ContractMessage Message => Received.Message;

        /// <summary>Seconds past due when released (0 when on time, up to one frame).</summary>
        public double Late => Released - Due;
    }

    /// <summary>
    /// The game's delay buffer (architecture doc, latency model): turns each message's
    /// audio time <c>t</c> into a steady local time, then holds it until
    /// <c>t + base + delay</c>, so network and analysis jitter never reach the screen.
    ///
    /// <c>base</c> is the smallest (arrival time - t) seen in the last
    /// <see cref="WindowSeconds"/>: the fastest any message got here. It needs no clock
    /// sharing with the sender and adapts to slow drift between the audio clock and this
    /// one. A message that arrives later than base + delay is released at once, as late.
    ///
    /// Pure logic with explicit times, so it is unit-tested without Unity running.
    /// </summary>
    public sealed class EventScheduler
    {
        /// <summary>Seconds to hold messages past their earliest possible arrival.</summary>
        public double DelaySeconds { get; set; }

        /// <summary>How far back the base looks. Longer is steadier; shorter follows drift sooner.</summary>
        public double WindowSeconds { get; set; } = 10.0;

        /// <summary>A jump back in t larger than this means a new stream (a restart or a new file).</summary>
        public double RestartSeconds { get; set; } = 1.0;

        /// <summary>Smallest arrival - t in the window, or null before the first message.</summary>
        public double? Base => _mins.Count > 0 ? _mins.First.Value.Arrival : (double?)null;

        public int Pending => _pending.Count;
        public int Restarts { get; private set; }

        readonly LinkedList<(double Received, double Arrival)> _mins = new LinkedList<(double, double)>();
        readonly List<ReceivedMessage> _pending = new List<ReceivedMessage>();
        double _lastT = double.NegativeInfinity;

        public EventScheduler(double delaySeconds = 0.15) { DelaySeconds = delaySeconds; }

        public void Add(ReceivedMessage message)
        {
            double t = message.Message.T;
            if (t < _lastT - RestartSeconds) Reset(countRestart: true);
            _lastT = Math.Max(_lastT, t);

            // Windowed minimum with a monotonic queue: values increase from front to back.
            double arrival = message.Received - t;
            while (_mins.Count > 0 && _mins.Last.Value.Arrival >= arrival) _mins.RemoveLast();
            _mins.AddLast((message.Received, arrival));
            while (_mins.First.Value.Received < message.Received - WindowSeconds) _mins.RemoveFirst();

            // Keep pending sorted by t; messages almost always arrive in order.
            int i = _pending.Count;
            while (i > 0 && _pending[i - 1].Message.T > t) i--;
            _pending.Insert(i, message);
        }

        /// <summary>When a message with audio time <paramref name="t"/> is due, or null before any message.</summary>
        public double? DueTime(double t) => Base is double b ? t + b + DelaySeconds : (double?)null;

        /// <summary>Move every message due by <paramref name="now"/> into <paramref name="output"/>, in t order.</summary>
        public int Release(double now, List<ScheduledMessage> output)
        {
            if (!(Base is double b)) return 0;
            int count = 0;
            while (count < _pending.Count)
            {
                var m = _pending[count];
                double due = m.Message.T + b + DelaySeconds;
                if (due > now) break;
                output.Add(new ScheduledMessage(m, due, now));
                count++;
            }
            _pending.RemoveRange(0, count);
            return count;
        }

        public void Reset() => Reset(countRestart: false);

        void Reset(bool countRestart)
        {
            _mins.Clear();
            _pending.Clear();
            _lastT = double.NegativeInfinity;
            if (countRestart) Restarts++;
        }
    }
}
