using UnityEngine;

namespace InfinitySpace
{
    // Attach to a GameObject in Main.unity. Instantiates the Sector prefab matching
    // GameControl.CurrentSector by weighted-picking from _sectorTypeTable.Tiers (see
    // SectorTypeTable.PickTier) -- e.g. Sector L0 for the player's first sector after a
    // New Game, Sector L1 for sectors 2 through 4, Sector L2 from sector 6 onward, with
    // Sector Triceptor LT inserted at sector 5 specifically. Tier/sector-number assignment
    // is Inspector-editable data on the SectorTypeTable.asset, not hardcoded thresholds
    // (see BACKLOG.md "Level Difficulty Scaling" for the eventual per-stat scaling this
    // tier ladder is standing in for). Runs in Awake (before any other script's
    // Start/OnEnable) so the spawned Sector exists in time for VectorHudController
    // to find it via FindObjectOfType.
    [DefaultExecutionOrder(-100)]
    public class SectorSpawner : MonoBehaviour
    {
        [SerializeField] private SectorTypeTable _sectorTypeTable;

        private void Awake()
        {
            int currentSector = GameControl.control != null ? GameControl.control.CurrentSector : 1;
            var prefab = _sectorTypeTable != null ? _sectorTypeTable.PickTier(currentSector) : null;
            if (prefab == null) return;

            var instance = Instantiate(prefab, Vector3.zero, Quaternion.identity);

            // Applied here, before the new Sector's own Start() runs (all Awake()s finish
            // before any Start() does), so type-dependent spawning -- e.g. seeding a relic
            // carrier -- sees the chosen type from the start.
            var type = _sectorTypeTable != null ? _sectorTypeTable.Pick(currentSector) : SectorType.ClearHostiles;
            instance.GetComponent<Sector>()?.ApplyType(type);
        }
    }
}
