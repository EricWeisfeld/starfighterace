using Godot;
using System.Collections.Generic;

/// <summary>
/// Draws all planning visuals on top of the battle: the reachable-area fan for
/// the selected fighter, ghost-ship previews, locked-in arc paths, fire-cone
/// preview, selection ring, and HP pips for every fighter.
/// </summary>
public partial class BattleOverlay : Node2D
{
    static readonly Color PathColor = new(0.302f, 0.639f, 1f, 0.6f);
    static readonly Color FanFill = new(1f, 1f, 1f, 0.055f);
    static readonly Color FanEdge = new(1f, 1f, 1f, 0.28f);
    static readonly Color ConeFill = new(1f, 0.9f, 0.4f, 0.06f);
    static readonly Color ConeEdge = new(1f, 0.9f, 0.4f, 0.22f);
    static readonly Color StrongSolution = new(0.3f, 1f, 0.68f, 0.95f);
    static readonly Color PossibleSolution = new(1f, 0.78f, 0.28f, 0.95f);
    static readonly Color NoSolution = new(1f, 0.38f, 0.32f, 0.72f);
    static readonly Color ThreatLow = new(1f, 0.78f, 0.28f, 0.55f);
    static readonly Color ThreatHigh = new(1f, 0.32f, 0.28f, 0.7f);
    static readonly Color TimeSliceCone = new(1f, 0.9f, 0.4f, 0.13f);
    static readonly float[] TimeSliceProgress = { 0.25f, 0.5f, 0.75f };
    const int TargetingTimeSamples = 24;
    const float StrongCoverageThreshold = 0.65f;

    readonly struct TargetPlan
    {
        public readonly ManeuverType Maneuver;
        public readonly float Turn;
        public readonly float Distance;

        public TargetPlan(ManeuverType maneuver, float turn, float distance)
        {
            Maneuver = maneuver;
            Turn = turn;
            Distance = distance;
        }
    }

    sealed class TargetingAnalysis
    {
        public readonly float[] Coverage = new float[TargetingTimeSamples + 1];
        public float BestCoverage;
        public float NearMissScore = float.PositiveInfinity;
        public float DistanceGap;
        public float AngleGapDeg;
        public float SignedAngleDeg;
    }

    /// <summary>
    /// A conservative forecast of how much of the selected ship's planned
    /// path can be covered by plausible enemy routes. It deliberately lives
    /// beneath the route: targeting owns the route's centre line.
    /// </summary>
    sealed class ThreatAnalysis
    {
        public readonly float[] Risk = new float[TargetingTimeSamples + 1];
        public float PeakRisk;
        public int Contacts;
    }

    sealed class ThreatSource
    {
        public Fighter Fighter;
        public List<TargetPlan> Plans;
        public List<PredictedPath> Paths;
        public bool CanThreaten;
    }

    sealed class PredictedPath
    {
        public readonly Vector2[] Positions = new Vector2[TargetingTimeSamples + 1];
        public readonly float[] Headings = new float[TargetingTimeSamples + 1];
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var mgr = BattleManager.Instance;
        if (mgr == null)
            return;

        Fighter focusedTarget = null;
        TargetingAnalysis targetingAnalysis = null;
        ThreatAnalysis threatAnalysis = null;
        if (mgr.CurrentPhase == BattleManager.Phase.Planning && mgr.Selected != null && mgr.Selected.IsAlive)
        {
            focusedTarget = FocusedTarget(mgr);
            if (focusedTarget != null)
                targetingAnalysis = AnalyzeTargeting(mgr, mgr.Selected, focusedTarget);
            else
                threatAnalysis = AnalyzeIncomingThreat(mgr, mgr.Selected);
        }

        if (mgr.EscortShip != null)
            DrawEscortObjective(mgr.EscortShip);

        if (mgr.CurrentPhase == BattleManager.Phase.Planning)
        {
            foreach (Fighter f in mgr.PlayerFighters)
            {
                if (!f.IsAlive)
                    continue;
                if (f == mgr.Selected)
                    DrawSelection(mgr, f);

                // Every fighter keeps a ghost visible, even before it has an
                // order. The player grabs that ghost to set its maneuver.
                float alpha = f == mgr.Selected ? 0.9f : f.PlannedTurnAngleRadians.HasValue ? 0.65f : 0.45f;
                DrawPlan(mgr, f, f.PlannedTurnAngleRadians ?? 0f, alpha, showCone: f == mgr.Selected,
                    targeting: f == mgr.Selected ? targetingAnalysis : null,
                    threat: f == mgr.Selected ? threatAnalysis : null);
                if (f == mgr.Selected)
                    DrawManeuverIcons(mgr, f);
            }
        }

        foreach (Fighter f in mgr.PlayerFighters)
            DrawHpBar(f, SignalUi.Positive);
        foreach (Fighter f in mgr.EnemyFighters)
            DrawHpBar(f, SignalUi.Negative);
        if (mgr.PriorityTarget != null && mgr.PriorityTarget.IsAlive)
        {
            Vector2 p = mgr.PriorityTarget.Position;
            DrawArc(p, 34f, 0, Mathf.Tau, 28, SignalUi.Warning, 2f, true);
            DrawString(ThemeDB.FallbackFont, p + new Vector2(-70, -38), "PRIORITY TARGET", HorizontalAlignment.Center, 140, 11, SignalUi.Warning);
        }

