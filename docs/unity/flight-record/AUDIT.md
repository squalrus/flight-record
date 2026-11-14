# InfinitySpace — Codebase Audit

> Prepared: 2026-06-15  
> Unity version at audit: **6000.3.17f1 (Unity 6)**  
> Last committed Unity version: **2018.3.6f1**

---

## Summary

The project was originally built for Unity 2018.3 and targeting Android. It has been opened in Unity 6 (6000.3.17f1), which auto-upgraded several project settings. The core gameplay loop (ship movement, shooting, pickups, sector spawning) is intact. All critical compile errors, deprecated APIs, and high-priority performance issues from the original audit have been resolved, including the `BackgroundTiler.cs` scrolling fix. All active work items are tracked in [BACKLOG.md](BACKLOG.md).

---

## Unity Version Delta

| | Then (02/2019) | Now (06/2026) |
|---|---|---|
| Unity | 2018.3.6f1 | 6000.3.17f1 (Unity 6) |
| Render Pipeline | Built-in | Built-in (unchanged) |
| Input | Old Input Manager + Standard Assets CrossPlatformInput | Unity Input System (`com.unity.inputsystem` 1.11.0) with on-screen touch controls |
| UI | Legacy `UnityEngine.UI.Text` | TextMeshPro (`TMPro.TextMeshProUGUI`) |
| Scripting Backend | Mono/IL2CPP | Mono/IL2CPP (unchanged) |
| .NET Target | .NET 4.x Equivalent | .NET Standard 2.1 |

---

## Critical Issues (Compile Errors / Runtime Failures)

### 1. ~~Removed Build Targets in CrossPlatformInputInitialize.cs~~

**Fixed:** `Assets/Editor/CrossPlatformInput/` and `Assets/Standard Assets/CrossPlatformInput/` deleted. Input System (`com.unity.inputsystem` 1.11.0) adopted as replacement.

`CrossPlatformInputInitialize.cs` referenced `BuildTarget.PSM`, `BuildTarget.Tizen`, `BuildTargetGroup.PSM`, `BuildTargetGroup.SamsungTV`, `BuildTargetGroup.Tizen`, and `BuildTargetGroup.WSA` — all removed from Unity 6.

### 2. ~~BinaryFormatter Obsolete~~

**Fixed:** `GameControl.cs` now uses `JsonUtility` for save/load.

`BinaryFormatter` was marked `[Obsolete]` in .NET and disabled by default in .NET 5+ AOT contexts (IL2CPP builds). It was also a known deserialization security vulnerability.

---

## High Priority — Deprecated APIs

### 3. ~~`OnGUI()` Health Bar~~

**Fixed:** `Ship.OnGUI()` removed. Replaced with a world-space Canvas `Image` (Filled / Horizontal) child on each ship prefab, driven from `Update()`. `GUIStyleBackground.cs`, `World.cs`, and the entire `Assets/Scripts/Squalrus/` folder deleted.

~~**File:** `Assets/Scripts/Ship.cs` (lines 77–99)~~  
~~**Helper:** `Assets/Scripts/Squalrus/GUI/GUIStyleBackground.cs`~~  
~~**Helper:** `Assets/Scripts/Squalrus/World.cs`~~

~~`OnGUI()` is Unity's legacy immediate-mode GUI, which re-renders every frame and allocates a new `Texture2D` and `GUIStyle` on every call via `GUIStyleBackground.Create()`. This is a memory allocation per-frame that the GC must clean up and is visually limited. Unity 6 still supports it but it is not recommended.~~

~~**Fix:** Replace with a Canvas `Image` component for the health bar. `GUIStyleBackground.cs` and `World.cs` can be deleted after.~~

### 4. ~~Standard Assets CrossPlatformInput~~

**Fixed:** All CrossPlatformInput folders deleted. `PlayerController.cs` migrated to `UnityEngine.InputSystem`.

Unity Standard Assets were officially deprecated and removed from Unity's download assistant. The CrossPlatformInput system was a workaround before the new Input System existed.

### 5. ~~Legacy `UnityEngine.UI.Text`~~

**Fixed:** All `UnityEngine.UI.Text` references in `PlayerController.cs`, `GameControl.cs`, `PauseMenu.cs`, and `VersionDisplay.cs` replaced with `TMPro.TextMeshProUGUI`. Scene and prefab components updated to match.

