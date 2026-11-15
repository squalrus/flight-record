using System;
using System.Collections.Generic;
using FlightRecord.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlightRecord
{
    // How an enemy points its nose (and so its Cannon and any beam).
    public enum EnemyAim
    {
        // Along the direction it's moving across the screen.
        FaceTravel = 0,
        // At the nearest player: a turret.
        TrackNearestPlayer = 1,
        // Never turns.
        Fixed = 2,
    }

    // Side-scrolling enemy movement (backlog 4.12). An enemy flies an EnemyPath, given by
    // whoever spawns it (FormationSpawner for now, waves in 4.2), relative to the scrolling
    // view: it enters from off screen, sweeps, may hold, and leaves. Once it has been on
    // screen and then goes past the left edge or above or below the screen, or has finished
    // its path and is off screen in any direction, it despawns without exploding or
    // dropping anything (Escaped), so a wave can tell escapes from kills.
    //
    // Motion runs in FixedUpdate through the Rigidbody2D: velocity is set each step to land
    // exactly on the path point (the way PlayerController drives the ship), so the body
    // still collides normally. Rotation goes through the body too, so it's interpolated.
    //
    // Subclasses change rotation (NextRotation), stop on the path (PathPaused), or fire
    // their own way (TryFire). Firing happens only while on screen.
    public class Enemy : MonoBehaviour
    {
        protected Rigidbody2D _rigidbody;
        protected Ship _ship;

        // Toggled with F9 in the Editor: draws each enemy's path. Player reads it too
        // (pickup radius).
        public static bool ShowDebugRanges;
        private static int _lastToggleFrame = -1;

        [Header("Aim")]
        public EnemyAim Aim = EnemyAim.FaceTravel;

        [Header("Score")]
        [Tooltip("Points for destroying this enemy (backlog 4.14). Escaping pays nothing.")]
        [Min(0)] public int Points = 100;

        [Header("Despawn")]
        [Tooltip("Extra world units past the screen edge, beyond the enemy's own size, before it despawns.")]
        [Min(0f)] public float DespawnMargin = 2f;

        [Header("Debug Visualization")]
        [SerializeField] private Color _pathGizmoColor = Color.yellow;

        private PathTrack _track;
        private Vector2 _slotOffset;
        private float _pathTime;
        private Vector2 _lastLocal;
        private Vector2 _travelDirection = Vector2.left;
        private bool _wasOnScreen;
        private float _extent;

        // True while the enemy's center is inside the visible screen.
        public bool IsOnScreen { get; private set; }

        // Has a PatternEmitter (4.13), which replaces the default single-cannon shot.
        private bool _hasEmitter;

        // Set when the enemy left the screen alive (despawned rather than destroyed).
        public bool HasEscaped { get; private set; }
        public event Action<Enemy> Escaped;

        // Override to stop moving along the path (Satellite while its beam is on).
        protected virtual bool PathPaused => false;

        protected Transform NearestPlayer => PlayerController.NearestTo(transform.position);

        // Colliders of every live enemy, so enemy shots can pass through them (Missile.IgnoreEnemies).
        private static readonly List<Collider2D> AllColliders = new List<Collider2D>();
        private Collider2D[] _colliders;

        private void OnEnable()
        {
            _colliders = GetComponentsInChildren<Collider2D>();
            AllColliders.AddRange(_colliders);
        }

        private void OnDisable()
        {
            foreach (var c in _colliders) AllColliders.Remove(c);
        }

        public static void IgnoreCollisionsWith(Collider2D other)
        {
            foreach (var c in AllColliders)
                if (c != null) Physics2D.IgnoreCollision(other, c);
        }

        // Called by the spawner right after Instantiate. `offset` is the formation slot's
        // offset from the path (world units) and `delay` how long to wait at the path's
        // start before moving.
        public void FollowPath(PathTrack track, Vector2 offset, float delay)
        {
            _track = track;
            _slotOffset = offset;
            _pathTime = -Mathf.Max(0f, delay);

            // Face the way the path starts, so it doesn't turn in from facing up.
            track.Evaluate(0f, out var x0, out var y0);
            track.Evaluate(0.1f, out var x1, out var y1);
            var start = new Vector2(x1 - x0, y1 - y0);
            if (start.sqrMagnitude > 1e-6f) _travelDirection = start.normalized;
            _lastLocal = new Vector2(x0, y0) + offset;

            if (Aim == EnemyAim.FaceTravel)
                transform.rotation = Quaternion.Euler(0f, 0f, AngleOf(_travelDirection));
        }

        private void Start()
        {
            _rigidbody = GetComponent<Rigidbody2D>();
            _ship = GetComponent<Ship>();
            _hasEmitter = GetComponent<PatternEmitter>() != null;

            // Position and rotation are set exactly each step; physics only handles contact.
            // Rotation isn't frozen (that constraint would also block MoveRotation); any spin
            // from a hit is cleared each step instead.
            _rigidbody.angularVelocity = 0f;
            _rigidbody.linearDamping = 0f;
            _rigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;

            var collider = GetComponent<Collider2D>();
            _extent = collider != null ? Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.y) : 1f;

            OnSpawn();
        }

        // Runs once at the end of Start(), after _rigidbody/_ship are assigned. Exists so
        // subclasses can kick off their own coroutines without redefining Start() -- Unity invokes
        // a private Start() on every class in the hierarchy that declares one, so a second private
        // Start() in a subclass would run alongside (not instead of) this one.
        protected virtual void OnSpawn() { }

        private void Update()
        {
            ToggleDebugRangesOnKeyPress();
        }

        private void FixedUpdate()
        {
            if (!Playfield.TryGetView(out var center, out var halfSize)) return;
            var dt = Time.fixedDeltaTime;

            if (_track != null)
            {
                if (!PathPaused) _pathTime += dt;

                _track.Evaluate(_pathTime, out var x, out var y);
                var local = new Vector2(x, y) + _slotOffset;
                var motion = local - _lastLocal;
                if (motion.sqrMagnitude > 1e-8f) _travelDirection = motion.normalized;
                _lastLocal = local;

                _rigidbody.linearVelocity = (center + local - _rigidbody.position) / dt;
            }
            else
            {
                // No path: hold still in the world and let the view scroll past.
                _rigidbody.linearVelocity = Vector2.zero;
            }

            _rigidbody.angularVelocity = 0f;
            _rigidbody.MoveRotation(NextRotation(_rigidbody.rotation, dt));

            UpdateScreenState(_rigidbody.position + _rigidbody.linearVelocity * dt, center, halfSize);
        }

        // The body's rotation (degrees) for the next physics step.
        protected virtual float NextRotation(float current, float dt)
        {
            Vector2 direction;
            switch (Aim)
            {
                case EnemyAim.FaceTravel:
                    direction = _travelDirection;
                    break;
                case EnemyAim.TrackNearestPlayer:
                    var target = NearestPlayer;
                    if (target == null) return current;
                    direction = (Vector2)target.position - _rigidbody.position;
                    break;
                default:
                    return current;
            }

            var goal = AngleOf(direction);
            var turnRate = _ship != null ? _ship.RotationSpeed : 0f;
            return turnRate <= 0f ? goal : Mathf.MoveTowardsAngle(current, goal, turnRate * dt);
        }

        // Rotation (degrees) that points the ship's nose (local +Y) along `direction`.
        protected static float AngleOf(Vector2 direction) => Mathf.Atan2(direction.x, direction.y) * -Mathf.Rad2Deg;

        private void UpdateScreenState(Vector2 position, Vector2 center, Vector2 halfSize)
        {
            var min = center - halfSize;
            var max = center + halfSize;
            IsOnScreen = position.x >= min.x && position.x <= max.x && position.y >= min.y && position.y <= max.y;
            if (IsOnScreen)
            {
                _wasOnScreen = true;
                return;
            }

            var margin = _extent + DespawnMargin;
            var pastLeft = position.x < min.x - margin;
            var pastTopOrBottom = position.y > max.y + margin || position.y < min.y - margin;
            var pastRight = position.x > max.x + margin;
            var finished = _track == null || _pathTime > _track.Duration;

            if ((_wasOnScreen && (pastLeft || pastTopOrBottom))
                || (finished && (pastLeft || pastTopOrBottom || pastRight)))
            {
                Escape();
            }
        }

        private void Escape()
        {
            if (HasEscaped) return;
            HasEscaped = true;
            Escaped?.Invoke(this);
            Destroy(gameObject);
        }

        private void LateUpdate()
        {
            if (Time.timeScale == 0f || !IsOnScreen || NearestPlayer == null) return;

            TryFire();
        }

        // Default: Ship's single-cannon primary weapon, along the enemy's facing, unless the
        // enemy has a PatternEmitter, which fires on its own (Triceptor). Satellite overrides
        // this to a no-op and fires its beam instead.
        protected virtual void TryFire()
        {
            if (_hasEmitter) return;
            _ship.FirePrimaryWeapon();
        }

        private void ToggleDebugRangesOnKeyPress()
        {
            if (Time.frameCount == _lastToggleFrame) return;
            if (Keyboard.current == null || !Keyboard.current.f9Key.wasPressedThisFrame) return;

            ShowDebugRanges = !ShowDebugRanges;
            _lastToggleFrame = Time.frameCount;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!ShowDebugRanges || _track == null || !Playfield.TryGetView(out var center, out _)) return;

            Gizmos.color = _pathGizmoColor;
            var origin = center + _slotOffset;
            _track.Evaluate(0f, out var px, out var py);
            const float step = 0.1f;
            for (var t = step; t <= _track.Duration + step; t += step)
            {
                _track.Evaluate(t, out var x, out var y);
                Gizmos.DrawLine(origin + new Vector2(px, py), origin + new Vector2(x, y));
                px = x;
                py = y;
            }
        }
#endif
    }
}
