using UnityEngine;
using UnityEngine.UIElements;

namespace InfinitySpace
{
    // Attach to a GameObject with a UIDocument (Source Asset = Mission.uxml) in Main.unity.
    // Lets the player re-check the sector's objective from the Pause Menu in case the
    // MissionBriefingController overlay already faded out by the time they paused.
    [RequireComponent(typeof(UIDocument))]
    public class MissionScreenController : MonoBehaviour
    {
        [SerializeField] private PauseMenu _pauseMenu;

        private VisualElement _root;
        private Button _closeBtn;
        private Label _missionTextVal;
        private Label _missionDescriptionVal;

        public bool IsShown => _root != null && _root.style.display != DisplayStyle.None;

        // Disabling the GameObject via SetActive detaches the UIDocument's panel and can leave
        // buttons unable to register clicks on a later reopen (the same class of bug fixed for
        // ShipManagementController and the between-sector screens). The GameObject now stays
        // active permanently; Show()/Hide() toggle the root's display style instead.
        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;

            _missionTextVal = _root.Q<Label>("mission-text-val");
            _missionDescriptionVal = _root.Q<Label>("mission-description-val");

            _closeBtn = _root.Q<Button>("close-btn");
            _closeBtn?.RegisterCallback<ClickEvent>(_ => _pauseMenu?.CloseMission());
            _root.RegisterCallback<NavigationCancelEvent>(_ => _pauseMenu?.CloseMission());

            _root.style.display = DisplayStyle.None;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            _root.style.display = DisplayStyle.Flex;
            GamepadUiNavigation.EnsureBound(_root);
            Refresh();
            _closeBtn?.Focus();
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
        }

        private void Refresh()
        {
            if (_missionTextVal == null) return;
            var sector = FindFirstObjectByType<Sector>();
            _missionTextVal.text = sector != null ? sector.MissionText : "CLEAR THE HOSTILES";
            if (_missionDescriptionVal != null)
                _missionDescriptionVal.text = sector != null ? sector.MissionDescription : "Clear the threats and find the portal.";
        }
    }
}
