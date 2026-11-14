using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // Attach to a GameObject with a UIDocument (Source Asset = MainMenu.uxml) in Start.unity.
    [RequireComponent(typeof(UIDocument), typeof(LoadLevel))]
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] private GameObject _lifetimeStatsScreen;
        [SerializeField] private GameObject _profileSetupScreen;
        [SerializeField] private GameObject _settingsScreen;
        [SerializeField] private GameObject _creditsScreen;

        private VisualElement _root;
        private VisualElement _menuRoot;
        private VisualElement _newGameConfirm;
        private Button _newGameBtn;
        private Button _statisticsBtn;
        private Button _settingsBtn;
        private Button _creditsBtn;
        private SettingsController _settings;
        private LifetimeStatsController _lifetimeStats;
        private ProfileSetupController _profileSetup;
        private CreditsController _credits;
        private bool _hasActiveRun;

        private void OnEnable()
        {
            _settings = _settingsScreen.GetComponent<SettingsController>();
            _lifetimeStats = _lifetimeStatsScreen.GetComponent<LifetimeStatsController>();
            _profileSetup = _profileSetupScreen.GetComponent<ProfileSetupController>();
            _credits = _creditsScreen.GetComponent<CreditsController>();

            var root = GetComponent<UIDocument>().rootVisualElement;
            _root = root;
            GamepadUiNavigation.EnsureBound(root);

            _menuRoot = root.Q<VisualElement>("main-menu");
            _newGameBtn = root.Q<Button>("new-game-btn");
            var continueBtn = root.Q<Button>("continue-btn");
            _settingsBtn = root.Q<Button>("settings-btn");
            _statisticsBtn = root.Q<Button>("statistics-btn");
            _creditsBtn = root.Q<Button>("credits-btn");
            var versionLabel = root.Q<Label>("version-label");

            _newGameConfirm = root.Q<VisualElement>("new-game-confirm");
            var newGameCancelBtn = root.Q<Button>("new-game-cancel-btn");
            var newGameConfirmBtn = root.Q<Button>("new-game-confirm-btn");
            _newGameConfirm.style.display = DisplayStyle.None;
            newGameCancelBtn?.RegisterCallback<ClickEvent>(_ => HideNewGameConfirm());
            newGameConfirmBtn?.RegisterCallback<ClickEvent>(_ => ConfirmNewGame());
            _newGameConfirm.RegisterCallback<NavigationCancelEvent>(_ => HideNewGameConfirm());

            _newGameBtn?.RegisterCallback<ClickEvent>(_ => OnNewGameClicked());

            var loadLevel = GetComponent<LoadLevel>();
            var saveFile = Application.persistentDataPath + "/stats.json";
            var stats = File.Exists(saveFile)
                ? JsonUtility.FromJson<PlayerStatistics>(File.ReadAllText(saveFile))
                : null;
            // File.Exists alone isn't enough -- the save file exists permanently once Lifetime*
            // totals exist, even after the run that earned them ended in Game Over.
            var hasSave = stats != null && stats.HasActiveRun;
            _hasActiveRun = hasSave;
            if (continueBtn != null) continueBtn.style.display = hasSave ? DisplayStyle.Flex : DisplayStyle.None;
            continueBtn?.RegisterCallback<ClickEvent>(_ => loadLevel.LoadScene());

            if (hasSave)
                RefreshContinueCard(root, stats);

            _statisticsBtn?.RegisterCallback<ClickEvent>(_ => ShowLifetimeStats());

            _settingsBtn?.RegisterCallback<ClickEvent>(_ => ShowSettings());

            _creditsBtn?.RegisterCallback<ClickEvent>(_ => ShowCredits());

            if (versionLabel != null)
                versionLabel.text = "v" + Application.version;

            // Focus() doesn't reliably stick when called the same frame the UIDocument's
            // panel is created (which OnEnable runs on for the very first scene load) --
            // scheduling it lets the panel finish initializing first. PauseMenu doesn't need
            // this because it focuses on a later user action (toggling pause), well after
            // its panel already exists.
            var focusTarget = hasSave ? continueBtn : _newGameBtn;
            root.schedule.Execute(() => focusTarget?.Focus());
        }

        // Only the Main Menu's New Game button needs this -- by the time the player is back
        // here, GameControl's OnDisable() has already flushed the run's Lifetime* totals to
        // stats.json (it's a DontDestroyOnLoad singleton that saves on scene unload), so the
        // dialog can truthfully say those stats are preserved without an extra save step here.
        private void OnNewGameClicked()
        {
            if (_hasActiveRun)
                ShowNewGameConfirm();
            else
                ShowProfileSetup();
        }

        private void ShowNewGameConfirm()
        {
            _menuRoot.style.display = DisplayStyle.None;
            _newGameConfirm.style.display = DisplayStyle.Flex;
            _newGameConfirm.Q<Button>("new-game-cancel-btn")?.Focus();
        }

        private void HideNewGameConfirm()
        {
            _newGameConfirm.style.display = DisplayStyle.None;
            _menuRoot.style.display = DisplayStyle.Flex;
            GamepadUiNavigation.EnsureBound(_root);
            _newGameBtn?.Focus();
        }

        private void ConfirmNewGame()
        {
            _newGameConfirm.style.display = DisplayStyle.None;
            ShowProfileSetup();
        }

        private void RefreshContinueCard(VisualElement root, PlayerStatistics stats)
        {
            var shipGlyph = root.Q<Label>("continue-ship-glyph");
            var avatarGlyph = root.Q<Label>("continue-avatar-glyph");
            var sectorVal = root.Q<Label>("continue-sector-val");
            var playtimeVal = root.Q<Label>("continue-playtime-val");
            var partsVal = root.Q<Label>("continue-parts-val");

            if (shipGlyph != null) shipGlyph.text = stats.ShipId;
            if (avatarGlyph != null) avatarGlyph.text = stats.AvatarGlyph;
            if (sectorVal != null) sectorVal.text = "SECTOR " + RomanNumeral.ToRoman(stats.CurrentSector);

            if (playtimeVal != null) playtimeVal.text = PlaytimeFormat.ToClock(stats.LifetimePlaytimeSeconds);

            if (partsVal != null) partsVal.text = Mathf.FloorToInt(stats.Parts) + " PARTS";
        }

        public void ShowLifetimeStats()
        {
            _menuRoot.style.display = DisplayStyle.None;
            _lifetimeStats.Show();
        }

        public void CloseLifetimeStats()
        {
            _lifetimeStats.Hide();
            GamepadUiNavigation.EnsureBound(_root);
            _menuRoot.style.display = DisplayStyle.Flex;
            _statisticsBtn?.Focus();
        }

        public void ShowProfileSetup()
        {
            _menuRoot.style.display = DisplayStyle.None;
            _profileSetup.Show();
        }

        public void CloseProfileSetup()
        {
            _profileSetup.Hide();
            GamepadUiNavigation.EnsureBound(_root);
            _menuRoot.style.display = DisplayStyle.Flex;
            _newGameBtn?.Focus();
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
            _settingsBtn?.Focus();
        }

        public void ShowCredits()
        {
            _menuRoot.style.display = DisplayStyle.None;
            _credits.Show();
        }

        public void CloseCredits()
        {
            _credits.Hide();
            GamepadUiNavigation.EnsureBound(_root);
            _menuRoot.style.display = DisplayStyle.Flex;
            _creditsBtn?.Focus();
        }
    }
}
