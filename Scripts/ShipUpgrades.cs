using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>The three physical hardpoint types carried by player hulls.</summary>
public enum ShipUpgradeSlot
{
    Engine,
    Guns,
    Shields,
}

/// <summary>
/// Ship modules. Each fits one slot type, and a ship carries at most one per
/// slot its frame provides. Modules are hardware: always on, bought with
/// salvage, and never learned by a pilot.
/// </summary>
public enum ShipUpgrade
{
    EngineSpeed,
    EngineTurn,
    GunsExtraShot,
    GunsAccuracy,
    ShieldsCapacity,
    ShieldsRegen,
    EngineRetro,
    GunsWideMount,
    ShieldsArmor,
}

public sealed record ShipUpgradeDefinition(
    ShipUpgrade Id,
    ShipUpgradeSlot Slot,
    string Name,
    string Description,
    int Cost);

/// <summary>Catalog, prices and combat bonuses for ship modules.</summary>
public static class ShipUpgrades
{
    public const float NormalMoveLimitBonus = 30f;
    public const float NormalTurnLimitBonusDegrees = 15f;
    public const float RetroMinMoveReduction = 40f;
    public const int ExtraShots = 1;
    public const float AccuracyBonus = 0.08f;
    public const float WideMountConeBonusDegrees = 4f;
    public const int ShieldCapacityBonus = 8;
    public const int ShieldRegenBonus = 1;
    public const int ArmorHullBonus = 8;
    public const float ArmorMoveLimitPenalty = 15f;

    public static readonly ShipUpgradeDefinition[] All =
    {
        new(ShipUpgrade.EngineSpeed, ShipUpgradeSlot.Engine, "Overdrive",
            $"+{NormalMoveLimitBonus:0} maximum move.", 70),
        new(ShipUpgrade.EngineTurn, ShipUpgradeSlot.Engine, "Vector Nozzles",
            $"+{NormalTurnLimitBonusDegrees:0}° maximum turn.", 70),
        new(ShipUpgrade.EngineRetro, ShipUpgradeSlot.Engine, "Retro Thrusters",
            $"-{RetroMinMoveReduction:0} minimum move, so the ship can fly slower.", 60),
        new(ShipUpgrade.GunsExtraShot, ShipUpgradeSlot.Guns, "Burst Loader",
            $"+{ExtraShots} shot per volley.", 90),
        new(ShipUpgrade.GunsAccuracy, ShipUpgradeSlot.Guns, "Targeting Array",
            $"+{AccuracyBonus * 100:0}% accuracy.", 75),
        new(ShipUpgrade.GunsWideMount, ShipUpgradeSlot.Guns, "Wide Mount",
            $"Firing cone {Fighter.FireConeDeg * 2:0}° → {(Fighter.FireConeDeg + WideMountConeBonusDegrees) * 2:0}°.", 80),
        new(ShipUpgrade.ShieldsCapacity, ShipUpgradeSlot.Shields, "Shield Capacitor",
            $"+{ShieldCapacityBonus} maximum shields.", 70),
        new(ShipUpgrade.ShieldsRegen, ShipUpgradeSlot.Shields, "Flux Recycler",
            $"+{ShieldRegenBonus} shield regeneration per turn.", 75),
        new(ShipUpgrade.ShieldsArmor, ShipUpgradeSlot.Shields, "Armor Plating",
            $"+{ArmorHullBonus} hull, -{ArmorMoveLimitPenalty:0} maximum move.", 65),
    };

    public static ShipUpgradeDefinition Get(ShipUpgrade upgrade) => All.First(definition => definition.Id == upgrade);
    public static IEnumerable<ShipUpgradeDefinition> ForSlot(ShipUpgradeSlot slot) => All.Where(definition => definition.Slot == slot);
    public static string SlotName(ShipUpgradeSlot slot) => slot.ToString().ToUpperInvariant();

    public static bool TryParse(string value, out ShipUpgrade upgrade) => Enum.TryParse(value, out upgrade);
}
