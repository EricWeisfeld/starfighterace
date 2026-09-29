using Godot;
using System;

/// <summary>Editable authored operation. Add .tres files under Resources/StoryMissions.</summary>
[GlobalClass]
public partial class StoryMissionResource : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string SystemName { get; set; } = "";
    [Export] public string PlanetId { get; set; } = "";
    [Export] public string PlanetName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Briefing { get; set; } = "";
    [Export] public string PlanetTexture { get; set; } = "";
    [Export] public Vector2 MapPosition { get; set; }
    [Export] public int Threat { get; set; } = 1;
    [Export] public int Credits { get; set; } = 150;
    [Export] public MissionObjective Objective { get; set; } = MissionObjective.EliminateHostiles;
    [Export] public string MapId { get; set; } = "shard-run";
    [Export] public string[] EnemyShipIds { get; set; } = Array.Empty<string>();
    [Export] public string[] PrerequisiteIds { get; set; } = Array.Empty<string>();

    public CampaignMission CreateMission()
    {
        // Authored ships remain the story wing's core. At higher difficulties,
        // deterministic same-faction reinforcements fill out the rolled formation.
        // The stable seed means this does not reshuffle every time the board opens.
        int enemyDifficulty = EncounterDifficulty.CombatLevel(Threat, Objective);
        EnemyEncounter encounter = EncounterDifficulty.Roll(enemyDifficulty,
            new RandomNumberGenerator { Seed = EncounterDifficulty.StableSeed(Id) });
        ShipType[] authoredWing = Array.ConvertAll(EnemyShipIds, ShipTypes.FromId);
        ShipType[] enemies = BuildEncounterWing(authoredWing, encounter.ShipCount,
            new RandomNumberGenerator { Seed = EncounterDifficulty.StableSeed($"{Id}-wing") });
        return new CampaignMission
        {
            Id = Id, SystemName = SystemName, PlanetId = PlanetId, PlanetName = PlanetName, Briefing = Briefing,
            Threat = Threat, Credits = Credits, Source = MissionSource.Story, Objective = Objective,
            MapId = Objective == MissionObjective.EscortShip ? BattleMaps.EscortCorridorId : MapId,
            EnemySquad = enemies, EnemyStatMultiplier = encounter.StatMultiplier, EnemyDifficulty = enemyDifficulty,
            EnemyAITuning = MissionAITuning.ForObjective(Objective),
        };
    }

    static ShipType[] BuildEncounterWing(ShipType[] authoredWing, int count, RandomNumberGenerator rng)
    {
        if (authoredWing.Length == 0) authoredWing = new[] { ShipTypes.Scout };
        var wing = new ShipType[count];
        for (int i = 0; i < count; i++)
            wing[i] = i < authoredWing.Length ? authoredWing[i] : authoredWing[rng.RandiRange(0, authoredWing.Length - 1)];
        return wing;
    }
}
