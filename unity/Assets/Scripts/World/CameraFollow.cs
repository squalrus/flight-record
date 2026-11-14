using UnityEngine;

namespace FlightRecord
{
    public class CameraFollow : MonoBehaviour
    {
        public Transform Target;
        public float BaseSize = 20f;

        private Camera _camera;

        // Additive offsets applied on top of BaseSize each frame.
        // Set from external systems (speed zoom, screen shake, etc.) and
        // reset each frame so callers don't need to clean up after themselves.
        private float _sizeOffset;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            // Target (the player ship) is destroyed on death, but this still runs at least
            // once more before the Game Over scene load actually unloads Main.
            if (Target == null) return;

            var pos = Target.position;
            transform.position = new Vector3(pos.x, pos.y, -10f);
            _camera.orthographicSize = BaseSize + _sizeOffset;
            _sizeOffset = 0f;
        }

        // Call each frame from speed/shake systems to add a transient size delta.
        public void AddSizeOffset(float delta) => _sizeOffset += delta;
    }
}
