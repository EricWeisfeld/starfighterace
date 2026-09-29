using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>Special maneuvers a hull can offer while planning its move.</summary>
public enum ShipAbility
{
    UTurn,
    BreakTurn,
    EngineBoost,
    RotatingGuns,
    SuppressionFire,
    EmergencyThrusters,
    SnapTurn,
    HunterLock,
    PursuitBurn,
    EcmJink,
    SensorScramble,
    GhostRun,
    EvasiveDodge,
}

/// <summary>Texture paths + sheet layout for one ship type on one team; loads lazily.</summary>
public class SkinDef
{
    public string BasePath, EnginePath, WeaponPath, ShieldPath, DestructPath;
    public int EngineFrames, WeaponFrames, ShieldFrames, DestructFrames;
    FighterSkin _cached;

    public FighterSkin Load()
    {
        return _cached ??= new FighterSkin
        {
            Base = GD.Load<Texture2D>(BasePath),
            Engine = GD.Load<Texture2D>(EnginePath),
            EngineFrames = EngineFrames,
            Weapon = GD.Load<Texture2D>(WeaponPath),
            WeaponFrames = WeaponFrames,
            Shield = GD.Load<Texture2D>(ShieldPath),
            ShieldFrames = ShieldFrames,
            Destruction = GD.Load<Texture2D>(DestructPath),
            DestructionFrames = DestructFrames,
        };
    }
}

/// <summary>
/// The complete configuration for a hull family's selectable maneuvers. Branch
/// hulls normally share their base class's profile by reference; a branch can
/// opt into a distinct profile when it gains a genuinely unique maneuver.
/// </summary>
public sealed class ShipManeuverProfile
{
    public static readonly ShipManeuverProfile None = new();

    public float UTurnMoveDistance { get; init; }
    public float BreakTurnMoveDistance { get; init; }
    public float EngineBoostMoveDistance { get; init; }
    public float EngineBoostTurnLimitDegrees { get; init; }
    public float RotatingGunsMoveDistance { get; init; }
    public float RotatingGunsFireConeDeg { get; init; }
    public float SuppressionTurnPenaltyDeg { get; init; }
    public float EmergencyThrustersMoveDistance { get; init; }
    public float EmergencyThrustersTurnLimitDegrees { get; init; }
    public float EmergencyThrustersEvasionPenalty { get; init; }
    public float SnapTurnMoveDistance { get; init; }
    public float SnapTurnAngleDegrees { get; init; }
    public float PursuitBurnMoveDistance { get; init; }
    public float PursuitBurnTurnLimitDegrees { get; init; }
    public float HunterLockAccuracyBonus { get; init; }
    public int HunterLockCooldownTurns { get; init; }
    public float EcmJinkMoveDistance { get; init; }
    public float EcmJinkTurnLimitDegrees { get; init; }
    public float EcmJinkEvasionBonus { get; init; }
    public float SensorScrambleAccuracyPenalty { get; init; }
    public int SensorScrambleDurationTurns { get; init; }
    public int SensorScrambleCooldownTurns { get; init; }
    public float GhostRunMoveDistance { get; init; }
    public float GhostRunTurnLimitDegrees { get; init; }
    public float GhostRunEvasionBonus { get; init; }
    public float EvasiveDodgeMoveDistance { get; init; }
    public float EvasiveDodgeAngleDegrees { get; init; }
    public float EvasiveDodgeEvasionBonus { get; init; }
    /// <summary>All maneuvers this class can equip; pilots choose their loadout from this pool.</summary>
    public ShipAbility[] Pool { get; init; } = System.Array.Empty<ShipAbility>();
    /// <summary>Levels at which a pilot earns one maneuver loadout slot.</summary>
    public int[] SlotLevels { get; init; } = System.Array.Empty<int>();
}

