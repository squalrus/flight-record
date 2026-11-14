using UnityEngine;

namespace FlightRecord
{
    // Non-tiling sprite (e.g. a 1024x1024 galaxy PNG) placed at a random position each
    // time the scene loads, so it doesn't appear in the same spot every run. Drifts at a
    // tiny fraction of camera speed so it appears further away than any tiling parallax
    // layer.
    //
    // With a ScrollingCamera (Main), the decal starts somewhere in or just ahead of the
    // view, and once it has drifted fully off the left edge it comes back in from the
    // right with a new variant and height, so a long side keeps showing one now and then.
    // Without one (the title and game-over scenes), it is anchored at a random world
    // position within ±100 on x/y, as before.
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
        private SpriteRenderer _renderer;
        private bool _wraps;
        private Vector2 _anchor;
        private float _z;

        private void Start()
        {
            _camera = Camera.main;
            _renderer = GetComponent<SpriteRenderer>();
            _wraps = _camera.GetComponent<ScrollingCamera>() != null;
            _z = transform.position.z;

            PickVariant();

            if (_wraps)
            {
                // Anywhere from the middle of the view to one view-width past the right edge.
                var halfView = HalfViewSize();
                PlaceAtScreenOffset(Random.Range(0f, 2f * halfView.x) + HalfDecalWidth(), RandomScreenY(halfView));
            }
            else
            {
                _anchor = new Vector2(Random.Range(-100f, 100f), Random.Range(-100f, 100f));
            }
        }

        private void LateUpdate()
        {
            Vector2 camPos = _camera.transform.position;

            if (_wraps)
            {
                var halfView = HalfViewSize();
                var screenX = ScreenOffset(camPos).x;
                if (screenX + HalfDecalWidth() < -halfView.x)
                {
                    PickVariant();
                    // Re-enter past the right edge, with a random gap so the timing varies.
                    PlaceAtScreenOffset(halfView.x + HalfDecalWidth() + Random.Range(0f, halfView.x), RandomScreenY(halfView));
                }
            }

            // As camera moves by d, this object moves by d*(1-factor) so it appears
            // to move at only factor*d in screen space — extremely slow = very far away.
            transform.position = new Vector3(
                _anchor.x + camPos.x * (1f - _parallaxFactor),
                _anchor.y + camPos.y * (1f - _parallaxFactor),
                _z
            );
        }

        private void PickVariant()
        {
            if (_variants != null && _variants.Length > 0)
                _renderer.sprite = _variants[Random.Range(0, _variants.Length)];
        }

        // Where the decal sits relative to the camera center, given the current anchor.
        private Vector2 ScreenOffset(Vector2 camPos) => _anchor - camPos * _parallaxFactor;

        // Sets the anchor so the decal's center lands at this offset from the camera center.
        private void PlaceAtScreenOffset(float x, float y)
        {
            Vector2 camPos = _camera.transform.position;
            _anchor = new Vector2(x, y) + camPos * _parallaxFactor;
        }

        private float RandomScreenY(Vector2 halfView) => Random.Range(-halfView.y, halfView.y);

        private Vector2 HalfViewSize()
        {
            var halfHeight = _camera.orthographicSize;
            return new Vector2(halfHeight * _camera.aspect, halfHeight);
        }

        // From the sprite and scale, not renderer bounds, which lag a sprite swap until
        // the renderer updates.
        private float HalfDecalWidth()
        {
            var sprite = _renderer.sprite;
            return sprite != null ? sprite.bounds.extents.x * Mathf.Abs(transform.lossyScale.x) : 0f;
        }
    }
}
