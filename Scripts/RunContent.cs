using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum RunNodeKind { Skirmish, Strike, Elite, Repair, Recruit, Event, Boss }

/// <summary>
/// What a card offers. Level-ups offer pilot growth (maneuvers, masteries,
/// instincts); module crates offer ship hardware.
/// </summary>
public enum CardKind { Maneuver, Mastery, Instinct, Module }

/// <summary>One option in a promotion or module-crate choice.</summary>
public class PromotionCard
{
    public CardKind Kind { get; set; }
    /// <summary>A ShipAbility (maneuver or mastery), perk id or ShipUpgrade, depending on <see cref="Kind"/>.</summary>
    public string Id { get; set; } = "";
    /// <summary>For module cards: the pilot whose ship gets the module.</summary>
    public string Callsign { get; set; }
}

/// <summary>A pilot offered for hire at a recruit stop.</summary>
public class RecruitOffer
{
    public PilotSave Pilot { get; set; }
    public int Cost { get; set; }
}

/// <summary>What a chosen event option did, for the event page to report.</summary>
public class EventOutcome
{
    public string Text = "";
    /// <summary>Set when the option leads into a fight at this stop.</summary>
    public RunNodeKind? Battle;
    public int BonusSalvage;
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

    static readonly string[] BattleMapIds = { "shard-run", "cobalt-veil", "broken-ring" };

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
        RunNodeKind.Elite => "A veteran wing. Win it to open a module crate.",
        RunNodeKind.Repair => "Buy modules and refits, repair hulls and treat scars.",
        RunNodeKind.Recruit => "Hire a new pilot for the squadron.",
        RunNodeKind.Event => "An unknown signal. Could be salvage, could be trouble.",
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

    public static int SalvageReward(RunNodeKind kind, int sector) => kind switch
    {
        RunNodeKind.Strike => 65 + 20 * (sector - 1),
        RunNodeKind.Elite => 90 + 25 * (sector - 1),
        RunNodeKind.Boss => 120 + 30 * (sector - 1),
        _ => 45 + 15 * (sector - 1),
    };

    static string[] EnemyPool(int tier) => tier switch
    {
        1 => new[] { "scout", "scout", "raptor", "zt" },
        2 => new[] { "scout", "raptor", "zt", "s4_striker", "r3_black_hawk", "zt_6" },
        _ => new[] { "s4_striker", "s9_ghost", "r3_black_hawk", "r5_falcon", "zt_6", "zt_8_bulwark" },
    };

    static string HeavyHull(int tier) => tier switch { 1 => "zt", 2 => "zt_6", _ => "zt_8_bulwark" };

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
        EnemyEncounter encounter = EncounterDifficulty.Roll(EncounterDifficulty.CombatLevel(threat, objective), rng);
        string[] pool = EnemyPool(tier);
        var squad = new ShipType[encounter.ShipCount];
        for (int i = 0; i < squad.Length; i++)
            squad[i] = ShipTypes.FromId(pool[rng.RandiRange(0, pool.Length - 1)]);
        if (objective == MissionObjective.DestroyTarget)
            squad[0] = ShipTypes.FromId(HeavyHull(tier)); // the first enemy is the marked target

