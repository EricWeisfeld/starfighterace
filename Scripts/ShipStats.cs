using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Hull refits earned by levelling: Mk I at level 1, then Mk II, III and IV
/// at levels 2, 4 and 6. Each refit raises hull, shields, shield regen and
/// shot damage by a fifth (compounding) and adds a little accuracy and
/// evasion, so a pilot's ship keeps pace with larger enemy wings.
/// </summary>
public static class Refits
{
    public static readonly int[] Levels = { 2, 4, 6 };
    /// <summary>Hull, shields, regen and damage gained per refit, compounding.</summary>
    public const float CoreBonus = 0.20f;
    public const float AccuracyBonus = 0.02f;
    public const float EvasionBonus = 0.01f;

    /// <summary>Refits earned by this level: 0 (Mk I) to 3 (Mk IV).</summary>
    public static int TierFor(int level) => Levels.Count(refitLevel => level >= refitLevel);

    public static bool IsRefitLevel(int level) => Levels.Contains(level);

    public static string Name(int tier) => tier switch { 0 => "MK I", 1 => "MK II", 2 => "MK III", _ => "MK IV" };

    /// <summary>The core-stat multiplier after this many refits.</summary>
    public static float Multiplier(int tier) => Mathf.Pow(1f + CoreBonus, tier);

    /// <summary>What one refit adds, for level-up and pilot pages.</summary>
    public static string Summary =>
        $"Hull, shields, regen and damage +{CoreBonus * 100:0}%, accuracy +{AccuracyBonus * 100:0}%, evasion +{EvasionBonus * 100:0}%.";
}

/// <summary>
/// A ship's always-on numbers: its frame's, refitted to its pilot's level,
/// with its modules fitted. The one place these effects are worked out, so
/// the fighter in battle and the pilot pages always agree. Instincts, scars
/// and maneuvers are situational and are not part of these numbers.
/// </summary>
public readonly record struct ShipStats(
    int MaxHull,
    int MaxShield,
    int ShieldRegen,
    int ShotDamage,
    int ShotsMin,
    int ShotsMax,
    float Accuracy,
    float Evasion,
    /// <summary>Half-angle of the forward firing cone.</summary>
    float FireConeDeg,
    float TurnDeg,
    float MinMove,
    float MaxMove)
{
    public const int BaseShotsMin = 3;
    public const int BaseShotsMax = 5;

    public static ShipStats Frame(ShipType frame) => new(
        frame.MaxHp, frame.MaxShield, frame.ShieldRegenPerTurn, frame.ShotDamage, BaseShotsMin, BaseShotsMax,
        frame.Accuracy, frame.Evasion, Fighter.FireConeDeg, frame.NormalTurnLimitDegrees,
        frame.NormalMoveMinDistance, frame.NormalMoveMaxDistance);

    public static ShipStats For(ShipType frame, IEnumerable<ShipUpgrade> modules, int refits = 0) =>
        modules.Aggregate(Frame(frame).Refitted(refits), (stats, module) => stats.With(module));

    /// <summary>These numbers after this many hull refits.</summary>
    public ShipStats Refitted(int refits)
    {
        if (refits <= 0)
            return this;
        float m = Refits.Multiplier(refits);
        return this with
        {
            MaxHull = Mathf.RoundToInt(MaxHull * m),
            MaxShield = Mathf.RoundToInt(MaxShield * m),
            ShieldRegen = Mathf.RoundToInt(ShieldRegen * m),
            ShotDamage = Mathf.RoundToInt(ShotDamage * m),
            Accuracy = Accuracy + Refits.AccuracyBonus * refits,
            Evasion = Evasion + Refits.EvasionBonus * refits,
        };
    }

    /// <summary>These numbers with one more module fitted.</summary>
    public ShipStats With(ShipUpgrade module) => module switch
    {
        ShipUpgrade.EngineSpeed => this with { MaxMove = MaxMove + ShipUpgrades.NormalMoveLimitBonus },
        ShipUpgrade.EngineTurn => this with { TurnDeg = TurnDeg + ShipUpgrades.NormalTurnLimitBonusDegrees },
        ShipUpgrade.EngineRetro => this with { MinMove = MinMove - ShipUpgrades.RetroMinMoveReduction },
        ShipUpgrade.GunsExtraShot => this with { ShotsMin = ShotsMin + ShipUpgrades.ExtraShots, ShotsMax = ShotsMax + ShipUpgrades.ExtraShots },
        ShipUpgrade.GunsAccuracy => this with { Accuracy = Accuracy + ShipUpgrades.AccuracyBonus },
        ShipUpgrade.GunsWideMount => this with { FireConeDeg = FireConeDeg + ShipUpgrades.WideMountConeBonusDegrees },
        ShipUpgrade.ShieldsCapacity => this with { MaxShield = Mathf.RoundToInt(MaxShield * (1f + ShipUpgrades.ShieldCapacityBonus)) },
        ShipUpgrade.ShieldsRegen => this with { ShieldRegen = Mathf.RoundToInt(ShieldRegen * (1f + ShipUpgrades.ShieldRegenBonus)) },
        ShipUpgrade.ShieldsArmor => this with
        {
            MaxHull = Mathf.RoundToInt(MaxHull * (1f + ShipUpgrades.ArmorHullBonus)),
            MaxMove = MaxMove - ShipUpgrades.ArmorMoveLimitPenalty,
        },
        _ => this,
    };
}