        if (targetingAnalysis != null)
            DrawTargetingAssist(mgr, mgr.Selected, focusedTarget, focusedTarget == mgr.PinnedTarget, targetingAnalysis);
    }

    Fighter FocusedTarget(BattleManager mgr)
    {
        // A targeting preview represents an explicit player choice. Hovering
        // can pass over several ships while planning a route, so it must not
        // change the target; a left click pins the target instead.
        return mgr.PinnedTarget;
    }

    void DrawEscortObjective(ObjectiveShip transport)
    {
        Color zone = transport.Escaped ? SignalUi.Positive : new Color(0.35f, 0.9f, 1f, 0.9f);
        DrawLine(transport.Position, transport.Destination, new Color(zone, 0.18f), 2f, true);
        DrawCircle(transport.Destination, transport.DestinationRadius, new Color(zone, 0.08f));
        DrawArc(transport.Destination, transport.DestinationRadius, 0f, Mathf.Tau, 48, zone, 3f, true);
        DrawArc(transport.Destination, transport.DestinationRadius * 0.62f, 0f, Mathf.Tau, 36, new Color(zone, 0.45f), 1.5f, true);
        DrawString(ThemeDB.FallbackFont, transport.Destination + new Vector2(-90f, -transport.DestinationRadius - 14f),
            transport.Escaped ? "TRANSPORT SECURED" : "JUMP ZONE",
            HorizontalAlignment.Center, 180f, 13, zone);
    }

    void DrawManeuverIcons(BattleManager mgr, Fighter fighter)
    {
        var icons = mgr.GetManeuverIcons(fighter);
        Vector2 mouse = GetGlobalMousePosition();
        string hoveredName = null;
        Font font = ThemeDB.FallbackFont;

        for (int i = 0; i < icons.Count; i++)
        {
            BattleManager.ManeuverIcon icon = icons[i];
            Rect2 bounds = mgr.GetManeuverIconBounds(fighter, i, icons.Count);
            Color color = mgr.GetManeuverIconColor(icon);
            bool hovered = bounds.HasPoint(mouse);
            bool active = IsActiveManeuverIcon(fighter, icon);
            Color fill = new Color(color, active ? 0.42f : hovered ? 0.32f : 0.16f);

            DrawRect(bounds, fill, true);
            DrawRect(bounds, new Color(color, hovered ? 1f : 0.7f), false, hovered ? 2.5f : 1.5f);
            DrawString(font, bounds.Position + new Vector2(0f, 22f), mgr.GetManeuverIconSymbol(icon),
                HorizontalAlignment.Center, bounds.Size.X, 13, new Color(1f, 1f, 1f, 0.95f));
            if (hovered)
                hoveredName = mgr.GetManeuverIconName(fighter, icon);
        }

        if (hoveredName != null)
        {
            Rect2 firstIcon = mgr.GetManeuverIconBounds(fighter, 0, icons.Count);
            DrawString(font, firstIcon.Position + new Vector2(44f, -8f), hoveredName,
                HorizontalAlignment.Left, -1f, 12, new Color(0.85f, 0.94f, 1f, 0.96f));
        }
    }

    static bool IsActiveManeuverIcon(Fighter fighter, BattleManager.ManeuverIcon icon) => icon switch
    {
        BattleManager.ManeuverIcon.Normal => fighter.PlannedManeuver == ManeuverType.Normal,
        BattleManager.ManeuverIcon.UTurnLeft => fighter.PlannedManeuver == ManeuverType.UTurn && fighter.PlannedTurnAngleRadians < 0f,
        BattleManager.ManeuverIcon.UTurnRight => fighter.PlannedManeuver == ManeuverType.UTurn && fighter.PlannedTurnAngleRadians > 0f,
        BattleManager.ManeuverIcon.BreakTurnLeft =>
            fighter.PlannedManeuver == ManeuverType.BreakTurn && fighter.PlannedTurnAngleRadians < 0f,
        BattleManager.ManeuverIcon.BreakTurnRight =>
            fighter.PlannedManeuver == ManeuverType.BreakTurn && fighter.PlannedTurnAngleRadians > 0f,
        BattleManager.ManeuverIcon.EngineBoost => fighter.PlannedManeuver == ManeuverType.EngineBoost,
        BattleManager.ManeuverIcon.RotatingGuns => fighter.PlannedManeuver == ManeuverType.RotatingGuns,
        BattleManager.ManeuverIcon.EmergencyThrusters => fighter.PlannedManeuver == ManeuverType.EmergencyThrusters,
        BattleManager.ManeuverIcon.SnapTurnLeft => fighter.PlannedManeuver == ManeuverType.SnapTurn && fighter.PlannedTurnAngleRadians < 0f,
        BattleManager.ManeuverIcon.SnapTurnRight => fighter.PlannedManeuver == ManeuverType.SnapTurn && fighter.PlannedTurnAngleRadians > 0f,
        BattleManager.ManeuverIcon.PursuitBurn => fighter.PlannedManeuver == ManeuverType.PursuitBurn,
        BattleManager.ManeuverIcon.EcmJink => fighter.PlannedManeuver == ManeuverType.EcmJink,
        BattleManager.ManeuverIcon.GhostRun => fighter.PlannedManeuver == ManeuverType.GhostRun,
        BattleManager.ManeuverIcon.EvasiveDodgeLeft => fighter.PlannedManeuver == ManeuverType.EvasiveDodge && fighter.PlannedTurnAngleRadians < 0f,
        BattleManager.ManeuverIcon.EvasiveDodgeRight => fighter.PlannedManeuver == ManeuverType.EvasiveDodge && fighter.PlannedTurnAngleRadians > 0f,
        _ => false,
    };

