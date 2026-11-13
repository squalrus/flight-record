using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace InfinitySpace
{
    // Attach to a GameObject with a UIDocument (Source Asset = GameOver.uxml) in GameOver.unity.
    // Two screens in sequence: the sector the player died in ("Mission Failed", from
    // GameControl.LastDeath's sector-scoped fields), then a run summary across every sector
    // played this run (the same snapshot's run-scoped fields). GameControl.TransferStatsAndClearRun()
    // already ran and saved to disk back in Player.Destruct(), before this scene even loaded --
    // that's deliberately kept immediate/crash-safe. This controller only gates when the
    // Main Menu button becomes available, not when the save itself happens.
    [RequireComponent(typeof(UIDocument))]
    public class GameOverController : MonoBehaviour
    {
        private VisualElement _missionFailedScreen;
        private VisualElement _runSummaryScreen;
        private bool _showingRunSummary;

        private void OnEnable()
        {
            // Player.RumbleGamepad() is a coroutine on the player ship's GameObject -- dying
            // destroys that GameObject mid-WaitForSeconds, before the coroutine ever reaches
            // its own SetMotorSpeeds(0f, 0f) cleanup line, so the controller keeps buzzing
            // straight through the scene change unless something else stops it here.
            Gamepad.current?.SetMotorSpeeds(0f, 0f);

            var root = GetComponent<UIDocument>().rootVisualElement;
            GamepadUiNavigation.EnsureBound(root);

            _missionFailedScreen = root.Q<VisualElement>("mission-failed-screen");
            _runSummaryScreen = root.Q<VisualElement>("run-summary-screen");

            var sectorContinueBtn = root.Q<Button>("sector-continue-btn");
            sectorContinueBtn?.RegisterCallback<ClickEvent>(_ => ShowRunSummary());
            sectorContinueBtn?.Focus();

            var mainMenuBtn = root.Q<Button>("main-menu-btn");
            mainMenuBtn?.RegisterCallback<ClickEvent>(_ => SceneManager.LoadScene("Start"));

            root.RegisterCallback<NavigationCancelEvent>(_ =>
            {
                if (_showingRunSummary)
                    SceneManager.LoadScene("Start");
                else
                    ShowRunSummary();
            });

            ShowMissionFailed();
        }

        private void ShowMissionFailed()
        {
            var death = GameControl.control != null ? GameControl.control.LastDeath : null;
            if (death == null) return;

            var subtitle = _missionFailedScreen?.Q<Label>("mission-failed-subtitle");
            if (subtitle != null)
            {
                var mission = death.FailureReason switch
                {
                    "RELIC_DESTROYED" => "RELIC LOST",
                    "PORTAL_TIMEOUT" => "PORTAL TIMED OUT",
                    _ => death.MissionText,
                };
                var missionSuffix = string.IsNullOrEmpty(mission) ? "" : $" — {mission}";
                subtitle.text = $"SECTOR {RomanNumeral.ToRoman(death.SectorNumber)}{missionSuffix}";
            }

            SetLabel("sector-enemies-val", $"{death.EnemiesDestroyed}/{death.TotalEnemies}");
            SetLabel("sector-asteroids-val", $"{death.AsteroidsDestroyed}/{death.TotalAsteroids}");
            SetLabel("sector-shots-val", death.ShotsFired.ToString());
            SetLabel("sector-accuracy-val", $"{Mathf.RoundToInt(death.HitPercentage * 100f)}%");
            SetLabel("sector-damage-dealt-val", death.DamageDealt.ToString());
            SetLabel("sector-damage-taken-val", death.DamageTaken.ToString());
            SetLabel("sector-fuel-val", Mathf.RoundToInt(death.FuelConsumed).ToString());
            SetLabel("sector-parts-earned-val", Mathf.RoundToInt(death.PartsEarned).ToString());
            SetLabel("sector-parts-spent-val", Mathf.RoundToInt(death.PartsSpent).ToString());

            int sectorSeconds = Mathf.RoundToInt(death.ElapsedSeconds);
            SetLabel("sector-time-val", $"{sectorSeconds / 60}:{sectorSeconds % 60:D2}");
        }

        private void ShowRunSummary()
        {
            var death = GameControl.control != null ? GameControl.control.LastDeath : null;
            if (death != null)
            {
                SetLabel("run-summary-subtitle", $"{death.RunSectorsCleared} SECTOR{(death.RunSectorsCleared == 1 ? "" : "S")} CLEARED");
                SetLabel("run-enemies-val", death.RunEnemiesDestroyed.ToString());
                SetLabel("run-asteroids-val", death.RunAsteroidsDestroyed.ToString());
                SetLabel("run-shots-val", death.RunShotsFired.ToString());
                SetLabel("run-accuracy-val", $"{Mathf.RoundToInt(death.RunHitPercentage * 100f)}%");
                SetLabel("run-damage-dealt-val", death.RunDamageDealt.ToString());
                SetLabel("run-damage-taken-val", death.RunDamageTaken.ToString());
                SetLabel("run-fuel-val", Mathf.RoundToInt(death.RunFuelConsumed).ToString());
                SetLabel("run-parts-earned-val", Mathf.RoundToInt(death.RunPartsEarned).ToString());
                SetLabel("run-parts-spent-val", Mathf.RoundToInt(death.RunPartsSpent).ToString());
                SetLabel("run-playtime-val", PlaytimeFormat.ToClock(death.RunPlaytimeSeconds));
            }

            _showingRunSummary = true;
            if (_missionFailedScreen != null) _missionFailedScreen.style.display = DisplayStyle.None;
            if (_runSummaryScreen != null) _runSummaryScreen.style.display = DisplayStyle.Flex;

            _runSummaryScreen?.Q<Button>("main-menu-btn")?.Focus();
        }

        private void SetLabel(string name, string text)
        {
            var label = GetComponent<UIDocument>().rootVisualElement.Q<Label>(name);
            if (label != null) label.text = text;
        }
    }
}
