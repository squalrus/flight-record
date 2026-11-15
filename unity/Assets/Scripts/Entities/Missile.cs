using UnityEngine;

namespace FlightRecord
{
    // A projectile. Spawned directly (Ship's shots, destroyed when done) or taken from
    // EnemyProjectilePool (enemy pattern shots, returned to the pool when done). Either way
    // it ends when it hits something, when its time to live runs out, or once it is past
    // the edge of the screen (by OffscreenMargin).
    public class Missile : DestroyableObject
    {
        private const float OffscreenMargin = 2f;

        private float _ttl = 7f;
        private float _age;
        private ParticleSystem _explosion;
        private MeshRenderer _meshRenderer;
        private Rigidbody2D _rigidbody;
        private bool _canDamage = true;
        private float _homingTurnRate;
        private float _homingDelay;
        private float _homingRange;

        // Pooling (4.13): the pool and prefab this missile came from, if any, and the
        // countdown to returning after a hit (so the hit effect can play).
        private EnemyProjectilePool _pool;
        private GameObject _poolPrefab;
        private bool _inPool;
        private float _returnIn = -1f;

        // Set by Player.OnWeaponFired() when the player fires -- used to count Hits for the
        // Sector Complete screen's hit-percentage stat without counting enemy missiles
        // or friendly fire toward the player's accuracy.
        public bool IsPlayerFired;

        public Rigidbody2D Body => _rigidbody;

        // Awake, not Start: a pooled missile is reset right after it is instantiated.
        private void Awake()
        {
            _explosion = GetComponentInChildren<ParticleSystem>();
            // InChildren, not GetComponent -- the regular Missile's MeshRenderer is on the root,
            // but the Homing Rocket's glyph (TextMesh + MeshRenderer) lives on a child object.
            _meshRenderer = GetComponentInChildren<MeshRenderer>();
            _rigidbody = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            if (_returnIn >= 0f)
            {
                _returnIn -= Time.deltaTime;
                if (_returnIn < 0f) ReturnToPool();
                return;
            }

            _age += Time.deltaTime;
            if (_age > _ttl || IsPastScreenEdge())
            {
                Destruct();
            }
        }

        private bool IsPastScreenEdge()
        {
            if (!Playfield.TryGetView(out var center, out var halfSize)) return false;
            var offset = (Vector2)transform.position - center;
            return Mathf.Abs(offset.x) > halfSize.x + OffscreenMargin
                   || Mathf.Abs(offset.y) > halfSize.y + OffscreenMargin;
        }

        // Called by EnemyProjectilePool each time it hands this missile out: back to a
        // fresh, unfired state. The caller then sets damage, velocity, and homing.
        internal void TakenFromPool(EnemyProjectilePool pool, GameObject prefab)
        {
            _pool = pool;
            _poolPrefab = prefab;
            _inPool = false;
            _returnIn = -1f;
            _age = 0f;
            _canDamage = true;
            _homingTurnRate = _homingDelay = _homingRange = 0f;
            IsPlayerFired = false;
            if (_meshRenderer != null) _meshRenderer.enabled = true;
            if (_explosion != null) _explosion.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _rigidbody.linearVelocity = Vector2.zero;
            _rigidbody.angularVelocity = 0f;
        }

        private void ReturnToPool()
        {
            if (_inPool) return;
            _inPool = true;
            _returnIn = -1f;
            _pool.Return(this, _poolPrefab);
        }

        private void OnDestroy()
        {
            // Destroyed in flight (not returned): the pool stops counting it.
            if (_pool != null && !_inPool) _pool.Forget();
        }

        public override void Destruct()
        {
            if (_pool != null) ReturnToPool();
            else base.Destruct();
        }

