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
| Currency | None. Everything grows through level-ups. |
| Fleet | Lean squadron: pilots with a class, a level, maneuvers, masteries and instincts, flying ships with a frame and modules. |
| Progression | One track: XP. A level-up offers pilot cards and ship cards side by side. See Phase 4. |

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
| Elite wing | +2 threat. A win opens a module crate: pick one of three modules, each matched to a surviving ship. |
| Repair dock | Every hull is repaired to full on arrival, and the medic treats one scar per visit. |
| Recruit | Two candidates at the squadron's level minus one, already promoted to that level. One can join. |
| Signal | One of six events. Rewards are XP, module crates, repairs, a recruit or a faded scar. Some turn into a fight. |
| Boss | Sector 1: blockade command ship (strike). Sector 2: the convoy escort. Sector 3: the ace wing. |

- **Threat** runs from 1 in the first sector to 9 at the last boss. It feeds the existing `EncounterDifficulty` curve.
- **Enemy wings** draw from base hulls early and refit frames later.
- **Sector transitions:** clearing a sector patches half of each ship's damage. Full repairs are at docks.
- **Losing** an ordinary battle spends the stop and earns nothing. Losing a boss, or losing every pilot, ends the run.

### Squadron (`Pilot`)

- The run starts with three level-1 pilots, one per class. Each knows their class's signature maneuver: Break Turn, U-Turn or Rotating Guns.
- The roster holds up to 5 pilots, and up to 3 fly each battle.
- XP needed per level rises (100, 150, 200, 250, 300), with a cap at level 6.
- Level-ups and the ship are described in Phase 4.
- A pilot who is shot down without ejecting is killed, along with the ship.
- A pilot who ejects is fit to fly the next battle; being shot down costs the ship and risks a scar (Gun Shy or Rattled). There are no wounds. What happens to the ship depends on the result:
  - **Win:** the squadron holds the field and tows the wreck home. Frame and modules are intact, but the hull is down to 1. It can still fly, on full shields, until it is repaired at a dock, by the drones event, or by the half patch between sectors.
  - **Loss or retreat:** the wreck is left behind. The pilot comes home to a new, bare base frame of their class; the refit and every module are gone. Maneuvers, masteries, instincts and level stay. The pilot's next level-up offers the refit frames again.
  - The pause menu's Retreat note names any wrecks a retreat would leave behind.

### Saving

- The run is saved as JSON at `user://ace-star-pilot-run.json` after every change.
- Launching a battle saves a checkpoint and nothing is saved again until the battle ends. Quitting mid-battle (closing the app, or Quit to Title in the pause menu) resumes at the start of that battle, with the same squad, enemy wing and map. Retrying a battle this way is allowed by design.

### Screens

- `Run.tscn` / `RunScreen` shows one page chosen from run state: sector map, squadron, briefing, promotion or crate cards, repair dock, recruit, signal, or run end. A signal's outcome is shown before any crate or level-up it brought.
- Home offers New Run, Continue Run and Quick Battle.

### Removed

The old sector/system/planet campaign and fleet screens were replaced and deleted: campaign map, system view, squad select, flagship deck plan, hangar, pilot career, recruitment, shipyard, memorial, and their resources and debug scenes.

## Phase 4: pilot and ship growth (done)

The rule that keeps pilot and ship apart: **a ship's bonuses are always on; a
pilot's only apply in a situation.** Anything true on turn 1 with no setup is
hardware. Anything you have to fly a certain way to get is a pilot skill.

Both grow from the same place: level-ups. There is no currency. (Phase 4 first
had ships bought with salvage at docks; that was removed to keep the run simple.)

### Ship growth (`ShipUpgrades`, `ShipTypes`)

- **Frames** set the base numbers and the slots. Base frames (Kestrel, Raptor, ZT) have one slot; refit frames keep it and add a second. From level 3, a pilot still in the base frame is offered both refit frames at every level-up until they take one; modules move across.
- **Modules**, one per slot, three choices per slot:

