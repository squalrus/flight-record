using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace FlightRecord
{
    // Three cannons fixed 120 degrees apart, firing a homing rocket volley from all three at once
    // every VolleyInterval seconds while on screen -- distinct from the rest of the Enemy roster,
    // which fires a single weapon along its facing instead. It flies its path like any enemy but
    // spins on its own axis at SpinRate the whole time, ignoring Aim.
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
        [Tooltip("Degrees per second.")]
        [FormerlySerializedAs("AttackSpinRate")]
        public float SpinRate = 120f;

        protected override void OnSpawn()
        {
            StartCoroutine(VolleyRoutine());
        }

        protected override float NextRotation(float current, float dt) => current + SpinRate * dt;

        protected override void TryFire() { }

        private IEnumerator VolleyRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(VolleyInterval);
                if (Time.timeScale > 0f && IsOnScreen && NearestPlayer != null) FireVolley();
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
                missile.IgnoreEnemies();
                missile.SetHoming(HomingTurnRate, HomingDelay, HomingRange);
                rocket.GetComponent<Rigidbody2D>().linearVelocity = (Vector2)cannon.up * LaunchSpeed;
            }
        }
    }
}
