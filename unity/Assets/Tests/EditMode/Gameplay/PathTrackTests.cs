using FlightRecord.Gameplay;
using NUnit.Framework;

namespace FlightRecord.Gameplay.Tests
{
    public class PathTrackTests
    {
        private const float Tolerance = 1e-3f;

        private static PathTrack Straight(float speed, params PathPoint[] points) => new PathTrack(points, speed, curved: false);

        [Test]
        public void StraightPath_MovesAtConstantSpeed()
        {
            var track = Straight(5f, new PathPoint(10f, 0f), new PathPoint(0f, 0f));

            Assert.That(track.Duration, Is.EqualTo(2f).Within(Tolerance));

            track.Evaluate(1f, out var x, out var y);
            Assert.That(x, Is.EqualTo(5f).Within(Tolerance));
            Assert.That(y, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void BeforeStart_IsAtFirstPoint()
        {
            var track = Straight(5f, new PathPoint(10f, 3f), new PathPoint(0f, 0f));

            track.Evaluate(-1f, out var x, out var y);
            Assert.That(x, Is.EqualTo(10f).Within(Tolerance));
            Assert.That(y, Is.EqualTo(3f).Within(Tolerance));
        }

        [Test]
        public void HoldAtWaypoint_StopsThereForItsDuration()
        {
            var track = Straight(10f,
                new PathPoint(0f, 0f),
                new PathPoint(10f, 0f, hold: 3f),
                new PathPoint(10f, 10f));

            // 1 s to arrive, 3 s hold, 1 s to leave.
            Assert.That(track.Duration, Is.EqualTo(5f).Within(Tolerance));

            track.Evaluate(2.5f, out var x, out var y);
            Assert.That(x, Is.EqualTo(10f).Within(Tolerance));
            Assert.That(y, Is.EqualTo(0f).Within(Tolerance));

            track.Evaluate(4.5f, out x, out y);
            Assert.That(x, Is.EqualTo(10f).Within(Tolerance));
            Assert.That(y, Is.EqualTo(5f).Within(Tolerance));
        }

        [Test]
        public void HoldAtFirstPoint_DelaysTheStart()
        {
            var track = Straight(10f, new PathPoint(0f, 0f, hold: 2f), new PathPoint(10f, 0f));

            track.Evaluate(1.5f, out var x, out _);
            Assert.That(x, Is.EqualTo(0f).Within(Tolerance));

            track.Evaluate(2.5f, out x, out _);
            Assert.That(x, Is.EqualTo(5f).Within(Tolerance));
        }

        [Test]
        public void AfterEnd_KeepsFlyingInExitDirection()
        {
            var track = Straight(4f, new PathPoint(0f, 0f), new PathPoint(0f, -8f));

            Assert.That(track.ExitDirectionX, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(track.ExitDirectionY, Is.EqualTo(-1f).Within(Tolerance));

            track.Evaluate(track.Duration + 1f, out var x, out var y);
            Assert.That(x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(y, Is.EqualTo(-12f).Within(Tolerance));
        }

        [Test]
        public void CurvedPath_PassesThroughEveryWaypoint()
        {
            var points = new[]
            {
                new PathPoint(20f, 0f),
                new PathPoint(10f, 8f, hold: 1f),
                new PathPoint(0f, 0f, hold: 1f),
                new PathPoint(-10f, -8f),
            };
            var track = new PathTrack(points, 6f, curved: true);

            track.Evaluate(0f, out var x, out var y);
            Assert.That(x, Is.EqualTo(20f).Within(Tolerance));
            Assert.That(y, Is.EqualTo(0f).Within(Tolerance));

            // During each hold the position is exactly that waypoint.
            var arrival = FindHoldStart(track, 10f, 8f);
            Assert.That(arrival, Is.GreaterThan(0f));
            track.Evaluate(arrival + 0.5f, out x, out y);
            Assert.That(x, Is.EqualTo(10f).Within(Tolerance));
            Assert.That(y, Is.EqualTo(8f).Within(Tolerance));

            track.Evaluate(track.Duration, out x, out y);
            Assert.That(x, Is.EqualTo(-10f).Within(Tolerance));
            Assert.That(y, Is.EqualTo(-8f).Within(Tolerance));
        }

        [Test]
        public void CurvedPath_SpeedIsEvenAlongTheCurve()
        {
            var points = new[]
            {
                new PathPoint(30f, 10f),
                new PathPoint(15f, -10f),
                new PathPoint(0f, 10f),
                new PathPoint(-15f, -10f),
            };
            var track = new PathTrack(points, 8f, curved: true);
            const float step = 0.05f;

            track.Evaluate(0f, out var px, out var py);
            for (var t = step; t < track.Duration; t += step)
            {
                track.Evaluate(t, out var x, out var y);
                var moved = System.Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                // Chord vs arc: allow a little under the exact 0.4 on tight bends.
                Assert.That(moved, Is.EqualTo(8f * step).Within(0.03f), $"at t={t}");
                px = x;
                py = y;
            }
        }

        [Test]
        public void SinglePoint_StaysPut()
        {
            var track = Straight(5f, new PathPoint(3f, 4f));

            track.Evaluate(10f, out var x, out var y);
            Assert.That(x, Is.EqualTo(3f).Within(Tolerance));
            Assert.That(y, Is.EqualTo(4f).Within(Tolerance));
        }

        [Test]
        public void NoPoints_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new PathTrack(new PathPoint[0], 5f, curved: true));
        }

        // First time the track sits exactly on (x, y), scanning in small steps.
        private static float FindHoldStart(PathTrack track, float x, float y)
        {
            for (var t = 0f; t <= track.Duration; t += 0.01f)
            {
                track.Evaluate(t, out var px, out var py);
                if (System.Math.Abs(px - x) < Tolerance && System.Math.Abs(py - y) < Tolerance) return t;
            }
            return -1f;
        }
    }
}
