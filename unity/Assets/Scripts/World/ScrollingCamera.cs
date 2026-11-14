using UnityEngine;

namespace FlightRecord
{
    // Horizontal scrolling (D-016): the camera moves right on its own at ScrollSpeed,
    // and the ships are bounded to its view (PlayerController). The parallax layers
    // (ParallaxBackground, GalaxyDecal) read the camera's position, so they scroll with it.
    //
    // ScrollSpeed is the one tunable value. Change it at any time, from the Inspector or
    // SetScrollSpeed (later the audio-driven generator); the actual speed eases toward it
    // at SpeedChangeRate so changes mid-run are smooth.
    //
    // The scroll advances in FixedUpdate so the physics step (the ship's bound and the
    // scroll velocity it inherits) sees exactly where the view is, and the rendered camera
    // is interpolated between steps like an interpolated Rigidbody2D, so a ship held at
    // an edge stays put on screen instead of jittering.
    //
    // Runs before PlayerController's FixedUpdate and the parallax layers' LateUpdate.
    [DefaultExecutionOrder(-100)]
    public class ScrollingCamera : MonoBehaviour
    {
        // Ship the camera centers on at start. The camera does not follow it afterwards.
        public Transform Target;
        public float BaseSize = 20f;

        [Header("Scrolling")]
        [Tooltip("Target scroll speed to the right, in world units per second. Can be changed at runtime.")]
        [Range(0f, 40f)]
        public float ScrollSpeed = 4f;
        [Tooltip("How fast the actual speed moves toward ScrollSpeed, in units per second per second.")]
        [Range(0.1f, 50f)]
        public float SpeedChangeRate = 4f;

        private Camera _camera;
        private float _currentSpeed;
        private float _previousX;
        private float _physicsX;

        // Additive offsets applied on top of BaseSize each frame.
        // Set from external systems (speed zoom, screen shake, etc.) and
        // reset each frame so callers don't need to clean up after themselves.
        private float _sizeOffset;

        // Speed the view is scrolling at right now (it eases toward ScrollSpeed).
        public float CurrentSpeed => _currentSpeed;

        // Scroll velocity for the current physics step; ships add it so a centered stick
        // holds them still on screen.
        public Vector2 ScrollVelocity => new Vector2(_currentSpeed, 0f);

        // Center of the view as of the latest physics step. Physics code should bound
        // against this, not transform.position, which is interpolated for rendering.
        public Vector2 PhysicsCenter => new Vector2(_physicsX, transform.position.y);

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            // Centered here, before any FixedUpdate, so the player's screen bound is
            // around the ship from the first physics step.
            if (Target != null)
            {
                var pos = Target.position;
                transform.position = new Vector3(pos.x, pos.y, -10f);
            }
            _physicsX = _previousX = transform.position.x;
            _currentSpeed = ScrollSpeed;
            _camera.orthographicSize = BaseSize;
        }

        private void FixedUpdate()
        {
            var dt = Time.fixedDeltaTime;
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, ScrollSpeed, SpeedChangeRate * dt);
            _previousX = _physicsX;
            _physicsX += _currentSpeed * dt;
        }

        private void LateUpdate()
        {
            // Same scheme as Rigidbody2D interpolation: draw between the last two physics
            // positions, by how far real time has run past the latest step.
            var alpha = Time.fixedDeltaTime > 0f
                ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
                : 1f;
            var pos = transform.position;
            transform.position = new Vector3(Mathf.Lerp(_previousX, _physicsX, alpha), pos.y, pos.z);

            _camera.orthographicSize = BaseSize + _sizeOffset;
            _sizeOffset = 0f;
        }

        // Set the target scroll speed; the actual speed eases toward it at SpeedChangeRate.
        public void SetScrollSpeed(float unitsPerSecond) => ScrollSpeed = Mathf.Max(0f, unitsPerSecond);

        // Call each frame from speed/shake systems to add a transient size delta.
        public void AddSizeOffset(float delta) => _sizeOffset += delta;
    }
}
