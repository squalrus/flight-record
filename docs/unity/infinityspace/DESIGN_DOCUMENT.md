# InfinitySpace — Game Design Document

**Genre:** Top-down 2D Survival Space Shooter  
**Platform:** Android (primary); PC Editor  
**Created:** June 2015  
**Revised:** June 2026

---

## Revision History

| Date | Description |
|---|---|
| 06/05/2015 | Initial document |
| 06/15/2026 | Revised and reformatted; reconciled with current implementation |

---

## Overview

Infinity Space is a top-down shooter where the player fights through procedurally populated sectors of hostiles, collecting parts to keep their ship fueled, armed, and upgraded.

The player's ship is dropped into the first sector surrounded by enemies. The goal is to fight their way out, collect the loot, and move on to the next sector. At its core this is a survival game — fuel and ammunition are finite, and managing both is the primary tension. Parts collected from debris can upgrade the ship between runs.

---

## Target Platform

**Primary:** Android (latest OS). Touch input via on-screen joystick and fire button.  
**Secondary:** PC (Unity Editor play mode, gamepad supported).

---

## Visual Style

Top-down 2D. Dark space background with sprite-based ships, missiles, and pickups. Pixel-art aesthetic with a limited color palette.

Icon/UI reference: [Infinity space icons on Behance](https://www.behance.net/gallery/Infinity-space-icons/12721009)

---

## Audio Style

Ambient space atmosphere with synthesized SFX for weapons, explosions, and pickups. Music TBD.

---

## Core Loop

```
Dropped into sector
    → Destroy enemies
    → Collect drops (parts)
    → Clear sector / find exit
    → Upgrade ship (if parts collected)
    → Next sector (harder)
    → Death → Game Over → Restart
```

---

## Scenes

| Scene | Description |
|---|---|
| `Start` | Main menu — New Game, Continue, Settings, Statistics |
| `Main` | Core gameplay — one sector per session currently |
| `GameOver` | End screen — shows session statistics, restart option |

---

## Starting Out

### Main Menu

- **New Game** — choose from 3 starting ships (see Ships), enter sector 1
- **Continue** — resume from last completed sector (save slot persisted)
- **Settings** — audio, control sensitivity
- **Statistics** — career stats (shots fired, hit %, enemies destroyed, power-ups collected)

### Intro / First Sector

Player spawns in sector 1 surrounded by basic enemy probes. No tutorial text — the controls are intuitive enough. Upon clearing the sector the player receives a large drop of parts before advancing.

---

## In-Game HUD

| Element | Location | Status |
|---|---|---|
| Health bar | Above ship | Implemented (Canvas `Image`, fill-based) |
| Ammo counter | UI overlay | Implemented (TextMeshPro) |
| Fuel counter | UI overlay | Implemented (TextMeshPro) |
| Parts counter | UI overlay | Implemented (TextMeshPro) |
| Minimap | Corner overlay | **Not implemented** |
| Pause button | Corner | Implemented |

---

## Controls

### Mobile (Primary)
- **Left stick (on-screen joystick):** Steer and thrust
- **Fire button:** Primary blaster

### Gamepad
- **Left stick:** Steer and thrust
- **A button:** Primary blaster

### Fuel Mechanic
Moving consumes fuel. If the fuel tank empties, movement stops. The player must spend collected parts at the Ship Management screen to refuel.

---

## Ships

Three ships available at game start with different stat distributions:

| Stat | Fighting Class | Balanced Class | Runner Class |
|---|---|---|---|
| Health | High | Medium | Low |
| Speed | Low | Medium | High |
| Fire Power | High | Medium | Low |
| Ammo Capacity | High | Medium | Low |
| Fuel Capacity | Low | Medium | High |

*Ship selection screen not yet implemented. Currently one ship archetype is used.*

---

## Enemies

### Probe (Basic)
Small, fast scout ship. Moves toward the player when within radar range and fires its primary weapon.

| Stat | Value |
|---|---|
| Radar Distance | Configurable per prefab |
| Speed | Configurable per prefab |
| Fire Power | Configured on Ship component |

### Probe II (Variant)
Tougher variant with higher health and fire power. Spawned in later sectors.

*Additional enemy types are planned — see BACKLOG.md.*

### Asteroid
Slow-moving debris. Takes damage from missiles. Drops loot on destruction. Moves randomly each frame.

---

## Power-Ups / Drops

All drops spawn from destroyed enemies and asteroids.

| Drop | Effect |
|---|---|
| Ship Part | Adds to parts counter; spend on ship upgrades or convert to fuel/ammo at Ship Management |

Drop quantity is configured on each `Ship` component via `PartsDrop` (average drop count) and `PartsVariance` (± swing around it).

---

## Ship Upgrades

Parts collected during a run can be spent between sectors to upgrade the ship:

- **Speed** — increase thrust force
- **Rate of Fire** — reduce primary weapon cooldown
- **Health Capacity** — increase `MaxHealth`
- **Ammunition Capacity** — increase `MaxPrimaryAmmunition`
- **Damage** — increase `FirePower`

Parts can also be converted to fuel or ammunition in emergencies.

*Upgrade UI and between-sector shop not yet implemented.*

---

## Sectors & Level Generation

A **Sector** is a bounded play area procedurally populated at scene load by `Sector.cs`. Each sector defines:

- **Probe count and spread** — basic enemies
- **Probe II count and spread** — advanced enemies
- **Asteroid count and spread** — terrain/loot

Sectors chain into **Levels**. A Level is a sequence of sectors with increasing difficulty. Level types (dense enemies, resource-rich asteroid fields, boss sector) are planned.

*Level sequencing and exit triggers not yet implemented.*

---

## Statistics

Tracked per career (persisted to device):

| Stat | Tracked | Displayed |
|---|---|---|
| Shots Fired | Yes | Yes |
| Hit Percentage | No | Stubbed ("?") |
| Power-ups Collected | No | Stubbed ("?") |
| Enemies Destroyed | No | Stubbed ("?") |
| Experience | Yes (field) | No |

---

## Persistence

Save data stored at `Application.persistentDataPath/stats.json` via `JsonUtility`. Only `ShotsFired` and `Experience` are saved.

---

## Multiplayer / Social

Global leaderboard: career statistics viewable as "All Time", "Recent", and "By Version". No real-time multiplayer. Deferred until core loop is complete.

---

## Monetization

- Unity Ads — rewarded video integration planned
- Unity IAP — cosmetic ship skins or one-time unlock planned
- No pay-to-win mechanics

---

## Out of Scope (for initial release)

- Real-time multiplayer
- User-generated sectors
- PC/console ports
- Story/campaign mode