~~**Files:** `Assets/Scripts/PlayerController.cs`, `Assets/Scripts/GameControl.cs`, `Assets/Scripts/PauseMenu.cs`, `Assets/Scripts/VersionDisplay.cs`~~

~~`UnityEngine.UI.Text` is legacy UI. Unity 6 ships TextMeshPro as the default text solution. Legacy Text components render lower quality and do not support rich text or SDF fonts.~~

~~**Fix:** Replace `Text` references with `TMPro.TextMeshProUGUI`.~~

### 6. ~~`SendMessage` Pattern~~

**Fixed:** All `SendMessage` calls removed. Call sites now use `ICanTakeDamage` interface or direct typed references.

`SendMessage` uses reflection, bypasses compile-time type checking, generates GC allocations, and silently fails if the method name is wrong.

---

## Medium Priority — Performance & Code Quality

### 7. ~~`GetComponent` in Update Loops~~

**Fixed:** All components (`Rigidbody2D`, `Ship`, etc.) cached in `Start()`/`Awake()` in `Asteroid.cs`, `Enemy.cs`, and `PlayerController.cs`.

`GetComponent<T>()` is a relatively expensive call when issued every frame.

### 8. ~~`Resources.Load` at Runtime~~

**Fixed:** Missile and power-up prefabs are now serialized `[SerializeField]` Inspector fields on the `Ship` component. `Resources.Load` calls removed.

Loading assets from `Resources/` at runtime blocks the main thread and makes asset management opaque.

### 9. ~~`GameObject.Find` / `FindWithTag` in Start~~

**Fixed:** `PlayerController.PlayerTransform` static property added (set in `Awake()`). `Enemy` and `BackgroundTiler` reference it directly. `Ship._enemyManager` changed to `[SerializeField]` Inspector field with null-check in `FireWeapon()`.

~~**Files:** `Assets/Scripts/Enemy.cs` (line 19), `Assets/Scripts/BackgroundTiler.cs` (line 21), `Assets/Scripts/Ship.cs` (line 55)~~

~~`GameObject.Find()` and `FindWithTag()` iterate every active object in the scene and are fragile against rename/restructure. They are best reserved for tooling, not production gameplay code.~~

~~**Fix:** Inject dependencies via serialized Inspector fields or a lightweight service locator.~~

### 10. ~~`StartCoroutine` with String Argument~~

**Fixed:** `Ship.cs` now uses `StartCoroutine(FireWeapon())` (method reference, not string).

`StartCoroutine("FireWeapon")` used the string overload, which relies on reflection and cannot be compile-time verified.

### 11. ~~`BackgroundTiler` Does Not Scroll~~

**Fixed:** `BackgroundTiler.cs` now repositions its 3×3 tile pool around the main camera every `LateUpdate()`, snapping to a tile-sized grid anchor so the background scrolls infinitely with the player.

~~**File:** `Assets/Scripts/BackgroundTiler.cs`~~

~~The background is tiled once at `Start()` into a 3×3 grid around the player's spawn position. As the player moves, the background does not follow — the player will fly off the edge of the tiled area. This is an incomplete feature.~~

~~**Fix:** Re-pool or reposition tiles as the player moves, checking distance each frame.~~

### 12. ~~`Camera` Class Name Shadows `UnityEngine.Camera`~~

**Fixed:** `Assets/Scripts/Camera.cs` renamed to `Assets/Scripts/CameraFollow.cs`.

The class `InfinitySpace.Camera` shadowed the engine's `UnityEngine.Camera`, requiring explicit `UnityEngine.Camera.main` qualification across the codebase.

### 13. ~~Random Power-Up Shuffle via `OrderBy(Guid.NewGuid())`~~

**Fixed:** Replaced with `_powerUpPrefabs[Random.Range(0, _powerUpPrefabs.Count)]`. `using System` and `using System.Linq` removed from `Ship.cs`.

~~**File:** `Assets/Scripts/Ship.cs` (line 134)~~

~~Using `OrderBy(x => Guid.NewGuid())` to shuffle a list allocates a `Guid` per element and runs a full sort. It works but is a misuse of `OrderBy`.~~

~~**Fix:** `_powerUpPrefabs[Random.Range(0, _powerUpPrefabs.Count)]` is sufficient and allocation-free.~~

### 14. ~~`PauseMenu` — StatisticsScreen Not Hidden on Unpause~~

