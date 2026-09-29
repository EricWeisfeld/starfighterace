using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Textures + sheet layout for one faction's fighter.
/// </summary>
public class FighterSkin
{
    public Texture2D Base;
    public Texture2D Engine;
    public Texture2D Weapon;
    public Texture2D Shield;
    public Texture2D Destruction;
    public int EngineFrames;
    public int WeaponFrames;
    public int ShieldFrames;
    public int DestructionFrames;
}

/// <summary>The move currently prepared for a fighter.</summary>
public enum ManeuverType
{
    Normal,
    UTurn,
    BreakTurn,
    EngineBoost,
    RotatingGuns,
    EmergencyThrusters,
    SnapTurn,
    PursuitBurn,
    EcmJink,
    GhostRun,
    EvasiveDodge,
}

/// <summary>
/// One star fighter. Ships always move forward along a fixed-distance path each
/// turn. Most paths are constant-rate arcs: higher maximum turn makes a tighter
/// turn, while a larger turn radius makes a wider turn. Heading is in radians
/// where 0 = +X; the sprite art faces up, so the visual child is pre-rotated +90 deg.
/// </summary>
public partial class Fighter : Node2D
{
    public const int SpecialManeuverCooldownRounds = 1;
    public const int EngineBoostCooldownRounds = 2;
    public const int GhostRunCooldownRounds = 2;
    public const float FireRange = 280f;
    public const float FireConeDeg = 12f;     // half-angle of the forward cone

    // Normal-flight handling: maximum total heading change for one normal move.
    // Higher values permit tighter turns; turn radius is derived from path distance / turn angle.
    public float NormalTurnLimitDegrees = 110f;
    // Experimental radius constraint; every current hull leaves this at zero.
    // Do not tune accidentally: a positive value forces wider normal turns.
    public float NormalMinimumTurnRadius;

    // A ship can never stop; its planned distance is clamped to this range.
    public float NormalMoveMinDistance = 160f;
    public float NormalMoveMaxDistance = 230f;
    // Path distance applies to every maneuver; normal distance persists between turns.
    public float PlannedPathDistance = 230f;
    public float SelectedNormalMoveDistance = 230f;

    // Per-fighter combat stats — tune freely (or vary per ship type later).
    public int MaxHp = 30;
    public int MaxShield = 15;
    public int ShieldRegenPerTurn = 2;
    public int ShotDamage = 4;
    public float Accuracy = 0.85f;            // base chance each shot connects
    public float Evasion = 0.25f;             // reduces attackers' hit chance
    public float FireCooldown = 0.6f;         // delay between barrages
    public int BarrageMin = 3;                // shots per barrage
    public int BarrageMax = 5;
    public bool CanFire = true;

    public ShipType Type;
    public Pilot Pilot;                       // persistent campaign pilot; null for enemies
    public Texture2D BaseTexture;             // for ghost previews
    public int Team;                          // 0 = player, 1 = enemy
    public float Heading;
    public int Hp;
    public int Shield;
    public float? PlannedTurnAngleRadians;   // signed heading change; null = no order yet
    public ManeuverType PlannedManeuver = ManeuverType.Normal;
    public Vector2 Velocity;
    public float Cooldown;

    // Battle record, read by the perk system when the fight ends.
    public int TurnOneDamage;                 // hull lost during the first turn where anyone takes damage
    public int EdgeHits;                      // hits landed from the edge of firing range
    public int ShotsFired;
    public int HitsLanded;
    public int Kills;
    public int LowHullKills;
    public int ShieldDamageTurns;
    public int FullThrottleNormalMoveCount;
    public bool TookDamage;
    public bool HullDamageTaken;
    public bool DroppedBelowQuarterHull;
    public bool HitAsteroid;
    public bool Ejected;
    public Fighter HunterLockTarget;
    public int HunterLockCooldownTurns;
    public int SensorScrambleCooldownTurns;
    public int SensorScrambleTurns;
    public float SensorScrambleAccuracyPenalty;
    public float NormalTurnLimitPenaltyDegrees;

    // In-progress barrage state, driven by BattleManager during execution.
    public int BarrageShotsLeft;
    public float BarrageShotTimer;
    public Fighter BarrageTarget;

    // The greatest scrape damage reached against each asteroid this maneuver.
    // Subsequent frames only apply the difference when the ship hits deeper.
    readonly Dictionary<TerrainFeature, int> _asteroidScrapeDamage = new();
    readonly Dictionary<ManeuverType, int> _maneuverCooldownTurns = new();

    public bool IsAlive => Hp > 0;

