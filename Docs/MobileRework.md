# Mobile Rework

The game is being rebuilt as a portrait, touch-first mobile game. The battle
simulation is kept as it is: simultaneous planning, arc movement, terrain, targeting and threat previews, and the enemy AI. The shell around it is rebuilt.

## Decisions

| Area | Decision |
| --- | --- |
| Orientation | Portrait, one-handed. |
| Campaign | Roguelite runs: 3 sectors of branching stops, about 9 battles, around 45 minutes, playable in pieces. |
| Between battles | Hull damage carries over; repair docks and retreat decisions matter. |
| Between runs | Nothing carries over (pure roguelite). |
| Currency | One: salvage. |
| Fleet | Lean squadron: pilots with a class, a level, maneuvers, upgrades and traits. Level-ups are card choices. |

## Phase 1: battles on a phone (done)

- **Project:** 720×1280 portrait base viewport, `canvas_items` stretch with `expand` aspect, portrait orientation, touch emulation from the mouse, GL Compatibility renderer.
- **Portrait arena:** maps stay authored in landscape. `BattleMapDefinition.ToPortrait()` turns them at load, so the player starts at the bottom.
- **Touch input:**
  - Drag a ghost to steer. The ghost keeps its offset from the finger so it stays visible.
  - Tap a ship or ghost to select it. Tap an enemy to pin the targeting preview.
  - Drag or pinch the map. Double-tap empty space to frame everything.
- **`BattleCameraRig`:** frames the battle each turn, focuses a ship on request, and follows the turn as it executes. It frames inside the band between the HUD bars.
- **`BattleHud`:**
  - Squadron chips, objective, and labelled maneuver buttons with drawn path icons.
  - Undo, Engage, and a pause menu with Retreat.
  - Engage asks for confirmation when any ship is on a course into an asteroid.
- **`ManeuverCatalog`:** one place for maneuver names, colours and descriptions.

## Phase 2 and 3: runs and the lean squadron (done)

### Run structure (`RunState`, `RunContent`)

- A run is three sectors: Orion Spur, Cygnus Reach and Helios Crown.
- Each sector is a generated map: four layers of 2–3 branching stops, then a boss. Routes only lead upward and never cross.
- There is always a repair dock just before the boss.

### Stops

| Stop | What it does |
| --- | --- |
| Skirmish | Destroy the enemy patrol. |
| Strike | Destroy a marked command ship; the rest of the wing can live. |
| Elite wing | +2 threat. Every survivor earns a field upgrade (a card choice). |
| Repair dock | Repairs cost 2 salvage per hull point; treating a wound costs 40. |
| Recruit | Two candidates at the squadron's level minus one, already promoted to that level. |
| Signal | One of six events. Some can turn into a fight. |
| Boss | Sector 1: blockade command ship (strike). Sector 2: the convoy escort. Sector 3: the ace wing. |

- **Threat** runs from 1 in the first sector to 9 at the last boss. It feeds the existing `EncounterDifficulty` curve.
- **Enemy wings** draw from base hulls early and refit frames later.
- **Sector transitions:** clearing a sector repairs every ship and heals every wound.
- **Losing** an ordinary battle spends the stop and earns nothing. Losing a boss, or losing every pilot, ends the run.

### Squadron (`Pilot`)

- The run starts with three level-1 pilots, one per class. Each knows their class's signature maneuver: Break Turn, U-Turn or Rotating Guns.
- The roster holds up to 5 pilots, and up to 3 fly each battle.
- XP needed per level rises (100, 150, 200, 250, 300), with a cap at level 6.
- Each level-up offers three cards: a new class maneuver (up to three known), a ship upgrade, or a trait.
  - From level 3, both refit frames for the pilot's class are offered until one is taken.
  - Upgrades have no hardpoint limits; any pilot can take each once.
- Ejection, wounds (sit out the next stop), permanent death and perks earned in battle are unchanged.
- If nobody is fit to fly, the wounded fly anyway, so a run can never soft-lock.

### Saving

- The run is saved as JSON at `user://ace-star-pilot-run.json` after every change.
- A battle still running when the app closed counts as abandoned: the stop is spent and nothing is earned.

### Screens

- `Run.tscn` / `RunScreen` shows one page chosen from run state: sector map, squadron, briefing, promotion cards, repair dock, recruit, signal, or run end.
- Home offers New Run, Continue Run and Quick Battle.

### Removed

The old sector/system/planet campaign and fleet screens were replaced and deleted: campaign map, system view, squad select, flagship deck plan, hangar, pilot career, recruitment, shipyard, memorial, and their resources and debug scenes.

## Known issues and next steps

- `EnemyAI` reads the player's queued maneuver when choosing its own, so enemies react to orders the player has not revealed yet. For a simultaneous-turn game this is worth reconsidering, together with difficulty.
- Balance is untested with human play: salvage income versus costs, threat per layer, and wound and ejection odds.
- More battle maps would add variety; there are currently three regular maps plus the escort corridor.
- An Android export preset and a device test pass are still to do.
