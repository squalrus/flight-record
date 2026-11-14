using UnityEngine;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // Attach to a GameObject with a UIDocument (Source Asset = SectorComplete.uxml) in Main.unity.
    // Shown by SectorTransitionController when the player enters the portal; page 1 of 3 in the
    // between-sector flow (Sector Complete -> Sector Achievements -> Ship Upgrades). Next hands
    // off to the sector-achievements screen.
    [RequireComponent(typeof(UIDocument))]
    public class SectorCompleteController : MonoBehaviour
    {
        [SerializeField] private SectorTransitionController _sectorTransition;

        private VisualElement _root;
        private Button _nextBtn;
        private Label _titleLabel;
        private Label _missionLabel;
        private Label _enemiesDestroyedVal;
        private Label _asteroidsDestroyedVal;
        private Label _shotsFiredVal;
        private Label _accuracyVal;
        private Label _damageDealtVal;
        private Label _damageTakenVal;
        private Label _fuelUsedVal;
        private Label _partsEarnedVal;
        private Label _partsConsumedVal;
        private Label _timeVal;

        // This screen is shown/hidden via Show()/Hide() (display style) rather than
        // GameObject.SetActive() -- see the identical comment on ShipManagementController for
        // why: SetActive cycling detaches the UIDocument's panel and can leave buttons unable
        // to register clicks on a later reopen, which now matters since Back/Next means this
        // screen can be revisited within one scene load instead of shown exactly once.
        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            GamepadUiNavigation.EnsureBound(_root);

            _titleLabel = _root.Q<Label>("title-label");
            _missionLabel = _root.Q<Label>("mission-label");
            _enemiesDestroyedVal = _root.Q<Label>("enemies-destroyed-val");
            _asteroidsDestroyedVal = _root.Q<Label>("asteroids-destroyed-val");
            _shotsFiredVal = _root.Q<Label>("shots-fired-val");
            _accuracyVal = _root.Q<Label>("accuracy-val");
            _damageDealtVal = _root.Q<Label>("damage-dealt-val");
            _damageTakenVal = _root.Q<Label>("damage-taken-val");
            _fuelUsedVal = _root.Q<Label>("fuel-used-val");
            _partsEarnedVal = _root.Q<Label>("parts-earned-val");
            _partsConsumedVal = _root.Q<Label>("parts-consumed-val");
            _timeVal = _root.Q<Label>("time-val");

            _nextBtn = _root.Q<Button>("next-btn");
            _nextBtn?.RegisterCallback<ClickEvent>(_ => _sectorTransition?.ShowAchievements());

            // No screen to go back to from page 1 -- Cancel falls through to Next instead.
            _root.RegisterCallback<NavigationCancelEvent>(_ => _sectorTransition?.ShowAchievements());

            _root.style.display = DisplayStyle.None;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            _root.style.display = DisplayStyle.Flex;
            GamepadUiNavigation.EnsureBound(_root);
            _nextBtn?.Focus();
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
        }

        public void Populate(Sector sector)
        {
            if (_titleLabel != null && GameControl.control != null)
                _titleLabel.text = $"SECTOR {GameControl.control.CurrentSector} COMPLETE";

            if (_missionLabel != null)
                _missionLabel.text = $"MISSION: {sector.MissionText}";

            if (_enemiesDestroyedVal != null)
                _enemiesDestroyedVal.text = $"{sector.EnemiesDestroyed}/{sector.TotalEnemies}";

            if (_asteroidsDestroyedVal != null)
                _asteroidsDestroyedVal.text = $"{sector.AsteroidsDestroyed}/{sector.TotalAsteroids}";

            if (_shotsFiredVal != null)
                _shotsFiredVal.text = sector.ShotsFired.ToString();

            if (_accuracyVal != null)
                _accuracyVal.text = $"{Mathf.RoundToInt(sector.HitPercentage * 100f)}%";

            if (_damageDealtVal != null)
                _damageDealtVal.text = sector.DamageDealt.ToString();

            if (_damageTakenVal != null)
                _damageTakenVal.text = sector.DamageTaken.ToString();

            if (_fuelUsedVal != null)
                _fuelUsedVal.text = Mathf.RoundToInt(sector.FuelConsumed).ToString();

            if (_partsEarnedVal != null)
                _partsEarnedVal.text = Mathf.RoundToInt(sector.PartsEarned).ToString();

            if (_partsConsumedVal != null)
                _partsConsumedVal.text = Mathf.RoundToInt(sector.PartsSpent).ToString();

            if (_timeVal != null)
            {
                int totalSeconds = Mathf.RoundToInt(sector.ElapsedSeconds);
                _timeVal.text = $"{totalSeconds / 60}:{totalSeconds % 60:D2}";
            }

            GameControl.control?.RecordSectorTime(sector.ElapsedSeconds);
        }
    }
}
