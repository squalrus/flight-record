using UnityEngine;

namespace FlightRecord
{
    // The visible playfield, read from the main camera. With a ScrollingCamera the center is
    // where the view is as of the current physics step (PhysicsCenter), not the interpolated
    // rendered position, so physics code that places things relative to the view lines up
    // with PlayerController's screen bound.
    public static class Playfield
    {
        private static Camera _camera;
        private static ScrollingCamera _scroller;

        public static bool TryGetView(out Vector2 center, out Vector2 halfSize)
        {
            center = halfSize = Vector2.zero;
            if (_camera == null)
            {
                _camera = Camera.main;
                _scroller = _camera != null ? _camera.GetComponent<ScrollingCamera>() : null;
            }
            if (_camera == null || !_camera.orthographic) return false;

            center = _scroller != null ? _scroller.PhysicsCenter : (Vector2)_camera.transform.position;
            halfSize = new Vector2(_camera.orthographicSize * _camera.aspect, _camera.orthographicSize);
            return true;
        }

        // View coordinates (0,0 bottom-left, 1,1 top-right) to an offset from the view's
        // center in world units.
        public static Vector2 ViewToLocal(Vector2 view, Vector2 halfSize) =>
            new Vector2((view.x * 2f - 1f) * halfSize.x, (view.y * 2f - 1f) * halfSize.y);
    }
}
