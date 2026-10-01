using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum RunNodeKind { Skirmish, Strike, Elite, Repair, Recruit, Event, Boss }

/// <summary>
/// What a card offers. Level-ups offer maneuvers and masteries for the pilot
/// and modules for the ship. Module crates offer modules only.
/// </summary>
public enum CardKind { Maneuver, Mastery, Module }

/// <summary>One option in a promotion or module-crate choice.</summary>
public class PromotionCard
{
    public CardKind Kind { get; set; }
    /// <summary>A ShipAbility (maneuver or mastery) or ShipUpgrade, depending on <see cref="Kind"/>.</summary>
    public string Id { get; set; } = "";
    /// <summary>For crate cards: the pilot whose ship gets the module.</summary>
    public string Callsign { get; set; }
}

/// <summary>A pilot offered for hire at a recruit stop.</summary>
public class RecruitOffer
{
    public PilotSave Pilot { get; set; }
}

/// <summary>What a chosen event option did, for the event page to report.</summary>
public class EventOutcome
{
    public string Text = "";
    /// <summary>Set when the option leads into a fight at this stop.</summary>
    public RunNodeKind? Battle;
    /// <summary>For a fight: the reason on the module crate a win opens, or null for none.</summary>
    public string CrateReward;
}

/// <summary>A choice offered by a distress-signal stop.</summary>
public class RunEventOption
{
    public string Label;
    public string Detail;
    public Func<RunState, RandomNumberGenerator, EventOutcome> Resolve;
    public Func<RunState, bool> Available = _ => true;
}

public class RunEvent
{
    public string Id;
    public string Title;
    public string Text;
    public RunEventOption[] Options;
}

/// <summary>
/// The authored and generated content of a run: sector maps, the fight at
/// each stop, events, recruits and promotion cards. Every roll is seeded from
/// the run so a saved run replays the same stops.
/// </summary>
public static class RunContent
{
    public const int SectorCount = 3;
    /// <summary>Regular layers before the boss; the boss is layer <c>RegularLayers + 1</c>.</summary>
    public const int RegularLayers = 4;

    public static readonly string[] SectorNames = { "ORION SPUR", "CYGNUS REACH", "HELIOS CROWN" };
    public static readonly string[] SectorBriefings =
    {
        "Kla'ed raiders have cut the Orion lanes. Break their blockade.",
        "Haven's evacuation convoy is waiting for a clear corridor. Get it out.",
        "The Kla'ed ace wing guards the Helios gate. End this.",
    };

    static readonly string[] BattleMapIds = BattleMaps.Battlefields.Select(map => map.Id).ToArray();

    // ---------------------------------------------------------------- maps

    /// <summary>Builds one sector: four layers of branching stops, then the boss.</summary>
    public static List<RunNode> GenerateSector(int sector, ulong seed)
    {
        var rng = new RandomNumberGenerator { Seed = seed };
        int[] sizes = { 2, rng.RandiRange(2, 3), rng.RandiRange(2, 3), rng.RandiRange(2, 3), 1 };
        var layers = new List<List<RunNode>>();
        int id = 0;
        for (int layer = 1; layer <= sizes.Length; layer++)
        {
            var row = new List<RunNode>();
            var usedSupport = new HashSet<RunNodeKind>();
            for (int slot = 0; slot < sizes[layer - 1]; slot++)
            {
                RunNodeKind kind = layer > RegularLayers ? RunNodeKind.Boss : PickKind(layer, sector, rng, usedSupport);
                row.Add(new RunNode
                {
                    Id = id++,
                    Layer = layer,
                    Slot = slot,
                    SlotCount = sizes[layer - 1],
                    Kind = kind,
                    Seed = ((ulong)rng.Randi() << 32) | rng.Randi(),
                });
            }
            layers.Add(row);
        }

        // Always offer a repair dock just before the boss.
        List<RunNode> preBoss = layers[RegularLayers - 1];
        if (preBoss.All(node => node.Kind != RunNodeKind.Repair))
            preBoss[rng.RandiRange(0, preBoss.Count - 1)].Kind = RunNodeKind.Repair;

        for (int i = 0; i < layers.Count - 1; i++)
            Connect(layers[i], layers[i + 1]);

        List<RunNode> nodes = layers.SelectMany(row => row).ToList();
        foreach (RunNode node in nodes)
        {
            node.MapId = BattleMapIds[rng.RandiRange(0, BattleMapIds.Length - 1)];
            if (node.Kind == RunNodeKind.Event)
                node.EventId = Events[rng.RandiRange(0, Events.Length - 1)].Id;
        }
        return nodes;
    }

