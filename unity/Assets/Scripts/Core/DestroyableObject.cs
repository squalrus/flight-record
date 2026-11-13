using UnityEngine;

namespace InfinitySpace
{
    public abstract class DestroyableObject : MonoBehaviour, ICanTakeDamage
    {
        protected int _currentHealth;

        [Header("Health")]
        [Range(1, 1000)]
        public int MaxHealth;

        /// <summary>
        /// Returns the current health
        /// </summary>
        /// <returns>Current health</returns>
        public int GetCurrentHealth()
        {
            return _currentHealth;
        }

        /// <summary>
        /// Explicitly set the current health
        /// </summary>
        /// <param name="health">Health value</param>
        public void SetHealth(int health)
        {
            _currentHealth = Mathf.Clamp(health, 0, MaxHealth);
        }

        /// <summary>
        /// Add the health value
        /// </summary>
        /// <param name="health">Health value, can be negative</param>
        public virtual void AddHealth(int health, Vector3? hitPoint = null)
        {
            _currentHealth += health;

            if (_currentHealth <= 0)
            {
                Destruct();
            }

            _currentHealth = Mathf.Min(_currentHealth, MaxHealth);
        }

        /// <summary>
        /// Destroy the gameObject
        /// </summary>
        public virtual void Destruct()
        {
            Destroy(gameObject);
        }
    }
}
