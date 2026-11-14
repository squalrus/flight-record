using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlightRecord
{
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerController : MonoBehaviour
    {
        public static Transform PlayerTransform { get; private set; }

        // Every live player ship, for enemies that target the nearest one (4.12). There is
        // one player until 5.3 adds joining; enemies already go through NearestTo so they
        // need no change then.
        private static readonly List<PlayerController> Active = new List<PlayerController>();

        public static Transform NearestTo(Vector2 position)
        {
            Transform nearest = null;
            var best = float.MaxValue;
            foreach (var player in Active)
            {
                var distance = ((Vector2)player.transform.position - position).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = player.transform;
            }
            return nearest;
        }

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        // Horizontal scrolling (D-016): the ship always faces right and moves straight from
        // the stick, with no rotation and no momentum, inside the camera's visible rect.
        // The ship's art and Cannon point along local +Y, so facing right is -90 degrees.
        private const float FACING_RIGHT = -90f;

        [Header("Movement")]
        [Tooltip("Ship speed in world units per second at full stick deflection.")]
        [Range(1f, 60f)]
        public float MoveSpeed = 22f;
        [Tooltip("Extra gap, in world units, kept between the ship's collider and the screen edges.")]
        [Range(0f, 5f)]
        public float EdgeMargin = 0.5f;
        [Tooltip("How far right the ship may go, as a fraction of the visible width (1 = the right edge).")]
        [Range(0.25f, 1f)]
        public float MaxRightFraction = 1f;

        private Rigidbody2D _player;
        private Player _ship;
        private PauseMenu _pauseMenu;
        private Camera _camera;
        private ScrollingCamera _scroller;
        private Vector2 _halfExtents;
        private Vector2 _moveInput;
        private bool _firing;

        private void Awake()
        {
            PlayerTransform = transform;
        }

        private void Start()
        {
            _player = GetComponent<Rigidbody2D>();
            _player.GetComponentInChildren<MeshRenderer>().sortingLayerName = "Player";
            _player.GetComponentInChildren<ParticleSystem>().GetComponent<Renderer>().sortingLayerName = "Player";
            _ship = GetComponent<Player>();
            _pauseMenu = FindFirstObjectByType<PauseMenu>();
            CacheCamera();

            transform.rotation = Quaternion.Euler(0f, 0f, FACING_RIGHT);
            // Push the new rotation to the physics body and collider now, so the collider
            // bounds read below are the right-facing ones.
            Physics2D.SyncTransforms();
            _player.freezeRotation = true;
            _player.angularVelocity = 0f;
            // Velocity is set exactly each step; damping would shave it and leave the
            // ship short of the stick's target and of the screen edge.
            _player.linearDamping = 0f;
            // Physics steps at a fixed rate; without interpolation, direct movement
            // stutters against a still camera on displays faster than the physics rate.
            _player.interpolation = RigidbodyInterpolation2D.Interpolate;

            // Rotation never changes after this, so the collider's world extents are fixed.
            var collider = GetComponent<Collider2D>();
            _halfExtents = collider != null ? (Vector2)collider.bounds.extents : Vector2.zero;
        }

        // True while Ship Management is open. That screen deliberately doesn't pause the
        // game (Time.timeScale stays > 0 and PlayerInput stays enabled, so Start/Select keep
        // working to close it), so piloting/firing input is blocked here instead -- otherwise
        // the stick still flies the ship and Fire both shoots and clicks the menu's
        // gamepad-Submit-focused button at the same time.
        private bool IsShipManagementOpen => _pauseMenu != null && _pauseMenu.IsShipManagementOpen;

        // Tracks the previous frame's pause state so we can detect the resume transition --
        // PlayerInput stays enabled while paused (so OnPause/OnShipManagement keep working),
        // which means OnFire keeps updating _firing even while paused. On a gamepad, the same
        // physical button (south) both clicks "Resume" via GamepadUiNavigation's simulated
        // Submit and drives the Fire action, so pressing it to resume leaves _firing true the
        // instant Time.timeScale is restored, firing a shot on the same frame the player meant
        // only to unpause. Update() still runs every frame regardless of Time.timeScale, so
        // this transition is reliably caught here.
        private bool _wasPaused;

        private void Update()
        {
            bool isPaused = Time.timeScale <= 0f;
            if (_wasPaused && !isPaused)
                _firing = false;
            _wasPaused = isPaused;

            if (_firing && Time.timeScale > 0f && !IsShipManagementOpen)
                _ship.FirePrimaryWeapon();
        }

        private void FixedUpdate()
        {
            var input = IsShipManagementOpen ? Vector2.zero : Vector2.ClampMagnitude(_moveInput, 1f);
            var position = _player.position;
            // Riding the scroll keeps a centered stick still on screen; the stick then
            // moves the ship relative to the view, not the world.
            var scroll = _scroller != null ? _scroller.ScrollVelocity : Vector2.zero;
            var target = position + (input * MoveSpeed + scroll) * Time.fixedDeltaTime;

            if (TryGetPlayfield(out var min, out var max))
            {
                target.x = Mathf.Clamp(target.x, min.x, max.x);
                target.y = Mathf.Clamp(target.y, min.y, max.y);
            }

            // Drive the body by velocity (not MovePosition) so it stays a dynamic body that
            // collides normally; overwriting it every step removes momentum and knockback.
            // If something pushed the ship off screen, this also brings it straight back.
            _player.linearVelocity = (target - position) / Time.fixedDeltaTime;
            _player.angularVelocity = 0f;
            _player.rotation = FACING_RIGHT;
        }

        private void CacheCamera()
        {
            _camera = Camera.main;
            _scroller = _camera != null ? _camera.GetComponent<ScrollingCamera>() : null;
        }

        // The rect the ship's center may occupy: the camera's visible area, inset by the
        // ship's half size and EdgeMargin, with the right side limited by MaxRightFraction.
        // Read from the camera every step, so it follows a scrolling or zooming camera.
        private bool TryGetPlayfield(out Vector2 min, out Vector2 max)
        {
            min = max = Vector2.zero;
            if (_camera == null) CacheCamera();
            if (_camera == null || !_camera.orthographic) return false;

            // The scrolling camera's rendered position is interpolated between physics
            // steps; bound against where the view is as of this step instead.
            var center = _scroller != null ? _scroller.PhysicsCenter : (Vector2)_camera.transform.position;
            var halfHeight = _camera.orthographicSize;
            var halfWidth = halfHeight * _camera.aspect;
            var inset = _halfExtents + Vector2.one * EdgeMargin;

            min = new Vector2(center.x - halfWidth, center.y - halfHeight) + inset;
            max = new Vector2(center.x - halfWidth + 2f * halfWidth * MaxRightFraction, center.y + halfHeight) - inset;
            max.x = Mathf.Max(min.x, max.x);
            max.y = Mathf.Max(min.y, max.y);
            return true;
        }

        private void OnMove(InputValue value)
        {
            _moveInput = value.Get<Vector2>();
        }

        private void OnFire(InputValue value)
        {
            _firing = value.isPressed;
        }

        private void OnPause(InputValue value)
        {
            if (value.isPressed)
                _pauseMenu?.TogglePause();
        }

        private void OnShipManagement(InputValue value)
        {
            if (value.isPressed)
                _pauseMenu?.ToggleShipManagement();
        }

        public void AddParts(int parts)
        {
            _ship.AddParts(parts);
        }
    }
}
