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

/// <summary>Basic modules that can be permanently installed in a matching slot.</summary>
public enum ShipUpgrade
{
    EngineSpeed,
    EngineTurn,
    GunsExtraShot,
    GunsAccuracy,
    ShieldsCapacity,
    ShieldsRegen,
}

public sealed record ShipUpgradeDefinition(
    ShipUpgrade Id,
    ShipUpgradeSlot Slot,
    string Name,
    string Description);

/// <summary>Catalog, prices and combat bonuses for ship hardware.</summary>
public static class ShipUpgrades
{
    public const int Cost = 150;
    public const float NormalMoveLimitBonus = 30f;
    public const float NormalTurnLimitBonusDegrees = 15f;
    public const int ExtraShots = 1;
    public const float AccuracyBonus = 0.08f;
    public const int ShieldCapacityBonus = 8;
    public const int ShieldRegenBonus = 1;

    public static readonly ShipUpgradeDefinition[] All =
    {
        new(ShipUpgrade.EngineSpeed, ShipUpgradeSlot.Engine, "Overdrive", $"+{NormalMoveLimitBonus:0} maximum normal move"),
        new(ShipUpgrade.EngineTurn, ShipUpgradeSlot.Engine, "Vector Nozzles", $"+{NormalTurnLimitBonusDegrees:0}° maximum normal turn"),
        new(ShipUpgrade.GunsExtraShot, ShipUpgradeSlot.Guns, "Burst Loader", $"+{ExtraShots} shot per barrage"),
        new(ShipUpgrade.GunsAccuracy, ShipUpgradeSlot.Guns, "Targeting Array", $"+{AccuracyBonus * 100:0}% accuracy"),
        new(ShipUpgrade.ShieldsCapacity, ShipUpgradeSlot.Shields, "Shield Capacitor", $"+{ShieldCapacityBonus} maximum shields"),
        new(ShipUpgrade.ShieldsRegen, ShipUpgradeSlot.Shields, "Flux Recycler", $"+{ShieldRegenBonus} shield regen per turn"),
    };

    public static ShipUpgradeDefinition Get(ShipUpgrade upgrade) => All.First(definition => definition.Id == upgrade);
    public static IEnumerable<ShipUpgradeDefinition> ForSlot(ShipUpgradeSlot slot) => All.Where(definition => definition.Slot == slot);
    public static string SlotName(ShipUpgradeSlot slot) => slot.ToString().ToUpperInvariant();

    public static int MaxShield(Pilot pilot) => pilot.Ship.MaxShield +
        (pilot.HasUpgrade(ShipUpgrade.ShieldsCapacity) ? ShieldCapacityBonus : 0);
    public static int BaseShieldRegen(Pilot pilot) => pilot.Ship.ShieldRegenPerTurn +
        (pilot.HasUpgrade(ShipUpgrade.ShieldsRegen) ? ShieldRegenBonus : 0);
    public static float BaseAccuracy(Pilot pilot) => pilot.Ship.Accuracy +
        (pilot.HasUpgrade(ShipUpgrade.GunsAccuracy) ? AccuracyBonus : 0f);
    public static float BaseNormalMoveLimit(Pilot pilot) => pilot.Ship.NormalMoveMaxDistance +
        (pilot.HasUpgrade(ShipUpgrade.EngineSpeed) ? NormalMoveLimitBonus : 0f);
    public static float BaseNormalTurnLimitDegrees(Pilot pilot) => pilot.Ship.NormalTurnLimitDegrees +
        (pilot.HasUpgrade(ShipUpgrade.EngineTurn) ? NormalTurnLimitBonusDegrees : 0f);
    public static int BonusBarrageShots(Pilot pilot) =>
        pilot.HasUpgrade(ShipUpgrade.GunsExtraShot) ? ExtraShots : 0;

    public static bool TryParse(string value, out ShipUpgrade upgrade) => Enum.TryParse(value, out upgrade);
}
