using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A persistent squadron member: flies the same hull class across the whole
/// campaign, gains experience and perks between battles, and can die for good.
/// </summary>
public class Pilot
{
    public string Callsign;
    /// <summary>The pilot's permanent ship class.</summary>
    public ShipType Ship { get; private set; }
    public string ClassId { get; }
    /// <summary>The maneuvers currently equipped from this class's shared pool.</summary>
    public readonly List<ShipAbility> Maneuvers = new();
    /// <summary>Purchased modules permanently installed in this pilot's current airframe.</summary>
    public readonly List<ShipUpgrade> Upgrades = new();
    public int Level = 1;
    public int Xp;                          // progress within the current level
    public readonly List<Perk> Perks = new();
    public PilotCondition Condition = PilotCondition.Ready;
    public int RecoveryMissionsRemaining;
    /// <summary>Unrepaired hull points carried between missions.</summary>
    public int HullDamage;
    public int CareerKills;
    public int Missions;

    public bool Alive => Condition != PilotCondition.KIA;
    public bool CanDeploy => Condition == PilotCondition.Ready && !NeedsHullChoice;
    public bool IsRaptor => ClassId == "raptor";
    public bool IsScout => ClassId == "scout";
    public bool IsZt => ClassId == "zt";
    public int ManeuverSlots => Ship.ManeuverSlotsAtLevel(Level);
    public bool NeedsManeuverChoice => Maneuvers.Count < ManeuverSlots;
    public bool NeedsHullChoice => Level >= 3 && Ship == ShipTypes.BaseClass(ClassId);
    public bool NeedsCareerChoice => NeedsHullChoice || NeedsManeuverChoice;
    public string ClassDisplayName => IsRaptor ? "Raptor Class" : IsZt ? "ZT Class" : "Scout Class";
    public string CareerDisplayName => $"{Ship.DisplayName} · {ClassDisplayName}";

    public Pilot(string callsign, ShipType ship)
    {
        Callsign = callsign;
        Ship = ship;
        ClassId = ShipTypes.ClassIdForHull(ship.Id);
    }

    public bool HasManeuver(ShipAbility ability) => Maneuvers.Contains(ability);

    /// <summary>Equips an available class maneuver, provided a learned slot is open.</summary>
    public bool EquipManeuver(ShipAbility ability)
    {
        if (!Ship.OffersManeuver(ability) || HasManeuver(ability) || Maneuvers.Count >= ManeuverSlots)
            return false;
        Maneuvers.Add(ability);
        return true;
    }

    /// <summary>Unequips a maneuver so the player can install a different pool option.</summary>
    public bool UnequipManeuver(ShipAbility ability)
    {
        return Maneuvers.Remove(ability);
    }

    public void RestoreManeuvers(IEnumerable<ShipAbility> maneuvers)
    {
        Maneuvers.Clear();
        foreach (ShipAbility ability in maneuvers.Where(Ship.OffersManeuver).Distinct().Take(ManeuverSlots))
            Maneuvers.Add(ability);
    }

    public bool ChooseHull(ShipType hull)
    {
        if (!NeedsHullChoice || !ShipTypes.HullBranches(ClassId).Contains(hull))
            return false;
        Ship = hull;
        RestoreManeuvers(Maneuvers.ToArray());
        return true;
    }

    public bool HasUpgrade(ShipUpgrade upgrade) => Upgrades.Contains(upgrade);

    public bool CanInstallUpgrade(ShipUpgrade upgrade)
    {
        ShipUpgradeSlot slot = ShipUpgrades.Get(upgrade).Slot;
        int available = Ship.UpgradeSlots.Count(candidate => candidate == slot);
        int occupied = Upgrades.Count(installed => ShipUpgrades.Get(installed).Slot == slot);
        return !HasUpgrade(upgrade) && occupied < available;
    }

    public bool InstallUpgrade(ShipUpgrade upgrade)
    {
        if (!CanInstallUpgrade(upgrade))
            return false;
        Upgrades.Add(upgrade);
        return true;
    }

    public ShipUpgrade? UpgradeInSlot(ShipUpgradeSlot slot)
    {
        foreach (ShipUpgrade upgrade in Upgrades)
            if (ShipUpgrades.Get(upgrade).Slot == slot)
                return upgrade;
        return null;
    }

