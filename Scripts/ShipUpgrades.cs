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
/// slot its frame provides. Modules are hardware: always on, and fitted from
/// level-up cards or module crates.
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
    string Description);

/// <summary>Catalog and combat bonuses for ship modules.</summary>
public static class ShipUpgrades
{
    public const float NormalMoveLimitBonus = 30f;
    public const float NormalTurnLimitBonusDegrees = 15f;
    public const float RetroMinMoveReduction = 40f;
    public const int ExtraShots = 1;
    public const float AccuracyBonus = 0.08f;
    public const float WideMountConeBonusDegrees = 4f;
    // Shield-slot modules scale with the ship, so they matter as much on a
    // Bulwark as on a Kestrel.
    public const float ShieldCapacityBonus = 0.35f;
    public const float ShieldRegenBonus = 0.50f;
    public const float ArmorHullBonus = 0.30f;
    public const float ArmorMoveLimitPenalty = 15f;

    public static readonly ShipUpgradeDefinition[] All =
    {
        new(ShipUpgrade.EngineSpeed, ShipUpgradeSlot.Engine, "Overdrive",
            $"+{NormalMoveLimitBonus:0} maximum move."),
        new(ShipUpgrade.EngineTurn, ShipUpgradeSlot.Engine, "Vector Nozzles",
            $"+{NormalTurnLimitBonusDegrees:0}° maximum turn."),
        new(ShipUpgrade.EngineRetro, ShipUpgradeSlot.Engine, "Retro Thrusters",
            $"-{RetroMinMoveReduction:0} minimum move, so the ship can fly slower."),
        new(ShipUpgrade.GunsExtraShot, ShipUpgradeSlot.Guns, "Burst Loader",
            $"+{ExtraShots} shot per volley."),
        new(ShipUpgrade.GunsAccuracy, ShipUpgradeSlot.Guns, "Targeting Array",
            $"+{AccuracyBonus * 100:0}% accuracy."),
        new(ShipUpgrade.GunsWideMount, ShipUpgradeSlot.Guns, "Wide Mount",
            $"Firing cone {Fighter.FireConeDeg * 2:0}° → {(Fighter.FireConeDeg + WideMountConeBonusDegrees) * 2:0}°."),
        new(ShipUpgrade.ShieldsCapacity, ShipUpgradeSlot.Shields, "Shield Capacitor",
            $"+{ShieldCapacityBonus * 100:0}% maximum shields."),
        new(ShipUpgrade.ShieldsRegen, ShipUpgradeSlot.Shields, "Flux Recycler",
            $"+{ShieldRegenBonus * 100:0}% shield regeneration per turn."),
        new(ShipUpgrade.ShieldsArmor, ShipUpgradeSlot.Shields, "Armor Plating",
            $"+{ArmorHullBonus * 100:0}% hull, -{ArmorMoveLimitPenalty:0} maximum move."),
    };

    public static ShipUpgradeDefinition Get(ShipUpgrade upgrade) => All.First(definition => definition.Id == upgrade);
    public static IEnumerable<ShipUpgradeDefinition> ForSlot(ShipUpgradeSlot slot) => All.Where(definition => definition.Slot == slot);
    public static string SlotName(ShipUpgradeSlot slot) => slot.ToString().ToUpperInvariant();

    public static bool TryParse(string value, out ShipUpgrade upgrade) => Enum.TryParse(value, out upgrade);
}
