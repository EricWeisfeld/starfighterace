using Godot;

/// <summary>A non-combatant dreadnought used by escort operations.</summary>
public partial class ObjectiveShip : Fighter
{
    public bool Escaped { get; private set; }
    public Vector2 Destination { get; private set; }
    public float DestinationRadius { get; private set; }
    public float DistanceRemaining => Mathf.Max(0f, Position.DistanceTo(Destination) - DestinationRadius);
    public float EscapeProgress { get; private set; }
    float _initialDistanceRemaining;

    /// <summary>The transport's art is already large; it grows only a little.</summary>
    public override float ArtScale => 1.15f;

    /// <summary>Configure the transport with the normal ship movement and visual systems.</summary>
    public void Setup(Vector2 position, Vector2 destination, float destinationRadius)
    {
        Destination = destination;
        DestinationRadius = destinationRadius;
        _initialDistanceRemaining = Mathf.Max(0f, position.DistanceTo(destination) - destinationRadius);
        EscapeProgress = 0f;
        ApplyType(ShipTypes.CivilianDreadnought);
        CanFire = false;
        Setup(0, position, (destination - position).Angle(), Type.GetSkin(0));
    }

    /// <summary>
    /// Prepare the transport's next leg toward the exit, bending only as much
    /// as necessary to preserve a safe asteroid clearance.
    /// </summary>
    public void PlanEscapeMove(BattleManager battle)
    {
        ClearPlannedManeuver();
        const float preferredClearance = 12f;
        const int turnSamples = 70;
        float distance = Position.DistanceTo(Destination);
        if (distance <= DestinationRadius)
        {
            Escaped = true;
            return;
        }

        float moveDistance = Mathf.Min(NormalMoveMaxDistance,
            Mathf.Max(NormalMoveMinDistance, distance - DestinationRadius));
        float maxTurn = Mathf.DegToRad(GetNormalTurnLimitDegrees(moveDistance));
        float bestSafeScore = float.MinValue;
        float bestSafeTurn = 0f;
        float bestEmergencyClearance = float.NegativeInfinity;
        float bestEmergencyTurn = 0f;

        for (int i = 0; i <= turnSamples; i++)
        {
            float turn = Mathf.Lerp(-maxTurn, maxTurn, i / (float)turnSamples);
            float clearance = battle.PathAsteroidClearance(this, ManeuverType.Normal, turn, moveDistance);

            RoutePoint(ManeuverType.Normal, turn, moveDistance, 1f, out Vector2 end, out _);

            // Make progress toward the authored destination while choosing a
            // safe arc around terrain instead of merely flying east.
            float score = -end.DistanceTo(Destination) - Mathf.Abs(turn) * 12f;
            if (clearance >= preferredClearance && score > bestSafeScore)
            {
                bestSafeScore = score;
                bestSafeTurn = turn;
            }
            if (clearance > bestEmergencyClearance)
            {
                bestEmergencyClearance = clearance;
                bestEmergencyTurn = turn;
            }
        }

        PlannedTurnAngleRadians = bestSafeScore > float.MinValue ? bestSafeTurn : bestEmergencyTurn;
        SetPlannedMoveDistance(moveDistance);
    }

    /// <summary>Complete the objective only when the actual movement path enters the jump zone.</summary>
    public void UpdateDestinationProgress(Vector2 previousPosition)
    {
        if (!IsAlive || Escaped) return;
        EscapeProgress = _initialDistanceRemaining <= 0f
            ? 1f
            : Mathf.Clamp(1f - DistanceRemaining / _initialDistanceRemaining, 0f, 1f);
        if (DistanceToSegment(Destination, previousPosition, Position) <= DestinationRadius)
        {
            Escaped = true;
            EscapeProgress = 1f;
        }
        QueueRedraw();
    }

    static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float lengthSquared = segment.LengthSquared();
        if (lengthSquared < 0.0001f)
            return point.DistanceTo(from);
        float progress = Mathf.Clamp((point - from).Dot(segment) / lengthSquared, 0f, 1f);
        return point.DistanceTo(from + segment * progress);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        QueueRedraw();
    }

    public override void _Draw()
    {
        // The node turns with its heading; keep the readout upright and at a
        // constant on-screen size (the distance itself lives in the HUD).
        float s = BattleManager.Instance?.ScreenToWorldScale ?? 1f;
        DrawSetTransform(Vector2.Zero, -Rotation, Vector2.One);
        Color hull = IsAlive ? new Color(0.72f, 0.9f, 1f) : new Color(0.35f, 0.12f, 0.12f);
        float width = Mathf.Max(64f, 72f * s);
        float barY = -Mathf.Max(40f, VisualRadius + 4f) - 8f * s;
        DrawRect(new Rect2(-width / 2f, barY, width, 7f * s), new Color(0.02f, 0.04f, 0.08f, 0.9f));
        DrawRect(new Rect2(-width / 2f, barY, width * Mathf.Clamp(Hp / (float)MaxHp, 0f, 1f), 7f * s), new Color(0.4f, 1f, 0.7f));
        string label = Escaped ? "JUMP ZONE REACHED" : "TRANSPORT";
        int size = Mathf.Max(1, Mathf.RoundToInt(SignalUi.FontMicro * s));
        Vector2 extent = SignalUi.Display.GetStringSize(label, HorizontalAlignment.Left, -1f, size);
        DrawString(SignalUi.Display, new Vector2(-extent.X / 2f, barY - 6f * s), label, HorizontalAlignment.Left, -1f, size, hull);
        DrawSetTransform(Vector2.Zero);
    }
}
