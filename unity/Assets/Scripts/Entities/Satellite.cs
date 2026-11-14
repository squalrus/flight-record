using System.Collections;
using UnityEngine;

namespace FlightRecord
{
    // Fires a sustained directional beam instead of missiles. The beam direction locks to
    // transform.up at the moment of firing, and for the beam's full duration the satellite
    // stops on its path and stops turning (it still rides the scroll, and the beam follows
    // it), so the player has a fixed hazard to read and dodge. Best set to aim at the
    // nearest player (EnemyAim.TrackNearestPlayer) and flown on a path that holds.
    public class Satellite : Enemy
    {
        [Header("Beam")]
        [Tooltip("Seconds between beam firings.")]
        public float BeamInterval = 4f;
        [Tooltip("How long the fired beam stays active.")]
        public float BeamDuration = 1.5f;
        [Tooltip("World-unit length of the beam, extending from this satellite along its facing.")]
        public float BeamLength = 120f;
        [Tooltip("Visual and hitbox width of the beam.")]
        public float BeamWidth = 0.3f;
        public Color BeamColor = new Color(0.2f, 1f, 1f, 1f);

        private bool _beamActive;

        protected override bool PathPaused => _beamActive;

        protected override void OnSpawn()
        {
            StartCoroutine(BeamRoutine());
        }

        protected override float NextRotation(float current, float dt) =>
            _beamActive ? current : base.NextRotation(current, dt);

        // Satellite has no standard missile weapon -- all damage comes through BeamRoutine.
        protected override void TryFire() { }

        private IEnumerator BeamRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(BeamInterval);

                if (Time.timeScale > 0f && IsOnScreen && NearestPlayer != null)
                    FireBeam();
            }
        }

        private void FireBeam()
        {
            _beamActive = true;

            var beamGO = new GameObject("SatelliteBeam");
            beamGO.transform.position = transform.position;
            beamGO.transform.rotation = transform.rotation;
            var beam = beamGO.AddComponent<BeamEffect>();
            beam.Initialize(_ship.RollFirePower(), BeamLength, BeamWidth, BeamDuration, BeamColor);
            beam.Follow(transform);

            StartCoroutine(BeamActiveRoutine());
        }

        private IEnumerator BeamActiveRoutine()
        {
            yield return new WaitForSeconds(BeamDuration);
            _beamActive = false;
        }
    }
}
