using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A squadron member for one run. Levelling up grows both the pilot
/// (maneuvers, masteries, instincts) and their ship (modules, a refit frame).
/// Hull damage carries between battles, and a pilot can die for good.
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
    /// <summary>Modules fitted to the ship: at most one per slot the frame provides.</summary>
    public readonly List<ShipUpgrade> Upgrades = new();
    /// <summary>Known maneuvers the pilot has mastered.</summary>
    public readonly List<ShipAbility> Masteries = new();
    public int Level = 1;
    public int Xp;                          // progress within the current level
    /// <summary>Instincts and scars.</summary>
    public readonly List<Perk> Perks = new();
    public PilotCondition Condition = PilotCondition.Ready;
    /// <summary>Unrepaired hull points carried between battles. A ship always keeps at least 1 hull.</summary>
    public int HullDamage;
    public int Kills;
    public int Battles;

    public bool Alive => Condition != PilotCondition.KIA;
    public bool IsMaxLevel => Level >= MaxLevel;
    public ShipType BaseClass => ShipTypes.BaseClass(ClassId);
    public bool CanRefit => Level >= RefitLevel && Ship == BaseClass;
    /// <summary>Maximum hull: the frame's, plus armor plating.</summary>
    public int MaxHull => Ship.MaxHp + (HasUpgrade(ShipUpgrade.ShieldsArmor) ? ShipUpgrades.ArmorHullBonus : 0);
    /// <summary>Hull the ship will launch with. Shields always launch full.</summary>
    public int Hull => Mathf.Clamp(MaxHull - HullDamage, 1, MaxHull);
    public IEnumerable<ShipAbility> UnlearnedManeuvers => Ship.ManeuverPool.Where(ability => !Maneuvers.Contains(ability));
    public IEnumerable<ShipAbility> UnmasteredManeuvers => Maneuvers.Where(ability => !Masteries.Contains(ability));
    public IEnumerable<Perk> Instincts => Perks.Where(perk => perk.Positive);
    public IEnumerable<Perk> Scars => Perks.Where(perk => perk.IsScar);
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
        Masteries.RemoveAll(ability => !Maneuvers.Contains(ability));
    }

    public bool HasMastered(ShipAbility ability) => Masteries.Contains(ability);

    public bool Master(ShipAbility ability)
    {
        if (!HasManeuver(ability) || HasMastered(ability))
            return false;
        Masteries.Add(ability);
        return true;
    }

    /// <summary>
    /// Moves the pilot into one of their class's refit frames. Modules move
    /// across to the new frame when it has a slot for them.
    /// </summary>
    public bool Refit(ShipType frame)
    {
        if (!CanRefit || !ShipTypes.HullBranches(ClassId).Contains(frame))
            return false;
        Ship = frame;
        Upgrades.RemoveAll(upgrade => !HasSlot(ShipUpgrades.Get(upgrade).Slot));
        HullDamage = Mathf.Min(HullDamage, MaxHull - 1);
        RestoreManeuvers(Maneuvers.ToArray());
        return true;
    }

    /// <summary>
    /// The ship was shot down but towed home: frame and modules intact, hull
    /// down to 1. It can still fly, on its shields, until it is repaired.
    /// </summary>
    public void RecoverWreck() => HullDamage = MaxHull - 1;

    /// <summary>
    /// The ship was left behind. The pilot flies a new, bare base frame of
    /// their class; what they have learned stays with them. Returns what was
    /// lost.
    /// </summary>
    public (ShipType Frame, List<ShipUpgrade> Modules) LoseShip()
    {
        (ShipType Frame, List<ShipUpgrade> Modules) lost = (Ship, Upgrades.ToList());
        Ship = BaseClass;
        Upgrades.Clear();
        HullDamage = 0;
        RestoreManeuvers(Maneuvers.ToArray());
        return lost;
    }

    /// <summary>Hull damage from outside a battle (events). It never takes a ship below 1 hull.</summary>
    public void TakeHullDamage(int amount) => HullDamage = Mathf.Min(MaxHull - 1, HullDamage + amount);

    public bool HasSlot(ShipUpgradeSlot slot) => Ship.UpgradeSlots.Contains(slot);
    public bool HasUpgrade(ShipUpgrade upgrade) => Upgrades.Contains(upgrade);

    /// <summary>The module in a slot, or null when it is empty.</summary>
    public ShipUpgrade? ModuleIn(ShipUpgradeSlot slot) =>
        Upgrades.Select(upgrade => (ShipUpgrade?)upgrade).FirstOrDefault(upgrade => ShipUpgrades.Get(upgrade.Value).Slot == slot);

    /// <summary>True when the frame has the module's slot and the module is not already fitted.</summary>
    public bool CanInstall(ShipUpgrade upgrade) => HasSlot(ShipUpgrades.Get(upgrade).Slot) && !HasUpgrade(upgrade);

    /// <summary>Fits a module, replacing whatever was in its slot.</summary>
    public bool InstallUpgrade(ShipUpgrade upgrade)
    {
        if (!CanInstall(upgrade))
            return false;
        ShipUpgradeSlot slot = ShipUpgrades.Get(upgrade).Slot;
        Upgrades.RemoveAll(existing => ShipUpgrades.Get(existing).Slot == slot);
        Upgrades.Add(upgrade);
        HullDamage = Mathf.Min(HullDamage, MaxHull - 1);
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
    /// <summary>Retired: only found in saves from before wounds were removed. Loads as Ready.</summary>
    Wounded,
    KIA,
}

