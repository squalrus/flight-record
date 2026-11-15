using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace FlightRecord
{
    public class Ship : DestroyableObject, ICanTakeDamage
    {
        protected Collider2D _collider;
        protected Rigidbody2D _rigidbody;

        [SerializeField] private GameObject _missilePrefab;
        protected GameObject MissilePrefab => _missilePrefab;
        [SerializeField] private GameObject _partPrefab;
        [SerializeField] private GameObject _damageNumberPrefab;

        private static readonly Color DamageNumberColor = new Color(1f, 0.2f, 0.2f);

        [Header("Health Bar")]
        [SerializeField] private Image _healthBarFill;
        private float _healthBarYOffset;

        [Header("Ammunition")]
        public Transform Cannon;
        [Range(0, 20)]
        public int FirePower;
        [FormerlySerializedAs("FirePowerTolerance")]
        [Range(0, 10)]
        public int FirePowerVariance;
        [Range(20, 100)]
        public int MaxPrimaryAmmunition;
        protected float _currentAmmo;
        [Range(0, 10)]
        public float PrimaryCoolDown;
        private bool _fireReady = true;

        [Header("Speed")]
        public float Speed;
        public float RotationSpeed;

        [Header("Parts Dropped")]
        public float PartsDrop;
        public int PartsVariance;

        private void Start()
        {
            _collider = GetComponent<Collider2D>();
            _rigidbody = GetComponent<Rigidbody2D>();

            ApplyStatUpgrades();

            _currentAmmo = MaxPrimaryAmmunition;
            int startingHealth = MaxHealth;

            startingHealth = ApplyCarryOverStats(startingHealth);

            RotationSpeed = (RotationSpeed == 0) ? Speed * 30f : RotationSpeed;

            SetHealth(startingHealth);
            OnHealthInitialized();

            if (_healthBarFill != null)
                _healthBarYOffset = _healthBarFill.canvas.transform.position.y - transform.position.y;
        }

        // Hooks below are no-ops here so shared Start()/Update()/FireWeapon()/FirePrimaryWeapon()
        // bodies stay identical for enemy Ships. Player overrides them for its own economy/HUD/
        // haptics logic. These can't be a second same-named private Start()/Update() declared on
        // Player -- see Enemy.OnSpawn()'s comment: Unity invokes every same-named private magic
        // method declared anywhere in the hierarchy independently, not via override dispatch.
        protected virtual void ApplyStatUpgrades() { }
        protected virtual int ApplyCarryOverStats(int startingHealth) => startingHealth;
        protected virtual void OnHealthInitialized() { }

        private void Update()
        {
            if (_currentHealth <= 0)
            {
                Destruct();
            }

            if (_healthBarFill != null)
            {
                var pct = (float)_currentHealth / MaxHealth;
                _healthBarFill.fillAmount = pct;
                _healthBarFill.color = pct < .2f ? Color.red : pct < .5f ? Color.yellow : Color.green;
            }

#if UNITY_EDITOR
            HandleDebugInput();
#endif
        }

#if UNITY_EDITOR
        protected virtual void HandleDebugInput() { }
#endif

        private void LateUpdate()
        {
            if (_healthBarFill != null)
            {
                var canvas = _healthBarFill.canvas.transform;
                canvas.rotation = Quaternion.identity;
                canvas.position = transform.position + Vector3.up * _healthBarYOffset;
            }
        }

        // Same shape as the PartsDrop/PartsVariance roll in Destruct() -- a symmetric
        // range around the base value, clamped so variance can't roll damage below 0.
        public int RollFirePower()
        {
            return Random.Range(Mathf.Max(0, FirePower - FirePowerVariance), FirePower + FirePowerVariance + 1);
        }

        private IEnumerator FireWeapon()
        {
            GameObject instance = SpawnShot(_missilePrefab, Cannon.rotation, RollFirePower());

            if (UsesAmmo) _currentAmmo--;

            OnWeaponFired(instance);

            _fireReady = false;
            yield return new WaitForSeconds(CurrentCoolDown);
            _fireReady = true;
        }

        // Spawns one projectile from the Cannon, flying along `rotation`. Shared by the
        // primary shot and Player's extra shots (spread, homing rocket).
        protected GameObject SpawnShot(GameObject prefab, Quaternion rotation, int damage)
        {
            GameObject instance = Instantiate(prefab, Cannon.position, rotation);
            if (ProjectileContainer.Instance != null)
                instance.transform.SetParent(ProjectileContainer.Instance);
            var missile = instance.GetComponent<Missile>();
            missile.SetHealth(damage);
            // No enemy friendly fire (4.12).
            if (GetComponent<Enemy>() != null) missile.IgnoreEnemies();
            var direction = (Vector2)(rotation * Vector3.up);
            instance.GetComponent<Rigidbody2D>().linearVelocity = direction * 12f + InheritedShotVelocity(direction);
            return instance;
        }

        // Velocity added to each shot on top of its muzzle speed. Enemy Ships still push
        // shots along their facing at their current speed; Player overrides this so shots
        // fly along their own direction at a constant speed (D-016).
        protected virtual Vector2 InheritedShotVelocity(Vector2 shotDirection)
        {
            return (Vector2)_rigidbody.transform.up * _rigidbody.linearVelocity.magnitude;
        }

        // Whether firing spends ammunition. Enemy Ships keep the original limit; Player
        // spends ammo only when the Limited Ammo setting is on (D-014).
        protected virtual bool UsesAmmo => true;

        // Seconds between primary shots. Player shortens it while Rapid Fire is active.
        protected virtual float CurrentCoolDown => PrimaryCoolDown;

        // Player-only follow-up on a shared shot: HomingRocket proc, IsPlayerFired flag,
        // ShotsFired stat, fire rumble. No-op for enemy Ships.
        protected virtual void OnWeaponFired(GameObject missileInstance) { }

        public void FirePrimaryWeapon()
        {
            bool hasAmmo = !UsesAmmo || _currentAmmo > 0;

            if (_fireReady && hasAmmo)
            {
                StartCoroutine(FireWeapon());
                if (UsesAmmo) OnAmmoLow();
            }
            else if (!hasAmmo)
            {
                OnAmmoEmpty();
            }
        }

        protected virtual void OnAmmoLow() { }
        protected virtual void OnAmmoEmpty() { }

        public override void Destruct()
        {
            if (!MarkDestructed()) return;

            // Only enemies are worth points (4.14); the player's own Ship never is.
            var enemy = GetComponent<Enemy>();
            if (enemy != null) GameControl.control?.AddPoints(enemy.Points);

            var num = UnityEngine.Random.Range(Mathf.Max(0f, PartsDrop - PartsVariance), PartsDrop + PartsVariance);

            for (var i = 0; i < num; i++)
            {
                var powerup = Instantiate(_partPrefab, _collider.bounds.center, Quaternion.Euler(new Vector3(0, 0, 0))) as GameObject;
                powerup.transform.position = _collider.bounds.center;
                powerup.GetComponent<Rigidbody2D>().AddForce(new Vector2(UnityEngine.Random.Range(-25, 25), UnityEngine.Random.Range(-25, 25)));
                powerup.GetComponent<Rigidbody2D>().AddTorque(UnityEngine.Random.Range(5, 5));
            }

            // Only enemies drop power-ups; the player's own Ship (also a Ship) never does.
            if (enemy != null)
                PowerUpDropTable.TryDrop(_collider.bounds.center);

            var explosion = new GameObject("ShipExplosion");
            explosion.transform.position = _collider.bounds.center;
            explosion.AddComponent<ParticleSystem>();
            explosion.AddComponent<ExplosionEffect>();

            base.Destruct();
        }

        public float GetAmmunition()
        {
            return _currentAmmo;
        }

        public void AddAmmo(int ammo)
        {
            _currentAmmo += ammo;
            _currentAmmo = Mathf.Clamp(_currentAmmo, 0, MaxPrimaryAmmunition);
        }

        public override void AddHealth(int health, Vector3? hitPoint = null)
        {
            base.AddHealth(health, hitPoint);

            if (health < 0)
            {
                SpawnDamageNumber(-health, hitPoint);
            }
        }

        private void SpawnDamageNumber(int amount, Vector3? hitPoint)
        {
            if (_damageNumberPrefab == null) return;

            var center = _collider.bounds.center;
            Vector3 spawnPosition;

            if (hitPoint.HasValue)
            {
                var outward = ((Vector2)hitPoint.Value - (Vector2)center).normalized;
                spawnPosition = hitPoint.Value + (Vector3)(outward * 0.2f);
            }
            else
            {
                spawnPosition = center + new Vector3(Random.Range(-.3f, .3f), 0, 0);
            }

            var instance = Instantiate(_damageNumberPrefab, spawnPosition, Quaternion.identity);
            instance.GetComponent<DamageNumber>().Initialize(amount, DamageNumberColor);
        }
    }
}
