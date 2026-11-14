using System.Collections;
using UnityEngine;

namespace FlightRecord
{
    // Orbits the player at medium range and fires a sustained directional beam instead of
    // missiles. The beam direction locks to transform.up at the moment of firing and the
    // satellite freezes in place for the beam's full duration -- no orbiting or rotation
    // while the beam is active, so the player has a stationary hazard to read and dodge.
    public class Satellite : Enemy
    {
        [Header("Beam")]
        [Tooltip("Seconds between beam firings.")]
        public float BeamInterval = 4f;
        [Tooltip("How long the fired beam stays active.")]
        public float BeamDuration = 1.5f;
        [Tooltip("World-unit length of the beam, extending from this satellite toward the player.")]
        public float BeamLength = 120f;
        [Tooltip("Visual and hitbox width of the beam.")]
        public float BeamWidth = 0.3f;
        public Color BeamColor = new Color(0.2f, 1f, 1f, 1f);

        private bool _beamActive;

        protected override void OnSpawn()
        {
            StartCoroutine(BeamRoutine());
        }

        // While the beam is active, suppress all movement and rotation so the satellite
        // holds its position. Base UpdateBehavior calls RotateTowardPlayer + Orbit.
        protected override void UpdateBehavior()
        {
            if (_beamActive) return;
            base.UpdateBehavior();
        }

        // Satellite has no standard missile weapon -- all damage comes through BeamRoutine.
        protected override void TryFire() { }

        private IEnumerator BeamRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(BeamInterval);

                if (Time.timeScale > 0f && _distance <= FiringRange)
                    FireBeam();
            }
        }

        private void FireBeam()
        {
            _beamActive = true;
            _rigidbody.linearVelocity = Vector2.zero;

            var beamGO = new GameObject("SatelliteBeam");
            beamGO.transform.position = transform.position;
            beamGO.transform.rotation = transform.rotation;
            beamGO.AddComponent<BeamEffect>().Initialize(
                _ship.RollFirePower(), BeamLength, BeamWidth, BeamDuration, BeamColor);

            StartCoroutine(BeamActiveRoutine());
        }

        private IEnumerator BeamActiveRoutine()
        {
            yield return new WaitForSeconds(BeamDuration);
            _beamActive = false;
        }
    }
}
