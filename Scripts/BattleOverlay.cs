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
    static readonly Color NebulaTag = new(0.56f, 0.76f, 1f);
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

    // World units per viewport pixel for this frame. Text, HP bars and line
    // widths are multiplied by it so they keep a constant, readable size on
    // screen at any camera zoom.
    float _s = 1f;

    float Px(float screenPixels) => screenPixels * _s;
    int FontPx(int screenSize) => Mathf.Max(1, Mathf.RoundToInt(screenSize * _s));
    float W(float width) => width * Mathf.Max(1f, _s);

    // Names, bars and status tags fade out while a turn plays so the fight
    // itself is what you watch; damage numbers carry the story meanwhile.
    float _labelAlpha = 1f;

    public override void _Process(double delta)
    {
        bool executing = BattleManager.Instance?.CurrentPhase == BattleManager.Phase.Executing;
        float realDelta = (float)delta / (float)Mathf.Max(Engine.TimeScale, 0.01);
        _labelAlpha = Mathf.MoveToward(_labelAlpha, executing ? 0f : 1f, realDelta * 6f);
        QueueRedraw();
    }

    /// <summary>Draws a label centred on a world point at a constant on-screen size.</summary>
    void DrawLabel(Vector2 center, string text, int screenSize, Color color)
    {
        Font font = SignalUi.Display;
        int size = FontPx(screenSize);
        Vector2 extent = font.GetStringSize(text, HorizontalAlignment.Left, -1f, size);
        DrawString(font, center + new Vector2(-extent.X / 2f, size * 0.35f), text, HorizontalAlignment.Left, -1f, size, color);
    }

    public override void _Draw()
    {
        var mgr = BattleManager.Instance;
        if (mgr == null)
            return;
        _s = mgr.ScreenToWorldScale;

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
            }
        }

        foreach (Fighter f in mgr.PlayerFighters)
            DrawHpBar(f, SignalUi.Positive);
        foreach (Fighter f in mgr.EnemyFighters)
            DrawHpBar(f, SignalUi.Negative);
        if (mgr.PriorityTarget != null && mgr.PriorityTarget.IsAlive)
        {
            Vector2 p = mgr.PriorityTarget.Position;
            float ring = Mathf.Max(34f, mgr.PriorityTarget.VisualRadius + 12f);
            DrawArc(p, ring, 0, Mathf.Tau, 28, SignalUi.Warning, W(2f), true);
            if (_labelAlpha > 0f)
                DrawLabel(p + new Vector2(0f, -ring - Px(22f)), "PRIORITY TARGET", SignalUi.FontMicro, new Color(SignalUi.Warning, _labelAlpha));
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
        DrawLine(transport.Position, transport.Destination, new Color(zone, 0.18f), W(2f), true);
        DrawCircle(transport.Destination, transport.DestinationRadius, new Color(zone, 0.08f));
        DrawArc(transport.Destination, transport.DestinationRadius, 0f, Mathf.Tau, 48, zone, W(3f), true);
        DrawArc(transport.Destination, transport.DestinationRadius * 0.62f, 0f, Mathf.Tau, 36, new Color(zone, 0.45f), W(1.5f), true);
        DrawLabel(transport.Destination + new Vector2(0f, -transport.DestinationRadius - Px(18f)),
            transport.Escaped ? "TRANSPORT SECURED" : "JUMP ZONE", SignalUi.FontCaption, zone);
    }

    void DrawSelection(BattleManager mgr, Fighter f)
    {
        DrawArc(f.Position, Mathf.Max(26f, f.VisualRadius + 6f), 0, Mathf.Tau, 40, new Color(0.302f, 0.639f, 1f, 0.9f), W(2.5f), true);
        if (Fighter.FliesLikeNormal(f.PlannedManeuver) ||
            f.PlannedManeuver is ManeuverType.EngineBoost or ManeuverType.EmergencyThrusters or ManeuverType.PursuitBurn or ManeuverType.EcmJink)
            DrawReachableFan(f);
        if (Fighter.FliesLikeNormal(f.PlannedManeuver))
            DrawThrottleGauge(f);
    }

    /// <summary>
    /// The throttle range along the ship's heading, with the planned distance
    /// marked: normal flight's, or the shorter one of an evasive spin. The
    /// numbers live in the HUD.
    /// </summary>
    void DrawThrottleGauge(Fighter f)
    {
        // Drawn at the distances actually flown, so nebula drag shows here too.
        Vector2 forward = Vector2.FromAngle(f.Heading) * f.RouteScale;
        Vector2 min = f.Position + forward * f.MinMoveFor(f.PlannedManeuver);
        Vector2 max = f.Position + forward * f.MaxMoveFor(f.PlannedManeuver);
        Vector2 current = f.Position + forward * f.PlannedPathDistance;
        DrawLine(min, max, new Color(0.302f, 0.639f, 1f, 0.55f), W(3f), true);
        DrawCircle(min, Px(4f), new Color(1f, 1f, 1f, 0.4f));
        DrawCircle(max, Px(4f), new Color(1f, 1f, 1f, 0.4f));
        DrawCircle(current, Px(6f), new Color(0.302f, 0.639f, 1f, 0.9f));
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
                        : f.PlannedNormalTurnLimitDegrees);
        const int samples = 32;
        var edge = new Vector2[samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            float turn = Mathf.Lerp(-maxTurn, maxTurn, i / (float)samples);
            f.RoutePoint(ManeuverType.Normal, turn, f.PlannedPathDistance, 1f, out edge[i], out _);
        }

        var poly = new Vector2[samples + 2];
        edge.CopyTo(poly, 0);
        poly[samples + 1] = f.Position;
        DrawColoredPolygon(poly, FanFill);
        DrawPolyline(edge, FanEdge, W(1.5f), true);
        DrawLine(f.Position, edge[0], FanEdge, W(1.5f), true);
        DrawLine(f.Position, edge[samples], FanEdge, W(1.5f), true);
    }

    void DrawPlan(BattleManager mgr, Fighter f, float turn, float alpha, bool showCone = false,
        TargetingAnalysis targeting = null, ThreatAnalysis threat = null)
    {
        const int samples = 24;
        var pts = new Vector2[samples + 1];
        float endHeading = f.Heading;
        for (int i = 0; i <= samples; i++)
            f.RoutePoint(f.PlannedManeuver, turn, f.PlannedPathDistance, i / (float)samples, out pts[i], out endHeading);
        Color pathColor = f.PlannedManeuver switch
        {
            ManeuverType.UTurn => new Color(0.62f, 0.48f, 1f, 0.85f),
            ManeuverType.BreakTurn => new Color(1f, 0.62f, 0.3f, 0.88f),
            ManeuverType.EngineBoost => new Color(0.3f, 1f, 0.75f, 0.88f),
            ManeuverType.RotatingGuns => new Color(1f, 0.78f, 0.32f, 0.92f),
            ManeuverType.EmergencyThrusters => new Color(1f, 0.36f, 0.3f, 0.92f),
            ManeuverType.PursuitBurn => new Color(0.4f, 1f, 0.7f, 0.92f),
            ManeuverType.EcmJink => new Color(0.45f, 0.7f, 1f, 0.92f),
            ManeuverType.EvasiveDodge => new Color(0.35f, 0.9f, 1f, 0.92f),
            ManeuverType.EvasiveSpin => new Color(0.6f, 0.95f, 1f, 0.92f),
            ManeuverType.RearGuns => new Color(0.95f, 0.55f, 1f, 0.92f),
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
            DrawPolyline(pts, new Color(pathColor, pathColor.A * alpha / 0.8f), W(2.5f), true);

        Vector2 end = pts[samples];
        if (showCone && Fighter.GunsFireDuring(f.PlannedManeuver))
            DrawFireCone(end, Fighter.GunHeadingFor(f.PlannedManeuver, endHeading), f.EffectiveFireRange, f.EffectiveFireConeDeg);
        Vector2 labelAt = end + new Vector2(0f, -Mathf.Max(24f, f.VisualRadius + 4f) - Px(16f));
        if (fatalAsteroidPath)
            DrawLabel(labelAt, "FATAL COLLISION", SignalUi.FontMicro, new Color(1f, 0.35f, 0.3f, alpha));
        else if (scrapeDamage > 0)
            DrawLabel(labelAt, $"SCRAPE -{scrapeDamage} HULL", SignalUi.FontMicro, new Color(1f, 0.72f, 0.28f, alpha));
        else if (threat != null)
            DrawThreatReadout(labelAt, threat, alpha);

        Texture2D tex = f.BaseTexture;
        DrawSetTransform(end, endHeading + Mathf.Pi / 2f, Vector2.One * f.ArtScale);
        DrawTexture(tex, -tex.GetSize() / 2f, new Color(1, 1, 1, alpha * 0.6f));
        DrawSetTransform(Vector2.Zero);

        // The ghost is the drag handle; give it a finger-sized ring so it
        // reads as grabbable even when the camera is pulled back.
        float handle = Mathf.Max(24f, Px(26f));
        DrawCircle(end, handle, new Color(pathColor, 0.08f * alpha));
        DrawArc(end, handle, 0f, Mathf.Tau, 36, new Color(pathColor, 0.7f * alpha), W(2f), true);
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
            DrawLine(points[i], points[i + 1], new Color(color, 0.13f + risk * 0.3f), W(7f), true);

            if (risk < 0.12f)
                continue;
            Vector2 segment = points[i + 1] - points[i];
            if (segment.LengthSquared() <= 0.01f)
                continue;
            Vector2 mid = points[i].Lerp(points[i + 1], 0.5f);
            Vector2 normal = segment.Normalized().Rotated(Mathf.Pi / 2f) * 4f;
            DrawLine(mid - normal * Mathf.Max(1f, _s), mid + normal * Mathf.Max(1f, _s), color, W(1.5f), true);
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
        DrawLabel(end, $"THREAT {status} · {contacts}", SignalUi.FontMicro, color);
    }

    static Color ThreatColor(float risk) => ThreatLow.Lerp(ThreatHigh, Mathf.Clamp(risk / 0.35f, 0f, 1f));

    void DrawTargetedPath(Vector2[] points, Color maneuverColor, float alpha, TargetingAnalysis targeting)
    {
        float baseAlpha = Mathf.Min(0.34f, maneuverColor.A * alpha / 0.8f * 0.55f);
        DrawPolyline(points, new Color(maneuverColor, baseAlpha), W(1.6f), true);

        for (int i = 0; i < TargetingTimeSamples; i++)
        {
            float coverage = Mathf.Max(targeting.Coverage[i], targeting.Coverage[i + 1]);
            if (coverage >= StrongCoverageThreshold)
            {
                DrawLine(points[i], points[i + 1], StrongSolution, W(3.5f), true);
            }
            else if (coverage > 0f)
            {
                Vector2 dashEnd = points[i].Lerp(points[i + 1], 0.58f);
                DrawLine(points[i], dashEnd, PossibleSolution, W(3f), true);
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
        if (!Fighter.GunsFireDuring(shooter.PlannedManeuver))
            return;
        foreach (float progress in TimeSliceProgress)
        {
            mgr.PredictExecutionPoint(shooter, shooter.PlannedManeuver, shooter.PlannedTurnAngleRadians ?? 0f,
                shooter.PlannedPathDistance, progress, out Vector2 position, out float heading);
            DrawFireConeOutline(position, Fighter.GunHeadingFor(shooter.PlannedManeuver, heading),
                shooter.EffectiveFireRange, shooter.EffectiveFireConeDeg);
        }
    }

    void DrawFireConeOutline(Vector2 tip, float heading, float range, float coneDeg)
    {
        float cone = Mathf.DegToRad(coneDeg);
        float left = heading - cone;
        float right = heading + cone;
        DrawLine(tip, tip + Vector2.FromAngle(left) * range, TimeSliceCone, W(1f), true);
        DrawLine(tip, tip + Vector2.FromAngle(right) * range, TimeSliceCone, W(1f), true);
        DrawArc(tip, range, left, right, 10, TimeSliceCone, W(1f), true);
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
        // Silent guns have no firing window at all.
        if (!Fighter.GunsFireDuring(shooter.PlannedManeuver))
            return result;
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
            float shooterHeading = Fighter.GunHeadingFor(shooter.PlannedManeuver, shooterPath.Headings[i]);

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
                for (int plan = 0; plan < source.Paths.Count; plan++)
                {
                    PredictedPath enemyPath = source.Paths[plan];
                    ManeuverType enemyManeuver = source.Plans[plan].Maneuver;
                    if (!Fighter.GunsFireDuring(enemyManeuver))
                        continue;
                    Vector2 enemyPosition = enemyPath.Positions[i];
                    float enemyHeading = Fighter.GunHeadingFor(enemyManeuver, enemyPath.Headings[i]);
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
        float ring = Mathf.Max(27f, target.VisualRadius + 7f);
        Color color = analysis.BestCoverage >= StrongCoverageThreshold
            ? StrongSolution
            : analysis.BestCoverage > 0f ? PossibleSolution : NoSolution;

        if (analysis.BestCoverage >= StrongCoverageThreshold)
            DrawArc(center, ring, 0f, Mathf.Tau, 32, color, W(2.5f), true);
        else
            DrawDashedArc(center, ring, color);

        float bracketX = ring + 4f;
        const float bracketOuterY = 14f;
        const float bracketInnerY = 7f;
        const float bracketArm = 7f;
        DrawLine(center + new Vector2(-bracketX, -bracketOuterY), center + new Vector2(-bracketX, -bracketInnerY), color, W(2.5f), true);
        DrawLine(center + new Vector2(-bracketX, -bracketOuterY), center + new Vector2(-bracketX + bracketArm, -bracketOuterY), color, W(2.5f), true);
        DrawLine(center + new Vector2(-bracketX, bracketOuterY), center + new Vector2(-bracketX, bracketInnerY), color, W(2.5f), true);
        DrawLine(center + new Vector2(-bracketX, bracketOuterY), center + new Vector2(-bracketX + bracketArm, bracketOuterY), color, W(2.5f), true);
        DrawLine(center + new Vector2(bracketX, -bracketOuterY), center + new Vector2(bracketX, -bracketInnerY), color, W(2.5f), true);
        DrawLine(center + new Vector2(bracketX, -bracketOuterY), center + new Vector2(bracketX - bracketArm, -bracketOuterY), color, W(2.5f), true);
        DrawLine(center + new Vector2(bracketX, bracketOuterY), center + new Vector2(bracketX, bracketInnerY), color, W(2.5f), true);
        DrawLine(center + new Vector2(bracketX, bracketOuterY), center + new Vector2(bracketX - bracketArm, bracketOuterY), color, W(2.5f), true);

        if (pinned)
            DrawCircle(center + new Vector2(0f, -ring - 4f), Px(4f), color);

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

        int size = FontPx(SignalUi.FontMicro);
        DrawString(SignalUi.Display, center + new Vector2(bracketX + 6f + Px(4f), size * 0.35f), status,
            HorizontalAlignment.Left, -1f, size, color);
    }

    void DrawDashedArc(Vector2 center, float radius, Color color)
    {
        const int dashes = 8;
        float dashWidth = Mathf.Tau / dashes * 0.56f;
        for (int i = 0; i < dashes; i++)
        {
            float start = i * Mathf.Tau / dashes;
            DrawArc(center, radius, start, start + dashWidth, 4, color, W(2.5f), true);
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
            yield return new TargetPlan(ManeuverType.UTurn, -Mathf.Pi, target.Moves.UTurnMoveDistance);
            yield return new TargetPlan(ManeuverType.UTurn, Mathf.Pi, target.Moves.UTurnMoveDistance);
        }
        if (target.HasAbility(ShipAbility.BreakTurn) && target.IsManeuverReady(ManeuverType.BreakTurn))
        {
            yield return new TargetPlan(ManeuverType.BreakTurn, -Mathf.Pi, target.Moves.BreakTurnMoveDistance);
            yield return new TargetPlan(ManeuverType.BreakTurn, Mathf.Pi, target.Moves.BreakTurnMoveDistance);
        }
        if (target.HasAbility(ShipAbility.RotatingGuns) && target.IsManeuverReady(ManeuverType.RotatingGuns))
            yield return new TargetPlan(ManeuverType.RotatingGuns, 0f, target.Moves.RotatingGunsMoveDistance);

        foreach (float turn in SampleTurns(target.EngineBoostTurnLimitDegrees))
            if (target.HasAbility(ShipAbility.EngineBoost) && target.IsManeuverReady(ManeuverType.EngineBoost))
                yield return new TargetPlan(ManeuverType.EngineBoost, turn, target.Moves.EngineBoostMoveDistance);
        foreach (float turn in SampleTurns(target.EmergencyThrustersTurnLimitDegrees))
            if (target.HasAbility(ShipAbility.EmergencyThrusters) && target.IsManeuverReady(ManeuverType.EmergencyThrusters))
                yield return new TargetPlan(ManeuverType.EmergencyThrusters, turn, target.Moves.EmergencyThrustersMoveDistance);
        foreach (float turn in SampleTurns(target.PursuitBurnTurnLimitDegrees))
            if (target.HasAbility(ShipAbility.PursuitBurn) && target.IsManeuverReady(ManeuverType.PursuitBurn))
                yield return new TargetPlan(ManeuverType.PursuitBurn, turn, target.Moves.PursuitBurnMoveDistance);
        foreach (float turn in SampleTurns(target.EcmJinkTurnLimitDegrees))
            if (target.HasAbility(ShipAbility.EcmJink) && target.IsManeuverReady(ManeuverType.EcmJink))
                yield return new TargetPlan(ManeuverType.EcmJink, turn, target.Moves.EcmJinkMoveDistance);
        if (target.HasAbility(ShipAbility.EvasiveDodge) && target.IsManeuverReady(ManeuverType.EvasiveDodge))
        {
            float turn = Mathf.DegToRad(target.Moves.EvasiveDodgeAngleDegrees);
            yield return new TargetPlan(ManeuverType.EvasiveDodge, -turn, target.Moves.EvasiveDodgeMoveDistance);
            yield return new TargetPlan(ManeuverType.EvasiveDodge, turn, target.Moves.EvasiveDodgeMoveDistance);
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
        DrawLine(tip, poly[1], ConeEdge, W(1f), true);
        DrawLine(tip, poly[samples + 1], ConeEdge, W(1f), true);
        DrawArc(tip, range, heading - cone, heading + cone, samples + 1, ConeEdge, W(1f), true);
    }

    /// <summary>
    /// Shield and hull bars under each ship, drawn at a constant screen size
    /// so they stay legible when the camera pulls back. Player ships also
    /// carry a callsign above them, and a suppressed or nebula-slowed ship of
    /// either side says so under its bars.
    /// </summary>
    void DrawHpBar(Fighter f, Color color)
    {
        if (!f.IsAlive || _labelAlpha <= 0f)
            return;
        float a = _labelAlpha;
        float width = Px(44f);
        float shieldHeight = Px(4f);
        float hullHeight = Px(6f);
        Vector2 shieldTopLeft = f.Position + new Vector2(-width / 2f, Mathf.Max(22f, f.VisualRadius + 4f) + Px(2f));
        DrawRect(new Rect2(shieldTopLeft, new Vector2(width, shieldHeight)), new Color(0.2f, 0.5f, 1f, 0.2f * a));
        float shieldFraction = f.MaxShield > 0 ? f.Shield / (float)f.MaxShield : 0f;
        DrawRect(new Rect2(shieldTopLeft, new Vector2(width * shieldFraction, shieldHeight)), new Color(0.35f, 0.7f, 1f, a));

        Vector2 hullTopLeft = shieldTopLeft + new Vector2(0f, shieldHeight + Px(2f));
        DrawRect(new Rect2(hullTopLeft, new Vector2(width, hullHeight)), new Color(1, 1, 1, 0.13f * a));
        float hullFraction = Mathf.Clamp(f.Hp / (float)f.MaxHp, 0f, 1f);
        DrawRect(new Rect2(hullTopLeft, new Vector2(width * hullFraction, hullHeight)), new Color(color, color.A * a));

        if (f.Team == 0)
            DrawLabel(f.Position + new Vector2(0f, -Mathf.Max(26f, f.VisualRadius + 6f) - Px(12f)), BattleManager.CallsignOf(f), SignalUi.FontMicro,
                new Color(SignalUi.Body.R, SignalUi.Body.G, SignalUi.Body.B, 0.85f * a));

        // Status tags under the bars: Suppression Fire slows a ship's turning
        // until it gets out of the fire; nebula gas shortens this turn's move.
        Vector2 tagAt = hullTopLeft + new Vector2(width / 2f, hullHeight + Px(12f));
        if (f.IsSuppressed)
        {
            DrawLabel(tagAt, $"SUPPRESSED −{f.NormalTurnLimitPenaltyDegrees:0}°", SignalUi.FontMicro, new Color(SignalUi.Warning, a));
            tagAt.Y += Px(16f);
        }
        if (f.InNebula && BattleManager.Instance?.CurrentPhase == BattleManager.Phase.Planning)
            DrawLabel(tagAt, $"NEBULA −{(1f - f.RouteScale) * 100:0}% MOVE", SignalUi.FontMicro, new Color(NebulaTag, a));
    }
}

/// <summary>
/// A trait's name shown over the ship it just helped (or hurt), so the player
/// can see an instinct or scar make a difference. Each ship's callouts stack
/// into a short feed above its callsign. Drawn at a constant on-screen size
/// and timed in real seconds, so fast-forward doesn't cut it short.
/// </summary>
public partial class TraitCallout : Node2D
{
    public static readonly Color InstinctColor = SignalUi.Instinct;
    public static readonly Color ScarColor = SignalUi.Warning;
    const float Life = 1.6f;
    const int MaxRows = 3;
    /// <summary>Screen pixels from the top of the hull art to the lowest callout: clear of the callsign label.</summary>
    const float BaseOffset = 40f;
    const float RowHeight = 26f;

    public Fighter Anchor;
    public string Text = "";
    public Color Color = InstinctColor;
    /// <summary>Place in the ship's feed; 0 is the newest, just above the callsign.</summary>
    public int Row;

    double _born = -1;
    float _shownRow;

    /// <summary>Seconds since the callout appeared.</summary>
    public float Age => _born < 0 ? 0f : (float)(Time.GetTicksMsec() / 1000.0 - _born);

    /// <summary>Moves this callout up the feed for a newer one. Callouts that fired together start apart.</summary>
    public void PushUp()
    {
        Row++;
        if (Age < 0.1f)
            _shownRow = Row;
    }

    public override void _Ready()
    {
        _born = Time.GetTicksMsec() / 1000.0;
        ZIndex = 20;
    }

    public override void _Process(double delta)
    {
        if (Age >= Life || Row >= MaxRows)
        {
            QueueFree();
            return;
        }
        _shownRow = Mathf.MoveToward(_shownRow, Row, (float)delta * 8f);
        if (IsInstanceValid(Anchor))
            Position = Anchor.Position;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float s = BattleManager.Instance?.ScreenToWorldScale ?? 1f;
        float t = Age / Life;
        float alpha = (t < 0.75f ? 1f : 1f - (t - 0.75f) / 0.25f) * (1f - _shownRow * 0.2f);
        float pop = Mathf.Clamp(Age / 0.12f, 0f, 1f); // a short rise into place
        Font font = SignalUi.Display;
        int size = Mathf.Max(1, Mathf.RoundToInt(SignalUi.FontCaption * s));
        Vector2 extent = font.GetStringSize(Text, HorizontalAlignment.Left, -1f, size);
        float hullTop = IsInstanceValid(Anchor) ? Mathf.Max(26f, Anchor.VisualRadius + 6f) : 26f;
        var baseline = new Vector2(-extent.X / 2f, -hullTop - (BaseOffset + _shownRow * RowHeight + 10f * pop) * s);
        // Keep the text on screen when the ship flies near the edge of the view.
        Transform2D toWorld = GetViewport().GetCanvasTransform().AffineInverse();
        float margin = SignalUi.ScreenGutter / 2f;
        float left = (toWorld * new Vector2(margin, 0f)).X - Position.X;
        float right = (toWorld * new Vector2(GetViewportRect().Size.X - margin, 0f)).X - Position.X - extent.X;
        baseline.X = Mathf.Clamp(baseline.X, left, Mathf.Max(left, right));
        DrawString(font, baseline + new Vector2(2f, 2f) * s, Text, HorizontalAlignment.Left, -1f, size, new Color(0f, 0f, 0f, 0.75f * alpha));
        DrawString(font, baseline, Text, HorizontalAlignment.Left, -1f, size, new Color(Color, alpha));
    }
}