    Node2D _visual;
    Sprite2D _baseSprite, _engineSprite, _weaponSprite, _shieldSprite, _destructSprite;
    int _engineFrames, _weaponFrames, _shieldFrames, _destructFrames;
    double _enginePhase;
    float _fireAnimT = -1f;                   // <0 = weapon animation idle
    float _fireAnimDur;
    float _shieldAnimT = -1f;                 // <0 = shield animation idle
    bool _dying;
    double _dieT;
    Vector2 _startPos;
    float _startHeading;
    float _execTurn;
    float _execDist;
    float _execProgress;
    ManeuverType _execManeuver;
    int _lastShieldDamageTurn = -1;
    float _damageCarry;

    public bool HasPerk(Perk perk) => Pilot != null && Pilot.Perks.Contains(perk);
    public bool SurvivorsGuiltActive => HasPerk(Perks.SurvivorsGuilt) &&
        BattleManager.Instance?.GetTeam(Team).Any(other => other != this && other.Pilot != null && !other.IsAlive) == true;

    public float EffectiveFireRange => Perks.EffectiveFireRange(Pilot, FireRange, HitsLanded == 0);
    public float EffectiveFireCooldown => FireCooldown /
        ((HasPerk(Perks.RapidFire) ? 1f + Perks.RapidFireRateBonus : 1f) *
         (HasPerk(Perks.GunShy) ? 1f - Perks.GunShyFireRatePenalty : 1f));
    public float EjectChance => Perks.BaseEjectChance + (HasPerk(Perks.Survivor) ? Perks.SurvivorEjectBonus : 0f);
    public float EngineBoostTurnLimitDegrees => Mathf.Max(0f, Type.EngineBoostTurnLimitDegrees);
    public float PursuitBurnTurnLimitDegrees => Mathf.Max(0f, Type.PursuitBurnTurnLimitDegrees);
    public float EcmJinkTurnLimitDegrees => Mathf.Max(0f, Type.EcmJinkTurnLimitDegrees);
    public float GhostRunTurnLimitDegrees => Mathf.Max(0f, Type.GhostRunTurnLimitDegrees);
    public float EmergencyThrustersTurnLimitDegrees => Mathf.Max(0f, Type.EmergencyThrustersTurnLimitDegrees);
    /// <summary>Normal-flight maximum turn for the currently planned normal distance.</summary>
    public float PlannedNormalTurnLimitDegrees => GetNormalTurnLimitDegrees(PlannedPathDistance);

    /// <summary>
    /// Effective normal-flight maximum turn. Higher degrees mean tighter turns.
    /// An optional minimum turn radius can impose an additional, distance-based cap.
    /// </summary>
    public float GetNormalTurnLimitDegrees(float pathDistance)
    {
        float turnLimitDegrees = Mathf.Max(25f,
            Perks.ApplyNormalTurnLimitModifiers(Pilot, NormalTurnLimitDegrees - NormalTurnLimitPenaltyDegrees,
                Mathf.IsEqualApprox(pathDistance, NormalMoveMaxDistance)));
        if (NormalMinimumTurnRadius > 0f)
        {
            // radius = path distance / turn angle (radians), therefore the
            // radius constraint limits turn angle to path distance / radius.
            float radiusTurnLimitDegrees = Mathf.RadToDeg(pathDistance / NormalMinimumTurnRadius);
            turnLimitDegrees = Mathf.Min(turnLimitDegrees, radiusTurnLimitDegrees);
        }
        return turnLimitDegrees;
    }
    public bool IsRotatingGunsActive => (BattleManager.Instance?.CurrentPhase == BattleManager.Phase.Executing
        ? _execManeuver : PlannedManeuver) == ManeuverType.RotatingGuns;
    public bool IsEmergencyThrustersActive => (BattleManager.Instance?.CurrentPhase == BattleManager.Phase.Executing
        ? _execManeuver : PlannedManeuver) == ManeuverType.EmergencyThrusters;
    public bool IsEcmJinkActive => (BattleManager.Instance?.CurrentPhase == BattleManager.Phase.Executing
        ? _execManeuver : PlannedManeuver) == ManeuverType.EcmJink;
    public bool IsGhostRunActive => (BattleManager.Instance?.CurrentPhase == BattleManager.Phase.Executing
        ? _execManeuver : PlannedManeuver) == ManeuverType.GhostRun;
    public bool IsEvasiveDodgeActive => (BattleManager.Instance?.CurrentPhase == BattleManager.Phase.Executing
        ? _execManeuver : PlannedManeuver) == ManeuverType.EvasiveDodge;
    public float EffectiveFireConeDeg => IsRotatingGunsActive ? Type.RotatingGunsFireConeDeg : FireConeDeg;
    public float EffectiveEvasion => Mathf.Clamp(Evasion
        - (IsEmergencyThrustersActive ? Type.EmergencyThrustersEvasionPenalty : 0f)
        + (IsEcmJinkActive ? Type.EcmJinkEvasionBonus : 0f)
        + (IsGhostRunActive ? Type.GhostRunEvasionBonus : 0f)
        + (IsEvasiveDodgeActive ? Type.EvasiveDodgeEvasionBonus : 0f)
        + (HasPerk(Perks.Phantom) && !TookDamage ? Perks.PhantomEvasionBonus : 0f)
        - (HasPerk(Perks.Rattled) ? Perks.RattledEvasionPenalty : 0f)
        - (SurvivorsGuiltActive ? Perks.SurvivorsGuiltCombatPenalty : 0f), 0f, 0.95f);
    /// <summary>Move distance currently planned for this turn.</summary>
    public bool HasAbility(ShipAbility ability) =>
        Type != null && (Pilot != null ? Pilot.HasManeuver(ability) : Type.OffersManeuver(ability));

