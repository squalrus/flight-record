using FlightRecord.Gameplay;
using UnityEngine;

namespace FlightRecord
{
    // Where power-ups come from until per-enemy and per-wave drop tables exist (backlog
    // 4.16): every destroyed enemy rolls DropChance once, and a hit drops one power-up of a
    // random type from Types. Loaded from Resources/PowerUpDrops.asset so no enemy prefab
    // or scene needs a new reference.
    [CreateAssetMenu(menuName = "Flight Record/Power-Up Drop Table", fileName = "PowerUpDrops")]
    public class PowerUpDropTable : ScriptableObject
    {
        private const string ResourcePath = "PowerUpDrops";

        public GameObject PowerUpPrefab;

        [Range(0f, 1f)]
        public float DropChance = 0.15f;

        public PowerUpType[] Types = { PowerUpType.Spread, PowerUpType.RapidFire, PowerUpType.Homing };

        private static PowerUpDropTable _instance;
        private static bool _loadAttempted;

        private static PowerUpDropTable Instance
        {
            get
            {
                if (_instance == null && !_loadAttempted)
                {
                    _loadAttempted = true;
                    _instance = Resources.Load<PowerUpDropTable>(ResourcePath);
                    if (_instance == null)
                        Debug.LogWarning($"PowerUpDropTable: Resources/{ResourcePath}.asset not found; no power-ups will drop.");
                }
                return _instance;
            }
        }

        public static void TryDrop(Vector3 position)
        {
            var table = Instance;
            if (table == null || Random.value >= table.DropChance) return;

            Spawn(RandomType(), position);
        }

        public static PowerUpType RandomType()
        {
            var table = Instance;
            var types = table != null && table.Types != null && table.Types.Length > 0 ? table.Types : PowerUpTimers.AllTypes;
            return types[Random.Range(0, types.Length)];
        }

        public static PowerUp Spawn(PowerUpType type, Vector3 position)
        {
            var table = Instance;
            if (table == null || table.PowerUpPrefab == null) return null;

            var powerUp = Instantiate(table.PowerUpPrefab, position, Quaternion.identity).GetComponent<PowerUp>();
            if (powerUp != null) powerUp.Type = type;
            return powerUp;
        }
    }
}
