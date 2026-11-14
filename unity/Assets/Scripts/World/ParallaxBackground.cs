using UnityEngine;

namespace FlightRecord
{
    // Attach to a full-screen sprite whose texture Wrap Mode is set to Repeat.
    // The sprite follows the camera and shifts its texture offset at a fraction
    // of the camera's travel, creating the illusion of depth.
    [RequireComponent(typeof(SpriteRenderer))]
    [DefaultExecutionOrder(100)]
    public class ParallaxBackground : MonoBehaviour
    {
        // Design reference: deep=0.15, mid=0.45, fore=1.0, future foreground layer >1.0
        // 0 = texture never shifts (deepest); 1 = world-aligned; >1 = closer than world plane
        [SerializeField, Range(0f, 2f)] float _parallaxFactor = 0.15f;
        [SerializeField] Sprite[] _variants;

        private SpriteRenderer _renderer;
        private Camera _camera;
        private Vector2 _startCameraPos;
        private Vector2 _spriteSize;

        private void Start()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _camera = Camera.main;
            _startCameraPos = _camera.transform.position;

            if (_variants != null && _variants.Length > 0)
                _renderer.sprite = _variants[Random.Range(0, _variants.Length)];

            // Use renderer bounds (world-space, scale-aware) not sprite bounds (local, unscaled).
            _spriteSize = _renderer.bounds.size;
        }

        private void LateUpdate()
        {
            Vector2 camPos = _camera.transform.position;

            transform.position = new Vector3(camPos.x, camPos.y, transform.position.z);

            Vector2 travel = camPos - _startCameraPos;
            _renderer.material.mainTextureOffset = new Vector2(
                travel.x * _parallaxFactor / _spriteSize.x,
                travel.y * _parallaxFactor / _spriteSize.y
            );
        }
    }
}