    static RunNodeKind PickKind(int layer, int sector, RandomNumberGenerator rng, HashSet<RunNodeKind> usedSupport)
    {
        (RunNodeKind Kind, int Weight)[] table = layer switch
        {
            1 => sector == 1
                ? new[] { (RunNodeKind.Skirmish, 1) }
                : new[] { (RunNodeKind.Skirmish, 7), (RunNodeKind.Strike, 3) },
            2 => new[] { (RunNodeKind.Skirmish, 35), (RunNodeKind.Strike, 25), (RunNodeKind.Event, 20), (RunNodeKind.Recruit, 20) },
            3 => new[] { (RunNodeKind.Skirmish, 20), (RunNodeKind.Strike, 20), (RunNodeKind.Elite, 25), (RunNodeKind.Event, 20), (RunNodeKind.Repair, 15) },
            _ => new[] { (RunNodeKind.Repair, 30), (RunNodeKind.Recruit, 20), (RunNodeKind.Event, 25), (RunNodeKind.Elite, 25) },
        };
        // A layer never offers the same kind of non-combat stop twice.
        (RunNodeKind Kind, int Weight)[] allowed = table.Where(entry => !usedSupport.Contains(entry.Kind)).ToArray();
        int roll = rng.RandiRange(0, allowed.Sum(entry => entry.Weight) - 1);
        foreach ((RunNodeKind kind, int weight) in allowed)
        {
            if (roll < weight)
            {
                if (!IsBattleKind(kind))
                    usedSupport.Add(kind);
                return kind;
            }
            roll -= weight;
        }
        return RunNodeKind.Skirmish;
    }

    public static bool IsBattleKind(RunNodeKind kind) =>
        kind is RunNodeKind.Skirmish or RunNodeKind.Strike or RunNodeKind.Elite or RunNodeKind.Boss;

    /// <summary>
    /// Links each stop to the stops ahead that sit roughly above it, makes sure
    /// every stop can be reached, then removes crossing routes.
    /// </summary>
    static void Connect(List<RunNode> from, List<RunNode> to)
    {
        static float Position(RunNode node) => (node.Slot + 0.5f) / node.SlotCount;

        foreach (RunNode a in from)
        {
            foreach (RunNode b in to)
                if (Mathf.Abs(Position(a) - Position(b)) <= 0.5f)
                    a.Next.Add(b.Id);
            if (a.Next.Count == 0)
                a.Next.Add(to.OrderBy(b => Mathf.Abs(Position(a) - Position(b))).First().Id);
        }
        foreach (RunNode b in to)
            if (from.All(a => !a.Next.Contains(b.Id)))
                from.OrderBy(a => Mathf.Abs(Position(a) - Position(b))).First().Next.Add(b.Id);

        int SlotOf(int id) => to.First(node => node.Id == id).Slot;
        int InDegree(int id) => from.Count(node => node.Next.Contains(id));
        bool removed = true;
        while (removed)
        {
            removed = false;
            foreach (RunNode a1 in from)
            foreach (RunNode a2 in from.Where(node => node.Slot > a1.Slot))
            foreach (int b1 in a1.Next.ToList())
            foreach (int b2 in a2.Next.ToList())
            {
                if (removed || SlotOf(b1) <= SlotOf(b2))
                    continue;
                // Drop whichever crossing route leaves both ends still connected.
                if (a1.Next.Count > 1 && InDegree(b1) > 1)
                    a1.Next.Remove(b1);
                else if (a2.Next.Count > 1 && InDegree(b2) > 1)
                    a2.Next.Remove(b2);
                else
                    continue;
                removed = true;
            }
        }
        foreach (RunNode a in from)
            a.Next.Sort((x, y) => SlotOf(x).CompareTo(SlotOf(y)));
    }

