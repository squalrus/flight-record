using FlightRecord.Gameplay;
using UnityEngine;

namespace FlightRecord
{
    // A weapon power-up pickup (backlog 4.9, D-014). Flying into it gives the player a timed
    // weapon change (see Player.ApplyPowerUp). One prefab serves every type: Type is set
    // when it spawns (PowerUpDropTable.Spawn), and the glyph and color follow from it.
    public class PowerUp : Collectible
    {
        public PowerUpType Type;

        [Tooltip("Seconds before an uncollected power-up disappears.")]
        public float Lifetime = 15f;

        protected override void Start()
        {
            // Before base.Start(), which caches the glyph color as the pulse's base color.
            var glyph = GetComponentInChildren<TextMesh>();
            if (glyph != null)
            {
                glyph.text = Glyph(Type);
                glyph.color = ColorOf(Type);
            }

            base.Start();

            if (Lifetime > 0f) Destroy(gameObject, Lifetime);
        }

        // Shots can't destroy a power-up; it only goes away when collected or when it times out.
        public override void AddHealth(int health, Vector3? hitPoint = null)
        {
            if (health > 0) base.AddHealth(health, hitPoint);
        }

        protected override void OnCollected(GameObject player)
        {
            player.GetComponent<Player>()?.ApplyPowerUp(Type);
            Destroy(gameObject);
        }

        // Shared by the pickup and the HUD so both say and show the same thing.
        public static string DisplayName(PowerUpType type)
        {
            switch (type)
            {
                case PowerUpType.Spread: return "SPREAD";
                case PowerUpType.RapidFire: return "RAPID FIRE";
                case PowerUpType.Homing: return "HOMING";
                default: return type.ToString().ToUpperInvariant();
            }
        }

        public static Color ColorOf(PowerUpType type)
        {
            switch (type)
            {
                case PowerUpType.Spread: return new Color(1f, 0.6f, 0.2f);
                case PowerUpType.RapidFire: return new Color(1f, 0.92f, 0.3f);
                case PowerUpType.Homing: return new Color(1f, 0.4f, 0.85f);
                default: return Color.white;
            }
        }

        // Geometric shapes from the Infinity Icons HU font (see font-reference.html).
        private static string Glyph(PowerUpType type)
        {
            switch (type)
            {
                case PowerUpType.Spread: return "▲";
                case PowerUpType.RapidFire: return "◆";
                case PowerUpType.Homing: return "●";
                default: return "◆";
            }
        }
    }
}
