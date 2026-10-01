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
  - Ten battlefields. Runs pick any battlefield at random for each stop.
  - Shard Run, Cobalt Veil, Broken Ring: the originals, with the squadrons starting closer and Cobalt Veil's gas about 30% smaller.
  - Open Drift (a few rocks as cover), Rubble Belt (a rock wall with two gaps), Crossing (the enemy wing crosses ahead of you), Monolith (one huge central rock), Shallows (small gas pockets at the edges), Gravel Field (many small rocks) and Knife Fight (a close start with rocks ahead).
  - The squadrons start about 850–950 apart (Knife Fight 700), so first contact comes on turn 2 rather than turn 3 or 4.
- **Nebulae:**
  - A ship that starts its turn inside gas flies every move 25% shorter that turn. It is tagged under its bars, and its path, ghost and reach fan already show the shorter route.
  - Shots fired through gas have ×0.7 accuracy.
  - Movement is never slowed partway through a move, so every ship ends its turn exactly where its preview showed. The earlier mid-move slowdown left ghosts and targeting previews wrong.
- **Battle visuals** (art only; collision, ranges and damage are unchanged):
  - Ships are drawn at 1.6×, so their art roughly fills their collision circle. `Fighter.VisualRadius` places bars, rings, labels and callouts around the larger art.
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
| Ace pilot (elite) | +2 threat. An enemy ace flies with the wing (see Aces in Phase 5). A win opens a module crate: pick one of three modules, each matched to a surviving ship. |
| Repair dock | Every hull is repaired to full on arrival, and the medic treats one scar per visit. |
| Recruit | Two candidates at the squadron's level minus one, each with a random ship and instinct, already promoted to that level. One can join. |
| Signal | One of six events. Rewards are XP, module crates, repairs, a recruit or a faded scar. Some turn into a fight. |
| Boss | Sector 1: the blockade wing. Sector 2: the convoy raiders. Sector 3: the Helios gate guard. One ace flies with each boss, two with the last. |

- **Every battle is won by destroying every enemy ship**, reinforcements included. Strike stops (destroy a marked command ship) and the escort boss (bring a transport to a jump zone) were removed, along with the escort's corridor map; other mission types may come later.

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
| Kestrel | 120° | 170–235 | Break Turn | Pursuit Burn, Sensor Scramble, Evasive Spin, Air Brake, Chaff Screen |
| Raptor | 110° | 145–210 | U-Turn | Engine Boost, Evasive Dodge, Hunter Lock, Sideslip, Alpha Strike |
| ZT | 70° | 105–165 | Turret | Suppression Fire, Emergency Thrusters, Rear Guns, Tractor Beam |