    // ------------------------------------------------------------ battles

    public static string KindName(RunNodeKind kind) => kind switch
    {
        RunNodeKind.Skirmish => "SKIRMISH",
        RunNodeKind.Strike => "STRIKE",
        RunNodeKind.Elite => "ELITE WING",
        RunNodeKind.Repair => "REPAIR DOCK",
        RunNodeKind.Recruit => "RECRUIT",
        RunNodeKind.Event => "SIGNAL",
        RunNodeKind.Boss => "SECTOR BOSS",
        _ => kind.ToString().ToUpper(),
    };

    public static string KindSummary(RunNodeKind kind, int sector) => kind switch
    {
        RunNodeKind.Skirmish => "Destroy an enemy patrol.",
        RunNodeKind.Strike => "Destroy a marked command ship. The rest of the wing can live.",
        RunNodeKind.Elite => "An ace wing, sharper than any patrol. Win it to open a module crate.",
        RunNodeKind.Repair => "Every ship repaired to full, and a medic who can treat one scar.",
        RunNodeKind.Recruit => "Pilots looking for a squadron. One can join.",
        RunNodeKind.Event => "An unknown signal. Could be a prize, could be trouble.",
        RunNodeKind.Boss => BossName(sector) + ". Win to leave the sector; lose and the run ends.",
        _ => "",
    };

    static string BossName(int sector) => sector switch
    {
        1 => "The blockade command ship",
        2 => "The convoy escort",
        _ => "The Kla'ed ace wing",
    };

    /// <summary>Enemy combat level for a battle stop, from 1 to 10.</summary>
    public static int ThreatFor(int sector, int layer, RunNodeKind kind)
    {
        int threat = 3 * (sector - 1) + (layer <= 2 ? 1 : 2);
        return kind switch
        {
            RunNodeKind.Elite => threat + 2,
            RunNodeKind.Boss => 3 * sector,
            _ => threat,
        };
    }

    /// <summary>
    /// The hulls a wing draws from: mostly Kestrels in sector 1, then an even
    /// mix of the three lines.
    /// </summary>
    static ShipType[] EnemyPool(int tier) => tier == 1
        ? new[] { ShipTypes.Scout, ShipTypes.Scout, ShipTypes.Raptor, ShipTypes.Zt }
        : ShipTypes.PlayerFrames;

    /// <summary>
    /// How many enemies a battle stop brings at the start, and how many join
    /// later. Enemies always fly at their frames' base numbers: a fight gets
    /// harder through more ships, while pilots grow through levels and refits.
    /// </summary>
    public static (int Initial, int Wave) EnemyForce(int sector, RunNodeKind kind, int layer, RandomNumberGenerator rng)
    {
        bool late = layer > 2;
        return (kind, sector) switch
        {
            (RunNodeKind.Elite, 1) => (3, 0),
            (RunNodeKind.Elite, 2) => (3, 1),
            (RunNodeKind.Elite, _) => (4, 1),
            (RunNodeKind.Boss, 1) => (3, 1),
            (RunNodeKind.Boss, _) => (4, 2),
            (_, 1) => (late && rng.Randf() < 0.5f ? 3 : 2, 0),
            (_, 2) => (3, late ? 1 : 0),
            _ => (4, late ? 1 : 0),
        };
    }

    /// <summary>
    /// How many of its line's maneuvers an enemy can fly, by hull tier: none
    /// for sector 1's raiders, then the same two a pilot can know.
    /// </summary>
    public static int EnemyManeuvers(int tier) => tier == 1 ? 0 : Pilot.MaxManeuvers;

    /// <summary>A quick battle's opposition for a max-level squadron: a late sector 3 patrol.</summary>
    public static BattleMission QuickBattleForces(ulong seed) =>
        BuildMission(new RunNode { Layer = 3, Seed = seed, Kind = RunNodeKind.Skirmish }, 3, RunNodeKind.Skirmish);

