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
| Progression | Each pilot's ship and instinct are picked at the start and never change. XP is the only growth: a level-up offers pilot cards and ship cards side by side. See Phase 4. |

## Phase 1: battles on a phone (done)

- **Project:** 720×1280 portrait base viewport, `canvas_items` stretch with `expand` aspect, portrait orientation, touch emulation from the mouse, GL Compatibility renderer.
- **Portrait arena:** maps stay authored in landscape. `BattleMapDefinition.ToPortrait()` turns them at load, so the player starts at the bottom.
- **Touch input:**
  - Drag a ghost to steer. The ghost keeps its offset from the finger so it stays visible.
  - Tap a ship or ghost to select it. Tap an enemy to pin the targeting preview.
  - Drag or pinch the map. Double-tap empty space to frame everything.
- **`BattleCameraRig`:** frames the battle each turn, focuses a ship on request, and follows the turn as it executes. It frames inside the band between the HUD bars.
- **Camera modes** (pause menu toggle, saved in `user://settings.cfg` by `GameSettings`). Planning is framed the same way in both; they differ while a turn plays:
  - Overview (the default): follows every ship.
  - Action: follows the ships trading fire. A ship counts from its last shot, hit or crash for 1.2 execution seconds, so a ship that just died stays in shot. Before anyone fires, it frames your squadron and any enemy closing on it. It zooms in further (up to 1.6× against 1.1×), leaves a smaller margin, and eases more gently between framings.
- **`BattleHud`:**
  - Squadron chips, objective, and labelled maneuver buttons with drawn path icons.
  - Undo, Engage, and a pause menu with Retreat.
  - Engage asks for confirmation when any ship is on a course into an asteroid.
- **`ManeuverCatalog`:** one place for maneuver names, colours and descriptions.
- **Maps (`BattleMaps`):**
  - Ten battlefields plus the escort corridor. Runs pick any battlefield at random for each stop.
  - Shard Run, Cobalt Veil, Broken Ring: the originals, with the squadrons starting closer and Cobalt Veil's gas about 30% smaller.
  - Open Drift (a few rocks as cover), Rubble Belt (a rock wall with two gaps), Crossing (the enemy wing crosses ahead of you), Monolith (one huge central rock), Shallows (small gas pockets at the edges), Gravel Field (many small rocks) and Knife Fight (a close start with rocks ahead).
  - The squadrons start about 850–950 apart (Knife Fight 700), so first contact comes on turn 2 rather than turn 3 or 4.
- **Nebulae:**
  - A ship that starts its turn inside gas flies every move 25% shorter that turn. It is tagged under its bars, and its path, ghost and reach fan already show the shorter route.
  - Shots fired through gas have ×0.7 accuracy.
  - Movement is never slowed partway through a move, so every ship ends its turn exactly where its preview showed. The earlier mid-move slowdown left ghosts and targeting previews wrong.
- **Battle visuals** (art only; collision, ranges and damage are unchanged):
  - Ships are drawn at 1.6× (the escort transport at 1.15×), so their art roughly fills their collision circle. `Fighter.VisualRadius` places bars, rings, labels and callouts around the larger art.
  - Enemy hulls are repainted in one hostile red by a brightness-ramp shader (`ShipPaint.Enemy`, colour `ShipPaint.EnemyHull`). Your ships sit on a faint cyan glow.
  - Names, bars and status tags fade out while a turn plays. Floating damage numbers take their place: blue for shield, gold for hull, and hits close together add into one number.
  - Shots use the packs' pixel bolts (Nairan for you, Kla'ed for the enemy), glowing in the team colour with a short tracer, plus muzzle flashes and sparks (orange on hull, blue on shields, grey on rock). A kill adds a debris burst, a shock ring and a small camera shake. The ship's own shield animation still plays when its shield takes a hit.
  - `AsteroidSprite`: each rock is generated from its terrain circle as pixel art. It has a lumpy outline between 96% and 108% of the collision radius, craters and banded lighting from the top-left. The rock turns slowly while the light stays fixed.
  - `NebulaCloud`: nebulas are drifting, banded gas drawn by a shader. While planning, a dashed ring marks the exact edge that decides who is inside.
  - `SpaceBackdrop.ForBattle`: a per-map colour wash, three pixel-star layers that drift with the camera at different speeds, and a dimmed, slowly turning planet at the screen edge (`MapLook` per map). The planet's sheet frames cross-fade (`AnimatedCelestial`), so the spin glides instead of stepping a sheet pixel at a time. The 12000px star sheets in `CelestialBodies` are skipped, because they exceed many phone GPUs' texture size.
