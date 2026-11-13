using UnityEngine;
using UnityEngine.UIElements;

namespace InfinitySpace
{
    // Attach to a GameObject with a UIDocument (Source Asset = SectorAchievements.uxml) in Main.unity.
    // Shown by SectorTransitionController after the sector-complete stats screen and before
    // Ship Upgrades; page 2 of 3 in the between-sector flow. Lists only the achievements
    // unlocked this run and awards parts for them.
    [RequireComponent(typeof(UIDocument))]
    public class SectorAchievementsController : MonoBehaviour
    {
        [SerializeField] private SectorTransitionController _sectorTransition;

        private VisualElement _root;
        private Button _backBtn;
        private Button _nextBtn;
        private Label _titleLabel;
        private VisualElement _achievementList;

        // Parts are only awarded the first time this sector's achievements are populated --
        // Back/Next means Populate() can run again for the same sector if the player goes
        // Back here from Ship Upgrades and forward again, and re-awarding would double-pay.
        private bool _awarded;

        // See the identical comment on ShipManagementController/SectorCompleteController for
        // why this toggles visibility via Show()/Hide() instead of GameObject.SetActive().
        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            GamepadUiNavigation.EnsureBound(_root);

            _titleLabel = _root.Q<Label>("title-label");
            _achievementList = _root.Q<VisualElement>("achievement-list");

            _backBtn = _root.Q<Button>("back-btn");
            _nextBtn = _root.Q<Button>("next-btn");
            _backBtn?.RegisterCallback<ClickEvent>(_ => _sectorTransition?.BackToSectorComplete());
            _nextBtn?.RegisterCallback<ClickEvent>(_ => _sectorTransition?.ShowShipUpgrades());

            _root.RegisterCallback<NavigationCancelEvent>(_ => _sectorTransition?.BackToSectorComplete());

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
                _titleLabel.text = $"SECTOR {GameControl.control.CurrentSector} - ACHIEVEMENTS";

            if (_achievementList == null) return;
            _achievementList.Clear();

            foreach (var achievement in SectorAchievements.All)
            {
                bool unlocked = achievement.IsUnlocked(sector);
                if (unlocked && !_awarded) Award(achievement.RewardParts);
                if (!unlocked) continue;

                var row = new VisualElement();
                row.AddToClassList("achievement-row");

                var top = new VisualElement();
                top.AddToClassList("achievement-row__top");
                var title = new Label(achievement.Title);
                title.AddToClassList("achievement-row__title");
                var reward = new Label($"+{achievement.RewardParts} PARTS");
                reward.AddToClassList("achievement-row__reward");
                top.Add(title);
                top.Add(reward);

                var desc = new Label(achievement.Description);
                desc.AddToClassList("achievement-row__desc");

                row.Add(top);
                row.Add(desc);
                _achievementList.Add(row);
            }

            _awarded = true;
        }

        // Mirrors Player's private Parts setter (Parts -> LifetimePartsEarned) since the ship
        // instance isn't guaranteed reachable here during the paused portal transition.
        private void Award(int parts)
        {
            if (GameControl.control == null) return;
            GameControl.control.Parts += parts;
            GameControl.control.LifetimePartsEarned += parts;
        }
    }
}