/// <summary>A hull class: combat stats, shared maneuver profile, and per-team art.</summary>
public class ShipType
{
    public string Id;
    public string DisplayName;
    public string Description;
    public int MaxHp;
    public int MaxShield;
    public int ShieldRegenPerTurn = 2;
    public int ShotDamage;
    public float Accuracy;
    public float Evasion;
    // Normal-flight handling. Higher maximum turn permits tighter turns;
    // turn radius is derived by the movement code rather than stored here.
    public float NormalTurnLimitDegrees;
    public float NormalMoveMinDistance;
    public float NormalMoveMaxDistance;
    /// <summary>
    /// Experimental, currently unused by all hull balance: smallest allowed
    /// radius for normal-flight arcs. Keep this at zero unless intentionally
    /// testing radius-constrained handling; zero preserves turn-limit-only behavior.
    /// </summary>
    public float NormalMinimumTurnRadius;
    public ShipManeuverProfile Maneuvers { get; init; } = ShipManeuverProfile.None;
    public float UTurnMoveDistance => Maneuvers.UTurnMoveDistance;
    public float BreakTurnMoveDistance => Maneuvers.BreakTurnMoveDistance;
    public float EngineBoostMoveDistance => Maneuvers.EngineBoostMoveDistance;
    public float EngineBoostTurnLimitDegrees => Maneuvers.EngineBoostTurnLimitDegrees;
    public float RotatingGunsMoveDistance => Maneuvers.RotatingGunsMoveDistance;
    public float RotatingGunsFireConeDeg => Maneuvers.RotatingGunsFireConeDeg;
    public float SuppressionTurnPenaltyDeg => Maneuvers.SuppressionTurnPenaltyDeg;
    public float EmergencyThrustersMoveDistance => Maneuvers.EmergencyThrustersMoveDistance;
    public float EmergencyThrustersTurnLimitDegrees => Maneuvers.EmergencyThrustersTurnLimitDegrees;
    public float EmergencyThrustersEvasionPenalty => Maneuvers.EmergencyThrustersEvasionPenalty;
    public float SnapTurnMoveDistance => Maneuvers.SnapTurnMoveDistance;
    public float SnapTurnAngleDegrees => Maneuvers.SnapTurnAngleDegrees;
    public float PursuitBurnMoveDistance => Maneuvers.PursuitBurnMoveDistance;
    public float PursuitBurnTurnLimitDegrees => Maneuvers.PursuitBurnTurnLimitDegrees;
    public float HunterLockAccuracyBonus => Maneuvers.HunterLockAccuracyBonus;
    public int HunterLockCooldownTurns => Maneuvers.HunterLockCooldownTurns;
    public float EcmJinkMoveDistance => Maneuvers.EcmJinkMoveDistance;
    public float EcmJinkTurnLimitDegrees => Maneuvers.EcmJinkTurnLimitDegrees;
    public float EcmJinkEvasionBonus => Maneuvers.EcmJinkEvasionBonus;
    public float SensorScrambleAccuracyPenalty => Maneuvers.SensorScrambleAccuracyPenalty;
    public int SensorScrambleDurationTurns => Maneuvers.SensorScrambleDurationTurns;
    public int SensorScrambleCooldownTurns => Maneuvers.SensorScrambleCooldownTurns;
    public float GhostRunMoveDistance => Maneuvers.GhostRunMoveDistance;
    public float GhostRunTurnLimitDegrees => Maneuvers.GhostRunTurnLimitDegrees;
    public float GhostRunEvasionBonus => Maneuvers.GhostRunEvasionBonus;
    public float EvasiveDodgeMoveDistance => Maneuvers.EvasiveDodgeMoveDistance;
    public float EvasiveDodgeAngleDegrees => Maneuvers.EvasiveDodgeAngleDegrees;
    public float EvasiveDodgeEvasionBonus => Maneuvers.EvasiveDodgeEvasionBonus;
    public ShipAbility[] ManeuverPool => Maneuvers.Pool;
    public int[] ManeuverSlotLevels => Maneuvers.SlotLevels;
    /// <summary>Permanent hardware hardpoints supplied by this hull.</summary>
    public ShipUpgradeSlot[] UpgradeSlots = System.Array.Empty<ShipUpgradeSlot>();
    public SkinDef[] SkinsByTeam;   // [0] = player (Nairan), [1] = enemy (Kla'ed)