    void DrawSelection(BattleManager mgr, Fighter f)
    {
        DrawArc(f.Position, 24f, 0, Mathf.Tau, 40, new Color(0.302f, 0.639f, 1f, 0.9f), 2f, true);
        if (f.PlannedManeuver is ManeuverType.Normal or ManeuverType.EngineBoost or ManeuverType.EmergencyThrusters or ManeuverType.PursuitBurn or ManeuverType.EcmJink or ManeuverType.GhostRun)
            DrawReachableFan(f);
        DrawMoveDistanceReadout(f);
    }

    void DrawMoveDistanceReadout(Fighter f)
    {
        Font font = ThemeDB.FallbackFont;
        if (f.PlannedManeuver == ManeuverType.UTurn)
        {
            DrawString(font, f.Position + new Vector2(-90f, 44f),
                $"U-TURN {f.PlannedPathDistance:0}  //  180 deg",
                HorizontalAlignment.Center, 180f, 12, new Color(0.5f, 0.85f, 1f, 0.9f));
            return;
        }
        if (f.PlannedManeuver == ManeuverType.BreakTurn)
        {
            string name = "BREAK TURN";
            DrawString(font, f.Position + new Vector2(-90f, 44f),
                $"{name} {f.PlannedPathDistance:0}  //  180 deg",
                HorizontalAlignment.Center, 180f, 12, new Color(1f, 0.62f, 0.3f, 0.95f));
            return;
        }
        if (f.PlannedManeuver == ManeuverType.EngineBoost)
        {
            DrawString(font, f.Position + new Vector2(-90f, 44f),
                $"ENGINE BOOST {f.PlannedPathDistance:0}  //  ONCE PER BATTLE",
                HorizontalAlignment.Center, 180f, 12, new Color(0.35f, 1f, 0.8f, 0.95f));
            return;
        }
        if (f.PlannedManeuver == ManeuverType.PursuitBurn)
        {
            DrawString(font, f.Position + new Vector2(-90f, 44f),
                $"PURSUIT BURN {f.PlannedPathDistance:0}  //  {f.PursuitBurnTurnLimitDegrees:0} DEG MAX TURN",
                HorizontalAlignment.Center, 180f, 12, new Color(0.4f, 1f, 0.7f, 0.95f));
            return;
        }
        if (f.PlannedManeuver == ManeuverType.SnapTurn)
        {
            DrawString(font, f.Position + new Vector2(-90f, 44f),
                $"SNAP TURN {f.PlannedPathDistance:0}  //  145 DEG",
                HorizontalAlignment.Center, 180f, 12, new Color(0.95f, 0.55f, 1f, 0.95f));
            return;
        }
        if (f.PlannedManeuver == ManeuverType.EcmJink)
        {
            DrawString(font, f.Position + new Vector2(-90f, 44f),
                $"ECM JINK {f.PlannedPathDistance:0}  //  EVA +{f.Type.EcmJinkEvasionBonus * 100:0}%",
                HorizontalAlignment.Center, 180f, 12, new Color(0.45f, 0.7f, 1f, 0.95f));
            return;
        }
        if (f.PlannedManeuver == ManeuverType.GhostRun)
        {
            DrawString(font, f.Position + new Vector2(-90f, 44f),
                $"GHOST RUN {f.PlannedPathDistance:0}  //  EVA +{f.Type.GhostRunEvasionBonus * 100:0}%",
                HorizontalAlignment.Center, 180f, 12, new Color(0.6f, 0.95f, 1f, 0.95f));
            return;
        }
        if (f.PlannedManeuver == ManeuverType.EvasiveDodge)
        {
            DrawString(font, f.Position + new Vector2(-90f, 44f),
                $"EVASIVE DODGE {f.PlannedPathDistance:0}  //  {f.Type.EvasiveDodgeAngleDegrees:0} DEG + EVA +{f.Type.EvasiveDodgeEvasionBonus * 100:0}%",
                HorizontalAlignment.Center, 180f, 12, new Color(0.35f, 0.9f, 1f, 0.95f));
            return;
        }
        if (f.PlannedManeuver == ManeuverType.RotatingGuns)
        {
            DrawString(font, f.Position + new Vector2(-90f, 44f),
                $"ROTATING GUNS {f.PlannedPathDistance:0}  //  {f.EffectiveFireConeDeg:0}° FIRE ARC",
                HorizontalAlignment.Center, 180f, 12, new Color(1f, 0.78f, 0.35f, 0.95f));
            return;
        }
        if (f.PlannedManeuver == ManeuverType.EmergencyThrusters)
        {
            DrawString(font, f.Position + new Vector2(-90f, 44f),
                $"EMERGENCY THRUSTERS {f.PlannedPathDistance:0}  //  EVA -{f.Type.EmergencyThrustersEvasionPenalty * 100:0}%",
                HorizontalAlignment.Center, 180f, 12, new Color(1f, 0.38f, 0.3f, 0.95f));
            return;
        }
        DrawString(font, f.Position + new Vector2(-90f, 40f),
            $"NORMAL MOVE {f.PlannedPathDistance:0} / {f.NormalMoveMinDistance:0}-{f.NormalMoveMaxDistance:0}",
            HorizontalAlignment.Center, 180f, 12, new Color(1f, 1f, 1f, 0.75f));
        DrawString(font, f.Position + new Vector2(-90f, 55f),
            $"MAX TURN {f.PlannedNormalTurnLimitDegrees:0}°",
            HorizontalAlignment.Center, 180f, 12, new Color(0.302f, 0.639f, 1f, 0.9f));

        Vector2 forward = Vector2.FromAngle(f.Heading);
        Vector2 min = f.Position + forward * f.NormalMoveMinDistance;
        Vector2 max = f.Position + forward * f.NormalMoveMaxDistance;
        Vector2 current = f.Position + forward * f.PlannedPathDistance;
        DrawLine(min, max, new Color(0.302f, 0.639f, 1f, 0.55f), 3f, true);
        DrawCircle(min, 4f, new Color(1f, 1f, 1f, 0.4f));
        DrawCircle(max, 4f, new Color(1f, 1f, 1f, 0.4f));
        DrawCircle(current, 6f, new Color(0.302f, 0.639f, 1f, 0.9f));
    }

