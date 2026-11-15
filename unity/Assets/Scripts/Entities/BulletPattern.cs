using FlightRecord.Gameplay;
using UnityEngine;

namespace FlightRecord
{
    // Where a pattern points before its shape is applied.
    public enum PatternAim
    {
        // At the nearest player, measured from the emitter's origin.
        NearestPlayer = 0,
        // Along the emitter's facing (its nose, transform.up), so the pattern turns with it.
        Facing = 1,
        // A fixed screen direction (Direction).
        FixedDirection = 2,
    }

    // A bullet pattern (backlog 4.13), authored as data: its shape (aimed, spread, ring, or
    // sweeping stream), how often it fires, and what it fires. Put it on any enemy with a
    // PatternEmitter. Angles are degrees, counterclockwise, 0 = right, 90 = up, 180 = left.
    [CreateAssetMenu(fileName = "Pattern", menuName = "Flight Record/Bullet Pattern")]
    public class BulletPattern : ScriptableObject
    {
        [Header("Shape")]
        public PatternKind Kind = PatternKind.Aimed;
        [Tooltip("Shots per volley for Spread and Ring. Aimed and Stream fire one shot per volley.")]
        [Range(1, BulletPatternMath.MaxShotsPerVolley)] public int Count = 1;
        [Tooltip("Degrees. Spread: the width of the fan. Stream: the width it sweeps back and forth across.")]
        [Range(0f, 360f)] public float Arc = 60f;
        [Tooltip("Stream only: degrees the stream moves between shots (negative sweeps the other way first).")]
        public float SweepStep = 10f;
        [Tooltip("Degrees each volley turns from the one before: a spinning ring, a spiral.")]
        public float VolleySpin;
        [Tooltip("Degrees added to the aim direction, counterclockwise.")]
        public float AngleOffset;

        [Header("Aim")]
        public PatternAim Aim = PatternAim.NearestPlayer;
        [Tooltip("FixedDirection only: degrees, 0 = right, 90 = up, 180 = left, 270 = down.")]
        public float Direction = 180f;

        [Header("Rate")]
        [Tooltip("Seconds between bursts when the emitter fires on its own timer. The first burst comes one interval after spawning.")]
        [Min(0.05f)] public float Interval = 2f;
        [Tooltip("Volleys in one burst (one timer tick or one Trigger call).")]
        [Min(1)] public int VolleysPerBurst = 1;
        [Tooltip("Seconds between the volleys of a burst. 0 fires them all at once.")]
        [Min(0f)] public float VolleySpacing = 0.1f;

        [Header("Projectile")]
        [Tooltip("A prefab with a Missile and a Rigidbody2D (Missile, HomingRocket).")]
        public GameObject Projectile;
        [Tooltip("World units per second, relative to the screen when Ride Scroll is on.")]
        [Min(0.1f)] public float Speed = 12f;
        [Tooltip("How far from the origin each shot starts, along its own direction, to clear the hull.")]
        [Min(0f)] public float SpawnRadius;
        [Tooltip("Add the view's scroll velocity, so the pattern keeps its shape on screen.")]
        public bool RideScroll = true;

        [Header("Homing (turn rate 0 = off)")]
        [Tooltip("Degrees per second the shot turns toward the nearest player.")]
        [Min(0f)] public float HomingTurnRate;
        [Tooltip("Seconds of straight flight before homing starts.")]
        [Min(0f)] public float HomingDelay;
        [Tooltip("Homing only steers while the player is this close (world units).")]
        [Min(0f)] public float HomingRange;

        public PatternShape Shape => new PatternShape(Kind, Count, Arc, AngleOffset, VolleySpin, SweepStep);
    }
}