    public void RestoreUpgrades(IEnumerable<ShipUpgrade> upgrades)
    {
        Upgrades.Clear();
        foreach (ShipUpgrade upgrade in upgrades.Distinct())
            InstallUpgrade(upgrade);
    }

}

public enum PilotCondition
{
    Ready,
    Wounded,
    KIA,
}

/// <summary>The squadron roster; persists for the play session like CampaignData.</summary>
public static class PilotRoster
{
    public const int XpPerLevel = 100;
    // The highest class track currently ends at level 6. Keeping this value
    // explicit gives sandbox battles a stable "all abilities unlocked" level.
    public const int MaxLevel = 6;

    static readonly string[] Callsigns =
    {
        "VIPER", "GHOST", "NOVA", "RAZOR", "ECHO", "JINX",
        "MAVERICK", "WRAITH", "COMET", "HAVOC", "SABLE", "FLINT",
        "ORION", "TALON", "DUSK", "ZEPHYR", "IRONSIDE", "PIXIE",
    };
    static int _nextCallsign;
    static List<Pilot> _pilots;

    public static List<Pilot> Pilots => _pilots ??= CreateStartingRoster();
    public static IEnumerable<Pilot> Living => Pilots.Where(p => p.Alive);

    /// <summary>Average level of living pilots, rounded down for recruitment.</summary>
    public static int AverageLivingLevel
    {
        get
        {
            Pilot[] living = Living.ToArray();
            return living.Length == 0 ? 1 : Mathf.Max(1, living.Sum(pilot => pilot.Level) / living.Length);
        }
    }

    /// <summary>Level offered to recruits: one below the living fleet average.</summary>
    public static int RecruitmentLevel => Mathf.Max(1, AverageLivingLevel - 1);

    static List<Pilot> CreateStartingRoster()
    {
        _pilots = new List<Pilot>();
        ShipType[] startingClasses =
        {
            ShipTypes.Scout,
            ShipTypes.Raptor,
            ShipTypes.Zt,
        };
        foreach (ShipType shipClass in startingClasses)
            Recruit(shipClass);
        return _pilots;
    }

    /// <summary>Adds a pilot at the requested level, optionally rolling a recruitment trait.</summary>
    public static Pilot Recruit(ShipType ship, int level = 1, bool rollRecruitmentTrait = false)
    {
        string name = Callsigns[_nextCallsign % Callsigns.Length];
        if (_nextCallsign >= Callsigns.Length)
            name += $" {_nextCallsign / Callsigns.Length + 1}";
        _nextCallsign++;
        var pilot = new Pilot(name, ship) { Level = Mathf.Max(1, level) };
        (_pilots ??= new List<Pilot>()).Add(pilot);
        if (rollRecruitmentTrait)
            Perks.RollRecruitmentTrait(pilot);
        return pilot;
    }

    public static void ResetCampaign()
    {
        _nextCallsign = 0;
        _pilots = null;
    }

    public static void SaveTo(ConfigFile save)
    {
        List<Pilot> pilots = Pilots;
        save.SetValue("pilot", "count", pilots.Count);
        save.SetValue("pilot", "next_callsign", _nextCallsign);
        for (int i = 0; i < pilots.Count; i++)
        {
            Pilot pilot = pilots[i];
            string key = i.ToString();
            save.SetValue("pilot", $"{key}_callsign", pilot.Callsign);
            save.SetValue("pilot", $"{key}_class", pilot.ClassId);
            save.SetValue("pilot", $"{key}_ship", pilot.Ship.Id);
            save.SetValue("pilot", $"{key}_maneuvers", string.Join(',', pilot.Maneuvers));
            save.SetValue("pilot", $"{key}_ship_upgrades", string.Join(',', pilot.Upgrades));
            save.SetValue("pilot", $"{key}_level", pilot.Level);
            save.SetValue("pilot", $"{key}_xp", pilot.Xp);
            save.SetValue("pilot", $"{key}_condition", (int)pilot.Condition);
            save.SetValue("pilot", $"{key}_recovery", pilot.RecoveryMissionsRemaining);
            save.SetValue("pilot", $"{key}_hull_damage", pilot.HullDamage);
            save.SetValue("pilot", $"{key}_kills", pilot.CareerKills);
            save.SetValue("pilot", $"{key}_missions", pilot.Missions);
            save.SetValue("pilot", $"{key}_perks", string.Join(',', pilot.Perks.Select(perk => perk.Id)));
        }
    }

