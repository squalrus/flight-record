using UnityEngine;
using UnityEngine.EventSystems;

namespace FlightRecord
{
    public class FloatingJoystick : MonoBehaviour, IPointerDownHandler, IPointerMoveHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform _visual;
        [SerializeField] private RectTransform _knob;
        [SerializeField] private float _movementRange = 50f;

        private Canvas _canvas;
        private Vector2 _startLocalPos;
        private PlayerController _playerController;

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
        }

        private void Start()
        {
            _playerController = FindFirstObjectByType<PlayerController>();
        }

        private void OnEnable()
        {
            if (_visual != null)
                _visual.gameObject.SetActive(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_visual == null) return;
            _visual.gameObject.SetActive(true);
            PositionVisualAt(eventData.position);
            if (_knob != null)
                _knob.anchoredPosition = Vector2.zero;
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (_visual == null || !_visual.gameObject.activeSelf) return;
            MoveKnob(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            OnPointerMove(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_knob != null)
                _knob.anchoredPosition = Vector2.zero;
            if (_visual != null)
                _visual.gameObject.SetActive(false);
            _playerController?.SetJoystickInput(Vector2.zero);
        }

        private void MoveKnob(Vector2 screenPosition)
        {
            if (_knob == null) return;
            var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                transform as RectTransform, screenPosition, camera, out var currentLocal);
            var delta = Vector2.ClampMagnitude(currentLocal - _startLocalPos, _movementRange);
            _knob.anchoredPosition = delta;
            _playerController?.SetJoystickInput(delta / _movementRange);
        }

        private void PositionVisualAt(Vector2 screenPosition)
        {
            var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                transform as RectTransform, screenPosition, camera, out var localPoint);
            _visual.anchoredPosition = localPoint;
            _startLocalPos = localPoint;
        }
    }
}
