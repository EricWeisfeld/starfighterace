using System;

public enum MissionObjective { EliminateHostiles, DestroyTarget, EscortShip }

/// <summary>
/// Mission-owned target preferences used by enemy maneuver and weapon AI.
/// Future objective types can supply a different profile without changing the
/// general flight planner.
/// </summary>
public sealed class MissionAITuning
{
    public float PlayerShipPriority = 1f;
    public float EscortShipPriority = 1f;
    public float ObjectiveFocusChance = 1f;
    public float DistanceBias = 300f;

    public float PriorityFor(Fighter target) => target is ObjectiveShip
        ? EscortShipPriority
        : PlayerShipPriority;

    public static MissionAITuning ForObjective(MissionObjective objective) => objective switch
    {
        // The transport is the primary mission target, but nearby fighters can
        // still become more attractive because distance remains part of the score.
        MissionObjective.EscortShip => new MissionAITuning
        {
            EscortShipPriority = 2f,
            ObjectiveFocusChance = 0.72f,
        },
        _ => new MissionAITuning(),
    };
}

/// <summary>Everything a battle needs to know about the fight it is staging.</summary>
public class BattleMission
{
    public string Name = "";
    public string Briefing = "";
    public int Threat = 1;
    public MissionObjective Objective;
    public string MapId = "shard-run";
    public ShipType[] EnemySquad = Array.Empty<ShipType>();
    public float EnemyStatMultiplier = 1f;
    public MissionAITuning EnemyAITuning = new();

    public string ObjectiveLabel => Objective switch
    {
        MissionObjective.DestroyTarget => "DESTROY THE MARKED TARGET",
        MissionObjective.EscortShip => "ESCORT THE TRANSPORT",
        _ => "ELIMINATE ALL HOSTILES",
    };
}