    void DrawReachableFan(Fighter f)
    {
        float maxTurn = Mathf.DegToRad(f.PlannedManeuver == ManeuverType.EngineBoost
            ? f.EngineBoostTurnLimitDegrees
            : f.PlannedManeuver == ManeuverType.EmergencyThrusters
                ? f.EmergencyThrustersTurnLimitDegrees
                : f.PlannedManeuver == ManeuverType.PursuitBurn
                    ? f.PursuitBurnTurnLimitDegrees
                    : f.PlannedManeuver == ManeuverType.EcmJink
                        ? f.EcmJinkTurnLimitDegrees
                        : f.PlannedManeuver == ManeuverType.GhostRun
                            ? f.GhostRunTurnLimitDegrees
                : f.PlannedNormalTurnLimitDegrees);
        const int samples = 32;
        var edge = new Vector2[samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            float turn = Mathf.Lerp(-maxTurn, maxTurn, i / (float)samples);
            Fighter.ArcPoint(f.Position, f.Heading, turn, f.PlannedPathDistance, 1f, out edge[i], out _);
        }

        var poly = new Vector2[samples + 2];
        edge.CopyTo(poly, 0);
        poly[samples + 1] = f.Position;
        DrawColoredPolygon(poly, FanFill);
        DrawPolyline(edge, FanEdge, 1.5f, true);
        DrawLine(f.Position, edge[0], FanEdge, 1.5f, true);
        DrawLine(f.Position, edge[samples], FanEdge, 1.5f, true);
    }

    void DrawPlan(BattleManager mgr, Fighter f, float turn, float alpha, bool showCone = false,
        TargetingAnalysis targeting = null, ThreatAnalysis threat = null)
    {
        const int samples = 24;
        var pts = new Vector2[samples + 1];
        float endHeading = f.Heading;
        for (int i = 0; i <= samples; i++)
            Fighter.ManeuverPoint(f.PlannedManeuver, f.Position, f.Heading, turn, f.PlannedPathDistance,
                i / (float)samples, out pts[i], out endHeading);
        Color pathColor = f.PlannedManeuver switch
        {
            ManeuverType.UTurn => new Color(0.62f, 0.48f, 1f, 0.85f),
            ManeuverType.BreakTurn => new Color(1f, 0.62f, 0.3f, 0.88f),
            ManeuverType.EngineBoost => new Color(0.3f, 1f, 0.75f, 0.88f),
            ManeuverType.RotatingGuns => new Color(1f, 0.78f, 0.32f, 0.92f),
            ManeuverType.EmergencyThrusters => new Color(1f, 0.36f, 0.3f, 0.92f),
            ManeuverType.SnapTurn => new Color(0.95f, 0.55f, 1f, 0.92f),
            ManeuverType.PursuitBurn => new Color(0.4f, 1f, 0.7f, 0.92f),
            ManeuverType.EcmJink => new Color(0.45f, 0.7f, 1f, 0.92f),
            ManeuverType.GhostRun => new Color(0.6f, 0.95f, 1f, 0.92f),
            ManeuverType.EvasiveDodge => new Color(0.35f, 0.9f, 1f, 0.92f),
            _ => PathColor,
        };
        bool fatalAsteroidPath = mgr.PathHitsAsteroid(f, f.PlannedManeuver, turn, f.PlannedPathDistance);
        int scrapeDamage = fatalAsteroidPath ? 0 : mgr.PathScrapeDamage(f, f.PlannedManeuver, turn, f.PlannedPathDistance);
        if (fatalAsteroidPath)
            pathColor = new Color(1f, 0.25f, 0.2f, 0.95f);
        if (threat != null && !fatalAsteroidPath)
            DrawThreatPath(pts, threat);
        if (targeting != null && !fatalAsteroidPath)
            DrawTargetedPath(pts, pathColor, alpha, targeting);
        else
            DrawPolyline(pts, new Color(pathColor, pathColor.A * alpha / 0.8f), 2f, true);

        Vector2 end = pts[samples];
        if (showCone)
            DrawFireCone(end, endHeading, f.EffectiveFireRange, f.EffectiveFireConeDeg);
        if (fatalAsteroidPath)
            DrawString(ThemeDB.FallbackFont, end + new Vector2(-55f, -24f), "FATAL COLLISION",
                HorizontalAlignment.Center, 110f, 11, new Color(1f, 0.35f, 0.3f, alpha));
        else if (scrapeDamage > 0)
            DrawString(ThemeDB.FallbackFont, end + new Vector2(-55f, -24f), $"SCRAPE - {scrapeDamage} HULL",
                HorizontalAlignment.Center, 110f, 11, new Color(1f, 0.72f, 0.28f, alpha));
        else if (threat != null)
            DrawThreatReadout(end, threat, alpha);

        Texture2D tex = f.BaseTexture;
        DrawSetTransform(end, endHeading + Mathf.Pi / 2f, Vector2.One);
        DrawTexture(tex, -tex.GetSize() / 2f, new Color(1, 1, 1, alpha * 0.6f));
        DrawSetTransform(Vector2.Zero);
    }