    /// <summary>The fight at a battle stop. Seeded by the stop, so the briefing matches the battle.</summary>
    public static BattleMission BuildMission(RunNode node, int sector, RunNodeKind kind)
    {
        var rng = new RandomNumberGenerator { Seed = node.Seed };
        int threat = ThreatFor(sector, node.Layer, kind);
        MissionObjective objective = kind switch
        {
            RunNodeKind.Strike => MissionObjective.DestroyTarget,
            RunNodeKind.Boss when sector == 1 => MissionObjective.DestroyTarget,
            RunNodeKind.Boss when sector == 2 => MissionObjective.EscortShip,
            _ => MissionObjective.EliminateHostiles,
        };
        int tier = Mathf.Clamp(sector + (kind is RunNodeKind.Elite or RunNodeKind.Boss ? 1 : 0), 1, 3);
        (int initial, int wave) = EnemyForce(sector, kind, node.Layer, rng);
        ShipType[] pool = EnemyPool(tier);
        ShipType Pick() => pool[rng.RandiRange(0, pool.Length - 1)];
        ShipType[] squad = Enumerable.Range(0, initial).Select(_ => Pick()).ToArray();
        ShipType[] reinforcements = Enumerable.Range(0, wave).Select(_ => Pick()).ToArray();
        if (objective == MissionObjective.DestroyTarget)
            squad[0] = ShipTypes.Zt; // the first enemy is the marked target

        string name = kind switch
        {
            RunNodeKind.Boss => sector switch { 1 => "BLOCKADE BREAKER", 2 => "CONVOY ESCORT", _ => "ACE WING" },
            RunNodeKind.Elite => "ACE INTERCEPTORS",
            RunNodeKind.Strike => "COMMAND STRIKE",
            _ => "PATROL CLASH",
        };
        return new BattleMission
        {
            Name = name,
            Briefing = KindSummary(kind, sector),
            Threat = threat,
            Objective = objective,
            MapId = objective == MissionObjective.EscortShip ? BattleMaps.EscortCorridorId : node.MapId,
            EnemySquad = squad,
            Reinforcements = reinforcements,
            EnemyManeuvers = EnemyManeuvers(tier),
            EnemiesReadOrders = kind == RunNodeKind.Elite,
            EnemyAITuning = MissionAITuning.ForObjective(objective),
        };
    }

    // ------------------------------------------------------------ pilots

    public static readonly string[] Callsigns =
    {
        "VIPER", "GHOST", "NOVA", "RAZOR", "ECHO", "JINX",
        "MAVERICK", "WRAITH", "COMET", "HAVOC", "SABLE", "FLINT",
        "ORION", "TALON", "DUSK", "ZEPHYR", "IRONSIDE", "PIXIE",
    };

    /// <summary>A fresh level-1 pilot with their class's signature maneuver and, usually, an instinct.</summary>
    public static Pilot NewPilot(string callsign, ShipType frame, Perk instinct = null)
    {
        var pilot = new Pilot(callsign, frame);
        pilot.LearnManeuver(frame.ManeuverPool[0]);
        if (instinct is { Positive: true })
            pilot.Perks.Add(instinct);
        return pilot;
    }

    /// <summary>
    /// A recruit flies a random frame with a random instinct, and arrives at
    /// a level just below the squadron's, having already taken a level-up
    /// card for each level.
    /// </summary>
    public static Pilot NewRecruit(string callsign, int level, RandomNumberGenerator rng)
    {
        ShipType frame = ShipTypes.PlayerFrames[rng.RandiRange(0, ShipTypes.PlayerFrames.Length - 1)];
        Perk instinct = Perks.Instincts[rng.RandiRange(0, Perks.Instincts.Length - 1)];
        Pilot pilot = NewPilot(callsign, frame, instinct);
        for (int next = 2; next <= level; next++)
        {
            pilot.Level = next;
            List<PromotionCard> cards = PromotionCards(pilot, rng);
            if (cards.Count > 0)
                ApplyCard(pilot, cards[rng.RandiRange(0, cards.Count - 1)]);
        }
        return pilot;
    }

    // -------------------------------------------------------------- cards

