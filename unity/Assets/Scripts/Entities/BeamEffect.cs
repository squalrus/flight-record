using UnityEngine;

namespace FlightRecord
{
    // A stationary beam that lives as its own scene GameObject for a fixed duration.
    // Visual: LineRenderer in local space (no world-space drift as parent moves).
    // Damage: BoxCollider2D trigger -- OnTriggerEnter2D fires once per collider entry,
    // so both a direct hit (beam spawns on top of something) and a ship flying into the
    // active beam deal one damage event each. Enemies are filtered out -- only the player
    // and destructible non-enemy objects (asteroids, Relic) are affected.
    public class BeamEffect : MonoBehaviour
    {
        private int _damage;
        private float _duration;
        private float _elapsed;
        private BoxCollider2D _boxCollider;
        private LineRenderer _lineRenderer;
        private Color _beamColor;

        // Called synchronously by Satellite immediately after AddComponent, before the
        // first Update -- sets up the collider and renderer with the beam's parameters.
        public void Initialize(int damage, float length, float width, float duration, Color color)
        {
            _damage = damage;
            _duration = duration;
            _beamColor = color;

            // Trigger collider: a tall thin box extending forward from this object's origin.
            // offset pushes the center halfway along so the near edge sits at the satellite.
            _boxCollider = gameObject.AddComponent<BoxCollider2D>();
            _boxCollider.isTrigger = true;
            _boxCollider.size = new Vector2(width, length);
            _boxCollider.offset = new Vector2(0f, length / 2f);

            // LineRenderer in local space so its positions stay fixed even if the GO is
            // ever reparented. Point 0 at origin, point 1 along local +Y (= world direction
            // the satellite was facing when it fired, snapshotted via transform.rotation).
            _lineRenderer = gameObject.AddComponent<LineRenderer>();
            _lineRenderer.useWorldSpace = false;
            _lineRenderer.positionCount = 2;
            _lineRenderer.SetPosition(0, Vector3.zero);
            _lineRenderer.SetPosition(1, Vector3.up * length);
            _lineRenderer.startWidth = width;
            _lineRenderer.endWidth = width;
            _lineRenderer.sortingOrder = 10;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _lineRenderer.material = new Material(shader);
            _lineRenderer.startColor = color;
            _lineRenderer.endColor = color;
        }

        private void Update()
        {
            if (Time.timeScale == 0f) return;

            _elapsed += Time.deltaTime;

            // Fade alpha over the last 25% of the beam's life.
            var fadeStart = _duration * 0.75f;
            var alpha = _elapsed < fadeStart
                ? 1f
                : 1f - (_elapsed - fadeStart) / (_duration - fadeStart);

            var c = _beamColor;
            c.a = alpha;
            _lineRenderer.startColor = c;
            _lineRenderer.endColor = c;

            if (_elapsed >= _duration)
                Destroy(gameObject);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // Skip any Enemy (including the satellite that owns this beam).
            if (other.GetComponent<Enemy>() != null) return;

            other.GetComponent<ICanTakeDamage>()?.AddHealth(-_damage);
        }
    }
}
