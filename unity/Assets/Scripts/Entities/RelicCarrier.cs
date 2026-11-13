using UnityEngine;

namespace InfinitySpace
{
    // Added at runtime by Sector.SeedRelicCarrier() to whichever spawned enemy or asteroid
    // is chosen to carry the relic -- not its own prefab, since any spawned entity can host it.
    public class RelicCarrier : MonoBehaviour
    {
        private Sector _sector;

        public void Initialize(Sector sector)
        {
            _sector = sector;
        }

        // Fires when the host's Destruct() calls Destroy(gameObject) -- but OnDestroy also
        // fires when the whole scene tears down (sector reload, GameOver on death, stopping
        // Play Mode), not just on an in-combat kill. If the carrier is still alive when e.g.
        // the player dies to something else, Main.unity unloads with the carrier still in
        // it, and without this guard SpawnRelicPickup() would Instantiate() a fresh
        // Relic(Clone) mid-teardown -- Unity's "objects not cleaned up when closing the
        // scene" warning. gameObject.scene.isLoaded goes false before any object's OnDestroy
        // runs during a scene unload, so it distinguishes that case from a real kill.
        private void OnDestroy()
        {
            if (!gameObject.scene.isLoaded) return;
            if (_sector != null) _sector.SpawnRelicPickup(transform.position);
        }
    }
}
