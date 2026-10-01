using System;

/// <summary>
/// Everything a battle needs to know about the fight it is staging. Every
/// battle is won by destroying every enemy ship, reinforcements included.
/// </summary>
public class BattleMission
{
    public string Name = "";
    public string Briefing = "";
    public int Threat = 1;
    public string MapId = "shard-run";
    /// <summary>The enemy wing at the start. Enemies fly at their frames' base numbers.</summary>
    public ShipType[] EnemySquad = Array.Empty<ShipType>();
    /// <summary>Ships that join on <see cref="ReinforcementTurn"/>, or at once if the first group is wiped out sooner.</summary>
    public ShipType[] Reinforcements = Array.Empty<ShipType>();
    public int ReinforcementTurn = 3;
    /// <summary>How many of its line's maneuvers each enemy can fly, in pool order. Never more than a pilot can know.</summary>
    public int EnemyManeuvers = Pilot.MaxManeuvers;
    /// <summary>
    /// Ace pilots see your orders before they move. Everyone else plans
    /// against your ships' visible course. Players are only told they face
    /// aces, not why aces are better.
    /// </summary>
    public bool EnemiesReadOrders;

    /// <summary>"+2 ON TURN 3", or empty when nobody follows.</summary>
    public string ReinforcementLabel => Reinforcements.Length == 0 ? ""
        : $"+{Reinforcements.Length} ON TURN {ReinforcementTurn}";
}
