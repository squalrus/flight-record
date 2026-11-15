using UnityEngine;

namespace FlightRecord
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

        // Destroy() only takes effect at the end of the frame, so Destruct() can run more than
        // once for one death (AddHealth and Ship.Update both call it on the same frame). A
        // subclass that awards points or spawns drops calls this first and stops if it's false.
        private bool _destructed;

        protected bool IsDestructed => _destructed;

        protected bool MarkDestructed()
        {
            if (_destructed) return false;
            _destructed = true;
            return true;
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