**Fixed:** `PauseMenu`, `Statistics`, `Main Menu`, and `Game Over` were converted from Canvas/uGUI to UI Toolkit (UXML/USS) — see [BACKLOG.md](BACKLOG.md) "Main Menu (Start Scene)". `PauseMenu` now toggles a single `UIDocument` root's `display` style and explicitly hides the Statistics/Ship Management screen GameObjects in `TogglePause()`'s unpause path, so there's no longer a list of independently-shown canvases that can be left visible.

~~**File:** `Assets/Scripts/PauseMenu.cs` (lines 15–17)~~

~~When the game is unpaused (`Time.timeScale == 0` branch), `StatisticsScreen.enabled` is set to false, but the pause state only tracks `_timeScaleReference` — not which canvases are active — so if any canvas was shown independently it won't be hidden correctly. Minor but worth a refactor when touching this code.~~

---

## What to Delete

| Path | Reason |
|---|---|
| ~~`Assets/Standard Assets/CrossPlatformInput/`~~ | ~~Deprecated; causes compile errors in Unity 6; replaced by Input System~~ |
| ~~`Assets/Editor/CrossPlatformInput/`~~ | ~~Same — part of old Standard Assets~~ |
| ~~`Assets/MobileDependencyResolver/`~~ | ~~Version 1.2.185 (~2018); superseded by EDM4U~~ |
| ~~`Assets/UnityVS/Editor/`~~ | ~~Old Visual Studio Tools for Unity integration — now a native VS extension~~ |
| ~~`Assets/Scripts/Squalrus/GUI/GUIStyleBackground.cs`~~ | ~~Only supports OnGUI health bar; delete after migrating to Canvas~~ |
| ~~`Assets/Scripts/Squalrus/World.cs`~~ | ~~Screen ratio helper only used by OnGUI health bar; delete after migrating~~ |

---

## Packages to Remove or Replace

| Package | Action | Reason |
|---|---|---|
| ~~`com.unity.xr.legacyinputhelpers`~~ | ~~Remove~~ | ~~XR package; not needed for a 2D mobile shooter~~ |
| ~~`com.unity.ai.navigation`~~ | ~~Remove~~ | ~~NavMesh package; not used in a 2D top-down game~~ |
| ~~`com.unity.multiplayer.center`~~ | ~~Remove~~ | ~~Auto-added by Unity 6 upgrade wizard; not needed~~ |
| ~~`com.unity.analytics` 3.8.2~~ | ~~Replace or remove~~ | ~~Legacy analytics, deprecated~~ |
| ~~`com.unity.inputsystem`~~ | ~~**Add**~~ | ~~New Input System — needed to replace CrossPlatformInput~~ |

Packages to keep: `com.unity.ads`, `com.unity.purchasing`, `com.unity.timeline`, `com.unity.ugui` (or migrate to UI Toolkit), `com.unity.ide.visualstudio`, `com.unity.test-framework`.

---

## Incomplete Features (From GDD vs. Code)

| Feature | Status |
|---|---|
| Main Menu (New Game / Continue) | Implemented (`MainMenuController.cs`, UI Toolkit) — New Game/Continue wired to `LoadLevel.LoadScene()`; Settings/Statistics buttons present but disabled pending their own backlog items |
| Ship selection (3 ships) | Missing |
| Minimap | Not implemented |
| Level progression / Level Types | `Sector.cs` spawns content but no level sequence manager exists |
| Background tiling / infinite space | Implemented — scrolling 3×3 tile pool |
| Statistics persistence (Hit %, Ships destroyed, Powerups collected) | Stubbed with `"?"` in `Statistics.uxml` / `StatisticsController.cs` (moved off `GameControl.Update()` during the UI Toolkit conversion) |
| Fuel consumption from movement | Implemented in `PlayerController.FixedUpdate()` but fuel reaching 0 has no consequence |
| Parts system / Ship upgrades | `_currentParts` tracked but upgrade UI and system not implemented |
| Audio | Not implemented (GDD says "Music TBD") |
| Multiplayer / leaderboard | Not implemented |

---

## File Inventory