        private void FixedUpdate()
        {
            if (!_canDamage || _homingTurnRate <= 0f || _age < _homingDelay) return;

            // Player-fired homing rockets hunt the nearest enemy/asteroid; enemy-fired ones (e.g.
            // Triceptor's volley) home in on the player instead -- there's only one valid target.
            var target = IsPlayerFired ? FindNearestEnemy() : PlayerController.NearestTo(_rigidbody.position);
            if (target == null) return;

            // Past the flat HomingDelay, the rocket still flies straight (no force/turn applied
            // below) until the target is within HomingRange -- so it looks ballistic at range and
            // only snaps onto a lock once a target is actually close enough to engage.
            if (Vector2.Distance(_rigidbody.position, target.position) > _homingRange) return;

            var direction = (Vector2)target.position - _rigidbody.position;
            var targetAngle = Mathf.Atan2(direction.x, direction.y) * -Mathf.Rad2Deg;
            var targetRotation = Quaternion.AngleAxis(targetAngle, Vector3.forward);

            // MoveRotation (not transform.rotation=) so turning stays a Rigidbody2D operation the
            // physics step picks up cleanly, rather than a transform write fighting the simulation.
            var newRotation = Quaternion.RotateTowards(transform.rotation, targetRotation, _homingTurnRate * Time.fixedDeltaTime);
            _rigidbody.MoveRotation(newRotation);

            // Homing only ever changes heading, never speed -- redirecting the existing velocity
            // vector onto the new facing preserves whatever speed the rocket already had (launch
            // velocity, ship momentum included) instead of accelerating/decelerating toward a
            // fixed target speed, which read as a jarring speed change the instant a lock started.
            var speed = _rigidbody.linearVelocity.magnitude;
            _rigidbody.linearVelocity = (Vector2)transform.up * speed;
        }

        // Enemy-fired shots pass through every enemy alive now (4.12): formations fly close
        // together and fire along their path, so otherwise the back of a V shoots down the front.
        // Call right after spawning the shot, before its first physics step.
        public void IgnoreEnemies()
        {
            foreach (var shot in GetComponentsInChildren<Collider2D>())
                Enemy.IgnoreCollisionsWith(shot);
        }

        public void SetHoming(float turnRate, float delay, float range)
        {
            _homingTurnRate = turnRate;
            _homingDelay = delay;
            _homingRange = range;
        }

        private Transform FindNearestEnemy()
        {
            Transform nearest = null;
            var nearestDistance = float.MaxValue;

            foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            {
                var distance = Vector2.Distance(transform.position, enemy.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = enemy.transform;
                }
            }

            foreach (var asteroid in FindObjectsByType<Asteroid>(FindObjectsSortMode.None))
            {
                var distance = Vector2.Distance(transform.position, asteroid.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = asteroid.transform;
                }
            }

            return nearest;
        }

        private void OnCollisionEnter2D(Collision2D collider)
        {
            // Same-side projectiles (e.g. a Missile and a Homing Rocket fired together from the
            // same shot, or two enemy missiles) shouldn't blow each other up on contact -- only
            // opposing-side projectiles (player vs. enemy) should be able to destroy one another.
            var otherMissile = collider.gameObject.GetComponent<Missile>();
            if (otherMissile != null && otherMissile.IsPlayerFired == IsPlayerFired) return;

            if (_canDamage)
            {
                collider.gameObject.GetComponent<ICanTakeDamage>()?.AddHealth(-_currentHealth, collider.GetContact(0).point);

                // Accuracy/damage stats count any destructible target connected with, not just
                // enemy kills -- an asteroid hit is still a successful shot.
                var hitEnemyOrAsteroid = collider.gameObject.GetComponent<Enemy>() != null
                    || collider.gameObject.GetComponent<Asteroid>() != null;
                if (IsPlayerFired && hitEnemyOrAsteroid && GameControl.control != null)
                {
                    GameControl.control.Hits++;
                    GameControl.control.DamageDealt += _currentHealth;
                }

                _explosion.Play(true);
                _meshRenderer.enabled = false;
                _canDamage = false;
                _rigidbody.linearVelocity = Vector2.zero;
                Destruct(.2f);
            }
        }

        public void Destruct(float time)
        {
            if (_pool != null) _returnIn = Mathf.Max(0f, time);
            else Destroy(gameObject, time);
        }
    }
}
