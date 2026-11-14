using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // The Input System package's bundled DefaultInputActions (assigned to each scene's
    // EventSystem) binds UI Submit via the "*/{Submit}" usage tag, which doesn't resolve to
    // buttonSouth on some gamepad layouts (XInputControllerWindows here) even though Cancel's
    // "*/{Cancel}" tag resolves fine on buttonEast. Move/Cancel both work through the existing
    // EventSystem, so only Submit needs a workaround: this reads buttonSouth/Enter/Space directly
    // and simulates a real ClickEvent on the focused element (Button doesn't convert a manually
    // dispatched NavigationSubmitEvent into a click in this Unity version, so ClickEvent it is).
    public static class GamepadUiNavigation
    {
        private static InputAction _submit;
        private static VisualElement _activeRoot;

        public static void EnsureBound(VisualElement root)
        {
            _activeRoot = root;

            if (_submit == null)
            {
                var asset = ScriptableObject.CreateInstance<InputActionAsset>();
                var map = asset.AddActionMap("UI");

                _submit = map.AddAction("Submit", InputActionType.Button);
                _submit.AddBinding("<Gamepad>/buttonSouth");
                _submit.AddBinding("<Keyboard>/enter");
                _submit.AddBinding("<Keyboard>/space");

                map.Enable();

                _submit.performed += _ => DispatchSubmit();
            }
        }

        private static void DispatchSubmit()
        {
            var focused = _activeRoot?.panel?.focusController?.focusedElement;
            if (focused == null) return;

            using var evt = ClickEvent.GetPooled();
            evt.target = focused;
            focused.SendEvent(evt);
        }
    }
}