    public FighterSkin GetSkin(int team) => SkinsByTeam[team].Load();
    public bool OffersManeuver(ShipAbility ability) => ManeuverPool.Contains(ability);
    public int ManeuverSlotsAtLevel(int level) => Mathf.Min(3, ManeuverSlotLevels.Count(slotLevel => level >= slotLevel));
}

public static class ShipTypes
{
    const string Nairan = "res://Assets/Foozle_2DS0013_Void_FleetPack_2/Foozle_2DS0013_Void_EnemyFleet_2/Nairan";
    const string Klaed = "res://Assets/Foozle_2DS0012_Void_FleetPack_1/Foozle_2DS0012_Void_EnemyFleet_1/Kla'ed";

    /// <summary>Escort-only civilian hull. It uses the regular ship systems but carries no weapons.</summary>
    public static readonly ShipType CivilianDreadnought = new()
    {
        Id = "civilian_dreadnought",
        DisplayName = "Civilian Dreadnought",
        Description = "An unarmed civilian transport built on a dreadnought hull.",
        MaxHp = 80,
        MaxShield = 0,
        ShieldRegenPerTurn = 0,
        ShotDamage = 0,
        Accuracy = 0f,
        Evasion = 0f,
        NormalTurnLimitDegrees = 28f,
        NormalMoveMinDistance = 120f,
        NormalMoveMaxDistance = 120f,
        SkinsByTeam = new[]
        {
            new SkinDef
            {
                BasePath = $"{Nairan}/Designs - Base/PNGs/Nairan - Dreadnought - Base.png",
                EnginePath = $"{Nairan}/Engine Effects/PNGs/Nairan - Dreadnought - Engine.png", EngineFrames = 8,
                WeaponPath = $"{Nairan}/Weapons/PNGs/Nairan - Dreadnought - Weapons.png", WeaponFrames = 34,
                ShieldPath = $"{Nairan}/Shields/PNGs/Nairan - Dreadnought - Shield.png", ShieldFrames = 8,
                DestructPath = $"{Nairan}/Destruction/PNGs/Nairan - Dreadnought -  Destruction.png", DestructFrames = 18,
            },
        },
    };

