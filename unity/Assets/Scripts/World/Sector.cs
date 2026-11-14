using System.Collections.Generic;
using UnityEngine;

namespace FlightRecord
{
    [System.Serializable]
    public class EntitySpawn
    {
        public GameObject Prefab;
        [Range(0, 100)] public int Count = 5;
        [Range(1, 100)] public int Spread = 80;
    }

    public class Sector : MonoBehaviour
    {
        [SerializeField] float _size = 50f;
        [SerializeField] private GameObject _portalPrefab;
        [SerializeField] List<EntitySpawn> _entities = new List<EntitySpawn>();

        [Header("Boundary Marker (Asteroid Belt)")]
        [Tooltip("Purely decorative ring placed at the sector edge so players can see where the play area ends.")]
        [SerializeField] private GameObject _boundaryMarkerPrefab;
        [SerializeField, Range(0, 800)] private int _boundaryMarkerCount = 40;
        [Tooltip("Random outward variance applied to each marker's ring radius, as a fraction of the radius, so the ring reads as organic debris rather than a perfect circle. Markers never spawn closer than the radius itself -- only farther out.")]
        [SerializeField, Range(0f, 0.5f)] private float _boundaryRingJitter = 0.08f;
        [Tooltip("Degrees/second the entire ring slowly revolves around the sector center, independent of each marker's own self-rotation.")]
        [SerializeField] private float _boundaryRingRevolveSpeed = 0.5f;

        // Read by MissionBriefingController at sector start. Purely a function of Type --
        // see SectorTypeText.MissionFor() -- so it's computed rather than a serialized field
        // that ApplyType() would just have to overwrite before Start() runs.
        public string MissionText => SectorTypeText.MissionFor(Type);

        // Read by MissionBriefingController alongside MissionText. ClearHostiles needs
        // RequiredFraction (set below), which is why this isn't computed inline in
        // SectorTypeText like MissionFor() is.
        public string MissionDescription => SectorTypeText.DescriptionFor(Type, RequiredFraction);

        [Header("Completion (Sector Type: ClearHostiles)")]
        [Tooltip("Percentage of spawned enemies that must be destroyed before the exit portal opens.")]
        [SerializeField, Range(1, 100)] private int _completionPercent = 100;

        [Header("Portal Timeout")]
        [Tooltip("Seconds from sector start before the portal vanishes and the sector fails, regardless of objective progress.")]
        [SerializeField] private float _timeoutSeconds = 600f;
        private bool _timedOut;

        [Header("Relic Objective (Sector Type: Relic)")]
        [Tooltip("Spawned where the seeded relic carrier dies. Only used when Type == Relic.")]
        [SerializeField] private GameObject _relicPickupPrefab;
        private bool _relicCollected;

        [Header("Large Threat Objective (Sector Type: LargeThreat)")]
        [Tooltip("Enemies that must ALL be destroyed to clear the sector when Type == LargeThreat (e.g. the boss plus its escort). Spawned in addition to the base _entities list above, which still spawns normally but is optional flavor/cover -- not required for completion.")]
        [SerializeField] private List<EntitySpawn> _largeThreatEntities = new List<EntitySpawn>();
        private readonly List<Enemy> _largeThreatEnemies = new List<Enemy>();
        private int _totalLargeThreatEnemies;

        public SectorType Type { get; private set; } = SectorType.ClearHostiles;

        // Called by SectorSpawner right after Instantiate, before this Sector's own Start()
        // runs -- see SectorSpawner.Awake().
        public void ApplyType(SectorType type)
        {
            Type = type;
        }

        private Transform _boundaryRing;

        private readonly List<Enemy> _spawnedEnemies = new List<Enemy>();
        private readonly List<Asteroid> _spawnedAsteroids = new List<Asteroid>();
        private int _totalEnemies;
        private int _totalAsteroids;
        private Portal _portal;

        public float CompletionFraction { get; private set; } = 1f;
        public float RequiredFraction => _completionPercent / 100f;

        // LargeThreat ignores CompletionFraction/RequiredFraction entirely -- it's a kill-all
        // condition over _largeThreatEnemies rather than a percentage of the base _entities
        // list, which is optional flavor/cover for that type rather than the objective itself.
        public bool IsCleared => Type switch
        {
            SectorType.Relic => _relicCollected,
            SectorType.LargeThreat => _totalLargeThreatEnemies > 0 && _largeThreatEnemies.Count == 0,
            _ => CompletionFraction >= RequiredFraction,
        };

        // Read by VectorHudController for the "PORTAL PROGRESS" HUD bar so it doesn't need
        // to know about SectorType itself -- Relic has no partial credit (a single pickup
        // is either found or not), unlike the enemy-kill ratio ClearHostiles/LargeThreat track.
        public float ObjectiveFraction => Type switch
        {
            SectorType.Relic => _relicCollected ? 1f : 0f,
            SectorType.LargeThreat => _totalLargeThreatEnemies == 0
                ? 1f
                : Mathf.Clamp01(1f - (float)_largeThreatEnemies.Count / _totalLargeThreatEnemies),
            _ => RequiredFraction <= 0f ? 1f : Mathf.Clamp01(CompletionFraction / RequiredFraction),
        };

