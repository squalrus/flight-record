using System;

namespace InfinitySpace
{
    public class SectorAchievementDef
    {
        public string Title;
        public string Description;
        public int RewardParts;
        public Func<Sector, bool> IsUnlocked;
    }

    // Catalog of all possible sector achievements. SectorAchievementsController displays
    // every entry (greyed out if locked) and awards RewardParts for the unlocked ones.
    public static class SectorAchievements
    {
        public static readonly SectorAchievementDef[] All =
        {
            new SectorAchievementDef
            {
                Title = "Sector Clear",
                Description = "Reach the portal and clear the sector.",
                RewardParts = 5,
                IsUnlocked = sector => sector != null,
            },
            new SectorAchievementDef
            {
                Title = "Natural Navigator",
                Description = "Discover the portal without purchasing a scan.",
                RewardParts = 10,
                IsUnlocked = sector => sector != null && sector.PortalDiscoveredNaturally,
            },
            new SectorAchievementDef
            {
                Title = "Pristine Run",
                Description = "Complete the sector without taking any damage.",
                RewardParts = 25,
                IsUnlocked = sector => sector != null && sector.DamageTaken == 0,
            },
            new SectorAchievementDef
            {
                Title = "Sharpshooter",
                Description = "Land over half of your shots fired this sector.",
                RewardParts = 5,
                IsUnlocked = sector => sector != null && sector.ShotsFired > 0 && sector.HitPercentage > 0.5f,
            },
            new SectorAchievementDef
            {
                Title = "Perfect Accuracy",
                Description = "Land every shot fired this sector.",
                RewardParts = 25,
                IsUnlocked = sector => sector != null && sector.ShotsFired > 0 && sector.HitPercentage >= 1f,
            },
            new SectorAchievementDef
            {
                Title = "Speedrun",
                Description = "Clear the sector in under 2 minutes.",
                RewardParts = 5,
                IsUnlocked = sector => sector != null && sector.ElapsedSeconds < 120f,
            },
            new SectorAchievementDef
            {
                Title = "Pacifist Drifter",
                Description = "Complete the sector without destroying any asteroids.",
                RewardParts = 5,
                IsUnlocked = sector => sector != null && sector.TotalAsteroids > 0 && sector.AsteroidsDestroyed == 0,
            },
            new SectorAchievementDef
            {
                Title = "Exterminator",
                Description = "Destroy every enemy in the sector.",
                RewardParts = 15,
                IsUnlocked = sector => sector != null && sector.TotalEnemies > 0 && sector.EnemiesDestroyed == sector.TotalEnemies,
            },
            new SectorAchievementDef
            {
                Title = "Clean Sweep",
                Description = "Destroy every asteroid in the sector.",
                RewardParts = 15,
                IsUnlocked = sector => sector != null && sector.TotalAsteroids > 0 && sector.AsteroidsDestroyed == sector.TotalAsteroids,
            },
            new SectorAchievementDef
            {
                Title = "No Survivors",
                Description = "Destroy every enemy and every asteroid in the sector.",
                RewardParts = 30,
                IsUnlocked = sector => sector != null
                    && sector.TotalEnemies > 0 && sector.EnemiesDestroyed == sector.TotalEnemies
                    && sector.TotalAsteroids > 0 && sector.AsteroidsDestroyed == sector.TotalAsteroids,
            },
            new SectorAchievementDef
            {
                Title = "Big Spender",
                Description = "Spend over 100 Parts in a single sector.",
                RewardParts = 10,
                IsUnlocked = sector => sector != null && sector.PartsSpent > 100f,
            },
            new SectorAchievementDef
            {
                Title = "Hoarder",
                Description = "Complete the sector without spending any Parts.",
                RewardParts = 10,
                IsUnlocked = sector => sector != null && sector.PartsSpent == 0f,
            },
            new SectorAchievementDef
            {
                Title = "Close Call",
                Description = "Survive the sector after dropping to 5 HP or below.",
                RewardParts = 20,
                IsUnlocked = sector => sector != null && sector.LowestHealthSeen <= 5,
            },
            new SectorAchievementDef
            {
                Title = "Scrap Collector",
                Description = "Collect every Part dropped in the sector.",
                RewardParts = 15,
                IsUnlocked = sector => sector != null && sector.PartsSpawned > 0 && sector.PartsCollected >= sector.PartsSpawned,
            },
            new SectorAchievementDef
            {
                Title = "Relic Hunter",
                Description = "Recover the relic and complete the sector.",
                RewardParts = 15,
                IsUnlocked = sector => sector != null && sector.Type == SectorType.Relic && sector.IsCleared,
            },
            new SectorAchievementDef
            {
                Title = "Threat Neutralized",
                Description = "Take down the large threat and complete the sector.",
                RewardParts = 25,
                IsUnlocked = sector => sector != null && sector.Type == SectorType.LargeThreat && sector.IsCleared,
            },
        };
    }
}
