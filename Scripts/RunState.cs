using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum RunOutcome { InProgress, Victory, Defeat }

/// <summary>One stop on a sector map.</summary>
public class RunNode
{
    public int Id { get; set; }
    /// <summary>1-based row, counted up from the sector entrance; the boss is the last row.</summary>
    public int Layer { get; set; }
    public int Slot { get; set; }
    public int SlotCount { get; set; }
    public RunNodeKind Kind { get; set; }
    public List<int> Next { get; set; } = new();
    public bool Visited { get; set; }
    public ulong Seed { get; set; }
    public string MapId { get; set; } = "shard-run";
    public string EventId { get; set; }
    /// <summary>Set when an event at this stop turned into a fight.</summary>
    public RunNodeKind? EventBattle { get; set; }
    public int BonusSalvage { get; set; }

    /// <summary>The kind of fight at this stop, or null when there is none.</summary>
    [JsonIgnore]
    public RunNodeKind? BattleKind => EventBattle ?? (RunContent.IsBattleKind(Kind) ? Kind : null);
}

/// <summary>
/// A choice of cards waiting to be made: a level-up for one pilot, or a
/// module crate for the squadron (no callsign; each card names its pilot).
/// </summary>
public class PendingPromotion
{
    public const string CrateReason = "MODULE CRATE";

    public string Callsign { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<PromotionCard> Cards { get; set; } = new();

    [JsonIgnore] public bool IsCrate => string.IsNullOrEmpty(Callsign);
}

/// <summary>Everything the debrief shows about a run battle's consequences.</summary>
public class RunBattleReport
{
    public int Salvage;
    public List<string> Promotions = new();
    public bool SectorCleared;
    public RunOutcome Outcome;
}

/// <summary>Serialized form of a pilot.</summary>
public class PilotSave
{
    public string Callsign { get; set; } = "";
    public string ShipId { get; set; } = "scout";
    public List<string> Maneuvers { get; set; } = new();
    public List<string> Upgrades { get; set; } = new();
    public List<string> Masteries { get; set; } = new();
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public List<string> Perks { get; set; } = new();
    public PilotCondition Condition { get; set; }
    public int RecoveryStops { get; set; }
    public int HullDamage { get; set; }
    public int Kills { get; set; }
    public int Battles { get; set; }

    public static PilotSave From(Pilot pilot) => new()
    {
        Callsign = pilot.Callsign,
        ShipId = pilot.Ship.Id,
        Maneuvers = pilot.Maneuvers.Select(m => m.ToString()).ToList(),
        Upgrades = pilot.Upgrades.Select(u => u.ToString()).ToList(),
        Masteries = pilot.Masteries.Select(m => m.ToString()).ToList(),
        Level = pilot.Level,
        Xp = pilot.Xp,
        Perks = pilot.Perks.Select(p => p.Id).ToList(),
        Condition = pilot.Condition,
        RecoveryStops = pilot.RecoveryStops,
        HullDamage = pilot.HullDamage,
        Kills = pilot.Kills,
        Battles = pilot.Battles,
    };

