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
    /// <summary>Rough radius of the hull art in texture pixels, for placing bars and rings around it.</summary>
    public float HullRadius = 16f;
    /// <summary>The hull art cropped to the ship itself, so a picture of it fills its box.</summary>
    public Texture2D Icon;
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
    PursuitBurn,
    EvasiveDodge,
    EvasiveSpin,
    RearGuns,
    AirBrake,
    Sideslip,
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
    public const int EvasiveSpinCooldownRounds = 2;
    /// <summary>An alpha strike leaves the guns offline next turn, so it comes round every other turn.</summary>
    const int AlphaStrikeCooldownRounds = 2;
    /// <summary>Barrel rolls the ship makes through one evasive spin.</summary>
    const float EvasiveSpinRolls = 3f;
    public const float FireRange = 280f;
    public const float FireConeDeg = 12f;     // half-angle of the forward cone
    /// <summary>This ship's forward-cone half-angle; a wide gun mount widens it.</summary>
    public float BaseFireConeDeg = FireConeDeg;

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
    /// <summary>The throttle the ship started this turn's planning with: all an enemy who can't read orders sees.</summary>
    public float TurnStartPathDistance = 230f;
    public float SelectedNormalMoveDistance = 230f;
    /// <summary>
    /// Share of a maneuver's distance the ship actually covers this turn.
    /// Nebula gas around a ship when the turn starts cuts it; it is set before
    /// planning and holds all turn, so every preview already shows the
    /// shorter route. Throttle, turn limits and maneuver choice are unchanged.
    /// </summary>
    public float RouteScale = 1f;
    public bool InNebula => RouteScale < 1f;

    // Per-fighter combat stats — tune freely (or vary per ship type later).
    public int MaxHp = 300;
    public int MaxShield = 150;
    public int ShieldRegenPerTurn = 2;
    public int ShotDamage = 40;
    public float Accuracy = 0.85f;            // base chance each shot connects
    public float Evasion = 0.25f;             // reduces attackers' hit chance
    public float FireCooldown = 0.6f;         // delay between barrages
    public int BarrageMin = ShipStats.BaseShotsMin;   // shots per barrage
    public int BarrageMax = ShipStats.BaseShotsMax;
    public bool CanFire = true;

    public ShipType Type;
    /// <summary>The maneuver numbers this ship flies with: its hull's, adjusted by its pilot's masteries.</summary>
    public ShipManeuverProfile Moves = ShipManeuverProfile.None;
    public Pilot Pilot;                       // persistent campaign pilot; null for enemies
    /// <summary>An enemy ace's callsign; null for every other ship (see <see cref="Aces"/>).</summary>
    public string AceName;
    public bool IsAce => AceName != null;
    /// <summary>How an enemy pilot fights (see <see cref="EnemyAI"/>). Player ships fly None.</summary>
    public EnemyTactic Tactic;
    /// <summary>A flanker's side of its target: 1 or -1.</summary>
    public int FlankSide = 1;
    /// <summary>The ship an enemy pilot is working on, kept between turns.</summary>
    public Fighter AiTarget;
    /// <summary>Turns in a row an enemy pilot has gone without firing; it grows bolder as they mount.</summary>
    public int AiIdleTurns;
    /// <summary><see cref="ShotsFired"/> when the enemy pilot last planned.</summary>
    public int AiShotsSeen;
    public Texture2D BaseTexture;             // for ghost previews
    public int Team;                          // 0 = player, 1 = enemy
    public float Heading;
    public int Hp;
    public int Shield;
    public float? PlannedTurnAngleRadians;   // signed heading change; null = no order yet
    public ManeuverType PlannedManeuver = ManeuverType.Normal;
    public Vector2 Velocity;
    public float Cooldown;

    // Battle record, read by the debrief and the scar roll when the fight ends.
    public int TurnOneDamage;                 // hull lost during the first turn where anyone takes damage
    public int ShotsFired;
    public int HitsLanded;
    public int Kills;
    public bool TookDamage;
    public bool DroppedBelowQuarterHull;
    public bool HitAsteroid;
    public bool Ejected;
    /// <summary>How many times each instinct or scar made a difference this battle.</summary>
    public readonly Dictionary<Perk, int> TraitTriggers = new();
    public Fighter HunterLockTarget;
    public int HunterLockCooldownTurns;
    public int SensorScrambleCooldownTurns;
    public int SensorScrambleTurns;
    public float SensorScrambleAccuracyPenalty;
    public int ChaffCooldownTurns;
    public int AlphaStrikeCooldownTurns;
    public int TractorCooldownTurns;
    /// <summary>Chaff to drop and an alpha strike to fire this turn, armed while planning.</summary>
    public bool ChaffArmed, AlphaStrikeArmed;
    bool _alphaStrikeThisTurn;
    int _gunsOfflineTurns;
    /// <summary>The guns are cooling after an alpha strike and won't fire this turn.</summary>
    public bool GunsOffline => _gunsOfflineTurns > 0;
    /// <summary>Turns a tractor beam still stops this ship from flying any maneuver but normal flight.</summary>
    public int ManeuverLockTurns;
    public bool IsTractored => ManeuverLockTurns > 0;
    /// <summary>Degrees taken off normal-flight turning by enemy Suppression Fire.</summary>
    public float NormalTurnLimitPenaltyDegrees;
    /// <summary>The part of the suppression penalty added during the turn being flown.</summary>
    float _suppressionThisTurn;
    /// <summary>Share of older suppression that lingers each turn while the ship stays under fire.</summary>
    public const float SuppressionLinger = 0.5f;
    public bool IsSuppressed => NormalTurnLimitPenaltyDegrees >= 0.5f;

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
    Sprite2D _baseSprite, _engineSprite, _weaponSprite, _shieldSprite, _destructSprite, _glowSprite;
    float _hullRadius = 16f;

    /// <summary>
    /// Ship art is drawn larger than its source pixels so hulls read on a
    /// phone. Only the picture grows: collision and shot ranges are unchanged.
    /// </summary>
    public const float FighterArtScale = 1.6f;
    public virtual float ArtScale => FighterArtScale;
    /// <summary>World-space radius of the drawn hull, for bars, rings and labels around it.</summary>
    public float VisualRadius => _hullRadius * ArtScale;
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
    float _damageCarry;
    // Situational-trait state for the turn being flown.
    int _volleysThisTurn;
    int _hullDamageTurn = -1;
    bool _aceResetThisTurn;
    /// <summary>The turn Second Chance was spent in, or -1 while it is still to come.</summary>
    int _secondChanceTurn = -1;
    Fighter _lastVolleyTarget;
    bool _volleySwitchedTarget;
    readonly Dictionary<Perk, int> _calloutTurn = new();

    public bool HasPerk(Perk perk) => Pilot != null && Pilot.Perks.Contains(perk);
    /// <summary>A pilot's masteries; an enemy ace has mastered every maneuver it flies.</summary>
    public bool HasMastered(ShipAbility ability) => Pilot != null ? Pilot.HasMastered(ability) : IsAce && HasAbility(ability);

    // ------------------------------------------------ situational traits
    // Each check below is true only in the moment its instinct or scar
    // applies; the combat math reads them, and NoteTrait records and shows
    // them as they make a difference.

    public bool SurvivorsGuiltActive => HasPerk(Perks.SurvivorsGuilt) &&
        BattleManager.Instance?.GetTeam(Team).Any(other => other != this && other.Pilot != null && !other.IsAlive) == true;
    public bool HesitantActive => HasPerk(Perks.Hesitant) && HitsLanded == 0;
    /// <summary>Until this ship fires in a turn; the whole planning phase counts, since no one has fired yet.</summary>
    public bool StalkerActive => HasPerk(Perks.Stalker) &&
        (_volleysThisTurn == 0 || BattleManager.Instance?.CurrentPhase != BattleManager.Phase.Executing);
    /// <summary>On a turn flown at full throttle, or further with a maneuver.</summary>
    public bool DaredevilActive => HasPerk(Perks.Daredevil) &&
        (BattleManager.Instance?.CurrentPhase == BattleManager.Phase.Executing ? _execDist : PlannedPathDistance) >= NormalMoveMaxDistance - 0.5f;
    public bool CoolUnderFireActive => HasPerk(Perks.CoolUnderFire) && Hp < MaxHp * Perks.CoolUnderFireThreshold;
    public bool RattledActive => HasPerk(Perks.Rattled) && Hp < MaxHp * Perks.RattledThreshold;
    public bool GunShyActive => HasPerk(Perks.GunShy) && _hullDamageTurn == (BattleManager.Instance?.TurnNumber ?? -2);
    public bool TunnelVisionActive => HasPerk(Perks.TunnelVision) && _volleySwitchedTarget;

    /// <summary>True when this ship is behind the target: the target is flying away from it.</summary>
    public bool TailGunnerActiveAgainst(Fighter target)
    {
        if (!HasPerk(Perks.TailGunner) || target == null)
            return false;
        float bearing = (target.Position - Position).Angle();
        return Mathf.Abs(Mathf.Wrap(target.Heading - bearing, -Mathf.Pi, Mathf.Pi)) <= Mathf.DegToRad(Perks.TailGunnerRearAngleDegrees);
    }

    public bool LongShotActiveAgainst(Vector2 targetPosition) =>
        HasPerk(Perks.LongShot) && Position.DistanceTo(targetPosition) >= EffectiveFireRange * Perks.LongShotRangeFraction;

    public bool FinisherActiveAgainst(Fighter target) =>
        HasPerk(Perks.Finisher) && target != null && target.Hp < target.MaxHp * Perks.FinisherHullThreshold;

    public bool BrawlerActiveAgainst(Vector2 targetPosition) =>
        HasPerk(Perks.Brawler) && Position.DistanceTo(targetPosition) <= EffectiveFireRange * Perks.BrawlerRangeFraction;

    public float EffectiveFireRange => FireRange * (HesitantActive ? Perks.HesitantRangeMultiplier : 1f);
    public float EffectiveFireCooldown => FireCooldown;
    public float EjectChance => Perks.BaseEjectChance;
    public float EngineBoostTurnLimitDegrees => Mathf.Max(0f, Moves.EngineBoostTurnLimitDegrees);
    public float PursuitBurnTurnLimitDegrees => Mathf.Max(0f, Moves.PursuitBurnTurnLimitDegrees);
    public float EmergencyThrustersTurnLimitDegrees => Mathf.Max(0f, Moves.EmergencyThrustersTurnLimitDegrees);
    /// <summary>Normal-flight maximum turn for the currently planned normal distance.</summary>
    public float PlannedNormalTurnLimitDegrees => GetNormalTurnLimitDegrees(PlannedPathDistance);

    /// <summary>
    /// Effective normal-flight maximum turn. Higher degrees mean tighter turns.
    /// An optional minimum turn radius can impose an additional, distance-based cap.
    /// </summary>
    public float GetNormalTurnLimitDegrees(float pathDistance)
    {
        float turnLimitDegrees = NormalTurnLimitDegrees - NormalTurnLimitPenaltyDegrees;
        if (HasPerk(Perks.EngineShy) && Mathf.IsEqualApprox(pathDistance, NormalMoveMaxDistance))
            turnLimitDegrees *= Perks.EngineShyFullThrottleTurnMultiplier;
        turnLimitDegrees = Mathf.Max(25f, turnLimitDegrees);
        if (NormalMinimumTurnRadius > 0f)
        {
            // radius = path distance / turn angle (radians), therefore the
            // radius constraint limits turn angle to path distance / radius.
            float radiusTurnLimitDegrees = Mathf.RadToDeg(pathDistance / NormalMinimumTurnRadius);
            turnLimitDegrees = Mathf.Min(turnLimitDegrees, radiusTurnLimitDegrees);
        }
        return turnLimitDegrees;
    }
    /// <summary>The maneuver being flown while a turn executes, or the one planned before it.</summary>
    ManeuverType ActiveManeuver => BattleManager.Instance?.CurrentPhase == BattleManager.Phase.Executing
        ? _execManeuver : PlannedManeuver;
    public bool IsRotatingGunsActive => ActiveManeuver == ManeuverType.RotatingGuns;
    public bool IsEmergencyThrustersActive => ActiveManeuver == ManeuverType.EmergencyThrusters;
    public bool IsEvasiveDodgeActive => ActiveManeuver == ManeuverType.EvasiveDodge;
    /// <summary>An evasive spin: very hard to hit, and the guns stay silent.</summary>
    public bool IsEvasiveSpinActive => ActiveManeuver == ManeuverType.EvasiveSpin;
    public bool IsRearGunsActive => ActiveManeuver == ManeuverType.RearGuns;
    /// <summary>Which way the guns point: astern while the rear guns are on.</summary>
    public float GunHeading => GunHeadingFor(ActiveManeuver, Heading);
    /// <summary>Where the guns point for a ship flying this maneuver on this heading.</summary>
    public static float GunHeadingFor(ManeuverType maneuver, float heading) =>
        maneuver == ManeuverType.RearGuns ? heading + Mathf.Pi : heading;
    /// <summary>Maneuvers whose guns can fire: all but the evasive spin.</summary>
    public static bool GunsFireDuring(ManeuverType maneuver) => maneuver != ManeuverType.EvasiveSpin;
    /// <summary>Whether this ship's guns stay silent while flying a maneuver: a spin, or guns offline after an alpha strike.</summary>
    public bool GunsSilentDuring(ManeuverType maneuver) => !GunsFireDuring(maneuver) || GunsOffline;

    /// <summary>
    /// Maneuvers steered like normal flight: dragging the ghost sets the
    /// throttle and the turn, within the normal turn limit.
    /// </summary>
    public static bool FliesLikeNormal(ManeuverType maneuver) =>
        maneuver is ManeuverType.Normal or ManeuverType.EvasiveSpin or ManeuverType.RearGuns or ManeuverType.AirBrake;
    /// <summary>Share of the normal throttle range a maneuver flown like normal flight covers.</summary>
    public float ThrottleScale(ManeuverType maneuver) =>
        maneuver == ManeuverType.EvasiveSpin ? Moves.EvasiveSpinDistanceScale : 1f;
    /// <summary>The throttle range of normal flight or a maneuver flown like it. An air brake has its own crawl.</summary>
    public float MinMoveFor(ManeuverType maneuver) => maneuver == ManeuverType.AirBrake
        ? Moves.AirBrakeMinDistance : NormalMoveMinDistance * ThrottleScale(maneuver);
    public float MaxMoveFor(ManeuverType maneuver) => maneuver == ManeuverType.AirBrake
        ? Moves.AirBrakeMaxDistance : NormalMoveMaxDistance * ThrottleScale(maneuver);
    public float EffectiveFireConeDeg => IsRotatingGunsActive ? Moves.RotatingGunsFireConeDeg : BaseFireConeDeg;
    public float EffectiveEvasion => Mathf.Clamp(Evasion
        - (IsEmergencyThrustersActive ? Moves.EmergencyThrustersEvasionPenalty : 0f)
        + (IsEvasiveDodgeActive ? Moves.EvasiveDodgeEvasionBonus : 0f)
        + (IsEvasiveSpinActive ? Moves.EvasiveSpinEvasionBonus : 0f)
        + (StalkerActive ? Perks.StalkerEvasionBonus : 0f)
        + (DaredevilActive ? Perks.DaredevilEvasionBonus : 0f)
        - (RattledActive ? Perks.RattledEvasionPenalty : 0f)
        - (SurvivorsGuiltActive ? Perks.SurvivorsGuiltCombatPenalty : 0f), 0f, 0.95f);
    /// <summary>Move distance currently planned for this turn.</summary>
    public bool HasAbility(ShipAbility ability) =>
        Type != null && (Pilot != null
            ? Pilot.HasManeuver(ability)
            : Type.OffersManeuver(ability)
                && System.Array.IndexOf(Type.ManeuverPool, ability) < Mathf.Min(ManeuverAccess, Pilot.MaxManeuvers));

    /// <summary>
    /// For ships without a pilot: how many of the line's maneuvers they can
    /// fly, in pool order. Capped at what a pilot can know.
    /// </summary>
    public int ManeuverAccess = Pilot.MaxManeuvers;

    /// <summary>
    /// Special maneuvers cannot be repeated during their per-fighter cooldown,
    /// and none can be flown while a tractor beam holds the ship.
    /// </summary>
    public bool IsManeuverReady(ManeuverType maneuver) =>
        maneuver == ManeuverType.Normal || (!IsTractored && GetManeuverCooldownTurns(maneuver) == 0);

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

    /// <summary>Turns a special maneuver is locked after use, after masteries.</summary>
    public int CooldownRoundsFor(ManeuverType maneuver)
    {
        ShipAbility? ability = ManeuverCatalog.ForManeuver(maneuver).Ability;
        if (ability is ShipAbility mastered && HasMastered(mastered) && Masteries.CooldownOverride(mastered) is int rounds)
            return rounds;
        return maneuver switch
        {
            ManeuverType.EvasiveSpin => EvasiveSpinCooldownRounds,
            _ => SpecialManeuverCooldownRounds,
        };
    }

    /// <summary>Starts the cooldown for the special maneuver completed this round.</summary>
    public void StartManeuverCooldown(ManeuverType maneuver)
    {
        // An Ace kill this turn wipes the slate, including the maneuver that set it up.
        if (_aceResetThisTurn)
        {
            _aceResetThisTurn = false;
            return;
        }
        ManeuverType key = ManeuverCooldownKey(maneuver);
        int rounds = key == ManeuverType.Normal ? 0 : CooldownRoundsFor(key);
        if (rounds > 0)
            _maneuverCooldownTurns[key] = rounds;
    }

    static ManeuverType ManeuverCooldownKey(ManeuverType maneuver) => maneuver;

    public float EffectiveAccuracyAgainst(Fighter target)
    {
        float accuracy = Accuracy
            - (SurvivorsGuiltActive ? Perks.SurvivorsGuiltCombatPenalty : 0f)
            - (TunnelVisionActive ? Perks.TunnelVisionAccuracyPenalty : 0f)
            + (CoolUnderFireActive ? Perks.CoolUnderFireAccuracyBonus : 0f)
            + (TailGunnerActiveAgainst(target) ? Perks.TailGunnerAccuracyBonus : 0f)
            + (target != null && target == HunterLockTarget && HasAbility(ShipAbility.HunterLock) ? Moves.HunterLockAccuracyBonus : 0f);
        accuracy *= 1f - SensorScrambleAccuracyPenalty;
        return Mathf.Clamp(accuracy, 0f, 1f);
    }

    /// <summary>Damage changes fixed when a shot is fired: a long shot or a rear-gun shot keeps its multiplier in flight.</summary>
    public float FireTimeDamageMultiplier(Vector2 targetPosition) =>
        (LongShotActiveAgainst(targetPosition) ? Perks.LongShotDamageMultiplier : 1f)
        * (IsRearGunsActive ? Moves.RearGunsDamageMultiplier : 1f);

    public int RollShotDamage(Fighter target = null, float fireTimeMultiplier = 1f)
    {
        float multiplier = fireTimeMultiplier * (FinisherActiveAgainst(target) ? Perks.FinisherDamageMultiplier : 1f);
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
        if (HasPerk(Perks.Ace) && IsAlive)
        {
            _maneuverCooldownTurns.Clear();
            HunterLockCooldownTurns = 0;
            SensorScrambleCooldownTurns = 0;
            ChaffCooldownTurns = 0;
            AlphaStrikeCooldownTurns = 0;
            TractorCooldownTurns = 0;
            _aceResetThisTurn = true;
            Shield = MaxShield;
            PlayShieldAnimation();
            NoteTrait(Perks.Ace);
        }
    }

    /// <summary>
    /// Starts a volley at a target and returns how many shots it has, after
    /// the pilot's volley instincts and scars.
    /// </summary>
    public int StartVolley(Fighter target)
    {
        _volleySwitchedTarget = _lastVolleyTarget != null && target != _lastVolleyTarget;
        _lastVolleyTarget = target;
        int shots = GD.RandRange(BarrageMin, BarrageMax);
        if (_alphaStrikeThisTurn)
            shots += Moves.AlphaStrikeExtraShots;
        if (target != null && BrawlerActiveAgainst(target.Position))
        {
            shots += Perks.BrawlerExtraShots;
            NoteTrait(Perks.Brawler);
        }
        if (GunShyActive)
        {
            shots -= Perks.GunShyLostShots;
            NoteTrait(Perks.GunShy);
        }
        if (TunnelVisionActive)
            NoteTrait(Perks.TunnelVision);
        _volleysThisTurn++;
        return Mathf.Max(1, shots);
    }

    /// <summary>Calls out the situational traits in play as this ship fires at a target.</summary>
    public void NoteFiringTraits(Fighter target)
    {
        if (target == null)
            return;
        if (TailGunnerActiveAgainst(target))
            NoteTrait(Perks.TailGunner);
        if (CoolUnderFireActive)
            NoteTrait(Perks.CoolUnderFire);
        if (LongShotActiveAgainst(target.Position))
            NoteTrait(Perks.LongShot);
        if (FinisherActiveAgainst(target))
            NoteTrait(Perks.Finisher);
        if (SurvivorsGuiltActive)
            NoteTrait(Perks.SurvivorsGuilt);
        if (HesitantActive)
            NoteTrait(Perks.Hesitant);
    }

    /// <summary>Calls out the situational traits in play as this ship is shot at.</summary>
    public void NoteDefendingTraits()
    {
        if (StalkerActive)
            NoteTrait(Perks.Stalker);
        if (DaredevilActive)
            NoteTrait(Perks.Daredevil);
        if (RattledActive)
            NoteTrait(Perks.Rattled);
    }

    /// <summary>
    /// Records that a trait made a difference, and shows its name over the
    /// ship the first time it does so each turn.
    /// </summary>
    public void NoteTrait(Perk perk)
    {
        if (!HasPerk(perk))
            return;
        TraitTriggers[perk] = TraitTriggers.GetValueOrDefault(perk) + 1;
        int turn = BattleManager.Instance?.TurnNumber ?? 0;
        if (_calloutTurn.TryGetValue(perk, out int shownTurn) && shownTurn == turn)
            return;
        _calloutTurn[perk] = turn;
        BattleManager.Instance?.ShowCallout(this, perk.Name.ToUpper(), perk.Positive ? TraitCallout.InstinctColor : TraitCallout.ScarColor);
    }

    /// <summary>
    /// Sets the throttle within the planned maneuver's range. For normal
    /// flight and the maneuvers flown like it, the throttle is remembered
    /// across turns and maneuvers.
    /// </summary>
    public void SetPlannedMoveDistance(float distance)
    {
        ManeuverType maneuver = FliesLikeNormal(PlannedManeuver) ? PlannedManeuver : ManeuverType.Normal;
        PlannedPathDistance = Mathf.Clamp(distance, MinMoveFor(maneuver), MaxMoveFor(maneuver));
        // An air brake's crawl is its own range, so it leaves the throttle alone.
        if (FliesLikeNormal(PlannedManeuver) && maneuver != ManeuverType.AirBrake)
            SelectedNormalMoveDistance = PlannedPathDistance / ThrottleScale(maneuver);
    }

    /// <summary>Prepare this hull's fixed-distance 180-degree maneuver.</summary>
    public void PlanUTurn(float direction)
    {
        PlannedManeuver = ManeuverType.UTurn;
        PlannedPathDistance = Moves.UTurnMoveDistance;
        PlannedTurnAngleRadians = Mathf.Sign(direction) * Mathf.Pi;
    }

    /// <summary>Prepare this class's wide, curved 180-degree break turn.</summary>
    public void PlanBreakTurn(float direction)
    {
        PlannedManeuver = ManeuverType.BreakTurn;
        PlannedPathDistance = Moves.BreakTurnMoveDistance;
        PlannedTurnAngleRadians = Mathf.Sign(direction) * Mathf.Pi;
    }

    /// <summary>Prepare the class's long-range engine boost.</summary>
    public void PlanEngineBoost(float turn)
    {
        PlannedManeuver = ManeuverType.EngineBoost;
        PlannedPathDistance = Moves.EngineBoostMoveDistance;
        PlannedTurnAngleRadians = Mathf.Clamp(turn,
            -Mathf.DegToRad(EngineBoostTurnLimitDegrees),
            Mathf.DegToRad(EngineBoostTurnLimitDegrees));
    }

    public void PlanPursuitBurn(float turn)
    {
        PlannedManeuver = ManeuverType.PursuitBurn;
        PlannedPathDistance = Moves.PursuitBurnMoveDistance;
        PlannedTurnAngleRadians = Mathf.Clamp(turn,
            -Mathf.DegToRad(PursuitBurnTurnLimitDegrees),
            Mathf.DegToRad(PursuitBurnTurnLimitDegrees));
    }

    /// <summary>
    /// Prepare a maneuver flown like normal flight (an evasive spin, rear
    /// guns or an air brake) at the current throttle, scaled to its range,
    /// and the current turn within the normal limit.
    /// </summary>
    public void PlanNormalStyle(ManeuverType maneuver, float turn)
    {
        PlannedManeuver = maneuver;
        PlannedPathDistance = Mathf.Clamp(SelectedNormalMoveDistance * ThrottleScale(maneuver), MinMoveFor(maneuver), MaxMoveFor(maneuver));
        float maxTurn = Mathf.DegToRad(GetNormalTurnLimitDegrees(PlannedPathDistance));
        PlannedTurnAngleRadians = Mathf.Clamp(turn, -maxTurn, maxTurn);
    }

    /// <summary>
    /// Prepare a sideslip: the ship slides in a straight line up to its
    /// maximum angle off the nose, and the nose doesn't turn. The planned
    /// "turn" holds the slide's angle.
    /// </summary>
    public void PlanSideslip(float angle)
    {
        float max = Mathf.DegToRad(Moves.SideslipMaxAngleDegrees);
        PlannedManeuver = ManeuverType.Sideslip;
        PlannedPathDistance = Moves.SideslipDistance;
        PlannedTurnAngleRadians = Mathf.Clamp(angle, -max, max);
    }

    /// <summary>Arms or disarms chaff or an alpha strike for this turn.</summary>
    public void ToggleArmed(ManeuverAction action)
    {
        if (action == ManeuverAction.ChaffScreen)
            ChaffArmed = !ChaffArmed;
        else if (action == ManeuverAction.AlphaStrike)
            AlphaStrikeArmed = !AlphaStrikeArmed;
    }

    public bool IsArmed(ManeuverAction action) => action switch
    {
        ManeuverAction.ChaffScreen => ChaffArmed,
        ManeuverAction.AlphaStrike => AlphaStrikeArmed,
        _ => false,
    };

    /// <summary>Whether this ship can catch an enemy in its tractor beam right now.</summary>
    public bool CanTractor(Fighter target) =>
        HasAbility(ShipAbility.TractorBeam) && TractorCooldownTurns == 0 && target != null && target.IsAlive &&
        target.Team != Team && Position.DistanceTo(target.Position) <= Moves.TractorRange;

    /// <summary>Prepare the Raptor line's fixed sharp turn and short forward burst.</summary>
    public void PlanEvasiveDodge(float direction)
    {
        PlannedManeuver = ManeuverType.EvasiveDodge;
        PlannedPathDistance = Moves.EvasiveDodgeMoveDistance;
        PlannedTurnAngleRadians = Mathf.Sign(direction) * Mathf.DegToRad(Moves.EvasiveDodgeAngleDegrees);
    }

    public bool SetHunterLock(Fighter target)
    {
        if (!HasAbility(ShipAbility.HunterLock) || HunterLockCooldownTurns > 0 || target == null || !target.IsAlive || target.Team == Team)
            return false;
        HunterLockTarget = target;
        HunterLockCooldownTurns = Moves.HunterLockCooldownTurns;
        return true;
    }

    public void AdvanceHunterLockCooldown()
    {
        HunterLockCooldownTurns = Mathf.Max(0, HunterLockCooldownTurns - 1);
        if (HunterLockTarget != null && !HunterLockTarget.IsAlive)
            HunterLockTarget = null;
    }

    /// <summary>
    /// Jams an enemy's sensors. With the maneuver mastered, the nearest other
    /// enemy within range of the target is jammed too; returns that ship, if any.
    /// </summary>
    public bool ApplySensorScramble(Fighter target, out Fighter splash)
    {
        splash = null;
        if (!HasAbility(ShipAbility.SensorScramble) || SensorScrambleCooldownTurns > 0 || target == null || !target.IsAlive || target.Team == Team)
            return false;
        Scramble(target);
        if (HasMastered(ShipAbility.SensorScramble))
        {
            splash = BattleManager.Instance?.GetTeam(target.Team)
                .Where(other => other != target && other.IsAlive && other.Position.DistanceTo(target.Position) <= Masteries.ScrambleSplashRange)
                .OrderBy(other => other.Position.DistanceSquaredTo(target.Position))
                .FirstOrDefault();
            if (splash != null)
                Scramble(splash);
        }
        SensorScrambleCooldownTurns = Moves.SensorScrambleCooldownTurns;
        return true;
    }

    void Scramble(Fighter target)
    {
        target.SensorScrambleTurns = Moves.SensorScrambleDurationTurns;
        target.SensorScrambleAccuracyPenalty = Mathf.Max(target.SensorScrambleAccuracyPenalty, Moves.SensorScrambleAccuracyPenalty);
    }

    public void AdvanceTacticalEffects()
    {
        RecoverFromSuppression();
        AdvanceHunterLockCooldown();
        SensorScrambleCooldownTurns = Mathf.Max(0, SensorScrambleCooldownTurns - 1);
        if (SensorScrambleTurns > 0 && --SensorScrambleTurns == 0)
            SensorScrambleAccuracyPenalty = 0f;
        ChaffCooldownTurns = Mathf.Max(0, ChaffCooldownTurns - 1);
        AlphaStrikeCooldownTurns = Mathf.Max(0, AlphaStrikeCooldownTurns - 1);
        TractorCooldownTurns = Mathf.Max(0, TractorCooldownTurns - 1);
        ManeuverLockTurns = Mathf.Max(0, ManeuverLockTurns - 1);
        // The turn after an alpha strike, the guns are offline.
        _gunsOfflineTurns = Mathf.Max(0, _gunsOfflineTurns - 1);
        if (_alphaStrikeThisTurn)
        {
            _alphaStrikeThisTurn = false;
            _gunsOfflineTurns = 1;
        }
    }

    /// <summary>Prepare a short straight advance while the turret tracks a broad firing arc.</summary>
    public void PlanRotatingGuns()
    {
        PlannedManeuver = ManeuverType.RotatingGuns;
        PlannedPathDistance = Moves.RotatingGunsMoveDistance;
        PlannedTurnAngleRadians = 0f;
    }

    /// <summary>Prepare the ZT-6's high-speed emergency burst.</summary>
    public void PlanEmergencyThrusters(float turn)
    {
        PlannedManeuver = ManeuverType.EmergencyThrusters;
        PlannedPathDistance = Moves.EmergencyThrustersMoveDistance;
        PlannedTurnAngleRadians = Mathf.Clamp(turn,
            -Mathf.DegToRad(EmergencyThrustersTurnLimitDegrees),
            Mathf.DegToRad(EmergencyThrustersTurnLimitDegrees));
    }

    /// <summary>
    /// A suppressing hit: takes degrees off normal-flight turning, capped so
    /// the ship can always turn a little. Maneuvers keep their own angles.
    /// </summary>
    public void ApplySuppression(float penalty)
    {
        float before = NormalTurnLimitPenaltyDegrees;
        NormalTurnLimitPenaltyDegrees = Mathf.Min(NormalTurnLimitDegrees - 25f,
            NormalTurnLimitPenaltyDegrees + penalty);
        _suppressionThisTurn += NormalTurnLimitPenaltyDegrees - before;
    }

    /// <summary>
    /// End of turn. This turn's suppressing hits count in full for the next
    /// turn; older suppression halves. A turn without being suppressed clears
    /// it all, so it never lingers once the ship is out of the fire.
    /// </summary>
    void RecoverFromSuppression()
    {
        float older = NormalTurnLimitPenaltyDegrees - _suppressionThisTurn;
        NormalTurnLimitPenaltyDegrees = _suppressionThisTurn > 0f ? _suppressionThisTurn + older * SuppressionLinger : 0f;
        _suppressionThisTurn = 0f;
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
        Moves = type.Maneuvers;
        BaseFireConeDeg = FireConeDeg;
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

    /// <summary>
    /// Attach the campaign pilot flying this hull. Call after ApplyType, before
    /// Setup. The ship's modules change its numbers here; the pilot's
    /// masteries change its maneuvers. Instincts and scars are not applied
    /// here: they are checked in the moment they matter.
    /// </summary>
    public void ApplyPilot(Pilot pilot)
    {
        Pilot = pilot;
        if (Pilot == null)
            return;
        ShipStats stats = Pilot.Stats;
        MaxHp = stats.MaxHull;
        MaxShield = stats.MaxShield;
        ShieldRegenPerTurn = stats.ShieldRegen;
        ShotDamage = stats.ShotDamage;
        BarrageMin = stats.ShotsMin;
        BarrageMax = stats.ShotsMax;
        Accuracy = stats.Accuracy;
        Evasion = stats.Evasion;
        BaseFireConeDeg = stats.FireConeDeg;
        NormalTurnLimitDegrees = stats.TurnDeg;
        NormalMoveMinDistance = stats.MinMove;
        NormalMoveMaxDistance = stats.MaxMove;
        Moves = Masteries.Apply(Type.Maneuvers, Pilot.Masteries);
        SelectedNormalMoveDistance = NormalMoveMaxDistance;
        PlannedPathDistance = NormalMoveMaxDistance;
    }

    /// <summary>
    /// Makes this enemy an ace: its hull refitted like a pilot's, sharper aim
    /// and evasion, and its line's first two maneuvers, both mastered. Call
    /// after ApplyType, before Setup.
    /// </summary>
    public void MakeAce(string callsign, int refits)
    {
        AceName = callsign;
        ShipStats stats = ShipStats.Frame(Type).Refitted(refits);
        MaxHp = stats.MaxHull;
        MaxShield = stats.MaxShield;
        ShieldRegenPerTurn = stats.ShieldRegen;
        ShotDamage = stats.ShotDamage;
        Accuracy = stats.Accuracy + Aces.AccuracyBonus;
        Evasion = stats.Evasion + Aces.EvasionBonus;
        ManeuverAccess = Pilot.MaxManeuvers;
        Moves = Masteries.Apply(Type.Maneuvers, Type.ManeuverPool.Take(Pilot.MaxManeuvers).ToList());
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
        _hullRadius = skin.HullRadius;
        _enginePhase = GD.RandRange(0.0, 10.0); // desync flame flicker across ships

        // Your ships sit on a faint cyan pool of light so they lift off the dark.
        if (team == 0)
        {
            float glowSize = VisualRadius * 3.2f;
            _glowSprite = new Sprite2D
            {
                Texture = ShipPaint.SoftDot,
                Scale = Vector2.One * glowSize / ShipPaint.SoftDot.GetWidth(),
                Modulate = new Color(ShipPaint.PlayerGlow, 0.22f),
                Material = ShipPaint.Additive,
            };
            AddChild(_glowSprite);
        }
        _visual = new Node2D { RotationDegrees = 90, Scale = Vector2.One * ArtScale };
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
        // Enemy hulls are repainted in one hostile colour so they read at a glance.
        if (team == 1)
            _baseSprite.Material = _weaponSprite.Material = IsAce ? ShipPaint.Ace : ShipPaint.Enemy;
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
        ManeuverType flown = executing ? _execManeuver : PlannedManeuver;
        float moveFraction = Mathf.InverseLerp(MinMoveFor(flown), MaxMoveFor(flown),
            executing ? _execDist : PlannedPathDistance);
        _enginePhase += delta * (executing ? 12.0 + 4.0 * moveFraction : 7.0);
        _engineSprite.Frame = (int)_enginePhase % _engineFrames;
        _engineSprite.Scale = new Vector2(1f, 0.85f + 0.15f * moveFraction);

        // Bank into turns: roll (wingspan squash) eases in and back out over the arc.
        float bank = 0f;
        if (executing)
            bank = Mathf.Min(1f, Mathf.Abs(_execTurn) / Mathf.DegToRad(NormalTurnLimitDegrees)) * Mathf.Sin(Mathf.Pi * _execProgress);
        _visual.Scale = new Vector2(1f - 0.25f * bank, 1f) * ArtScale;
        // An evasive spin barrel-rolls the ship over and over through the turn.
        if (executing && _execManeuver == ManeuverType.EvasiveSpin)
            _visual.Scale = new Vector2(Mathf.Cos(_execProgress * Mathf.Tau * EvasiveSpinRolls), 1f) * ArtScale;
        // Rear guns fire from the tail.
        _weaponSprite.FlipV = IsRearGunsActive;

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
        if (FliesLikeNormal(_execManeuver))
        {
            float maxTurn = Mathf.DegToRad(GetNormalTurnLimitDegrees(_execDist));
            _execTurn = Mathf.Clamp(_execTurn, -maxTurn, maxTurn);
        }
        if (FliesLikeNormal(_execManeuver) && Mathf.IsEqualApprox(PlannedPathDistance, NormalMoveMaxDistance) &&
            Mathf.Abs(_execTurn) > Mathf.DegToRad(GetNormalTurnLimitDegrees(_execDist)) * 0.9f)
            NoteTrait(Perks.EngineShy); // a hard turn at full throttle is where the scar bites
        _volleysThisTurn = 0;
        _aceResetThisTurn = false;
        if (ChaffArmed)
        {
            ChaffArmed = false;
            ChaffCooldownTurns = Moves.ChaffCooldownTurns;
        }
        _alphaStrikeThisTurn = AlphaStrikeArmed;
        if (AlphaStrikeArmed)
        {
            AlphaStrikeArmed = false;
            AlphaStrikeCooldownTurns = AlphaStrikeCooldownRounds;
        }
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

    /// <summary>Advance one slice of the execution clock along the maneuver.</summary>
    public void AdvanceExecute(float dt)
    {
        SetExecuteProgress(AdvanceManeuverProgress(_execProgress, dt));
    }

    /// <summary>Advances a maneuver's route fraction: every maneuver takes the whole execution clock.</summary>
    public static float AdvanceManeuverProgress(float progress, float dt) =>
        Mathf.Min(progress + dt / BattleManager.ExecTime, 1f);

    public bool IsExecutingMove => _execProgress < 1f;

    /// <summary>Place the fighter at fraction s (0..1) along its planned maneuver.</summary>
    public void SetExecuteProgress(float s)
    {
        _execProgress = s;
        float routeDistance = _execDist * RouteScale;
        ManeuverPoint(_execManeuver, _startPos, _startHeading, _execTurn, routeDistance, s,
            out Vector2 pos, out float heading);
        Position = pos;
        Heading = heading;
        Rotation = heading;
        float travelHeading = _execManeuver switch
        {
            ManeuverType.UTurn => _startHeading,
            ManeuverType.Sideslip => _startHeading + _execTurn,
            _ => heading,
        };
        Velocity = Vector2.FromAngle(travelHeading) * (routeDistance / BattleManager.ExecTime);
    }

    /// <summary>
    /// Point at fraction s along one of this ship's maneuvers flown from where
    /// it is now, nebula drag included. Every preview and plan goes through
    /// here, so what the player sees is where the ship will fly.
    /// </summary>
    public void RoutePoint(ManeuverType maneuver, float turn, float distance, float s, out Vector2 pos, out float heading) =>
        ManeuverPoint(maneuver, Position, Heading, turn, distance * RouteScale, s, out pos, out heading);

    /// <summary>
    /// Point at fraction s along a maneuver. U-turns thrust straight while rotating;
    /// sideslips slide straight without turning; evasive dodges curve sharply, then
    /// make a short straight advance; all other turns follow constant-rate arcs.
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
        if (maneuver == ManeuverType.Sideslip)
        {
            // "turn" is the slide's angle off the nose; the nose never turns.
            pos = p0 + Vector2.FromAngle(h0 + turn) * dist * s;
            heading = h0;
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

    /// <summary>
    /// Damage from an enemy shot. Second Chance turns the first killing shot
    /// of a battle into a narrow escape: shields stripped, 1 hull left, and no
    /// shot can finish the ship for the rest of that turn, so the pilot gets
    /// to plan a way out. Asteroids stay fatal: they don't come through here.
    /// </summary>
    public void TakeHit(int dmg)
    {
        int turn = BattleManager.Instance?.TurnNumber ?? 0;
        if (IsAlive && HasPerk(Perks.SecondChance) && dmg >= Hp + Shield &&
            (_secondChanceTurn < 0 || _secondChanceTurn == turn))
        {
            _secondChanceTurn = turn;
            dmg = Hp + Shield - 1;
            NoteTrait(Perks.SecondChance);
        }
        TakeDamage(dmg);
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
            Shield -= shieldDamage;
            dmg -= shieldDamage;
            PlayShieldAnimation();
        }
        int hullDamage = Mathf.Min(Hp, dmg);
        BattleManager.Instance?.ShowDamage(this, shieldDamage, Mathf.Max(0, hullDamage));
        if (dmg <= 0)
            return;

        Hp -= dmg;
        if (isOpeningDamageTurn)
            TurnOneDamage += hullDamage;
        _hullDamageTurn = BattleManager.Instance?.TurnNumber ?? -1;
        if (Hp < MaxHp * Perks.LowHullThreshold)
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

    /// <summary>
    /// Restore the ship's independent shield pool, up to its hull's configured
    /// limit. Call after the turn's flight: Steady pilots recover faster after
    /// a turn of normal flight.
    /// </summary>
    public void RegenerateShield()
    {
        if (!IsAlive)
            return;
        int regen = ShieldRegenPerTurn;
        bool steady = HasPerk(Perks.Steady) && _execManeuver == ManeuverType.Normal && Shield < MaxShield;
        if (steady)
            regen += Perks.SteadyShieldRegenBonus;
        if (regen <= 0)
            return;
        int before = Shield;
        Shield = Mathf.Min(Shield + regen, MaxShield);
        if (steady && Shield - before > ShieldRegenPerTurn)
            NoteTrait(Perks.Steady);
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
        if (_glowSprite != null)
            _glowSprite.Visible = false;
        _visual.Scale = Vector2.One * ArtScale;
        _destructSprite.Visible = true;
        _destructSprite.Frame = 0;
        BattleManager.Instance?.ShowDestruction(this);

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
        {
            float s = BattleManager.Instance?.ScreenToWorldScale ?? 1f;
            int size = Mathf.Max(1, Mathf.RoundToInt(SignalUi.FontMicro * s));
            Vector2 extent = SignalUi.Display.GetStringSize("EJECTED", HorizontalAlignment.Left, -1f, size);
            DrawString(SignalUi.Display, new Vector2(-extent.X / 2f, -10f - 8f * s), "EJECTED",
                HorizontalAlignment.Left, -1f, size, new Color(1f, 1f, 1f, labelAlpha));
        }
    }
}
