using UnityEngine;

namespace FlightRecord
{
    public interface ICanTakeDamage
    {
        void AddHealth(int health, Vector3? hitPoint = null);

        void Destruct();
    }
}