| Slot | Modules |
| --- | --- |
| Engine | Overdrive (+30 max move) · Vector Nozzles (+15° turn) · Retro Thrusters (-40 min move) |
| Guns | Burst Loader (+1 shot per volley) · Targeting Array (+8% accuracy) · Wide Mount (cone 24° → 32°) |
| Shields | Shield Capacitor (+8 shields) · Flux Recycler (+1 regen) · Armor Plating (+8 hull, -15 max move) |

- Modules come from level-up cards and from module crates (elite wins and some signals). A crate offers three modules, each matched to a pilot's ship.
- Fitting a module to a filled slot replaces the old one.

### Level-ups (`RunContent.PromotionCards`, `Masteries`, `Perks`)

A level-up offers three cards. A pilot ready for a refit sees both frames plus
one more card. Otherwise the offer leans toward a new maneuver and a module for
an empty slot, and the rest is drawn from instincts, masteries and module swaps.

- **New frame** (ship): from level 3, see above.
- **Module** (ship): for an empty slot, or a swap for a filled one.
- **New maneuver** from the class pool, up to three.
- **Mastery** of a maneuver the pilot knows. It only matters on turns that maneuver is flown: Snap Turn to 180°, Boost turns 90°, U-Turn and Break Turn lose their cooldown, Lock On +25%, Scramble jams a second enemy, and so on (`Masteries.Describe`).
- **Instinct**, a situational bonus:

| Instinct | When it applies |
| --- | --- |
| Phantom | +15% evasion until first hit each battle. |
| Cool Under Fire | +20% accuracy below half hull. |
| Finisher | +25% damage against enemies below 40% hull. |
| Survivor | +25% eject chance. |
| Tail Gunner | +20% accuracy against a target flying away from you. |
| Long Shot | +50% damage from the outer third of range. |
| Ace | A kill resets all maneuver cooldowns. |
| Trigger Happy | First volley each turn has +2 shots. |
| Steady | +2 shield regen after a turn of normal flight. |
| Wingman | +15% evasion within 250 of a squadmate. |

In battle, the HUD lists the selected pilot's instincts and scars, a trait's
name floats above the ship when it makes a difference (`TraitCallout`), and the
debrief counts how often each one fired.

### Scars

Scars are situational penalties from mishaps. After a battle, each surviving
pilot at risk may pick up one (20% after a win, 40% after a loss). A dock's
medic treats one scar per visit, and waiting out the ion storm fades one.

| Scar | Effect | Risked by |
| --- | --- | --- |
| Hesitant | -20% range until the first hit | Winning without a hit |
| Survivor's Guilt | -15% accuracy and evasion once a squadmate is down | A squadmate dying |
| Gun Shy | Volleys 2 shots shorter on a turn you took hull damage | Being shot down; a brutal first exchange |
| Rattled | -15% evasion below half hull | Being shot down; ending below a quarter hull |
| Tunnel Vision | -15% accuracy when switching targets | Missing most shots |
| Engine Shy | 25% less turning at full throttle | Hitting an asteroid |

Positive traits are never rolled after battles, and recruits carry no random
traits.

### Saving

The save is version 2. Older saves can't be loaded; Home says so and offers a
new run.

## Known issues and next steps

- `EnemyAI` reads the player's queued maneuver when choosing its own, so enemies react to orders the player has not revealed yet. For a simultaneous-turn game this is worth reconsidering, together with difficulty.
- Balance is untested with human play: how many level-ups a run gives against how many cards a pilot wants, free docks, threat per layer, ejection and scar odds, and instinct strength.
- A max-level pilot who loses their ship can't take a refit again, because frames only come from level-ups. Crates can still fill the base frame's slot.
- Next: pick the three starting pilots at the start of a run, each with an instinct, instead of drawing instincts from level-ups.
- More battle maps would add variety; there are currently three regular maps plus the escort corridor.
- An Android export preset and a device test pass are still to do.
