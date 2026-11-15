using System.Collections.Generic;
using UnityEngine;

namespace FlightRecord
{
    // Pooled projectiles for enemy bullet patterns (backlog 4.13), with one cap on how many
    // are in flight at once. Past the cap a new shot is skipped rather than recycling one
    // already flying, so bullets never vanish in front of the player; dense patterns thin
    // out instead of dropping the frame rate.
    //
    // Created on first use on the scene's ProjectileContainer (so pooled shots sit under
    // "Projectiles" and go with the scene when it unloads), or put one in a scene to tune
    // MaxActive there. Missiles taken from the pool return to it instead of being destroyed.
    public class EnemyProjectilePool : MonoBehaviour
    {
        [Tooltip("Most enemy pattern shots in flight at once. Shots past it are skipped.")]
        [Min(1)] public int MaxActive = 300;

        private static EnemyProjectilePool _instance;
        private readonly Dictionary<GameObject, Stack<Missile>> _free = new Dictionary<GameObject, Stack<Missile>>();

        // Pooled shots in flight right now.
        public int ActiveCount { get; private set; }

        // Shots skipped because the cap was reached, since the pool was created.
        public int SkippedCount { get; private set; }

        public static EnemyProjectilePool Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var host = ProjectileContainer.Instance != null
                    ? ProjectileContainer.Instance.gameObject
                    : new GameObject("Enemy Projectile Pool");
                return host.AddComponent<EnemyProjectilePool>();
            }
        }

        // A projectile from `prefab` at `position`, active and reset, or null at the cap.
        public static Missile Spawn(GameObject prefab, Vector2 position, Quaternion rotation) =>
            prefab == null ? null : Instance.Take(prefab, position, rotation);

        private void Awake()
        {
            if (_instance == null) _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private Missile Take(GameObject prefab, Vector2 position, Quaternion rotation)
        {
            if (ActiveCount >= MaxActive)
            {
                SkippedCount++;
                return null;
            }

            if (!_free.TryGetValue(prefab, out var stack))
            {
                stack = new Stack<Missile>();
                _free[prefab] = stack;
            }

            Missile missile = null;
            while (missile == null && stack.Count > 0) missile = stack.Pop(); // skips destroyed ones

            if (missile != null)
            {
                missile.transform.SetPositionAndRotation(position, rotation);
                missile.gameObject.SetActive(true);
            }
            else
            {
                var instance = Instantiate(prefab, position, rotation, transform);
                missile = instance.GetComponent<Missile>();
                if (missile == null)
                {
                    Debug.LogWarning($"{prefab.name} has no Missile component; it can't be a pattern projectile.", prefab);
                    Destroy(instance);
                    return null;
                }
            }

            missile.TakenFromPool(this, prefab);
            ActiveCount++;
            return missile;
        }

        internal void Return(Missile missile, GameObject prefab)
        {
            missile.gameObject.SetActive(false);
            if (!_free.TryGetValue(prefab, out var stack))
            {
                stack = new Stack<Missile>();
                _free[prefab] = stack;
            }
            stack.Push(missile);
            ActiveCount--;
        }

        // A pooled missile was destroyed while in flight (not returned).
        internal void Forget() => ActiveCount--;
    }
}
