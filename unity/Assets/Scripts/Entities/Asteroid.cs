using UnityEngine;

namespace FlightRecord
{
    public class Asteroid : DestroyableObject
    {
        private const float MOVEMENT = 10f;

        private static readonly Color DamageNumberColor = new Color(1f, 0.2f, 0.2f);

        [Header("Size & Mass")]
        public float MinScale = 0.5f;
        public float MaxScale = 2.5f;
        public float MinMass = 2f;
        public float MaxMass = 15f;

        [Header("Color")]
        public Color ColorA = new Color(0.8f, 0.76f, 0.72f);
        public Color ColorB = new Color(0.55f, 0.5f, 0.45f);

        [Header("Health")]
        public int MinHealth = 100;
        public int HealthVariance = 400;

        [Header("Parts Dropped")]
        [SerializeField] private GameObject _partPrefab;
        public int PartsDrop = 20;
        public int PartsVariance = 80;

        [Header("Damage Feedback")]
        [SerializeField] private GameObject _damageNumberPrefab;

        private Rigidbody2D _rigidbody;
        private Collider2D _collider;
        private TextMesh _glyph;

        private void Start()
        {
            _rigidbody = GetComponent<Rigidbody2D>();
            _collider = GetComponent<Collider2D>();
            _glyph = GetComponentInChildren<TextMesh>();

            var scale = Random.Range(MinScale, MaxScale);
            transform.localScale = new Vector3(scale, scale, 1f);
            _rigidbody.mass = Random.Range(MinMass, MaxMass);

            if (_glyph != null) _glyph.color = Color.Lerp(ColorA, ColorB, Random.value);

            MaxHealth = Random.Range(MinHealth, MinHealth + HealthVariance + 1);
            SetHealth(MaxHealth);
        }

        private void LateUpdate()
        {
            _rigidbody.AddForce(new Vector2(Random.Range(-MOVEMENT, MOVEMENT), Random.Range(-MOVEMENT, MOVEMENT)));
            _rigidbody.AddTorque(Random.Range(-MOVEMENT, MOVEMENT));
        }

        public override void AddHealth(int health, Vector3? hitPoint = null)
        {
            base.AddHealth(health, hitPoint);

            if (health < 0) SpawnDamageNumber(-health, hitPoint);
        }

        private void SpawnDamageNumber(int amount, Vector3? hitPoint)
        {
            if (_damageNumberPrefab == null) return;

            var center = _collider.bounds.center;
            var spawnPosition = hitPoint.HasValue
                ? hitPoint.Value + (Vector3)(((Vector2)hitPoint.Value - (Vector2)center).normalized * 0.2f)
                : center;

            var instance = Instantiate(_damageNumberPrefab, spawnPosition, Quaternion.identity);
            instance.GetComponent<DamageNumber>().Initialize(amount, DamageNumberColor);
        }

        public override void Destruct()
        {
            if (_partPrefab != null)
            {
                var count = Random.Range(Mathf.Max(0, PartsDrop - PartsVariance), PartsDrop + PartsVariance + 1);
                var center = _collider.bounds.center;
                for (var i = 0; i < count; i++)
                {
                    // Spawning every Part at the exact same point (up to 100 of them) packs
                    // their box colliders into one fully-overlapping pile that physics can
                    // struggle to separate, leaving some wedged against each other or a
                    // neighboring asteroid's collider. A small random offset gives each one
                    // its own starting position so the scatter force below has room to work.
                    var spawnOffset = Random.insideUnitCircle * 0.5f;
                    var powerup = Instantiate(_partPrefab, center + (Vector3)spawnOffset, Quaternion.identity);
                    powerup.GetComponent<Rigidbody2D>().AddForce(new Vector2(Random.Range(-25, 25), Random.Range(-25, 25)));
                    powerup.GetComponent<Rigidbody2D>().AddTorque(Random.Range(-5, 5));
                    if (GameControl.control != null) GameControl.control.PartsSpawned++;
                }
            }

            base.Destruct();
        }
    }
}
