using System.Collections.Generic;
using UnityEngine;

namespace InfinitySpace
{
    [System.Serializable]
    public class SectorTypeOption
    {
        public SectorType Type;
        public int MinSector = 1;
        public int MaxSector = 999;
        [Range(0f, 1f)] public float Weight = 1f;
    }

    [System.Serializable]
    public class SectorTierOption
    {
        public GameObject Prefab;
        public int MinSector = 1;
        public int MaxSector = 999;
        [Range(0f, 1f)] public float Weight = 1f;
    }

    // Asset-based config read by SectorSpawner to weighted-pick both the tier (Sector prefab --
    // L0/L1/L2/etc.) and the SectorType for the sector about to spawn. Kept as data rather than
    // code so probability/range tuning doesn't need a script change -- mirrors into
    // sector-reference.html like other baked Inspector values. Tiers and Types are picked
    // independently: Sector.ApplyType() works the same regardless of which tier prefab was
    // instantiated, so a tier's spawn composition and its objective type don't need to be coupled.
    [CreateAssetMenu(fileName = "SectorTypeTable", menuName = "InfinitySpace/Sector Type Table")]
    public class SectorTypeTable : ScriptableObject
    {
        public SectorTierOption[] Tiers;
        public SectorTypeOption[] Options;

        public GameObject PickTier(int currentSector)
        {
            var candidates = new List<SectorTierOption>();
            float totalWeight = 0f;

            if (Tiers != null)
            {
                foreach (var tier in Tiers)
                {
                    if (tier == null || tier.Prefab == null) continue;
                    if (currentSector < tier.MinSector || currentSector > tier.MaxSector) continue;
                    if (tier.Weight <= 0f) continue;

                    candidates.Add(tier);
                    totalWeight += tier.Weight;
                }
            }

            if (candidates.Count == 0) return null;

            float roll = Random.Range(0f, totalWeight);
            float cumulative = 0f;
            foreach (var tier in candidates)
            {
                cumulative += tier.Weight;
                if (roll <= cumulative) return tier.Prefab;
            }

            return candidates[candidates.Count - 1].Prefab;
        }

        public SectorType Pick(int currentSector)
        {
            var candidates = new List<SectorTypeOption>();
            float totalWeight = 0f;

            if (Options != null)
            {
                foreach (var option in Options)
                {
                    if (option == null) continue;
                    if (currentSector < option.MinSector || currentSector > option.MaxSector) continue;
                    if (option.Weight <= 0f) continue;

                    candidates.Add(option);
                    totalWeight += option.Weight;
                }
            }

            if (candidates.Count == 0) return SectorType.ClearHostiles;

            float roll = Random.Range(0f, totalWeight);
            float cumulative = 0f;
            foreach (var option in candidates)
            {
                cumulative += option.Weight;
                if (roll <= cumulative) return option.Type;
            }

            return candidates[candidates.Count - 1].Type;
        }
    }
}
