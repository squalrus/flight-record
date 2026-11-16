using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace FlightRecord.Audio.Tests
{
    public class FilePlaybackFeedTests
    {
        /// <summary>Messages handed in by the test, as if they had just arrived.</summary>
        sealed class FakeSource : IMessageSource
        {
            public readonly Queue<ReceivedMessage> Queue = new Queue<ReceivedMessage>();
            public string Description => "fake";
            public void Start() { }
            public bool TryRead(out ReceivedMessage m) => Queue.TryDequeue(out m);
            public void Dispose() { }
        }

        /// <summary>Playback whose position follows the clock exactly from where it started.</summary>
        sealed class FakePlayback : IPlayback
        {
            public bool Ready { get; set; } = true;
            public double Length { get; set; } = 180;
            public double? StartedAt, From;
            public int Plays, Stops;
            public double Now; // set by the test before each Update

            public void Play(double fromSeconds) { StartedAt = Now; From = fromSeconds; Plays++; }
            public void Stop() { StartedAt = null; Stops++; }
            public double? Position(double now) =>
                StartedAt is double s ? System.Math.Min(Length, From.Value + now - s) : (double?)null;
        }

        static string F(double x) => x.ToString("R", CultureInfo.InvariantCulture);

        static ReceivedMessage Features(double t, double received) => new ReceivedMessage(
            Contract.Parse($"{{\"v\":1,\"type\":\"features\",\"t\":{F(t)},\"onset\":false,\"onset_strength\":0," +
                           "\"bass\":0,\"mid\":0,\"treble\":0,\"rms\":0,\"tempo\":null,\"tempo_confidence\":0}"),
            received);

        static ReceivedMessage End(double t, double received) => new ReceivedMessage(
            Contract.Parse($"{{\"v\":1,\"type\":\"side_end\",\"t\":{F(t)},\"cause\":\"file_end\"}}"), received);

        (FilePlaybackFeed Feed, FakeSource Source, FakePlayback Playback) Make(double lead = 0.5)
        {
            var source = new FakeSource();
            var playback = new FakePlayback();
            var feed = new FilePlaybackFeed(source, playback, lead);
            feed.Start();
            return (feed, source, playback);
        }

        static int Step(FilePlaybackFeed feed, FakePlayback playback, double now, List<ScheduledMessage> output)
        {
            playback.Now = now;
            return feed.Update(now, output);
        }

        [Test]
        public void PlaybackStartsALeadAfterTheFirstMessage()
        {
            var (feed, source, playback) = Make(lead: 0.5);
            var output = new List<ScheduledMessage>();
            source.Queue.Enqueue(Features(0.05, 100.06)); // t = 0 heard at 100.01 + 0.5
            Step(feed, playback, 100.06, output);
            Assert.IsFalse(feed.Started);
            Step(feed, playback, 100.50, output);
            Assert.IsFalse(feed.Started);
            Step(feed, playback, 100.52, output);
            Assert.IsTrue(feed.Started);
            Assert.AreEqual(0.01, playback.From.Value, 1e-9); // one frame late: starts 10 ms in
        }

        [Test]
        public void MessagesAreReleasedWhenPlaybackReachesThem()
        {
            var (feed, source, playback) = Make(lead: 0.5);
            var output = new List<ScheduledMessage>();
            for (int i = 0; i < 100; i++) source.Queue.Enqueue(Features(i * 0.01, 10 + i * 0.01));
            Step(feed, playback, 10.5, output); // starts at position 0
            Assert.AreEqual(1, output.Count); // t = 0 only
            output.Clear();
            Step(feed, playback, 10.8, output);
            Assert.AreEqual(30, output.Count); // t = 0.01 .. 0.30
            Assert.AreEqual(0.30, output[29].Message.T, 1e-9);
            Assert.AreEqual(10.8, output[29].Due, 1e-9); // heard now
            Assert.AreEqual(10.79, output[28].Due, 1e-9);
            Assert.AreEqual(0, feed.Late);
            Assert.AreEqual(69, feed.Pending);
        }

        [Test]
        public void SidecarStartedFirstMeansStartingPartwayIn()
        {
            var (feed, source, playback) = Make(lead: 0.5);
            var output = new List<ScheduledMessage>();
            source.Queue.Enqueue(Features(20.0, 100.0)); // the game joined 20 s into the sidecar's run
            Step(feed, playback, 100.0, output);
            Step(feed, playback, 100.5, output);
            Assert.IsTrue(feed.Started);
            Assert.AreEqual(19.5, playback.From.Value, 1e-9);
        }

        [Test]
        public void NoSidecarStillPlaysAfterWaiting()
        {
            var (feed, source, playback) = Make();
            feed.WaitSeconds = 3;
            var output = new List<ScheduledMessage>();
            Step(feed, playback, 5.0, output);
            Step(feed, playback, 7.9, output);
            Assert.IsFalse(feed.Started);
            Step(feed, playback, 8.0, output);
            Assert.IsTrue(feed.Started);
            Assert.AreEqual(0, playback.From.Value);

            // A sidecar joining now starts from t = 0: its features are stale, its side_end is not.
            source.Queue.Enqueue(Features(0.05, 18.0));
            source.Queue.Enqueue(End(0.06, 18.0));
            Step(feed, playback, 18.0, output);
            Assert.AreEqual(1, output.Count);
            Assert.IsInstanceOf<SideEnd>(output[0].Message);
            Assert.AreEqual(1, feed.Stale);
        }

        [Test]
        public void ASidecarRestartRestartsPlayback()
        {
            var (feed, source, playback) = Make(lead: 0.5);
            var output = new List<ScheduledMessage>();
            source.Queue.Enqueue(Features(30.0, 100.0));
            Step(feed, playback, 100.5, output);
            Assert.AreEqual(1, playback.Plays);

            source.Queue.Enqueue(Features(0.05, 200.0)); // send.py run again
            Step(feed, playback, 200.0, output);
            Assert.AreEqual(1, feed.Restarts);
            Assert.AreEqual(1, playback.Stops);
            Assert.IsFalse(feed.Started);
            Step(feed, playback, 200.5, output);
            Assert.AreEqual(2, playback.Plays);
            Assert.AreEqual(0.05, playback.From.Value, 1e-9);
        }

        [Test]
        public void HeadroomShowsHowFarAheadAnalysisRuns()
        {
            var (feed, source, playback) = Make(lead: 0.5);
            var output = new List<ScheduledMessage>();
            source.Queue.Enqueue(Features(0.0, 10.0));
            Step(feed, playback, 10.5, output);
            source.Queue.Enqueue(Features(1.5, 11.5)); // position 1.0
            Step(feed, playback, 11.5, output);
            Assert.AreEqual(0.5, feed.Headroom.Value, 1e-9);
            source.Queue.Enqueue(Features(2.1, 12.4)); // position 1.9: the sidecar slipped 300 ms
            Step(feed, playback, 12.4, output);
            Assert.AreEqual(0.2, feed.Headroom.Value, 1e-9);
            Assert.AreEqual(0.2, feed.MinHeadroom.Value, 1e-9);
        }

        [Test]
        public void ALateMessageIsReleasedAtOnceAndCounted()
        {
            var (feed, source, playback) = Make(lead: 0.5);
            var output = new List<ScheduledMessage>();
            source.Queue.Enqueue(Features(0.0, 10.0));
            Step(feed, playback, 10.5, output);
            output.Clear();
            source.Queue.Enqueue(Features(1.0, 11.6)); // heard at 11.5, arrived 100 ms after
            Step(feed, playback, 11.6, output);
            Assert.AreEqual(1, output.Count);
            Assert.AreEqual(1, feed.Late);
            Assert.AreEqual(0.1, output[0].Late, 1e-9);
        }

        [Test]
        public void AGameHitchDropsNothing()
        {
            var (feed, source, playback) = Make(lead: 0.5);
            var output = new List<ScheduledMessage>();
            source.Queue.Enqueue(Features(0.0, 10.0));
            Step(feed, playback, 10.5, output);
            for (int i = 1; i <= 50; i++) source.Queue.Enqueue(Features(i * 0.01, 10.5 + i * 0.01 - 0.5));
            Step(feed, playback, 11.0, output); // half a second without a frame
            Assert.AreEqual(51, output.Count);
            Assert.AreEqual(0, feed.Stale);
            Assert.AreEqual(0, feed.Late);
        }

        [Test]
        public void NothingStartsBeforeTheFileLoads()
        {
            var (feed, source, playback) = Make(lead: 0.5);
            playback.Ready = false;
            var output = new List<ScheduledMessage>();
            source.Queue.Enqueue(Features(0.0, 10.0));
            Step(feed, playback, 11.0, output);
            Assert.IsFalse(feed.Started);
            playback.Ready = true;
            Step(feed, playback, 11.2, output);
            Assert.AreEqual(0.7, playback.From.Value, 1e-9); // kept in step with the sidecar
        }

        [Test]
        public void BufferedFeedHoldsInTheDelayBuffer()
        {
            var source = new FakeSource();
            var feed = new BufferedFeed(source, new EventScheduler(0.1));
            var output = new List<ScheduledMessage>();
            source.Queue.Enqueue(End(1.0, 101.0));
            Assert.AreEqual(0, feed.Update(101.05, output));
            Assert.AreEqual(1, feed.Update(101.11, output));
        }
    }
}
