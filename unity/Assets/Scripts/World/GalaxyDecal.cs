using UnityEngine;

namespace FlightRecord
{
    // Non-tiling sprite (e.g. a 1024x1024 galaxy PNG) anchored to a random world
    // position (±100 on x/y) each time the sector loads, so it doesn't appear in
    // the same spot every run. Drifts at a tiny fraction of camera speed so it
    // appears further away than any tiling parallax layer.
    //
    // Design reference factor: 0.08 (behind the deep tiling layer at 0.15).
    // Semantics: 0 = pinned to screen (no depth); 1 = fixed in world (no parallax).
    [RequireComponent(typeof(SpriteRenderer))]
    [DefaultExecutionOrder(100)]
    public class GalaxyDecal : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float _parallaxFactor = 0.08f;
        [SerializeField] Sprite[] _variants;

        private Camera _camera;
        private Vector2 _initialWorldPos;
        private float _z;

        private void Start()
        {
            _camera = Camera.main;

            if (_variants != null && _variants.Length > 0)
                GetComponent<SpriteRenderer>().sprite = _variants[Random.Range(0, _variants.Length)];

            _initialWorldPos = new Vector2(Random.Range(-100f, 100f), Random.Range(-100f, 100f));
            _z = transform.position.z;
        }

        private void LateUpdate()
        {
            Vector2 camPos = _camera.transform.position;
            // As camera moves by d, this object moves by d*(1-factor) so it appears
            // to move at only factor*d in screen space — extremely slow = very far away.
            transform.position = new Vector3(
                _initialWorldPos.x + camPos.x * (1f - _parallaxFactor),
                _initialWorldPos.y + camPos.y * (1f - _parallaxFactor),
                _z
            );
        }
    }
}
