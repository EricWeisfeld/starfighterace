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
    /// <summary>For an event's fight: the reason on the module crate a win opens, or null for none.</summary>
    public string CrateReward { get; set; }

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
    public string Callsign { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<PromotionCard> Cards { get; set; } = new();

    [JsonIgnore] public bool IsCrate => string.IsNullOrEmpty(Callsign);
}

/// <summary>
/// One pilot being set up at the start of a run: a ship picked from every
/// frame, and one instinct picked from three offered.
/// </summary>
public class DraftPilot
{
    public string Callsign { get; set; } = "";
    public string FrameId { get; set; } = "scout";
    public List<string> InstinctOffers { get; set; } = new();
    /// <summary>The chosen instinct's id, or null until one is picked.</summary>
    public string Instinct { get; set; }

    [JsonIgnore] public ShipType Frame => ShipTypes.FromId(FrameId);
}

/// <summary>Everything the debrief shows about a run battle's consequences.</summary>
public class RunBattleReport
{
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
        // Damage last: fitting armor above would otherwise clamp it.
        pilot.HullDamage = HullDamage;
        return pilot;
    }
}

/// <summary>
/// A roguelite run: three sectors of branching stops and a squadron of up to
/// five pilots. It starts by picking each pilot's ship and instinct. Hull
/// damage, scars and deaths carry from stop to stop. There is no currency:
/// pilots and their ships grow through level-ups, with module crates as bonus
/// rewards. The run saves after every change so a phone can close the app at
/// any moment.
/// </summary>
public class RunState
{
    /// <summary>Version 3: ships and instincts are picked at the start; frames are never earned or lost.</summary>
    public const int SaveVersion = 3;
    public const int RosterLimit = 5;
    public const int SquadLimit = 3;
    public const int StartingPilots = 3;
    public const int InstinctOffers = 3;
    /// <summary>Share of each ship's damage patched when a sector is cleared.</summary>
    public const float SectorPatchFraction = 0.5f;
    const string SavePath = "user://ace-star-pilot-run.json";

    public static RunState Current { get; private set; }

