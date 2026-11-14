using System.Collections;
using UnityEngine;

namespace FlightRecord
{
    public class Probe : Enemy
    {
        [Header("Orbit Randomization")]
        public Vector2 OrbitBurstIntervalRange = new Vector2(2f, 5f);
        public Vector2 OrbitBurstDurationRange = new Vector2(1f, 2f);
        public float OrbitBurstSpeedMultiplier = 2f;

        private float _orbitDirection = 1f;
        private float _orbitSpeedMultiplier = 1f;
        private Coroutine _orbitBurstCoroutine;

        protected override float OrbitTangentMultiplier => _orbitDirection * _orbitSpeedMultiplier;

        protected override void OnEnterCombat()
        {
            _orbitDirection = Random.value < 0.5f ? -1f : 1f;
            if (_orbitBurstCoroutine != null) StopCoroutine(_orbitBurstCoroutine);
            _orbitBurstCoroutine = StartCoroutine(OrbitBurstRoutine());
        }

        protected override void OnExitCombat()
        {
            if (_orbitBurstCoroutine != null) StopCoroutine(_orbitBurstCoroutine);
            _orbitSpeedMultiplier = 1f;
        }

        private IEnumerator OrbitBurstRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(OrbitBurstIntervalRange.x, OrbitBurstIntervalRange.y));
                _orbitSpeedMultiplier = OrbitBurstSpeedMultiplier;
                yield return new WaitForSeconds(Random.Range(OrbitBurstDurationRange.x, OrbitBurstDurationRange.y));
                _orbitSpeedMultiplier = 1f;
            }
        }
    }
}
