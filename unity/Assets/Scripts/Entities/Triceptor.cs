using System.Collections;
using UnityEngine;

namespace FlightRecord
{
    // Three cannons fixed 120 degrees apart, firing a homing rocket volley from all three at once
    // every VolleyInterval seconds whenever the player is within FiringRange -- distinct from the
    // rest of the Enemy roster, which aims and fires a single weapon instead of a fixed-interval
    // volley. Patrol: spins slowly while drifting forward. Attack (player within RadarDistance):
    // spins faster and orbits the player; the volley timer runs throughout, in both states, and
    // only needs FiringRange to actually let a tick through.
    public class Triceptor : Enemy
    {
        [Header("Cannons")]
        [SerializeField] private Transform[] _cannons;
        [SerializeField] private GameObject _homingRocketPrefab;

        [Header("Volley")]
        public float VolleyInterval = 2f;
        public float HomingTurnRate = 400f;
        public float HomingDelay = 0.75f;
        public float HomingRange = 8f;
        public float LaunchSpeed = 12f;

        [Header("Spin")]
        public float PatrolSpinRate = 30f;
        public float AttackSpinRate = 120f;
        public float PatrolDriftSpeed = 1f;

        protected override void OnSpawn()
        {
            StartCoroutine(VolleyRoutine());
        }

        protected override void UpdateBehavior()
        {
            transform.Rotate(0f, 0f, AttackSpinRate * Time.deltaTime);
            Orbit();
        }

        protected override IEnumerator PatrolRoutine()
        {
            while (true)
            {
                if (!InCombatRange)
                {
                    transform.Rotate(0f, 0f, PatrolSpinRate * Time.deltaTime);
                    _rigidbody.AddForce((Vector2)transform.up * PatrolDriftSpeed);
                }
                yield return null;
            }
        }

        protected override void TryFire() { }

        private IEnumerator VolleyRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(VolleyInterval);
                if (Time.timeScale > 0f && _distance <= FiringRange) FireVolley();
            }
        }

        private void FireVolley()
        {
            if (_homingRocketPrefab == null || _cannons == null) return;

            foreach (var cannon in _cannons)
            {
                if (cannon == null) continue;

                var rocket = Instantiate(_homingRocketPrefab, cannon.position, cannon.rotation);
                if (ProjectileContainer.Instance != null) rocket.transform.SetParent(ProjectileContainer.Instance);

                var missile = rocket.GetComponent<Missile>();
                missile.SetHealth(_ship.RollFirePower());
                missile.SetHoming(HomingTurnRate, HomingDelay, HomingRange);
                rocket.GetComponent<Rigidbody2D>().linearVelocity = (Vector2)cannon.up * LaunchSpeed;
            }
        }
    }
}
