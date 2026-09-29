# Mobile Rework

The game is being rebuilt as a portrait, touch-first mobile game. The battle
simulation (simultaneous planning, arc movement, terrain, targeting and threat
previews, enemy AI) is kept as it is. The shell around it is rebuilt.

## Decisions

| Area | Decision |
| --- | --- |
| Orientation | Portrait, one-handed. |
| Campaign | Roguelite runs replace the sector / system / planet campaign. |
| Fleet | Lean squadron: pilots with a class, a level and perks; level-up choices happen in the debrief. |
| Order of work | Battle controls first, then the run structure, then the squadron. |

## Phase 1: battles on a phone (done)

- **Project:** 720×1280 portrait base viewport, `canvas_items` stretch with `expand` aspect, portrait orientation, touch emulation from the mouse, GL Compatibility renderer. The desktop window is phone-shaped for testing.
- **Portrait arena:** maps stay authored in landscape. `BattleMapDefinition.ToPortrait()` turns them at load, so the player starts at the bottom and enemies at the top.
- **Touch input** (`BattleManager`):
  - Drag a ghost to steer. The ghost keeps its offset from the finger so it stays visible.
  - Tap a ship or ghost to select it. Tap an enemy to pin the targeting preview.
  - Drag or pinch the map to look around. Double-tap empty space to frame the whole battle.
  - Hit areas are sized in screen pixels, so they stay finger-sized at any zoom.
- **Camera** (`BattleCameraRig`):
  - Frames the whole battle at the start of each turn.
  - Tapping a squadron chip focuses that ship's planning area.
  - Follows the action while the turn executes.
  - Always frames inside the band between the HUD bars.
- **HUD** (`BattleHud`):
  - Top bar: turn, squadron chips and objective.
  - Bottom bar: the selected ship, labelled maneuver buttons with drawn path icons, Undo and Engage.
  - A pause menu with Retreat and a controls reference.
  - Engage is always available; ships without orders hold course. If any ship is on a course into an asteroid, the first tap only warns and a second tap confirms.
- **Maneuver vocabulary** (`ManeuverCatalog`): one place for names, colours and descriptions. Fixed turns (U-turn, break turn, snap turn, dodge) are one button each; the side is chosen by dragging the ghost or by tapping the button again.
- **Readability:** in-world labels, HP bars and line widths keep a constant on-screen size at any zoom. `SignalUi` has a mobile type scale (22 px minimum for text players must read), 96 px touch targets and safe-area insets.
- **Screens:** Home, Quick Battle setup and the debrief are rebuilt for portrait. `TouchScroll` / `TapCard` make lists scroll by dragging without triggering taps.

The old campaign screens (`CampaignMap`, `CampaignSystem`, `SelectScreen`, `Flagship`, `Hangar`, `PilotCareer`, `Recruitment`, `Shipyard`, `Memorial`) still use the 1152×648 landscape layout. They are unreachable from Home until Phase 2 replaces them.

## Phase 2: roguelite runs (next)

To be designed and agreed before building: run length, node types, rewards, and what carries over between runs.

## Phase 3: lean squadron

- One squadron screen replaces the flagship deck plan, hangar, recruitment, shipyard and memorial.
- Level-ups offer a choice of cards in the debrief.

## Known issues found during the survey

- The Orion Spur escort mission (`orion_escort.tres`) has no `Source` line, so it loads as a planetary operation. As a result the system can never be captured, and the mission pays 250 credits on every replay. It goes away with the old campaign in Phase 2.
- `EnemyAI` reads the player's queued maneuver when choosing its own, so enemies react to orders the player has not revealed yet.
