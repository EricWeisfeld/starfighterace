using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>A command offered in the battle HUD's maneuver bar.</summary>
public enum ManeuverAction
{
    Normal,
    UTurn,
    BreakTurn,
    SnapTurn,
    EvasiveDodge,
    EngineBoost,
    PursuitBurn,
    EmergencyThrusters,
    EcmJink,
    GhostRun,
    RotatingGuns,
    HunterLock,
    SensorScramble,
}

/// <summary>Player-facing description of one maneuver-bar command.</summary>
public sealed class ManeuverInfo
{
    public ManeuverAction Action { get; init; }
    /// <summary>The hull ability that grants this command; null for normal flight.</summary>
    public ShipAbility? Ability { get; init; }
    /// <summary>The flight path this command plans; null for commands aimed at an enemy.</summary>
    public ManeuverType? Maneuver { get; init; }
    public string Name { get; init; }
    public Color Color { get; init; }
    /// <summary>A fixed turn whose side (left or right) the player chooses.</summary>
    public bool Directional { get; init; }
    /// <summary>The player aims this maneuver by dragging its ghost.</summary>
    public bool Aimable { get; init; }
    /// <summary>Applied by tapping an enemy ship rather than by flying a path.</summary>
    public bool TargetsEnemy { get; init; }
    /// <summary>One line explaining what the command does for this hull.</summary>
    public Func<Fighter, string> Summary { get; init; }
}