/// <summary>What happened to one pilot in the battle that just ended.</summary>
public class PilotResult
{
    public Pilot Pilot;
    public bool Survived;
    public bool Ejected;
    /// <summary>Shot down in a win: the ship came home as a wreck at 1 hull.</summary>
    public bool WreckRecovered;
    /// <summary>Shot down in a loss: the ship was left behind.</summary>
    public bool ShipLost;
    /// <summary>For a lost ship: its frame and the modules that went with it.</summary>
    public ShipType LostFrame;
    public List<ShipUpgrade> LostModules = new();
    public int Kills;
    public int XpGained;
    public int LevelsGained;
    public Perk NewScar;
}

/// <summary>
/// Applies the consequences of a finished battle to the pilots who flew it:
/// eject survival, permadeath, hull damage, wrecks and lost ships,
/// XP, level-ups and scar rolls. Call exactly once per battle.
/// </summary>
public static class BattleResolution
{
    public const int XpBase = 40;
    public const int XpPerKill = 25;
    public const int XpWinBonus = 50;
    public const float LossEjectSurvival = 0.5f;

    public static List<PilotResult> Resolve(IEnumerable<Fighter> playerFighters, bool won)
    {
        List<Fighter> fighters = playerFighters.Where(f => f.Pilot != null).ToList();
        var results = new List<PilotResult>();
        var resultsByFighter = new Dictionary<Fighter, PilotResult>();

        // Resolve every fate first so surviving pilots' scar rolls can react to
        // the actual number of squadmates lost, including failed rescues.
        foreach (Fighter f in fighters)
        {
            var r = new PilotResult { Pilot = f.Pilot, Ejected = f.Ejected, Kills = f.Kills };
            // Winning recovers every ejected pilot; losing gives each a coin flip.
            r.Survived = f.IsAlive || (f.Ejected && (won || GD.Randf() < LossEjectSurvival));
            if (!r.Survived)
            {
                f.Pilot.Condition = PilotCondition.KIA;
                f.Pilot.HullDamage = 0;
            }
            else
            {
                // Being shot down costs the ship (and risks a scar); the
                // pilot is fit to fly the next battle.
                if (!f.Ejected)
                {
                    // A ship that made it home keeps its damage.
                    f.Pilot.HullDamage = Mathf.Max(0, f.MaxHp - f.Hp);
                }
                else if (won)
                {
                    // Holding the field means towing the wreck home.
                    f.Pilot.RecoverWreck();
                    r.WreckRecovered = true;
                }
                else
                {
                    // A loss or retreat leaves the wreck behind.
                    (r.LostFrame, r.LostModules) = f.Pilot.LoseShip();
                    r.ShipLost = true;
                }
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
            r.NewScar = Perks.RollScar(f, won, squadDeaths);
        }
        return results;
    }
}