    /// <summary>Special maneuvers cannot be repeated during their per-fighter cooldown.</summary>
    public bool IsManeuverReady(ManeuverType maneuver) =>
        maneuver == ManeuverType.Normal || GetManeuverCooldownTurns(maneuver) == 0;

    public int GetManeuverCooldownTurns(ManeuverType maneuver) =>
        _maneuverCooldownTurns.GetValueOrDefault(ManeuverCooldownKey(maneuver));

    /// <summary>Counts down cooldowns that were active during the round that just ended.</summary>
    public void AdvanceManeuverCooldowns()
    {
        foreach (ManeuverType maneuver in new List<ManeuverType>(_maneuverCooldownTurns.Keys))
        {
            int remaining = _maneuverCooldownTurns[maneuver] - 1;
            if (remaining <= 0)
                _maneuverCooldownTurns.Remove(maneuver);
            else
                _maneuverCooldownTurns[maneuver] = remaining;
        }
    }

    /// <summary>Starts the cooldown for the special maneuver completed this round.</summary>
    public void StartManeuverCooldown(ManeuverType maneuver)
    {
        ManeuverType key = ManeuverCooldownKey(maneuver);
        if (key != ManeuverType.Normal)
            _maneuverCooldownTurns[key] = key switch
            {
                ManeuverType.EngineBoost => EngineBoostCooldownRounds,
                ManeuverType.GhostRun => GhostRunCooldownRounds,
                _ => SpecialManeuverCooldownRounds,
            };
    }

    static ManeuverType ManeuverCooldownKey(ManeuverType maneuver) => maneuver;

    public float EffectiveAccuracyAgainst(Fighter target)
    {
        float accuracy = Accuracy
            + (HasPerk(Perks.Deadeye) ? Perks.DeadeyeAccuracyBonus : 0f)
            + (HasPerk(Perks.Hotshot) ? Perks.HotshotAccuracyBonus : 0f)
            - (HasPerk(Perks.ShakyHands) ? Perks.ShakyHandsAccuracyPenalty : 0f)
            - (SurvivorsGuiltActive ? Perks.SurvivorsGuiltCombatPenalty : 0f)
            + (HasPerk(Perks.CoolUnderFire) && Hp < MaxHp * Perks.CoolUnderFireThreshold ? Perks.CoolUnderFireAccuracyBonus : 0f)
            + (target != null && target == HunterLockTarget && HasAbility(ShipAbility.HunterLock) ? Type.HunterLockAccuracyBonus : 0f);
        accuracy *= 1f - SensorScrambleAccuracyPenalty;
        if (HasPerk(Perks.RapidFire))
            accuracy *= Perks.RapidFireAccuracyMultiplier;
        return Mathf.Clamp(accuracy, 0f, 1f);
    }

    public int RollShotDamage(Fighter target = null)
    {
        float multiplier = Perks.DamageMultiplierAgainst(Pilot, target);
        if (Mathf.IsEqualApprox(multiplier, 1f))
            return ShotDamage;
        float scaledDamage = ShotDamage * multiplier + _damageCarry;
        int damage = Mathf.FloorToInt(scaledDamage);
        _damageCarry = scaledDamage - damage;
        return damage;
    }

    public void RecordHit(bool killed)
    {
        HitsLanded++;
        if (!killed)
            return;
        Kills++;
        if (Hp < MaxHp * Perks.CoolUnderFireEarnThreshold)
            LowHullKills++;
    }