/// <summary>
/// Single source of names, colors and descriptions for every maneuver, so the
/// HUD, the path overlay and the debrief all use the same vocabulary.
/// </summary>
public static class ManeuverCatalog
{
    public static readonly ManeuverInfo[] All =
    {
        new()
        {
            Action = ManeuverAction.Normal, Maneuver = ManeuverType.Normal, Name = "FLY",
            Color = new Color(0.302f, 0.639f, 1f), Aimable = true,
            Summary = f => $"Drag the ghost to steer. Throttle {f.NormalMoveMinDistance:0}–{f.NormalMoveMaxDistance:0}, turn up to {f.PlannedNormalTurnLimitDegrees:0}°.",
        },
        new()
        {
            Action = ManeuverAction.UTurn, Ability = ShipAbility.UTurn, Maneuver = ManeuverType.UTurn, Name = "U-TURN",
            Color = new Color(0.62f, 0.48f, 1f), Directional = true,
            Summary = f => "Flip to face the way you came in a short, sharp reversal.",
        },
        new()
        {
            Action = ManeuverAction.BreakTurn, Ability = ShipAbility.BreakTurn, Maneuver = ManeuverType.BreakTurn, Name = "BREAK TURN",
            Color = new Color(1f, 0.62f, 0.3f), Directional = true,
            Summary = f => $"Swing through a wide 180° arc ({f.Type.BreakTurnMoveDistance:0}).",
        },
        new()
        {
            Action = ManeuverAction.SnapTurn, Ability = ShipAbility.SnapTurn, Maneuver = ManeuverType.SnapTurn, Name = "SNAP TURN",
            Color = new Color(0.95f, 0.55f, 1f), Directional = true,
            Summary = f => $"Whip around {f.Type.SnapTurnAngleDegrees:0}° in a very short distance.",
        },
        new()
        {
            Action = ManeuverAction.EvasiveDodge, Ability = ShipAbility.EvasiveDodge, Maneuver = ManeuverType.EvasiveDodge, Name = "DODGE",
            Color = new Color(0.35f, 0.9f, 1f), Directional = true,
            Summary = f => $"Hard {f.Type.EvasiveDodgeAngleDegrees:0}° jink, then a short burst. +{f.Type.EvasiveDodgeEvasionBonus * 100:0}% evasion.",
        },
        new()
        {
            Action = ManeuverAction.EngineBoost, Ability = ShipAbility.EngineBoost, Maneuver = ManeuverType.EngineBoost, Name = "BOOST",
            Color = new Color(0.3f, 1f, 0.75f), Aimable = true,
            Summary = f => $"Long burn ({f.Type.EngineBoostMoveDistance:0}) with up to {f.EngineBoostTurnLimitDegrees:0}° of turn.",
        },
        new()
        {
            Action = ManeuverAction.PursuitBurn, Ability = ShipAbility.PursuitBurn, Maneuver = ManeuverType.PursuitBurn, Name = "PURSUIT",
            Color = new Color(0.4f, 1f, 0.7f), Aimable = true,
            Summary = f => $"Chase burn ({f.Type.PursuitBurnMoveDistance:0}) with up to {f.PursuitBurnTurnLimitDegrees:0}° of turn.",
        },
        new()
        {
            Action = ManeuverAction.EmergencyThrusters, Ability = ShipAbility.EmergencyThrusters, Maneuver = ManeuverType.EmergencyThrusters, Name = "THRUSTERS",
            Color = new Color(1f, 0.36f, 0.3f), Aimable = true,
            Summary = f => $"Emergency sprint ({f.Type.EmergencyThrustersMoveDistance:0}). -{f.Type.EmergencyThrustersEvasionPenalty * 100:0}% evasion.",
        },
        new()
        {
            Action = ManeuverAction.EcmJink, Ability = ShipAbility.EcmJink, Maneuver = ManeuverType.EcmJink, Name = "ECM JINK",
            Color = new Color(0.45f, 0.7f, 1f), Aimable = true,
            Summary = f => $"Jamming jink ({f.Type.EcmJinkMoveDistance:0}). +{f.Type.EcmJinkEvasionBonus * 100:0}% evasion.",
        },
        new()
        {
            Action = ManeuverAction.GhostRun, Ability = ShipAbility.GhostRun, Maneuver = ManeuverType.GhostRun, Name = "GHOST RUN",
            Color = new Color(0.6f, 0.95f, 1f), Aimable = true,
            Summary = f => $"Stealthy run ({f.Type.GhostRunMoveDistance:0}). +{f.Type.GhostRunEvasionBonus * 100:0}% evasion.",
        },
        new()
        {
            Action = ManeuverAction.RotatingGuns, Ability = ShipAbility.RotatingGuns, Maneuver = ManeuverType.RotatingGuns, Name = "TURRET",
            Color = new Color(1f, 0.78f, 0.32f),
            Summary = f => $"Creep forward while the guns sweep a {f.Type.RotatingGunsFireConeDeg * 2f:0}° arc.",
        },
        new()
        {
            Action = ManeuverAction.HunterLock, Ability = ShipAbility.HunterLock, Name = "LOCK ON",
            Color = new Color(1f, 0.85f, 0.3f), TargetsEnemy = true,
            Summary = f => $"Tap an enemy: +{f.Type.HunterLockAccuracyBonus * 100:0}% accuracy against it.",
        },
        new()
        {
            Action = ManeuverAction.SensorScramble, Ability = ShipAbility.SensorScramble, Name = "SCRAMBLE",
            Color = new Color(0.75f, 0.55f, 1f), TargetsEnemy = true,
            Summary = f => $"Tap an enemy: -{f.Type.SensorScrambleAccuracyPenalty * 100:0}% accuracy for {f.Type.SensorScrambleDurationTurns} turns.",
        },
    };

    static readonly Dictionary<ManeuverAction, ManeuverInfo> ByAction = All.ToDictionary(info => info.Action);

    public static ManeuverInfo Get(ManeuverAction action) => ByAction[action];

    /// <summary>The command that plans a given flight path.</summary>
    public static ManeuverInfo ForManeuver(ManeuverType maneuver) =>
        All.FirstOrDefault(info => info.Maneuver == maneuver) ?? Get(ManeuverAction.Normal);

    /// <summary>Every command this fighter can use, in maneuver-bar order, including ones cooling down.</summary>
    public static List<ManeuverInfo> ActionsFor(Fighter fighter) =>
        All.Where(info => info.Ability == null || fighter.HasAbility(info.Ability.Value)).ToList();

