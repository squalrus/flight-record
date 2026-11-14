using UnityEngine;
using UnityEngine.InputSystem;

namespace FlightRecord
{
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerController : MonoBehaviour
    {
        public static Transform PlayerTransform { get; private set; }

        private Rigidbody2D _player;
        private Player _ship;
        private PauseMenu _pauseMenu;
        private float _speed;
        private Vector2 _moveInput;
        private Vector2 _joystickInput;
        private bool _firing;

        private const float ROTATION_THRESHOLD = .2f;

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
            _speed = _ship.Speed;
            _pauseMenu = FindFirstObjectByType<PauseMenu>();
        }

        // True while Ship Management is open. That screen deliberately doesn't pause the
        // game (Time.timeScale stays > 0 and PlayerInput stays enabled, so Start/Select keep
        // working to close it), so piloting/firing input is blocked here instead -- otherwise
        // the joystick still flies the ship and Fire both shoots and clicks the menu's
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

        public void SetJoystickInput(Vector2 input) { _joystickInput = input; }

        private void FixedUpdate()
        {
            if (IsShipManagementOpen) return;

            var input = _joystickInput.sqrMagnitude > 0f ? _joystickInput : _moveInput;
            if (input.sqrMagnitude > ROTATION_THRESHOLD * ROTATION_THRESHOLD)
            {
                _player.freezeRotation = false;
                var angle = Mathf.Atan2(input.x, input.y) * -Mathf.Rad2Deg;
                _player.rotation = angle;
                if (_ship.TryConsumeFuel(.01f))
                {
                    _player.AddForce(input * _speed);
                }
            }
            else
            {
                _player.freezeRotation = true;
            }
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