    public void SetPlannedMoveDistance(float distance)
    {
        PlannedPathDistance = Mathf.Clamp(distance, NormalMoveMinDistance, NormalMoveMaxDistance);
        if (PlannedManeuver == ManeuverType.Normal)
            SelectedNormalMoveDistance = PlannedPathDistance;
    }

    /// <summary>Prepare this hull's fixed-distance 180-degree maneuver.</summary>
    public void PlanUTurn(float direction)
    {
        PlannedManeuver = ManeuverType.UTurn;
        PlannedPathDistance = Type.UTurnMoveDistance;
        PlannedTurnAngleRadians = Mathf.Sign(direction) * Mathf.Pi;
    }

    /// <summary>Prepare this class's wide, curved 180-degree break turn.</summary>
    public void PlanBreakTurn(float direction)
    {
        PlannedManeuver = ManeuverType.BreakTurn;
        PlannedPathDistance = Type.BreakTurnMoveDistance;
        PlannedTurnAngleRadians = Mathf.Sign(direction) * Mathf.Pi;
    }

    public void PlanSnapTurn(float direction)
    {
        PlannedManeuver = ManeuverType.SnapTurn;
        PlannedPathDistance = Type.SnapTurnMoveDistance;
        PlannedTurnAngleRadians = Mathf.Sign(direction) * Mathf.DegToRad(Type.SnapTurnAngleDegrees);
    }

    /// <summary>Prepare the class's long-range engine boost.</summary>
    public void PlanEngineBoost(float turn)
    {
        PlannedManeuver = ManeuverType.EngineBoost;
        PlannedPathDistance = Type.EngineBoostMoveDistance;
        PlannedTurnAngleRadians = Mathf.Clamp(turn,
            -Mathf.DegToRad(EngineBoostTurnLimitDegrees),
            Mathf.DegToRad(EngineBoostTurnLimitDegrees));
    }

    public void PlanPursuitBurn(float turn)
    {
        PlannedManeuver = ManeuverType.PursuitBurn;
        PlannedPathDistance = Type.PursuitBurnMoveDistance;
        PlannedTurnAngleRadians = Mathf.Clamp(turn,
            -Mathf.DegToRad(PursuitBurnTurnLimitDegrees),
            Mathf.DegToRad(PursuitBurnTurnLimitDegrees));
    }

    public void PlanEcmJink(float turn)
    {
        PlannedManeuver = ManeuverType.EcmJink;
        PlannedPathDistance = Type.EcmJinkMoveDistance;
        PlannedTurnAngleRadians = Mathf.Clamp(turn,
            -Mathf.DegToRad(EcmJinkTurnLimitDegrees),
            Mathf.DegToRad(EcmJinkTurnLimitDegrees));
    }

    public void PlanGhostRun(float turn)
    {
        PlannedManeuver = ManeuverType.GhostRun;
        PlannedPathDistance = Type.GhostRunMoveDistance;
        PlannedTurnAngleRadians = Mathf.Clamp(turn,
            -Mathf.DegToRad(GhostRunTurnLimitDegrees),
            Mathf.DegToRad(GhostRunTurnLimitDegrees));
    }

    /// <summary>Prepare the Raptor line's fixed sharp turn and short forward burst.</summary>
    public void PlanEvasiveDodge(float direction)
    {
        PlannedManeuver = ManeuverType.EvasiveDodge;
        PlannedPathDistance = Type.EvasiveDodgeMoveDistance;
        PlannedTurnAngleRadians = Mathf.Sign(direction) * Mathf.DegToRad(Type.EvasiveDodgeAngleDegrees);
    }

    public bool SetHunterLock(Fighter target)
    {
        if (!HasAbility(ShipAbility.HunterLock) || HunterLockCooldownTurns > 0 || target == null || !target.IsAlive || target.Team == Team)
            return false;
        HunterLockTarget = target;
        HunterLockCooldownTurns = Type.HunterLockCooldownTurns;
        return true;
    }

    public void AdvanceHunterLockCooldown()
    {
        HunterLockCooldownTurns = Mathf.Max(0, HunterLockCooldownTurns - 1);
        if (HunterLockTarget != null && !HunterLockTarget.IsAlive)
            HunterLockTarget = null;
    }

    public bool ApplySensorScramble(Fighter target)
    {
        if (!HasAbility(ShipAbility.SensorScramble) || SensorScrambleCooldownTurns > 0 || target == null || !target.IsAlive || target.Team == Team)
            return false;
        target.SensorScrambleTurns = Type.SensorScrambleDurationTurns;
        target.SensorScrambleAccuracyPenalty = Mathf.Max(target.SensorScrambleAccuracyPenalty, Type.SensorScrambleAccuracyPenalty);
        SensorScrambleCooldownTurns = Type.SensorScrambleCooldownTurns;
        return true;
    }

