using System;

namespace FlightRecord.Gameplay
{
    // The shapes a bullet-pattern emitter can fire (backlog 4.13).
    public enum PatternKind
    {
        // One shot along the aim direction.
        Aimed = 0,
        // Count shots fanned evenly across Arc, centered on the aim direction.
        Spread = 1,
        // Count shots evenly around a full circle, starting at the aim direction.
        Ring = 2,
        // One shot per volley, sweeping back and forth across Arc by SweepStep each shot.
        Stream = 3,
    }

    // A pattern's shape, independent of where it is fired from or how often.
    // Angles are degrees, counterclockwise, 0 = +X (right), 90 = +Y (up).
    public readonly struct PatternShape
    {
        public readonly PatternKind Kind;
        public readonly int Count;
        public readonly float Arc;
        public readonly float AngleOffset;
        public readonly float VolleySpin;
        public readonly float SweepStep;

        public PatternShape(PatternKind kind, int count, float arc, float angleOffset = 0f,
            float volleySpin = 0f, float sweepStep = 0f)
        {
            Kind = kind;
            Count = Math.Max(1, Math.Min(BulletPatternMath.MaxShotsPerVolley, count));
            Arc = Math.Max(0f, Math.Min(360f, arc));
            AngleOffset = angleOffset;
            VolleySpin = volleySpin;
            SweepStep = sweepStep;
        }
    }

    // Shot directions for one volley of a pattern. Kept engine-free so it can be
    // unit-tested in EditMode; PatternEmitter turns the angles into projectiles.
    public static class BulletPatternMath
    {
        public const int MaxShotsPerVolley = 64;

        public static int ShotsPerVolley(in PatternShape shape) =>
            shape.Kind == PatternKind.Spread || shape.Kind == PatternKind.Ring ? shape.Count : 1;

        // Writes the angles of volley number `volleyIndex` (0, 1, 2... over the emitter's
        // life) into `output` and returns how many it wrote. `baseAngle` is the aim
        // direction. Every volley turns VolleySpin degrees from the one before.
        public static int VolleyAngles(in PatternShape shape, float baseAngle, int volleyIndex, float[] output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            var count = Math.Min(ShotsPerVolley(shape), output.Length);
            var spin = (float)((double)volleyIndex * shape.VolleySpin % 360.0);
            var start = baseAngle + shape.AngleOffset + spin;

            switch (shape.Kind)
            {
                case PatternKind.Spread:
                    if (count == 1)
                    {
                        output[0] = start;
                        break;
                    }
                    for (var i = 0; i < count; i++)
                        output[i] = start - shape.Arc * 0.5f + shape.Arc * i / (count - 1);
                    break;

                case PatternKind.Ring:
                    for (var i = 0; i < count; i++)
                        output[i] = start + 360f * i / count;
                    break;

                case PatternKind.Stream:
                    output[0] = start + SweepOffset(shape.Arc, shape.SweepStep, volleyIndex);
                    break;

                default:
                    output[0] = start;
                    break;
            }
            return count;
        }

        // Stream's offset from the aim direction: starts at the center, moves SweepStep
        // degrees per shot, and bounces between -Arc/2 and +Arc/2.
        public static float SweepOffset(float arc, float step, int shotIndex)
        {
            if (arc <= 0f) return 0f;
            var period = 2.0 * arc;
            var t = ((double)shotIndex * Math.Abs(step) + arc * 0.5) % period;
            var bounce = t > arc ? period - t : t;
            var offset = (float)bounce - arc * 0.5f;
            return step < 0f ? -offset : offset;
        }
    }

    // When a pattern fires. A burst is VolleysPerBurst volleys, VolleySpacing seconds
    // apart. With AutoFire on, a burst starts every Interval seconds (the first one
    // Interval after creation); Trigger() starts one right now (the beat-driven
    // generator, later) and restarts the auto timer. A new burst replaces one still
    // in progress. Advance(dt) returns how many volleys fall due in that much time.
    public sealed class PatternClock
    {
        private readonly float _interval;
        private readonly int _volleysPerBurst;
        private readonly float _spacing;

        private float _untilAuto;
        private int _burstRemaining;
        private float _untilVolley;

        public bool AutoFire { get; set; } = true;

        public PatternClock(float interval, int volleysPerBurst, float volleySpacing)
        {
            _interval = Math.Max(0.05f, interval);
            _volleysPerBurst = Math.Max(1, volleysPerBurst);
            _spacing = Math.Max(0f, volleySpacing);
            _untilAuto = _interval;
        }

        public void Trigger()
        {
            StartBurst();
            _untilAuto = _interval;
        }

        public int Advance(float dt)
        {
            var due = 0;
            var remaining = Math.Max(0f, dt);
            while (true)
            {
                var nextVolley = _burstRemaining > 0 ? _untilVolley : float.PositiveInfinity;
                var nextAuto = AutoFire ? _untilAuto : float.PositiveInfinity;
                var next = Math.Min(nextVolley, nextAuto);
                if (next > remaining)
                {
                    Elapse(remaining);
                    return due;
                }

                Elapse(next);
                remaining -= next;
                if (nextAuto <= nextVolley)
                {
                    StartBurst();
                    _untilAuto += _interval;
                }
                else
                {
                    due++;
                    _burstRemaining--;
                    _untilVolley = _spacing;
                }
            }
        }

        private void StartBurst()
        {
            _burstRemaining = _volleysPerBurst;
            _untilVolley = 0f;
        }

        private void Elapse(float time)
        {
            if (_burstRemaining > 0) _untilVolley -= time;
            if (AutoFire) _untilAuto -= time;
        }
    }
}
