using System;
using System.Collections.Generic;
using FlightRecord.Gameplay;
using UnityEngine;

namespace FlightRecord
{
    // An enemy entry path (backlog 4.12), authored as data. Waypoints are in view
    // coordinates, so a path is the same on screen however far the camera has scrolled:
    // (0, 0) is the bottom-left of the visible screen and (1, 1) the top-right. Put the
    // first waypoint outside 0-1 so the enemy enters from off screen (x > 1 from the
    // right, y > 1 from the top, y < 0 from the bottom), and the last one outside too so
    // it leaves. After the last waypoint the enemy keeps flying the same way until it is
    // off screen, then despawns (Enemy).
    //
    // A waypoint's Hold stops the enemy there for that many seconds, still on screen and
    // riding the scroll: a turret enters, holds while it fires, then leaves.
    [CreateAssetMenu(fileName = "Path", menuName = "Flight Record/Enemy Path")]
    public class EnemyPath : ScriptableObject
    {
        [Serializable]
        public struct Waypoint
        {
            [Tooltip("View coordinates: (0,0) bottom-left, (1,1) top-right of the visible screen.")]
            public Vector2 Position;
            [Tooltip("Seconds to stop at this waypoint before moving on.")]
            [Min(0f)] public float Hold;
        }

        public List<Waypoint> Waypoints = new List<Waypoint>
        {
            new Waypoint { Position = new Vector2(1.1f, 0.5f) },
            new Waypoint { Position = new Vector2(-0.1f, 0.5f) },
        };

        [Tooltip("World units per second, measured relative to the scrolling view.")]
        [Min(0.1f)] public float Speed = 10f;

        [Tooltip("Smooth curve through the waypoints; off for straight lines and sharp turns.")]
        public bool Curved = true;

        // The path as an offset from the view's center, in world units, for a view of
        // `halfSize`. MirrorY flips it top to bottom, so one asset covers entries from the
        // top and from the bottom.
        public PathTrack Build(Vector2 halfSize, bool mirrorY)
        {
            var points = new PathPoint[Mathf.Max(1, Waypoints.Count)];
            if (Waypoints.Count == 0) return new PathTrack(points, Speed, Curved);

            for (var i = 0; i < Waypoints.Count; i++)
            {
                var view = Waypoints[i].Position;
                if (mirrorY) view.y = 1f - view.y;
                var local = Playfield.ViewToLocal(view, halfSize);
                points[i] = new PathPoint(local.x, local.y, Waypoints[i].Hold);
            }
            return new PathTrack(points, Speed, Curved);
        }
    }
}
