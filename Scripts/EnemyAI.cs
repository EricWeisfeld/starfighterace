using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Plans enemy trajectories. A combat route is only considered after its full
/// movement arc has been checked against terrain; attacking position is a
/// secondary concern to keeping the ship clear of asteroids.
/// </summary>
public static class EnemyAI
{
    // Clearance is measured from the outside of the ship collision envelope.
    // It gives the live movement simulation room to remain visibly clear of a
    // rock, rather than merely avoiding its fatal core.
    const float PreferredAsteroidClearance = 12f;
    const int NormalDistanceSamples = 16;
    const int NormalTurnSamples = 72;
    const int AbilityTurnSamples = 48;

    readonly struct FlightPlan
    {
        public readonly ManeuverType Maneuver;
        public readonly float Turn;
        public readonly float Distance;

        public FlightPlan(ManeuverType maneuver, float turn, float distance)
        {
            Maneuver = maneuver;
            Turn = turn;
            Distance = distance;
        }
    }

    sealed class PlanChoices
    {
        public bool HasSafe;
        public FlightPlan Safe;
        public float SafeScore = float.MinValue;
        public bool HasSurvivable;
        public FlightPlan Survivable;
        public float SurvivableScore = float.MinValue;
        public bool HasEmergency;
        public FlightPlan Emergency;
        public float EmergencyClearance = float.NegativeInfinity;
        public float EmergencyScore = float.MinValue;
    }

    /// <summary>
    /// Chooses a target using the mission's role weights and proximity. A
    /// higher-priority objective usually wins, while a fighter that closes the
    /// distance can still draw an attack.
    /// </summary>
    public static Fighter SelectTarget(Fighter self, IEnumerable<Fighter> foes, MissionAITuning tuning)
    {
        tuning ??= new MissionAITuning();
        List<Fighter> liveTargets = foes
            .Where(f => f != null && f.IsAlive && tuning.PriorityFor(f) > 0f)
            .ToList();
        bool hasObjective = liveTargets.Any(f => f is ObjectiveShip);
        bool hasFighter = liveTargets.Any(f => f is not ObjectiveShip);
        if (hasObjective && hasFighter && GD.Randf() > Mathf.Clamp(tuning.ObjectiveFocusChance, 0f, 1f))
            liveTargets.RemoveAll(f => f is ObjectiveShip);

        return liveTargets
            .OrderByDescending(f => TargetScore(self, f, tuning))
            .ThenBy(f => f.Position.DistanceSquaredTo(self.Position))
            .FirstOrDefault();
    }

    static float TargetScore(Fighter self, Fighter target, MissionAITuning tuning)
    {
        float distance = self.Position.DistanceTo(target.Position);
        float bias = Mathf.Max(1f, tuning.DistanceBias);
        return tuning.PriorityFor(target) * bias / (bias + distance);
    }

    public static void Plan(Fighter self, IReadOnlyList<Fighter> foes, IReadOnlyList<Fighter> allies,
        MissionAITuning tuning = null)
    {
        Fighter target = SelectTarget(self, foes, tuning);
        if (target == null)
        {
            self.PlannedManeuver = ManeuverType.Normal;
            self.PlannedTurnAngleRadians = 0f;
            return;
        }

        // Predict the maneuver the target actually has queued, rather than
        // always treating a tight turn or ability move as a straight line.
        target.RoutePoint(target.PlannedManeuver, target.PlannedTurnAngleRadians ?? 0f, target.PlannedPathDistance, 1f,
            out Vector2 targetEnd, out _);

        var arena = new Rect2(30, 30, BattleManager.ArenaW - 60, BattleManager.ArenaH - 60);
        var choices = new PlanChoices();
        // Enemy pilots weigh the maneuvers their sector allows them alongside
        // normal flight, to turn onto a target or slip out of its cone.
        IEnumerable<FlightPlan> candidates = NormalCandidates(self);
        if (self.Pilot == null)
            candidates = candidates.Concat(EscapeCandidates(self));
        ScoreCandidates(self, targetEnd, allies, arena, candidates, choices);

        // A normal arc may be unable to clear a narrow pocket. In that case,
        // consider the hull's special maneuvers before accepting even a
        // survivable scrape.
        if (!choices.HasSafe && self.Pilot != null)
            ScoreCandidates(self, targetEnd, allies, arena, EscapeCandidates(self), choices);

        if (choices.HasSafe)
            ApplyPlan(self, choices.Safe);
        else if (choices.HasSurvivable)
            ApplyPlan(self, choices.Survivable);
        else if (choices.HasEmergency)
            ApplyPlan(self, choices.Emergency);
    }