    public Pilot ToPilot()
    {
        var pilot = new Pilot(Callsign, ShipTypes.FromId(ShipId))
        {
            Level = Level,
            Xp = Xp,
            Condition = Condition,
            RecoveryStops = RecoveryStops,
            HullDamage = HullDamage,
            Kills = Kills,
            Battles = Battles,
        };
        pilot.RestoreManeuvers(Maneuvers.Select(m => Enum.TryParse(m, out ShipAbility a) ? (ShipAbility?)a : null)
            .Where(a => a.HasValue).Select(a => a.Value));
        foreach (string id in Masteries)
            if (Enum.TryParse(id, out ShipAbility ability))
                pilot.Master(ability);
        foreach (string id in Upgrades)
            if (ShipUpgrades.TryParse(id, out ShipUpgrade upgrade))
                pilot.InstallUpgrade(upgrade);
        foreach (string id in Perks)
        {
            Perk perk = global::Perks.ById(id);
            if (perk != null && !pilot.Perks.Contains(perk))
                pilot.Perks.Add(perk);
        }
        return pilot;
    }
}

/// <summary>
/// A roguelite run: three sectors of branching stops, a squadron of up to
/// five pilots, and salvage to spend. Hull damage, wounds, scars and deaths
/// carry from stop to stop. Pilots grow through XP; ships grow through
/// salvage spent at repair docks. The run saves after every change so a phone
/// can close the app at any moment.
/// </summary>
public class RunState
{
    /// <summary>Version 2: pilots and ships progress separately (modules, masteries, instincts, scars).</summary>
    public const int SaveVersion = 2;
    public const int RosterLimit = 5;
    public const int SquadLimit = 3;
    public const int StartingSalvage = 50;
    public const int RepairCostPerHull = 2;
    public const int TreatWoundCost = 40;
    public const int TreatScarCost = 50;
    public const int RefitCost = 120;
    public const int DockStockSize = 3;
    /// <summary>Share of each ship's damage patched for free when a sector is cleared.</summary>
    public const float SectorPatchFraction = 0.5f;
    const string SavePath = "user://ace-star-pilot-run.json";

    public static RunState Current { get; private set; }

    public int Version { get; set; } = SaveVersion;
    public ulong Seed { get; set; }
    public int Sector { get; set; } = 1;
    public int Salvage { get; set; }
    public List<RunNode> Nodes { get; set; } = new();
    /// <summary>The last stop completed in this sector, or -1 at the sector entrance.</summary>
    public int CurrentNodeId { get; set; } = -1;
    /// <summary>The stop the squadron is at but has not finished, or -1.</summary>
    public int ActiveNodeId { get; set; } = -1;
    /// <summary>
    /// True from launch until the battle's result is saved. The save made at
    /// launch is the battle's checkpoint: quitting mid-battle resumes here.
    /// </summary>
    public bool BattleInProgress { get; set; }
    /// <summary>Callsigns of the pilots flying the battle in progress.</summary>
    public List<string> BattleSquad { get; set; } = new();
    public List<PendingPromotion> Promotions { get; set; } = new();
    public List<RecruitOffer> RecruitOffers { get; set; } = new();
    /// <summary>Modules for sale at the active repair dock; each sells once.</summary>
    public List<ShipUpgrade> DockStock { get; set; } = new();
    /// <summary>The report of an event option already chosen at the active stop.</summary>
    public string EventResult { get; set; }
    public RunOutcome Outcome { get; set; }
    public int BattlesWon { get; set; }
    public int TotalKills { get; set; }
    public int NextCallsign { get; set; }
    public int NextRngStep { get; set; }
    /// <summary>A one-time message for the map, such as a new sector.</summary>
    public string Notice { get; set; }
    public List<PilotSave> PilotData { get; set; } = new();

    [JsonIgnore] public List<Pilot> Pilots { get; private set; } = new();
    [JsonIgnore] public IEnumerable<Pilot> Living => Pilots.Where(p => p.Alive);
    [JsonIgnore] public IEnumerable<Pilot> Fallen => Pilots.Where(p => !p.Alive);
    [JsonIgnore] public IEnumerable<Pilot> Ready => Living.Where(p => p.CanDeploy);
    [JsonIgnore] public string SectorName => RunContent.SectorNames[Mathf.Clamp(Sector, 1, RunContent.SectorCount) - 1];
    [JsonIgnore] public RunNode CurrentNode => Node(CurrentNodeId);
    [JsonIgnore] public RunNode ActiveNode => Node(ActiveNodeId);

    public RunNode Node(int id) => Nodes.FirstOrDefault(node => node.Id == id);

    /// <summary>Stops the squadron can travel to next.</summary>
    [JsonIgnore]
    public IEnumerable<RunNode> Reachable => CurrentNodeId < 0
        ? Nodes.Where(node => node.Layer == 1)
        : CurrentNode.Next.Select(Node);

    /// <summary>A fresh generator for one decision, stepped so each roll differs but replays identically.</summary>
    RandomNumberGenerator NextRng() => new() { Seed = Seed ^ (0x9E3779B97F4A7C15UL * (ulong)(++NextRngStep)) };

