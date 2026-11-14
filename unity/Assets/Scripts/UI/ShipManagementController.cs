using UnityEngine;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // Attach to a GameObject with a UIDocument (Source Asset = ShipManagement.uxml) in Main.unity.
    // Mid-sector-only resource-refill + exploration screen: parts count, parts-to-resource
    // conversion rows, a top-off-all button, the portal scan, and a close button. Opened from
    // the Pause Menu (HUD Manage button / gamepad Select) and deliberately does not pause the
    // game -- see PauseMenu.OpenShipManagement(). Not part of the between-sector flow: its
    // Sector Exploration scans don't apply to a sector that hasn't loaded yet, and its resource
    // purchases are duplicated onto ShipUpgradesController for that flow instead. Permanent
    // research upgrades live in ShipUpgradesController.
    [RequireComponent(typeof(UIDocument))]
    public class ShipManagementController : MonoBehaviour
    {
        [SerializeField] private Player _ship;
        [SerializeField] private PauseMenu _pauseMenu;

        private Label _partsVal;
        private Label _healthVal;
        private Label _fuelVal;
        private Label _ammoVal;
        private Label _portalVal;
        private Label _enemyLocatorVal;
        private Label _asteroidLocatorVal;
        private Label _partsLocatorVal;
        private Button _closeBtn;
        private Button _convertHealthBtn;
        private Button _convertFuelBtn;
        private Button _convertAmmoBtn;
        private Button _topOffBtn;
        private Button _scanPortalBtn;
        private Button _scanEnemyLocatorBtn;
        private Button _scanAsteroidLocatorBtn;
        private Button _scanPartsLocatorBtn;
        private Button[] _focusOrder;

        private VisualElement _root;

        public bool IsShown => _root != null && _root.style.display != DisplayStyle.None;

        // This screen toggles visibility via Show()/Hide() (display style) instead of
        // GameObject.SetActive(), unlike Settings/Statistics/Mission. Disabling the GameObject
        // detaches the UIDocument's panel; reactivating it left the close/launch button unable
        // to register clicks on the second open. Binding happens once in OnEnable() -- the
        // GameObject stays active (and the panel attached) for the screen's whole lifetime,
        // the same way PauseMenu's own _menuRoot (never disabled) toggles via display style.
        // OnEnable() rather than Awake(): UIDocument builds rootVisualElement during its own
        // OnEnable, and per-GameObject lifecycle order runs every component's Awake() before
        // any of their OnEnable()s, so reading rootVisualElement from Awake() here could race
        // ahead of UIDocument actually building it.
        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            Bind(_root);
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

        private void Bind(VisualElement root)
        {
            _partsVal = root.Q<Label>("parts-val");
            _healthVal = root.Q<Label>("health-val");
            _fuelVal = root.Q<Label>("fuel-val");
            _ammoVal = root.Q<Label>("ammo-val");
            _portalVal = root.Q<Label>("portal-val");
            _enemyLocatorVal = root.Q<Label>("enemy-locator-val");
            _asteroidLocatorVal = root.Q<Label>("asteroid-locator-val");
            _partsLocatorVal = root.Q<Label>("parts-locator-val");

            _closeBtn = root.Q<Button>("close-btn");
            _closeBtn?.RegisterCallback<ClickEvent>(_ => _pauseMenu?.CloseShipManagement());

            root.RegisterCallback<NavigationCancelEvent>(_ => _pauseMenu?.CloseShipManagement());

            _convertHealthBtn = root.Q<Button>("convert-health-btn");
            _convertFuelBtn = root.Q<Button>("convert-fuel-btn");
            _convertAmmoBtn = root.Q<Button>("convert-ammo-btn");
            _topOffBtn = root.Q<Button>("topoff-btn");
            _scanPortalBtn = root.Q<Button>("scan-portal-btn");
            _scanEnemyLocatorBtn = root.Q<Button>("scan-enemy-locator-btn");
            _scanAsteroidLocatorBtn = root.Q<Button>("scan-asteroid-locator-btn");
            _scanPartsLocatorBtn = root.Q<Button>("scan-parts-locator-btn");

            _focusOrder = new[]
            {
                _closeBtn, _topOffBtn, _convertHealthBtn, _convertFuelBtn, _convertAmmoBtn,
                _scanPortalBtn, _scanEnemyLocatorBtn, _scanAsteroidLocatorBtn, _scanPartsLocatorBtn,
            };

            // Close sits at the top-right of the header, with the Resources column below it
            // on the LEFT and the Exploration column on the RIGHT -- automatic spatial
            // navigation from Close tends to land on Exploration (the nearer column) or get
            // stuck with no valid target at all, leaving Resources unreachable via Down. Always
            // redirect explicitly to the first usable row instead of relying on default nav.
            _closeBtn?.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                if (evt.direction != NavigationMoveEvent.Direction.Down) return;

                FocusFirstUsable();
                evt.StopPropagation();
                _closeBtn.focusController.IgnoreEvent(evt);
            });

            _convertHealthBtn?.RegisterCallback<ClickEvent>(_ => _ship?.ConvertPartsToHealth());
            _convertFuelBtn?.RegisterCallback<ClickEvent>(_ => _ship?.ConvertPartsToFuel());
            _convertAmmoBtn?.RegisterCallback<ClickEvent>(_ => _ship?.ConvertPartsToAmmo());
            _topOffBtn?.RegisterCallback<ClickEvent>(_ => _ship?.TopOff());
            _scanPortalBtn?.RegisterCallback<ClickEvent>(_ => _ship?.ScanForPortal());
            _scanEnemyLocatorBtn?.RegisterCallback<ClickEvent>(_ => _ship?.PurchaseEnemyLocator());
            _scanAsteroidLocatorBtn?.RegisterCallback<ClickEvent>(_ => _ship?.PurchaseAsteroidLocator());
            _scanPartsLocatorBtn?.RegisterCallback<ClickEvent>(_ => _ship?.PurchasePartsLocator());

            if (_ship == null) return;

            var buyLabel = $"+{Mathf.RoundToInt(_ship.PartsConversionPercent * 100)}% ({_ship.PartsConversionCost})";
            if (_convertHealthBtn != null) _convertHealthBtn.text = buyLabel;
            if (_convertFuelBtn != null) _convertFuelBtn.text = buyLabel;
            if (_convertAmmoBtn != null) _convertAmmoBtn.text = buyLabel;
            if (_scanPortalBtn != null) _scanPortalBtn.text = $"SCAN ({_ship.ScanForPortalCost})";
            if (_scanEnemyLocatorBtn != null) _scanEnemyLocatorBtn.text = $"SCAN ({_ship.LocatorScanCost})";
            if (_scanAsteroidLocatorBtn != null) _scanAsteroidLocatorBtn.text = $"SCAN ({_ship.LocatorScanCost})";
            if (_scanPartsLocatorBtn != null) _scanPartsLocatorBtn.text = $"SCAN ({_ship.LocatorScanCost})";
        }

        // Focuses the first enabled row after Close in _focusOrder -- used to deterministically
        // escape Close into the content below it instead of relying on automatic spatial nav.
        private void FocusFirstUsable()
        {
            for (int i = 1; i < _focusOrder.Length; i++)
            {
                if (_focusOrder[i] != null && _focusOrder[i].enabledSelf)
                {
                    _focusOrder[i].Focus();
                    return;
                }
            }
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

            if (_portalVal != null)
                _portalVal.text = _ship.IsPortalDiscovered() ? "DISCOVERED" : "UNKNOWN";

            if (_enemyLocatorVal != null)
                _enemyLocatorVal.text = _ship.IsEnemyLocatorPurchased() ? "ACTIVE" : "LOCKED";

            if (_asteroidLocatorVal != null)
                _asteroidLocatorVal.text = _ship.IsAsteroidLocatorPurchased() ? "ACTIVE" : "LOCKED";

            if (_partsLocatorVal != null)
                _partsLocatorVal.text = _ship.IsPartsLocatorPurchased() ? "ACTIVE" : "LOCKED";

            SetEnabledPreservingFocus(_convertHealthBtn, _ship.CanConvertPartsToHealth());
            SetEnabledPreservingFocus(_convertFuelBtn, _ship.CanConvertPartsToFuel());
            SetEnabledPreservingFocus(_convertAmmoBtn, _ship.CanConvertPartsToAmmo());
            SetEnabledPreservingFocus(_topOffBtn, _ship.CanTopOff());
            SetEnabledPreservingFocus(_scanPortalBtn, _ship.CanScanForPortal());
            SetEnabledPreservingFocus(_scanEnemyLocatorBtn, _ship.CanPurchaseEnemyLocator());
            SetEnabledPreservingFocus(_scanAsteroidLocatorBtn, _ship.CanPurchaseAsteroidLocator());
            SetEnabledPreservingFocus(_scanPartsLocatorBtn, _ship.CanPurchasePartsLocator());
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

            _closeBtn?.Focus();
        }
    }
}
