using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace FlightRecord
{
    // Attach to a GameObject with a UIDocument (Source Asset = HUD.uxml) in Main.unity.
    // Draws the radial dials and radar with Painter2D (USS can't do conic gradients /
    // glow), and reads live values from Player each frame.
    [RequireComponent(typeof(UIDocument))]
    public class VectorHudController : MonoBehaviour
    {
        private static readonly Color Cyan = new Color(0.498f, 0.914f, 1f);
        private static readonly Color Green = new Color(0.212f, 1f, 0.620f);
        private static readonly Color Amber = new Color(1f, 0.761f, 0.302f);
        private static readonly Color Hostile = new Color(1f, 0.353f, 0.353f);
        private static readonly Color Rock = new Color(0.435f, 0.737f, 1f);
        private static readonly Color Portal = new Color(0.714f, 0.608f, 1f);
        private static readonly Color PartsBlip = new Color(1f, 0.761f, 0.302f);
        private static readonly Color White = Color.white;
        private static readonly Color Track = new Color(0.498f, 0.914f, 1f, 0.12f);

        [Header("Game References")]
        [SerializeField] private Player _ship;
        [SerializeField] private PauseMenu _pauseMenu;
        [SerializeField] private Sector _sector;

        [Header("Radar")]
        [SerializeField] private float _radarRange = 30f;
        [SerializeField] private float _sweepSpeed = 120f;

        private enum BlipKind { You, Hostile, Rock, Parts, Portal, HostileDirection, RockDirection, PartsDirection, PortalDirection }
        private struct Blip { public BlipKind Kind; public Vector2 Pos; }
        private readonly List<Blip> _blips = new List<Blip>();

        private VisualElement _healthGauge, _ammoGauge, _fuelGauge, _radar;
        private VisualElement _sectorProgressFill;
        private Label _healthVal, _ammoVal, _fuelVal, _partsVal, _sectorLabel, _sectorTimeLabel, _sectorProgressStatus, _magVal, _homVal;
        private Label _hpVal, _dmgVal, _fuelCapVal, _ammoCapVal;
        private Label _pilotAvatar;
        private VisualElement _rofPips;
        private float _sweepAngle;
        private Portal _portal;

        private float _ellipsisTimer;
        private const float ELLIPSIS_CYCLE_INTERVAL = 0.5f;

        private void OnEnable()
        {
            // _sector is no longer wired in the Inspector -- Main.unity's Sector instance
            // is now spawned at runtime by SectorSpawner (different prefab per CurrentSector),
            // so it doesn't exist yet at scene-load time for a static reference to point at.
            if (_sector == null) _sector = FindFirstObjectByType<Sector>();

            var root = GetComponent<UIDocument>().rootVisualElement;

            // The HUD overlay otherwise covers the whole screen and silently absorbs
            // touch input meant for the on-screen joystick/fire button canvases beneath
            // it. Only the ship/pause button should actually intercept clicks.
            SetPickingModeRecursive(root, PickingMode.Ignore);

            _healthGauge = root.Q<VisualElement>("health-gauge");
            _ammoGauge = root.Q<VisualElement>("ammo-gauge");
            _fuelGauge = root.Q<VisualElement>("fuel-gauge");
            _radar = root.Q<VisualElement>("radar");

            _healthVal = root.Q<Label>("health-val");
            _ammoVal = root.Q<Label>("ammo-val");
            _fuelVal = root.Q<Label>("fuel-val");
            _partsVal = root.Q<Label>("parts-val");
            _sectorLabel = root.Q<Label>("sector-name");
            _sectorTimeLabel = root.Q<Label>("sector-time");
            _sectorProgressStatus = root.Q<Label>("sector-progress-status");
            _sectorProgressFill = root.Q<VisualElement>("sector-progress-fill");
            _magVal = root.Q<Label>("mag-val");
            _homVal = root.Q<Label>("hom-val");
            _hpVal = root.Q<Label>("hp-val");
            _dmgVal = root.Q<Label>("dmg-val");
            _fuelCapVal = root.Q<Label>("fuel-cap-val");
            _ammoCapVal = root.Q<Label>("ammo-cap-val");
            _rofPips = root.Q<VisualElement>("rof-pips");
            _pilotAvatar = root.Q<Label>("pilot-avatar");

            if (_healthGauge != null) _healthGauge.generateVisualContent += ctx => DrawDial(ctx, HealthFraction(), Green);
            if (_ammoGauge != null) _ammoGauge.generateVisualContent += ctx => DrawDial(ctx, AmmoFraction(), Cyan);
            if (_fuelGauge != null) _fuelGauge.generateVisualContent += ctx => DrawDial(ctx, FuelFraction(), Amber);
            if (_radar != null) _radar.generateVisualContent += DrawRadar;

            var manageBtn = root.Q<VisualElement>("manage-btn");
            if (manageBtn == null)
                Debug.LogWarning("VectorHudController: 'manage-btn' not found in HUD.uxml — ship management click can't be wired.");
            if (manageBtn != null)
            {
                manageBtn.pickingMode = PickingMode.Position;
                manageBtn.RegisterCallback<ClickEvent>(_ => _pauseMenu?.OpenShipManagement());
            }

            var pauseBtn = root.Q<VisualElement>("pause-btn");
            if (pauseBtn == null)
                Debug.LogWarning("VectorHudController: 'pause-btn' not found in HUD.uxml — pause click can't be wired.");
            if (pauseBtn != null)
            {
                pauseBtn.pickingMode = PickingMode.Position;
                pauseBtn.RegisterCallback<ClickEvent>(_ => _pauseMenu?.TogglePause());
            }

            if (_sectorLabel != null) _sectorLabel.text = BuildSectorLabel();
        }

        private void Update()
        {
            _sweepAngle = (_sweepAngle + _sweepSpeed * Time.deltaTime) % 360f;
            _ellipsisTimer = (_ellipsisTimer + Time.deltaTime) % (ELLIPSIS_CYCLE_INTERVAL * 4f);
            _radar?.MarkDirtyRepaint();

            if (_pilotAvatar != null) _pilotAvatar.text = GameControl.control != null ? GameControl.control.AvatarGlyph : "v";

            if (_ship == null) return;

            RefreshGauges();
            RefreshUpgrades();
            RefreshBlips();
            RefreshSectorProgress();
            RefreshSectorTime();
        }

        private const float TIME_WARNING_THRESHOLD_SECONDS = 60f;

        private void RefreshSectorTime()
        {
            if (_sectorTimeLabel == null || _sector == null) return;
            float remaining = _sector.TimeRemaining;
            int totalSeconds = Mathf.CeilToInt(remaining);
            _sectorTimeLabel.text = $"· {totalSeconds / 60}:{totalSeconds % 60:D2}";
            _sectorTimeLabel.EnableInClassList("sector__time--warning", remaining <= TIME_WARNING_THRESHOLD_SECONDS);
        }

        private void RefreshGauges()
        {
            if (_healthVal != null) _healthVal.text = $"{_ship.GetCurrentHealth()}/{_ship.MaxHealth}";
            if (_ammoVal != null) _ammoVal.text = $"{Mathf.RoundToInt(_ship.GetAmmunition())}/{_ship.MaxPrimaryAmmunition}";
            if (_fuelVal != null) _fuelVal.text = $"{Mathf.RoundToInt(_ship.GetFuel())}/{Mathf.RoundToInt(_ship.MaxFuel)}";
            if (_partsVal != null) _partsVal.text = Mathf.RoundToInt(_ship.GetParts()).ToString("00");

            _healthGauge?.MarkDirtyRepaint();
            _ammoGauge?.MarkDirtyRepaint();
            _fuelGauge?.MarkDirtyRepaint();
        }

        private void RefreshUpgrades()
        {
            if (_magVal != null) _magVal.text = $"Lv {_ship.GetPickupAttractionLevel()}";
            if (_homVal != null) _homVal.text = $"Lv {_ship.GetHomingLevel()}";

            // Health/Firepower/Fuel/Ammo have no level cap, so they're shown as a plain level
            // number rather than pips that could never look "full". Fire Rate keeps the legacy
            // capped pip display instead, since it's the row that slot was originally reserved for.
            if (_hpVal != null) _hpVal.text = $"Lv {_ship.GetHealthUpgradeLevel()}";
            if (_dmgVal != null) _dmgVal.text = $"Lv {_ship.GetFirePowerUpgradeLevel()}";
            if (_fuelCapVal != null) _fuelCapVal.text = $"Lv {_ship.GetFuelUpgradeLevel()}";
            if (_ammoCapVal != null) _ammoCapVal.text = $"Lv {_ship.GetAmmoUpgradeLevel()}";

            SetPipRow(_rofPips, _ship.GetFireRateUpgradeLevel());
        }

        private void RefreshBlips()
        {
            _blips.Clear();
            _blips.Add(new Blip { Kind = BlipKind.You, Pos = Vector2.zero });

            Vector3 origin = _ship.transform.position;

            foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                AddBlip(BlipKind.Hostile, BlipKind.HostileDirection, enemy.transform.position, origin, _ship.IsEnemyLocatorPurchased());

            foreach (var asteroid in FindObjectsByType<Asteroid>(FindObjectsSortMode.None))
                AddBlip(BlipKind.Rock, BlipKind.RockDirection, asteroid.transform.position, origin, _ship.IsAsteroidLocatorPurchased());

            if (_ship.IsPartsLocatorPurchased())
            {
                foreach (var part in FindObjectsByType<Part>(FindObjectsSortMode.None))
                {
                    if (part.TransferrableParts <= 0) continue;
                    AddBlip(BlipKind.Parts, BlipKind.PartsDirection, part.transform.position, origin, true);
                }
            }

            if (_portal != null && _portal.IsDiscovered)
                AddBlip(BlipKind.Portal, BlipKind.PortalDirection, _portal.Position, origin, true);
        }

        private void RefreshSectorProgress()
        {
            if (_portal == null) _portal = FindFirstObjectByType<Portal>();
            if (_sectorProgressFill == null) return;

            if (_portal == null || !_portal.IsDiscovered)
            {
                int dotCount = Mathf.FloorToInt(_ellipsisTimer / ELLIPSIS_CYCLE_INTERVAL);
                string dots = new string('.', dotCount);
                SetSectorProgress(0f, ready: false, $"SCANNING FOR PORTAL{dots}");
                return;
            }

            if (_portal.IsOpen)
            {
                SetSectorProgress(1f, ready: true, "PORTAL OPEN");
                return;
            }

            float frac = _sector != null ? _sector.ObjectiveFraction : 1f;
            string status = _sector != null ? _sector.ObjectiveStatusText : $"PORTAL PROGRESS {Mathf.RoundToInt(frac * 100f)}%";
            SetSectorProgress(frac, ready: false, status);
        }

        private void SetSectorProgress(float frac, bool ready, string status)
        {
            _sectorProgressFill.style.width = Length.Percent(frac * 100f);
            _sectorProgressFill.EnableInClassList("sector-progress__fill--ready", ready);

            if (_sectorProgressStatus == null) return;
            _sectorProgressStatus.text = status;
            _sectorProgressStatus.EnableInClassList("sector-progress__status--ready", ready);
        }

        private static readonly string[] GalaxyNames =
        {
            "KESPRA", "VANTHEA", "ORYSSA", "DRELKIN", "NYXARA", "THALOS", "QUORIN", "SABLETH",
            "MIRAGOS", "ZENDRA", "CALYPHOR", "BRYXIS", "ELUVIAN", "GORATH", "HALIXA", "ISVENA",
            "JOTHRYN", "KAELOS", "LUMENTA", "MORVENE", "NEKRYSS", "OBSIDRA", "PYRELLA", "QUASTOR",
            "RAVELYN", "SOLTHRYN", "TYRESSA", "UMBRELL", "VEXARIA", "WRAITHE", "XYLONIS", "YSANDRE",
            "ZEPHYRA", "ARCANUM", "BORELIS", "CINDRYS", "DUSKARA", "EMBERON", "FALCRYN", "GLIMARA",
        };

        private static readonly string[] GreekLetters =
        {
            "ALPHA", "BETA", "GAMMA", "DELTA", "EPSILON", "ZETA", "ETA", "THETA",
            "IOTA", "KAPPA", "LAMBDA", "MU", "NU", "XI", "OMICRON", "PI",
            "RHO", "SIGMA", "TAU", "UPSILON", "PHI", "CHI", "PSI", "OMEGA",
        };

        private static string BuildSectorLabel()
        {
            int sector = GameControl.control != null ? GameControl.control.CurrentSector : 1;

            // Seeded by sector number so the same sector always shows the same name,
            // but each new sector gets a different galaxy name / Greek letter pairing.
            var rng = new System.Random(sector);
            string galaxy = GalaxyNames[rng.Next(GalaxyNames.Length)];
            string letter = GreekLetters[rng.Next(GreekLetters.Length)];

            return $"SECTOR {RomanNumeral.ToRoman(sector)} · {galaxy} {letter}";
        }

        // In range, draws as a normal blip. Out of range, only draws (pinned to the radar
        // edge as a directional chevron) when showDirectionIfOutOfRange is set -- the Portal
        // arrow this was modeled on always shows once discovered, while Hostile/Rock/Parts
        // arrows are gated behind their respective Locator purchase.
        private void AddBlip(BlipKind inRangeKind, BlipKind directionKind, Vector3 worldPos, Vector3 origin, bool showDirectionIfOutOfRange)
        {
            Vector2 offset = (Vector2)(worldPos - origin) / _radarRange;
            Vector2 pos = new Vector2(offset.x, -offset.y);

            if (pos.sqrMagnitude <= 1f)
            {
                _blips.Add(new Blip { Kind = inRangeKind, Pos = pos });
                return;
            }

            if (showDirectionIfOutOfRange)
                _blips.Add(new Blip { Kind = directionKind, Pos = pos.normalized * 0.92f });
        }

        private float HealthFraction() => _ship.MaxHealth <= 0 ? 0f : (float)_ship.GetCurrentHealth() / _ship.MaxHealth;
        private float AmmoFraction() => _ship.MaxPrimaryAmmunition <= 0 ? 0f : _ship.GetAmmunition() / _ship.MaxPrimaryAmmunition;
        private float FuelFraction() => _ship.MaxFuel <= 0 ? 0f : _ship.GetFuel() / _ship.MaxFuel;

        private static void SetPickingModeRecursive(VisualElement element, PickingMode mode)
        {
            element.pickingMode = mode;
            foreach (var child in element.Children())
                SetPickingModeRecursive(child, mode);
        }

        private static void SetPipRow(VisualElement pipsRow, int level)
        {
            if (pipsRow == null) return;
            int i = 0;
            foreach (var pip in pipsRow.Children())
            {
                bool on = i < level;
                pip.RemoveFromClassList(on ? "pip--off" : "pip--on");
                pip.AddToClassList(on ? "pip--on" : "pip--off");
                i++;
            }
        }

        // ---- Drawing ----
        private void DrawDial(MeshGenerationContext ctx, float frac, Color valueColor)
        {
            var rect = ctx.visualElement.contentRect;
            if (rect.width < 1f) return;
            var p = ctx.painter2D;
            Vector2 c = rect.center;
            float ring = 9f;
            float r = rect.width * 0.5f - ring * 0.5f;

            p.lineWidth = ring;
            p.lineCap = LineCap.Butt;

            p.strokeColor = Track;
            p.BeginPath();
            p.Arc(c, r, 0f, 360f);
            p.Stroke();

            if (frac > 0.001f)
            {
                p.strokeColor = valueColor;
                p.BeginPath();
                p.Arc(c, r, -90f, -90f + 360f * Mathf.Clamp01(frac));
                p.Stroke();
            }
        }

        private void DrawRadar(MeshGenerationContext ctx)
        {
            var rect = ctx.visualElement.contentRect;
            if (rect.width < 1f) return;
            var p = ctx.painter2D;
            Vector2 c = rect.center;
            float R = rect.width * 0.5f;

            p.lineWidth = 1f;
            p.strokeColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f);
            Ring(p, c, R - 1f);
            p.strokeColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.3f);
            Ring(p, c, R * 0.66f);
            p.strokeColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.2f);
            Ring(p, c, R * 0.33f);

            p.strokeColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.26f);
            p.BeginPath(); p.MoveTo(new Vector2(c.x - R, c.y)); p.LineTo(new Vector2(c.x + R, c.y)); p.Stroke();
            p.BeginPath(); p.MoveTo(new Vector2(c.x, c.y - R)); p.LineTo(new Vector2(c.x, c.y + R)); p.Stroke();

            float a = _sweepAngle;
            p.fillColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.12f);
            p.BeginPath();
            p.MoveTo(c);
            p.Arc(c, R - 1f, a - 45f - 90f, a - 90f);
            p.LineTo(c);
            p.Fill();
            Vector2 tip = c + Dir(a - 90f) * (R - 1f);
            p.lineWidth = 1.5f;
            p.strokeColor = Cyan;
            p.BeginPath(); p.MoveTo(c); p.LineTo(tip); p.Stroke();

            foreach (var b in _blips)
            {
                Vector2 bp = new Vector2(c.x + b.Pos.x * (R - 6f), c.y + b.Pos.y * (R - 6f));
                switch (b.Kind)
                {
                    case BlipKind.You: Diamond(p, bp, 4f, White, true); break;
                    case BlipKind.Hostile: Diamond(p, bp, 3.5f, Hostile, true); break;
                    case BlipKind.Rock: Diamond(p, bp, 3.5f, Rock, false); break;
                    case BlipKind.Parts: Diamond(p, bp, 3.5f, PartsBlip, true); break;
                    case BlipKind.Portal:
                        Ring(p, bp, 6f, Portal, 1.5f);
                        Diamond(p, bp, 2.5f, Portal, true);
                        break;
                    case BlipKind.HostileDirection:
                        Chevron(p, bp, b.Pos.normalized, 7f, Hostile);
                        break;
                    case BlipKind.RockDirection:
                        Chevron(p, bp, b.Pos.normalized, 7f, Rock);
                        break;
                    case BlipKind.PartsDirection:
                        Chevron(p, bp, b.Pos.normalized, 7f, PartsBlip);
                        break;
                    case BlipKind.PortalDirection:
                        Chevron(p, bp, b.Pos.normalized, 7f, Portal);
                        break;
                }
            }
        }

        // ---- helpers ----
        private static Vector2 Dir(float deg) { float r = deg * Mathf.Deg2Rad; return new Vector2(Mathf.Cos(r), Mathf.Sin(r)); }

        private static void Ring(Painter2D p, Vector2 c, float r)
        {
            p.BeginPath(); p.Arc(c, r, 0f, 360f); p.Stroke();
        }

        private static void Ring(Painter2D p, Vector2 c, float r, Color col, float lw)
        {
            float prev = p.lineWidth; p.lineWidth = lw; p.strokeColor = col;
            p.BeginPath(); p.Arc(c, r, 0f, 360f); p.Stroke();
            p.lineWidth = prev;
        }

        private static void Chevron(Painter2D p, Vector2 pos, Vector2 outDir, float size, Color col)
        {
            Vector2 perp = new Vector2(-outDir.y, outDir.x) * size * 0.6f;
            Vector2 apex = pos + outDir * size * 0.6f;
            Vector2 baseA = pos - outDir * size * 0.4f + perp;
            Vector2 baseB = pos - outDir * size * 0.4f - perp;

            p.BeginPath();
            p.MoveTo(apex);
            p.LineTo(baseA);
            p.LineTo(baseB);
            p.ClosePath();
            p.fillColor = col;
            p.Fill();
        }

        private static void Diamond(Painter2D p, Vector2 c, float s, Color col, bool filled)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(c.x, c.y - s));
            p.LineTo(new Vector2(c.x + s, c.y));
            p.LineTo(new Vector2(c.x, c.y + s));
            p.LineTo(new Vector2(c.x - s, c.y));
            p.ClosePath();
            if (filled) { p.fillColor = col; p.Fill(); }
            else { p.lineWidth = 1f; p.strokeColor = col; p.Stroke(); }
        }

    }
}
