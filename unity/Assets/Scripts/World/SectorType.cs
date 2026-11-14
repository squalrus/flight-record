using UnityEngine;

namespace FlightRecord
{
    public enum SectorType
    {
        ClearHostiles,
        Relic,
        LargeThreat,
    }

    // Mission text shown by MissionBriefingController/GameOverController for each type --
    // Sector.MissionText computes from this directly, so those controllers keep reading
    // Sector.MissionText unchanged regardless of type.
    public static class SectorTypeText
    {
        public static string MissionFor(SectorType type)
        {
            switch (type)
            {
                case SectorType.Relic:
                    return "RECOVER THE RELIC";
                case SectorType.LargeThreat:
                    return "ELIMINATE THE THREAT";
                default:
                    return "CLEAR THE HOSTILES";
            }
        }

        // Read by MissionBriefingController alongside MissionFor() -- a one-line summary of
        // the win condition, shown beneath the mission name. ClearHostiles takes the sector's
        // own required-kill percentage since that varies per sector; Relic's objective is a
        // fixed pass/fail with no partial credit; LargeThreat is a kill-all over its own
        // dedicated entity list (Sector._largeThreatEntities), not a percentage.
        public static string DescriptionFor(SectorType type, float requiredFraction)
        {
            switch (type)
            {
                case SectorType.Relic:
                    return "Recover the relic and find the portal.";
                case SectorType.LargeThreat:
                    return "Destroy the heavy threat and its escort, then find the portal.";
                default:
                    return $"Clear {Mathf.RoundToInt(requiredFraction * 100f)}% of the threats and find the portal.";
            }
        }
    }
}