        string name = kind switch
        {
            RunNodeKind.Boss => sector switch { 1 => "BLOCKADE BREAKER", 2 => "CONVOY ESCORT", _ => "ACE WING" },
            RunNodeKind.Elite => "VETERAN INTERCEPTORS",
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
            EnemyStatMultiplier = encounter.StatMultiplier,
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

    /// <summary>A fresh level-1 pilot with their class's signature maneuver.</summary>
    public static Pilot NewPilot(string callsign, ShipType baseClass)
    {
        var pilot = new Pilot(callsign, baseClass);
        pilot.LearnManeuver(baseClass.ManeuverPool[0]);
        return pilot;
    }

    /// <summary>
    /// A recruit arrives at a level just below the squadron's, having already
    /// taken the promotions a pilot of that level would have. Their ship is a
    /// bare base frame: modules and refits are for the squadron to buy.
    /// </summary>
    public static Pilot NewRecruit(string callsign, ShipType baseClass, int level, RandomNumberGenerator rng)
    {
        Pilot pilot = NewPilot(callsign, baseClass);
        for (int next = 2; next <= level; next++)
        {
            pilot.Level = next;
            List<PromotionCard> cards = PromotionCards(pilot, rng);
            if (cards.Count > 0)
                ApplyCard(pilot, cards[0]);
        }
        return pilot;
    }

    public static int RecruitCost(int level) => 40 + 30 * level;

    // -------------------------------------------------------------- cards

    /// <summary>
    /// Three distinct level-up choices for a pilot, all pilot growth: a new
    /// maneuver while they have room for one, then instincts and masteries of
    /// maneuvers they already fly. Ship hardware never appears here.
    /// </summary>
    public static List<PromotionCard> PromotionCards(Pilot pilot, RandomNumberGenerator rng)
    {
        var cards = new List<PromotionCard>();
        var maneuvers = new List<PromotionCard>();
        if (pilot.Maneuvers.Count < Pilot.MaxManeuvers)
            maneuvers.AddRange(pilot.UnlearnedManeuvers.Select(a => new PromotionCard { Kind = CardKind.Maneuver, Id = a.ToString() }));
        List<PromotionCard> instincts = Perks.Instincts.Where(p => !pilot.Perks.Contains(p))
            .Select(p => new PromotionCard { Kind = CardKind.Instinct, Id = p.Id }).ToList();
        List<PromotionCard> masteries = pilot.UnmasteredManeuvers
            .Select(a => new PromotionCard { Kind = CardKind.Mastery, Id = a.ToString() }).ToList();

        if (maneuvers.Count > 0)
            cards.Add(TakeRandom(maneuvers, rng));
        if (instincts.Count > 0)
            cards.Add(TakeRandom(instincts, rng));
        var rest = maneuvers.Concat(instincts).Concat(masteries).ToList();
        while (cards.Count < 3 && rest.Count > 0)
            cards.Add(TakeRandom(rest, rng));
        return cards;
    }

    /// <summary>
    /// An elite wing's reward: three modules, each already matched to a
    /// surviving pilot's ship, one of which is fitted free. Empty slots are
    /// favoured so the crate usually adds rather than swaps.
    /// </summary>
    public static List<PromotionCard> ModuleCrate(IEnumerable<Pilot> survivors, RandomNumberGenerator rng)
    {
        var options = survivors
            .SelectMany(pilot => ShipUpgrades.All
                .Where(module => pilot.CanInstall(module.Id))
                .Select(module => (Pilot: pilot, Module: module, Empty: pilot.ModuleIn(module.Slot) == null)))
            .ToList();
        var cards = new List<PromotionCard>();
        var usedPilots = new HashSet<string>();
        var usedModules = new HashSet<ShipUpgrade>();
        while (cards.Count < 3 && options.Count > 0)
        {
            // Spread the offer: a pilot and a module not yet on offer, an
            // empty slot if possible, relaxing one preference at a time.
            var pool = options.Where(o => !usedPilots.Contains(o.Pilot.Callsign) && !usedModules.Contains(o.Module.Id) && o.Empty).ToList();
            if (pool.Count == 0)
                pool = options.Where(o => !usedPilots.Contains(o.Pilot.Callsign) && !usedModules.Contains(o.Module.Id)).ToList();
            if (pool.Count == 0)
                pool = options.Where(o => !usedModules.Contains(o.Module.Id)).ToList();
            if (pool.Count == 0)
                pool = options;
            var pick = pool[rng.RandiRange(0, pool.Count - 1)];
            options.Remove(pick);
            options.RemoveAll(o => o.Pilot == pick.Pilot && o.Module.Id == pick.Module.Id);
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
            case CardKind.Instinct:
                Perk perk = Perks.ById(card.Id);
                if (perk != null && perk.Positive && !pilot.Perks.Contains(perk))
                    pilot.Perks.Add(perk);
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
            case CardKind.Instinct:
                Perk perk = Perks.ById(card.Id);
                return ((perk?.Name ?? card.Id).ToUpper(), "INSTINCT", perk?.Description ?? "");
            case CardKind.Module when ShipUpgrades.TryParse(card.Id, out ShipUpgrade module):
                ShipUpgradeDefinition definition = ShipUpgrades.Get(module);
                string text = definition.Description;
                if (pilot.ModuleIn(definition.Slot) is ShipUpgrade replaced)
                    text += $" Replaces {ShipUpgrades.Get(replaced).Name}.";
                return (definition.Name.ToUpper(), $"{ShipUpgrades.SlotName(definition.Slot)} MODULE · {pilot.Callsign}", text);
        }
        return (card.Id, "", "");
    }

    /// <summary>How a refit frame differs from the pilot's current hull.</summary>
    public static string FrameDelta(ShipType from, ShipType to)
    {
        var parts = new List<string>();
        void Add(string label, float delta, string unit = "")
        {
            if (Mathf.Abs(delta) >= 0.5f)
                parts.Add($"{(delta > 0 ? "+" : "")}{delta:0}{unit} {label}");
        }
        Add("hull", to.MaxHp - from.MaxHp);
        Add("shield", to.MaxShield - from.MaxShield);
        Add("damage", to.ShotDamage - from.ShotDamage);
        Add("accuracy", (to.Accuracy - from.Accuracy) * 100f, "%");
        Add("evasion", (to.Evasion - from.Evasion) * 100f, "%");
        Add("turn", to.NormalTurnLimitDegrees - from.NormalTurnLimitDegrees, "°");
        Add("speed", to.NormalMoveMaxDistance - from.NormalMoveMaxDistance);
        return string.Join(", ", parts) + ".";
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
                    Label = "SALVAGE IT", Detail = "+60 salvage. Someone else may want it too.",
                    Resolve = (run, rng) => rng.Randf() < 0.35f
                        ? new EventOutcome { Text = "Kla'ed raiders were waiting in the freighter's shadow.", Battle = RunNodeKind.Skirmish, BonusSalvage = 60 }
                        : Salvage(run, 60, "The holds were full. +60 salvage."),
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
                new() { Label = "STRIP THE POD", Detail = "+30 salvage.", Resolve = (run, _) => Salvage(run, 30, "You take what you can use. +30 salvage.") },
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
                new() { Label = "SCRAP THEM", Detail = "+50 salvage.", Resolve = (run, _) => Salvage(run, 50, "The swarm makes good scrap. +50 salvage.") },
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
                    Label = "CRACK IT OPEN", Detail = "A free module for an empty slot. It might go off.",
                    Available = run => run.Living.Any(HasEmptySlot),
                    Resolve = (run, rng) =>
                    {
                        Pilot[] candidates = run.Living.Where(HasEmptySlot).ToArray();
                        Pilot pilot = candidates[rng.RandiRange(0, candidates.Length - 1)];
                        ShipUpgradeDefinition[] upgrades = ShipUpgrades.All
                            .Where(u => pilot.HasSlot(u.Slot) && pilot.ModuleIn(u.Slot) == null).ToArray();
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
                    Label = "PUSH THROUGH", Detail = "Every ship takes 6 hull damage. +40 salvage from wrecks inside.",
                    Resolve = (run, _) =>
                    {
                        foreach (Pilot pilot in run.Living)
                            pilot.TakeHullDamage(6);
                        return Salvage(run, 40, "Rough going, but the storm hid some prizes. +40 salvage.");
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
                    Label = "ANSWER THE CALL", Detail = "Fight a strike mission. +40 bonus salvage if you win.",
                    Resolve = (_, _) => new EventOutcome { Text = "You turn toward the convoy.", Battle = RunNodeKind.Strike, BonusSalvage = 40 },
                },
                new() { Label = "KEEP COURSE", Detail = "Someone else will have to help.", Resolve = (_, _) => new EventOutcome { Text = "The signal fades behind you." } },
            },
        },
    };

    static bool HasEmptySlot(Pilot pilot) => pilot.Ship.UpgradeSlots.Any(slot => pilot.ModuleIn(slot) == null);

    static EventOutcome Salvage(RunState run, int amount, string text)
    {
        run.Salvage += amount;
        return new EventOutcome { Text = text };
    }

    public static RunEvent EventById(string id) => Events.FirstOrDefault(e => e.Id == id) ?? Events[0];
}
