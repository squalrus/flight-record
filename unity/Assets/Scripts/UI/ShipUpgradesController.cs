using UnityEngine;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // Attach to a GameObject with a UIDocument (Source Asset = ShipUpgrades.uxml) in Main.unity.
    // Shown by SectorTransitionController between sectors, after SectorAchievements; page 3 of 3
    // in the between-sector flow (terminal action: Launch Sector). Permanent Research upgrades
    // (Max Health/Fire Rate/Firepower/Max Fuel/Max Ammo, Magnet, Homing) plus a Resources section
    // (Top Off, Health/Fuel/Ammo conversion -- copied from ShipManagementController's wiring,
    // since the between-sector flow no longer shows that screen at all: its Sector Exploration
    // scans don't apply to a sector that hasn't loaded yet).
    [RequireComponent(typeof(UIDocument))]
    public class ShipUpgradesController : MonoBehaviour
    {
        [SerializeField] private Player _ship;
        [SerializeField] private SectorTransitionController _sectorTransition;

        private VisualElement _root;
        private Label _partsVal;
        private Label _healthVal;
        private Label _fuelVal;
        private Label _ammoVal;
        private Label _attractionVal;
        private Label _homingVal;
        private Label _fireRateVal;
        private Label _healthCapVal;
        private Label _firepowerVal;
        private Label _fuelCapVal;
        private Label _ammoCapVal;
        private Button _backBtn;
        private Button _launchBtn;
        private Button _topOffBtn;
        private Button _convertHealthBtn;
        private Button _convertFuelBtn;
        private Button _convertAmmoBtn;
        private Button _upgradeAttractionBtn;
        private Button _upgradeHomingBtn;
        private Button _upgradeFireRateBtn;
        private Button _upgradeHealthBtn;
        private Button _upgradeFirepowerBtn;
        private Button _upgradeFuelBtn;
        private Button _upgradeAmmoBtn;
        private Button[] _focusOrder;
        private Button[] _leftColumnOrder;
        private Button[] _rightColumnOrder;

        public bool IsShown => _root != null && _root.style.display != DisplayStyle.None;

        // See the identical comment on ShipManagementController/SectorCompleteController for
        // why this toggles visibility via Show()/Hide() instead of GameObject.SetActive().
        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            GamepadUiNavigation.EnsureBound(_root);

            _partsVal = _root.Q<Label>("parts-val");
            _healthVal = _root.Q<Label>("health-val");
            _fuelVal = _root.Q<Label>("fuel-val");
            _ammoVal = _root.Q<Label>("ammo-val");
            _attractionVal = _root.Q<Label>("attraction-val");
            _homingVal = _root.Q<Label>("homing-val");
            _fireRateVal = _root.Q<Label>("firerate-val");
            _healthCapVal = _root.Q<Label>("health-cap-val");
            _firepowerVal = _root.Q<Label>("firepower-val");
            _fuelCapVal = _root.Q<Label>("fuel-cap-val");
            _ammoCapVal = _root.Q<Label>("ammo-cap-val");

            _backBtn = _root.Q<Button>("back-btn");
            _launchBtn = _root.Q<Button>("launch-btn");
            _backBtn?.RegisterCallback<ClickEvent>(_ => _sectorTransition?.BackToAchievements());
            _launchBtn?.RegisterCallback<ClickEvent>(_ => Launch());

            _root.RegisterCallback<NavigationCancelEvent>(_ => _sectorTransition?.BackToAchievements());

            _topOffBtn = _root.Q<Button>("topoff-btn");
            _convertHealthBtn = _root.Q<Button>("convert-health-btn");
            _convertFuelBtn = _root.Q<Button>("convert-fuel-btn");
            _convertAmmoBtn = _root.Q<Button>("convert-ammo-btn");
            _topOffBtn?.RegisterCallback<ClickEvent>(_ => _ship?.TopOff());
            _convertHealthBtn?.RegisterCallback<ClickEvent>(_ => _ship?.ConvertPartsToHealth());
            _convertFuelBtn?.RegisterCallback<ClickEvent>(_ => _ship?.ConvertPartsToFuel());
            _convertAmmoBtn?.RegisterCallback<ClickEvent>(_ => _ship?.ConvertPartsToAmmo());

            _upgradeAttractionBtn = _root.Q<Button>("upgrade-attraction-btn");
            _upgradeHomingBtn = _root.Q<Button>("upgrade-homing-btn");
            _upgradeFireRateBtn = _root.Q<Button>("upgrade-firerate-btn");
            _upgradeHealthBtn = _root.Q<Button>("upgrade-health-btn");
            _upgradeFirepowerBtn = _root.Q<Button>("upgrade-firepower-btn");
            _upgradeFuelBtn = _root.Q<Button>("upgrade-fuel-btn");
            _upgradeAmmoBtn = _root.Q<Button>("upgrade-ammo-btn");

            _focusOrder = new[]
            {
                _backBtn, _launchBtn, _topOffBtn, _convertHealthBtn, _convertFuelBtn, _convertAmmoBtn,
                _upgradeAttractionBtn, _upgradeHomingBtn, _upgradeFireRateBtn, _upgradeHealthBtn,
                _upgradeFirepowerBtn, _upgradeFuelBtn, _upgradeAmmoBtn,
            };

            // The grid is two independent top-to-bottom columns: left (under Back) is Resources
            // then Fire Rate/Firepower; right (under Launch) is Resource Upgrades then Magnet/Homing.
            // Up/Down chains are tracked per column so a button only considers what's actually
            // above/below it in its own column, not the other one.
            _leftColumnOrder = new[]
            {
                _topOffBtn, _convertHealthBtn, _convertFuelBtn, _convertAmmoBtn,
                _upgradeFireRateBtn, _upgradeFirepowerBtn,
            };
            _rightColumnOrder = new[]
            {
                _upgradeHealthBtn, _upgradeFuelBtn, _upgradeAmmoBtn,
                _upgradeAttractionBtn, _upgradeHomingBtn,
            };

            // Pressing Up from Back/Launch should land in the bottom-most enabled row of the
            // column below that button (Back is over the left column, Launch over the right),
            // falling back to the other column if the nearer one has nothing enabled.
            RegisterColumnUpHandler(_backBtn, _leftColumnOrder, _rightColumnOrder);
            RegisterColumnUpHandler(_launchBtn, _rightColumnOrder, _leftColumnOrder);

            // Symmetric to the Up handlers above: pressing Down on the last enabled row of a
            // column has no element below it in its VisualElement to fall through to, so it
            // must explicitly hand focus to Launch.
            RegisterColumnDownHandlers(_leftColumnOrder);
            RegisterColumnDownHandlers(_rightColumnOrder);

            _upgradeAttractionBtn?.RegisterCallback<ClickEvent>(_ => _ship?.UpgradePickupAttraction());
            _upgradeHomingBtn?.RegisterCallback<ClickEvent>(_ => _ship?.UpgradeHoming());
            _upgradeFireRateBtn?.RegisterCallback<ClickEvent>(_ => _ship?.UpgradeFireRate());
            _upgradeHealthBtn?.RegisterCallback<ClickEvent>(_ => _ship?.UpgradeHealth());
            _upgradeFirepowerBtn?.RegisterCallback<ClickEvent>(_ => _ship?.UpgradeFirePower());
            _upgradeFuelBtn?.RegisterCallback<ClickEvent>(_ => _ship?.UpgradeFuel());
            _upgradeAmmoBtn?.RegisterCallback<ClickEvent>(_ => _ship?.UpgradeAmmo());

            if (_ship != null)
            {
                var buyLabel = $"+{Mathf.RoundToInt(_ship.PartsConversionPercent * 100)}% ({_ship.PartsConversionCost})";
                if (_convertHealthBtn != null) _convertHealthBtn.text = buyLabel;
                if (_convertFuelBtn != null) _convertFuelBtn.text = buyLabel;
                if (_convertAmmoBtn != null) _convertAmmoBtn.text = buyLabel;
            }

            _root.style.display = DisplayStyle.None;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            _root.style.display = DisplayStyle.Flex;
            GamepadUiNavigation.EnsureBound(_root);
            _launchBtn?.Focus();
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
        }

        // Replaces the old hand-off to ShipManagementController's "launch" mode -- this is now
        // the terminal action of the between-sector flow.
        private void Launch()
        {
            _ship?.PersistCarryOverStats();
            _sectorTransition?.LaunchNextSector();
        }

        private void Update()
        {
            if (!IsShown || _ship == null) return;

            if (_partsVal != null)
                _partsVal.text = Mathf.RoundToInt(_ship.GetParts()).ToString();

            if (_healthVal != null)
                _healthVal.text = $"{_ship.GetCurrentHealth()}/{_ship.MaxHealth}";

            if (_fuelVal != null)
                _fuelVal.text = $"{Mathf.RoundToInt(_ship.GetFuel())}/{Mathf.RoundToInt(_ship.MaxFuel)}";

            if (_ammoVal != null)
                _ammoVal.text = $"{Mathf.RoundToInt(_ship.GetAmmunition())}/{_ship.MaxPrimaryAmmunition}";

            if (_topOffBtn != null)
                _topOffBtn.text = $"TOP OFF ({_ship.GetTopOffCost()})";

            bool attractionMaxed = _ship.GetPickupAttractionLevel() >= _ship.GetPickupAttractionMaxLevel();
            if (_attractionVal != null)
                _attractionVal.text = $"{_ship.GetPickupAttractionLevel()}/{_ship.GetPickupAttractionMaxLevel()}";
            if (_upgradeAttractionBtn != null)
                _upgradeAttractionBtn.text = attractionMaxed ? "MAX" : $"UPGRADE ({_ship.GetPickupAttractionUpgradeCost()})";

            bool homingMaxed = _ship.GetHomingLevel() >= _ship.GetHomingMaxLevel();
            if (_homingVal != null)
                _homingVal.text = $"{_ship.GetHomingLevel()}/{_ship.GetHomingMaxLevel()}";
            if (_upgradeHomingBtn != null)
                _upgradeHomingBtn.text = homingMaxed ? "MAX" : $"UPGRADE ({_ship.GetHomingUpgradeCost()})";

            if (_fireRateVal != null)
                _fireRateVal.text = $"Lv {_ship.GetFireRateUpgradeLevel()}";
            if (_upgradeFireRateBtn != null)
                _upgradeFireRateBtn.text = $"UPGRADE ({_ship.GetFireRateUpgradeCost()})";

            if (_healthCapVal != null)
                _healthCapVal.text = $"Lv {_ship.GetHealthUpgradeLevel()}";
            if (_upgradeHealthBtn != null)
                _upgradeHealthBtn.text = $"UPGRADE ({_ship.GetHealthUpgradeCost()})";

            if (_firepowerVal != null)
                _firepowerVal.text = $"Lv {_ship.GetFirePowerUpgradeLevel()}";
            if (_upgradeFirepowerBtn != null)
                _upgradeFirepowerBtn.text = $"UPGRADE ({_ship.GetFirePowerUpgradeCost()})";

            if (_fuelCapVal != null)
                _fuelCapVal.text = $"Lv {_ship.GetFuelUpgradeLevel()}";
            if (_upgradeFuelBtn != null)
                _upgradeFuelBtn.text = $"UPGRADE ({_ship.GetFuelUpgradeCost()})";

            if (_ammoCapVal != null)
                _ammoCapVal.text = $"Lv {_ship.GetAmmoUpgradeLevel()}";
            if (_upgradeAmmoBtn != null)
                _upgradeAmmoBtn.text = $"UPGRADE ({_ship.GetAmmoUpgradeCost()})";

            SetEnabledPreservingFocus(_convertHealthBtn, _ship.CanConvertPartsToHealth());
            SetEnabledPreservingFocus(_convertFuelBtn, _ship.CanConvertPartsToFuel());
            SetEnabledPreservingFocus(_convertAmmoBtn, _ship.CanConvertPartsToAmmo());
            SetEnabledPreservingFocus(_topOffBtn, _ship.CanTopOff());
            SetEnabledPreservingFocus(_upgradeAttractionBtn, _ship.CanUpgradePickupAttraction());
            SetEnabledPreservingFocus(_upgradeHomingBtn, _ship.CanUpgradeHoming());
            SetEnabledPreservingFocus(_upgradeFireRateBtn, _ship.CanUpgradeFireRate());
            SetEnabledPreservingFocus(_upgradeHealthBtn, _ship.CanUpgradeHealth());
            SetEnabledPreservingFocus(_upgradeFirepowerBtn, _ship.CanUpgradeFirePower());
            SetEnabledPreservingFocus(_upgradeFuelBtn, _ship.CanUpgradeFuel());
            SetEnabledPreservingFocus(_upgradeAmmoBtn, _ship.CanUpgradeAmmo());
        }

        private void RegisterColumnUpHandler(Button navButton, Button[] nearColumn, Button[] farColumn)
        {
            navButton?.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                if (evt.direction != NavigationMoveEvent.Direction.Up) return;

                if (FocusBottomMostEnabled(nearColumn) || FocusBottomMostEnabled(farColumn))
                {
                    evt.StopPropagation();
                    navButton.focusController.IgnoreEvent(evt);
                }
            });
        }

        private bool FocusBottomMostEnabled(Button[] column)
        {
            for (int i = column.Length - 1; i >= 0; i--)
            {
                if (column[i] == null || !column[i].enabledSelf) continue;

                column[i].Focus();
                return true;
            }

            return false;
        }

        private void RegisterColumnDownHandlers(Button[] column)
        {
            for (int i = 0; i < column.Length; i++)
            {
                int index = i;
                column[i]?.RegisterCallback<NavigationMoveEvent>(evt =>
                {
                    if (evt.direction != NavigationMoveEvent.Direction.Down) return;

                    for (int j = index + 1; j < column.Length; j++)
                    {
                        if (column[j] != null && column[j].enabledSelf) return;
                    }

                    _launchBtn?.Focus();
                    evt.StopPropagation();
                    column[index].focusController.IgnoreEvent(evt);
                });
            }
        }

        private void SetEnabledPreservingFocus(Button button, bool enabled)
        {
            if (button == null) return;

            if (!enabled && button.enabledSelf && button.panel?.focusController?.focusedElement == button)
                FocusPreviousButton(button);

            button.SetEnabled(enabled);
        }

        private void FocusPreviousButton(Button current)
        {
            int index = System.Array.IndexOf(_focusOrder, current);
            for (int i = index - 1; i >= 0; i--)
            {
                if (_focusOrder[i] != null && _focusOrder[i].enabledSelf)
                {
                    _focusOrder[i].Focus();
                    return;
                }
            }

            _launchBtn?.Focus();
        }
    }
}
