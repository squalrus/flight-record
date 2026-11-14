using UnityEngine;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // Attach to a GameObject with a UIDocument (Source Asset = ProfileSetup.uxml) in Start.unity.
    // Shown by MainMenuController.ShowProfileSetup() when New Game is pressed, before the Main
    // scene loads. Ship and avatar choices are cosmetic only (icon glyphs from Infinity Icons HU)
    // -- there are no per-ship stat presets yet, see Ship Selection Screen backlog item.
    // Pilot name entry is deferred -- still tracked in BACKLOG.md -- so PlayerName stays at its
    // PlayerStatistics default for now.
    [RequireComponent(typeof(UIDocument))]
    public class ProfileSetupController : MonoBehaviour
    {
        [SerializeField] private MainMenuController _mainMenu;
        [SerializeField] private LoadLevel _loadLevel;

        private static readonly string[] ShipGlyphs = { "R", "N", "J", "X" };
        private static readonly string[] AvatarGlyphs = { "v", "w", "s", "t" };

        private VisualElement _root;
        private Button[] _shipButtons;
        private Button[] _avatarButtons;
        private int _selectedShip;
        private int _selectedAvatar;

        public bool IsShown => _root != null && _root.style.display != DisplayStyle.None;

        // Disabling the GameObject via SetActive detaches the UIDocument's panel and can leave
        // buttons unable to register clicks on a later reopen (the same class of bug fixed for
        // ShipManagementController and the Pause Menu's sub-screens). The GameObject now stays
        // active permanently; Show()/Hide() toggle the root's display style instead, and
        // click/cancel handlers are bound once here rather than re-registered each reopen.
        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;

            _shipButtons = new[]
            {
                _root.Q<Button>("ship-option-0"),
                _root.Q<Button>("ship-option-1"),
                _root.Q<Button>("ship-option-2"),
                _root.Q<Button>("ship-option-3"),
            };
            _avatarButtons = new[]
            {
                _root.Q<Button>("avatar-option-0"),
                _root.Q<Button>("avatar-option-1"),
                _root.Q<Button>("avatar-option-2"),
                _root.Q<Button>("avatar-option-3"),
            };

            for (int i = 0; i < _shipButtons.Length; i++)
            {
                int index = i;
                _shipButtons[i]?.RegisterCallback<ClickEvent>(_ => SelectShip(index));
            }
            for (int i = 0; i < _avatarButtons.Length; i++)
            {
                int index = i;
                _avatarButtons[i]?.RegisterCallback<ClickEvent>(_ => SelectAvatar(index));
            }

            var backBtn = _root.Q<Button>("back-btn");
            backBtn?.RegisterCallback<ClickEvent>(_ => _mainMenu?.CloseProfileSetup());
            _root.RegisterCallback<NavigationCancelEvent>(_ => _mainMenu?.CloseProfileSetup());

            var beginBtn = _root.Q<Button>("begin-btn");
            beginBtn?.RegisterCallback<ClickEvent>(_ => Begin());

            _root.style.display = DisplayStyle.None;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            _root.style.display = DisplayStyle.Flex;
            GamepadUiNavigation.EnsureBound(_root);

            SelectShip(0);
            SelectAvatar(0);

            _root.schedule.Execute(() => _shipButtons[0]?.Focus());
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
        }

        private void SelectShip(int index)
        {
            _selectedShip = index;
            for (int i = 0; i < _shipButtons.Length; i++)
                _shipButtons[i]?.EnableInClassList("option-btn--selected", i == index);
        }

        private void SelectAvatar(int index)
        {
            _selectedAvatar = index;
            for (int i = 0; i < _avatarButtons.Length; i++)
                _avatarButtons[i]?.EnableInClassList("option-btn--selected", i == index);
        }

        private void Begin()
        {
            GameControl.ClearSaveData(ShipGlyphs[_selectedShip], AvatarGlyphs[_selectedAvatar]);
            _loadLevel?.LoadScene();
        }
    }
}
