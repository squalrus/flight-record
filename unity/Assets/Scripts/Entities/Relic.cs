using UnityEngine;

namespace FlightRecord
{
    // Dropped by Sector.SpawnRelicPickup() where the seeded RelicCarrier dies. Shares
    // bloom-delay, pulse glow, and magnet-reel behavior with Part via Collectible, but has
    // two distinct outcomes instead of one: the player touching it completes the sector's
    // Relic objective (OnCollected()), while it being shot down first fails the sector
    // (Destruct() override) instead of simply disappearing.
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class Relic : Collectible
    {
        [Header("Rainbow Shimmer")]
        public float RainbowCycleSpeed = 1f;

        private Sector _sector;

        public void Initialize(Sector sector)
        {
            _sector = sector;
        }

        // Distinguishes the Relic visually from Parts/gold pickups.
        protected override Color GetShimmerColor(float pulse)
        {
            return Color.HSVToRGB(Mathf.Repeat(Time.time * RainbowCycleSpeed, 1f), 1f, 1f);
        }

        protected override void OnCollected(GameObject player)
        {
            _sector?.CompleteRelicObjective();
            Destroy(gameObject);
        }

        public override void Destruct()
        {
            _sector?.FailRelicObjective();
            base.Destruct();
        }
    }
}
