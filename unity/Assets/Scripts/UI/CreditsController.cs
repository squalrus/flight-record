using UnityEngine;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // Attach to a GameObject with a UIDocument (Source Asset = Credits.uxml) in Start.unity.
    [RequireComponent(typeof(UIDocument))]
    public class CreditsController : MonoBehaviour
    {
        [SerializeField] private MainMenuController _mainMenu;

        private VisualElement _root;
        private Button _closeBtn;

        // GameObject stays active permanently; Show()/Hide() toggle the root's display style
        // instead of SetActive, matching SettingsController/ProfileSetupController.
        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;

            _closeBtn = _root.Q<Button>("close-btn");
            _closeBtn?.RegisterCallback<ClickEvent>(_ => Close());
            _root.RegisterCallback<NavigationCancelEvent>(_ => Close());

            _root.style.display = DisplayStyle.None;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            _root.style.display = DisplayStyle.Flex;
            GamepadUiNavigation.EnsureBound(_root);
            _closeBtn?.Focus();
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
        }

        private void Close()
        {
            _mainMenu.CloseCredits();
        }
    }
}
