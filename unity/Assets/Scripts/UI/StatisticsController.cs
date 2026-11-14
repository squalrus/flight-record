using UnityEngine;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // Attach to a GameObject with a UIDocument (Source Asset = Statistics.uxml) in Main.unity.
    // Shows stats for the current sector only (Sector's start-of-sector snapshots), not
    // the run-cumulative GameControl totals. PowerupsCollected isn't tracked yet —
    // see "Complete Statistics Tracking" in BACKLOG.md.
    [RequireComponent(typeof(UIDocument))]
    public class StatisticsController : MonoBehaviour
    {
        [SerializeField] private PauseMenu _pauseMenu;
        [SerializeField] private Sector _sector;

        private VisualElement _root;
        private Button _closeBtn;
        private Label _shotsFiredVal;
        private Label _hitPctVal;
        private Label _shipsDestroyedVal;

        public bool IsShown => _root != null && _root.style.display != DisplayStyle.None;

        // Disabling the GameObject via SetActive detaches the UIDocument's panel and can leave
        // buttons unable to register clicks on a later reopen (the same class of bug fixed for
        // ShipManagementController and the between-sector screens). The GameObject now stays
        // active permanently; Show()/Hide() toggle the root's display style instead.
        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;

            // _sector is no longer wired in the Inspector -- see VectorHudController for why.
            if (_sector == null) _sector = FindFirstObjectByType<Sector>();

            _shotsFiredVal = _root.Q<Label>("shots-fired-val");
            _hitPctVal = _root.Q<Label>("hit-pct-val");
            _shipsDestroyedVal = _root.Q<Label>("ships-destroyed-val");

            _closeBtn = _root.Q<Button>("close-btn");
            _closeBtn?.RegisterCallback<ClickEvent>(_ => _pauseMenu?.CloseStatistics());
            _root.RegisterCallback<NavigationCancelEvent>(_ => _pauseMenu?.CloseStatistics());

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
            if (_sector == null) return;

            if (_shotsFiredVal != null) _shotsFiredVal.text = _sector.ShotsFired.ToString();
            if (_hitPctVal != null) _hitPctVal.text = $"{Mathf.RoundToInt(_sector.HitPercentage * 100)}%";
            if (_shipsDestroyedVal != null) _shipsDestroyedVal.text = _sector.EnemiesDestroyed.ToString();
        }
    }
}