    public static readonly ShipType Scout = new()
    {
        Id = "scout",
        DisplayName = "S1 Kestrel",
        Description = "Fast and nimble reconnaissance frame with a tight turn, but fragile and lightly armed.",
        MaxHp = 20,
        MaxShield = 10,
        ShotDamage = 3,
        Accuracy = 0.85f,
        Evasion = 0.35f,
        NormalTurnLimitDegrees = 120f,
        // Former slow and regular throttle distances.
        NormalMoveMinDistance = 170f,
        NormalMoveMaxDistance = 235f,
        Maneuvers = new ShipManeuverProfile
        {
            BreakTurnMoveDistance = 180f,
            SnapTurnMoveDistance = 80f,
            SnapTurnAngleDegrees = 145f,
            HunterLockAccuracyBonus = 0.12f,
            HunterLockCooldownTurns = 4,
            PursuitBurnMoveDistance = 370f,
            PursuitBurnTurnLimitDegrees = 20f,
            EcmJinkMoveDistance = 110f,
            EcmJinkTurnLimitDegrees = 100f,
            EcmJinkEvasionBonus = 0.25f,
            SensorScrambleAccuracyPenalty = 0.20f,
            SensorScrambleDurationTurns = 2,
            SensorScrambleCooldownTurns = 4,
            GhostRunMoveDistance = 300f,
            GhostRunTurnLimitDegrees = 125f,
            GhostRunEvasionBonus = 0.25f,
            Pool = new[] { ShipAbility.BreakTurn, ShipAbility.SnapTurn, ShipAbility.HunterLock, ShipAbility.PursuitBurn,
                ShipAbility.EcmJink, ShipAbility.SensorScramble, ShipAbility.GhostRun },
            SlotLevels = new[] { 2, 4, 6 },
        },
        UpgradeSlots = new[] { ShipUpgradeSlot.Engine },
        SkinsByTeam = new[]
        {
            new SkinDef
            {
                BasePath = $"{Nairan}/Designs - Base/PNGs/Nairan - Scout - Base.png",
                EnginePath = $"{Nairan}/Engine Effects/PNGs/Nairan - Scout - Engine.png", EngineFrames = 8,
                WeaponPath = $"{Nairan}/Weapons/PNGs/Nairan - Scout - Weapons.png", WeaponFrames = 6,
                ShieldPath = $"{Nairan}/Shields/PNGs/Nairan - Scout - Shield.png", ShieldFrames = 18,
                DestructPath = $"{Nairan}/Destruction/PNGs/Nairan - Scout -  Destruction.png", DestructFrames = 16,
            },
            new SkinDef
            {
                BasePath = $"{Klaed}/Base/PNGs/Kla'ed - Scout - Base.png",
                EnginePath = $"{Klaed}/Engine/PNGs/Kla'ed - Scout - Engine.png", EngineFrames = 10,
                WeaponPath = $"{Klaed}/Weapons/PNGs/Kla'ed - Scout - Weapons.png", WeaponFrames = 6,
                ShieldPath = $"{Klaed}/Shield/PNGs/Kla'ed - Scout - Shield.png", ShieldFrames = 14,
                DestructPath = $"{Klaed}/Destruction/PNGs/Kla'ed - Scout - Destruction.png", DestructFrames = 10,
            },
        },
    };

    /// <summary>The Striker is the first selectable Kestrel-series specialization.</summary>
    public static readonly ShipType ScoutStriker = new()
    {
        Id = "s4_striker",
        DisplayName = "S4 Striker",
        Description = "An aggressive Kestrel-series frame built to reverse pursuit, lock a target, and run it down.",
        MaxHp = 18,
        MaxShield = 8,
        ShotDamage = 4,
        Accuracy = 0.88f,
        Evasion = 0.38f,
        NormalTurnLimitDegrees = 125f,
        NormalMoveMinDistance = 170f,
        NormalMoveMaxDistance = 250f,
        Maneuvers = Scout.Maneuvers,
        UpgradeSlots = new[] { ShipUpgradeSlot.Engine, ShipUpgradeSlot.Guns },
        SkinsByTeam = Scout.SkinsByTeam,
    };

    /// <summary>The Ghost is the evasive electronic-warfare Kestrel-series specialization.</summary>
    public static readonly ShipType ScoutGhost = new()
    {
        Id = "s9_ghost",
        DisplayName = "S9 Ghost",
        Description = "An evasive Kestrel-series frame that confuses enemy sensors and slips out of danger.",
        MaxHp = 20,
        MaxShield = 12,
        ShotDamage = 3,
        Accuracy = 0.84f,
        Evasion = 0.42f,
        NormalTurnLimitDegrees = 130f,
        NormalMoveMinDistance = 170f,
        NormalMoveMaxDistance = 245f,
        Maneuvers = Scout.Maneuvers,
        UpgradeSlots = new[] { ShipUpgradeSlot.Engine, ShipUpgradeSlot.Shields },
        SkinsByTeam = Scout.SkinsByTeam,
    };

