using NUnit.Framework;

namespace FlightRecord.Audio.Tests
{
    public class PlaybackClockTests
    {
        /// <summary>A mixer that advances in 1024-sample buffers at 48 kHz, read at frame times.</summary>
        static double Reported(double trueSeconds) => System.Math.Floor(trueSeconds * 48000 / 1024) * 1024 / 48000;

        [Test]
        public void NullBeforeAnyReading()
        {
            Assert.IsNull(new PlaybackClock().Position(1.0));
        }

        [Test]
        public void SmoothsBufferStepsToTheTruePosition()
        {
            var clock = new PlaybackClock();
            double start = 50.0;
            // Frames every 11.7 ms for 2 s, playback started at 50.0 from 0.
            for (double now = start; now < start + 2; now += 0.0117)
                clock.Observe(now, true, Reported(now - start));
            double t = start + 2.0;
            double error = clock.Position(t).Value - (t - start);
            Assert.That(error, Is.LessThanOrEqualTo(1e-9));
            Assert.That(error, Is.GreaterThan(-0.003)); // a raw reading trails by up to 21 ms
        }

        [Test]
        public void OutputLatencyIsSubtracted()
        {
            var clock = new PlaybackClock { OutputLatency = 0.04 };
            clock.Observe(10.0, true, 5.0);
            Assert.AreEqual(5.06, clock.Position(10.1).Value, 1e-9);
        }

        [Test]
        public void PauseFreezesAndResumeFollows()
        {
            var clock = new PlaybackClock();
            clock.Observe(10.0, true, 5.0);
            clock.Observe(11.0, false, 6.0);
            Assert.AreEqual(6.0, clock.Position(20.0).Value, 1e-9);
            Assert.IsFalse(clock.Playing);
            clock.Observe(30.0, true, 6.0);
            Assert.AreEqual(6.5, clock.Position(30.5).Value, 1e-9);
        }

        [Test]
        public void ASeekStartsAgain()
        {
            var clock = new PlaybackClock();
            clock.Observe(10.0, true, 5.0);
            clock.Observe(10.5, true, 60.0); // jumped ahead
            Assert.AreEqual(60.5, clock.Position(11.0).Value, 1e-9);
            clock.Observe(11.0, true, 2.0); // and back
            Assert.AreEqual(2.0, clock.Position(11.0).Value, 1e-9);
        }
    }
}