    /// <summary>
    /// Three distinct level-up choices. While a pilot can still learn a
    /// maneuver (until they know <see cref="Pilot.MaxManeuvers"/>), the
    /// level-up offers every maneuver their line could teach them, so the
    /// second maneuver is chosen from all of them. After that, one each of a
    /// module and a mastery while there are any, then any of those.
    /// </summary>
    public static List<PromotionCard> PromotionCards(Pilot pilot, RandomNumberGenerator rng)
    {
        var cards = new List<PromotionCard>();
        if (pilot.Maneuvers.Count < Pilot.MaxManeuvers)
        {
            List<ShipAbility> learnable = pilot.UnlearnedManeuvers.ToList();
            while (learnable.Count > 3)
                learnable.RemoveAt(rng.RandiRange(0, learnable.Count - 1));
            cards.AddRange(learnable.Select(a => new PromotionCard { Kind = CardKind.Maneuver, Id = a.ToString() }));
        }
        List<PromotionCard> modules = pilot.UnfittedModules
            .Select(module => new PromotionCard { Kind = CardKind.Module, Id = module.Id.ToString() }).ToList();
        List<PromotionCard> masteries = pilot.UnmasteredManeuvers
            .Select(a => new PromotionCard { Kind = CardKind.Mastery, Id = a.ToString() }).ToList();

        foreach (List<PromotionCard> kind in new[] { modules, masteries })
            if (cards.Count < 3 && kind.Count > 0)
                cards.Add(TakeRandom(kind, rng));
        var rest = modules.Concat(masteries).ToList();
        while (cards.Count < 3 && rest.Count > 0)
            cards.Add(TakeRandom(rest, rng));
        return cards;
    }

    /// <summary>
    /// A bonus reward (an elite win, some signals): three modules, each
    /// already matched to a pilot's ship, one of which is fitted. The offer is
    /// spread across pilots and modules where it can be.
    /// </summary>
    public static List<PromotionCard> ModuleCrate(IEnumerable<Pilot> survivors, RandomNumberGenerator rng)
    {
        var options = survivors
            .SelectMany(pilot => pilot.UnfittedModules.Select(module => (Pilot: pilot, Module: module)))
            .ToList();
        var cards = new List<PromotionCard>();
        var usedPilots = new HashSet<string>();
        var usedModules = new HashSet<ShipUpgrade>();
        while (cards.Count < 3 && options.Count > 0)
        {
            // Spread the offer: a pilot and a module not yet on offer,
            // relaxing one preference at a time.
            var pool = options.Where(o => !usedPilots.Contains(o.Pilot.Callsign) && !usedModules.Contains(o.Module.Id)).ToList();
            if (pool.Count == 0)
                pool = options.Where(o => !usedModules.Contains(o.Module.Id)).ToList();
            if (pool.Count == 0)
                pool = options;
            var pick = pool[rng.RandiRange(0, pool.Count - 1)];
            options.Remove(pick);
            usedPilots.Add(pick.Pilot.Callsign);
            usedModules.Add(pick.Module.Id);
            cards.Add(new PromotionCard { Kind = CardKind.Module, Id = pick.Module.Id.ToString(), Callsign = pick.Pilot.Callsign });
        }
        return cards;
    }

    static T TakeRandom<T>(List<T> list, RandomNumberGenerator rng)
    {
        int index = rng.RandiRange(0, list.Count - 1);
        T item = list[index];
        list.RemoveAt(index);
        return item;
    }

    public static void ApplyCard(Pilot pilot, PromotionCard card)
    {
        switch (card.Kind)
        {
            case CardKind.Maneuver when Enum.TryParse(card.Id, out ShipAbility ability):
                pilot.LearnManeuver(ability);
                break;
            case CardKind.Mastery when Enum.TryParse(card.Id, out ShipAbility mastered):
                pilot.Master(mastered);
                break;
            case CardKind.Module when ShipUpgrades.TryParse(card.Id, out ShipUpgrade module):
                pilot.InstallUpgrade(module);
                break;
        }
    }