    /// <summary>
    /// The Raptor class before a pilot has committed to a flight-frame
    /// specialization. It uses the Black Hawk art as a neutral frame until the
    /// player makes that decision in the pilot career screen.
    /// </summary>
    public static readonly ShipType Raptor = new()
    {
        Id = "raptor",
        DisplayName = "Raptor",
        Description = "Versatile Raptor-class starfighter. Build a three-maneuver loadout as its pilot levels up.",
        MaxHp = 34,
        MaxShield = 17,
        ShotDamage = 5,
        Accuracy = 0.88f,
        Evasion = 0.28f,
        NormalTurnLimitDegrees = 110f,
        NormalMoveMinDistance = 145f,
        NormalMoveMaxDistance = 210f,
        Maneuvers = new ShipManeuverProfile
        {
            UTurnMoveDistance = 75f,
            EngineBoostMoveDistance = 350f,
            EngineBoostTurnLimitDegrees = 45f,
            EvasiveDodgeMoveDistance = 110f,
            EvasiveDodgeAngleDegrees = 135f,
            EvasiveDodgeEvasionBonus = 0.40f,
            Pool = new[] { ShipAbility.UTurn, ShipAbility.EngineBoost, ShipAbility.EvasiveDodge },
            SlotLevels = new[] { 2, 4, 6 },
        },
        UpgradeSlots = new[] { ShipUpgradeSlot.Guns },
        SkinsByTeam = new[]
        {
            new SkinDef
            {
                BasePath = $"{Nairan}/Designs - Base/PNGs/Nairan - Bomber - Base.png",
                EnginePath = $"{Nairan}/Engine Effects/PNGs/Nairan - Bomber - Engine.png", EngineFrames = 8,
                WeaponPath = $"{Nairan}/Weapons/PNGs/Nairan - Fighter - Weapons.png", WeaponFrames = 28,
                ShieldPath = $"{Nairan}/Shields/PNGs/Nairan - Bomber - Shield.png", ShieldFrames = 10,
                DestructPath = $"{Nairan}/Destruction/PNGs/Nairan - Bomber -  Destruction.png", DestructFrames = 16,
            },
            new SkinDef
            {
                BasePath = $"{Klaed}/Base/PNGs/Kla'ed - Bomber - Base.png",
                EnginePath = $"{Klaed}/Engine/PNGs/Kla'ed - Bomber - Engine.png", EngineFrames = 10,
                WeaponPath = $"{Klaed}/Weapons/PNGs/Kla'ed - Fighter - Weapons.png", WeaponFrames = 6,
                ShieldPath = $"{Klaed}/Shield/PNGs/Kla'ed - Bomber - Shield.png", ShieldFrames = 6,
                DestructPath = $"{Klaed}/Destruction/PNGs/Kla'ed - Bomber - Destruction.png", DestructFrames = 9,
            },
        },
    };

    public static readonly ShipType BlackHawk = new()
    {
        Id = "r3_black_hawk",
        DisplayName = "R3 Black Hawk",
        // Balance identity: a modestly heavier hitter than the baseline Raptor,
        // paid for with small reductions to speed, handling, and defense.
        Description = "A heavier-hitting Raptor variant that trades a little speed, handling, and protection for stronger shots.",
        MaxHp = 32,
        MaxShield = 15,
        ShotDamage = 6,
        Accuracy = 0.87f,
        Evasion = 0.26f,
        NormalTurnLimitDegrees = 105f,
        NormalMoveMinDistance = 145f,
        NormalMoveMaxDistance = 195f,
        Maneuvers = Raptor.Maneuvers,
        UpgradeSlots = new[] { ShipUpgradeSlot.Guns, ShipUpgradeSlot.Shields },
        SkinsByTeam = new[]
        {
            new SkinDef
            {
                BasePath = $"{Nairan}/Designs - Base/PNGs/Nairan - Bomber - Base.png",
                EnginePath = $"{Nairan}/Engine Effects/PNGs/Nairan - Bomber - Engine.png", EngineFrames = 8,
                // The pack has no bomber weapon sheet, so use the matching faction's fighter fire effect.
                WeaponPath = $"{Nairan}/Weapons/PNGs/Nairan - Fighter - Weapons.png", WeaponFrames = 28,
                ShieldPath = $"{Nairan}/Shields/PNGs/Nairan - Bomber - Shield.png", ShieldFrames = 10,
                DestructPath = $"{Nairan}/Destruction/PNGs/Nairan - Bomber -  Destruction.png", DestructFrames = 16,
            },
            new SkinDef
            {
                BasePath = $"{Klaed}/Base/PNGs/Kla'ed - Bomber - Base.png",
                EnginePath = $"{Klaed}/Engine/PNGs/Kla'ed - Bomber - Engine.png", EngineFrames = 10,
                WeaponPath = $"{Klaed}/Weapons/PNGs/Kla'ed - Fighter - Weapons.png", WeaponFrames = 6,
                ShieldPath = $"{Klaed}/Shield/PNGs/Kla'ed - Bomber - Shield.png", ShieldFrames = 6,
                DestructPath = $"{Klaed}/Destruction/PNGs/Kla'ed - Bomber - Destruction.png", DestructFrames = 9,
            },
        },
    };