    public static void LoadFrom(ConfigFile save)
    {
        int count = (int)save.GetValue("pilot", "count", 0);
        if (count <= 0) return;
        _pilots = new List<Pilot>();
        _nextCallsign = (int)save.GetValue("pilot", "next_callsign", count);
        for (int i = 0; i < count; i++)
        {
            string key = i.ToString();
            string callsign = (string)save.GetValue("pilot", $"{key}_callsign", $"PILOT {i + 1}");
            string classId = (string)save.GetValue("pilot", $"{key}_class", "scout");
            classId = ShipTypes.ClassIdForHull(classId);
            ShipType baseClass = ShipTypes.BaseClass(classId);
            string savedHullId = (string)save.GetValue("pilot", $"{key}_ship", baseClass.Id);
            ShipType savedHull = ShipTypes.FromId(savedHullId);
            if (ShipTypes.ClassIdForHull(savedHull.Id) != ShipTypes.ClassIdForHull(baseClass.Id))
                savedHull = baseClass;
            var pilot = new Pilot(callsign, savedHull)
            {
                Level = (int)save.GetValue("pilot", $"{key}_level", 1),
                Xp = (int)save.GetValue("pilot", $"{key}_xp", 0),
                Condition = (PilotCondition)(int)save.GetValue("pilot", $"{key}_condition", (int)PilotCondition.Ready),
                RecoveryMissionsRemaining = (int)save.GetValue("pilot", $"{key}_recovery", 0),
                HullDamage = (int)save.GetValue("pilot", $"{key}_hull_damage", 0),
                CareerKills = (int)save.GetValue("pilot", $"{key}_kills", 0),
                Missions = (int)save.GetValue("pilot", $"{key}_missions", 0),
            };
            string maneuvers = (string)save.GetValue("pilot", $"{key}_maneuvers", "");
            if (!string.IsNullOrEmpty(maneuvers))
            {
                pilot.RestoreManeuvers(maneuvers.Split(',', System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => System.Enum.TryParse(value, out ShipAbility ability) ? (ShipAbility?)ability : null)
                    .Where(ability => ability.HasValue).Select(ability => ability.Value));
            }
            else
            {
                // Existing campaigns used permanent frames. Preserve their learned moves
                // while moving the pilot back to the new class-wide maneuver pool.
                ShipType legacyShip = ShipTypes.FromId((string)save.GetValue("pilot", $"{key}_ship", baseClass.Id));
                pilot.RestoreManeuvers(legacyShip.ManeuverPool);
            }
            string upgrades = (string)save.GetValue("pilot", $"{key}_ship_upgrades", "");
            pilot.RestoreUpgrades(upgrades.Split(',', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(value => ShipUpgrades.TryParse(value, out ShipUpgrade upgrade) ? (ShipUpgrade?)upgrade : null)
                .Where(upgrade => upgrade.HasValue).Select(upgrade => upgrade.Value));
            string perks = (string)save.GetValue("pilot", $"{key}_perks", "");
            foreach (string perkId in perks.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
            {
                Perk perk = Perks.All.FirstOrDefault(candidate => candidate.Id == perkId);
                if (perk != null) pilot.Perks.Add(perk);
            }
            _pilots.Add(pilot);
        }
    }

    /// <summary>XP needed to go from <paramref name="level"/> to the next.</summary>
    public static int XpToNext(int level) => XpPerLevel;

    /// <summary>Award XP, applying any level-ups. Returns levels gained.</summary>
    public static int GrantXp(Pilot pilot, int amount)
    {
        pilot.Xp += amount;
        int gained = 0;
        while (pilot.Xp >= XpToNext(pilot.Level))
        {
            pilot.Xp -= XpToNext(pilot.Level);
            pilot.Level++;
            gained++;
        }
        return gained;
    }

    /// <summary>Advance recovery for pilots who sat out the mission.</summary>
    public static void AdvanceRecovery(IReadOnlyCollection<Pilot> deployed)
    {
        foreach (Pilot pilot in Living.Where(p => !deployed.Contains(p)))
        {
            if (pilot.Condition == PilotCondition.Wounded)
            {
                pilot.RecoveryMissionsRemaining = Mathf.Max(0, pilot.RecoveryMissionsRemaining - 1);
                if (pilot.RecoveryMissionsRemaining == 0)
                    pilot.Condition = PilotCondition.Ready;
            }
        }
    }

    /// <summary>Repairs the current campaign fraction of every living pilot's effective maximum hull after a mission.</summary>
    public static void RepairHullsAfterMission()
    {
        foreach (Pilot pilot in Living)
        {
            int maxHp = global::Perks.EffectiveMaxHp(pilot, pilot.Ship.MaxHp);
            pilot.HullDamage = Mathf.Max(0, pilot.HullDamage - Mathf.CeilToInt(maxHp * CampaignData.HullRepairFraction));
        }
    }
}

/// <summary>What happened to one pilot in the battle that just ended.</summary>
public class PilotResult
{
    public Pilot Pilot;
    public bool Survived;
    public bool Ejected;
    public int Kills;
    public int XpGained;
    public int LevelsGained;
    public Perk NewPerk;
    public int ManeuverSlotsGained;
}

/// <summary>
/// Applies the campaign consequences of a finished battle: eject survival,
/// permadeath, recovery, XP, level-ups and perk rolls. Call exactly once.
/// </summary>
public static class BattleResolution
{
    public const int XpBase = 40;
    public const int XpPerKill = 25;
    public const int XpWinBonus = 50;
    public const float LossEjectSurvival = 0.5f;
    public const int WoundedRecoveryMissions = 3;

    public static List<PilotResult> Resolve(IEnumerable<Fighter> playerFighters, bool won,
        IReadOnlyCollection<Fighter> combatants = null)
    {
        List<Fighter> fighters = playerFighters.Where(f => f.Pilot != null).ToList();
        List<Pilot> deployed = fighters.Select(f => f.Pilot).ToList();
        var results = new List<PilotResult>();
        var resultsByFighter = new Dictionary<Fighter, PilotResult>();

        // Resolve every fate first so surviving pilots' perk rolls can react to
        // the actual number of squadmates lost, including failed rescues.
        foreach (Fighter f in fighters)
        {
            var r = new PilotResult { Pilot = f.Pilot, Ejected = f.Ejected, Kills = f.Kills };
            // Winning recovers every ejected pilot; losing gives each a coin flip.
            r.Survived = f.IsAlive || (f.Ejected && (won || GD.Randf() < LossEjectSurvival));
            if (!r.Survived)
            {
                f.Pilot.Condition = PilotCondition.KIA;
                f.Pilot.RecoveryMissionsRemaining = 0;
                f.Pilot.HullDamage = 0;
            }
            else
            {
                // A ship that made it home keeps its damage; a shot-down pilot
                // returns in a replacement hull after recovering from injuries.
                f.Pilot.HullDamage = f.Ejected ? 0 : Mathf.Max(0, f.MaxHp - f.Hp);
                f.Pilot.Condition = f.Ejected ? PilotCondition.Wounded : PilotCondition.Ready;
                f.Pilot.RecoveryMissionsRemaining = f.Ejected ? WoundedRecoveryMissions : 0;
            }
            results.Add(r);
            resultsByFighter[f] = r;
        }

        int squadDeaths = results.Count(result => !result.Survived);
        foreach (Fighter f in fighters)
        {
            PilotResult r = resultsByFighter[f];
            if (!r.Survived)
                continue;

            int previousSlots = f.Pilot.ManeuverSlots;
            r.XpGained = XpBase + XpPerKill * f.Kills + (won ? XpWinBonus : 0);
            r.LevelsGained = PilotRoster.GrantXp(f.Pilot, r.XpGained);
            r.ManeuverSlotsGained = f.Pilot.ManeuverSlots - previousSlots;
            f.Pilot.CareerKills += f.Kills;
            f.Pilot.Missions++;
            r.NewPerk = Perks.Roll(f, won, combatants ?? fighters, squadDeaths);
        }
        PilotRoster.AdvanceRecovery(deployed);
        PilotRoster.RepairHullsAfterMission();
        return results;
    }
}