    /// <summary>Card text: a short title, a category line and one sentence.</summary>
    public static (string Title, string Category, string Text) DescribeCard(PromotionCard card, Pilot pilot)
    {
        switch (card.Kind)
        {
            case CardKind.Maneuver when Enum.TryParse(card.Id, out ShipAbility ability):
                return (ManeuverCatalog.AbilityName(ability).ToUpper(), "NEW MANEUVER", ManeuverCatalog.Blurb(ability));
            case CardKind.Mastery when Enum.TryParse(card.Id, out ShipAbility mastered):
                return (ManeuverCatalog.AbilityName(mastered).ToUpper(), "MASTERY", Masteries.Describe(mastered, pilot.Ship));
            case CardKind.Module when ShipUpgrades.TryParse(card.Id, out ShipUpgrade module):
                ShipUpgradeDefinition definition = ShipUpgrades.Get(module);
                string text = definition.Description;
                string category = $"{ShipUpgrades.SlotName(definition.Slot)} MODULE";
                if (!string.IsNullOrEmpty(card.Callsign))
                    category += $" · {card.Callsign}";
                return (definition.Name.ToUpper(), category, text);
        }
        return (card.Id, "", "");
    }

    // ------------------------------------------------------------- events

    public static readonly RunEvent[] Events =
    {
        new()
        {
            Id = "derelict", Title = "DERELICT FREIGHTER",
            Text = "A gutted freighter drifts across the lane. Some of its cargo holds still look sealed.",
            Options = new RunEventOption[]
            {
                new()
                {
                    Label = "SEARCH THE HOLDS", Detail = "A module crate. Someone else may want it too.",
                    Resolve = (run, rng) => rng.Randf() < 0.35f
                        ? new EventOutcome { Text = "Kla'ed raiders were waiting in the freighter's shadow. Win and the cargo is yours.", Battle = RunNodeKind.Skirmish, CrateReward = "DERELICT CARGO" }
                        : Crate(run, "DERELICT CARGO", rng, "The holds are intact. Open the crate."),
                },
                new() { Label = "MOVE ON", Detail = "Leave it drifting.", Resolve = (_, _) => new EventOutcome { Text = "You leave the wreck behind." } },
            },
        },
        new()
        {
            Id = "stranded", Title = "STRANDED PILOT",
            Text = "An escape pod's beacon is still blinking. Someone inside is alive.",
            Options = new RunEventOption[]
            {
                new()
                {
                    Label = "TAKE THEM ABOARD", Detail = "A free recruit joins the squadron.",
                    Available = run => run.Living.Count() < RunState.RosterLimit,
                    Resolve = (run, rng) =>
                    {
                        Pilot pilot = run.HireFreePilot(rng);
                        return new EventOutcome { Text = $"{pilot.Callsign} flies a {pilot.Ship.DisplayName} and is glad of the ride." };
                    },
                },
                new()
                {
                    Label = "CALL A RESCUE SHIP", Detail = $"The pilot shares their combat logs while they wait. +{EventXpSmall} XP for every pilot.",
                    Resolve = (run, rng) => SquadXp(run, EventXpSmall, rng, "A rescue ship is on its way. The squadron studies the pilot's logs."),
                },
            },
        },
        new()
        {
            Id = "drones", Title = "REPAIR DRONES",
            Text = "An abandoned repair swarm answers your hail and waits for instructions.",
            Options = new RunEventOption[]
            {
                new()
                {
                    Label = "LET THEM WORK", Detail = "Repair half of every ship's damage.",
                    Resolve = (run, _) =>
                    {
                        foreach (Pilot pilot in run.Living)
                            pilot.Repair(Mathf.CeilToInt(pilot.HullDamage / 2f));
                        return new EventOutcome { Text = "The drones patch every hull they can reach." };
                    },
                },
                new()
                {
                    Label = "STRIP THEM FOR PARTS", Detail = "A module crate, but no repairs.",
                    Resolve = (run, rng) => Crate(run, "DRONE PARTS", rng, "The swarm makes good spares."),
                },
            },
        },
        new()
        {
            Id = "cache", Title = "WAR CACHE",
            Text = "A sealed munitions cache from the last war, still wired with old ordnance.",
            Options = new RunEventOption[]
            {
                new()
                {
                    Label = "CRACK IT OPEN", Detail = "A free module for one of your ships. It might go off.",
                    Available = run => run.Living.Any(CanTakeModule),
                    Resolve = (run, rng) =>
                    {
                        Pilot[] candidates = run.Living.Where(CanTakeModule).ToArray();
                        Pilot pilot = candidates[rng.RandiRange(0, candidates.Length - 1)];
                        ShipUpgradeDefinition[] upgrades = pilot.UnfittedModules.ToArray();
                        ShipUpgradeDefinition upgrade = upgrades[rng.RandiRange(0, upgrades.Length - 1)];
                        pilot.InstallUpgrade(upgrade.Id);
                        string text = $"{pilot.Callsign} fits a {upgrade.Name}.";
                        if (rng.Randf() < 0.3f)
                        {
                            int damage = Mathf.CeilToInt(pilot.MaxHull * 0.4f);
                            pilot.TakeHullDamage(damage);
                            text += $" A charge goes off: {damage} hull damage.";
                        }
                        return new EventOutcome { Text = text };
                    },
                },
                new() { Label = "LEAVE IT", Detail = "Not worth the risk.", Resolve = (_, _) => new EventOutcome { Text = "You mark the cache and fly on." } },
            },
        },
        new()
        {
            Id = "storm", Title = "ION STORM",
            Text = "A storm front sits across the direct route.",
            Options = new RunEventOption[]
            {
                new()
                {
                    Label = "PUSH THROUGH", Detail = $"Every ship takes {StormHullDamage} hull damage. Hard flying: +{EventXpLarge} XP for every pilot.",
                    Resolve = (run, rng) =>
                    {
                        foreach (Pilot pilot in run.Living)
                            pilot.TakeHullDamage(StormHullDamage);
                        return SquadXp(run, EventXpLarge, rng, "Rough going, but every pilot comes out sharper.");
                    },
                },
                new()
                {
                    Label = "WAIT IT OUT", Detail = "Rest while it passes. One pilot's scar fades.",
                    Resolve = (run, rng) =>
                    {
                        Pilot[] scarred = run.Living.Where(p => p.Scars.Any()).ToArray();
                        if (scarred.Length == 0)
                            return new EventOutcome { Text = "The squadron rests while the storm passes." };
                        Pilot pilot = scarred[rng.RandiRange(0, scarred.Length - 1)];
                        Perk[] scars = pilot.Scars.ToArray();
                        Perk scar = scars[rng.RandiRange(0, scars.Length - 1)];
                        pilot.Perks.Remove(scar);
                        return new EventOutcome { Text = $"The squadron rests while the storm passes. {pilot.Callsign} shakes off {scar.Name}." };
                    },
                },
            },
        },
        new()
        {
            Id = "distress", Title = "DISTRESS CALL",
            Text = "A convoy is under attack and calling for any Alliance ship in range.",
            Options = new RunEventOption[]
            {
                new()
                {
                    Label = "ANSWER THE CALL", Detail = "Fight a strike mission. Win and the convoy hands over a module crate.",
                    Resolve = (_, _) => new EventOutcome { Text = "You turn toward the convoy.", Battle = RunNodeKind.Strike, CrateReward = "CONVOY'S THANKS" },
                },
                new() { Label = "KEEP COURSE", Detail = "Someone else will have to help.", Resolve = (_, _) => new EventOutcome { Text = "The signal fades behind you." } },
            },
        },
    };

    const int StormHullDamage = 60;
    const int EventXpSmall = 50;
    const int EventXpLarge = 70;

    static bool CanTakeModule(Pilot pilot) => pilot.UnfittedModules.Any();

    /// <summary>XP for every living pilot; the text names anyone who levels up.</summary>
    static EventOutcome SquadXp(RunState run, int amount, RandomNumberGenerator rng, string text)
    {
        List<string> promoted = run.Living.ToList().Where(pilot => run.GrantXp(pilot, amount, rng) > 0).Select(pilot => pilot.Callsign).ToList();
        text += $" +{amount} XP each.";
        if (promoted.Count > 0)
            text += $" {JoinNames(promoted)} {(promoted.Count == 1 ? "levels" : "level")} up.";
        return new EventOutcome { Text = text };
    }

    static EventOutcome Crate(RunState run, string reason, RandomNumberGenerator rng, string text) => new()
    {
        Text = run.QueueCrate(reason, run.Living, rng) ? text : "There's nothing in it the squadron's ships can use.",
    };

    static string JoinNames(List<string> names) =>
        names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

    public static RunEvent EventById(string id) => Events.FirstOrDefault(e => e.Id == id) ?? Events[0];
}