    // ---------------------------------------------------------- lifecycle

    public static bool HasSave => FileAccess.FileExists(SavePath);

    public static RunState StartNew(ulong? seed = null)
    {
        var run = new RunState
        {
            Seed = seed ?? (((ulong)GD.Randi() << 32) | GD.Randi()),
            Salvage = StartingSalvage,
        };
        foreach (ShipType shipClass in ShipTypes.RecruitableClasses)
            run.Pilots.Add(RunContent.NewPilot(run.TakeCallsign(), shipClass));
        run.Nodes = RunContent.GenerateSector(1, run.Seed);
        run.Notice = $"SECTOR 1 · {run.SectorName}. {RunContent.SectorBriefings[0]}";
        Current = run;
        run.Save();
        return run;
    }

    /// <summary>
    /// Loads the saved run. If a battle was running when the game closed,
    /// <see cref="BattleInProgress"/> is still set and the run resumes at the
    /// start of that battle (see <see cref="ResumeBattle"/>).
    /// </summary>
    public static RunState Load()
    {
        if (!HasSave)
            return null;
        try
        {
            using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
            RunState run = JsonSerializer.Deserialize<RunState>(file.GetAsText(), JsonOptions);
            if (run == null || run.Version != SaveVersion)
                return null;
            run.Pilots = run.PilotData.Select(p => p.ToPilot()).ToList();
            Current = run;
            return run;
        }
        catch (Exception error)
        {
            GD.PushError($"Could not load the saved run: {error.Message}");
            return null;
        }
    }

    public void Save()
    {
        PilotData = Pilots.Select(PilotSave.From).ToList();
        using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(this, JsonOptions));
    }

