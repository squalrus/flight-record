using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InfinitySpace
{
    // More negative than SectorSpawner's DefaultExecutionOrder(-100) so that on a fresh
    // app session (Continue from the Main Menu, where this singleton doesn't exist yet),
    // this Awake() -- and the Load() it now calls -- runs before SectorSpawner.Awake()
    // reads CurrentSector. Without this, SectorSpawner would always see the field's
    // default CurrentSector (1) and spawn Sector L0 regardless of the save file, since
    // Load() previously ran in OnEnable(), a phase that fires after every object's Awake().
    [DefaultExecutionOrder(-200)]
    public class GameControl : MonoBehaviour
    {
        public static GameControl control;

        public string ShipId = "R";
        public string AvatarGlyph = "v";
        public string PlayerName = "Player";

        public int Experience;
        public bool ControlsFlipped;
        public bool RumbleEnabled = true;
        public int CurrentSector = 1;
        public bool HasActiveRun = true;
        public float Parts;
        public int PickupAttractionLevel;
        public int HomingLevel;
        public int FireRateUpgradeLevel;
        public int HealthUpgradeLevel;
        public int FirePowerUpgradeLevel;
        public int FuelUpgradeLevel;
        public int AmmoUpgradeLevel;
        public float LastSectorTime;
        public float LifetimePlaytimeSeconds;

        // See PlayerStatistics for the -1 "not set" sentinel.
        public float CurrentFuel = -1f;
        public float CurrentAmmo = -1f;
        public int CurrentHealth = -1;

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
        public int LifetimeRunsStarted;
        public int LifetimeSectorsCleared;

        // Count of Part pickups spawned/collected (not their TransferrableParts value --
        // see PartsEarned for that). Not persisted to stats.json -- only ever read within
        // the sector that wrote them, the same as the snapshot fields below, just without
        // needing a Lifetime* counterpart. Incremented by Asteroid.Destruct() and
        // Part.OnCollected(); backs the "Scrap Collector" sector achievement.
        public int PartsSpawned;
        public int PartsCollected;

        // Current-run only (not persisted to stats.json directly) -- Sector snapshots
        // these at sector start and reports the delta on the Sector Complete screen.
        // Each setter below also accrues into its Lifetime* counterpart, the same
        // pattern ShotsFired/LifetimePartsEarned use for run-cumulative values.
        private int _hits;
        private int _damageDealt;
        private int _damageTaken;
        private float _fuelConsumed;

        public int Hits
        {
            get => _hits;
            set
            {
                int delta = value - _hits;
                if (delta > 0) LifetimeHits += delta;
                _hits = value;
            }
        }

        public int DamageDealt
        {
            get => _damageDealt;
            set
            {
                int delta = value - _damageDealt;
                if (delta > 0) LifetimeDamageDealt += delta;
                _damageDealt = value;
            }
        }

        public int DamageTaken
        {
            get => _damageTaken;
            set
            {
                int delta = value - _damageTaken;
                if (delta > 0) LifetimeDamageTaken += delta;
                _damageTaken = value;
            }
        }

        public float FuelConsumed
        {
            get => _fuelConsumed;
            set
            {
                float delta = value - _fuelConsumed;
                if (delta > 0) LifetimeFuelConsumed += delta;
                _fuelConsumed = value;
            }
        }

        private int _shotsFired;

        // Tracks LifetimeShotsFired alongside the current-run count, the same pattern
        // Ship.cs uses for Parts -> LifetimePartsEarned/Spent.
        public int ShotsFired
        {
            get => _shotsFired;
            set
            {
                int delta = value - _shotsFired;
                if (delta > 0) LifetimeShotsFired += delta;
                _shotsFired = value;
            }
        }

        // Run-cumulative counters for the Game Over run summary screen -- same accrual
        // pattern as ShotsFired/Hits above, just for the figures that previously only had
        // a Lifetime* total (enemies/asteroids destroyed, parts, playtime) with nothing
        // tracking "this run alone" across every sector played before death.
        private int _runEnemiesDestroyed;
        private int _runAsteroidsDestroyed;
        private float _runPartsEarned;
        private float _runPartsSpent;
        private float _runPlaytimeSeconds;

        public int RunEnemiesDestroyed
        {
            get => _runEnemiesDestroyed;
            set
            {
                int delta = value - _runEnemiesDestroyed;
                if (delta > 0) LifetimeEnemiesDestroyed += delta;
                _runEnemiesDestroyed = value;
            }
        }

        public int RunAsteroidsDestroyed
        {
            get => _runAsteroidsDestroyed;
            set
            {
                int delta = value - _runAsteroidsDestroyed;
                if (delta > 0) LifetimeAsteroidsDestroyed += delta;
                _runAsteroidsDestroyed = value;
            }
        }

        public float RunPartsEarned
        {
            get => _runPartsEarned;
            set
            {
                float delta = value - _runPartsEarned;
                if (delta > 0) LifetimePartsEarned += delta;
                _runPartsEarned = value;
            }
        }

        public float RunPartsSpent
        {
            get => _runPartsSpent;
            set
            {
                float delta = value - _runPartsSpent;
                if (delta > 0) LifetimePartsSpent += delta;
                _runPartsSpent = value;
            }
        }

        public float RunPlaytimeSeconds
        {
            get => _runPlaytimeSeconds;
            set
            {
                float delta = value - _runPlaytimeSeconds;
                if (delta > 0) LifetimePlaytimeSeconds += delta;
                _runPlaytimeSeconds = value;
            }
        }

        // Sectors successfully cleared this run -- CurrentSector is 1-based and already
        // incremented past the last cleared sector, so subtract 1 rather than reading it raw.
        public int RunSectorsCleared => Mathf.Max(0, CurrentSector - 1);

        public void AdvanceSector()
        {
            CurrentSector++;
            if (CurrentSector > HighestSectorReached) HighestSectorReached = CurrentSector;
            LifetimeSectorsCleared++;
            Save();
        }

        public void RecordSectorTime(float seconds)
        {
            LastSectorTime = seconds;
            RunPlaytimeSeconds += seconds;
        }

        // Captured by Player.Destruct() the moment the player dies, before TransferStatsAndClearRun()
        // zeroes the run counters above and before the Main scene (and its Sector instance) unloads.
        // GameOverController reads this across the scene transition to GameOver.unity since GameControl
        // is the only thing here that survives it.
        [System.Serializable]
        public class DeathSnapshot
        {
            // "PLAYER_DESTROYED" (Player.Destruct()) or "RELIC_DESTROYED" (Relic.Destruct(),
            // Sector Type: Relic) -- read by GameOverController to vary the mission-failed
            // subtitle without needing a separate screen per failure cause.
            public string FailureReason;
            public int SectorNumber;
            public string MissionText;
            public int EnemiesDestroyed;
            public int TotalEnemies;
            public int AsteroidsDestroyed;
            public int TotalAsteroids;
            public int ShotsFired;
            public int Hits;
            public float HitPercentage;
            public int DamageDealt;
            public int DamageTaken;
            public float FuelConsumed;
            public float ElapsedSeconds;
            public float PartsEarned;
            public float PartsSpent;

            public int RunSectorsCleared;
            public int RunShotsFired;
            public int RunHits;
            public float RunHitPercentage;
            public int RunDamageDealt;
            public int RunDamageTaken;
            public float RunFuelConsumed;
            public int RunEnemiesDestroyed;
            public int RunAsteroidsDestroyed;
            public float RunPartsEarned;
            public float RunPartsSpent;
            public float RunPlaytimeSeconds;
        }

        public DeathSnapshot LastDeath;

        public void CaptureDeathSnapshot(Sector sector, string failureReason)
        {
            LastDeath = new DeathSnapshot
            {
                FailureReason = failureReason,
                SectorNumber = CurrentSector,
                MissionText = sector != null ? sector.MissionText : "",
                EnemiesDestroyed = sector != null ? sector.EnemiesDestroyed : 0,
                TotalEnemies = sector != null ? sector.TotalEnemies : 0,
                AsteroidsDestroyed = sector != null ? sector.AsteroidsDestroyed : 0,
                TotalAsteroids = sector != null ? sector.TotalAsteroids : 0,
                ShotsFired = sector != null ? sector.ShotsFired : 0,
                Hits = sector != null ? sector.Hits : 0,
                HitPercentage = sector != null ? sector.HitPercentage : 0f,
                DamageDealt = sector != null ? sector.DamageDealt : 0,
                DamageTaken = sector != null ? sector.DamageTaken : 0,
                FuelConsumed = sector != null ? sector.FuelConsumed : 0f,
                ElapsedSeconds = sector != null ? sector.ElapsedSeconds : 0f,
                PartsEarned = sector != null ? sector.PartsEarned : 0f,
                PartsSpent = sector != null ? sector.PartsSpent : 0f,

                RunSectorsCleared = RunSectorsCleared,
                RunShotsFired = ShotsFired,
                RunHits = Hits,
                RunHitPercentage = ShotsFired == 0 ? 0f : (float)Hits / ShotsFired,
                RunDamageDealt = DamageDealt,
                RunDamageTaken = DamageTaken,
                RunFuelConsumed = FuelConsumed,
                RunEnemiesDestroyed = RunEnemiesDestroyed,
                RunAsteroidsDestroyed = RunAsteroidsDestroyed,
                RunPartsEarned = RunPartsEarned,
                RunPartsSpent = RunPartsSpent,
                RunPlaytimeSeconds = RunPlaytimeSeconds,
            };
        }

        private void Awake()
        {
            if (control == null)
            {
                DontDestroyOnLoad(gameObject);
                control = this;
                Load();
            }
            else if (control != this)
            {
                Destroy(gameObject);
            }
        }

        private bool _suppressSaveOnDisable;

        private void OnDisable()
        {
            if (_suppressSaveOnDisable) return;
            Save();
        }

        public void Save()
        {
            var stats = new PlayerStatistics
            {
                ShipId = ShipId,
                AvatarGlyph = AvatarGlyph,
                PlayerName = PlayerName,
                Experience = Experience,
                ShotsFired = ShotsFired,
                Hits = Hits,
                DamageDealt = DamageDealt,
                DamageTaken = DamageTaken,
                FuelConsumed = FuelConsumed,
                ControlsFlipped = ControlsFlipped,
                RumbleEnabled = RumbleEnabled,
                CurrentSector = CurrentSector,
                HasActiveRun = HasActiveRun,
                Parts = Parts,
                PickupAttractionLevel = PickupAttractionLevel,
                HomingLevel = HomingLevel,
                FireRateUpgradeLevel = FireRateUpgradeLevel,
                HealthUpgradeLevel = HealthUpgradeLevel,
                FirePowerUpgradeLevel = FirePowerUpgradeLevel,
                FuelUpgradeLevel = FuelUpgradeLevel,
                AmmoUpgradeLevel = AmmoUpgradeLevel,
                LastSectorTime = LastSectorTime,
                LifetimePlaytimeSeconds = LifetimePlaytimeSeconds,
                CurrentFuel = CurrentFuel,
                CurrentAmmo = CurrentAmmo,
                CurrentHealth = CurrentHealth,
                LifetimePartsEarned = LifetimePartsEarned,
                LifetimePartsSpent = LifetimePartsSpent,
                LifetimeShotsFired = LifetimeShotsFired,
                LifetimeHits = LifetimeHits,
                LifetimeDamageDealt = LifetimeDamageDealt,
                LifetimeDamageTaken = LifetimeDamageTaken,
                LifetimeFuelConsumed = LifetimeFuelConsumed,
                LifetimeEnemiesDestroyed = LifetimeEnemiesDestroyed,
                LifetimeAsteroidsDestroyed = LifetimeAsteroidsDestroyed,
                HighestSectorReached = HighestSectorReached,
                LifetimeRunsStarted = LifetimeRunsStarted,
                LifetimeSectorsCleared = LifetimeSectorsCleared,
                RunEnemiesDestroyed = RunEnemiesDestroyed,
                RunAsteroidsDestroyed = RunAsteroidsDestroyed,
                RunPartsEarned = RunPartsEarned,
                RunPartsSpent = RunPartsSpent,
                RunPlaytimeSeconds = RunPlaytimeSeconds,
            };
            File.WriteAllText(Application.persistentDataPath + "/stats.json", JsonUtility.ToJson(stats));
        }

        // Called once, by Player.Destruct() on the player ship, the moment Game Over happens.
        // Lifetime totals are already current -- Hits/DamageDealt/DamageTaken/FuelConsumed/
        // ShotsFired/Parts all accrue into their Lifetime* counterpart via their setters as
        // they change during play, not just at Save() time. This only needs to reset the
        // current-run fields back to "no run in progress" before saving, so the next
        // MainMenu load hides Continue (see HasActiveRun) instead of resuming a dead run.
        public void TransferStatsAndClearRun()
        {
            CurrentSector = 1;
            HasActiveRun = false;
            Parts = 0;
            PickupAttractionLevel = 0;
            HomingLevel = 0;
            FireRateUpgradeLevel = 0;
            HealthUpgradeLevel = 0;
            FirePowerUpgradeLevel = 0;
            FuelUpgradeLevel = 0;
            AmmoUpgradeLevel = 0;
            LastSectorTime = 0;
            CurrentFuel = -1f;
            CurrentAmmo = -1f;
            CurrentHealth = -1;
            Experience = 0;
            _shotsFired = 0;
            _hits = 0;
            _damageDealt = 0;
            _damageTaken = 0;
            _fuelConsumed = 0;
            _runEnemiesDestroyed = 0;
            _runAsteroidsDestroyed = 0;
            _runPartsEarned = 0;
            _runPartsSpent = 0;
            _runPlaytimeSeconds = 0;
            Save();
        }

        // Shared by every "the run just ended" path -- originally only Player.Destruct() on
        // player death, now also Relic.Destruct() (Sector Type: Relic) -- so both capture
        // the snapshot, clear the run, and load GameOver the same way instead of duplicating
        // the three calls at each call site.
        public void FailRun(Sector sector, string failureReason)
        {
            CaptureDeathSnapshot(sector, failureReason);
            TransferStatsAndClearRun();
            SceneManager.LoadScene("GameOver");
        }

        // Resets current-run fields but preserves lifetime totals, so New Game doesn't
        // wipe career progression. Static (and reads the file directly, not GameControl.control)
        // because it's called from the Start scene's Main Menu, before GameControl's singleton
        // GameObject (which only lives in Main.unity) has ever been created this session.
        // Takes the Player Profile Setup screen's picks so they're on disk even on a brand
        // new save file -- GameControl.Load() then picks them up once Main.unity loads.
        // Pilot name entry isn't implemented yet (see BACKLOG.md), so PlayerName is left at
        // its PlayerStatistics default here.
        public static void ClearSaveData(string shipId, string avatarGlyph)
        {
            var saveFile = Application.persistentDataPath + "/stats.json";
            var existing = File.Exists(saveFile)
                ? JsonUtility.FromJson<PlayerStatistics>(File.ReadAllText(saveFile))
                : new PlayerStatistics();

            var reset = new PlayerStatistics
            {
                LifetimePartsEarned = existing.LifetimePartsEarned,
                LifetimePartsSpent = existing.LifetimePartsSpent,
                LifetimeShotsFired = existing.LifetimeShotsFired,
                LifetimeHits = existing.LifetimeHits,
                LifetimeDamageDealt = existing.LifetimeDamageDealt,
                LifetimeDamageTaken = existing.LifetimeDamageTaken,
                LifetimeFuelConsumed = existing.LifetimeFuelConsumed,
                LifetimeEnemiesDestroyed = existing.LifetimeEnemiesDestroyed,
                LifetimeAsteroidsDestroyed = existing.LifetimeAsteroidsDestroyed,
                LifetimePlaytimeSeconds = existing.LifetimePlaytimeSeconds,
                HighestSectorReached = existing.HighestSectorReached,
                LifetimeRunsStarted = existing.LifetimeRunsStarted + 1,
                LifetimeSectorsCleared = existing.LifetimeSectorsCleared,
                ShipId = shipId,
                AvatarGlyph = avatarGlyph,
                HasActiveRun = true,
            };
            File.WriteAllText(saveFile, JsonUtility.ToJson(reset));
        }

        // Deletes stats.json entirely (lifetime totals included) and tears down the live
        // singleton, so nothing re-writes the file from stale in-memory state afterwards
        // (OnDisable() would otherwise call Save() the moment this GameObject is destroyed).
        // Called from the Settings screen's "Clear Saved Data" button.
        public static void ClearAllSaveData()
        {
            var saveFile = Application.persistentDataPath + "/stats.json";
            if (File.Exists(saveFile)) File.Delete(saveFile);

            if (control != null)
            {
                control._suppressSaveOnDisable = true;
                Destroy(control.gameObject);
                control = null;
            }
        }

        // Static read/write pair so the Settings screen's Rumble toggle works from the Start
        // scene's Main Menu too, before GameControl's singleton GameObject (which only lives
        // in Main.unity) has ever been created this session -- mirrors ClearSaveData()'s
        // direct file I/O above. When control exists, callers should prefer the instance
        // RumbleEnabled field instead (kept in sync with disk via Save()/Load()).
        public static bool GetRumbleEnabled()
        {
            var saveFile = Application.persistentDataPath + "/stats.json";
            if (!File.Exists(saveFile)) return true;
            return JsonUtility.FromJson<PlayerStatistics>(File.ReadAllText(saveFile)).RumbleEnabled;
        }

        public static void SetRumbleEnabled(bool enabled)
        {
            var saveFile = Application.persistentDataPath + "/stats.json";
            var stats = File.Exists(saveFile)
                ? JsonUtility.FromJson<PlayerStatistics>(File.ReadAllText(saveFile))
                : new PlayerStatistics();
            stats.RumbleEnabled = enabled;
            File.WriteAllText(saveFile, JsonUtility.ToJson(stats));
        }

        public void Load()
        {
            var saveFile = Application.persistentDataPath + "/stats.json";
            if (File.Exists(saveFile))
            {
                var stats = JsonUtility.FromJson<PlayerStatistics>(File.ReadAllText(saveFile));
                ShipId = stats.ShipId;
                AvatarGlyph = stats.AvatarGlyph;
                PlayerName = stats.PlayerName;
                _shotsFired = stats.ShotsFired;
                _hits = stats.Hits;
                _damageDealt = stats.DamageDealt;
                _damageTaken = stats.DamageTaken;
                _fuelConsumed = stats.FuelConsumed;
                Experience = stats.Experience;
                ControlsFlipped = stats.ControlsFlipped;
                RumbleEnabled = stats.RumbleEnabled;
                CurrentSector = stats.CurrentSector;
                HasActiveRun = stats.HasActiveRun;
                Parts = stats.Parts;
                PickupAttractionLevel = stats.PickupAttractionLevel;
                HomingLevel = stats.HomingLevel;
                FireRateUpgradeLevel = stats.FireRateUpgradeLevel;
                HealthUpgradeLevel = stats.HealthUpgradeLevel;
                FirePowerUpgradeLevel = stats.FirePowerUpgradeLevel;
                FuelUpgradeLevel = stats.FuelUpgradeLevel;
                AmmoUpgradeLevel = stats.AmmoUpgradeLevel;
                LastSectorTime = stats.LastSectorTime;
                LifetimePlaytimeSeconds = stats.LifetimePlaytimeSeconds;
                CurrentFuel = stats.CurrentFuel;
                CurrentAmmo = stats.CurrentAmmo;
                CurrentHealth = stats.CurrentHealth;
                LifetimePartsEarned = stats.LifetimePartsEarned;
                LifetimePartsSpent = stats.LifetimePartsSpent;
                LifetimeShotsFired = stats.LifetimeShotsFired;
                LifetimeHits = stats.LifetimeHits;
                LifetimeDamageDealt = stats.LifetimeDamageDealt;
                LifetimeDamageTaken = stats.LifetimeDamageTaken;
                LifetimeFuelConsumed = stats.LifetimeFuelConsumed;
                LifetimeEnemiesDestroyed = stats.LifetimeEnemiesDestroyed;
                LifetimeAsteroidsDestroyed = stats.LifetimeAsteroidsDestroyed;
                HighestSectorReached = stats.HighestSectorReached;
                LifetimeRunsStarted = stats.LifetimeRunsStarted;
                LifetimeSectorsCleared = stats.LifetimeSectorsCleared;
                _runEnemiesDestroyed = stats.RunEnemiesDestroyed;
                _runAsteroidsDestroyed = stats.RunAsteroidsDestroyed;
                _runPartsEarned = stats.RunPartsEarned;
                _runPartsSpent = stats.RunPartsSpent;
                _runPlaytimeSeconds = stats.RunPlaytimeSeconds;
            }
        }
    }
}
