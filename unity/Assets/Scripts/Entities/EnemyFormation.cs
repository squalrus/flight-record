using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlightRecord
{
    // A group of enemies that fly one path together (backlog 4.12), authored as data. Each
    // slot is one enemy. Offset places it relative to the path, in world units, and the
    // whole group keeps that shape as it moves (a V, a line, a column). Delay starts it that
    // many seconds behind the first, so slots with zero offset and rising delays fly the
    // path nose to tail (a snake). Mix both for staggered rows. Offsets are flipped with
    // the path when a spawn mirrors it top to bottom.
    [CreateAssetMenu(fileName = "Formation", menuName = "Flight Record/Enemy Formation")]
    public class EnemyFormation : ScriptableObject
    {
        [Serializable]
        public struct Slot
        {
            [Tooltip("World units from the path, kept as the group moves.")]
            public Vector2 Offset;
            [Tooltip("Seconds behind the start of the path.")]
            [Min(0f)] public float Delay;
        }

        public List<Slot> Slots = new List<Slot> { new Slot() };
    }
}