    void DrawThreatPath(Vector2[] points, ThreatAnalysis threat)
    {
        // The underlay and perpendicular ticks are intentionally a different
        // visual language from the thin, centre-line targeting highlights.
        // This makes the two lenses readable without ever compositing them.
        for (int i = 0; i < TargetingTimeSamples; i++)
        {
            float risk = Mathf.Max(threat.Risk[i], threat.Risk[i + 1]);
            if (risk <= 0.01f)
                continue;

            Color color = ThreatColor(risk);
            DrawLine(points[i], points[i + 1], new Color(color, 0.13f + risk * 0.3f), 6f, true);

            if (risk < 0.12f)
                continue;
            Vector2 segment = points[i + 1] - points[i];
            if (segment.LengthSquared() <= 0.01f)
                continue;
            Vector2 mid = points[i].Lerp(points[i + 1], 0.5f);
            Vector2 normal = segment.Normalized().Rotated(Mathf.Pi / 2f) * 4f;
            DrawLine(mid - normal, mid + normal, color, 1.5f, true);
        }
    }

    void DrawThreatReadout(Vector2 end, ThreatAnalysis threat, float alpha)
    {
        string status = threat.PeakRisk switch
        {
            < 0.01f => "CLEAR",
            < 0.12f => "LOW",
            < 0.3f => "CAUTION",
            _ => "HIGH",
        };
        Color color = threat.PeakRisk < 0.01f
            ? new Color(0.45f, 0.9f, 0.75f, alpha)
            : new Color(ThreatColor(threat.PeakRisk), alpha);
        string contacts = threat.Contacts == 1 ? "1 CONTACT" : $"{threat.Contacts} CONTACTS";
        DrawString(ThemeDB.FallbackFont, end + new Vector2(-68f, -24f),
            $"THREAT // {status}  {contacts}", HorizontalAlignment.Center, 136f, 10, color);
    }

    static Color ThreatColor(float risk) => ThreatLow.Lerp(ThreatHigh, Mathf.Clamp(risk / 0.35f, 0f, 1f));

    void DrawTargetedPath(Vector2[] points, Color maneuverColor, float alpha, TargetingAnalysis targeting)
    {
        float baseAlpha = Mathf.Min(0.34f, maneuverColor.A * alpha / 0.8f * 0.55f);
        DrawPolyline(points, new Color(maneuverColor, baseAlpha), 1.6f, true);

        for (int i = 0; i < TargetingTimeSamples; i++)
        {
            float coverage = Mathf.Max(targeting.Coverage[i], targeting.Coverage[i + 1]);
            if (coverage >= StrongCoverageThreshold)
            {
                DrawLine(points[i], points[i + 1], StrongSolution, 3f, true);
            }
            else if (coverage > 0f)
            {
                Vector2 dashEnd = points[i].Lerp(points[i + 1], 0.58f);
                DrawLine(points[i], dashEnd, PossibleSolution, 2.5f, true);
            }
        }
    }

    void DrawTargetingAssist(BattleManager mgr, Fighter shooter, Fighter target, bool pinned, TargetingAnalysis analysis)
    {
        DrawTimeSliceCones(mgr, shooter);
        DrawTargetReticle(shooter, target, analysis, pinned);
    }

    void DrawTimeSliceCones(BattleManager mgr, Fighter shooter)
    {
        foreach (float progress in TimeSliceProgress)
        {
            mgr.PredictExecutionPoint(shooter, shooter.PlannedManeuver, shooter.PlannedTurnAngleRadians ?? 0f,
                shooter.PlannedPathDistance, progress, out Vector2 position, out float heading);
            DrawFireConeOutline(position, heading, shooter.EffectiveFireRange, shooter.EffectiveFireConeDeg);
        }
    }

    void DrawFireConeOutline(Vector2 tip, float heading, float range, float coneDeg)
    {
        float cone = Mathf.DegToRad(coneDeg);
        float left = heading - cone;
        float right = heading + cone;
        DrawLine(tip, tip + Vector2.FromAngle(left) * range, TimeSliceCone, 1f, true);
        DrawLine(tip, tip + Vector2.FromAngle(right) * range, TimeSliceCone, 1f, true);
        DrawArc(tip, range, left, right, 10, TimeSliceCone, 1f, true);
    }

    TargetingAnalysis AnalyzeTargeting(BattleManager mgr, Fighter shooter, Fighter target)
    {
        var targetPlans = new List<TargetPlan>();
        foreach (TargetPlan plan in LegalTargetPlans(target))
            targetPlans.Add(plan);
        return AnalyzeTargeting(mgr, shooter, target, targetPlans);
    }

