using FlightRecord.Gameplay;
using NUnit.Framework;

namespace FlightRecord.Gameplay.Tests
{
    public class BulletPatternTests
    {
        private const float Tol = 1e-3f;
        private readonly float[] _angles = new float[BulletPatternMath.MaxShotsPerVolley];

        [Test]
        public void AimedFiresOneShotAlongTheAim()
        {
            var shape = new PatternShape(PatternKind.Aimed, count: 5, arc: 90f);
            Assert.AreEqual(1, BulletPatternMath.VolleyAngles(shape, 180f, 0, _angles));
            Assert.AreEqual(180f, _angles[0], Tol);
        }

        [Test]
        public void SpreadFansEvenlyAcrossTheArcCenteredOnTheAim()
        {
            var shape = new PatternShape(PatternKind.Spread, count: 5, arc: 60f);
            Assert.AreEqual(5, BulletPatternMath.VolleyAngles(shape, 180f, 0, _angles));
            CollectionAssert.AreEqual(new[] { 150f, 165f, 180f, 195f, 210f }, Slice(5), new FloatComparer());
        }

        [Test]
        public void SpreadOfOneIsAimed()
        {
            var shape = new PatternShape(PatternKind.Spread, count: 1, arc: 60f);
            Assert.AreEqual(1, BulletPatternMath.VolleyAngles(shape, 90f, 0, _angles));
            Assert.AreEqual(90f, _angles[0], Tol);
        }

        [Test]
        public void RingSpacesShotsAroundTheCircleFromTheAim()
        {
            var shape = new PatternShape(PatternKind.Ring, count: 4, arc: 0f, angleOffset: 45f);
            Assert.AreEqual(4, BulletPatternMath.VolleyAngles(shape, 0f, 0, _angles));
            CollectionAssert.AreEqual(new[] { 45f, 135f, 225f, 315f }, Slice(4), new FloatComparer());
        }

        [Test]
        public void VolleySpinTurnsEachVolley()
        {
            var shape = new PatternShape(PatternKind.Ring, count: 3, arc: 0f, volleySpin: 10f);
            BulletPatternMath.VolleyAngles(shape, 0f, 2, _angles);
            Assert.AreEqual(20f, _angles[0], Tol);
            Assert.AreEqual(140f, _angles[1], Tol);
        }

        [Test]
        public void StreamStartsCenteredAndBouncesBetweenTheArcEdges()
        {
            var shape = new PatternShape(PatternKind.Stream, count: 9, arc: 40f, sweepStep: 10f);
            var expected = new[] { 0f, 10f, 20f, 10f, 0f, -10f, -20f, -10f, 0f, 10f };
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(1, BulletPatternMath.VolleyAngles(shape, 180f, i, _angles));
                Assert.AreEqual(180f + expected[i], _angles[0], Tol, $"shot {i}");
            }
        }

        [Test]
        public void StreamWithNoArcIsAimed()
        {
            Assert.AreEqual(0f, BulletPatternMath.SweepOffset(0f, 15f, 7), Tol);
        }

        [Test]
        public void NegativeSweepStepSweepsTheOtherWay()
        {
            Assert.AreEqual(-10f, BulletPatternMath.SweepOffset(40f, -10f, 1), Tol);
        }

        [Test]
        public void CountIsClampedToTheBuffer()
        {
            var shape = new PatternShape(PatternKind.Ring, count: 1000, arc: 0f);
            Assert.AreEqual(BulletPatternMath.MaxShotsPerVolley, shape.Count);
            Assert.AreEqual(3, BulletPatternMath.VolleyAngles(shape, 0f, 0, new float[3]));
        }

        [Test]
        public void AutoFireWaitsOneIntervalThenFiresEachInterval()
        {
            var clock = new PatternClock(interval: 2f, volleysPerBurst: 1, volleySpacing: 0f);
            Assert.AreEqual(0, clock.Advance(1.9f));
            Assert.AreEqual(1, clock.Advance(0.2f));
            Assert.AreEqual(0, clock.Advance(1.5f));
            Assert.AreEqual(1, clock.Advance(0.5f));
        }

        [Test]
        public void BurstSpacesItsVolleys()
        {
            var clock = new PatternClock(interval: 10f, volleysPerBurst: 3, volleySpacing: 0.25f);
            clock.Trigger();
            Assert.AreEqual(1, clock.Advance(0f));
            Assert.AreEqual(0, clock.Advance(0.2f));
            Assert.AreEqual(1, clock.Advance(0.1f));
            Assert.AreEqual(1, clock.Advance(0.25f));
            Assert.AreEqual(0, clock.Advance(1f));
        }

        [Test]
        public void SeveralVolleysCanFallDueInOneLongStep()
        {
            var clock = new PatternClock(interval: 10f, volleysPerBurst: 5, volleySpacing: 0.01f);
            clock.Trigger();
            Assert.AreEqual(5, clock.Advance(0.1f));
        }

        [Test]
        public void ZeroSpacingFiresTheWholeBurstAtOnce()
        {
            var clock = new PatternClock(interval: 10f, volleysPerBurst: 4, volleySpacing: 0f);
            clock.Trigger();
            Assert.AreEqual(4, clock.Advance(0f));
        }

        [Test]
        public void WithAutoFireOffOnlyTriggerFires()
        {
            var clock = new PatternClock(interval: 1f, volleysPerBurst: 1, volleySpacing: 0f) { AutoFire = false };
            Assert.AreEqual(0, clock.Advance(10f));
            clock.Trigger();
            Assert.AreEqual(1, clock.Advance(0f));
            Assert.AreEqual(0, clock.Advance(10f));
        }

        [Test]
        public void TriggerRestartsTheAutoTimer()
        {
            var clock = new PatternClock(interval: 2f, volleysPerBurst: 1, volleySpacing: 0f);
            Assert.AreEqual(0, clock.Advance(1.5f));
            clock.Trigger();
            Assert.AreEqual(1, clock.Advance(0f));
            Assert.AreEqual(0, clock.Advance(1.9f));
            Assert.AreEqual(1, clock.Advance(0.2f));
        }

        [Test]
        public void ANewBurstReplacesOneInProgress()
        {
            var clock = new PatternClock(interval: 10f, volleysPerBurst: 3, volleySpacing: 1f);
            clock.Trigger();
            Assert.AreEqual(1, clock.Advance(0f));
            clock.Trigger();
            Assert.AreEqual(1, clock.Advance(0f));
            Assert.AreEqual(2, clock.Advance(2f));
            Assert.AreEqual(0, clock.Advance(5f));
        }

        private float[] Slice(int n)
        {
            var result = new float[n];
            System.Array.Copy(_angles, result, n);
            return result;
        }

        private sealed class FloatComparer : System.Collections.IComparer
        {
            public int Compare(object x, object y) =>
                System.Math.Abs((float)x - (float)y) <= Tol ? 0 : ((float)x).CompareTo((float)y);
        }
    }
}
