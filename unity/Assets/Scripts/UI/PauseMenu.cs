using UnityEngine;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // Attach to a GameObject with a UIDocument (Source Asset = PauseMenu.uxml) in Main.unity.
    [RequireComponent(typeof(UIDocument))]
    public class PauseMenu : MonoBehaviour
    {
        private float _timeScaleReference = 1f;

        public GameObject StatisticsScreen;
        public GameObject ShipManagementScreen;
        public GameObject SettingsScreen;
        public GameObject MissionScreen;

        private VisualElement _root;
        private VisualElement _menuRoot;
        private Button _resumeBtn;
        private ShipManagementController _shipManagement;
        private StatisticsController _statistics;
        private SettingsController _settings;
        private MissionScreenController _mission;

        private void OnEnable()
        {
            _shipManagement = ShipManagementScreen.GetComponent<ShipManagementController>();
            _statistics = StatisticsScreen.GetComponent<StatisticsController>();
            _settings = SettingsScreen.GetComponent<SettingsController>();
            _mission = MissionScreen.GetComponent<MissionScreenController>();

            var root = GetComponent<UIDocument>().rootVisualElement;
            _root = root;
            GamepadUiNavigation.EnsureBound(root);
            _menuRoot = root.Q<VisualElement>("pause-menu");
            _resumeBtn = root.Q<Button>("resume-btn");

            _resumeBtn?.RegisterCallback<ClickEvent>(_ => TogglePause());
            root.RegisterCallback<NavigationCancelEvent>(_ => TogglePause());
            root.Q<Button>("mission-btn")?.RegisterCallback<ClickEvent>(_ => ShowMission());
            root.Q<Button>("statistics-btn")?.RegisterCallback<ClickEvent>(_ => ShowStatistics());
            root.Q<Button>("quit-btn")?.RegisterCallback<ClickEvent>(_ => Quit());

            root.Q<Button>("settings-btn")?.RegisterCallback<ClickEvent>(_ => ShowSettings());

            var versionLabel = root.Q<Label>("version-label");
            if (versionLabel != null)
                versionLabel.text = "v" + Application.version;
        }

        public void TogglePause()
        {
            if (Time.timeScale == 0)
            {
                Time.timeScale = _timeScaleReference;
                _menuRoot.style.display = DisplayStyle.None;
                _statistics.Hide();
                _shipManagement.Hide();
                _settings.Hide();
                _mission.Hide();
            }
            else
            {
                // Ship Management and Pause Menu are mutually exclusive -- don't stack the
                // Pause Menu on top of an already-open Ship Management screen.
                if (_shipManagement.IsShown) return;

                _timeScaleReference = Time.timeScale;
                Time.timeScale = 0f;
                GamepadUiNavigation.EnsureBound(_root);
                _menuRoot.style.display = DisplayStyle.Flex;
                _resumeBtn?.Focus();
            }
        }

        public void ShowStatistics()
        {
            _menuRoot.style.display = DisplayStyle.None;
            _statistics.Show();
        }

        public void CloseStatistics()
        {
            _statistics.Hide();
            GamepadUiNavigation.EnsureBound(_root);
            _menuRoot.style.display = DisplayStyle.Flex;
            _resumeBtn?.Focus();
        }

        public void ShowMission()
        {
            _menuRoot.style.display = DisplayStyle.None;
            _mission.Show();
        }

        public void CloseMission()
        {
            _mission.Hide();
            GamepadUiNavigation.EnsureBound(_root);
            _menuRoot.style.display = DisplayStyle.Flex;
            _resumeBtn?.Focus();
        }

        public bool IsShipManagementOpen => _shipManagement.IsShown;

        // Opens Ship Management straight from the HUD's Manage button or gamepad Select --
        // the only way to reach this screen mid-sector. Deliberately does NOT pause the
        // game: enemies, projectiles, and asteroids keep moving while the menu is open, so
        // running out of fuel or ammo and getting shot is still a real risk during a
        // purchase (PlayerController separately blocks piloting/firing input while this is
        // open, via IsShipManagementOpen, without touching Time.timeScale or PlayerInput).
        // Guarded against being called again while already open (e.g. a second click/press
        // before the screen has registered), and against opening while the Pause Menu (or
        // anything else that zeroes Time.timeScale, e.g. the between-sector flow) is active
        // -- Ship Management and Pause Menu are mutually exclusive.
        public void OpenShipManagement()
        {
            if (_shipManagement.IsShown || Time.timeScale == 0f) return;

            _shipManagement.Show();
        }

        public void CloseShipManagement()
        {
            if (!_shipManagement.IsShown) return;

            _shipManagement.Hide();
        }

        // Used by the gamepad Select binding so the same button opens and closes the screen,
        // mirroring how Start toggles the Pause Menu via TogglePause().
        public void ToggleShipManagement()
        {
            if (_shipManagement.IsShown) CloseShipManagement();
            else OpenShipManagement();
        }

        public void ShowSettings()
        {
            _menuRoot.style.display = DisplayStyle.None;
            _settings.Show();
        }

        public void CloseSettings()
        {
            _settings.Hide();
            GamepadUiNavigation.EnsureBound(_root);
            _menuRoot.style.display = DisplayStyle.Flex;
            _resumeBtn?.Focus();
        }

        public void Quit()
        {
            Application.Quit();
        }
    }
}
