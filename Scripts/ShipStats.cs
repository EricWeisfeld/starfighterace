using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A ship's always-on numbers: its frame's, with its modules fitted. The one
/// place module effects are worked out, so the fighter in battle and the
/// pilot pages always agree. Instincts, scars and maneuvers are situational
/// and are not part of these numbers.
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

    public static ShipStats For(ShipType frame, IEnumerable<ShipUpgrade> modules) =>
        modules.Aggregate(Frame(frame), (stats, module) => stats.With(module));

    /// <summary>These numbers with one more module fitted.</summary>
    public ShipStats With(ShipUpgrade module) => module switch
    {
        ShipUpgrade.EngineSpeed => this with { MaxMove = MaxMove + ShipUpgrades.NormalMoveLimitBonus },
        ShipUpgrade.EngineTurn => this with { TurnDeg = TurnDeg + ShipUpgrades.NormalTurnLimitBonusDegrees },
        ShipUpgrade.EngineRetro => this with { MinMove = MinMove - ShipUpgrades.RetroMinMoveReduction },
        ShipUpgrade.GunsExtraShot => this with { ShotsMin = ShotsMin + ShipUpgrades.ExtraShots, ShotsMax = ShotsMax + ShipUpgrades.ExtraShots },
        ShipUpgrade.GunsAccuracy => this with { Accuracy = Accuracy + ShipUpgrades.AccuracyBonus },
        ShipUpgrade.GunsWideMount => this with { FireConeDeg = FireConeDeg + ShipUpgrades.WideMountConeBonusDegrees },
        ShipUpgrade.ShieldsCapacity => this with { MaxShield = MaxShield + ShipUpgrades.ShieldCapacityBonus },
        ShipUpgrade.ShieldsRegen => this with { ShieldRegen = ShieldRegen + ShipUpgrades.ShieldRegenBonus },
        ShipUpgrade.ShieldsArmor => this with
        {
            MaxHull = MaxHull + ShipUpgrades.ArmorHullBonus,
            MaxMove = MaxMove - ShipUpgrades.ArmorMoveLimitPenalty,
        },
        _ => this,
    };
}
