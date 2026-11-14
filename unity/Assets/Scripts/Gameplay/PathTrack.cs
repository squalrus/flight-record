using System;
using System.Collections.Generic;

namespace FlightRecord.Gameplay
{
    // One waypoint of an enemy entry path (backlog 4.12): a position, and how long to
    // stop there before moving on (a turret entering, holding to fire, then leaving).
    public readonly struct PathPoint
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Hold;

        public PathPoint(float x, float y, float hold = 0f)
        {
            X = x;
            Y = y;
            Hold = Math.Max(0f, hold);
        }
    }

    // A path through waypoints, flown at a constant speed, with optional stops at waypoints.
    // Evaluate(time) gives the position at that many seconds after the start.
    //
    // Curved paths pass through every waypoint on a Catmull-Rom spline (end tangents come
    // from repeating the end points); straight paths are a polyline. Either way, each segment
    // is sampled into short chords and travel is measured along them, so the speed is even
    // regardless of how far apart the waypoints are.
    //
    // Kept engine-free so it can be unit-tested in EditMode. The units are whatever the
    // caller uses (Enemy passes world units relative to the view's center).
    public sealed class PathTrack
    {
        private const int SamplesPerSegment = 16;

        private readonly int _segmentCount;
        private readonly float[] _holdBefore;        // per segment: stop time at its start waypoint
        private readonly float[] _sampleX;           // per segment: SamplesPerSegment + 1 samples
        private readonly float[] _sampleY;
        private readonly float[] _sampleDistance;    // distance along the segment at each sample
        private readonly float[] _segmentLength;
        private readonly float _finalHold;
        private readonly float _endX;
        private readonly float _endY;

        public float Speed { get; }

        // Seconds from the start until the path is finished, including every stop.
        public float Duration { get; }

        // Unit direction of travel at the end of the path (for flying on once it's done).
        // Zero for a path with no length.
        public float ExitDirectionX { get; }
        public float ExitDirectionY { get; }

        public PathTrack(IReadOnlyList<PathPoint> points, float speed, bool curved)
        {
            if (points == null || points.Count == 0)
                throw new ArgumentException("A path needs at least one point.", nameof(points));

            Speed = Math.Max(0.001f, speed);
            _segmentCount = points.Count - 1;
            _holdBefore = new float[Math.Max(0, _segmentCount)];
            _segmentLength = new float[Math.Max(0, _segmentCount)];
            _sampleX = new float[_segmentCount * (SamplesPerSegment + 1)];
            _sampleY = new float[_sampleX.Length];
            _sampleDistance = new float[_sampleX.Length];
            _finalHold = points[points.Count - 1].Hold;
            _endX = points[points.Count - 1].X;
            _endY = points[points.Count - 1].Y;

            var duration = 0f;
            for (var s = 0; s < _segmentCount; s++)
            {
                _holdBefore[s] = points[s].Hold;
                SampleSegment(points, s, curved);
                duration += _holdBefore[s] + _segmentLength[s] / Speed;
            }
            Duration = duration + _finalHold;

            // Exit direction: the last chord with any length.
            for (var i = _sampleX.Length - 1; i > 0; i--)
            {
                var dx = _sampleX[i] - _sampleX[i - 1];
                var dy = _sampleY[i] - _sampleY[i - 1];
                var len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len <= 1e-5f) continue;
                ExitDirectionX = dx / len;
                ExitDirectionY = dy / len;
                break;
            }
        }

        public float StartX => _segmentCount > 0 ? _sampleX[0] : _endX;
        public float StartY => _segmentCount > 0 ? _sampleY[0] : _endY;

        // Position `time` seconds after the start. Before 0 it's the start; after Duration it
        // keeps flying in the exit direction at Speed, so a finished enemy leaves the screen
        // instead of stopping wherever the path happens to end.
        public void Evaluate(float time, out float x, out float y)
        {
            // A single-point path has no direction to fly on in, so it stays put.
            if (time <= 0f || _segmentCount == 0)
            {
                x = StartX;
                y = StartY;
                return;
            }

            var t = time;
            for (var s = 0; s < _segmentCount; s++)
            {
                if (t <= _holdBefore[s])
                {
                    x = _sampleX[s * (SamplesPerSegment + 1)];
                    y = _sampleY[s * (SamplesPerSegment + 1)];
                    return;
                }
                t -= _holdBefore[s];

                var travel = _segmentLength[s] / Speed;
                if (t <= travel)
                {
                    PointAtDistance(s, t * Speed, out x, out y);
                    return;
                }
                t -= travel;
            }

            if (t <= _finalHold)
            {
                x = _endX;
                y = _endY;
                return;
            }

            var past = (t - _finalHold) * Speed;
            x = _endX + ExitDirectionX * past;
            y = _endY + ExitDirectionY * past;
        }

        private void SampleSegment(IReadOnlyList<PathPoint> points, int segment, bool curved)
        {
            var p1 = points[segment];
            var p2 = points[segment + 1];
            var p0 = segment > 0 ? points[segment - 1] : p1;
            var p3 = segment + 2 < points.Count ? points[segment + 2] : p2;

            var baseIndex = segment * (SamplesPerSegment + 1);
            var distance = 0f;
            for (var i = 0; i <= SamplesPerSegment; i++)
            {
                var u = i / (float)SamplesPerSegment;
                float x, y;
                if (curved)
                {
                    x = CatmullRom(p0.X, p1.X, p2.X, p3.X, u);
                    y = CatmullRom(p0.Y, p1.Y, p2.Y, p3.Y, u);
                }
                else
                {
                    x = p1.X + (p2.X - p1.X) * u;
                    y = p1.Y + (p2.Y - p1.Y) * u;
                }

                if (i > 0)
                {
                    var dx = x - _sampleX[baseIndex + i - 1];
                    var dy = y - _sampleY[baseIndex + i - 1];
                    distance += (float)Math.Sqrt(dx * dx + dy * dy);
                }

                _sampleX[baseIndex + i] = x;
                _sampleY[baseIndex + i] = y;
                _sampleDistance[baseIndex + i] = distance;
            }
            _segmentLength[segment] = distance;
        }

        private void PointAtDistance(int segment, float distance, out float x, out float y)
        {
            var baseIndex = segment * (SamplesPerSegment + 1);
            for (var i = 1; i <= SamplesPerSegment; i++)
            {
                var d1 = _sampleDistance[baseIndex + i];
                if (distance > d1 && i < SamplesPerSegment) continue;

                var d0 = _sampleDistance[baseIndex + i - 1];
                var f = d1 > d0 ? Math.Min(1f, Math.Max(0f, (distance - d0) / (d1 - d0))) : 1f;
                x = _sampleX[baseIndex + i - 1] + (_sampleX[baseIndex + i] - _sampleX[baseIndex + i - 1]) * f;
                y = _sampleY[baseIndex + i - 1] + (_sampleY[baseIndex + i] - _sampleY[baseIndex + i - 1]) * f;
                return;
            }

            x = _sampleX[baseIndex + SamplesPerSegment];
            y = _sampleY[baseIndex + SamplesPerSegment];
        }

        private static float CatmullRom(float p0, float p1, float p2, float p3, float u)
        {
            var u2 = u * u;
            var u3 = u2 * u;
            return 0.5f * (2f * p1
                           + (-p0 + p2) * u
                           + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2
                           + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }
    }
}
