using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A squadron member for one run: flies one hull class, learns maneuvers and
/// upgrades as they level, carries hull damage between battles, and can die
/// for good.
/// </summary>
public class Pilot
{
    public const int MaxManeuvers = 3;
    public const int MaxLevel = 6;
    /// <summary>A pilot can refit into one of their class's two frames from this level.</summary>
    public const int RefitLevel = 3;

    public string Callsign;
    /// <summary>The pilot's current hull: their class's base frame or one of its refits.</summary>
    public ShipType Ship { get; private set; }
    public string ClassId { get; }
    /// <summary>Maneuvers learned from this class's pool, at most <see cref="MaxManeuvers"/>.</summary>
    public readonly List<ShipAbility> Maneuvers = new();
    /// <summary>Ship upgrades installed through promotions and field rewards; each at most once.</summary>
    public readonly List<ShipUpgrade> Upgrades = new();
    public int Level = 1;
    public int Xp;                          // progress within the current level
    public readonly List<Perk> Perks = new();
    public PilotCondition Condition = PilotCondition.Ready;
    /// <summary>Run stops a wounded pilot still has to sit out.</summary>
    public int RecoveryStops;
    /// <summary>Unrepaired hull points carried between battles.</summary>
    public int HullDamage;
    public int Kills;
    public int Battles;

    public bool Alive => Condition != PilotCondition.KIA;
    public bool CanDeploy => Condition == PilotCondition.Ready;
    public bool IsWounded => Condition == PilotCondition.Wounded;
    public bool IsMaxLevel => Level >= MaxLevel;
    public ShipType BaseClass => ShipTypes.BaseClass(ClassId);
    public bool CanRefit => Level >= RefitLevel && Ship == BaseClass;
    /// <summary>Maximum hull after traits.</summary>
    public int MaxHull => global::Perks.EffectiveMaxHp(this, Ship.MaxHp);
    /// <summary>Hull the ship will launch with.</summary>
    public int Hull => Mathf.Clamp(MaxHull - HullDamage, 1, MaxHull);
    public IEnumerable<ShipAbility> UnlearnedManeuvers => Ship.ManeuverPool.Where(ability => !Maneuvers.Contains(ability));
    public string ClassDisplayName => ClassId switch { "raptor" => "Raptor", "zt" => "ZT", _ => "Scout" };

    public Pilot(string callsign, ShipType ship)
    {
        Callsign = callsign;
        Ship = ship;
        ClassId = ShipTypes.ClassIdForHull(ship.Id);
    }

    public bool HasManeuver(ShipAbility ability) => Maneuvers.Contains(ability);

    public bool LearnManeuver(ShipAbility ability)
    {
        if (!Ship.OffersManeuver(ability) || HasManeuver(ability) || Maneuvers.Count >= MaxManeuvers)
            return false;
        Maneuvers.Add(ability);
        return true;
    }

    public void RestoreManeuvers(IEnumerable<ShipAbility> maneuvers)
    {
        Maneuvers.Clear();
        foreach (ShipAbility ability in maneuvers.Where(Ship.OffersManeuver).Distinct().Take(MaxManeuvers))
            Maneuvers.Add(ability);
    }

    /// <summary>Moves the pilot into one of their class's refit frames.</summary>
    public bool Refit(ShipType frame)
    {
        if (!CanRefit || !ShipTypes.HullBranches(ClassId).Contains(frame))
            return false;
        Ship = frame;
        HullDamage = Mathf.Min(HullDamage, MaxHull - 1);
        RestoreManeuvers(Maneuvers.ToArray());
        return true;
    }

    public bool HasUpgrade(ShipUpgrade upgrade) => Upgrades.Contains(upgrade);

    public bool InstallUpgrade(ShipUpgrade upgrade)
    {
        if (HasUpgrade(upgrade))
            return false;
        Upgrades.Add(upgrade);
        return true;
    }

    public void Repair(int hull) => HullDamage = Mathf.Max(0, HullDamage - hull);

    /// <summary>XP needed to go from <paramref name="level"/> to the next; each level asks a little more.</summary>
    public static int XpToNext(int level) => level >= MaxLevel ? 0 : 50 + 50 * level;

    /// <summary>Awards XP and applies level-ups. Returns the number of levels gained.</summary>
    public int GrantXp(int amount)
    {
        if (IsMaxLevel)
            return 0;
        Xp += amount;
        int gained = 0;
        while (!IsMaxLevel && Xp >= XpToNext(Level))
        {
            Xp -= XpToNext(Level);
            Level++;
            gained++;
        }
        if (IsMaxLevel)
            Xp = 0;
        return gained;
    }
}

public enum PilotCondition
{
    Ready,
    Wounded,
    KIA,
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
}

/// <summary>
/// Applies the consequences of a finished battle to the pilots who flew it:
/// eject survival, permadeath, wounds, hull damage, XP, level-ups and perk
/// rolls. Call exactly once per battle.
/// </summary>
public static class BattleResolution
{
    public const int XpBase = 40;
    public const int XpPerKill = 25;
    public const int XpWinBonus = 50;
    public const float LossEjectSurvival = 0.5f;
    /// <summary>
    /// Stops a newly wounded pilot waits out. The stop they were wounded on
    /// counts as one, so this means "sits out the next stop".
    /// </summary>
    public const int WoundedRecoveryStops = 2;

    public static List<PilotResult> Resolve(IEnumerable<Fighter> playerFighters, bool won,
        IReadOnlyCollection<Fighter> combatants = null)
    {
        List<Fighter> fighters = playerFighters.Where(f => f.Pilot != null).ToList();
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
                f.Pilot.RecoveryStops = 0;
                f.Pilot.HullDamage = 0;
            }
            else
            {
                // A ship that made it home keeps its damage; a shot-down pilot
                // returns in a replacement hull after recovering from injuries.
                f.Pilot.HullDamage = f.Ejected ? 0 : Mathf.Max(0, f.MaxHp - f.Hp);
                f.Pilot.Condition = f.Ejected ? PilotCondition.Wounded : PilotCondition.Ready;
                f.Pilot.RecoveryStops = f.Ejected ? WoundedRecoveryStops : 0;
            }
            results.Add(r);
            resultsByFighter[f] = r;
        }

        int squadDeaths = results.Count(result => !result.Survived);
        foreach (Fighter f in fighters)
        {
            PilotResult r = resultsByFighter[f];
            f.Pilot.Kills += f.Kills;
            if (!r.Survived)
                continue;

            r.XpGained = XpBase + XpPerKill * f.Kills + (won ? XpWinBonus : 0);
            r.LevelsGained = f.Pilot.GrantXp(r.XpGained);
            f.Pilot.Battles++;
            r.NewPerk = Perks.Roll(f, won, combatants ?? fighters, squadDeaths);
        }
        return results;
    }
}
