using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace InfinitySpace
{
    // Player-only extensions on top of the shared Ship component: economy (Parts,
    // upgrades, conversions), Fuel, Portal/Sector scanning, Pickup Attraction/Homing
    // upgrades, haptic rumble, and the HUD low-resource alerts. Enemy prefabs keep a
    // plain Ship component -- only Player.prefab has this script attached.
    public class Player : Ship
    {
        // The visible ship "sprite" is a legacy TextMesh glyph (Infinity Icons HU font),
        // not a SpriteRenderer -- see font-reference.html. Player Profile Setup's ship
        // pick (R/N/J) is applied here by swapping this glyph's text. Enemy prefabs never
        // wire this field, so it lives on Player rather than the shared Ship base.
        [SerializeField] private TextMesh _glyph;

        // Lowest _currentHealth observed since this Player instance's Start() -- a fresh
        // Player is instantiated each sector (Main reloads on every portal transition), so
        // this is effectively "lowest health this sector" without needing a separate
        // per-sector reset. Backs the "Close Call" sector achievement (SectorAchievements.cs).
        private int _lowestHealthThisLife;
        public int LowestHealthThisLife => _lowestHealthThisLife;

        [Header("Fuel")]
        [Range(0, 1000)]
        public float MaxFuel;
        private float _currentFuel;

        private Coroutine _rumbleCoroutine;

        [SerializeField] private GameObject _homingRocketPrefab;

        // Parts persist across sector reloads, so they're stored on the DontDestroyOnLoad
        // GameControl singleton rather than on the Player instance, which is recreated each
        // sector. The setter also tracks lifetime earned/spent totals so Sector can report
        // each separately on the sector-complete screen instead of just the net change.
        private float Parts
        {
            get => GameControl.control.Parts;
            set
            {
                float delta = value - GameControl.control.Parts;
                if (delta > 0) GameControl.control.RunPartsEarned += delta;
                else if (delta < 0) GameControl.control.RunPartsSpent -= delta;
                GameControl.control.Parts = value;
            }
        }

        [Header("Parts Conversion")]
        [Range(1, 50)]
        public int PartsConversionCost = 5;
        [Range(0.01f, 1f)]
        public float PartsConversionPercent = 0.2f;

        [Header("Portal Locator")]
        [Range(1, 50)]
        public int ScanForPortalCost = 30;
        private Portal _portal;

        // Enemy/Asteroid/Parts Locator are plain instance fields (not GameControl/PlayerStatistics)
        // since Player is destroyed and recreated fresh every sector reload -- the same "this
        // sector only" lifecycle the Portal scan already gets from _portal.IsDiscovered above.
        [Header("Sector Locators")]
        [Range(1, 50)]
        public int LocatorScanCost = 30;
        private bool _enemyLocatorPurchased;
        private bool _asteroidLocatorPurchased;
        private bool _partsLocatorPurchased;

        // Threshold (as a fraction of max) below which AddHealth/FirePrimaryWeapon/TryConsumeFuel
        // flash a "LOW" warning via ResourceAlertController instead of "EMPTY" (reserved for 0).
        private const float LOW_RESOURCE_FRACTION = 0.10f;

        // Fire Rate/Health/FirePower upgrades have no level cap, unlike Magnet/Homing above --
        // cost grows geometrically per level instead of following a fixed per-level array.
        [Header("Stat Upgrades")]
        [Range(1, 100)]
        public int StatUpgradeBaseCost = 25;
        [Range(1f, 2f)]
        public float StatUpgradeCostGrowth = 1.2f;
        // Levels 1 and 2 (0->1, 1->2) are cheap, easily-attainable early purchases -- a
        // flat 5 then 15 -- so the jump into the established geometric progression
        // (StatUpgradeBaseCost * Growth^level), which resumes unchanged from level 2->3
        // onward, isn't as steep as going straight from 5 to ~30.
        [Range(1, 100)]
        public int StatUpgradeFirstLevelCost = 5;
        [Range(1, 100)]
        public int StatUpgradeSecondLevelCost = 15;
        public float FireRateCoolDownDelta = 0.02f;
        public float MinPrimaryCoolDown = 0.05f;
        public int HealthUpgradeDelta = 10;
        public int FirePowerUpgradeDelta = 1;
        public float FuelUpgradeDelta = 10f;
        public int AmmoUpgradeDelta = 10;

#if UNITY_EDITOR
        [Header("Debug")]
        [Tooltip("Parts granted by pressing F10 in the Editor. Editor-only -- compiled out of builds.")]
        public int DebugPartsGrant = 100;
#endif

        // Per-level config: index 0 is the cost/radius/force of level 1, index 1 of level 2, etc.
        // Add more entries to extend past 3 levels; arrays must stay the same length.
        // Global player-progression table (not per-prefab tuning, unlike e.g. Enemy.cs's
        // OrbitSpeed) -- kept private/non-serialized so code is the only source of truth
        // and a stray Unity-Editor prefab save can never bake in a stale override.
        private readonly int[] PickupAttractionUpgradeCosts = { 5, 10, 15, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20 };
        private readonly float[] PickupAttractionRadii = { 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 15f, 16f, 17f, 18f, 19f, 20f, 21f, 22f, 23f, 24f, 25f, 26f, 27f, 28f, 29f, 30f, 31f, 32f };
        [SerializeField] private Color _attractionGizmoColor = Color.cyan;

        // Persists across sector reloads via the GameControl singleton, like Parts,
        // since this upgrade is expensive and meant to carry across the whole run.
        private int PickupAttractionLevel
        {
            get => GameControl.control.PickupAttractionLevel;
            set => GameControl.control.PickupAttractionLevel = value;
        }

        // Per-level config: index 0 is the cost/proc chance of level 1, index 1 of level 2, etc.
        // Global player-progression table (not per-prefab tuning) -- kept private/non-serialized,
        // same rationale as PickupAttraction above. Homing no longer bends the primary Missile
        // itself -- each level instead raises the odds that firing a Missile also launches a
        // separate, always-homing Homing Rocket alongside it (no extra ammo cost). The rocket's
        // turn rate is a flat constant (the old level-12 value) rather than scaling with level --
        // only the proc chance improves as the upgrade is leveled.
        private readonly int[] HomingUpgradeCosts = { 5, 10, 15, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20 };
        private readonly int[] HomingProcChances = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30 };
        private const float HomingRocketTurnRate = 400f;
        [Header("Homing")]
        [Tooltip("Minimum seconds a Homing Rocket flies straight before it's even eligible to start homing, regardless of target proximity.")]
        public float HomingDelay = .75f;
        [Tooltip("Distance to a target the rocket must be within (after HomingDelay has elapsed) before it actually starts homing.")]
        public float HomingRange = 8f;
        [Tooltip("Multiplier on the FirePower/FirePowerVariance roll applied to the Homing Rocket's damage, on top of the regular Missile it accompanies.")]
        public float HomingRocketDamageMultiplier = 2f;

        private int HomingLevel
        {
            get => GameControl.control.HomingLevel;
            set => GameControl.control.HomingLevel = value;
        }

        // Persists across sector reloads via the GameControl singleton, same rationale as
        // PickupAttraction/Homing above.
        private int FireRateUpgradeLevel
        {
            get => GameControl.control.FireRateUpgradeLevel;
            set => GameControl.control.FireRateUpgradeLevel = value;
        }

        private int HealthUpgradeLevel
        {
            get => GameControl.control.HealthUpgradeLevel;
            set => GameControl.control.HealthUpgradeLevel = value;
        }

        private int FirePowerUpgradeLevel
        {
            get => GameControl.control.FirePowerUpgradeLevel;
            set => GameControl.control.FirePowerUpgradeLevel = value;
        }

        private int FuelUpgradeLevel
        {
            get => GameControl.control.FuelUpgradeLevel;
            set => GameControl.control.FuelUpgradeLevel = value;
        }

        private int AmmoUpgradeLevel
        {
            get => GameControl.control.AmmoUpgradeLevel;
            set => GameControl.control.AmmoUpgradeLevel = value;
        }

        protected override void ApplyStatUpgrades()
        {
            if (GameControl.control == null) return;

            if (_glyph != null && !string.IsNullOrEmpty(GameControl.control.ShipId))
                _glyph.text = GameControl.control.ShipId;

            PrimaryCoolDown = Mathf.Max(MinPrimaryCoolDown, PrimaryCoolDown - FireRateUpgradeLevel * FireRateCoolDownDelta);
            FirePower += FirePowerUpgradeLevel * FirePowerUpgradeDelta;
            MaxHealth += HealthUpgradeLevel * HealthUpgradeDelta;
            MaxFuel += FuelUpgradeLevel * FuelUpgradeDelta;
            MaxPrimaryAmmunition += AmmoUpgradeLevel * AmmoUpgradeDelta;
        }

        // Carries over the resources the player had at the last successful sector
        // transition instead of always handing them a fresh full tank.
        protected override int ApplyCarryOverStats(int startingHealth)
        {
            _currentFuel = MaxFuel;

            if (GameControl.control == null) return startingHealth;

            if (GameControl.control.CurrentFuel >= 0) _currentFuel = Mathf.Clamp(GameControl.control.CurrentFuel, 0, MaxFuel);
            if (GameControl.control.CurrentAmmo >= 0) _currentAmmo = Mathf.Clamp(GameControl.control.CurrentAmmo, 0, MaxPrimaryAmmunition);
            if (GameControl.control.CurrentHealth >= 0) startingHealth = GameControl.control.CurrentHealth;

            return startingHealth;
        }

        protected override void OnHealthInitialized()
        {
            _lowestHealthThisLife = _currentHealth;
        }

#if UNITY_EDITOR
        protected override void HandleDebugInput()
        {
            if (Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
                AddParts(DebugPartsGrant);
        }
#endif

        private void FixedUpdate()
        {
            // Stuck-pickup safety net, independent of the Magnet upgrade below -- see
            // Collectible.ForceCollect() for why a fully-overlapped pickup can otherwise
            // never trigger its own collection.
            foreach (var hit in Physics2D.OverlapAreaAll(_collider.bounds.min, _collider.bounds.max))
            {
                var collectible = hit.GetComponent<Collectible>();
                if (collectible == null) continue;
                if (_collider.bounds.Contains(hit.bounds.min) && _collider.bounds.Contains(hit.bounds.max))
                    collectible.ForceCollect(gameObject);
            }

            if (PickupAttractionLevel <= 0) return;

            var radius = GetPickupAttractionRadius();
            foreach (var hit in Physics2D.OverlapCircleAll(transform.position, radius))
            {
                var attractable = hit.GetComponent<IMagnetAttractable>();
                if (attractable == null || !attractable.IsCollectable) continue;

                // Switch to a trigger while magnetized so the pickup doesn't physically
                // collide with (and push around) the ship as it's reeled in.
                hit.isTrigger = true;

                attractable.Reel(transform.position);
            }
        }

        protected override void OnWeaponFired(GameObject missileInstance)
        {
            missileInstance.GetComponent<Missile>().IsPlayerFired = true;
            GameControl.control.ShotsFired += 1;
            Rumble(0.1f, 0.05f);

            // Homing is a player-only upgrade, like Pickup Attraction -- the rocket costs no
            // ammo and isn't counted as its own shot, since it's a bonus on top of the Missile
            // that was just fired.
            if (HomingLevel > 0 && _homingRocketPrefab != null && Random.Range(0, 100) < HomingProcChances[HomingLevel - 1])
            {
                GameObject rocket = Instantiate(_homingRocketPrefab, Cannon.position, Cannon.rotation) as GameObject;
                if (ProjectileContainer.Instance != null)
                    rocket.transform.SetParent(ProjectileContainer.Instance);
                rocket.GetComponent<Missile>().SetHealth(Mathf.RoundToInt(RollFirePower() * HomingRocketDamageMultiplier));
                rocket.GetComponent<Rigidbody2D>().linearVelocity = (Cannon.up * 12f) + (_rigidbody.transform.up * _rigidbody.linearVelocity.magnitude);
                rocket.GetComponent<Missile>().SetHoming(HomingRocketTurnRate, HomingDelay, HomingRange);
                rocket.GetComponent<Missile>().IsPlayerFired = true;
            }
        }

        protected override void OnAmmoLow()
        {
            if (GetAmmunition() / MaxPrimaryAmmunition <= LOW_RESOURCE_FRACTION)
                ResourceAlertController.Instance?.Flash("AMMO LOW");
        }

        protected override void OnAmmoEmpty()
        {
            ResourceAlertController.Instance?.Flash("AMMO EMPTY", critical: true);
        }

        public override void Destruct()
        {
            base.Destruct();

            // GameControl.FailRun captures the snapshot (read across the scene
            // transition by GameOverController via GameControl.LastDeath, since
            // GameControl is the only thing that survives it), clears the run, and
            // loads GameOver.
            GameControl.control?.FailRun(FindFirstObjectByType<Sector>(), "PLAYER_DESTROYED");
        }

        // Called by ShipUpgradesController right before launching the next sector, after
        // any parts-conversion purchases on this same instance, so the carried-over values
        // reflect the post-purchase state.
        public void PersistCarryOverStats()
        {
            if (GameControl.control == null) return;

            GameControl.control.CurrentFuel = _currentFuel;
            GameControl.control.CurrentAmmo = _currentAmmo;
            GameControl.control.CurrentHealth = _currentHealth;
        }

        public float GetFuel()
        {
            return _currentFuel;
        }

        public void AddFuel(float fuel)
        {
            _currentFuel += fuel;
            _currentFuel = Mathf.Clamp(_currentFuel, 0, MaxFuel);

            if (fuel < 0 && GameControl.control != null)
                GameControl.control.FuelConsumed += -fuel;
        }

        // Used by PlayerController instead of calling AddFuel directly so thrust attempts
        // while empty flash a warning instead of just silently doing nothing.
        public bool TryConsumeFuel(float amount)
        {
            if (_currentFuel <= 0)
            {
                ResourceAlertController.Instance?.Flash("FUEL EMPTY", critical: true);
                return false;
            }

            AddFuel(-amount);

            if (_currentFuel / MaxFuel <= LOW_RESOURCE_FRACTION)
                ResourceAlertController.Instance?.Flash("FUEL LOW");

            return true;
        }

        public override void AddHealth(int health, Vector3? hitPoint = null)
        {
            base.AddHealth(health, hitPoint);

            if (health < 0)
            {
                if (GameControl.control != null)
                {
                    GameControl.control.DamageTaken += -health;
                    Rumble(Mathf.Lerp(0.25f, 1f, Mathf.Clamp01((float)-health / MaxHealth)), 0.2f);
                }

                _lowestHealthThisLife = Mathf.Min(_lowestHealthThisLife, _currentHealth);

                if (_currentHealth <= 0)
                    ResourceAlertController.Instance?.Flash("HEALTH EMPTY", critical: true);
                else if ((float)_currentHealth / MaxHealth <= LOW_RESOURCE_FRACTION)
                    ResourceAlertController.Instance?.Flash("HEALTH LOW");
            }
        }

        // Haptic feedback -- prefers the gamepad's motors; falls back to the Android
        // vibration API when no gamepad is connected (the common case for touch-screen play).
        private void Rumble(float intensity, float duration)
        {
            if (GameControl.control == null || !GameControl.control.RumbleEnabled) return;

            if (Gamepad.current != null)
            {
                if (_rumbleCoroutine != null) StopCoroutine(_rumbleCoroutine);
                _rumbleCoroutine = StartCoroutine(RumbleGamepad(intensity, duration));
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            else
            {
                Handheld.Vibrate();
            }
#endif
        }

        private IEnumerator RumbleGamepad(float intensity, float duration)
        {
            Gamepad.current.SetMotorSpeeds(intensity, intensity);
            yield return new WaitForSeconds(duration);
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
        }

        public float GetParts()
        {
            return Parts;
        }

        public void AddParts(int parts)
        {
            Parts += parts;
        }

        public bool CanConvertPartsToHealth()
        {
            return Parts >= PartsConversionCost && _currentHealth < MaxHealth;
        }

        public void ConvertPartsToHealth()
        {
            if (!CanConvertPartsToHealth()) return;

            Parts -= PartsConversionCost;
            AddHealth(Mathf.RoundToInt(MaxHealth * PartsConversionPercent));
        }

        public bool CanConvertPartsToFuel()
        {
            return Parts >= PartsConversionCost && _currentFuel < MaxFuel;
        }

        public void ConvertPartsToFuel()
        {
            if (!CanConvertPartsToFuel()) return;

            Parts -= PartsConversionCost;
            AddFuel(MaxFuel * PartsConversionPercent);
        }

        public bool CanConvertPartsToAmmo()
        {
            return Parts >= PartsConversionCost && _currentAmmo < MaxPrimaryAmmunition;
        }

        public void ConvertPartsToAmmo()
        {
            if (!CanConvertPartsToAmmo()) return;

            Parts -= PartsConversionCost;
            AddAmmo(Mathf.RoundToInt(MaxPrimaryAmmunition * PartsConversionPercent));
        }

        // Each individual Convert* purchase only restores PartsConversionPercent (20%) of max
        // for PartsConversionCost, so filling a resource the rest of the way to 100% costs as
        // many of those "levels" as it would take buying them one at a time.
        private int GetTopOffLevels(float current, float max)
        {
            if (current >= max) return 0;
            return Mathf.CeilToInt((max - current) / max / PartsConversionPercent);
        }

        public int GetTopOffCost()
        {
            int levels = GetTopOffLevels(_currentHealth, MaxHealth)
                + GetTopOffLevels(_currentFuel, MaxFuel)
                + GetTopOffLevels(_currentAmmo, MaxPrimaryAmmunition);
            return levels * PartsConversionCost;
        }

        public bool CanTopOff()
        {
            int cost = GetTopOffCost();
            return cost > 0 && Parts >= cost;
        }

        // Fills every below-max resource the rest of the way to 100% in one click, for the
        // same total cost as buying that many individual Convert* levels would have taken.
        public void TopOff()
        {
            if (!CanTopOff()) return;

            if (_currentHealth < MaxHealth)
            {
                Parts -= GetTopOffLevels(_currentHealth, MaxHealth) * PartsConversionCost;
                AddHealth(MaxHealth - _currentHealth);
            }

            if (_currentFuel < MaxFuel)
            {
                Parts -= GetTopOffLevels(_currentFuel, MaxFuel) * PartsConversionCost;
                AddFuel(MaxFuel - _currentFuel);
            }

            if (_currentAmmo < MaxPrimaryAmmunition)
            {
                Parts -= GetTopOffLevels(_currentAmmo, MaxPrimaryAmmunition) * PartsConversionCost;
                AddAmmo(Mathf.CeilToInt(MaxPrimaryAmmunition - _currentAmmo));
            }
        }

        public int GetPickupAttractionLevel()
        {
            return PickupAttractionLevel;
        }

        public int GetPickupAttractionMaxLevel()
        {
            return PickupAttractionUpgradeCosts.Length;
        }

        public float GetPickupAttractionRadius()
        {
            return PickupAttractionLevel > 0 ? PickupAttractionRadii[PickupAttractionLevel - 1] : 0f;
        }

        public int GetPickupAttractionUpgradeCost()
        {
            return PickupAttractionLevel < GetPickupAttractionMaxLevel() ? PickupAttractionUpgradeCosts[PickupAttractionLevel] : -1;
        }

        public bool CanUpgradePickupAttraction()
        {
            return PickupAttractionLevel < GetPickupAttractionMaxLevel() && Parts >= GetPickupAttractionUpgradeCost();
        }

        public void UpgradePickupAttraction()
        {
            if (!CanUpgradePickupAttraction()) return;

            Parts -= GetPickupAttractionUpgradeCost();
            PickupAttractionLevel++;
        }

        public int GetHomingLevel()
        {
            return HomingLevel;
        }

        public int GetHomingMaxLevel()
        {
            return HomingUpgradeCosts.Length;
        }

        public int GetHomingUpgradeCost()
        {
            return HomingLevel < GetHomingMaxLevel() ? HomingUpgradeCosts[HomingLevel] : -1;
        }

        public bool CanUpgradeHoming()
        {
            return HomingLevel < GetHomingMaxLevel() && Parts >= GetHomingUpgradeCost();
        }

        public void UpgradeHoming()
        {
            if (!CanUpgradeHoming()) return;

            Parts -= GetHomingUpgradeCost();
            HomingLevel++;
        }

        // Shared by Fire Rate/Health/FirePower upgrades, which have no level cap -- cost
        // compounds by StatUpgradeCostGrowth per level instead of using a fixed array.
        private int GetStatUpgradeCost(int level)
        {
            if (level == 0) return StatUpgradeFirstLevelCost;
            if (level == 1) return StatUpgradeSecondLevelCost;
            return Mathf.RoundToInt(StatUpgradeBaseCost * Mathf.Pow(StatUpgradeCostGrowth, level));
        }

        public int GetFireRateUpgradeLevel() => FireRateUpgradeLevel;

        public int GetFireRateUpgradeCost() => GetStatUpgradeCost(FireRateUpgradeLevel);

        public bool CanUpgradeFireRate() => Parts >= GetFireRateUpgradeCost();

        public void UpgradeFireRate()
        {
            if (!CanUpgradeFireRate()) return;

            Parts -= GetFireRateUpgradeCost();
            FireRateUpgradeLevel++;
            PrimaryCoolDown = Mathf.Max(MinPrimaryCoolDown, PrimaryCoolDown - FireRateCoolDownDelta);
        }

        public int GetHealthUpgradeLevel() => HealthUpgradeLevel;

        public int GetHealthUpgradeCost() => GetStatUpgradeCost(HealthUpgradeLevel);

        public bool CanUpgradeHealth() => Parts >= GetHealthUpgradeCost();

        public void UpgradeHealth()
        {
            if (!CanUpgradeHealth()) return;

            Parts -= GetHealthUpgradeCost();
            HealthUpgradeLevel++;
            MaxHealth += HealthUpgradeDelta;
            AddHealth(HealthUpgradeDelta);
        }

        public int GetFirePowerUpgradeLevel() => FirePowerUpgradeLevel;

        public int GetFirePowerUpgradeCost() => GetStatUpgradeCost(FirePowerUpgradeLevel);

        public bool CanUpgradeFirePower() => Parts >= GetFirePowerUpgradeCost();

        public void UpgradeFirePower()
        {
            if (!CanUpgradeFirePower()) return;

            Parts -= GetFirePowerUpgradeCost();
            FirePowerUpgradeLevel++;
            FirePower += FirePowerUpgradeDelta;
        }

        public int GetFuelUpgradeLevel() => FuelUpgradeLevel;

        public int GetFuelUpgradeCost() => GetStatUpgradeCost(FuelUpgradeLevel);

        public bool CanUpgradeFuel() => Parts >= GetFuelUpgradeCost();

        public void UpgradeFuel()
        {
            if (!CanUpgradeFuel()) return;

            Parts -= GetFuelUpgradeCost();
            FuelUpgradeLevel++;
            MaxFuel += FuelUpgradeDelta;
            AddFuel(FuelUpgradeDelta);
        }

        public int GetAmmoUpgradeLevel() => AmmoUpgradeLevel;

        public int GetAmmoUpgradeCost() => GetStatUpgradeCost(AmmoUpgradeLevel);

        public bool CanUpgradeAmmo() => Parts >= GetAmmoUpgradeCost();

        public void UpgradeAmmo()
        {
            if (!CanUpgradeAmmo()) return;

            Parts -= GetAmmoUpgradeCost();
            AmmoUpgradeLevel++;
            MaxPrimaryAmmunition += AmmoUpgradeDelta;
            AddAmmo(AmmoUpgradeDelta);
        }

        public bool IsPortalDiscovered()
        {
            if (_portal == null) _portal = FindFirstObjectByType<Portal>();
            return _portal != null && _portal.IsDiscovered;
        }

        public bool CanScanForPortal()
        {
            return Parts >= ScanForPortalCost && !IsPortalDiscovered();
        }

        public void ScanForPortal()
        {
            if (!CanScanForPortal()) return;

            Parts -= ScanForPortalCost;
            _portal.ForceDiscover();
        }

        public bool IsEnemyLocatorPurchased() => _enemyLocatorPurchased;

        public bool CanPurchaseEnemyLocator() => !_enemyLocatorPurchased && Parts >= LocatorScanCost;

        public void PurchaseEnemyLocator()
        {
            if (!CanPurchaseEnemyLocator()) return;

            Parts -= LocatorScanCost;
            _enemyLocatorPurchased = true;
        }

        public bool IsAsteroidLocatorPurchased() => _asteroidLocatorPurchased;

        public bool CanPurchaseAsteroidLocator() => !_asteroidLocatorPurchased && Parts >= LocatorScanCost;

        public void PurchaseAsteroidLocator()
        {
            if (!CanPurchaseAsteroidLocator()) return;

            Parts -= LocatorScanCost;
            _asteroidLocatorPurchased = true;
        }

        public bool IsPartsLocatorPurchased() => _partsLocatorPurchased;

        public bool CanPurchasePartsLocator() => !_partsLocatorPurchased && Parts >= LocatorScanCost;

        public void PurchasePartsLocator()
        {
            if (!CanPurchasePartsLocator()) return;

            Parts -= LocatorScanCost;
            _partsLocatorPurchased = true;
        }

#if UNITY_EDITOR
        // Shares Enemy's F9 toggle so all range/radius gizmos turn on and off together.
        private void OnDrawGizmos()
        {
            if (!Enemy.ShowDebugRanges || PickupAttractionLevel <= 0) return;

            Gizmos.color = _attractionGizmoColor;
            Gizmos.DrawWireSphere(transform.position, GetPickupAttractionRadius());
        }
#endif
    }
}