```text
Assets/
  Art/                        # Sprites: bg.png, bg_big, bg_fitted, bg_new, pickups
  Design/                     # Affinity Designer source files for pickup icons + screenshots
  Prefabs/
    Player/Player.prefab            # Player ship
    Entities/
      Asteroid.prefab               # Asteroid
      Enemies/Probe.prefab          # Enemy ship variants
      Enemies/ProbeII.prefab
      Missile/MissileL.prefab       # Player missile
      PowerUps/Ammo.prefab          # Ammo power-up
      PowerUps/Fuel.prefab          # Fuel power-up
      Collectable/Part.prefab       # Parts pickup
      Collectable/Relic.prefab      # Relic objective pickup
    World/
      Background.prefab             # Background tile
      Sector.prefab                 # Sector spawner
  Scenes/
    Start.unity               # Title/start screen
    Main.unity                # Core gameplay
    GameOver.unity            # Game over screen
  Scripts/
    Core/
      DestroyableObject.cs      # Abstract base: health, damage, destruct
      ICanTakeDamage.cs         # Interface: AddHealth, Destruct
      TransformHelper.cs        # LookAt2D extension method
    Entities/
      Asteroid.cs               # Asteroid physics behavior
      Enemy.cs                  # Enemy AI: radar, steering, firing
      Missile.cs                # Projectile: TTL, collision damage
      Collectible.cs            # Abstract base: bloom delay, pulse glow, magnet reel
      Part.cs                   # Collectible: transfers parts on pickup
      Relic.cs                  # Collectible: sector Relic objective (Collectible subclass)
      Ship.cs                   # Ship behavior: fuel, ammo, firing, Canvas health bar
    Player/
      PlayerController.cs       # Player input (Unity Input System + PlayerInput component), HUD text
      FloatingJoystick.cs       # On-screen floating joystick (Input System)
      TouchControls.cs          # On-screen touch button bindings (Input System)
    World/
      BackgroundTiler.cs        # Scrolling 3x3 tile pool, repositioned around camera each LateUpdate
      CameraFollow.cs           # Camera follow
      Sector.cs                 # Spawns enemies and asteroids in a sector
    UI/
      LoadLevel.cs              # Scene transition helper (used by GameOver/Start UI)
      MainMenuController.cs     # Start.unity main menu (UI Toolkit): New Game, Continue, Settings/Statistics placeholders
      PauseMenu.cs              # Pause/resume, statistics, ship management, quit (UI Toolkit)
      StatisticsController.cs   # Statistics screen (UI Toolkit): reads GameControl stats
      ShipManagementController.cs # Ship Management screen (UI Toolkit): parts display
      GameOverController.cs     # GameOver.unity screen (UI Toolkit): START OVER button
    Systems/
      GameControl.cs            # Singleton: statistics, save/load (JsonUtility)
      PlayerStatistics.cs       # Serializable stats DTO
    # Squalrus/ — DELETED (v0.3.2): GUIStyleBackground.cs and World.cs removed with OnGUI health bar
  Art/
    Fonts/                    # Custom and open-source fonts (OpenSans, Unipix, Infinity-Icons-HU)
DESIGN_DOCUMENT.md            # Game design document
README.md                     # Project overview and setup guide
```

---

## Recommended Modernization Order

1. ~~**Fix compile errors** — Delete `Assets/Standard Assets/CrossPlatformInput/` and `Assets/Editor/CrossPlatformInput/`.~~
2. ~~**Fix BinaryFormatter** — Replace with `JsonUtility` in `GameControl.cs`.~~
3. ~~**Remove unused packages** — xr.legacyinputhelpers, ai.navigation, multiplayer.center, legacy analytics. Delete MobileDependencyResolver and UnityVS asset folders.~~
4. ~~**Adopt Input System** — Install `com.unity.inputsystem`, create Input Actions asset, rewrite `PlayerController.cs`.~~
5. ~~**Migrate UI Text → TextMeshPro** — `PlayerController.cs` HUD labels and `GameControl.cs` stats text.~~
6. ~~**Replace OnGUI health bar** — Convert to Canvas `Slider` or `Image` fill. Delete `GUIStyleBackground.cs` and `World.cs`.~~
7. ~~**Eliminate SendMessage** — Refactor call sites to use `ICanTakeDamage` interface or direct method references.~~
8. ~~**Cache GetComponent calls** — Asteroid, Enemy, PlayerController.~~
9. ~~**Replace Resources.Load** — Serialize prefab references on Ship component.~~
10. ~~**Fix BackgroundTiler** — Implement scrolling tile pool.~~
