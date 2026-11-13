using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace InfinitySpace
{
    public class Enemy : MonoBehaviour
    {
        protected float _distance;
        protected Transform _player;
        protected Rigidbody2D _rigidbody;
        protected Ship _ship;

        private bool _wasInCombat;

        public static bool ShowDebugRanges;
        private static int _lastToggleFrame = -1;

        [Header("Detection")]
        public float RadarDistance;
        public float FiringRange;

        [Header("Orbit")]
        public float OrbitRadius;
        public float OrbitSpeed;

        [Header("Patrol (Idle Behavior)")]
        public float PatrolResumeDelay = 1f;
        public float PatrolMoveDistance = 5f;
        public float PatrolSpeed = 2f;
        public Vector2 PatrolScanDurationRange = new Vector2(2f, 5f);
        public Vector2 PatrolRotationRange = new Vector2(45f, 180f);

        private const float PATROL_MOVE_TIMEOUT = 3f;
        private const float PATROL_ARRIVAL_DISTANCE = 0.5f;
        private const float PATROL_ROTATION_TOLERANCE = 0.5f;

        [Header("Debug Visualization")]
        [SerializeField] private Color _radarGizmoColor = Color.cyan;
        [SerializeField] private Color _firingRangeGizmoColor = Color.red;
        [SerializeField] private Color _orbitGizmoColor = Color.yellow;

        private void Start()
        {
            _player = PlayerController.PlayerTransform;
            _rigidbody = GetComponent<Rigidbody2D>();
            _ship = GetComponent<Ship>();

            StartCoroutine(PatrolRoutine());
            OnSpawn();
        }

        // Runs once at the end of Start(), after _player/_rigidbody/_ship are assigned. Exists so
        // subclasses can kick off their own coroutines without redefining Start() -- Unity invokes
        // a private Start() on every class in the hierarchy that declares one, so a second private
        // Start() in a subclass would run alongside (not instead of) this one.
        protected virtual void OnSpawn() { }

        private void Update()
        {
            ToggleDebugRangesOnKeyPress();

            if (Time.timeScale == 0f) return;

            _distance = _player != null ? Vector2.Distance(transform.position, _player.position) : float.MaxValue;

            if (_distance <= RadarDistance)
            {
                if (!_wasInCombat) OnEnterCombat();
                _wasInCombat = true;

                UpdateBehavior();
            }
            else if (_wasInCombat)
            {
                _wasInCombat = false;
                OnExitCombat();
            }
        }

        protected virtual void OnEnterCombat() { }
        protected virtual void OnExitCombat() { }
        protected virtual float OrbitTangentMultiplier => 1f;

        // Default in-combat behavior: face the player and orbit them. Triceptor overrides this
        // wholesale -- it spins on its own axis and fires fixed-position cannons rather than
        // aiming a single weapon at the player.
        protected virtual void UpdateBehavior()
        {
            RotateTowardPlayer();
            Orbit();
        }

        private void LateUpdate()
        {
            if (_player == null || Time.timeScale == 0f) return;

            TryFire();
        }

        // Default: Ship's single-cannon primary weapon, gated by FiringRange. Triceptor overrides
        // this to a no-op since its cannons fire on their own fixed-interval volley instead.
        protected virtual void TryFire()
        {
            if (_distance <= FiringRange)
            {
                _ship.FirePrimaryWeapon();
            }
        }

        private void RotateTowardPlayer()
        {
            var direction = _player.position - transform.position;
            var targetAngle = Mathf.Atan2(direction.x, direction.y) * -Mathf.Rad2Deg;
            var targetRotation = Quaternion.AngleAxis(targetAngle, Vector3.forward);

            transform.rotation = _ship.RotationSpeed <= 0
                ? targetRotation
                : Quaternion.RotateTowards(transform.rotation, targetRotation, _ship.RotationSpeed * Time.deltaTime);
        }

        protected void Orbit()
        {
            if (_ship.Speed <= 0) return;

            var toPlayer = (Vector2)_player.position - (Vector2)transform.position;
            var radialDirection = toPlayer.normalized;
            var tangentDirection = new Vector2(-radialDirection.y, radialDirection.x);

            var radialError = toPlayer.magnitude - OrbitRadius;
            var orbitForce = radialDirection * radialError + tangentDirection * OrbitSpeed * OrbitTangentMultiplier;

            _rigidbody.AddForce(orbitForce.normalized * _ship.Speed);
        }

        protected bool InCombatRange => _distance <= RadarDistance;
        private bool _resumingFromCombat;

        // Default idle behavior: wander in a straight line, pause, rotate by a random amount,
        // repeat. Triceptor overrides this with a continuous self-spin instead.
        protected virtual IEnumerator PatrolRoutine()
        {
            while (true)
            {
                if (InCombatRange)
                {
                    _resumingFromCombat = true;
                    yield return null;
                    continue;
                }

                if (_resumingFromCombat)
                {
                    yield return StartCoroutine(PatrolResumeDelayRoutine());
                    _resumingFromCombat = false;
                    continue;
                }

                yield return StartCoroutine(PatrolMove());
                yield return StartCoroutine(PatrolScan());
                yield return StartCoroutine(PatrolRotate());
            }
        }

        private IEnumerator PatrolResumeDelayRoutine()
        {
            var elapsed = 0f;

            while (!InCombatRange && elapsed < PatrolResumeDelay)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator PatrolMove()
        {
            var direction = (Vector2)transform.up;
            var target = (Vector2)transform.position + direction * PatrolMoveDistance;
            var elapsed = 0f;

            while (!InCombatRange && elapsed < PATROL_MOVE_TIMEOUT
                   && Vector2.Distance(transform.position, target) > PATROL_ARRIVAL_DISTANCE)
            {
                if (Time.timeScale > 0f) _rigidbody.AddForce(direction * PatrolSpeed);
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator PatrolScan()
        {
            var duration = Random.Range(PatrolScanDurationRange.x, PatrolScanDurationRange.y);
            var elapsed = 0f;

            while (!InCombatRange && elapsed < duration)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator PatrolRotate()
        {
            var rotationAmount = Random.Range(PatrolRotationRange.x, PatrolRotationRange.y) * (Random.value < 0.5f ? -1f : 1f);
            var targetRotation = transform.rotation * Quaternion.Euler(0, 0, rotationAmount);
            var rotationSpeed = _ship.RotationSpeed > 0 ? _ship.RotationSpeed : 90f;

            while (!InCombatRange && Quaternion.Angle(transform.rotation, targetRotation) > PATROL_ROTATION_TOLERANCE)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                yield return null;
            }
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
            if (!ShowDebugRanges) return;

            Gizmos.color = _radarGizmoColor;
            Gizmos.DrawWireSphere(transform.position, RadarDistance);

            Gizmos.color = _firingRangeGizmoColor;
            Gizmos.DrawWireSphere(transform.position, FiringRange);

            var player = _player != null ? _player : PlayerController.PlayerTransform;
            if (player != null && OrbitRadius > 0)
            {
                Gizmos.color = _orbitGizmoColor;
                Gizmos.DrawWireSphere(player.position, OrbitRadius);
            }
        }
#endif
    }
}