    static IEnumerable<FlightPlan> NormalCandidates(Fighter self)
    {
        for (int distanceIndex = 0; distanceIndex <= NormalDistanceSamples; distanceIndex++)
        {
            float distance = Mathf.Lerp(self.NormalMoveMinDistance, self.NormalMoveMaxDistance,
                distanceIndex / (float)NormalDistanceSamples);
            float maxTurn = Mathf.DegToRad(self.GetNormalTurnLimitDegrees(distance));
            for (int turnIndex = 0; turnIndex <= NormalTurnSamples; turnIndex++)
            {
                float turn = Mathf.Lerp(-maxTurn, maxTurn, turnIndex / (float)NormalTurnSamples);
                yield return new FlightPlan(ManeuverType.Normal, turn, distance);
            }
        }
    }

    static IEnumerable<FlightPlan> EscapeCandidates(Fighter self)
    {
        if (self.HasAbility(ShipAbility.UTurn) && self.IsManeuverReady(ManeuverType.UTurn))
        {
            yield return new FlightPlan(ManeuverType.UTurn, -Mathf.Pi, self.Moves.UTurnMoveDistance);
            yield return new FlightPlan(ManeuverType.UTurn, Mathf.Pi, self.Moves.UTurnMoveDistance);
        }

        if (self.HasAbility(ShipAbility.BreakTurn) && self.IsManeuverReady(ManeuverType.BreakTurn))
        {
            yield return new FlightPlan(ManeuverType.BreakTurn, -Mathf.Pi, self.Moves.BreakTurnMoveDistance);
            yield return new FlightPlan(ManeuverType.BreakTurn, Mathf.Pi, self.Moves.BreakTurnMoveDistance);
        }


        if (self.HasAbility(ShipAbility.SnapTurn) && self.IsManeuverReady(ManeuverType.SnapTurn))
        {
            float snapTurn = Mathf.DegToRad(self.Moves.SnapTurnAngleDegrees);
            yield return new FlightPlan(ManeuverType.SnapTurn, -snapTurn, self.Moves.SnapTurnMoveDistance);
            yield return new FlightPlan(ManeuverType.SnapTurn, snapTurn, self.Moves.SnapTurnMoveDistance);
        }

        if (self.HasAbility(ShipAbility.RotatingGuns) && self.IsManeuverReady(ManeuverType.RotatingGuns))
            yield return new FlightPlan(ManeuverType.RotatingGuns, 0f, self.Moves.RotatingGunsMoveDistance);

        if (self.HasAbility(ShipAbility.EngineBoost) && self.IsManeuverReady(ManeuverType.EngineBoost))
        {
            foreach (float turn in TurnsFor(self.EngineBoostTurnLimitDegrees))
                yield return new FlightPlan(ManeuverType.EngineBoost, turn, self.Moves.EngineBoostMoveDistance);
        }

        if (self.HasAbility(ShipAbility.EmergencyThrusters) && self.IsManeuverReady(ManeuverType.EmergencyThrusters))
        {
            foreach (float turn in TurnsFor(self.EmergencyThrustersTurnLimitDegrees))
                yield return new FlightPlan(ManeuverType.EmergencyThrusters, turn, self.Moves.EmergencyThrustersMoveDistance);
        }

        if (self.HasAbility(ShipAbility.PursuitBurn) && self.IsManeuverReady(ManeuverType.PursuitBurn))
        {
            foreach (float turn in TurnsFor(self.PursuitBurnTurnLimitDegrees))
                yield return new FlightPlan(ManeuverType.PursuitBurn, turn, self.Moves.PursuitBurnMoveDistance);
        }

        if (self.HasAbility(ShipAbility.EcmJink) && self.IsManeuverReady(ManeuverType.EcmJink))
        {
            foreach (float turn in TurnsFor(self.EcmJinkTurnLimitDegrees))
                yield return new FlightPlan(ManeuverType.EcmJink, turn, self.Moves.EcmJinkMoveDistance);
        }

        if (self.HasAbility(ShipAbility.GhostRun) && self.IsManeuverReady(ManeuverType.GhostRun))
        {
            foreach (float turn in TurnsFor(self.GhostRunTurnLimitDegrees))
                yield return new FlightPlan(ManeuverType.GhostRun, turn, self.Moves.GhostRunMoveDistance);
        }

        if (self.HasAbility(ShipAbility.EvasiveDodge) && self.IsManeuverReady(ManeuverType.EvasiveDodge))
        {
            float dodgeTurn = Mathf.DegToRad(self.Moves.EvasiveDodgeAngleDegrees);
            yield return new FlightPlan(ManeuverType.EvasiveDodge, -dodgeTurn, self.Moves.EvasiveDodgeMoveDistance);
            yield return new FlightPlan(ManeuverType.EvasiveDodge, dodgeTurn, self.Moves.EvasiveDodgeMoveDistance);
        }
    }

