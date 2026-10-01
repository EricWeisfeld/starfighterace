using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// How an enemy pilot fights. Every enemy flies one, and you can read it over
/// the ship while planning.
/// </summary>
public enum EnemyTactic
{
    /// <summary>The plain charge: point at the nearest foe and close. Only the test autopilot flies it.</summary>
    None,
    /// <summary>Goes straight for its target and will take an even trade of fire.</summary>
    Striker,
    /// <summary>Swings wide while closing, comes in from the side, and keeps out of guns.</summary>
    Flanker,
    /// <summary>Hangs back near the edge of gun range and won't sit in front of anyone.</summary>
    Sniper,
    /// <summary>An ace: hunts your weakest ship from out of its guns.</summary>
    Ace,
}

/// <summary>
/// Plans enemy trajectories. A combat route is only considered after its full
/// movement arc has been checked against terrain; attacking position is a
/// secondary concern to keeping the ship clear of asteroids.
///
/// Enemy pilots weigh each route by when, along the move, their guns cover a
/// foe and a foe's guns cover them, against the course they expect each of
/// your ships to fly. Their <see cref="EnemyTactic"/> sets how much each
/// counts, where they like to end up, and which of your ships they go after.
/// </summary>
public static class EnemyAI
{
    /// <summary>What a tactic values. Weights are in route-score points.</summary>
    readonly struct TacticProfile
    {
        /// <summary>Distance to its target it likes to end a move at.</summary>
        public float Range { get; init; }
        /// <summary>Per unit a move ends away from that distance.</summary>
        public float RangeWeight { get; init; }
        /// <summary>Per degree its nose ends off where it's heading for.</summary>
        public float Bearing { get; init; }
        /// <summary>Per whole move a foe sits in this ship's guns.</summary>
        public float Offense { get; init; }
        /// <summary>Per whole move this ship sits in a foe's guns (scaled by the foe's firepower).</summary>
        public float Exposure { get; init; }
        /// <summary>Per degree off its target's nose the ship ends up.</summary>
        public float Tail { get; init; }
        /// <summary>Target choice: extra distance counted per ally already on a ship.</summary>
        public float Spread { get; init; }
        /// <summary>Target choice: distance discounted for a fully worn-down ship.</summary>
        public float Weakness { get; init; }
        /// <summary>Swings out to its side while still far from its target.</summary>
        public bool Wide { get; init; }
        /// <summary>Backs off anyone who would end a move close by.</summary>
        public bool Kite { get; init; }
    }

    static TacticProfile ProfileFor(EnemyTactic tactic) => tactic switch
    {
        EnemyTactic.Flanker => new()
        {
            Range = 190f, RangeWeight = 0.3f, Bearing = 1.0f, Offense = 110f, Exposure = 170f, Tail = 0.8f,
            Spread = 160f, Weakness = 80f, Wide = true,
        },
        EnemyTactic.Sniper => new()
        {
            Range = 250f, RangeWeight = 0.8f, Bearing = 0.6f, Offense = 140f, Exposure = 180f, Tail = 0.4f,
            Spread = 80f, Weakness = 100f, Kite = true,
        },
        EnemyTactic.Ace => new()
        {
            Range = 180f, RangeWeight = 0.3f, Bearing = 1.2f, Offense = 160f, Exposure = 160f, Tail = 0.8f,
            Spread = 0f, Weakness = 260f,
        },
        _ => new()
        {
            Range = 170f, RangeWeight = 0.3f, Bearing = 1.2f, Offense = 120f, Exposure = 70f, Tail = 0.35f,
            Spread = 140f, Weakness = 120f,
        },
    };