    public void AdvanceTacticalEffects()
    {
        AdvanceHunterLockCooldown();
        SensorScrambleCooldownTurns = Mathf.Max(0, SensorScrambleCooldownTurns - 1);
        if (SensorScrambleTurns > 0 && --SensorScrambleTurns == 0)
            SensorScrambleAccuracyPenalty = 0f;
    }

    /// <summary>Prepare a short straight advance while the turret tracks a broad firing arc.</summary>
    public void PlanRotatingGuns()
    {
        PlannedManeuver = ManeuverType.RotatingGuns;
        PlannedPathDistance = Type.RotatingGunsMoveDistance;
        PlannedTurnAngleRadians = 0f;
    }

    /// <summary>Prepare the ZT-6's high-speed emergency burst.</summary>
    public void PlanEmergencyThrusters(float turn)
    {
        PlannedManeuver = ManeuverType.EmergencyThrusters;
        PlannedPathDistance = Type.EmergencyThrustersMoveDistance;
        PlannedTurnAngleRadians = Mathf.Clamp(turn,
            -Mathf.DegToRad(EmergencyThrustersTurnLimitDegrees),
            Mathf.DegToRad(EmergencyThrustersTurnLimitDegrees));
    }

    /// <summary>Applies a persistent maneuvering penalty, capped so a ship can still turn.</summary>
    public void ApplySuppression(float penalty)
    {
        NormalTurnLimitPenaltyDegrees = Mathf.Min(NormalTurnLimitDegrees - 25f,
            NormalTurnLimitPenaltyDegrees + penalty);
    }

    /// <summary>Remove the current order and restore the normal throttle, if needed.</summary>
    public void ClearPlannedManeuver()
    {
        if (PlannedManeuver != ManeuverType.Normal)
            PlannedPathDistance = SelectedNormalMoveDistance;
        PlannedManeuver = ManeuverType.Normal;
        PlannedTurnAngleRadians = null;
    }

    /// <summary>Copy a hull class's stat block onto this fighter. Call before Setup.</summary>
    public void ApplyType(ShipType type)
    {
        Type = type;
        MaxHp = type.MaxHp;
        MaxShield = type.MaxShield;
        ShieldRegenPerTurn = type.ShieldRegenPerTurn;
        ShotDamage = type.ShotDamage;
        Accuracy = type.Accuracy;
        Evasion = type.Evasion;
        NormalTurnLimitDegrees = type.NormalTurnLimitDegrees;
        NormalMoveMinDistance = type.NormalMoveMinDistance;
        NormalMoveMaxDistance = type.NormalMoveMaxDistance;
        NormalMinimumTurnRadius = type.NormalMinimumTurnRadius;
        SelectedNormalMoveDistance = NormalMoveMaxDistance;
        PlannedPathDistance = NormalMoveMaxDistance;
    }

    /// <summary>Attach the campaign pilot flying this hull. Call after ApplyType, before Setup.</summary>
    public void ApplyPilot(Pilot pilot)
    {
        Pilot = pilot;
        if (Pilot == null)
            return;
        if (Pilot.HasUpgrade(ShipUpgrade.ShieldsCapacity))
            MaxShield += ShipUpgrades.ShieldCapacityBonus;
        if (Pilot.HasUpgrade(ShipUpgrade.ShieldsRegen))
            ShieldRegenPerTurn += ShipUpgrades.ShieldRegenBonus;
        if (Pilot.HasUpgrade(ShipUpgrade.EngineSpeed))
            NormalMoveMaxDistance += ShipUpgrades.NormalMoveLimitBonus;
        if (Pilot.HasUpgrade(ShipUpgrade.EngineTurn))
            NormalTurnLimitDegrees += ShipUpgrades.NormalTurnLimitBonusDegrees;
        if (Pilot.HasUpgrade(ShipUpgrade.GunsAccuracy))
            Accuracy += ShipUpgrades.AccuracyBonus;
        if (Pilot.HasUpgrade(ShipUpgrade.GunsExtraShot))
        {
            BarrageMin += ShipUpgrades.ExtraShots;
            BarrageMax += ShipUpgrades.ExtraShots;
        }
        MaxHp = Perks.EffectiveMaxHp(Pilot, MaxHp);
        ShieldRegenPerTurn = Perks.EffectiveShieldRegen(Pilot, ShieldRegenPerTurn);
        NormalMoveMaxDistance = Perks.ApplyNormalMoveLimitModifiers(Pilot, NormalMoveMaxDistance);
        SelectedNormalMoveDistance = NormalMoveMaxDistance;
        PlannedPathDistance = NormalMoveMaxDistance;
    }