- **Menu visuals:**
  - Display font: Chakra Petch SemiBold (SIL Open Font License, `Assets/Fonts`). `SignalUi.Tracked` builds on it, so every tracked uppercase label, title, button and tag uses it, and so do the battle's drawn labels and damage numbers (`SignalUi.Display`). Running sentences stay in the default font.
  - `SpaceBackdrop.ForMenu`: the battle sky drifts slowly behind Home, Quick Battle and every run page. Each sector has its own planet and tint (ice, then ocean, then fire); Home's planet sits inside its orbit rings.
  - `EventScene`: each event page shows an animated viewscreen in the space between the story and the choices. It shows a gutted Nautolan freighter with arcing sparks, a pod pinging its beacon, a repair swarm welding a hull, a munitions cache, an ion storm with lightning, or raiders circling a convoy.
  - Ship pictures on cards are cropped to the hull (`FighterSkin.Icon`), so the ship fills its box instead of sitting small in the middle of a 64px sheet. Enemy pictures on briefings use the enemy red.

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
| Elite wing | +2 threat. Ace pilots, who quietly read your orders (see Phase 5). A win opens a module crate: pick one of three modules, each matched to a surviving ship. |
| Repair dock | Every hull is repaired to full on arrival, and the medic treats one scar per visit. |
| Recruit | Two candidates at the squadron's level minus one, each with a random ship and instinct, already promoted to that level. One can join. |
| Signal | One of six events. Rewards are XP, module crates, repairs, a recruit or a faded scar. Some turn into a fight. |
| Boss | Sector 1: blockade command ship (strike). Sector 2: the convoy escort. Sector 3: the ace wing. |

- **Threat** runs from 1 in the first sector to 10 at a late sector 3 elite. It is shown on the map and briefing as a difficulty label.
- **Enemy wings** grow in numbers, not stats: see Phase 5.
- **Sector transitions:** clearing a sector patches half of each ship's damage. Full repairs are at docks.
- **Losing** an ordinary battle spends the stop and earns nothing. Losing a boss, or losing every pilot, ends the run.

### Squadron (`Pilot`)