    public static readonly ShipType Falcon = new()
    {
        Id = "r5_falcon",
        DisplayName = "R5 Falcon",
        // Balance identity: the safe general-purpose Raptor advancement, with
        // small improvements across the frame and no dramatic weakness.
        Description = "A refined all-round Raptor with slightly better durability, accuracy, evasion, turning, and speed.",
        MaxHp = 35,
        MaxShield = 18,
        ShotDamage = 5,
        Accuracy = 0.90f,
        Evasion = 0.29f,
        NormalTurnLimitDegrees = 115f,
        NormalMoveMinDistance = 145f,
        NormalMoveMaxDistance = 215f,
        Maneuvers = Raptor.Maneuvers,
        UpgradeSlots = new[] { ShipUpgradeSlot.Guns, ShipUpgradeSlot.Engine },
        SkinsByTeam = new[]
        {
            new SkinDef
            {
                BasePath = $"{Nairan}/Designs - Base/PNGs/Nairan - Torpedo Ship - Base.png",
                EnginePath = $"{Nairan}/Engine Effects/PNGs/Nairan - Torpedo Ship - Engine.png", EngineFrames = 8,
                WeaponPath = $"{Nairan}/Weapons/PNGs/Nairan - Torpedo Ship - Weapons.png", WeaponFrames = 12,
                ShieldPath = $"{Nairan}/Shields/PNGs/Nairan - Torpedo Ship - Shield.png", ShieldFrames = 8,
                DestructPath = $"{Nairan}/Destruction/PNGs/Nairan - Torpedo Ship -  Destruction.png", DestructFrames = 16,
            },
            new SkinDef
            {
                BasePath = $"{Klaed}/Base/PNGs/Kla'ed - Torpedo Ship - Base.png",
                EnginePath = $"{Klaed}/Engine/PNGs/Kla'ed - Torpedo Ship - Engine.png", EngineFrames = 10,
                WeaponPath = $"{Klaed}/Weapons/PNGs/Kla'ed - Torpedo Ship - Weapons.png", WeaponFrames = 16,
                ShieldPath = $"{Klaed}/Shield/PNGs/Kla'ed - Torpedo Ship - Shield.png", ShieldFrames = 10,
                DestructPath = $"{Klaed}/Destruction/PNGs/Kla'ed - Torpedo Ship - Destruction.png", DestructFrames = 10,
            },
        },
    };