    PredictedPath PredictPath(BattleManager mgr, Fighter fighter, ManeuverType maneuver, float turn, float distance)
    {
        var path = new PredictedPath();
        mgr.PredictExecutionPath(fighter, maneuver, turn, distance, path.Positions, path.Headings);
        return path;
    }

    TargetingAnalysis AnalyzeTargeting(BattleManager mgr, Fighter shooter, Fighter target, List<TargetPlan> targetPlans)
    {
        var result = new TargetingAnalysis();
        float coneRadians = Mathf.DegToRad(shooter.EffectiveFireConeDeg);
        float range = shooter.EffectiveFireRange;
        PredictedPath shooterPath = PredictPath(mgr, shooter, shooter.PlannedManeuver,
            shooter.PlannedTurnAngleRadians ?? 0f, shooter.PlannedPathDistance);
        var targetPaths = new List<PredictedPath>();
        foreach (TargetPlan plan in targetPlans)
            targetPaths.Add(PredictPath(mgr, target, plan.Maneuver, plan.Turn, plan.Distance));

        for (int i = 0; i <= TargetingTimeSamples; i++)
        {
            Vector2 shooterPosition = shooterPath.Positions[i];
            float shooterHeading = shooterPath.Headings[i];

            int solutions = 0;
            foreach (PredictedPath targetPath in targetPaths)
            {
                Vector2 targetPosition = targetPath.Positions[i];
                Vector2 offset = targetPosition - shooterPosition;
                float distance = offset.Length();
                float signedAngle = Mathf.Wrap(offset.Angle() - shooterHeading, -Mathf.Pi, Mathf.Pi);
                float distanceGap = Mathf.Max(0f, distance - range);
                float angleGap = Mathf.Max(0f, Mathf.Abs(signedAngle) - coneRadians);
                if (distanceGap <= 0f && angleGap <= 0f)
                    solutions++;

                // Normalize the two failure modes to the size of a useful
                // near-miss band, then keep the closest sampled course.
                float nearMissScore = Mathf.Max(distanceGap / (range * 0.2f),
                    angleGap / Mathf.DegToRad(10f));
                if (nearMissScore < result.NearMissScore)
                {
                    result.NearMissScore = nearMissScore;
                    result.DistanceGap = distanceGap;
                    result.AngleGapDeg = Mathf.RadToDeg(angleGap);
                    result.SignedAngleDeg = Mathf.RadToDeg(signedAngle);
                }
            }

            result.Coverage[i] = targetPlans.Count > 0 ? solutions / (float)targetPlans.Count : 0f;
            if (result.Coverage[i] > result.BestCoverage)
                result.BestCoverage = result.Coverage[i];
        }
        return result;
    }

    ThreatAnalysis AnalyzeIncomingThreat(BattleManager mgr, Fighter target)
    {
        var result = new ThreatAnalysis();
        var sources = new List<ThreatSource>();
        foreach (Fighter enemy in mgr.EnemyFighters)
        {
            if (!enemy.IsAlive)
                continue;
            var plans = new List<TargetPlan>();
            foreach (TargetPlan plan in LegalTargetPlans(enemy))
                plans.Add(plan);
            if (plans.Count > 0)
            {
                var paths = new List<PredictedPath>();
                foreach (TargetPlan plan in plans)
                    paths.Add(PredictPath(mgr, enemy, plan.Maneuver, plan.Turn, plan.Distance));
                sources.Add(new ThreatSource { Fighter = enemy, Plans = plans, Paths = paths });
            }
        }

        PredictedPath targetPath = PredictPath(mgr, target, target.PlannedManeuver,
            target.PlannedTurnAngleRadians ?? 0f, target.PlannedPathDistance);

        for (int i = 0; i <= TargetingTimeSamples; i++)
        {
            Vector2 targetPosition = targetPath.Positions[i];

            // Each source contributes its chance to establish a firing window
            // times the real hit chance. Combining misses prevents several
            // enemies from inflating this preview by simple addition.
            float combinedMiss = 1f;
            foreach (ThreatSource source in sources)
            {
                int firingPlans = 0;
                float sourceRisk = 0f;
                foreach (PredictedPath enemyPath in source.Paths)
                {
                    Vector2 enemyPosition = enemyPath.Positions[i];
                    float enemyHeading = enemyPath.Headings[i];
                    Vector2 offset = targetPosition - enemyPosition;
                    if (offset.Length() > source.Fighter.EffectiveFireRange)
                        continue;
                    float angle = Mathf.Abs(Mathf.Wrap(offset.Angle() - enemyHeading, -Mathf.Pi, Mathf.Pi));
                    if (angle <= Mathf.DegToRad(source.Fighter.EffectiveFireConeDeg))
                    {
                        firingPlans++;
                        Vector2 enemyNose = enemyPosition + Vector2.FromAngle(enemyHeading) * 20f;
                        float terrainAccuracy = mgr.ShotPassesNebula(enemyNose, targetPosition)
                            ? BattleManager.NebulaAccuracyMultiplier
                            : 1f;
                        sourceRisk += source.Fighter.EffectiveAccuracyAgainst(target) * terrainAccuracy
                            * (1f - target.EffectiveEvasion);
                    }
                }

                if (firingPlans == 0)
                    continue;

                source.CanThreaten = true;
                float averageRisk = sourceRisk / source.Plans.Count;
                combinedMiss *= 1f - Mathf.Clamp(averageRisk, 0f, 0.95f);
            }

            result.Risk[i] = 1f - combinedMiss;
            result.PeakRisk = Mathf.Max(result.PeakRisk, result.Risk[i]);
        }

        foreach (ThreatSource source in sources)
            if (source.CanThreaten)
                result.Contacts++;
        return result;
    }