    /// <summary>Scales only combat values, preserving a hull's flight handling and identity.</summary>
    public void ApplyCombatStatMultiplier(float multiplier)
    {
        MaxHp = Mathf.RoundToInt(MaxHp * multiplier);
        MaxShield = Mathf.RoundToInt(MaxShield * multiplier);
        ShieldRegenPerTurn = Mathf.RoundToInt(ShieldRegenPerTurn * multiplier);
        ShotDamage = Mathf.RoundToInt(ShotDamage * multiplier);
    }

    public void Setup(int team, Vector2 pos, float heading, FighterSkin skin)
    {
        Team = team;
        BaseTexture = skin.Base;
        Position = pos;
        Heading = heading;
        Rotation = heading;
        Hp = Pilot == null ? MaxHp : Mathf.Clamp(MaxHp - Pilot.HullDamage, 1, MaxHp);
        Shield = MaxShield;
        _engineFrames = skin.EngineFrames;
        _weaponFrames = skin.WeaponFrames;
        _shieldFrames = skin.ShieldFrames;
        _destructFrames = skin.DestructionFrames;
        _enginePhase = GD.RandRange(0.0, 10.0); // desync flame flicker across ships

        _visual = new Node2D { RotationDegrees = 90 };
        AddChild(_visual);
        _engineSprite = new Sprite2D { Texture = skin.Engine, Hframes = skin.EngineFrames };
        _baseSprite = new Sprite2D { Texture = skin.Base };
        _weaponSprite = new Sprite2D { Texture = skin.Weapon, Hframes = skin.WeaponFrames, Visible = CanFire };
        _shieldSprite = new Sprite2D { Texture = skin.Shield, Hframes = Mathf.Max(1, skin.ShieldFrames), Visible = false };
        _destructSprite = new Sprite2D { Texture = skin.Destruction, Hframes = skin.DestructionFrames, Visible = false };
        _visual.AddChild(_engineSprite);
        _visual.AddChild(_baseSprite);
        _visual.AddChild(_shieldSprite);
        _visual.AddChild(_weaponSprite);
        _visual.AddChild(_destructSprite);
    }

    public override void _Process(double delta)
    {
        if (_dying)
        {
            _dieT += delta;
            int frame = (int)(_dieT * 14.0);
            if (frame >= _destructFrames)
            {
                _destructSprite.Visible = false;
                _dying = false;
            }
            else
            {
                _destructSprite.Frame = frame;
            }
        }

        if (!IsAlive)
            return;

        var mgr = BattleManager.Instance;
        bool executing = mgr != null && mgr.CurrentPhase == BattleManager.Phase.Executing;

        // Engine flame reflects the planned distance while aiming and the
        // locked distance while the fighter is in flight.
        float moveFraction = Mathf.InverseLerp(NormalMoveMinDistance, NormalMoveMaxDistance,
            executing ? _execDist : PlannedPathDistance);
        _enginePhase += delta * (executing ? 12.0 + 4.0 * moveFraction : 7.0);
        _engineSprite.Frame = (int)_enginePhase % _engineFrames;
        _engineSprite.Scale = new Vector2(1f, 0.85f + 0.15f * moveFraction);

        // Bank into turns: roll (wingspan squash) eases in and back out over the arc.
        float bank = 0f;
        if (executing)
            bank = Mathf.Min(1f, Mathf.Abs(_execTurn) / Mathf.DegToRad(NormalTurnLimitDegrees)) * Mathf.Sin(Mathf.Pi * _execProgress);
        _visual.Scale = new Vector2(1f - 0.25f * bank, 1f);

        // Weapon firing animation, one pass per barrage.
        if (_fireAnimT >= 0f)
        {
            _fireAnimT += (float)delta;
            int frame = (int)(_fireAnimT / _fireAnimDur * _weaponFrames);
            if (frame >= _weaponFrames)
            {
                _weaponSprite.Frame = 0;
                _fireAnimT = -1f;
            }
            else
            {
                _weaponSprite.Frame = frame;
            }
        }

        if (_shieldAnimT >= 0f)
        {
            _shieldAnimT += (float)delta;
            int frame = (int)(_shieldAnimT * 24f);
            if (frame >= _shieldFrames)
            {
                _shieldSprite.Visible = false;
                _shieldAnimT = -1f;
            }
            else
            {
                _shieldSprite.Frame = frame;
            }
        }
    }