    /// <summary>A flanker swings this far to its side of the target while closing.</summary>
    const float FlankOffset = 420f;
    /// <summary>A flanker turns in once it is this close to its target, if the target isn't facing it.</summary>
    const float FlankCommitRange = 400f;
    /// <summary>A target whose nose is within this of a flanker is facing it: the flanker holds off to the side.</summary>
    const float FlankFacedDegrees = 70f;
    /// <summary>While holding off, a flanker keeps at least this far from its target.</summary>
    const float FlankHoldDistance = 320f;
    /// <summary>
    /// A sniper backs off a foe that would end a move this close; one that
    /// starts a turn this close has caught it, and it turns to fight as a striker.
    /// </summary>
    const float SniperCrowdRange = 180f;
    /// <summary>Below this share of hull a pilot fights cautiously and weighs exposure more.</summary>
    const float CautiousHullFraction = 0.35f;
    const float CautiousExposureMultiplier = 1.6f;
    /// <summary>
    /// Each turn in a row a pilot goes without firing takes this share off
    /// how much it minds your guns, down to <see cref="BoldestExposureMultiplier"/>:
    /// a pilot can't dodge forever, sooner or later it commits.
    /// </summary>
    const float ImpatiencePerIdleTurn = 0.3f;
    const float BoldestExposureMultiplier = 0.25f;
    /// <summary>
    /// A wing outnumbered two to one is cornered: its pilots mind your guns
    /// this much less and press in, rather than drag the fight out.
    /// </summary>
    const float CorneredExposureMultiplier = 0.4f;
    /// <summary>A flanker that has gone this many turns without firing stops swinging wide.</summary>
    const int FlankerPatienceTurns = 2;
    /// <summary>Points along a move where guns are checked, as fractions of the move.</summary>
    static readonly float[] GunSamples = { 0.2f, 0.4f, 0.6f, 0.8f, 1f };

    /// <summary>
    /// Gives a newly spawned enemy its tactic. Aces fly as aces. Otherwise each
    /// class line alternates between two tactics, so a wing of one line still
    /// mixes them: Kestrels flank first, Raptors strike first, ZTs snipe
    /// first. Flankers alternate sides.
    /// </summary>
    public static void AssignTactic(Fighter self, IEnumerable<Fighter> wing)
    {
        var others = wing.Where(f => f != self).ToList();
        if (self.IsAce)
        {
            self.Tactic = EnemyTactic.Ace;
            return;
        }
        string line = ShipTypes.ClassIdForHull(self.Type.Id);
        EnemyTactic[] cycle = line switch
        {
            "raptor" => new[] { EnemyTactic.Striker, EnemyTactic.Flanker },
            "zt" => new[] { EnemyTactic.Sniper, EnemyTactic.Striker },
            _ => new[] { EnemyTactic.Flanker, EnemyTactic.Striker },
        };
        int sameLine = others.Count(f => !f.IsAce && ShipTypes.ClassIdForHull(f.Type.Id) == line);
        self.Tactic = cycle[sameLine % cycle.Length];
        self.FlankSide = others.Count(f => f.Tactic == EnemyTactic.Flanker) % 2 == 0 ? 1 : -1;
    }

    /// <summary>The label shown over an enemy flying this tactic.</summary>
    public static string TacticLabel(EnemyTactic tactic) => tactic switch
    {
        EnemyTactic.Striker => "STRIKER",
        EnemyTactic.Flanker => "FLANKER",
        EnemyTactic.Sniper => "SNIPER",
        _ => "",
    };

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

    /// <summary>Chooses the nearest live foe.</summary>
    public static Fighter SelectTarget(Fighter self, IEnumerable<Fighter> foes) =>
        foes.Where(f => f != null && f.IsAlive)
            .OrderBy(f => f.Position.DistanceSquaredTo(self.Position))
            .FirstOrDefault();

