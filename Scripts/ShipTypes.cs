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
        if (_cached != null)
            return _cached;
        _cached = new FighterSkin
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
        // Measure the drawn hull once: half its width plus height, halved again,
        // sits between its short and long radius whichever way it faces.
        Rect2I used = _cached.Base.GetImage()?.GetUsedRect() ?? new Rect2I(0, 0, 32, 32);
        if (used.Size.X > 0 && used.Size.Y > 0)
            _cached.HullRadius = (used.Size.X + used.Size.Y) / 4f;
        _cached.Icon = used.Size.X > 0 && used.Size.Y > 0
            ? new AtlasTexture { Atlas = _cached.Base, Region = new Rect2(used.Position, used.Size) }
            : _cached.Base;
        return _cached;
    }
}

/// <summary>
/// The complete configuration for a hull family's selectable maneuvers. Branch
/// hulls normally share their base class's profile by reference; a branch can
/// opt into a distinct profile when it gains a genuinely unique maneuver. Each
/// fighter flies a copy (<see cref="Fighter.Moves"/>) that its pilot's
/// maneuver masteries adjust.
/// </summary>
public sealed record ShipManeuverProfile
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
}

/// <summary>
/// A frame's place in its class line. Attack frames hit harder and die
/// faster; guard frames last longer and hit softer.
/// </summary>
public enum FrameRole { Balanced, Attack, Guard }

/// <summary>A hull class: combat stats, shared maneuver profile, and per-team art.</summary>
public class ShipType
{
    public string Id;
    public string DisplayName;
    public string Description;
    public int MaxHp;
    public int MaxShield;
    /// <summary>The frame's place in its line: balanced, attack or guard.</summary>
    public FrameRole Role;
    public const int DefaultShieldRegen = 20;
    public int ShieldRegenPerTurn = DefaultShieldRegen;
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
    public SkinDef[] SkinsByTeam;   // [0] = player (Nairan), [1] = enemy (Kla'ed)

    public FighterSkin GetSkin(int team) => SkinsByTeam[team].Load();
    public bool OffersManeuver(ShipAbility ability) => ManeuverPool.Contains(ability);
}

public static class ShipTypes
{
    // Handling belongs to the class line: every frame in a line turns and
    // moves alike, and frames differ only in how they fight.
    const float KestrelTurn = 120f, KestrelMinMove = 170f, KestrelMaxMove = 235f;
    const float RaptorTurn = 110f, RaptorMinMove = 145f, RaptorMaxMove = 210f;
    const float ZtTurn = 70f, ZtMinMove = 105f, ZtMaxMove = 165f;

    const string Nairan = "res://Assets/Foozle_2DS0013_Void_FleetPack_2/Foozle_2DS0013_Void_EnemyFleet_2/Nairan";
    const string Klaed = "res://Assets/Foozle_2DS0012_Void_FleetPack_1/Foozle_2DS0012_Void_EnemyFleet_1/Kla'ed";