    /// <summary>Play the weapon sheet once, stretched over the barrage duration.</summary>
    public void PlayFireAnimation(float duration)
    {
        if (!CanFire)
            return;
        _fireAnimT = 0f;
        _fireAnimDur = Mathf.Max(duration, 0.15f);
    }

    /// <summary>Play this hull's shield impact animation from its first frame.</summary>
    void PlayShieldAnimation()
    {
        if (_shieldFrames <= 0 || _shieldSprite == null)
            return;
        _shieldAnimT = 0f;
        _shieldSprite.Frame = 0;
        _shieldSprite.Visible = true;
    }

    public void BeginExecute()
    {
        _startPos = Position;
        _startHeading = Heading;
        _execTurn = PlannedTurnAngleRadians ?? 0f;
        _execDist = PlannedPathDistance;
        _execProgress = 0f;
        _execManeuver = PlannedManeuver;
        if (_execManeuver == ManeuverType.Normal)
        {
            float maxTurn = Mathf.DegToRad(GetNormalTurnLimitDegrees(_execDist));
            _execTurn = Mathf.Clamp(_execTurn, -maxTurn, maxTurn);
        }
        if (_execManeuver == ManeuverType.Normal && Mathf.IsEqualApprox(PlannedPathDistance, NormalMoveMaxDistance))
            FullThrottleNormalMoveCount++;
        Cooldown = (float)GD.RandRange(0.0, 0.15); // stagger opening barrages
        BarrageShotsLeft = 0;
        BarrageTarget = null;
        _asteroidScrapeDamage.Clear();
    }

    /// <summary>Records an asteroid's peak scrape damage and returns the newly incurred amount.</summary>
    public int UpdateAsteroidScrapeDamage(TerrainFeature asteroid, int totalDamage)
    {
        int previousDamage = _asteroidScrapeDamage.GetValueOrDefault(asteroid);
        if (totalDamage <= previousDamage)
            return 0;
        _asteroidScrapeDamage[asteroid] = totalDamage;
        return totalDamage - previousDamage;
    }

    public void RecordAsteroidHit() => HitAsteroid = true;

    /// <summary>Place the fighter at fraction s (0..1) along its planned maneuver.</summary>
    public void SetExecuteProgress(float s)
    {
        SetExecuteProgress(s, 1f);
    }

    /// <summary>Advance according to the local terrain speed multiplier.</summary>
    public void AdvanceExecute(float dt, float speedMultiplier)
    {
        SetExecuteProgress(AdvanceManeuverProgress(_execProgress, dt, speedMultiplier), speedMultiplier);
    }

    /// <summary>Advances a maneuver's route fraction using the execution speed rule.</summary>
    public static float AdvanceManeuverProgress(float progress, float dt, float speedMultiplier) =>
        Mathf.Min(progress + dt / BattleManager.ExecTime * speedMultiplier, 1f);

    public bool IsExecutingMove => _execProgress < 1f;

    void SetExecuteProgress(float s, float speedMultiplier)
    {
        _execProgress = s;
        ManeuverPoint(_execManeuver, _startPos, _startHeading, _execTurn, _execDist, s,
            out Vector2 pos, out float heading);
        Position = pos;
        Heading = heading;
        Rotation = heading;
        float travelHeading = _execManeuver == ManeuverType.UTurn ? _startHeading : heading;
        Velocity = Vector2.FromAngle(travelHeading) * (_execDist / BattleManager.ExecTime) * speedMultiplier;
    }

    /// <summary>
    /// Point at fraction s along a maneuver. U-turns thrust straight while rotating;
    /// evasive dodges curve sharply, then make a short straight advance; all other
    /// turns follow constant-rate arcs.
    /// </summary>
    public static void ManeuverPoint(ManeuverType maneuver, Vector2 p0, float h0, float turn, float dist, float s,
        out Vector2 pos, out float heading)
    {
        if (maneuver == ManeuverType.UTurn)
        {
            pos = p0 + Vector2.FromAngle(h0) * dist * s;
            heading = h0 + turn * s;
            return;
        }
        if (maneuver == ManeuverType.EvasiveDodge)
        {
            // Raptor-line ships spend 75 units in the 135-degree turn, then
            // thrust ahead for the remaining 35 units of the 110-unit dodge.
            const float turnDistanceFraction = 75f / 110f;
            float turnDistance = dist * turnDistanceFraction;
            if (s <= turnDistanceFraction)
            {
                ArcPoint(p0, h0, turn, turnDistance, s / turnDistanceFraction, out pos, out heading);
                return;
            }

            ArcPoint(p0, h0, turn, turnDistance, 1f, out Vector2 turnEnd, out heading);
            pos = turnEnd + Vector2.FromAngle(heading) * dist * (s - turnDistanceFraction);
            return;
        }
        ArcPoint(p0, h0, turn, dist, s, out pos, out heading);
    }

