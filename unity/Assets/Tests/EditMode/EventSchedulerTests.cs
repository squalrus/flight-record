using System.Collections.Generic;
using NUnit.Framework;

namespace FlightRecord.Audio.Tests
{
    public class EventSchedulerTests
    {
        static ReceivedMessage At(double t, double received) =>
            new ReceivedMessage(
                Contract.Parse($"{{\"v\":1,\"type\":\"side_end\",\"t\":{t.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"cause\":\"x\"}}"),
                received);

        [Test]
        public void BaseIsTheFastestArrival()
        {
            var s = new EventScheduler(0.1);
            Assert.IsNull(s.Base);
            s.Add(At(1.00, 101.010)); // arrival 100.010
            s.Add(At(1.01, 101.015)); // 100.005: fastest
            s.Add(At(1.02, 101.050)); // 100.030: jittered
            Assert.AreEqual(100.005, s.Base.Value, 1e-9);
            Assert.AreEqual(1.02 + 100.005 + 0.1, s.DueTime(1.02).Value, 1e-9);
        }

        [Test]
        public void ReleasesInTimeOrderWhenDue()
        {
            var s = new EventScheduler(0.1);
            s.Add(At(1.00, 101.000));
            s.Add(At(1.02, 101.020));
            s.Add(At(1.01, 101.030)); // arrived out of order
            var out1 = new List<ScheduledMessage>();
            Assert.AreEqual(0, s.Release(101.099, out1));
            Assert.AreEqual(1, s.Release(101.1001, out1));
            Assert.AreEqual(2, s.Release(101.125, out1));
            CollectionAssert.AreEqual(new[] { 1.00, 1.01, 1.02 }, out1.ConvertAll(m => m.Message.T));
            Assert.AreEqual(0.0, out1[0].Late, 1e-3);
            Assert.AreEqual(0.015, out1[1].Late, 1e-6);
            Assert.AreEqual(0, s.Pending);
        }

        [Test]
        public void JitterSmallerThanTheDelayIsAbsorbed()
        {
            var s = new EventScheduler(0.05);
            var released = new List<ScheduledMessage>();
            double[] jitter = { 0, 0.03, 0.01, 0.045, 0, 0.02 };
            for (int i = 0; i < jitter.Length; i++)
            {
                double t = i * 0.01;
                s.Add(At(t, 10 + t + jitter[i]));
            }
            // Step the clock in 1 ms frames; nothing should ever be late by more than a frame.
            for (double now = 10; now < 10.2; now += 0.001) s.Release(now, released);
            Assert.AreEqual(jitter.Length, released.Count);
            foreach (var m in released) Assert.Less(m.Late, 0.0011);
        }

        [Test]
        public void BaseForgetsOldMinimumsAfterTheWindow()
        {
            var s = new EventScheduler(0) { WindowSeconds = 1.0 };
            s.Add(At(0.0, 100.000)); // arrival 100.000
            s.Add(At(0.5, 100.520)); // 100.020
            s.Add(At(2.0, 102.010)); // 100.010; the first two are now older than the window
            Assert.AreEqual(100.010, s.Base.Value, 1e-9);
        }

        [Test]
        public void AJumpBackInTimeIsANewStream()
        {
            var s = new EventScheduler(0);
            s.Add(At(50.0, 150.0));
            s.Add(At(0.0, 160.0)); // new file: t restarts
            Assert.AreEqual(1, s.Restarts);
            Assert.AreEqual(160.0, s.Base.Value, 1e-9);
            Assert.AreEqual(1, s.Pending);
        }
    }
}
