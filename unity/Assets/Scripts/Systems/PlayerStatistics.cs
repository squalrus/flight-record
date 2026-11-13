using System;

namespace InfinitySpace
{
    [Serializable]
    public class PlayerStatistics
    {
        // Set on New Game via the Player Profile Setup screen, then carried through
        // Continue. Ship/avatar are cosmetic icon glyph picks (Infinity Icons HU font) --
        // no per-ship stat presets exist yet, see Ship Selection Screen backlog item.
        public string ShipId = "R";
        public string AvatarGlyph = "v";
        public string PlayerName = "Player";

        // Current run -- reset on New Game.
        public int Experience;

        public int ShotsFired;

        public int Hits;

        public int DamageDealt;

        public int DamageTaken;

        public float FuelConsumed;

        public bool ControlsFlipped;

        public bool RumbleEnabled = true;

        public int CurrentSector = 1;

        // False until a New Game starts a run, false again once that run ends in Game Over.
        // MainMenuController reads this (not just File.Exists) to decide whether Continue
        // should be enabled -- stats.json exists permanently once Lifetime* totals exist,
        // so file presence alone can't tell "mid-run" apart from "last run already ended."
        public bool HasActiveRun;

        public float Parts;

        public int PickupAttractionLevel;

        public int HomingLevel;

        public int FireRateUpgradeLevel;

        public int HealthUpgradeLevel;

        public int FirePowerUpgradeLevel;

        public int FuelUpgradeLevel;

        public int AmmoUpgradeLevel;

        public float LastSectorTime;

        // Sum of LastSectorTime across every completed sector this save -- an approximation
        // of total playtime (excludes time spent in Ship Management/menus and any sector
        // not completed before death).
        public float LifetimePlaytimeSeconds;

        // Carried over from the ship the player had at the last successful sector transition.
        // -1 is a sentinel meaning "not set" -- Player.ApplyCarryOverStats() falls back to its Max* values,
        // which is also what a fresh New Game gets since ClearSaveData() doesn't copy these.
        public float CurrentFuel = -1f;
        public float CurrentAmmo = -1f;
        public int CurrentHealth = -1;

        // Lifetime -- accumulates across New Games.
        public float LifetimePartsEarned;

        public float LifetimePartsSpent;

        public int LifetimeShotsFired;

        public int LifetimeHits;

        public int LifetimeDamageDealt;

        public int LifetimeDamageTaken;

        public float LifetimeFuelConsumed;

        public int LifetimeEnemiesDestroyed;

        public int LifetimeAsteroidsDestroyed;

        public int HighestSectorReached;

        // Companion counters to HighestSectorReached -- total runs started (incremented on
        // each New Game) and total sectors cleared summed across every run, not just the
        // highest reached in any single run.
        public int LifetimeRunsStarted;

        public int LifetimeSectorsCleared;

        // Current run -- reset on New Game/death, same lifecycle as ShotsFired/Hits above.
        // Backs the Game Over run summary screen, since EnemiesDestroyed/AsteroidsDestroyed/
        // Parts/Playtime previously only had Lifetime* totals with nothing tracking "this
        // run alone" across every sector played before death.
        public int RunEnemiesDestroyed;

        public int RunAsteroidsDestroyed;

        public float RunPartsEarned;

        public float RunPartsSpent;

        public float RunPlaytimeSeconds;
    }
}