- **Evasive Spin** (Kestrel) is steered like normal flight, at half the throttle range and the normal turn limit. The ship barrel-rolls through the move with +55% evasion, and its guns stay silent all turn. On a Kestrel that is 90% evasion, near the 95% cap, because it can't fire. It can be used every third turn. Mastered, it flies the full throttle range.
- **Rear Guns** (ZT) is steered like normal flight, with the guns facing astern for the turn at half damage. The ghost's firing cone points backward. Mastered, the rear guns hit for full damage.
- **Air Brake** (Kestrel) is steered like normal flight but crawls only 40–80, with the normal turn limit, so pursuers overshoot. Every other turn; mastered, no cooldown.
- **Chaff Screen** (Kestrel) is armed on top of the turn's move (the button shows ARMED). When the turn starts, the ship drops a 90-wide chaff cloud where it is; shots through it lose 30% accuracy, as through a nebula, whichever side fires them. It lasts 2 turns (3 mastered) and comes back every third turn.
- **Sideslip** (Raptor) slides 120 in a straight line, up to 90° off the nose, without turning the nose: drag the ghost to aim it. Every other turn; mastered, it slides 180.
- **Alpha Strike** (Raptor) is armed on top of the turn's move: +2 shots in every volley that turn (+3 mastered), then the guns are offline for the next turn ("GUNS OFFLINE" under the bars), so it comes round every other turn.
- **Tractor Beam** (ZT) is used by tapping an enemy within 280: it is dragged 80 toward the ZT (140 mastered), stopping short of the ZT and of rocks, and can't fly any maneuver but normal flight that turn ("TRACTORED" under its bars). Every third turn.
- Rear Guns can be used every other turn. The AI never flies any of these, and enemies can't learn them, since enemies only fly their line's first two maneuvers.

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
maneuver their line can teach (four or five cards, "CHOOSE A MANEUVER TO
LEARN"), so the second maneuver is picked from the whole list. After that, a level-up
offers three cards: modules and masteries, one of each while there are any.
Frames and instincts never appear.

- **Module** (ship): any module the ship doesn't already carry. It is added to the ones fitted.
- **New maneuver** from the class pool, up to two: the class's signature maneuver and one more, chosen from everything the line offers on the first level-up. Once a pilot knows two, level-ups stop offering maneuvers and offer modules and masteries instead. The ZT line's **Suppression Fire** is always on: each hit takes 8° (16° mastered) off the target's normal-flight turning, felt in full on its next turn. Under continued fire, older suppression halves each turn; a turn without being suppressed clears it. It never takes a ship below 25°, and maneuvers keep their own angles. Enemy ZT-line ships have it too. A suppressed ship shows "SUPPRESSED −X°" under its bars, and the HUD hint says so.
- **Mastery** of a maneuver the pilot knows. It only matters on turns that maneuver is flown: Pursuit Burn turns 90° (45° unmastered), Boost 45° (20°), U-Turn and Break Turn lose their cooldown, Lock On +25%, Scramble jams a second enemy, Evasive Spin flies at full throttle, Rear Guns hit for full damage, Air Brake loses its cooldown, chaff lasts a turn longer, Sideslip slides 180, Alpha Strike adds 3 shots, Tractor Beam drags 140, and so on (`Masteries.Describe`).


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
| Skirmish, early | 2 | 3 | 4 |
| Skirmish, late | 2–3 | 3 + 1 | 4 + 1 |
| Elite | 3 | 3 + 1 | 4 + 1 |
| Boss | 3 + 1 | 4 + 2 | 4 + 2 |

- **Hull mix and maneuvers (composition).** Sector 1 wings are mostly Kestrels; later wings draw evenly from the three lines (elites and bosses use the next sector's mix). (Sector 2 used to add attack frames and sector 3 to fly only attack and guard frames; those are shelved.) Enemies fly none of their line's maneuvers in sector 1. From sector 2 on they fly two, the same cap as your pilots: the line's signature maneuver and the next in its pool (Break Turn and Pursuit Burn, U-Turn and Boost, Turret and Suppression Fire). They weigh those maneuvers alongside normal flight when lining up a shot, not only to escape rocks.
- **Reinforcements.** A "+N" wave arrives at the start of turn 3 at the enemy start positions furthest from your ships. If the first group is wiped out before then, the wave arrives at once instead of the battle ending. Briefings show the wave faded as INBOUND, and the map shows "3 HOSTILES +1".
- **Quick battle** pits the max-level (Mk IV) squadron against a late sector 3 patrol (4 + 1) instead of three basic ships.
- **Who sees your orders.** Enemies plan when you press Engage. Most can't see your orders: they lead each of your ships along its visible course, straight on at the throttle it started the turn with (`Fighter.TurnStartPathDistance`). Only aces read the move you actually queued. (Before aces were individuals, every ship in an elite wing read orders. Measured over 480 autopilot battles then: 46% overall, the same as when nobody reads orders and up from 38% when everyone did.)
- **One mission type.** Strike stops and the sector 2 escort boss are gone; every battle is eliminate-all-hostiles. Strike stops became skirmishes, the sector 1 boss is a plain 3 + 1 fight and the sector 2 boss a 4 + 2 one whose wave arrives on turn 3. (Enemies then shot the nearest of your ships; see Phase 6 for how they pick targets now.)
- **Aces (`Aces`).** An ace is one enemy pilot who makes a fight harder, and the biggest threat on the field. One flies with every elite stop (now called "ACE PILOT" on the map, titled with the ace's callsign) and every sector boss, two with the sector 3 boss. Aces take the place of a ship in the opening wing, so wing sizes are unchanged.
  - Hull: refitted one step ahead of your squadron (Mk II in sector 1, Mk III in sector 2, Mk IV in sector 3), plus +10% accuracy and +10% evasion. A sector 2 ace ZT has 720 hull and 65 damage against a plain ZT's 500 and 45.
  - Flying: its line's first two maneuvers from sector 1 on, both mastered, and it plans against the orders you actually gave. Every other enemy plans against your visible course.
  - Look: a black hull with gold highlights, a gold "ACE VEX" label over it, "ENEMY ACE · VEX" when the battle opens and "ACE DOWN · VEX" when it dies. The briefing shows its icon in the same paint with its callsign, and the map adds "· ACE" to the stop.
  - Winning an elite stop still opens a module crate ("ACE DEFEATED").
- **Measured: aces and one mission type** (480 battles; before the five new maneuvers, with the level-up that always teaches a maneuver at level 2): 47% overall (was 52%). Level 1 23% (was 18%), level 3 34% (was 50%), level 5 56% (was 64%), level 6 73% (was 77%). Across all levels, the stops with aces fell: sector 1 elite 50% (was 62%), sector 1 boss 30% (was 68%, when it only took the marked command ship), sector 2 elite 28% (was 40%), sector 3 boss with two Mk IV aces 0% (was 15%). The sector 2 boss rose to 15% (was 2% as an escort). Level 3 squadrons also carry fewer modules now (0.6 a ship, was 0.9), since level 2 always teaches a maneuver.
- **Five new maneuvers.** Air Brake and Chaff Screen for the Kestrel, Sideslip and Alpha Strike for the Raptor, Tractor Beam for the ZT (see Phase 4). Pilots still learn one maneuver beyond their signature one; the level-up now lists every option the line has. Unmeasured: the autopilot doesn't fly them.
- **Maneuver pools reworked.** Snap Turn, Ghost Run and ECM Jink are gone, Hunter Lock moved from the Kestrel to the Raptor, the Kestrel gained Evasive Spin and the ZT Rear Guns (see Phase 4). Each line now picks its second maneuver from three, all offered together on the first level-up (it used to be one random maneuver card per level-up, so a given maneuver could go unseen for whole runs). Enemy Kestrels fly Pursuit Burn where they flew Snap Turn.
- **Burn turn angles swapped.** Pursuit Burn (Kestrel, 370) turns up to 45° and Engine Boost (Raptor, 350) up to 20°; they were 20° and 45°. Engine Boost can now be used every other turn, like Pursuit Burn (it was every third). Their masteries swapped too: Pursuit Burn 90°, Boost 45°. The Kestrel's chase burn can now follow a target off its nose, and the Raptor's boost is the straight-line one.
- **Attack and guard frames shelved.** Players and enemies fly only the S1 Kestrel, Raptor and ZT Class (see Phase 4).
- **Balanced frames retuned.** The Kestrel hits for 35 (was 30) and the ZT for 45 (was 50). The Raptor and ZT regenerate a tenth of their shields a turn, 17 and 25 (both were 20); the Kestrel keeps 20, a fifth of its shields, to make up for its thin hull. Enemies fly the same frames, so enemy Kestrels hit harder and enemy ZTs softer. In a straight exchange of fire a Kestrel now needs about 2.3× the shots a Raptor needs to kill it (was 2.6×) and 2.4× against a ZT (was 3.1×), while the Raptor and ZT are about even (a ZT needs 19 shots to kill a Raptor, a Raptor 20 to kill a ZT).
- **Measured together** (480 battles, before ECM Jink was removed and the level-up offer changed; the autopilot never flies Evasive Spin or Rear Guns): 52% overall (was 49%). Level 1 18% (same), level 3 50% (was 39%), level 5 64% (was 61%), level 6 77% (was 78%). Sector 3 got easier now that its wings fly balanced frames instead of attack and guard ones: across all levels, its skirmishes win 45–55% (were 35–45%). The sector 2 escort boss is still near 0% (2%).
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

## Phase 6: fights that don't all start the same

Every fight used to open the same way: both wings spawned about 820 apart,
nose to nose, closing about 370 a turn, so everyone reached gun range (280)
together on turn 2 and jousted. The enemy pilot had one rule (point at the
nearest of your ships and end 170 from it), so its whole wing arrived as a
wall, focused one ship, flew through, and wheeled back. Measured over 40
autopilot battles: first contact on turn 2 in 29 of them, 96% of the
contact turn's volleys hit the target's nose, half of all damage landed in
those two turns, and 15 of 40 battles lost a ship there.

### Enemy tactics (`EnemyAI`, `EnemyTactic`)

Every enemy flies a tactic. Nothing on screen names it (role labels over
the ships were tried and dropped); each line has its own habits to learn.

| Tactic | Who | How it flies |
| --- | --- | --- |
| Striker | Raptors first, then every second Kestrel | Goes straight for its target and takes an even trade of fire. |
| Flanker | Kestrels first, then every second Raptor | Swings out to its side of the target (420 off its course) while closing, and holds off to the side, at least 320 away, while the target faces it. It turns in once the target looks away, or after two turns without firing. Flankers alternate sides. |
| Gunship | Every ZT | A slow, tough gun platform. It wades into the middle of the fight (likes to end about 150 from its target), barely minds your guns, counts any of your ships in its guns as good as its target, and goes after whichever of your ships is nearest its wing, so anyone chasing its wingmates flies into its fire. |
| Ace | Aces | Hunts your most worn-down ship, minds your guns, and reads your orders. |

- **Gunship replaced Sniper.** ZTs first flew as snipers that kept about 250 off and backed away from anyone closing. Over the same 40 sector 2 battles (level 1 squad flown as strikers), gunships fire about 75% more volleys than snipers did (268 against 153), fight closer (a median 239 from your nearest ship, was 264) and stay nearer their wing (228, was 256), and 40% of their first-exchange volleys hit a ship's side (snipers 18%). Wins and fight length are about the same (9 and 7 wins of 40; median 12 and 13 turns).

- **Guns along the route.** Each candidate route is scored on how much of the move the pilot's guns cover a foe and how much a foe's guns cover it (sampled at five points), weighted by the foe's firepower, plus where the move leaves it against its target: nose on, at its preferred range, and, for flankers and aces especially, off the target's nose.
- **What they expect you to do.** Non-ace pilots still can't see your orders. They now expect each of your ships to turn toward the nearest of their wing, as far as it can; straight ahead along your visible course still counts for half. Aces see the move you actually queued.
- **Spread targets.** Pilots pick the nearest of your ships, pulled toward worn-down ones and pushed off ships their allies are already on, and keep last turn's target unless another is clearly better. A wing no longer pours everything into one ship on the first pass.
- **Patience.** A pilot that minds your guns could dodge forever. Each turn a pilot goes without firing it minds your guns less (30% a turn, down to a quarter) and cares less about getting on your tail, and a wing outnumbered two to one presses in. Below 35% hull a pilot is more careful.
- **Measured** (40 sector 1 skirmishes, your squad flown by a striker autopilot): fights last a median 8 turns, as before. With the old charge autopilot flying your squad they ran a median 13.5 turns, because the new pilots read its turns perfectly and dodged it; a person is far less predictable. Hit chance still ignores angle, so when your ship charges an enemy, the first exchange is still mostly nose to nose (about 75% of volleys). Making angle matter is the next lever.

### Openings (`BattleOpenings`)

Each battle stop picks one of seven openings, named in the briefing (in
orange under the objective) and called out at the start of the battle.

| Opening | Start |
| --- | --- |
| HEAD-ON | The map's own starts, nose to nose. |
| LONG APPROACH | The enemy wing starts 330 further off (about 1,150 away), so the approach is yours to shape. |
| FLANKED | The enemy cuts in across your path from the left or right, about 600 off your front quarter. |
| PINCER | The enemy splits between your two front quarters and closes from both. |
| RUNNING FIGHT | Both wings fly the same way, side by side and about 550 apart, with the enemy on your left or right. |
| BOUNCED | The enemy starts about 500–700 behind you, heading your way. |
| AMBUSH | Head-on, but the reinforcements arrive 720 behind your squadron ("REINFORCEMENTS · 2 HOSTILES BEHIND YOU"). |

- **Picking.** A run's first fight is head-on or a long approach. After that the opening is drawn by weight: long approach, flanked, pincer and running fight 2 each, head-on 1.5, bounced 1 (from a sector's third row, or sector 2 on), ambush 1.5 (only when the stop has reinforcements). One-sided openings come from either side.
- **Starts are placed in arena coordinates** and moved to the nearest spot clear of rocks (75 clear) and gas, and at least 110 from any other ship. The map's own starts are used as authored.
- **Reinforcements** come in at the enemy's starting spots furthest from your ships, the map's top spots when the wing started elsewhere, or behind you in an ambush.
- **Measured** (10 battles each, sector 1, your squad flown by a striker autopilot): wins sit at 90–100% for every opening except the ambush. An ambush with two reinforcements wins 40%, against 60% when the same two come in from the front. A running fight is the quickest (median 5 turns: both wings turn in together); a pincer the longest (11).

### Measured: tactics and openings together

Two 240-battle batches, both with your squadron flown as strikers who can't
see enemy orders (the old charge autopilot is too predictable for the new
pilots to be a fair stand-in for a person). The control batch turns the enemy
tactics off and opens every battle head-on. Measured while ZTs still flew as
snipers.

| | Control | Tactics and openings |
| --- | --- | --- |
| Overall | 52% | 46% |
| Level 1 / 3 / 5 / 6 | 32 / 30 / 68 / 77% | 18 / 32 / 58 / 75% |
| Stops without an ace | 68% | 71% |
| Stops with an ace | 35% | 21% |
| Median battle length | 10 turns | 9 turns |
| Battles still going at turn 30 | 7 | 0 |

- **Ordinary fights are as hard as before.** Skirmishes win about the same; the enemy's rock deaths fell from 0.97 to 0.63 a battle.
- **Aces got much harder.** Across all levels: sector 1 elite 35% (control 60%), sector 1 boss 35% (45%), sector 2 elite 25% (45%), sector 2 boss 15% (25%), sector 3 elite 15% (30%), sector 3 boss 0% (5%). An ace now hunts the most worn-down ship from out of your guns while reading your orders.
- **By opening** (all stops, so harder stops weigh on the openings they allow): running fight 58%, head-on 54%, long approach 46%, pincer 43%, bounced 42%, ambush 38% (8 battles), flanked 35%.
- The old charge autopilot against the control enemies (the previous batch) won 47%; the striker autopilot wins 52%.

## Known issues and next steps

- Aces are too strong, more so since they fly the ace tactic: stops with an ace win 21% across all levels (35% before tactics), and the sector 3 boss (two Mk IV aces) never wins. Options: one ace at the sector 3 boss, and aces refitted level with your squadron (Mk I/II/III) instead of a step ahead.
- Flanked is the hardest opening (35% across all stops, head-on 54%).
- The player instinct called Ace shares a word with enemy aces; it may want a new name.
- The sector 2 boss is now an ordinary 4 + 2 fight; it has not been measured since the escort was removed.
- Sector 2 elites (3 + 1) and the sector 3 boss (4 + 2) sat below the 50% and 40% targets; worth a second pass once humans have played the new curve.
- Balance is untested with human play: line against line (is Kestrel handling worth its paper weakness?), how many modules a ship ends a run with now that they stack, free docks, threat per layer, ejection and scar odds, and instinct strength.
- Maps could be drawn from pools by stop type (open maps for skirmishes, dense ones for elites). Openings already mirror the one-sided starts; the terrain itself is never mirrored.
- Hit chance ignores angle, so a charge still ends in a nose-to-nose exchange; rear and flank shots counting for more would give the new tactics and openings their full effect.
- An Android export preset and a device test pass are still to do. The device pass should check the nebula and asteroid shaders' frame rate on a low-end phone.
- Try both camera modes in real play and pick the default. Overview stays the default until then.
