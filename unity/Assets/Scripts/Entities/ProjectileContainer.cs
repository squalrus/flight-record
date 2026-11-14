using UnityEngine;

namespace FlightRecord
{
    // Marks the scene's "Projectiles" GameObject so Ship/Triceptor can parent spawned missiles
    // under it without a prefab holding a hard reference to that scene instance -- prefabs (Player,
    // every enemy) can't serialize a reference to a specific scene object, only to other assets.
    // Same static-instance shape as ResourceAlertController.
    public class ProjectileContainer : MonoBehaviour
    {
        public static Transform Instance { get; private set; }

        private void OnEnable() => Instance = transform;
        private void OnDisable() => Instance = null;
    }
}
