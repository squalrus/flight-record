using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // Attach to a GameObject with a UIDocument (Source Asset = Settings.uxml) in Main.unity.
    [RequireComponent(typeof(UIDocument))]
    public class SettingsController : MonoBehaviour
    {
        private const long CLEAR_DATA_DISARM_MS = 3000;

        // Reused from both the Pause Menu (mid-run) and the Main Menu (pre-run) -- exactly
        // one of these is assigned per scene instance; only that one's Close call does anything.
        [SerializeField] private PauseMenu _pauseMenu;
        [SerializeField] private MainMenuController _mainMenu;

        private VisualElement _root;
        private Button _closeBtn;
        private Button _rumbleToggleBtn;
        private Button _clearDataBtn;
        private bool _clearDataArmed;
        private IVisualElementScheduledItem _disarmSchedule;
        private Coroutine _rumbleCoroutine;

        public bool IsShown => _root != null && _root.style.display != DisplayStyle.None;

        // Disabling the GameObject via SetActive detaches the UIDocument's panel and can leave
        // buttons unable to register clicks on a later reopen (the same class of bug fixed for
        // ShipManagementController and the between-sector screens). The GameObject now stays
        // active permanently; Show()/Hide() toggle the root's display style instead, and
        // click/cancel handlers are bound once here rather than re-registered each reopen.
        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;

            _rumbleToggleBtn = _root.Q<Button>("rumble-toggle-btn");
            _clearDataBtn = _root.Q<Button>("clear-data-btn");
            var clearDataRule = _root.Q<VisualElement>("clear-data-rule");

            // Clearing the save while a run is in progress (Pause Menu) would wipe lifetime
            // stats and upgrades out from under an active sector with no way back -- only
            // expose it from the Main Menu, before any run has started. Which reference is set
            // is fixed per scene instance, so this only needs to run once.
            if (_pauseMenu != null)
            {
                _clearDataBtn?.RemoveFromHierarchy();
                clearDataRule?.RemoveFromHierarchy();
                _clearDataBtn = null;
            }

            _closeBtn = _root.Q<Button>("close-btn");

            _rumbleToggleBtn?.RegisterCallback<ClickEvent>(_ => ToggleRumble());
            _clearDataBtn?.RegisterCallback<ClickEvent>(_ => OnClearDataClicked());
            _closeBtn?.RegisterCallback<ClickEvent>(_ => Close());
            _root.RegisterCallback<NavigationCancelEvent>(_ => Close());

            _root.style.display = DisplayStyle.None;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            _root.style.display = DisplayStyle.Flex;
            GamepadUiNavigation.EnsureBound(_root);
            RefreshRumbleToggleLabel();
            if (_clearDataBtn != null) RefreshClearDataLabel();
            _closeBtn?.Focus();
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
        }

        private void Close()
        {
            _pauseMenu?.CloseSettings();
            _mainMenu?.CloseSettings();
        }

        // GameControl.control is null when Settings is opened from the Start scene's Main
        // Menu before any run has started this session (its singleton GameObject only lives
        // in Main.unity) -- fall back to GameControl's static stats.json read/write directly
        // in that case, same as ClearSaveData()/LifetimeStatsController already do.
        private void ToggleRumble()
        {
            bool enabled;
            if (GameControl.control != null)
            {
                GameControl.control.RumbleEnabled = !GameControl.control.RumbleEnabled;
                enabled = GameControl.control.RumbleEnabled;
            }
            else
            {
                enabled = !GameControl.GetRumbleEnabled();
                GameControl.SetRumbleEnabled(enabled);
            }

            RefreshRumbleToggleLabel();

            // Immediate confirmation that the setting took effect, rather than making the
            // player wait for the next in-game hit/shot to find out the toggle actually works.
            if (enabled) PulseRumble();
        }

        private void PulseRumble()
        {
            if (Gamepad.current == null) return;

            if (_rumbleCoroutine != null) StopCoroutine(_rumbleCoroutine);
            _rumbleCoroutine = StartCoroutine(RumbleGamepad(0.5f, 0.2f));
        }

        private IEnumerator RumbleGamepad(float intensity, float duration)
        {
            Gamepad.current.SetMotorSpeeds(intensity, intensity);
            yield return new WaitForSeconds(duration);
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
        }

        private void RefreshRumbleToggleLabel()
        {
            if (_rumbleToggleBtn == null) return;

            bool enabled = GameControl.control != null ? GameControl.control.RumbleEnabled : GameControl.GetRumbleEnabled();
            _rumbleToggleBtn.text = enabled ? "ON" : "OFF";
        }

        // Two-click arm/confirm instead of a modal dialog -- this button is also reachable from
        // the Pause Menu mid-run, where stacking a second full-screen overlay on an already-paused
        // game adds friction for what's otherwise a same-screen action. New Game Confirmation
        // (Main Menu only, see MainMenuController.ShowNewGameConfirm()) uses a real modal instead,
        // since it needs to state context (lifetime stats are preserved) that doesn't fit on a
        // relabeled button. First click here arms a short confirmation window, second click within
        // it actually deletes the save.
        private void OnClearDataClicked()
        {
            if (!_clearDataArmed)
            {
                _clearDataArmed = true;
                RefreshClearDataLabel();
                _disarmSchedule = _clearDataBtn.schedule.Execute(() =>
                {
                    _clearDataArmed = false;
                    RefreshClearDataLabel();
                }).StartingIn(CLEAR_DATA_DISARM_MS);
                return;
            }

            _disarmSchedule?.Pause();
            _clearDataArmed = false;
            ClearSavedData();
        }

        private void RefreshClearDataLabel()
        {
            if (_clearDataBtn == null) return;
            _clearDataBtn.text = _clearDataArmed ? "CONFIRM CLEAR?" : "CLEAR SAVED DATA";
        }

        private void ClearSavedData()
        {
            GameControl.ClearAllSaveData();
            Time.timeScale = 1f;
            SceneManager.LoadScene("Start");
        }
    }
}