    public int Version { get; set; } = SaveVersion;
    public ulong Seed { get; set; }
    public int Sector { get; set; } = 1;
    /// <summary>The starting squadron being set up; empty once the run is under way.</summary>
    public List<DraftPilot> Draft { get; set; } = new();
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
    /// <summary>True once the active repair dock's medic has treated a scar; one per visit.</summary>
    public bool ScarTreated { get; set; }
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
    [JsonIgnore] public bool Drafting => Draft.Count > 0;
    [JsonIgnore] public IEnumerable<Pilot> Living => Pilots.Where(p => p.Alive);
    [JsonIgnore] public IEnumerable<Pilot> Fallen => Pilots.Where(p => !p.Alive);
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
        };
        // Each pilot is offered their own instincts, so no two offers overlap.
        RandomNumberGenerator rng = run.NextRng();
        List<Perk> instincts = Perks.Instincts.OrderBy(_ => rng.Randi()).ToList();
        ShipType[] defaults = { ShipTypes.Scout, ShipTypes.Raptor, ShipTypes.Zt };
        for (int i = 0; i < StartingPilots; i++)
            run.Draft.Add(new DraftPilot
            {
                Callsign = run.TakeCallsign(),
                FrameId = defaults[i % defaults.Length].Id,
                InstinctOffers = instincts.Skip(i * InstinctOffers).Take(InstinctOffers).Select(p => p.Id).ToList(),
            });
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

    // ------------------------------------------------------------- draft

    public void SetDraftFrame(int slot, ShipType frame)
    {
        if (slot < 0 || slot >= Draft.Count || !ShipTypes.PlayerFrames.Contains(frame))
            return;
        Draft[slot].FrameId = frame.Id;
        Save();
    }

    public void SetDraftInstinct(int slot, string instinctId)
    {
        if (slot < 0 || slot >= Draft.Count || !Draft[slot].InstinctOffers.Contains(instinctId))
            return;
        Draft[slot].Instinct = instinctId;
        Save();
    }

    [JsonIgnore] public bool DraftReady => Drafting && Draft.All(d => d.Instinct != null);

    /// <summary>Turns the finished draft into the starting squadron.</summary>
    public bool FinishDraft()
    {
        if (!DraftReady)
            return false;
        foreach (DraftPilot draft in Draft)
            Pilots.Add(RunContent.NewPilot(draft.Callsign, draft.Frame, Perks.ById(draft.Instinct)));
        Draft.Clear();
        Save();
        return true;
    }

    string TakeCallsign()
    {
        var used = new HashSet<string>(Pilots.Select(p => p.Callsign).Concat(Draft.Select(d => d.Callsign)));
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

    /// <summary>
    /// Travels to a stop. Recruit stops roll their candidates on arrival;
    /// repair docks fix every hull on arrival.
    /// </summary>
    public void EnterNode(RunNode node)
    {
        if (!Reachable.Contains(node) || ActiveNodeId >= 0)
            return;
        ActiveNodeId = node.Id;
        Notice = null;
        EventResult = null;
        RecruitOffers.Clear();
        ScarTreated = false;
        if (node.Kind == RunNodeKind.Recruit)
            RollRecruits();
        if (node.Kind == RunNodeKind.Repair)
            foreach (Pilot pilot in Living)
                pilot.HullDamage = 0;
        Save();
    }

    /// <summary>The fight at the active stop, if it has one.</summary>
    [JsonIgnore]
    public BattleMission ActiveMission =>
        ActiveNode?.BattleKind is RunNodeKind kind ? RunContent.BuildMission(ActiveNode, Sector, kind) : null;

    /// <summary>Pilots who can fly the next battle: every living pilot, whatever state their ship is in.</summary>
    public List<Pilot> Deployable() => Living.ToList();

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
            BattlesWon++;

        RandomNumberGenerator rng = NextRng();
        // A crate comes before the level-ups, which are rerolled around
        // whatever module it fits.
        string crateReason = kind == RunNodeKind.Elite ? "ELITE WING" : node?.CrateReward;
        if (won && crateReason != null && QueueCrate(crateReason, results.Where(r => r.Survived).Select(r => r.Pilot), rng))
            report.Promotions.Add($"{crateReason} · MODULE CRATE");
        foreach (PilotResult result in results.Where(r => r.Survived))
            for (int level = result.Pilot.Level - result.LevelsGained + 1; level <= result.Pilot.Level; level++)
                QueuePromotion(result.Pilot, $"REACHED LEVEL {level}", rng, report);

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

    void QueuePromotion(Pilot pilot, string reason, RandomNumberGenerator rng, RunBattleReport report = null)
    {
        // A pilot whose promotion would be generated before an earlier one is
        // applied could be offered the same card twice; ChoosePromotion
        // rerolls later choices once an earlier one is made.
        List<PromotionCard> cards = RunContent.PromotionCards(pilot, rng);
        if (cards.Count == 0)
            return;
        Promotions.Add(new PendingPromotion { Callsign = pilot.Callsign, Reason = reason, Cards = cards });
        report?.Promotions.Add($"{pilot.Callsign} · {reason}");
    }

    /// <summary>XP from outside a battle. Returns the levels gained; each queues a level-up.</summary>
    public int GrantXp(Pilot pilot, int amount, RandomNumberGenerator rng)
    {
        int gained = pilot.GrantXp(amount);
        for (int level = pilot.Level - gained + 1; level <= pilot.Level; level++)
            QueuePromotion(pilot, $"REACHED LEVEL {level}", rng);
        return gained;
    }

    /// <summary>
    /// Queues a module crate matched to these pilots' ships. The reason names
    /// where it came from. Returns false when no module fits any of them.
    /// </summary>
    public bool QueueCrate(string reason, IEnumerable<Pilot> pilots, RandomNumberGenerator rng)
    {
        List<PromotionCard> crate = RunContent.ModuleCrate(pilots, rng);
        if (crate.Count == 0)
            return false;
        Promotions.Add(new PendingPromotion { Callsign = "", Reason = reason, Cards = crate });
        return true;
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
        if (pilot != null)
            foreach (PendingPromotion later in Promotions.Where(p => !p.IsCrate && p.Callsign == pilot.Callsign))
                later.Cards = RunContent.PromotionCards(pilot, rng);
        Promotions.RemoveAll(p => p.Cards.Count == 0);
        Save();
    }

    /// <summary>The pilot a card applies to: the promoted pilot, or the pilot a crate card names.</summary>
    public Pilot PilotFor(PendingPromotion promotion, PromotionCard card) =>
        Living.FirstOrDefault(p => p.Callsign == (promotion.IsCrate ? card.Callsign : promotion.Callsign));

    /// <summary>Finishes the active stop and moves the squadron on.</summary>
    public void CompleteActiveNode()
    {
        RunNode node = ActiveNode;
        if (node == null)
            return;
        node.Visited = true;
        CurrentNodeId = node.Id;
        ActiveNodeId = -1;
        RecruitOffers.Clear();
        EventResult = null;
        if (!Living.Any())
            Outcome = RunOutcome.Defeat;
        Save();
    }

    /// <summary>
    /// Leaving a cleared sector: every ship is patched halfway, then a new
    /// map. Full repairs are at docks.
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
            pilot.Repair(Mathf.CeilToInt(pilot.HullDamage * SectorPatchFraction));
        Nodes = RunContent.GenerateSector(Sector, Seed ^ (ulong)Sector * 0xD1B54A32D192ED03UL);
        CurrentNodeId = -1;
        ActiveNodeId = -1;
        Notice = $"SECTOR {Sector} · {SectorName}. Ships patched halfway. {RunContent.SectorBriefings[Sector - 1]}";
    }

    // ------------------------------------------------------- repair dock

    /// <summary>The dock's medic treats one scar per visit.</summary>
    public bool TreatScar(Pilot pilot, Perk scar)
    {
        if (ActiveNode?.Kind != RunNodeKind.Repair || ScarTreated || scar == null || !scar.IsScar || !pilot.Perks.Contains(scar))
            return false;
        pilot.Perks.Remove(scar);
        ScarTreated = true;
        Save();
        return true;
    }

    // ------------------------------------------------------------ recruit

    void RollRecruits()
    {
        RandomNumberGenerator rng = NextRng();
        int level = Mathf.Max(1, (Living.Any() ? (int)Living.Average(p => p.Level) : 1) - 1);
        for (int i = 0; i < 2; i++)
            RecruitOffers.Add(new RecruitOffer { Pilot = PilotSave.From(RunContent.NewRecruit(TakeCallsign(), level, rng)) });
    }

    /// <summary>One candidate joins, which finishes the stop.</summary>
    public bool Hire(RecruitOffer offer)
    {
        if (!RecruitOffers.Contains(offer) || Living.Count() >= RosterLimit)
            return false;
        Pilot pilot = offer.Pilot.ToPilot();
        Pilots.Add(pilot);
        CompleteActiveNode();
        Notice = $"{pilot.Callsign} joins the squadron.";
        Save();
        return true;
    }

    /// <summary>Adds a free pilot, used by the stranded-pilot event.</summary>
    public Pilot HireFreePilot(RandomNumberGenerator rng)
    {
        int level = Mathf.Max(1, (Living.Any() ? (int)Living.Average(p => p.Level) : 1) - 1);
        Pilot pilot = RunContent.NewRecruit(TakeCallsign(), level, rng);
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
            ActiveNode.CrateReward = outcome.CrateReward;
        }
        EventResult = outcome.Text;
        Save();
        return outcome;
    }
}