    /// <summary>Neutral ZT-class frame, before a pilot selects a specialization.</summary>
    public static readonly ShipType Zt = new()
    {
        Id = "zt",
        DisplayName = "ZT Class",
        Description = "Armored ZT-class starfighter. Build a three-maneuver loadout as its pilot levels up.",
        MaxHp = 50,
        MaxShield = 25,
        ShotDamage = 5,
        Accuracy = 0.82f,
        Evasion = 0.15f,
        NormalTurnLimitDegrees = 70f,
        NormalMoveMinDistance = 105f,
        NormalMoveMaxDistance = 165f,
        Maneuvers = new ShipManeuverProfile
        {
            RotatingGunsMoveDistance = 80f,
            RotatingGunsFireConeDeg = 45f,
            SuppressionTurnPenaltyDeg = 8f,
            EmergencyThrustersMoveDistance = 320f,
            EmergencyThrustersTurnLimitDegrees = 35f,
            EmergencyThrustersEvasionPenalty = 0.10f,
            Pool = new[] { ShipAbility.RotatingGuns, ShipAbility.SuppressionFire, ShipAbility.EmergencyThrusters },
            SlotLevels = new[] { 2, 4, 6 },
        },
        UpgradeSlots = new[] { ShipUpgradeSlot.Shields },
        SkinsByTeam = new[]
        {
            new SkinDef
            {
                BasePath = $"{Nairan}/Designs - Base/PNGs/Nairan - Frigate - Base.png",
                EnginePath = $"{Nairan}/Engine Effects/PNGs/Nairan - Frigate - Engine.png", EngineFrames = 8,
                WeaponPath = $"{Nairan}/Weapons/PNGs/Nairan - Frigate - Weapons.png", WeaponFrames = 28,
                ShieldPath = $"{Nairan}/Shields/PNGs/Nairan - Frigate - Shield.png", ShieldFrames = 8,
                DestructPath = $"{Nairan}/Destruction/PNGs/Nairan - Frigate -  Destruction.png", DestructFrames = 16,
            },
            new SkinDef
            {
                BasePath = $"{Klaed}/Base/PNGs/Kla'ed - Frigate - Base.png",
                EnginePath = $"{Klaed}/Engine/PNGs/Kla'ed - Frigate - Engine.png", EngineFrames = 10,
                WeaponPath = $"{Klaed}/Weapons/PNGs/Kla'ed - Frigate - Weapons.png", WeaponFrames = 6,
                ShieldPath = $"{Klaed}/Shield/PNGs/Kla'ed - Frigate - Shield.png", ShieldFrames = 40,
                DestructPath = $"{Klaed}/Destruction/PNGs/Kla'ed - Frigate - Destruction.png", DestructFrames = 10,
            },
        },
    };

    public static readonly ShipType Zt6 = new()
    {
        Id = "zt_6",
        DisplayName = "ZT-6",
        Description = "A slow armored gunship that pins enemies down with broadside suppression.",
        MaxHp = 48,
        MaxShield = 22,
        ShotDamage = 6,
        Accuracy = 0.87f,
        Evasion = 0.14f,
        NormalTurnLimitDegrees = 65f,
        NormalMoveMinDistance = 105f,
        NormalMoveMaxDistance = 165f,
        Maneuvers = Zt.Maneuvers,
        UpgradeSlots = new[] { ShipUpgradeSlot.Shields, ShipUpgradeSlot.Guns },
        SkinsByTeam = new[]
        {
            new SkinDef
            {
                BasePath = $"{Nairan}/Designs - Base/PNGs/Nairan - Frigate - Base.png",
                EnginePath = $"{Nairan}/Engine Effects/PNGs/Nairan - Frigate - Engine.png", EngineFrames = 8,
                WeaponPath = $"{Nairan}/Weapons/PNGs/Nairan - Frigate - Weapons.png", WeaponFrames = 28,
                ShieldPath = $"{Nairan}/Shields/PNGs/Nairan - Frigate - Shield.png", ShieldFrames = 8,
                DestructPath = $"{Nairan}/Destruction/PNGs/Nairan - Frigate -  Destruction.png", DestructFrames = 16,
            },
            new SkinDef
            {
                BasePath = $"{Klaed}/Base/PNGs/Kla'ed - Frigate - Base.png",
                EnginePath = $"{Klaed}/Engine/PNGs/Kla'ed - Frigate - Engine.png", EngineFrames = 10,
                WeaponPath = $"{Klaed}/Weapons/PNGs/Kla'ed - Frigate - Weapons.png", WeaponFrames = 6,
                ShieldPath = $"{Klaed}/Shield/PNGs/Kla'ed - Frigate - Shield.png", ShieldFrames = 40,
                DestructPath = $"{Klaed}/Destruction/PNGs/Kla'ed - Frigate - Destruction.png", DestructFrames = 10,
            },
        },
    };

