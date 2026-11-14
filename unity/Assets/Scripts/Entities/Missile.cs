using UnityEngine;

namespace FlightRecord
{
    public class Missile : DestroyableObject
    {
        private float _ttl = 7f;
        private float _age;
        private ParticleSystem _explosion;
        private MeshRenderer _meshRenderer;
        private Rigidbody2D _rigidbody;
        private bool _canDamage = true;
        private float _homingTurnRate;
        private float _homingDelay;
        private float _homingRange;

        // Set by Player.OnWeaponFired() when the player fires -- used to count Hits for the
        // Sector Complete screen's hit-percentage stat without counting enemy missiles
        // or friendly fire toward the player's accuracy.
        public bool IsPlayerFired;

        private void Start()
        {
            _explosion = GetComponentInChildren<ParticleSystem>();
            // InChildren, not GetComponent -- the regular Missile's MeshRenderer is on the root,
            // but the Homing Rocket's glyph (TextMesh + MeshRenderer) lives on a child object.
            _meshRenderer = GetComponentInChildren<MeshRenderer>();
            _rigidbody = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age > _ttl)
            {
                Destruct();
            }
        }

        private void FixedUpdate()
        {
            if (!_canDamage || _homingTurnRate <= 0f || _age < _homingDelay) return;

            // Player-fired homing rockets hunt the nearest enemy/asteroid; enemy-fired ones (e.g.
            // Triceptor's volley) home in on the player instead -- there's only one valid target.
            var target = IsPlayerFired ? FindNearestEnemy() : PlayerController.PlayerTransform;
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
            Destroy(gameObject, time);
        }
    }
}