        public string ObjectiveStatusText => Type switch
        {
            SectorType.Relic => _relicCollected ? "RELIC RECOVERED" : "RELIC NOT RECOVERED",
            SectorType.LargeThreat => $"THREATS REMAINING {_largeThreatEnemies.Count}/{_totalLargeThreatEnemies}",
            _ => $"PORTAL PROGRESS {Mathf.RoundToInt(ObjectiveFraction * 100f)}%",
        };
        public int EnemiesDestroyed { get; private set; }
        public int AsteroidsDestroyed { get; private set; }
        public int TotalEnemies => _totalEnemies;
        public int TotalAsteroids => _totalAsteroids;

        // True unless the player purchased the Portal Locator scan (Player.ScanForPortal) this
        // sector -- backs the "Natural Navigator" sector achievement.
        public bool PortalDiscoveredNaturally => _portal == null || !_portal.WasScanned;

        // Snapshot of GameControl's lifetime earned/spent totals at sector start, so the
        // sector-complete screen can show each figure for this sector alone rather than
        // the lifetime total or just their net (which hid how much was spent in-flight).
        private float _partsEarnedAtStart;
        private float _partsSpentAtStart;
        public float PartsEarned => GameControl.control != null ? GameControl.control.LifetimePartsEarned - _partsEarnedAtStart : 0f;
        public float PartsSpent => GameControl.control != null ? GameControl.control.LifetimePartsSpent - _partsSpentAtStart : 0f;

        // Same start-of-sector snapshot pattern as Parts above, for the other run-cumulative
        // counters GameControl tracks (see GameControl.cs).
        private int _shotsFiredAtStart;
        private int _hitsAtStart;
        private int _damageDealtAtStart;
        private int _damageTakenAtStart;
        private float _fuelConsumedAtStart;
        private int _partsSpawnedAtStart;
        private int _partsCollectedAtStart;
        public int ShotsFired => GameControl.control != null ? GameControl.control.ShotsFired - _shotsFiredAtStart : 0;
        public int Hits => GameControl.control != null ? GameControl.control.Hits - _hitsAtStart : 0;
        public float HitPercentage => ShotsFired == 0 ? 0f : (float)Hits / ShotsFired;
        public int DamageDealt => GameControl.control != null ? GameControl.control.DamageDealt - _damageDealtAtStart : 0;
        public int DamageTaken => GameControl.control != null ? GameControl.control.DamageTaken - _damageTakenAtStart : 0;
        public float FuelConsumed => GameControl.control != null ? GameControl.control.FuelConsumed - _fuelConsumedAtStart : 0f;
        public int PartsSpawned => GameControl.control != null ? GameControl.control.PartsSpawned - _partsSpawnedAtStart : 0;
        public int PartsCollected => GameControl.control != null ? GameControl.control.PartsCollected - _partsCollectedAtStart : 0;

        // Lazily resolved once and cached. Backs the "Close Call" sector achievement;
        // read after the sector ends, while the player's Player instance is still alive
        // in the scene (achievements show before the next scene load).
        private Player _ship;
        public int LowestHealthSeen
        {
            get
            {
                if (_ship == null) _ship = GameObject.Find("Player")?.GetComponent<Player>();
                return _ship != null ? _ship.LowestHealthThisLife : int.MaxValue;
            }
        }

        // Time.time stops advancing once Time.timeScale is set to 0 (the portal transition
        // does this), so reading this after the sector ends gives a stable completion time.
        private float _startTime;
        public float ElapsedSeconds => Time.time - _startTime;
        public float TimeRemaining => Mathf.Max(0f, _timeoutSeconds - ElapsedSeconds);

        private void Start()
        {
            foreach (var entity in _entities)
                SpawnEntities(entity);

            foreach (var entity in _largeThreatEntities)
                SpawnEntities(entity, isLargeThreatObjective: true);

            _totalEnemies = _spawnedEnemies.Count;
            _totalAsteroids = _spawnedAsteroids.Count;
            _totalLargeThreatEnemies = _largeThreatEnemies.Count;
            CompletionFraction = _totalEnemies == 0 ? 1f : 0f;
            _partsEarnedAtStart = GameControl.control != null ? GameControl.control.LifetimePartsEarned : 0f;
            _partsSpentAtStart = GameControl.control != null ? GameControl.control.LifetimePartsSpent : 0f;
            if (GameControl.control != null)
            {
                _shotsFiredAtStart = GameControl.control.ShotsFired;
                _hitsAtStart = GameControl.control.Hits;
                _damageDealtAtStart = GameControl.control.DamageDealt;
                _damageTakenAtStart = GameControl.control.DamageTaken;
                _fuelConsumedAtStart = GameControl.control.FuelConsumed;
                _partsSpawnedAtStart = GameControl.control.PartsSpawned;
                _partsCollectedAtStart = GameControl.control.PartsCollected;
            }
            _startTime = Time.time;

            SpawnPortal();
            SpawnBoundaryRing();

            if (Type == SectorType.Relic) SeedRelicCarrier();
        }

