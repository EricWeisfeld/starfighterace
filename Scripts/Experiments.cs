using Godot;
using System.Linq;

/// <summary>
/// Experimental rules for following an enemy, each behind its own switch on
/// the quick battle screen and saved on the device. A rule that is on applies
/// to every battle, runs included.
/// </summary>
public static class Experiments
{
    public enum Rule
    {
        /// <summary>A ship behind an enemy sees its move; an enemy behind you reads yours.</summary>
        OnTheirSix,
        /// <summary>A hard turn caps the next turn's throttle at the minimum.</summary>
        BleedSpeed,
        /// <summary>A turn can only differ so much from the turn before it.</summary>
        TurnMomentum,
        /// <summary>Holding an enemy in your guns builds an accuracy bonus against it.</summary>
        TrackingLock,
    }

    public static readonly Rule[] All = { Rule.OnTheirSix, Rule.BleedSpeed, Rule.TurnMomentum, Rule.TrackingLock };

    static bool[] _on;

    static bool[] On => _on ??= All.Select(rule => GameSettings.GetExperiment(Key(rule))).ToArray();

    public static bool IsOn(Rule rule) => On[(int)rule];

    /// <summary>Switches a rule; <paramref name="save"/> keeps the choice on the device.</summary>
    public static void Set(Rule rule, bool on, bool save = true)
    {
        On[(int)rule] = on;
        if (save)
            GameSettings.SetExperiment(Key(rule), on);
    }

    static string Key(Rule rule) => rule.ToString();

    public static bool OnTheirSix => IsOn(Rule.OnTheirSix);
    public static bool BleedSpeed => IsOn(Rule.BleedSpeed);
    public static bool TurnMomentum => IsOn(Rule.TurnMomentum);
    public static bool TrackingLock => IsOn(Rule.TrackingLock);

    public static string Name(Rule rule) => rule switch
    {
        Rule.OnTheirSix => "ON THEIR SIX",
        Rule.BleedSpeed => "HARD TURNS BLEED SPEED",
        Rule.TurnMomentum => "TURN MOMENTUM",
        _ => "TRACKING LOCK",
    };

    public static string Description(Rule rule) => rule switch
    {
        Rule.OnTheirSix => $"Start a turn behind an enemy, within {SixRange:0} and pointed at it, and it commits first: you see its move while you plan. An enemy behind you plans against your real orders.",
        Rule.BleedSpeed => $"Turn more than {HardTurnShare * 100:0}% of your limit and next turn you fly at minimum throttle. Maneuvers don't bleed.",
        Rule.TurnMomentum => $"Each turn's turn can differ from your last by at most {MomentumShare * 100:0}% of your turn limit, so reversing a hard turn takes two turns. Maneuvers reset it.",
        _ => $"Keep an enemy in your guns to build lock on it, up to +{LockAccuracyBonus * 100:0}% accuracy against it, kept between turns. It fades once the enemy slips out.",
    };

    // ---- On their six

    /// <summary>How close a chaser must be to count as on a target's six.</summary>
    public const float SixRange = 300f;
    /// <summary>The chaser must sit within this of the target's tail...</summary>
    public const float SixRearArcDegrees = 60f;
    /// <summary>...and point within this of the target.</summary>
    public const float SixFacingDegrees = 60f;

    /// <summary>Whether <paramref name="chaser"/> is on <paramref name="target"/>'s six: close behind it and pointed at it.</summary>
    public static bool OnSix(Fighter chaser, Fighter target)
    {
        if (chaser == null || target == null || !chaser.IsAlive || !target.IsAlive || chaser.Team == target.Team)
            return false;
        Vector2 toChaser = chaser.Position - target.Position;
        if (toChaser.Length() > SixRange)
            return false;
        float offTail = Mathf.Pi - Mathf.Abs(Mathf.Wrap(toChaser.Angle() - target.Heading, -Mathf.Pi, Mathf.Pi));
        float offNose = Mathf.Abs(Mathf.Wrap((-toChaser).Angle() - chaser.Heading, -Mathf.Pi, Mathf.Pi));
        return offTail <= Mathf.DegToRad(SixRearArcDegrees) && offNose <= Mathf.DegToRad(SixFacingDegrees);
    }

    // ---- Hard turns bleed speed

    /// <summary>A normal turn sharper than this share of the ship's limit is a hard turn.</summary>
    public const float HardTurnShare = 2f / 3f;

    // ---- Turn momentum

    /// <summary>How far a turn may differ from the last one, as a share of the ship's turn limit.</summary>
    public const float MomentumShare = 0.75f;

    // ---- Tracking lock

    /// <summary>Seconds a target must spend in your guns to reach full lock.</summary>
    public const float LockBuildSeconds = 1.2f;
    /// <summary>Seconds full lock takes to fade once the target is out of your guns.</summary>
    public const float LockFadeSeconds = 0.8f;
    /// <summary>Accuracy added against the locked target at full lock.</summary>
    public const float LockAccuracyBonus = 0.30f;
}
