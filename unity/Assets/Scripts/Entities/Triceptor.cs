using UnityEngine;
using UnityEngine.Serialization;

namespace FlightRecord
{
    // Spins on its own axis at SpinRate the whole time, ignoring Aim, while it flies its path
    // like any enemy. Its weapon is a PatternEmitter on the prefab (4.13): a homing rocket
    // ring of three, aimed along its facing, so the volley turns with the spin and leaves
    // from where its three cannons point.
    public class Triceptor : Enemy
    {
        [Header("Spin")]
        [Tooltip("Degrees per second.")]
        [FormerlySerializedAs("AttackSpinRate")]
        public float SpinRate = 120f;

        protected override float NextRotation(float current, float dt) => current + SpinRate * dt;
    }
}