    static IEnumerable<float> TurnsFor(float maxTurnDegrees)
    {
        float maxTurn = Mathf.DegToRad(maxTurnDegrees);
        for (int i = 0; i <= AbilityTurnSamples; i++)
            yield return Mathf.Lerp(-maxTurn, maxTurn, i / (float)AbilityTurnSamples);
    }

    static void ScoreCandidates(Fighter self, Vector2 targetEnd, IReadOnlyList<Fighter> allies, Rect2 arena,
        IEnumerable<FlightPlan> candidates, PlanChoices choices)
    {
        BattleManager battle = BattleManager.Instance;
        foreach (FlightPlan candidate in candidates)
        {
            self.RoutePoint(candidate.Maneuver, candidate.Turn, candidate.Distance, 1f, out Vector2 end, out float endHeading);
            float dist = end.DistanceTo(targetEnd);
            float bearing = Mathf.Abs(Mathf.Wrap((targetEnd - end).Angle() - endHeading, -Mathf.Pi, Mathf.Pi));

            float clearance = float.PositiveInfinity;
            float terrainPenalty = 0f;
            if (battle != null)
            {
                clearance = battle.PathAsteroidClearance(self, candidate.Maneuver, candidate.Turn, candidate.Distance);
                if (clearance > -BattleManager.ShipCollisionRadius)
                {
                    int scrapeDamage = battle.PathScrapeDamage(self, candidate.Maneuver, candidate.Turn, candidate.Distance);
                    int remainingDurability = self.Hp + self.Shield;
                    terrainPenalty = scrapeDamage >= remainingDurability
                        ? 10000f
                        : scrapeDamage * 0.7f + scrapeDamage / (float)remainingDurability * 120f;
                }
                else
                {
                    terrainPenalty = 10000f;
                }
                // Ending in gas means a shorter move next turn.
                if (battle.IsInNebula(end))
                    terrainPenalty += 25f;
            }

            float score = -Mathf.RadToDeg(bearing) * 1.5f
                          - Mathf.Abs(dist - 170f) * 0.3f
                          - terrainPenalty;
            if (!arena.HasPoint(end))
                score -= 250f;
            foreach (Fighter ally in allies)
            {
                if (ally == self || !ally.IsAlive || !ally.PlannedTurnAngleRadians.HasValue)
                    continue;
                ally.RoutePoint(ally.PlannedManeuver, ally.PlannedTurnAngleRadians.Value, ally.PlannedPathDistance, 1f,
                    out Vector2 allyEnd, out _);
                if (allyEnd.DistanceTo(end) < 50f)
                    score -= 100f;
            }
            score += (float)GD.RandRange(0.0, 6.0);

            if (!choices.HasEmergency || clearance > choices.EmergencyClearance ||
                (Mathf.IsEqualApprox(clearance, choices.EmergencyClearance) && score > choices.EmergencyScore))
            {
                choices.HasEmergency = true;
                choices.Emergency = candidate;
                choices.EmergencyClearance = clearance;
                choices.EmergencyScore = score;
            }

            if (clearance <= -BattleManager.ShipCollisionRadius || terrainPenalty >= 10000f)
                continue;

            if (!choices.HasSurvivable || score > choices.SurvivableScore)
            {
                choices.HasSurvivable = true;
                choices.Survivable = candidate;
                choices.SurvivableScore = score;
            }

            if (clearance >= PreferredAsteroidClearance && (!choices.HasSafe || score > choices.SafeScore))
            {
                choices.HasSafe = true;
                choices.Safe = candidate;
                choices.SafeScore = score;
            }
        }
    }

    static void ApplyPlan(Fighter self, FlightPlan plan)
    {
        self.PlannedManeuver = plan.Maneuver;
        self.PlannedTurnAngleRadians = plan.Turn;
        if (plan.Maneuver == ManeuverType.Normal)
            self.SetPlannedMoveDistance(plan.Distance);
        else
            self.PlannedPathDistance = plan.Distance;
    }
}