- **Draft:** a run starts by setting up three level-1 pilots (`RunDraftPage`). Each picks a Kestrel, Raptor or ZT, and one of three instincts offered to them (the three pilots' offers never overlap). Both stay with the pilot for the whole run. Each pilot knows their class's signature maneuver: Break Turn, U-Turn or Turret.
- The roster holds up to 5 pilots, and up to 3 fly each battle.
- XP needed per level rises (100, 150, 200, 250, 300), with a cap at level 6.
- Level-ups and the ship are described in Phase 4.
- A pilot who is shot down without ejecting is killed, along with the ship.
- A pilot who ejects and is found comes home with the wreck: frame and modules intact, hull down to 1. It can still fly, on full shields, until it is repaired at a dock, by the drones event, or by the half patch between sectors. Being shot down also risks a scar (Gun Shy or Rattled).
- A win finds every ejected pilot. A loss or retreat gives each a 50% chance; the pause menu's Retreat note names who is at risk.

### Saving

- The run is saved as JSON at `user://ace-star-pilot-run.json` after every change.
- Launching a battle saves a checkpoint and nothing is saved again until the battle ends. Quitting mid-battle (closing the app, or Quit to Title in the pause menu) resumes at the start of that battle, with the same squad, enemy wing and map. Retrying a battle this way is allowed by design.

### Screens

- `Run.tscn` / `RunScreen` shows one page chosen from run state: the starting draft (and its ship picker), sector map, squadron, briefing, promotion or crate cards, repair dock, recruit, signal, or run end. A signal's outcome is shown before any crate or level-up it brought.
- **Pilot page** (`RunPilotPage`): tap a pilot on the squadron page, the pilot at the top of a level-up, or STATS on a recruit. It shows the ship's numbers with modules fitted (and what each module adds), every fitted module, maneuvers and masteries (including what an unmastered one would gain), the instinct, scars and the pilot's record. It opens over the current page and Back returns to it. The numbers come from `ShipStats`, the same frame-plus-modules calculation the fighter uses in battle.
- Home offers New Run, Continue Run and Quick Battle.

### Removed

The old sector/system/planet campaign and fleet screens were replaced and deleted: campaign map, system view, squad select, flagship deck plan, hangar, pilot career, recruitment, shipyard, memorial, and their resources and debug scenes.

## Phase 4: pilot and ship growth (done)

The rule that keeps pilot and ship apart: **a ship's bonuses are always on; a
pilot's only apply in a situation.** Anything true on turn 1 with no setup is
hardware. Anything you have to fly a certain way to get is a pilot skill.

The ship type and the instinct are picked when a pilot joins and never change.
Everything else grows from level-ups; there is no currency. (Phase 4 first had
ships bought with salvage and refit frames earned at level 3; both were removed
to keep the run simple. Hull refits came back in Phase 5 as automatic Mk
upgrades at levels 2, 4 and 6.)

### Ship growth (`ShipUpgrades`, `ShipTypes`)

- **The line decides how a ship flies and what its pilot can learn; the frame decides how it fights.** Every frame can fit any module, and every frame in a line has the line's handling. For now each line has one frame:

| Line | Turn | Speed | Signature maneuver | Learns one of |
| --- | --- | --- | --- | --- |
| Kestrel | 120° | 170–235 | Break Turn | Pursuit Burn, Sensor Scramble, Evasive Spin |
| Raptor | 110° | 145–210 | U-Turn | Engine Boost, Evasive Dodge, Hunter Lock |
| ZT | 70° | 105–165 | Turret | Suppression Fire, Emergency Thrusters, Rear Guns |

- **Evasive Spin** (Kestrel) is steered like normal flight, at half the throttle range and the normal turn limit. The ship barrel-rolls through the move with +55% evasion, and its guns stay silent all turn. On a Kestrel that is 90% evasion, near the 95% cap, because it can't fire. It can be used every third turn. Mastered, it flies the full throttle range.
- **Rear Guns** (ZT) is steered like normal flight, with the guns facing astern for the turn at half damage. The ghost's firing cone points backward. Mastered, the rear guns hit for full damage.
- Rear Guns can be used every other turn. The AI never flies either, and enemies can't learn them, since enemies only fly their line's first two maneuvers.

- **Frames.** Measured by firepower (damage × accuracy) and toughness ((hull + shield) ÷ (1 − evasion), since a hit lands at accuracy × (1 − evasion)):

| Frame | Hull | Shield | Regen | Dmg | Acc | Eva | Firepower | Toughness |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| S1 Kestrel | 200 | 100 | 20 | 35 | 85% | 35% | 29.8 | 460 |
| Raptor | 340 | 170 | 17 | 50 | 88% | 28% | 44.0 | 710 |
| ZT Class | 500 | 250 | 25 | 45 | 82% | 15% | 36.9 | 880 |

- The lines are not balanced against each other on paper: the Kestrel's firepower × toughness is about two fifths of the Raptor's or ZT's. It relies on speed, turning, Evasive Spin and its quicker shield regen.
- **Shelved frames.** Each line also had an attack frame (S4 Striker, R3 Black Hawk, ZT-6) and a guard frame (S9 Ghost, R5 Falcon, ZT-8 Bulwark) that moved about a fifth of the balanced frame's firepower into toughness or back. They are out of play until they are redesigned; their definitions stay in `ShipTypes`, and nobody, player or enemy, flies them.

- **Modules** stack: a ship can carry any number, each module once. There are nine, three for each ship system:

| System | Modules |
| --- | --- |
| Engine | Overdrive (+30 max move) · Vector Nozzles (+15° turn) · Retro Thrusters (-40 min move) |
| Guns | Burst Loader (+1 shot per volley) · Targeting Array (+8% accuracy) · Wide Mount (cone 24° → 32°) |
| Shields | Shield Capacitor (+35% shields) · Flux Recycler (+50% regen) · Armor Plating (+30% hull, -15 max move) |

- Shield-system modules are percentages, so they matter as much on a ZT as on a Kestrel.
- Modules come from level-up cards and from module crates (elite wins and some signals). A crate offers three modules, each matched to a pilot's ship.
- Nothing is ever replaced: every module you take is added. (Ships used to have one engine, guns and shields slot each, and a new module replaced the old one in its slot.)

### Level-ups (`RunContent.PromotionCards`, `Masteries`, `Perks`)

While a pilot knows only their signature maneuver, a level-up offers every
maneuver their line can teach (three cards, "CHOOSE A MANEUVER TO LEARN"), so
the second maneuver is picked from the whole list. After that, a level-up
offers three cards: modules and masteries, one of each while there are any.
Frames and instincts never appear.

- **Module** (ship): any module the ship doesn't already carry. It is added to the ones fitted.
- **New maneuver** from the class pool, up to two: the class's signature maneuver and one more, chosen from all three the line offers on the first level-up. Once a pilot knows two, level-ups stop offering maneuvers and offer modules and masteries instead. The ZT line's **Suppression Fire** is always on: each hit takes 8° (16° mastered) off the target's normal-flight turning, felt in full on its next turn. Under continued fire, older suppression halves each turn; a turn without being suppressed clears it. It never takes a ship below 25°, and maneuvers keep their own angles. Enemy ZT-line ships have it too. A suppressed ship shows "SUPPRESSED −X°" under its bars, and the HUD hint says so.
- **Mastery** of a maneuver the pilot knows. It only matters on turns that maneuver is flown: Boost turns 90°, U-Turn and Break Turn lose their cooldown, Lock On +25%, Scramble jams a second enemy, Evasive Spin flies at full throttle, Rear Guns hit for full damage, and so on (`Masteries.Describe`).


Each pilot has exactly one **instinct**, picked in the draft (recruits bring a
random one). An instinct should shape how that pilot flies or whom they shoot,
matter in most battles, and never be a flat number (that is the ship's job):

| Instinct | When it applies | What it asks of you |
| --- | --- | --- |
| Tail Gunner | +20% accuracy against a target flying away from you. | Get behind targets. |
| Long Shot | +35% damage from the outer third of range. | Hold at the edge of range. |
| Brawler | +2 shots on volleys from the inner third of range. | Close in. |
| Finisher | +25% damage against enemies below 40% hull. | Pick off the wounded. |
| Ace | A kill resets all maneuver cooldowns and refills shields. | Chase kills. |
| Steady | +20 shield regen after a turn of normal flight. | Fly plain turns instead of maneuvers. |
| Stalker | +20% evasion each turn until you open fire. | Hold fire, or fire late in the turn. |
| Daredevil | +15% evasion on turns flown at full throttle or faster. | Fly flat out. |
| Cool Under Fire | +20% accuracy below half hull. | Keep flying hurt; always on in a 1-hull wreck. |
| Second Chance | Once per battle, a shot that would destroy you leaves you at 1 hull, and no shot can finish you for the rest of that turn. | Take the risky pass, then plan a way out. |

Stalker, Brawler and Second Chance replaced Phantom, Trigger Happy and Survivor,
and Wingman was removed (old saves load Wingman as Daredevil); Daredevil is new.
The draft deals nine of the ten instincts, three to each pilot.

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

Instincts are never rolled after battles or learned later.

### Saving

The save is version 3. Older saves can't be loaded; Home says so and offers a
new run. A run saved during the draft resumes on the draft page.

## Phase 5: progression by numbers

The first win-rate batch (960 autopilot battles) showed that enemy ship count
decided fights far more than anything else, that a +% stat bonus on the enemy
outgrew pilot levels, and that damage values of 3–6 made every percentage
bump lumpy. So enemies now scale by count and pilots by refits.

- **Numbers ×10.** Hull, shields, regen and damage are ten times what they were (a Kestrel has 200 hull and 30 damage), as are rock scrapes (up to 140), the ion storm (60) and Steady (+20 regen). Percentage changes now land smoothly. The save version is 4; older runs can't be continued.
- **Hull refits (`Refits`).** At levels 2, 4 and 6 a pilot's frame becomes Mk II, III and IV. Each refit raises hull, shields, regen and damage by 20% (compounding, so Mk IV is +73%) and adds 2% accuracy and 1% evasion. Refits are automatic; the level-up card is still chosen as before. The Mk shows on every pilot card, the level-up page announces it, and the pilot page lists it above the numbers.
- **Enemies fly at base numbers.** The old encounter roll (a +0–52% stat bonus traded against ship count) is gone. Each stop brings a set wing, and some bring reinforcements:

| Stop | Sector 1 | Sector 2 | Sector 3 |
| --- | --- | --- | --- |
| Skirmish / strike, early | 2 | 3 | 4 |
| Skirmish / strike, late | 2–3 | 3 + 1 | 4 + 1 |
| Elite | 3 | 3 + 1 | 4 + 1 |
| Boss | 3 + 1 | 4 + 2 mid-escort | 4 + 2 |

- **Hull mix and maneuvers (composition).** Sector 1 wings are mostly Kestrels; later wings draw evenly from the three lines (elites and bosses use the next sector's mix). A strike's marked target is a ZT. (Sector 2 used to add attack frames and sector 3 to fly only attack and guard frames; those are shelved.) Enemies fly none of their line's maneuvers in sector 1. From sector 2 on they fly two, the same cap as your pilots: the line's signature maneuver and the next in its pool (Break Turn and Pursuit Burn, U-Turn and Boost, Turret and Suppression Fire). They weigh those maneuvers alongside normal flight when lining up a shot, not only to escape rocks.
- **Reinforcements.** A "+N" wave arrives at the start of turn 3 at the enemy start positions furthest from your ships. If the first group is wiped out before then, the wave arrives at once instead of the battle ending. The escort's wave still arrives partway along the corridor. Briefings show the wave faded as INBOUND, and the map shows "3 HOSTILES +1".
- **Quick battle** pits the max-level (Mk IV) squadron against a late sector 3 patrol (4 + 1) instead of three basic ships.
- **Who sees your orders.** Enemies plan when you press Engage. Most can't see your orders: they lead each of your ships along its visible course, straight on at the throttle it started the turn with (`Fighter.TurnStartPathDistance`). Only elite wings, the Ace Interceptors, read the move you actually queued (`BattleMission.EnemiesReadOrders`). The player is told only that they are aces: the briefing calls them "an ace wing, sharper than any patrol" and the battle opens with "ACE PILOTS · SHARPER THAN ANY PATROL". The escort transport's course is always visible to enemies. Measured over 480 autopilot battles: 46% overall, the same as when nobody reads orders and up from 38% when everyone did. Elite stops sit 6–13 points below their blind rates, about where they were when every enemy read orders.
- **Maneuver pools reworked.** Snap Turn, Ghost Run and ECM Jink are gone, Hunter Lock moved from the Kestrel to the Raptor, the Kestrel gained Evasive Spin and the ZT Rear Guns (see Phase 4). Each line now picks its second maneuver from three, all offered together on the first level-up (it used to be one random maneuver card per level-up, so a given maneuver could go unseen for whole runs). Enemy Kestrels fly Pursuit Burn where they flew Snap Turn. Not yet measured in a win-rate batch.
- **Attack and guard frames shelved.** Players and enemies fly only the S1 Kestrel, Raptor and ZT Class (see Phase 4). Not yet measured in a win-rate batch.
- **Balanced frames retuned.** The Kestrel hits for 35 (was 30) and the ZT for 45 (was 50). The Raptor and ZT regenerate a tenth of their shields a turn, 17 and 25 (both were 20); the Kestrel keeps 20, a fifth of its shields, to make up for its thin hull. Enemies fly the same frames, so enemy Kestrels hit harder and enemy ZTs softer. In a straight exchange of fire a Kestrel now needs about 2.3× the shots a Raptor needs to kill it (was 2.6×) and 2.4× against a ZT (was 3.1×), while the Raptor and ZT are about even (a ZT needs 19 shots to kill a Raptor, a Raptor 20 to kill a ZT). Not yet measured in a win-rate batch.
- **Two maneuvers, stacking modules.** Pilots learn one maneuver beyond their signature one, and every module taken is added rather than swapped into a slot (see Phase 4). Over the same 480 battles, with level-up cards taken at random: ships carry 2.8 modules at level 6 (was 1.9) and 1.9 maneuvers (was 2.5). Win rate is 49% overall (was 46%): level 1 and 3 squadrons are unchanged (18%, 39%), level 5 wins 61% (was 57%) and level 6 78% (was 70%). Per-stop changes are within the noise of 10–20 battles a cell. A player who takes modules on purpose will stack more than random picks do.

**Measured.** In the same 960-battle batch as before, the enemy AI flies both sides, neither side sees the other's orders, and each stop is fought at the pilot level a run reaches there:

| Stop | Before | After |
| --- | --- | --- |
| Sector 1 skirmishes (L1–3) | 70–80% | 75–80% |
| Sector 1 elite / boss (L3) | 30% / 50% | 50% / 40% |
| Sector 2 skirmishes (L3–5) | 40–50% | 70–80% |
| Sector 2 elite (L5) | 50% | 30% |
| Sector 2 boss, escort (L5) | 0% | 0% |
| Sector 3 skirmishes (L6) | 10% | 50–80% |
| Sector 3 elite / boss (L6) | 10% / 0% | 50% / 20% |

Level now matters: across all stops a level 1 squadron wins 21% and a level 6 one 73%, against 28% and 41% before. Cells are 10 battles each, so ±30%.

## Known issues and next steps

- Only elite wings read your orders now; everyone else plans against your visible course. Whether bosses (the sector 3 ace wing especially) should read orders too is open.
- The escort boss still loses every autopilot battle: the transport dies in about five turns while the squadron is mostly intact. It needs its own look (transport toughness, attacker focus, wave timing), ideally with human play.
- Sector 2 elites (3 + 1) and the sector 3 boss (4 + 2) sat below the 50% and 40% targets; worth a second pass once humans have played the new curve.
- Balance is untested with human play: line against line (is Kestrel handling worth its paper weakness?), how many modules a ship ends a run with now that they stack, free docks, threat per layer, ejection and scar odds, and instinct strength.
- Maps could be drawn from pools by stop type (open maps for skirmishes, dense ones for elites) and mirrored left to right for more variety.
- An Android export preset and a device test pass are still to do. The device pass should check the nebula and asteroid shaders' frame rate on a low-end phone.
- Try both camera modes in real play and pick the default. Overview stays the default until then.