    /// <summary>Escort-only civilian hull. It uses the regular ship systems but carries no weapons.</summary>
    public static readonly ShipType CivilianDreadnought = new()
    {
        Id = "civilian_dreadnought",
        DisplayName = "Civilian Dreadnought",
        Description = "An unarmed civilian transport built on a dreadnought hull.",
        MaxHp = 800,
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

    /// <summary>The Kestrel line's balanced frame.</summary>
    public static readonly ShipType Scout = new()
    {
        Id = "scout",
        DisplayName = "S1 Kestrel",
        Description = "Fast and nimble reconnaissance frame with a tight turn, but fragile and lightly armed.",
        Role = FrameRole.Balanced,
        MaxHp = 200,
        MaxShield = 100,
        ShotDamage = 35,
        Accuracy = 0.85f,
        Evasion = 0.35f,
        NormalTurnLimitDegrees = KestrelTurn,
        NormalMoveMinDistance = KestrelMinMove,
        NormalMoveMaxDistance = KestrelMaxMove,
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
        },
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
    /// <summary>The Kestrel line's attack frame: heavier guns, less hull and precision.</summary>
    public static readonly ShipType ScoutStriker = new()
    {
        Id = "s4_striker",
        DisplayName = "S4 Striker",
        Description = "An aggressive Kestrel-series frame. Heavier guns, paid for with hull and precision.",
        Role = FrameRole.Attack,
        MaxHp = 160,
        MaxShield = 80,
        ShotDamage = 40,
        Accuracy = 0.80f,
        Evasion = 0.35f,
        NormalTurnLimitDegrees = KestrelTurn,
        NormalMoveMinDistance = KestrelMinMove,
        NormalMoveMaxDistance = KestrelMaxMove,
        Maneuvers = Scout.Maneuvers,
        SkinsByTeam = Scout.SkinsByTeam,
    };

    /// <summary>The Ghost is the evasive electronic-warfare Kestrel-series specialization.</summary>
    /// <summary>The Kestrel line's guard frame: survives by not being hit, at the cost of its own aim.</summary>
    public static readonly ShipType ScoutGhost = new()
    {
        Id = "s9_ghost",
        DisplayName = "S9 Ghost",
        Description = "An evasive Kestrel-series frame that is hard to hit, but its jamming gear throws off its own aim.",
        Role = FrameRole.Guard,
        MaxHp = 200,
        MaxShield = 140,
        ShotDamage = 30,
        Accuracy = 0.70f,
        Evasion = 0.40f,
        NormalTurnLimitDegrees = KestrelTurn,
        NormalMoveMinDistance = KestrelMinMove,
        NormalMoveMaxDistance = KestrelMaxMove,
        Maneuvers = Scout.Maneuvers,
        SkinsByTeam = Scout.SkinsByTeam,
    };

    /// <summary>The Raptor line's balanced frame. It uses the Black Hawk art.</summary>
    public static readonly ShipType Raptor = new()
    {
        Id = "raptor",
        DisplayName = "Raptor",
        Description = "Versatile Raptor-class starfighter, balanced between firepower and protection.",
        Role = FrameRole.Balanced,
        MaxHp = 340,
        MaxShield = 170,
        ShieldRegenPerTurn = 17,
        ShotDamage = 50,
        Accuracy = 0.88f,
        Evasion = 0.28f,
        NormalTurnLimitDegrees = RaptorTurn,
        NormalMoveMinDistance = RaptorMinMove,
        NormalMoveMaxDistance = RaptorMaxMove,
        Maneuvers = new ShipManeuverProfile
        {
            UTurnMoveDistance = 75f,
            EngineBoostMoveDistance = 350f,
            EngineBoostTurnLimitDegrees = 45f,
            EvasiveDodgeMoveDistance = 110f,
            EvasiveDodgeAngleDegrees = 135f,
            EvasiveDodgeEvasionBonus = 0.40f,
            Pool = new[] { ShipAbility.UTurn, ShipAbility.EngineBoost, ShipAbility.EvasiveDodge },
        },
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

    /// <summary>The Raptor line's attack frame: stronger shots, less protection.</summary>
    public static readonly ShipType BlackHawk = new()
    {
        Id = "r3_black_hawk",
        DisplayName = "R3 Black Hawk",
        Description = "A heavier-hitting Raptor variant that trades protection for stronger shots.",
        Role = FrameRole.Attack,
        MaxHp = 290,
        MaxShield = 140,
        ShotDamage = 60,
        Accuracy = 0.88f,
        Evasion = 0.28f,
        NormalTurnLimitDegrees = RaptorTurn,
        NormalMoveMinDistance = RaptorMinMove,
        NormalMoveMaxDistance = RaptorMaxMove,
        Maneuvers = Raptor.Maneuvers,
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

    /// <summary>The Raptor line's guard frame: extra shields and evasion, lighter guns.</summary>
    public static readonly ShipType Falcon = new()
    {
        Id = "r5_falcon",
        DisplayName = "R5 Falcon",
        Description = "A shielded, hard-to-hit Raptor variant that carries lighter guns.",
        Role = FrameRole.Guard,
        MaxHp = 340,
        MaxShield = 220,
        ShotDamage = 40,
        Accuracy = 0.88f,
        Evasion = 0.36f,
        NormalTurnLimitDegrees = RaptorTurn,
        NormalMoveMinDistance = RaptorMinMove,
        NormalMoveMaxDistance = RaptorMaxMove,
        Maneuvers = Raptor.Maneuvers,
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

    /// <summary>The ZT line's balanced frame.</summary>
    public static readonly ShipType Zt = new()
    {
        Id = "zt",
        DisplayName = "ZT Class",
        Description = "Armored ZT-class starfighter, balanced between firepower and protection.",
        Role = FrameRole.Balanced,
        MaxHp = 500,
        MaxShield = 250,
        ShieldRegenPerTurn = 25,
        ShotDamage = 45,
        Accuracy = 0.82f,
        Evasion = 0.15f,
        NormalTurnLimitDegrees = ZtTurn,
        NormalMoveMinDistance = ZtMinMove,
        NormalMoveMaxDistance = ZtMaxMove,
        Maneuvers = new ShipManeuverProfile
        {
            RotatingGunsMoveDistance = 80f,
            RotatingGunsFireConeDeg = 45f,
            SuppressionTurnPenaltyDeg = 8f,
            EmergencyThrustersMoveDistance = 320f,
            EmergencyThrustersTurnLimitDegrees = 35f,
            EmergencyThrustersEvasionPenalty = 0.10f,
            Pool = new[] { ShipAbility.RotatingGuns, ShipAbility.SuppressionFire, ShipAbility.EmergencyThrusters },
        },
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

    /// <summary>The ZT line's attack frame: heavier guns, less armor.</summary>
    public static readonly ShipType Zt6 = new()
    {
        Id = "zt_6",
        DisplayName = "ZT-6",
        Description = "A slow armored gunship that trades armor for heavier guns to pin enemies down.",
        Role = FrameRole.Attack,
        MaxHp = 400,
        MaxShield = 200,
        ShotDamage = 60,
        Accuracy = 0.85f,
        Evasion = 0.15f,
        NormalTurnLimitDegrees = ZtTurn,
        NormalMoveMinDistance = ZtMinMove,
        NormalMoveMaxDistance = ZtMaxMove,
        Maneuvers = Zt.Maneuvers,
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
    /// <summary>The ZT line's guard frame: more armor and faster shield regeneration, weaker guns.</summary>
    public static readonly ShipType Zt8Bulwark = new()
    {
        Id = "zt_8_bulwark",
        DisplayName = "ZT-8 Bulwark",
        Description = "A reinforced fleet defender that trades gun output for armor and faster shield regeneration.",
        Role = FrameRole.Guard,
        MaxHp = 580,
        MaxShield = 320,
        ShieldRegenPerTurn = 30,
        ShotDamage = 40,
        Accuracy = 0.82f,
        Evasion = 0.15f,
        NormalTurnLimitDegrees = ZtTurn,
        NormalMoveMinDistance = ZtMinMove,
        NormalMoveMaxDistance = ZtMaxMove,
        Maneuvers = Zt.Maneuvers,
        SkinsByTeam = Zt.SkinsByTeam,
    };

    /// <summary>
    /// Every frame a pilot can fly, grouped by class line: balanced, attack,
    /// guard. Within a line, attack and guard move about a fifth of the
    /// balanced frame's firepower (damage x accuracy) into toughness ((hull +
    /// shield) / (1 - evasion)) or back, so none is a straight upgrade. A
    /// pilot's frame is chosen when they join.
    /// </summary>
    public static readonly ShipType[] PlayerFrames = { Scout, ScoutStriker, ScoutGhost, Raptor, BlackHawk, Falcon, Zt, Zt6, Zt8Bulwark };
    public static readonly ShipType[] SandboxHulls = PlayerFrames;

    /// <summary>Resolves a data-authored hull id. Invalid or retired hull IDs fall back to the S1 Kestrel.</summary>
    public static ShipType FromId(string id) => PlayerFrames.FirstOrDefault(type => type.Id == id) ?? Scout;

    /// <summary>A class line's name, for grouping frames: Kestrel, Raptor or ZT.</summary>
    public static string ClassName(string classId) => classId switch
    {
        "raptor" => "Raptor",
        "zt" => "ZT",
        _ => "Kestrel",
    };

    public static string ClassIdForHull(string hullId) => hullId switch
    {
        "raptor" or "r3_black_hawk" or "r5_falcon" => "raptor",
        "zt" or "zt_6" or "zt_8_bulwark" => "zt",
        _ => "scout",
    };
}

public enum BattleMode { Quick, Run }

/// <summary>Carries the chosen squad and mission into the battle scene.</summary>
public static class GameSetup
{
    public static List<Pilot> PlayerPilots = new();
    public static BattleMode Mode { get; private set; } = BattleMode.Quick;
    public static bool IsTestBattle => Mode == BattleMode.Quick;
    /// <summary>The run battle being fought; null in a quick battle.</summary>
    public static BattleMission Mission { get; private set; }
    public static BattleMapDefinition TestBattleMap { get; private set; }
    /// <summary>The enemy force in a quick battle: a late sector 3 patrol for max-level pilots.</summary>
    public static BattleMission QuickBattleForces { get; private set; }

    /// <summary>A disposable max-level squadron for a quick battle.</summary>
    public static void StartTestBattle(IEnumerable<ShipType> ships, BattleMapDefinition map)
    {
        string[] callsigns = { "ALPHA", "BRAVO", "CHARLIE" };
        PlayerPilots = ships.Select((ship, index) =>
        {
            var pilot = new Pilot(callsigns[index], ship) { Level = Pilot.MaxLevel };
            pilot.RestoreManeuvers(pilot.Ship.ManeuverPool);
            return pilot;
        }).ToList();
        Mode = BattleMode.Quick;
        Mission = null;
        TestBattleMap = map;
        QuickBattleForces = RunContent.QuickBattleForces(((ulong)GD.Randi() << 32) | GD.Randi());
    }

    public static void StartRunBattle(List<Pilot> squad, BattleMission mission)
    {
        PlayerPilots = squad.ToList();
        Mode = BattleMode.Run;
        Mission = mission;
        TestBattleMap = null;
    }
}