    /// <summary>
    /// Point at fraction s along a constant-speed, constant-turn-rate arc:
    /// start pos/heading, total signed turn (radians), total distance.
    /// </summary>
    public static void ArcPoint(Vector2 p0, float h0, float turn, float dist, float s,
        out Vector2 pos, out float heading)
    {
        heading = h0 + turn * s;
        if (Mathf.Abs(turn) < 0.001f)
        {
            pos = p0 + Vector2.FromAngle(h0) * dist * s;
            return;
        }
        float radius = dist / Mathf.Abs(turn);
        Vector2 center = p0 + Vector2.FromAngle(h0).Rotated(Mathf.Sign(turn) * Mathf.Pi / 2f) * radius;
        pos = center + (p0 - center).Rotated(turn * s);
    }

    public void TakeDamage(int dmg)
    {
        if (!IsAlive || dmg <= 0)
            return;
        bool isOpeningDamageTurn = BattleManager.Instance?.IsOpeningDamageTurn() == true;
        TookDamage = true;
        int shieldDamage = Mathf.Min(Shield, dmg);
        if (shieldDamage > 0)
        {
            int turn = BattleManager.Instance?.TurnNumber ?? 0;
            if (_lastShieldDamageTurn != turn)
            {
                _lastShieldDamageTurn = turn;
                ShieldDamageTurns++;
            }
            Shield -= shieldDamage;
            dmg -= shieldDamage;
            PlayShieldAnimation();
        }
        if (dmg <= 0)
            return;

        int hullDamage = Mathf.Min(Hp, dmg);
        Hp -= dmg;
        if (isOpeningDamageTurn)
            TurnOneDamage += hullDamage;
        HullDamageTaken = true;
        if (Hp < MaxHp * Perks.HardToKillEarnThreshold)
            DroppedBelowQuarterHull = true;
        if (Hp <= 0)
        {
            StartDestruction();
            return;
        }
        _baseSprite.Modulate = new Color(4f, 1.2f, 1.2f);
        CreateTween().TweenProperty(_baseSprite, "modulate", Colors.White, 0.3f);
    }

    public void Heal(int amount)
    {
        if (IsAlive)
            Hp = Mathf.Min(Hp + amount, MaxHp);
    }

    /// <summary>Restore the ship's independent shield pool, up to its hull's configured limit.</summary>
    public void RegenerateShield()
    {
        if (IsAlive && ShieldRegenPerTurn > 0)
            Shield = Mathf.Min(Shield + ShieldRegenPerTurn, MaxShield);
    }

    void StartDestruction()
    {
        Hp = 0;
        Velocity = Vector2.Zero;
        PlannedTurnAngleRadians = null;
        _dying = true;
        _dieT = 0;
        _baseSprite.Visible = false;
        _engineSprite.Visible = false;
        _weaponSprite.Visible = false;
        _shieldSprite.Visible = false;
        _visual.Scale = Vector2.One;
        _destructSprite.Visible = true;
        _destructSprite.Frame = 0;

        // Campaign pilots roll to punch out as the ship goes up.
        if (Pilot != null && GD.Randf() < EjectChance)
        {
            Ejected = true;
            GetParent().AddChild(new EjectPod { Position = Position });
        }
    }
}

/// <summary>A drifting escape pod left behind by an ejected pilot.</summary>
public partial class EjectPod : Node2D
{
    Vector2 _drift;
    float _t;

    public override void _Ready()
    {
        _drift = Vector2.FromAngle((float)GD.RandRange(0.0, Mathf.Tau)) * (float)GD.RandRange(20.0, 40.0);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        Position += _drift * (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, 5f, new Color(0.75f, 0.85f, 1f));
        if (Mathf.PosMod(_t, 0.8f) < 0.4f)
            DrawCircle(Vector2.Zero, 2f, new Color(0.3f, 1f, 0.5f)); // rescue beacon blink
        float labelAlpha = Mathf.Clamp(3f - _t, 0f, 1f);
        if (labelAlpha > 0f)
            DrawString(ThemeDB.FallbackFont, new Vector2(-60f, -14f), "EJECTED",
                HorizontalAlignment.Center, 120f, 12, new Color(1f, 1f, 1f, labelAlpha));
    }
}
