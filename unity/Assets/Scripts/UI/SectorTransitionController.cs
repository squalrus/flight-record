using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace InfinitySpace
{
    // Attach to a GameObject in Main.unity. Triggered by Portal instead of an immediate
    // AdvanceSector()+LoadScene("Main"): pauses the game and walks through a paginated 3-screen
    // flow -- Sector Complete (stats) -> Sector Achievements -> Ship Upgrades (permanent
    // Research/Magnet/Homing upgrades, plus a Resources section for Top Off/conversions) -- with
    // Back/Next on each screen, before Ship Upgrades' Launch action advances and reloads Main.
    // Ship Management itself is not part of this flow: its Sector Exploration scans don't apply
    // to a sector that hasn't loaded yet, and its resource purchases live on Ship Upgrades
    // instead. PortalTransition is activated for the whole sequence so a starfield streaks past
    // behind all three screens, replacing the otherwise-frozen game world with a sense of flying
    // through space.
    public class SectorTransitionController : MonoBehaviour
    {
        public GameObject SectorAchievementsScreen;
        public GameObject SectorCompleteScreen;
        public GameObject ShipUpgradesScreen;
        public PortalTransitionController PortalTransition;

        private PlayerInput _playerInput;
        private Sector _sector;

        private SectorCompleteController _sectorComplete;
        private SectorAchievementsController _sectorAchievements;
        private ShipUpgradesController _shipUpgrades;

        private void Awake()
        {
            _sectorComplete = SectorCompleteScreen.GetComponent<SectorCompleteController>();
            _sectorAchievements = SectorAchievementsScreen.GetComponent<SectorAchievementsController>();
            _shipUpgrades = ShipUpgradesScreen.GetComponent<ShipUpgradesController>();
        }

        public void BeginTransition(Sector sector)
        {
            _sector = sector;
            _playerInput = FindFirstObjectByType<PlayerInput>();
            if (_playerInput != null) _playerInput.enabled = false;
            Time.timeScale = 0f;

            if (PortalTransition != null) PortalTransition.gameObject.SetActive(true);

            _sectorComplete.Populate(sector);
            _sectorComplete.Show();
        }

        public void ShowAchievements()
        {
            _sectorComplete.Hide();
            _sectorAchievements.Populate(_sector);
            _sectorAchievements.Show();
        }

        public void BackToSectorComplete()
        {
            _sectorAchievements.Hide();
            _sectorComplete.Show();
        }

        public void ShowShipUpgrades()
        {
            _sectorAchievements.Hide();
            _shipUpgrades.Show();
        }

        public void BackToAchievements()
        {
            _shipUpgrades.Hide();
            _sectorAchievements.Show();
        }

        public void LaunchNextSector()
        {
            Time.timeScale = 1f;
            GameControl.control?.AdvanceSector();
            SceneManager.LoadScene("Main");
        }
    }
}