    /// <summary>Turns until the command can be used again; zero when it is ready.</summary>
    public static int CooldownTurns(Fighter fighter, ManeuverInfo info) => info.Action switch
    {
        ManeuverAction.HunterLock => fighter.HunterLockCooldownTurns,
        ManeuverAction.SensorScramble => fighter.SensorScrambleCooldownTurns,
        _ => info.Maneuver is ManeuverType maneuver ? fighter.GetManeuverCooldownTurns(maneuver) : 0,
    };

    /// <summary>Title-case ability name for lists and cards.</summary>
    public static string AbilityName(ShipAbility ability) => ability switch
    {
        ShipAbility.UTurn => "U-Turn",
        ShipAbility.BreakTurn => "Break Turn",
        ShipAbility.EngineBoost => "Engine Boost",
        ShipAbility.RotatingGuns => "Rotating Guns",
        ShipAbility.SuppressionFire => "Suppression Fire",
        ShipAbility.EmergencyThrusters => "Emergency Thrusters",
        ShipAbility.SnapTurn => "Snap Turn",
        ShipAbility.HunterLock => "Hunter Lock",
        ShipAbility.PursuitBurn => "Pursuit Burn",
        ShipAbility.EcmJink => "ECM Jink",
        ShipAbility.SensorScramble => "Sensor Scramble",
        ShipAbility.GhostRun => "Ghost Run",
        ShipAbility.EvasiveDodge => "Evasive Dodge",
        _ => ability.ToString(),
    };
}

/// <summary>
/// Draws a small pictogram of a maneuver's flight path. Paths are built from
/// the same constant-rate arcs the ships fly, so the icon matches the ghost.
/// Directional maneuvers mirror to show the side currently chosen.
/// </summary>
public partial class ManeuverGlyph : Control
{
    public ManeuverAction Action;
    /// <summary>+1 draws the right-hand version of a directional maneuver, -1 the left.</summary>
    public float Side = 1f;
    public Color Tint = Colors.White;

    public override void _Draw()
    {
        float width = Mathf.Max(2f, Size.Y * 0.07f);
        Rect2 box = new Rect2(Vector2.Zero, Size).Grow(-Size.Y * 0.14f);

        if (Action is ManeuverAction.HunterLock or ManeuverAction.SensorScramble)
        {
            DrawReticle(box, width, Action == ManeuverAction.SensorScramble);
            return;
        }

        List<Vector2> path = BuildPath(Action, Side);
        Vector2[] points = Fit(path, box);
        bool dashed = Action == ManeuverAction.GhostRun;
        if (dashed)
        {
            for (int i = 0; i < points.Length - 1; i += 2)
                DrawLine(points[i], points[i + 1], Tint, width, true);
        }
        else
        {
            DrawPolyline(points, Tint, width, true);
        }

        Vector2 tip = points[^1];
        Vector2 dir = (points[^1] - points[^3]).Normalized();
        float head = Size.Y * 0.16f;
        DrawColoredPolygon(new[]
        {
            tip + dir * head * 0.5f,
            tip - dir * head * 0.6f + dir.Orthogonal() * head * 0.55f,
            tip - dir * head * 0.6f - dir.Orthogonal() * head * 0.55f,
        }, Tint);

        switch (Action)
        {
            case ManeuverAction.EngineBoost:
            case ManeuverAction.PursuitBurn:
            case ManeuverAction.EmergencyThrusters:
                // Exhaust dashes behind the start of the burn.
                Vector2 start = points[0];
                Vector2 back = (points[0] - points[2]).Normalized();
                int dashes = Action == ManeuverAction.PursuitBurn ? 3 : 2;
                for (int i = 0; i < dashes; i++)
                {
                    Vector2 offset = back.Orthogonal() * (i - (dashes - 1) / 2f) * Size.Y * 0.1f;
                    DrawLine(start + offset, start + offset + back * Size.Y * 0.1f, new Color(Tint, 0.7f), width * 0.7f, true);
                }
                break;
            case ManeuverAction.RotatingGuns:
                // The widened firing arc projected from the nose.
                float reach = Size.Y * 0.28f;
                Vector2 up = Vector2.Up;
                DrawLine(tip, tip + up.Rotated(-0.8f) * reach, new Color(Tint, 0.75f), width * 0.7f, true);
                DrawLine(tip, tip + up.Rotated(0.8f) * reach, new Color(Tint, 0.75f), width * 0.7f, true);
                break;
        }
    }