        private void Update()
        {
            if (!_timedOut && TimeRemaining <= 0f)
            {
                _timedOut = true;
                if (_portal != null) Destroy(_portal.gameObject);
                GameControl.control?.FailRun(this, "PORTAL_TIMEOUT");
                return;
            }

            if (_boundaryRing != null)
                _boundaryRing.Rotate(0f, 0f, _boundaryRingRevolveSpeed * Time.deltaTime);

            if (_totalAsteroids > 0)
            {
                _spawnedAsteroids.RemoveAll(asteroid => asteroid == null);
                int destroyed = _totalAsteroids - _spawnedAsteroids.Count;
                int delta = destroyed - AsteroidsDestroyed;
                if (delta > 0 && GameControl.control != null) GameControl.control.RunAsteroidsDestroyed += delta;
                AsteroidsDestroyed = destroyed;
            }

            if (_totalLargeThreatEnemies > 0)
                _largeThreatEnemies.RemoveAll(enemy => enemy == null);

            if (_totalEnemies == 0) return;
            _spawnedEnemies.RemoveAll(enemy => enemy == null);
            int enemiesDestroyed = _totalEnemies - _spawnedEnemies.Count;
            int enemyDelta = enemiesDestroyed - EnemiesDestroyed;
            if (enemyDelta > 0 && GameControl.control != null) GameControl.control.RunEnemiesDestroyed += enemyDelta;
            EnemiesDestroyed = enemiesDestroyed;
            CompletionFraction = 1f - (float)_spawnedEnemies.Count / _totalEnemies;
        }

        private void SpawnEntities(EntitySpawn entity, bool isLargeThreatObjective = false)
        {
            if (entity.Prefab == null) return;
            float range = (_size / 2f) * (entity.Spread / 100f);
            for (int i = 0; i < entity.Count; i++)
            {
                var pos = transform.position + new Vector3(
                    Random.Range(-range, range),
                    Random.Range(-range, range)
                );
                var instance = Instantiate(entity.Prefab, pos, Quaternion.identity);
                instance.transform.SetParent(transform, true);

                var enemy = instance.GetComponent<Enemy>();
                if (enemy != null)
                {
                    _spawnedEnemies.Add(enemy);
                    if (isLargeThreatObjective) _largeThreatEnemies.Add(enemy);
                }

                var asteroid = instance.GetComponent<Asteroid>();
                if (asteroid != null) _spawnedAsteroids.Add(asteroid);
            }
        }

        private void SpawnPortal()
        {
            if (_portalPrefab == null) return;
            var pos = transform.position + new Vector3(
                Random.Range(-_size / 2f, _size / 2f),
                Random.Range(-_size / 2f, _size / 2f)
            );
            _portal = Instantiate(_portalPrefab, pos, Quaternion.identity).GetComponent<Portal>();
            _portal?.Initialize(this);
        }

        // Picks one already-spawned enemy to carry the relic, marking it with a
        // RelicCarrier component rather than a dedicated prefab. Enemies only -- an
        // asteroid doesn't read as something with an objective attached, so a sector
        // with no live enemies simply can't seed the Relic objective.
        private void SeedRelicCarrier()
        {
            if (_relicPickupPrefab == null || _spawnedEnemies.Count == 0) return;

            _spawnedEnemies[Random.Range(0, _spawnedEnemies.Count)].gameObject
                .AddComponent<RelicCarrier>().Initialize(this);
        }

        public void SpawnRelicPickup(Vector3 position)
        {
            if (_relicPickupPrefab == null) return;
            Instantiate(_relicPickupPrefab, position, Quaternion.identity).GetComponent<Relic>()?.Initialize(this);
        }

        public void CompleteRelicObjective()
        {
            _relicCollected = true;
        }

        public void FailRelicObjective()
        {
            GameControl.control?.FailRun(this, "RELIC_DESTROYED");
        }

        private void SpawnBoundaryRing()
        {
            if (_boundaryMarkerPrefab == null || _boundaryMarkerCount <= 0) return;

            // Markers are parented under their own container, rotated as a whole in Update(),
            // rather than under the Sector's own transform -- that lets the entire ring slowly
            // revolve without disturbing the Sector transform that enemies/asteroids/portal are
            // also parented under.
            var ringContainer = new GameObject("BoundaryRing");
            ringContainer.transform.SetPositionAndRotation(transform.position, Quaternion.identity);
            ringContainer.transform.SetParent(transform, true);
            _boundaryRing = ringContainer.transform;

            float radius = _size / 2f;
            for (int i = 0; i < _boundaryMarkerCount; i++)
            {
                float angle = (i / (float)_boundaryMarkerCount) * Mathf.PI * 2f;
                float jitteredRadius = radius * (1f + Random.Range(0f, _boundaryRingJitter * 2f));
                var pos = transform.position + new Vector3(
                    Mathf.Cos(angle) * jitteredRadius,
                    Mathf.Sin(angle) * jitteredRadius
                );
                var instance = Instantiate(_boundaryMarkerPrefab, pos, Quaternion.identity);
                instance.transform.SetParent(_boundaryRing, true);
            }
        }
    }
}