    /// <summary>
    /// Plans one ship's move against its chosen target. Most enemy pilots
    /// can't see your orders: they lead your ship along its visible course,
    /// straight on at the throttle it started the turn with. Pilots who
    /// <paramref name="readOrders"/> lead the move you actually queued.
    /// </summary>
    public static void Plan(Fighter self, IReadOnlyList<Fighter> foes, IReadOnlyList<Fighter> allies, bool readOrders = true)
    {
        bool tactical = self.Tactic != EnemyTactic.None;
        bool caught = self.Tactic == EnemyTactic.Sniper &&
                      foes.Any(f => f != null && f.IsAlive && f.Position.DistanceTo(self.Position) < SniperCrowdRange);
        TacticProfile profile = ProfileFor(caught ? EnemyTactic.Striker : self.Tactic);
        self.AiIdleTurns = self.ShotsFired > self.AiShotsSeen ? 0 : self.AiIdleTurns + 1;
        self.AiShotsSeen = self.ShotsFired;
        Fighter target = tactical ? ChooseTarget(self, foes, allies, profile) : SelectTarget(self, foes);
        if (target == null)
        {
            self.PlannedManeuver = ManeuverType.Normal;
            self.PlannedTurnAngleRadians = 0f;
            return;
        }

        // Tactical pilots who can't read orders expect each of your ships to
        // turn toward whichever of them is nearest it.
        var tracks = foes.Where(f => f != null && f.IsAlive)
            .Select(f => FoeTrack.Expect(f, readOrders, tactical ? allies : null)).ToList();
        FoeTrack targetTrack = tracks.First(t => t.Ship == target);
        var context = new PlanContext
        {
            Self = self,
            Target = targetTrack,
            Foes = tracks,
            Allies = allies,
            Profile = profile,
            Tactical = tactical,
            Arena = new Rect2(30, 30, BattleManager.ArenaW - 60, BattleManager.ArenaH - 60),
        };
        if (tactical)
            context.SettleTurn(allies.Count(a => a.IsAlive) * 2 <= tracks.Count);
        var choices = new PlanChoices();
        // Enemy pilots weigh the maneuvers their sector allows them alongside
        // normal flight, to turn onto a target or slip out of its cone.
        IEnumerable<FlightPlan> candidates = NormalCandidates(self);
        if (self.Pilot == null)
            candidates = candidates.Concat(EscapeCandidates(self));
        ScoreCandidates(context, candidates, choices);

        // A normal arc may be unable to clear a narrow pocket. In that case,
        // consider the hull's special maneuvers before accepting even a
        // survivable scrape.
        if (!choices.HasSafe && self.Pilot != null)
            ScoreCandidates(context, EscapeCandidates(self), choices);

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

    static void ScoreCandidates(PlanContext context, IEnumerable<FlightPlan> candidates, PlanChoices choices)
    {
        BattleManager battle = BattleManager.Instance;
        Fighter self = context.Self;
        IReadOnlyList<Fighter> allies = context.Allies;
        Rect2 arena = context.Arena;
        Vector2 targetEnd = context.Target.EndPosition;
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

            float score = context.Tactical
                ? TacticalScore(context, candidate, end, endHeading) - terrainPenalty
                : -Mathf.RadToDeg(bearing) * 1.5f
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

    /// <summary>
    /// The tactic's view of a route: where it leaves the ship against its
    /// target, and whose guns cover whom along the way.
    /// </summary>
    static float TacticalScore(PlanContext context, FlightPlan candidate, Vector2 end, float endHeading)
    {
        Fighter self = context.Self;
        TacticProfile p = context.Profile;
        FoeTrack target = context.Target;

        float bearingDeg = Mathf.RadToDeg(Mathf.Abs(Mathf.Wrap((context.Aim - end).Angle() - endHeading, -Mathf.Pi, Mathf.Pi)));
        float offNoseDeg = Mathf.RadToDeg(Mathf.Abs(Mathf.Wrap((end - target.EndPosition).Angle() - target.EndHeading, -Mathf.Pi, Mathf.Pi)));
        float score = -bearingDeg * p.Bearing
                      + offNoseDeg * p.Tail * context.Boldness
                      - Mathf.Abs(end.DistanceTo(context.Aim) - context.AimRange) * context.AimRangeWeight;
        if (context.Holding && end.DistanceTo(target.EndPosition) < FlankHoldDistance)
            score -= (FlankHoldDistance - end.DistanceTo(target.EndPosition)) * 0.8f;

        // Guns along the move: the time a foe sits in this ship's guns, and the
        // time this ship sits in a foe's, worth more the harder that foe hits.
        bool gunsLive = !self.GunsSilentDuring(candidate.Maneuver);
        float cone = Mathf.DegToRad(candidate.Maneuver == ManeuverType.RotatingGuns ? self.Moves.RotatingGunsFireConeDeg : self.BaseFireConeDeg);
        float offense = 0f, exposure = 0f;
        for (int i = 0; i < GunSamples.Length; i++)
        {
            self.RoutePoint(candidate.Maneuver, candidate.Turn, candidate.Distance, GunSamples[i], out Vector2 pos, out float heading);
            float gunHeading = Fighter.GunHeadingFor(candidate.Maneuver, heading);
            float best = 0f;
            foreach (FoeTrack foe in context.Foes)
            {
                FoeTrack.Sample at = foe.Samples[i];
                if (gunsLive && Covers(pos, gunHeading, cone, self.EffectiveFireRange, at.Position))
                    best = Mathf.Max(best, foe == target ? 1f : 0.6f);
                if (!at.GunsLive)
                    continue;
                if (Covers(at.Position, at.GunHeading, at.Cone, foe.Ship.EffectiveFireRange, pos))
                    exposure += foe.Threat;
                else if (Covers(at.Position, at.StraightGunHeading, at.Cone, foe.Ship.EffectiveFireRange, pos))
                    exposure += foe.Threat * 0.5f;
            }
            offense += best;
        }
        offense /= GunSamples.Length;
        exposure /= GunSamples.Length;
        float caution = (self.Hp < self.MaxHp * CautiousHullFraction ? CautiousExposureMultiplier : 1f) * context.Boldness;
        score += offense * p.Offense - exposure * p.Exposure * caution;

        // A sniper backs off anyone who would end up close.
        if (p.Kite)
        {
            foreach (FoeTrack foe in context.Foes)
            {
                float d = end.DistanceTo(foe.EndPosition);
                if (d < SniperCrowdRange)
                    score -= (SniperCrowdRange - d) * 1.5f * context.Boldness;
            }
        }
        return score;
    }

    static bool Covers(Vector2 from, float gunHeading, float cone, float range, Vector2 to) =>
        from.DistanceTo(to) <= range &&
        Mathf.Abs(Mathf.Wrap((to - from).Angle() - gunHeading, -Mathf.Pi, Mathf.Pi)) <= cone;

    /// <summary>
    /// Picks a target for a tactical pilot: the nearest, pulled toward worn-down
    /// ships, pushed off ships allies are already on, and held from last turn.
    /// </summary>
    static Fighter ChooseTarget(Fighter self, IReadOnlyList<Fighter> foes, IReadOnlyList<Fighter> allies, TacticProfile p)
    {
        Fighter best = null;
        float bestCost = float.PositiveInfinity;
        foreach (Fighter foe in foes)
        {
            if (foe == null || !foe.IsAlive)
                continue;
            float worn = 1f - (foe.Hp + foe.Shield) / (float)Mathf.Max(1, foe.MaxHp + foe.MaxShield);
            int onIt = allies.Count(a => a != self && a.IsAlive && a.AiTarget == foe);
            float cost = self.Position.DistanceTo(foe.Position)
                         - worn * p.Weakness
                         + onIt * p.Spread
                         - (foe == self.AiTarget ? TargetStickiness : 0f);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = foe;
            }
        }
        self.AiTarget = best;
        return best;
    }

    /// <summary>How much nearer last turn's target counts, so a pilot doesn't dither between two.</summary>
    const float TargetStickiness = 120f;

    sealed class PlanContext
    {
        public Fighter Self;
        public FoeTrack Target;
        public List<FoeTrack> Foes;
        public IReadOnlyList<Fighter> Allies;
        public TacticProfile Profile;
        public bool Tactical;
        public Rect2 Arena;

        /// <summary>Where the pilot steers for this turn, and how far from it it likes to end.</summary>
        public Vector2 Aim;
        public float AimRange, AimRangeWeight;
        /// <summary>A flanker holding off to its target's side.</summary>
        public bool Holding;
        /// <summary>
        /// How much the pilot still minds your guns, and still angles for your
        /// tail: less after turns without a shot, and less again when its wing
        /// is cornered.
        /// </summary>
        public float Boldness = 1f;

        /// <summary>Settles the turn's aim and mood before any route is scored.</summary>
        public void SettleTurn(bool cornered)
        {
            Boldness = Mathf.Max(BoldestExposureMultiplier, 1f - ImpatiencePerIdleTurn * Mathf.Max(0, Self.AiIdleTurns - 1))
                       * (cornered ? CorneredExposureMultiplier : 1f);
            Aim = Target.EndPosition;
            AimRange = Profile.Range;
            AimRangeWeight = Profile.RangeWeight;

            // A flanker swings out to its side while far off, and holds there
            // while its target faces it; it turns in once the target looks
            // away, or once it runs out of patience.
            Fighter target = Target.Ship;
            float faced = Mathf.RadToDeg(Mathf.Abs(Mathf.Wrap((Self.Position - target.Position).Angle() - target.Heading, -Mathf.Pi, Mathf.Pi)));
            Holding = Profile.Wide && Self.AiIdleTurns < FlankerPatienceTurns && !cornered &&
                      (Self.Position.DistanceTo(Target.EndPosition) > FlankCommitRange || faced < FlankFacedDegrees);
            if (!Holding)
                return;
            // Swing to whichever side of the target's course the flanker is
            // already on; dead ahead, it takes its assigned side.
            Vector2 beam = Vector2.FromAngle(Target.EndHeading + Mathf.Pi / 2f);
            float lateral = (Self.Position - Target.EndPosition).Dot(beam);
            float side = Mathf.Abs(lateral) > 100f ? Mathf.Sign(lateral) : Self.FlankSide;
            Aim = Target.EndPosition + beam * side * FlankOffset;
            AimRange = 0f;
            AimRangeWeight = 0.3f;
        }
    }

    /// <summary>
    /// Where a pilot expects a foe to be over the coming move. Pilots who read
    /// orders see the real move. The rest see the visible course, and tactical
    /// pilots also expect the foe to turn toward the nearest of their wing; a
    /// foe holding its course still covers the straight-ahead line, which
    /// counts half.
    /// </summary>
    sealed class FoeTrack
    {
        public readonly struct Sample
        {
            public Vector2 Position { get; init; }
            public float GunHeading { get; init; }
            /// <summary>Where its guns point if it holds its visible course.</summary>
            public float StraightGunHeading { get; init; }
            public float Cone { get; init; }
            public bool GunsLive { get; init; }
        }

        public Fighter Ship;
        public Vector2 EndPosition;
        public float EndHeading;
        public Sample[] Samples;
        /// <summary>How hard this foe hits, about 1 for a typical frame.</summary>
        public float Threat;

        public static FoeTrack Expect(Fighter foe, bool readOrders, IReadOnlyList<Fighter> anticipate)
        {
            ManeuverType maneuver = readOrders ? foe.PlannedManeuver : ManeuverType.Normal;
            float turn = readOrders ? foe.PlannedTurnAngleRadians ?? 0f : 0f;
            float distance = readOrders ? foe.PlannedPathDistance : foe.TurnStartPathDistance;
            if (!readOrders && anticipate != null &&
                anticipate.Where(f => f.IsAlive).OrderBy(f => f.Position.DistanceSquaredTo(foe.Position)).FirstOrDefault() is { } nearest)
            {
                float maxTurn = Mathf.DegToRad(foe.GetNormalTurnLimitDegrees(distance));
                turn = Mathf.Clamp(Mathf.Wrap((nearest.Position - foe.Position).Angle() - foe.Heading, -Mathf.Pi, Mathf.Pi),
                    -maxTurn, maxTurn);
            }
            bool gunsLive = !foe.GunsSilentDuring(maneuver);
            float cone = Mathf.DegToRad(maneuver == ManeuverType.RotatingGuns ? foe.Moves.RotatingGunsFireConeDeg : foe.BaseFireConeDeg);
            var track = new FoeTrack
            {
                Ship = foe,
                Samples = new Sample[GunSamples.Length],
                Threat = foe.ShotDamage * foe.Accuracy / 40f,
            };
            for (int i = 0; i < GunSamples.Length; i++)
            {
                foe.RoutePoint(maneuver, turn, distance, GunSamples[i], out Vector2 pos, out float heading);
                foe.RoutePoint(maneuver, readOrders ? turn : 0f, distance, GunSamples[i], out _, out float straightHeading);
                track.Samples[i] = new Sample
                {
                    Position = pos,
                    GunHeading = Fighter.GunHeadingFor(maneuver, heading),
                    StraightGunHeading = Fighter.GunHeadingFor(maneuver, straightHeading),
                    Cone = cone,
                    GunsLive = gunsLive,
                };
            }
            foe.RoutePoint(maneuver, turn, distance, 1f, out track.EndPosition, out track.EndHeading);
            return track;
        }
    }

    static void ApplyPlan(Fighter self, FlightPlan plan)
    {
        self.PlannedManeuver = plan.Maneuver;
        self.PlannedTurnAngleRadians = plan.Turn;
        if (Fighter.FliesLikeNormal(plan.Maneuver))
            self.SetPlannedMoveDistance(plan.Distance);
        else
            self.PlannedPathDistance = plan.Distance;
    }
}
