using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace InfinitySpace
{
    // Attach to a GameObject with a UIDocument (Source Asset = LifetimeStats.uxml) in
    // Start.unity. Reads stats.json directly rather than through GameControl.control,
    // since GameControl's singleton GameObject only lives in Main.unity and won't exist
    // yet if the player hasn't started a run this session.
    [RequireComponent(typeof(UIDocument))]
    public class LifetimeStatsController : MonoBehaviour
    {
        [SerializeField] private MainMenuController _mainMenu;

        private VisualElement _root;
        private Button _closeBtn;
        private Label _highestSectorVal;
        private Label _runsStartedVal;
        private Label _sectorsClearedVal;
        private Label _playtimeVal;
        private Label _enemiesDestroyedVal;
        private Label _asteroidsDestroyedVal;
        private Label _shotsFiredVal;
        private Label _accuracyVal;
        private Label _damageDealtVal;
        private Label _damageTakenVal;
        private Label _fuelUsedVal;
        private Label _partsEarnedVal;
        private Label _partsSpentVal;

        public bool IsShown => _root != null && _root.style.display != DisplayStyle.None;

        // Disabling the GameObject via SetActive detaches the UIDocument's panel and can leave
        // buttons unable to register clicks on a later reopen (the same class of bug fixed for
        // ShipManagementController and the Pause Menu's sub-screens). The GameObject now stays
        // active permanently; Show()/Hide() toggle the root's display style instead.
        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;

            _highestSectorVal = _root.Q<Label>("highest-sector-val");
            _runsStartedVal = _root.Q<Label>("runs-started-val");
            _sectorsClearedVal = _root.Q<Label>("sectors-cleared-val");
            _playtimeVal = _root.Q<Label>("playtime-val");
            _enemiesDestroyedVal = _root.Q<Label>("enemies-destroyed-val");
            _asteroidsDestroyedVal = _root.Q<Label>("asteroids-destroyed-val");
            _shotsFiredVal = _root.Q<Label>("shots-fired-val");
            _accuracyVal = _root.Q<Label>("accuracy-val");
            _damageDealtVal = _root.Q<Label>("damage-dealt-val");
            _damageTakenVal = _root.Q<Label>("damage-taken-val");
            _fuelUsedVal = _root.Q<Label>("fuel-used-val");
            _partsEarnedVal = _root.Q<Label>("parts-earned-val");
            _partsSpentVal = _root.Q<Label>("parts-spent-val");

            _closeBtn = _root.Q<Button>("close-btn");
            _closeBtn?.RegisterCallback<ClickEvent>(_ => _mainMenu?.CloseLifetimeStats());
            _root.RegisterCallback<NavigationCancelEvent>(_ => _mainMenu?.CloseLifetimeStats());

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
            var saveFile = Application.persistentDataPath + "/stats.json";
            var stats = File.Exists(saveFile)
                ? JsonUtility.FromJson<PlayerStatistics>(File.ReadAllText(saveFile))
                : new PlayerStatistics();

            _highestSectorVal.text = stats.HighestSectorReached.ToString();
            _runsStartedVal.text = stats.LifetimeRunsStarted.ToString();
            _sectorsClearedVal.text = stats.LifetimeSectorsCleared.ToString();
            _playtimeVal.text = PlaytimeFormat.ToClock(stats.LifetimePlaytimeSeconds);
            _enemiesDestroyedVal.text = stats.LifetimeEnemiesDestroyed.ToString();
            _asteroidsDestroyedVal.text = stats.LifetimeAsteroidsDestroyed.ToString();
            _shotsFiredVal.text = stats.LifetimeShotsFired.ToString();
            _accuracyVal.text = stats.LifetimeShotsFired == 0
                ? "0%"
                : $"{Mathf.RoundToInt((float)stats.LifetimeHits / stats.LifetimeShotsFired * 100f)}%";
            _damageDealtVal.text = stats.LifetimeDamageDealt.ToString();
            _damageTakenVal.text = stats.LifetimeDamageTaken.ToString();
            _fuelUsedVal.text = Mathf.RoundToInt(stats.LifetimeFuelConsumed).ToString();
            _partsEarnedVal.text = stats.LifetimePartsEarned.ToString("F0");
            _partsSpentVal.text = stats.LifetimePartsSpent.ToString("F0");
        }
    }
}