    void DrawTargetReticle(Fighter shooter, Fighter target, TargetingAnalysis analysis, bool pinned)
    {
        Vector2 center = target.Position;
        Color color = analysis.BestCoverage >= StrongCoverageThreshold
            ? StrongSolution
            : analysis.BestCoverage > 0f ? PossibleSolution : NoSolution;

        if (analysis.BestCoverage >= StrongCoverageThreshold)
            DrawArc(center, 27f, 0f, Mathf.Tau, 32, color, 2f, true);
        else
            DrawDashedArc(center, 27f, color);

        const float bracketX = 31f;
        const float bracketOuterY = 14f;
        const float bracketInnerY = 7f;
        const float bracketArm = 7f;
        DrawLine(center + new Vector2(-bracketX, -bracketOuterY), center + new Vector2(-bracketX, -bracketInnerY), color, 2f, true);
        DrawLine(center + new Vector2(-bracketX, -bracketOuterY), center + new Vector2(-bracketX + bracketArm, -bracketOuterY), color, 2f, true);
        DrawLine(center + new Vector2(-bracketX, bracketOuterY), center + new Vector2(-bracketX, bracketInnerY), color, 2f, true);
        DrawLine(center + new Vector2(-bracketX, bracketOuterY), center + new Vector2(-bracketX + bracketArm, bracketOuterY), color, 2f, true);
        DrawLine(center + new Vector2(bracketX, -bracketOuterY), center + new Vector2(bracketX, -bracketInnerY), color, 2f, true);
        DrawLine(center + new Vector2(bracketX, -bracketOuterY), center + new Vector2(bracketX - bracketArm, -bracketOuterY), color, 2f, true);
        DrawLine(center + new Vector2(bracketX, bracketOuterY), center + new Vector2(bracketX, bracketInnerY), color, 2f, true);
        DrawLine(center + new Vector2(bracketX, bracketOuterY), center + new Vector2(bracketX - bracketArm, bracketOuterY), color, 2f, true);

        if (pinned)
            DrawCircle(center + new Vector2(0f, -31f), 3.5f, color);

        string status;
        if (analysis.BestCoverage >= StrongCoverageThreshold)
        {
            status = "STRONG";
        }
        else if (analysis.BestCoverage > 0f)
        {
            status = "POSSIBLE";
        }
        else if (analysis.NearMissScore <= 1f)
        {
            float distanceWeight = analysis.DistanceGap / (shooter.EffectiveFireRange * 0.2f);
            float angleWeight = analysis.AngleGapDeg / 10f;
            status = distanceWeight >= angleWeight && analysis.DistanceGap > 0f
                ? $"{Mathf.CeilToInt(analysis.DistanceGap)} OUT"
                : $"{Mathf.CeilToInt(analysis.AngleGapDeg)} DEG {(analysis.SignedAngleDeg >= 0f ? "RIGHT" : "LEFT")}";
        }
        else
        {
            status = "NO WINDOW";
        }

        DrawString(ThemeDB.FallbackFont, center + new Vector2(37f, 4f), status,
            HorizontalAlignment.Left, -1f, 11, color);
    }

    void DrawDashedArc(Vector2 center, float radius, Color color)
    {
        const int dashes = 8;
        float dashWidth = Mathf.Tau / dashes * 0.56f;
        for (int i = 0; i < dashes; i++)
        {
            float start = i * Mathf.Tau / dashes;
            DrawArc(center, radius, start, start + dashWidth, 4, color, 2f, true);
        }
    }