    void DrawReticle(Rect2 box, float width, bool scrambled)
    {
        Vector2 center = box.GetCenter();
        float r = Mathf.Min(box.Size.X, box.Size.Y) * 0.4f;
        DrawArc(center, r, 0f, Mathf.Tau, 28, Tint, width, true);
        for (int i = 0; i < 4; i++)
        {
            Vector2 dir = Vector2.Up.Rotated(i * Mathf.Pi / 2f);
            DrawLine(center + dir * r * 0.55f, center + dir * r * 1.3f, Tint, width, true);
        }
        if (scrambled)
        {
            var zig = new[]
            {
                center + new Vector2(-r * 0.7f, r * 0.2f), center + new Vector2(-r * 0.25f, -r * 0.3f),
                center + new Vector2(r * 0.15f, r * 0.25f), center + new Vector2(r * 0.7f, -r * 0.25f),
            };
            DrawPolyline(zig, Tint, width, true);
        }
        else
        {
            DrawCircle(center, width * 1.2f, Tint);
        }
    }

    /// <summary>Path segments as (turn in degrees, length) pairs, starting upward.</summary>
    static List<Vector2> BuildPath(ManeuverAction action, float side)
    {
        (float TurnDeg, float Length)[] segments = action switch
        {
            ManeuverAction.Normal => new[] { (45f, 1.6f) },
            ManeuverAction.UTurn => new[] { (0f, 1.0f), (180f, 0.55f), (0f, 0.25f) },
            ManeuverAction.BreakTurn => new[] { (0f, 0.4f), (180f, 2.2f) },
            ManeuverAction.SnapTurn => new[] { (0f, 1.0f), (145f, 0.55f), (0f, 0.35f) },
            ManeuverAction.EvasiveDodge => new[] { (0f, 0.7f), (135f, 0.6f), (0f, 0.6f) },
            ManeuverAction.EngineBoost => new[] { (18f, 2.4f) },
            ManeuverAction.PursuitBurn => new[] { (0f, 2.5f) },
            ManeuverAction.EmergencyThrusters => new[] { (28f, 2.2f) },
            ManeuverAction.EcmJink => new[] { (-40f, 0.5f), (80f, 0.6f), (-80f, 0.6f), (40f, 0.3f) },
            ManeuverAction.GhostRun => new[] { (50f, 2.0f) },
            ManeuverAction.RotatingGuns => new[] { (0f, 1.1f) },
            _ => new[] { (0f, 1f) },
        };

        var points = new List<Vector2> { Vector2.Zero };
        Vector2 position = Vector2.Zero;
        float heading = -Mathf.Pi / 2f;
        foreach ((float turnDeg, float length) in segments)
        {
            float turn = Mathf.DegToRad(turnDeg) * side;
            const int steps = 12;
            for (int i = 1; i <= steps; i++)
            {
                Fighter.ArcPoint(position, heading, turn, length, i / (float)steps, out Vector2 point, out _);
                points.Add(point);
            }
            Fighter.ArcPoint(position, heading, turn, length, 1f, out position, out float endHeading);
            heading = endHeading;
        }
        return points;
    }

    /// <summary>Scales a path uniformly to fit a box, centred.</summary>
    static Vector2[] Fit(List<Vector2> path, Rect2 box)
    {
        float minX = path.Min(p => p.X), maxX = path.Max(p => p.X);
        float minY = path.Min(p => p.Y), maxY = path.Max(p => p.Y);
        var bounds = new Rect2(minX, minY, Mathf.Max(maxX - minX, 0.001f), Mathf.Max(maxY - minY, 0.001f));
        float scale = Mathf.Min(box.Size.X / bounds.Size.X, box.Size.Y / bounds.Size.Y);
        Vector2 offset = box.GetCenter() - bounds.GetCenter() * scale;
        return path.Select(p => p * scale + offset).ToArray();
    }
}