    public static void EndAndDelete()
    {
        Current = null;
        if (HasSave)
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    string TakeCallsign()
    {
        var used = new HashSet<string>(Pilots.Select(p => p.Callsign));
        for (int attempt = 0; attempt < RunContent.Callsigns.Length * 3; attempt++)
        {
            int index = NextCallsign++;
            string name = RunContent.Callsigns[index % RunContent.Callsigns.Length];
            if (index >= RunContent.Callsigns.Length)
                name += $" {index / RunContent.Callsigns.Length + 1}";
            if (!used.Contains(name))
                return name;
        }
        return $"PILOT {Pilots.Count + 1}";
    }

    // --------------------------------------------------------------- stops

    /// <summary>Travels to a stop. Recruit stops roll their candidates on arrival.</summary>
    public void EnterNode(RunNode node)
    {
        if (!Reachable.Contains(node) || ActiveNodeId >= 0)
            return;
        ActiveNodeId = node.Id;
        Notice = null;
        EventResult = null;
        RecruitOffers.Clear();
        DockStock.Clear();
        if (node.Kind == RunNodeKind.Recruit)
            RollRecruits();
        if (node.Kind == RunNodeKind.Repair)
            RollDockStock(node);
        Save();
    }

    /// <summary>The fight at the active stop, if it has one.</summary>
    [JsonIgnore]
    public BattleMission ActiveMission =>
        ActiveNode?.BattleKind is RunNodeKind kind ? RunContent.BuildMission(ActiveNode, Sector, kind) : null;

    /// <summary>Pilots who can fly the next battle. If nobody is fit, the wounded fly anyway.</summary>
    public List<Pilot> Deployable()
    {
        List<Pilot> ready = Ready.ToList();
        return ready.Count > 0 ? ready : Living.ToList();
    }

    /// <summary>
    /// Saves a checkpoint at the start of the battle, then hands the squad and
    /// mission to the battle scene. Nothing is saved again until the battle
    /// ends, so this checkpoint is where a quit mid-battle returns to.
    /// </summary>
    public void LaunchBattle(List<Pilot> squad)
    {
        BattleMission mission = ActiveMission;
        if (mission == null || squad.Count == 0)
            return;
        BattleInProgress = true;
        BattleSquad = squad.Select(p => p.Callsign).ToList();
        Save();
        GameSetup.StartRunBattle(squad, mission);
    }

    /// <summary>
    /// Restarts the battle in progress from its beginning: the same squad,
    /// the same enemy wing and map (both are seeded by the stop), and pilots
    /// exactly as they were at launch. Returns false if there is none.
    /// </summary>
    public bool ResumeBattle()
    {
        BattleMission mission = ActiveMission;
        if (!BattleInProgress || mission == null)
        {
            BattleInProgress = false;
            return false;
        }
        List<Pilot> squad = BattleSquad
            .Select(callsign => Living.FirstOrDefault(p => p.Callsign == callsign))
            .Where(p => p != null)
            .ToList();
        if (squad.Count == 0)
            squad = Deployable().OrderByDescending(p => p.Hull / (float)p.MaxHull).Take(SquadLimit).ToList();
        if (squad.Count == 0)
        {
            BattleInProgress = false;
            return false;
        }
        GameSetup.StartRunBattle(squad, mission);
        return true;
    }

    /// <summary>Applies a finished battle to the run and saves.</summary>
    public (List<PilotResult> Results, RunBattleReport Report) ResolveBattle(bool won, IEnumerable<Fighter> playerFighters)
    {
        RunNode node = ActiveNode;
        RunNodeKind kind = node?.BattleKind ?? RunNodeKind.Skirmish;
        List<PilotResult> results = BattleResolution.Resolve(playerFighters, won);
        var report = new RunBattleReport();
        BattleInProgress = false;
        BattleSquad.Clear();

        TotalKills += results.Sum(r => r.Kills);
        if (won)
        {
            BattlesWon++;
            report.Salvage = RunContent.SalvageReward(kind, Sector) + (node?.BonusSalvage ?? 0);
            Salvage += report.Salvage;
        }

        RandomNumberGenerator rng = NextRng();
        foreach (PilotResult result in results.Where(r => r.Survived))
            for (int level = result.Pilot.Level - result.LevelsGained + 1; level <= result.Pilot.Level; level++)
                QueuePromotion(result.Pilot, $"REACHED LEVEL {level}", rng, report);
        if (won && kind == RunNodeKind.Elite)
        {
            List<PromotionCard> crate = RunContent.ModuleCrate(results.Where(r => r.Survived).Select(r => r.Pilot), rng);
            if (crate.Count > 0)
            {
                Promotions.Add(new PendingPromotion { Callsign = "", Reason = PendingPromotion.CrateReason, Cards = crate });
                report.Promotions.Add("ELITE WING · MODULE CRATE");
            }
        }

        if (kind == RunNodeKind.Boss)
        {
            CompleteActiveNode();
            if (won)
            {
                report.SectorCleared = true;
                AdvanceSector();
            }
            else
            {
                Outcome = RunOutcome.Defeat;
            }
        }
        else
        {
            CompleteActiveNode();
        }
        if (!Living.Any())
            Outcome = RunOutcome.Defeat;
        report.Outcome = Outcome;
        Save();
        return (results, report);
    }

    void QueuePromotion(Pilot pilot, string reason, RandomNumberGenerator rng, RunBattleReport report)
    {
        // A pilot whose promotion would be generated before an earlier one is
        // applied could be offered the same card twice; ChoosePromotion
        // rerolls later choices once an earlier one is made.
        List<PromotionCard> cards = RunContent.PromotionCards(pilot, rng);
        if (cards.Count == 0)
            return;
        Promotions.Add(new PendingPromotion { Callsign = pilot.Callsign, Reason = reason, Cards = cards });
        report.Promotions.Add($"{pilot.Callsign} · {reason}");
    }

    /// <summary>Applies the chosen card to the first waiting promotion.</summary>
    public void ChoosePromotion(int cardIndex)
    {
        if (Promotions.Count == 0)
            return;
        PendingPromotion promotion = Promotions[0];
        PromotionCard card = cardIndex >= 0 && cardIndex < promotion.Cards.Count ? promotion.Cards[cardIndex] : null;
        Pilot pilot = card == null ? null : PilotFor(promotion, card);
        if (pilot != null)
            RunContent.ApplyCard(pilot, card);
        Promotions.RemoveAt(0);
        // Later level-ups for the same pilot were rolled before this one;
        // reroll them so they never offer something the pilot now already has.
        RandomNumberGenerator rng = NextRng();
        if (pilot != null && !promotion.IsCrate)
            foreach (PendingPromotion later in Promotions.Where(p => p.Callsign == promotion.Callsign))
                later.Cards = RunContent.PromotionCards(pilot, rng);
        Promotions.RemoveAll(p => p.Cards.Count == 0);
        Save();
    }

    /// <summary>The pilot a card applies to: the promoted pilot, or the pilot a crate card names.</summary>
    public Pilot PilotFor(PendingPromotion promotion, PromotionCard card) =>
        Living.FirstOrDefault(p => p.Callsign == (promotion.IsCrate ? card.Callsign : promotion.Callsign));

    /// <summary>Finishes the active stop: marks it visited and lets wounded pilots recover a step.</summary>
    public void CompleteActiveNode()
    {
        RunNode node = ActiveNode;
        if (node == null)
            return;
        node.Visited = true;
        CurrentNodeId = node.Id;
        ActiveNodeId = -1;
        RecruitOffers.Clear();
        DockStock.Clear();
        EventResult = null;
        foreach (Pilot pilot in Living.Where(p => p.IsWounded))
        {
            pilot.RecoveryStops = Mathf.Max(0, pilot.RecoveryStops - 1);
            if (pilot.RecoveryStops == 0)
                pilot.Condition = PilotCondition.Ready;
        }
        if (!Living.Any())
            Outcome = RunOutcome.Defeat;
        Save();
    }

    /// <summary>
    /// Leaving a cleared sector: crews rest and every ship is patched halfway,
    /// then a new map. Full repairs are bought at docks.
    /// </summary>
    void AdvanceSector()
    {
        if (Sector >= RunContent.SectorCount)
        {
            Outcome = RunOutcome.Victory;
            return;
        }
        Sector++;
        foreach (Pilot pilot in Living)
        {
            pilot.Repair(Mathf.CeilToInt(pilot.HullDamage * SectorPatchFraction));
            pilot.Condition = PilotCondition.Ready;
            pilot.RecoveryStops = 0;
        }
        Nodes = RunContent.GenerateSector(Sector, Seed ^ (ulong)Sector * 0xD1B54A32D192ED03UL);
        CurrentNodeId = -1;
        ActiveNodeId = -1;
        Notice = $"SECTOR {Sector} · {SectorName}. Crews rested, ships patched halfway. {RunContent.SectorBriefings[Sector - 1]}";
    }

    // ------------------------------------------------------- repair dock

    public int RepairCost(Pilot pilot) => pilot.HullDamage * RepairCostPerHull;

    public bool Repair(Pilot pilot)
    {
        int cost = RepairCost(pilot);
        if (cost <= 0 || cost > Salvage)
            return false;
        Salvage -= cost;
        pilot.HullDamage = 0;
        Save();
        return true;
    }

    public bool TreatWound(Pilot pilot)
    {
        if (!pilot.IsWounded || Salvage < TreatWoundCost)
            return false;
        Salvage -= TreatWoundCost;
        pilot.Condition = PilotCondition.Ready;
        pilot.RecoveryStops = 0;
        Save();
        return true;
    }

    public bool TreatScar(Pilot pilot, Perk scar)
    {
        if (scar == null || !scar.IsScar || !pilot.Perks.Contains(scar) || Salvage < TreatScarCost)
            return false;
        Salvage -= TreatScarCost;
        pilot.Perks.Remove(scar);
        Save();
        return true;
    }

    /// <summary>Fits a module from the dock's stock, replacing whatever was in its slot.</summary>
    public bool BuyModule(Pilot pilot, ShipUpgrade module)
    {
        int cost = ShipUpgrades.Get(module).Cost;
        if (!DockStock.Contains(module) || !pilot.CanInstall(module) || Salvage < cost)
            return false;
        Salvage -= cost;
        pilot.InstallUpgrade(module);
        DockStock.Remove(module);
        Save();
        return true;
    }

    public bool BuyRefit(Pilot pilot, ShipType frame)
    {
        if (!pilot.CanRefit || Salvage < RefitCost || !pilot.Refit(frame))
            return false;
        Salvage -= RefitCost;
        Save();
        return true;
    }

    /// <summary>
    /// Stocks a dock with modules. At least one fits a slot someone in the
    /// squadron has empty, when there is such a slot.
    /// </summary>
    void RollDockStock(RunNode node)
    {
        var rng = new RandomNumberGenerator { Seed = node.Seed ^ 0xA24BAED4963EE407UL };
        List<ShipUpgrade> pool = ShipUpgrades.All.Select(module => module.Id).ToList();
        List<ShipUpgrade> wanted = pool.Where(module => Living.Any(p => p.HasSlot(ShipUpgrades.Get(module).Slot) &&
            p.ModuleIn(ShipUpgrades.Get(module).Slot) == null)).ToList();
        if (wanted.Count > 0)
        {
            ShipUpgrade first = wanted[rng.RandiRange(0, wanted.Count - 1)];
            DockStock.Add(first);
            pool.Remove(first);
        }
        while (DockStock.Count < DockStockSize && pool.Count > 0)
        {
            int index = rng.RandiRange(0, pool.Count - 1);
            DockStock.Add(pool[index]);
            pool.RemoveAt(index);
        }
    }

    // ------------------------------------------------------------ recruit

    void RollRecruits()
    {
        RandomNumberGenerator rng = NextRng();
        int level = Mathf.Max(1, (Living.Any() ? (int)Living.Average(p => p.Level) : 1) - 1);
        List<ShipType> classes = ShipTypes.RecruitableClasses.OrderBy(_ => rng.Randi()).Take(2).ToList();
        foreach (ShipType shipClass in classes)
        {
            Pilot recruit = RunContent.NewRecruit(TakeCallsign(), shipClass, level, rng);
            RecruitOffers.Add(new RecruitOffer { Pilot = PilotSave.From(recruit), Cost = RunContent.RecruitCost(level) });
        }
    }

    public bool Hire(RecruitOffer offer)
    {
        if (!RecruitOffers.Contains(offer) || Salvage < offer.Cost || Living.Count() >= RosterLimit)
            return false;
        Salvage -= offer.Cost;
        Pilots.Add(offer.Pilot.ToPilot());
        RecruitOffers.Remove(offer);
        Save();
        return true;
    }

    /// <summary>Adds a free pilot, used by the stranded-pilot event.</summary>
    public Pilot HireFreePilot(RandomNumberGenerator rng)
    {
        int level = Mathf.Max(1, (Living.Any() ? (int)Living.Average(p => p.Level) : 1) - 1);
        ShipType shipClass = ShipTypes.RecruitableClasses[rng.RandiRange(0, ShipTypes.RecruitableClasses.Length - 1)];
        Pilot pilot = RunContent.NewRecruit(TakeCallsign(), shipClass, level, rng);
        Pilots.Add(pilot);
        return pilot;
    }

    // -------------------------------------------------------------- events

    [JsonIgnore] public RunEvent ActiveEvent => ActiveNode?.Kind == RunNodeKind.Event ? RunContent.EventById(ActiveNode.EventId) : null;

    /// <summary>Resolves an event option. Options that lead to a fight turn the stop into a battle.</summary>
    public EventOutcome ChooseEventOption(int index)
    {
        RunEvent runEvent = ActiveEvent;
        if (runEvent == null || EventResult != null || index < 0 || index >= runEvent.Options.Length ||
            !runEvent.Options[index].Available(this))
            return null;
        EventOutcome outcome = runEvent.Options[index].Resolve(this, NextRng());
        if (outcome.Battle is RunNodeKind battle)
        {
            ActiveNode.EventBattle = battle;
            ActiveNode.BonusSalvage = outcome.BonusSalvage;
        }
        EventResult = outcome.Text;
        Save();
        return outcome;
    }
}