    IEnumerable<TargetPlan> LegalTargetPlans(Fighter target)
    {
        // The normal envelope carries most of the weight: three throttle
        // settings crossed with five representative turn rates.
        for (int distanceIndex = 0; distanceIndex <= 2; distanceIndex++)
        {
            float distance = Mathf.Lerp(target.NormalMoveMinDistance, target.NormalMoveMaxDistance, distanceIndex / 2f);
            float maxTurn = Mathf.DegToRad(target.GetNormalTurnLimitDegrees(distance));
            for (int turnIndex = -2; turnIndex <= 2; turnIndex++)
                yield return new TargetPlan(ManeuverType.Normal, maxTurn * turnIndex / 2f, distance);
        }

        if (target.HasAbility(ShipAbility.UTurn) && target.IsManeuverReady(ManeuverType.UTurn))
        {
            yield return new TargetPlan(ManeuverType.UTurn, -Mathf.Pi, target.Type.UTurnMoveDistance);
            yield return new TargetPlan(ManeuverType.UTurn, Mathf.Pi, target.Type.UTurnMoveDistance);
        }
        if (target.HasAbility(ShipAbility.BreakTurn) && target.IsManeuverReady(ManeuverType.BreakTurn))
        {
            yield return new TargetPlan(ManeuverType.BreakTurn, -Mathf.Pi, target.Type.BreakTurnMoveDistance);
            yield return new TargetPlan(ManeuverType.BreakTurn, Mathf.Pi, target.Type.BreakTurnMoveDistance);
        }
        if (target.HasAbility(ShipAbility.SnapTurn) && target.IsManeuverReady(ManeuverType.SnapTurn))
        {
            float turn = Mathf.DegToRad(target.Type.SnapTurnAngleDegrees);
            yield return new TargetPlan(ManeuverType.SnapTurn, -turn, target.Type.SnapTurnMoveDistance);
            yield return new TargetPlan(ManeuverType.SnapTurn, turn, target.Type.SnapTurnMoveDistance);
        }
        if (target.HasAbility(ShipAbility.RotatingGuns) && target.IsManeuverReady(ManeuverType.RotatingGuns))
            yield return new TargetPlan(ManeuverType.RotatingGuns, 0f, target.Type.RotatingGunsMoveDistance);

        foreach (float turn in SampleTurns(target.EngineBoostTurnLimitDegrees))
            if (target.HasAbility(ShipAbility.EngineBoost) && target.IsManeuverReady(ManeuverType.EngineBoost))
                yield return new TargetPlan(ManeuverType.EngineBoost, turn, target.Type.EngineBoostMoveDistance);
        foreach (float turn in SampleTurns(target.EmergencyThrustersTurnLimitDegrees))
            if (target.HasAbility(ShipAbility.EmergencyThrusters) && target.IsManeuverReady(ManeuverType.EmergencyThrusters))
                yield return new TargetPlan(ManeuverType.EmergencyThrusters, turn, target.Type.EmergencyThrustersMoveDistance);
        foreach (float turn in SampleTurns(target.PursuitBurnTurnLimitDegrees))
            if (target.HasAbility(ShipAbility.PursuitBurn) && target.IsManeuverReady(ManeuverType.PursuitBurn))
                yield return new TargetPlan(ManeuverType.PursuitBurn, turn, target.Type.PursuitBurnMoveDistance);
        foreach (float turn in SampleTurns(target.EcmJinkTurnLimitDegrees))
            if (target.HasAbility(ShipAbility.EcmJink) && target.IsManeuverReady(ManeuverType.EcmJink))
                yield return new TargetPlan(ManeuverType.EcmJink, turn, target.Type.EcmJinkMoveDistance);
        foreach (float turn in SampleTurns(target.GhostRunTurnLimitDegrees))
            if (target.HasAbility(ShipAbility.GhostRun) && target.IsManeuverReady(ManeuverType.GhostRun))
                yield return new TargetPlan(ManeuverType.GhostRun, turn, target.Type.GhostRunMoveDistance);
        if (target.HasAbility(ShipAbility.EvasiveDodge) && target.IsManeuverReady(ManeuverType.EvasiveDodge))
        {
            float turn = Mathf.DegToRad(target.Type.EvasiveDodgeAngleDegrees);
            yield return new TargetPlan(ManeuverType.EvasiveDodge, -turn, target.Type.EvasiveDodgeMoveDistance);
            yield return new TargetPlan(ManeuverType.EvasiveDodge, turn, target.Type.EvasiveDodgeMoveDistance);
        }
    }

    static IEnumerable<float> SampleTurns(float maxTurnDegrees)
    {
        float maxTurn = Mathf.DegToRad(maxTurnDegrees);
        yield return -maxTurn;
        yield return 0f;
        yield return maxTurn;
    }

    void DrawFireCone(Vector2 tip, float heading, float range, float coneDeg)
    {
        float cone = Mathf.DegToRad(coneDeg);
        const int samples = 10;
        var poly = new Vector2[samples + 2];
        poly[0] = tip;
        for (int i = 0; i <= samples; i++)
        {
            float a = heading + Mathf.Lerp(-cone, cone, i / (float)samples);
            poly[i + 1] = tip + Vector2.FromAngle(a) * range;
        }
        DrawColoredPolygon(poly, ConeFill);
        DrawLine(tip, poly[1], ConeEdge, 1f, true);
        DrawLine(tip, poly[samples + 1], ConeEdge, 1f, true);
        DrawArc(tip, range, heading - cone, heading + cone, samples + 1, ConeEdge, 1f, true);
    }

    void DrawHpBar(Fighter f, Color color)
    {
        if (!f.IsAlive)
            return;
        const float width = 28f;
        const float shieldHeight = 3f;
        const float hullHeight = 4f;
        Vector2 shieldTopLeft = f.Position + new Vector2(-width / 2f, 20f);
        DrawRect(new Rect2(shieldTopLeft, new Vector2(width, shieldHeight)), new Color(0.2f, 0.5f, 1f, 0.2f));
        float shieldFraction = f.MaxShield > 0 ? f.Shield / (float)f.MaxShield : 0f;
        DrawRect(new Rect2(shieldTopLeft, new Vector2(width * shieldFraction, shieldHeight)), new Color(0.35f, 0.7f, 1f));

        Vector2 hullTopLeft = f.Position + new Vector2(-width / 2f, 25f);
        DrawRect(new Rect2(hullTopLeft, new Vector2(width, hullHeight)), new Color(1, 1, 1, 0.13f));
        float hullFraction = f.Hp / (float)f.MaxHp;
        DrawRect(new Rect2(hullTopLeft, new Vector2(width * hullFraction, hullHeight)), color);

        // Campaign pilots wear a nameplate so you know who is on the line
        // (above the ship: the move readout owns the space below).
        if (f.Pilot != null)
        {
            DrawString(ThemeDB.FallbackFont, f.Position + new Vector2(-60f, -30f),
                $"{f.Pilot.Callsign} · LV{f.Pilot.Level}",
                HorizontalAlignment.Center, 120f, 10, new Color(SignalUi.Body.R, SignalUi.Body.G, SignalUi.Body.B, 0.75f));
        }
    }
}
