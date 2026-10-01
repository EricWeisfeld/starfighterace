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
    AirBrake,
    Sideslip,
    EvasiveDodge,
    EngineBoost,
    PursuitBurn,
    EmergencyThrusters,
    EvasiveSpin,
    RotatingGuns,
    RearGuns,
    HunterLock,
    SensorScramble,
    TractorBeam,
    ChaffScreen,
    AlphaStrike,
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
    /// <summary>Armed for this turn by tapping the button, on top of whatever the ship flies.</summary>
    public bool Toggle { get; init; }
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
            Summary = f => $"Drag the ghost to steer. Throttle {f.NormalMoveMinDistance:0}–{f.NormalMoveMaxDistance:0}, turn up to {f.PlannedNormalTurnLimitDegrees:0}°." +
                (f.InNebula ? $" Nebula: every move is {(1f - f.RouteScale) * 100:0}% shorter this turn." : ""),
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
            Summary = f => $"Swing through a wide 180° arc ({f.Moves.BreakTurnMoveDistance:0}).",
        },
        new()
        {
            Action = ManeuverAction.AirBrake, Ability = ShipAbility.AirBrake, Maneuver = ManeuverType.AirBrake, Name = "AIR BRAKE",
            Color = new Color(1f, 0.9f, 0.55f), Aimable = true,
            Summary = f => $"Drag to steer. Crawl {f.MinMoveFor(ManeuverType.AirBrake):0}–{f.MaxMoveFor(ManeuverType.AirBrake):0}, " +
                $"turn up to {f.GetNormalTurnLimitDegrees(f.PlannedPathDistance):0}°. Pursuers overshoot.",
        },
        new()
        {
            Action = ManeuverAction.Sideslip, Ability = ShipAbility.Sideslip, Maneuver = ManeuverType.Sideslip, Name = "SIDESLIP",
            Color = new Color(0.55f, 1f, 0.55f), Aimable = true,
            Summary = f => $"Drag to aim. Slide {f.Moves.SideslipDistance:0} up to {f.Moves.SideslipMaxAngleDegrees:0}° off the nose; the nose stays put.",
        },
        new()
        {
            Action = ManeuverAction.EvasiveDodge, Ability = ShipAbility.EvasiveDodge, Maneuver = ManeuverType.EvasiveDodge, Name = "DODGE",
            Color = new Color(0.35f, 0.9f, 1f), Directional = true,
            Summary = f => $"Hard {f.Moves.EvasiveDodgeAngleDegrees:0}° jink, then a short burst. +{f.Moves.EvasiveDodgeEvasionBonus * 100:0}% evasion.",
        },
        new()
        {
            Action = ManeuverAction.EngineBoost, Ability = ShipAbility.EngineBoost, Maneuver = ManeuverType.EngineBoost, Name = "BOOST",
            Color = new Color(0.3f, 1f, 0.75f), Aimable = true,
            Summary = f => $"Long burn ({f.Moves.EngineBoostMoveDistance:0}) with up to {f.EngineBoostTurnLimitDegrees:0}° of turn.",
        },
        new()
        {
            Action = ManeuverAction.PursuitBurn, Ability = ShipAbility.PursuitBurn, Maneuver = ManeuverType.PursuitBurn, Name = "PURSUIT",
            Color = new Color(0.4f, 1f, 0.7f), Aimable = true,
            Summary = f => $"Chase burn ({f.Moves.PursuitBurnMoveDistance:0}) with up to {f.PursuitBurnTurnLimitDegrees:0}° of turn.",
        },
        new()
        {
            Action = ManeuverAction.EmergencyThrusters, Ability = ShipAbility.EmergencyThrusters, Maneuver = ManeuverType.EmergencyThrusters, Name = "THRUSTERS",
            Color = new Color(1f, 0.36f, 0.3f), Aimable = true,
            Summary = f => f.Moves.EmergencyThrustersEvasionPenalty > 0f
                ? $"Emergency sprint ({f.Moves.EmergencyThrustersMoveDistance:0}). -{f.Moves.EmergencyThrustersEvasionPenalty * 100:0}% evasion."
                : $"Emergency sprint ({f.Moves.EmergencyThrustersMoveDistance:0}) with no loss of evasion.",
        },
        new()
        {
            Action = ManeuverAction.EvasiveSpin, Ability = ShipAbility.EvasiveSpin, Maneuver = ManeuverType.EvasiveSpin, Name = "SPIN",
            Color = new Color(0.6f, 0.95f, 1f), Aimable = true,
            Summary = f => $"Drag to steer. Throttle {f.MinMoveFor(ManeuverType.EvasiveSpin):0}–{f.MaxMoveFor(ManeuverType.EvasiveSpin):0}. " +
                $"+{f.Moves.EvasiveSpinEvasionBonus * 100:0}% evasion, but your guns stay silent.",
        },
        new()
        {
            Action = ManeuverAction.RotatingGuns, Ability = ShipAbility.RotatingGuns, Maneuver = ManeuverType.RotatingGuns, Name = "TURRET",
            Color = new Color(1f, 0.78f, 0.32f),
            Summary = f => $"Creep forward while the guns sweep a {f.Moves.RotatingGunsFireConeDeg * 2f:0}° arc.",
        },
        new()
        {
            Action = ManeuverAction.RearGuns, Ability = ShipAbility.RearGuns, Maneuver = ManeuverType.RearGuns, Name = "REAR GUNS",
            Color = new Color(0.95f, 0.55f, 1f), Aimable = true,
            Summary = f => "Drag to steer as normal. Your guns face astern" +
                (f.Moves.RearGunsDamageMultiplier < 1f ? $" and hit for {f.Moves.RearGunsDamageMultiplier * 100:0}% damage." : " at full damage."),
        },
        new()
        {
            Action = ManeuverAction.HunterLock, Ability = ShipAbility.HunterLock, Name = "LOCK ON",
            Color = new Color(1f, 0.85f, 0.3f), TargetsEnemy = true,
            Summary = f => $"Tap an enemy: +{f.Moves.HunterLockAccuracyBonus * 100:0}% accuracy against it.",
        },
        new()
        {
            Action = ManeuverAction.SensorScramble, Ability = ShipAbility.SensorScramble, Name = "SCRAMBLE",
            Color = new Color(0.75f, 0.55f, 1f), TargetsEnemy = true,
            Summary = f => $"Tap an enemy: -{f.Moves.SensorScrambleAccuracyPenalty * 100:0}% accuracy for {f.Moves.SensorScrambleDurationTurns} turns" +
                (f.HasMastered(ShipAbility.SensorScramble) ? ", and the nearest enemy to it." : "."),
        },
        new()
        {
            Action = ManeuverAction.TractorBeam, Ability = ShipAbility.TractorBeam, Name = "TRACTOR",
            Color = new Color(0.4f, 0.95f, 0.9f), TargetsEnemy = true,
            Summary = f => $"Tap an enemy within {f.Moves.TractorRange:0}: drag it {f.Moves.TractorPullDistance:0} toward you. It can't fly a maneuver this turn.",
        },
        new()
        {
            Action = ManeuverAction.ChaffScreen, Ability = ShipAbility.ChaffScreen, Name = "CHAFF", Toggle = true,
            Color = new Color(0.8f, 0.85f, 0.95f),
            Summary = f => $"Drop chaff where you start this turn. Shots through it lose {(1f - BattleManager.NebulaAccuracyMultiplier) * 100:0}% accuracy for {f.Moves.ChaffDurationTurns} turns.",
        },
        new()
        {
            Action = ManeuverAction.AlphaStrike, Ability = ShipAbility.AlphaStrike, Name = "ALPHA STRIKE", Toggle = true,
            Color = new Color(1f, 0.4f, 0.55f),
            Summary = f => $"+{f.Moves.AlphaStrikeExtraShots} shots in every volley this turn. Your guns are offline next turn.",
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
        ManeuverAction.TractorBeam => fighter.TractorCooldownTurns,
        ManeuverAction.ChaffScreen => fighter.ChaffCooldownTurns,
        // Guns offline after an alpha strike count as its cooldown too.
        ManeuverAction.AlphaStrike => fighter.AlphaStrikeCooldownTurns,
        _ => info.Maneuver is ManeuverType maneuver ? fighter.GetManeuverCooldownTurns(maneuver) : 0,
    };

    /// <summary>One stat-free sentence describing an ability, for promotion cards.</summary>
    public static string Blurb(ShipAbility ability) => ability switch
    {
        ShipAbility.UTurn => "Flip to face the way you came in a short, sharp reversal.",
        ShipAbility.BreakTurn => "Swing through a wide 180° arc.",
        ShipAbility.EngineBoost => "A long, nearly straight burn that covers ground fast.",
        ShipAbility.RotatingGuns => "Creep forward while the guns sweep a wide arc.",
        ShipAbility.SuppressionFire => "Always on: your hits make the target turn less sharply next turn, until it gets out of your fire.",
        ShipAbility.EmergencyThrusters => "A fast escape sprint, at the cost of some evasion.",
        ShipAbility.HunterLock => "Lock an enemy for extra accuracy against it.",
        ShipAbility.PursuitBurn => "A long chase burn that can bend after its target.",
        ShipAbility.SensorScramble => "Scramble an enemy's sensors to spoil its aim.",
        ShipAbility.EvasiveDodge => "A hard jink and a short burst that is hard to hit.",
        ShipAbility.EvasiveSpin => "Barrel-roll through your move: very hard to hit, but your guns fall silent.",
        ShipAbility.AirBrake => "Crawl forward so anyone chasing you overshoots.",
        ShipAbility.ChaffScreen => "Leave a cloud of chaff that spoils any shot through it.",
        ShipAbility.Sideslip => "Slide sideways while your nose, and your guns, stay on target.",
        ShipAbility.AlphaStrike => "Empty the guns for extra shots, then let them cool for a turn.",
        ShipAbility.TractorBeam => "Drag an enemy toward you and pin it out of its maneuvers.",
        ShipAbility.RearGuns => "Fly as normal with your guns facing astern, at reduced damage.",
        _ => "",
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
        ShipAbility.HunterLock => "Hunter Lock",
        ShipAbility.PursuitBurn => "Pursuit Burn",
        ShipAbility.SensorScramble => "Sensor Scramble",
        ShipAbility.EvasiveDodge => "Evasive Dodge",
        ShipAbility.EvasiveSpin => "Evasive Spin",
        ShipAbility.RearGuns => "Rear Guns",
        ShipAbility.AirBrake => "Air Brake",
        ShipAbility.ChaffScreen => "Chaff Screen",
        ShipAbility.Sideslip => "Sideslip",
        ShipAbility.AlphaStrike => "Alpha Strike",
        ShipAbility.TractorBeam => "Tractor Beam",
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

        if (Action is ManeuverAction.HunterLock or ManeuverAction.SensorScramble or ManeuverAction.TractorBeam)
        {
            DrawReticle(box, width, Action == ManeuverAction.SensorScramble);
            if (Action == ManeuverAction.TractorBeam)
                DrawTractorLines(box, width);
            return;
        }
        if (Action == ManeuverAction.ChaffScreen)
        {
            DrawChaff(box, width);
            return;
        }
        if (Action == ManeuverAction.AlphaStrike)
        {
            DrawVolley(box, width);
            return;
        }
        if (Action == ManeuverAction.Sideslip)
        {
            DrawSideslip(box, width);
            return;
        }

        List<Vector2> path = BuildPath(Action, Side);
        Vector2[] points = Fit(path, box);
        bool dashed = Action == ManeuverAction.EvasiveSpin;
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
            case ManeuverAction.AirBrake:
                // Brake bars across the short crawl.
                foreach (float along in new[] { 0.4f, 0.65f })
                {
                    int i = Mathf.Clamp((int)(points.Length * along), 1, points.Length - 2);
                    Vector2 across = (points[i + 1] - points[i - 1]).Normalized().Orthogonal() * Size.Y * 0.13f;
                    DrawLine(points[i] - across, points[i] + across, Tint, width, true);
                }
                break;
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
            case ManeuverAction.RearGuns:
                // A narrow firing cone thrown back from the tail.
                Vector2 tail = points[0];
                Vector2 astern = (points[0] - points[2]).Normalized();
                float coneReach = Size.Y * 0.26f;
                DrawLine(tail, tail + astern.Rotated(-0.35f) * coneReach, new Color(Tint, 0.75f), width * 0.7f, true);
                DrawLine(tail, tail + astern.Rotated(0.35f) * coneReach, new Color(Tint, 0.75f), width * 0.7f, true);
                break;
        }
    }

    /// <summary>Two beams converging on the reticle from below: something being hauled in.</summary>
    void DrawTractorLines(Rect2 box, float width)
    {
        Vector2 center = box.GetCenter();
        float r = Mathf.Min(box.Size.X, box.Size.Y) * 0.4f;
        foreach (float side in new[] { -1f, 1f })
            DrawLine(new Vector2(center.X + side * r * 0.45f, box.End.Y + r * 0.2f), center + new Vector2(side * r * 0.22f, r * 0.3f),
                new Color(Tint, 0.75f), width * 0.7f, true);
    }

    /// <summary>A scatter of chaff glints in a loose ring.</summary>
    void DrawChaff(Rect2 box, float width)
    {
        Vector2 center = box.GetCenter();
        float r = Mathf.Min(box.Size.X, box.Size.Y) * 0.45f;
        for (int i = 0; i < 14; i++)
        {
            float angle = i * 2.39996f; // golden angle: an even, irregular-looking scatter
            float distance = r * Mathf.Sqrt((i + 0.5f) / 14f);
            Vector2 p = center + Vector2.FromAngle(angle) * distance;
            DrawRect(new Rect2(p - Vector2.One * width * 0.6f, Vector2.One * width * 1.2f), new Color(Tint, i % 3 == 0 ? 1f : 0.6f));
        }
    }

    /// <summary>Three shots side by side, the outer two longer: a heavier volley.</summary>
    void DrawVolley(Rect2 box, float width)
    {
        Vector2 center = box.GetCenter();
        float h = box.Size.Y * 0.45f;
        for (int i = -1; i <= 1; i++)
        {
            float x = center.X + i * box.Size.Y * 0.3f;
            float top = center.Y - h + Mathf.Abs(i) * h * 0.25f;
            DrawLine(new Vector2(x, center.Y + h), new Vector2(x, top), Tint, width, true);
            DrawColoredPolygon(new[]
            {
                new Vector2(x, top - width * 2f), new Vector2(x - width * 1.6f, top + width), new Vector2(x + width * 1.6f, top + width),
            }, Tint);
        }
    }

    /// <summary>A straight slide off to one side, with the nose pointing up at both ends.</summary>
    void DrawSideslip(Rect2 box, float width)
    {
        float r = box.Size.Y * 0.5f;
        Vector2 from = box.GetCenter() + new Vector2(-r * 0.75f * Side, r * 0.3f);
        Vector2 to = box.GetCenter() + new Vector2(r * 0.75f * Side, -r * 0.3f);
        DrawLine(from, to, new Color(Tint, 0.8f), width, true);
        // A ship-shaped nose at each end, both pointing straight up.
        foreach (Vector2 nose in new[] { from, to })
            DrawColoredPolygon(new[]
            {
                nose + new Vector2(0f, -r * 0.55f), nose + new Vector2(-r * 0.3f, r * 0.3f), nose + new Vector2(r * 0.3f, r * 0.3f),
            }, new Color(Tint, nose == to ? 1f : 0.45f));
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
            ManeuverAction.EvasiveDodge => new[] { (0f, 0.7f), (135f, 0.6f), (0f, 0.6f) },
            ManeuverAction.EngineBoost => new[] { (0f, 2.4f) },
            ManeuverAction.PursuitBurn => new[] { (18f, 2.5f) },
            ManeuverAction.EmergencyThrusters => new[] { (28f, 2.2f) },
            ManeuverAction.EvasiveSpin => new[] { (60f, 1.1f) },
            ManeuverAction.RotatingGuns => new[] { (0f, 1.1f) },
            ManeuverAction.RearGuns => new[] { (30f, 1.6f) },
            ManeuverAction.AirBrake => new[] { (50f, 0.7f) },
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

/// <summary>
/// Maneuver masteries: a level-up can deepen a maneuver the pilot already
/// knows instead of teaching a new one. A mastery only matters on the turns
/// the pilot chooses that maneuver.
/// </summary>
public static class Masteries
{
    public const float EngineBoostTurnLimitDegrees = 45f;
    public const float PursuitBurnTurnLimitDegrees = 90f;
    public const float EvasionBonus = 0.15f;
    public const float RotatingGunsFireConeDegrees = 65f;
    public const float HunterLockAccuracyBonus = 0.25f;
    public const float SuppressionMultiplier = 2f;
    public const float ScrambleSplashRange = 300f;
    public const float SideslipDistance = 180f;
    public const int AlphaStrikeExtraShots = 3;
    public const float TractorPullDistance = 140f;

    /// <summary>A hull's maneuver profile with a pilot's masteries applied.</summary>
    public static ShipManeuverProfile Apply(ShipManeuverProfile profile, ICollection<ShipAbility> mastered)
    {
        if (mastered.Count == 0)
            return profile;
        ShipManeuverProfile p = profile;
        if (mastered.Contains(ShipAbility.EngineBoost))
            p = p with { EngineBoostTurnLimitDegrees = Mathf.Max(p.EngineBoostTurnLimitDegrees, EngineBoostTurnLimitDegrees) };
        if (mastered.Contains(ShipAbility.PursuitBurn))
            p = p with { PursuitBurnTurnLimitDegrees = Mathf.Max(p.PursuitBurnTurnLimitDegrees, PursuitBurnTurnLimitDegrees) };
        if (mastered.Contains(ShipAbility.EvasiveDodge))
            p = p with { EvasiveDodgeEvasionBonus = p.EvasiveDodgeEvasionBonus + EvasionBonus };
        if (mastered.Contains(ShipAbility.EmergencyThrusters))
            p = p with { EmergencyThrustersEvasionPenalty = 0f };
        if (mastered.Contains(ShipAbility.RotatingGuns))
            p = p with { RotatingGunsFireConeDeg = Mathf.Max(p.RotatingGunsFireConeDeg, RotatingGunsFireConeDegrees) };
        if (mastered.Contains(ShipAbility.HunterLock))
            p = p with { HunterLockAccuracyBonus = Mathf.Max(p.HunterLockAccuracyBonus, HunterLockAccuracyBonus) };
        if (mastered.Contains(ShipAbility.SuppressionFire))
            p = p with { SuppressionTurnPenaltyDeg = p.SuppressionTurnPenaltyDeg * SuppressionMultiplier };
        if (mastered.Contains(ShipAbility.EvasiveSpin))
            p = p with { EvasiveSpinDistanceScale = 1f };
        if (mastered.Contains(ShipAbility.RearGuns))
            p = p with { RearGunsDamageMultiplier = 1f };
        if (mastered.Contains(ShipAbility.ChaffScreen))
            p = p with { ChaffDurationTurns = p.ChaffDurationTurns + 1 };
        if (mastered.Contains(ShipAbility.Sideslip))
            p = p with { SideslipDistance = Mathf.Max(p.SideslipDistance, SideslipDistance) };
        if (mastered.Contains(ShipAbility.AlphaStrike))
            p = p with { AlphaStrikeExtraShots = Mathf.Max(p.AlphaStrikeExtraShots, AlphaStrikeExtraShots) };
        if (mastered.Contains(ShipAbility.TractorBeam))
            p = p with { TractorPullDistance = Mathf.Max(p.TractorPullDistance, TractorPullDistance) };
        return p;
    }

    /// <summary>A mastered maneuver's cooldown, when mastery changes it.</summary>
    public static int? CooldownOverride(ShipAbility ability) => ability switch
    {
        ShipAbility.UTurn or ShipAbility.BreakTurn or ShipAbility.AirBrake => 0,
        _ => null,
    };

    /// <summary>One sentence saying what mastering a maneuver does on this hull.</summary>
    public static string Describe(ShipAbility ability, ShipType hull)
    {
        ShipManeuverProfile m = hull.Maneuvers;
        return ability switch
        {
            ShipAbility.UTurn => "U-Turn has no cooldown: reverse every turn if you need to.",
            ShipAbility.BreakTurn => "Break Turn has no cooldown: swing around every turn if you need to.",
            ShipAbility.EvasiveDodge => $"Evasive Dodge gives +{(m.EvasiveDodgeEvasionBonus + EvasionBonus) * 100:0}% evasion instead of +{m.EvasiveDodgeEvasionBonus * 100:0}%.",
            ShipAbility.EngineBoost => $"Engine Boost can turn up to {EngineBoostTurnLimitDegrees:0}° instead of {m.EngineBoostTurnLimitDegrees:0}°.",
            ShipAbility.PursuitBurn => $"Pursuit Burn can turn up to {PursuitBurnTurnLimitDegrees:0}° instead of {m.PursuitBurnTurnLimitDegrees:0}°.",
            ShipAbility.EmergencyThrusters => "Emergency Thrusters no longer cost any evasion.",
            ShipAbility.EvasiveSpin => "Evasive Spin flies your full throttle range instead of half of it.",
            ShipAbility.RearGuns => $"Rear Guns hit for full damage instead of {m.RearGunsDamageMultiplier * 100:0}%.",
            ShipAbility.AirBrake => "Air Brake has no cooldown: brake every turn if you need to.",
            ShipAbility.ChaffScreen => $"Chaff lasts {m.ChaffDurationTurns + 1} turns instead of {m.ChaffDurationTurns}.",
            ShipAbility.Sideslip => $"Sideslip slides {SideslipDistance:0} instead of {m.SideslipDistance:0}.",
            ShipAbility.AlphaStrike => $"Alpha Strike adds {AlphaStrikeExtraShots} shots a volley instead of {m.AlphaStrikeExtraShots}.",
            ShipAbility.TractorBeam => $"Tractor Beam drags {TractorPullDistance:0} instead of {m.TractorPullDistance:0}.",
            ShipAbility.RotatingGuns => $"Rotating Guns sweep a {RotatingGunsFireConeDegrees * 2:0}° arc instead of {m.RotatingGunsFireConeDeg * 2:0}°.",
            ShipAbility.HunterLock => $"Hunter Lock gives +{HunterLockAccuracyBonus * 100:0}% accuracy instead of +{m.HunterLockAccuracyBonus * 100:0}%.",
            ShipAbility.SensorScramble => "Sensor Scramble also jams the nearest enemy to your target.",
            ShipAbility.SuppressionFire => "Suppression Fire slows enemy turning twice as much.",
            _ => "",
        };
    }
}