    /// <summary>A shield-first ZT specialization with reinforced armor and improved handling.</summary>
    public static readonly ShipType Zt8Bulwark = new()
    {
        Id = "zt_8_bulwark",
        DisplayName = "ZT-8 Bulwark",
        Description = "A reinforced fleet defender that trades gun output for armor, shields, and steadier handling.",
        MaxHp = 56,
        MaxShield = 32,
        ShieldRegenPerTurn = 3,
        ShotDamage = 4,
        Accuracy = 0.80f,
        Evasion = 0.12f,
        NormalTurnLimitDegrees = 72f,
        NormalMoveMinDistance = 105f,
        NormalMoveMaxDistance = 170f,
        Maneuvers = Zt.Maneuvers,
        UpgradeSlots = new[] { ShipUpgradeSlot.Shields, ShipUpgradeSlot.Engine },
        SkinsByTeam = Zt.SkinsByTeam,
    };

    /// <summary>Current playable class lines. Their maneuvers come from pilot loadouts, not frame branches.</summary>
    public static readonly ShipType[] All = { Scout, Raptor, Zt };
    static readonly ShipType[] BranchFrames = { ScoutStriker, ScoutGhost, BlackHawk, Falcon, Zt6, Zt8Bulwark };
    public static readonly ShipType[] RecruitableClasses = { Scout, Raptor, Zt };
    public static readonly ShipType[] SandboxHulls = BranchFrames;

    /// <summary>Resolves a data-authored hull id. Invalid or retired hull IDs fall back to the S1 Kestrel.</summary>
    public static ShipType FromId(string id) => All.Concat(BranchFrames).FirstOrDefault(type => type.Id == id) ?? Scout;

    public static ShipType BaseClass(string classId) => classId switch
    {
        "raptor" => Raptor,
        "zt" => Zt,
        _ => Scout,
    };

    public static ShipType[] HullBranches(string classId) => classId switch
    {
        "raptor" => new[] { BlackHawk, Falcon },
        "zt" => new[] { Zt6, Zt8Bulwark },
        _ => new[] { ScoutStriker, ScoutGhost },
    };

    public static string ClassIdForHull(string hullId) => hullId switch
    {
        "raptor" or "r3_black_hawk" or "r5_falcon" => "raptor",
        "zt" or "zt_6" or "zt_8_bulwark" => "zt",
        _ => "scout",
    };
}

/// <summary>Carries the chosen squad from the selection screen into the battle scene.</summary>
public static class GameSetup
{
    public static List<Pilot> PlayerPilots = new();
    public static bool IsTestBattle { get; private set; }
    public static BattleMapDefinition TestBattleMap { get; private set; }

    public static void StartCampaign()
    {
        IsTestBattle = false;
        PlayerPilots.Clear();
        TestBattleMap = null;
        CampaignData.StartCampaign();
    }

    public static void StartNewCampaign()
    {
        IsTestBattle = false;
        PlayerPilots.Clear();
        TestBattleMap = null;
        CampaignData.StartNewCampaign();
    }

    public static void StartTestBattle(IEnumerable<ShipType> ships, BattleMapDefinition map)
    {
        string[] callsigns = { "ALPHA", "BRAVO", "CHARLIE" };
        PlayerPilots = ships.Select((ship, index) =>
        {
            var pilot = new Pilot(callsigns[index], ship) { Level = PilotRoster.MaxLevel };
            pilot.ChooseHull(ShipTypes.HullBranches(pilot.ClassId)[0]);
            pilot.RestoreManeuvers(pilot.Ship.ManeuverPool);
            return pilot;
        }).ToList();
        IsTestBattle = true;
        TestBattleMap = map;
    }
}
