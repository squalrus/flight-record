using UnityEngine;

namespace InfinitySpace
{
    // Purely decorative ring dressing placed at a sector's edge -- see "Sector Boundary Marker
    // (Asteroid Belt)" in BACKLOG.md. Deliberately has no Collider2D/Rigidbody2D/DestroyableObject:
    // it must not be damageable or push against the player, only signal where the play area ends.
    public class BoundaryMarker : MonoBehaviour
    {
        [SerializeField] private float _minRotationSpeed = -8f;
        [SerializeField] private float _maxRotationSpeed = 8f;

        // Kept below Asteroid.MinScale (0.5) so markers never overlap the size range of a
        // destroyable asteroid -- they need to read as distant background dressing, not a
        // same-size hazard the player might try to shoot.
        [SerializeField] private float _minScale = 0.15f;
        [SerializeField] private float _maxScale = 0.4f;

        [SerializeField, Range(0f, 1f)] private float _alpha = 0.3f;

        private float _rotationSpeed;

        private void Start()
        {
            _rotationSpeed = Random.Range(_minRotationSpeed, _maxRotationSpeed);

            var scale = Random.Range(_minScale, _maxScale);
            transform.localScale = new Vector3(scale, scale, 1f);
            transform.Rotate(0f, 0f, Random.Range(0f, 360f));

            var glyph = GetComponent<TextMesh>();
            if (glyph != null)
            {
                var color = glyph.color;
                color.a = _alpha;
                glyph.color = color;
            }
        }

        private void Update()
        {
            transform.Rotate(0f, 0f, _rotationSpeed * Time.deltaTime);
        }
    }
}
