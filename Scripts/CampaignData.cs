using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum PlanetControl { Alliance, Contested, Enemy }
public enum SystemControl { Alliance, Contested, Enemy }
public enum MissionSource { Story, Planetary }
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

/// <summary>Runtime contract shared by authored planetary operations and system stories.</summary>
public class CampaignMission
{
    public string Id;
    public string SystemId;
    public string SystemName;
    public string PlanetId;
    public string PlanetName;
    public string Briefing;
    public int Threat;
    public int Credits;
    public MissionSource Source;
    public MissionObjective Objective;
    public string MapId;
    public ShipType[] EnemySquad;
    public float EnemyStatMultiplier = 1f;
    public int EnemyDifficulty;
    public MissionAITuning EnemyAITuning = new();
    public bool IsStory => Source == MissionSource.Story;
    public bool IsPlanetary => Source == MissionSource.Planetary;
    public int EffectiveEnemyDifficulty => EnemyDifficulty > 0 ? EnemyDifficulty : Threat;
    public string ObjectiveLabel => Objective switch
    {
        MissionObjective.DestroyTarget => "DESTROY PRIORITY TARGET",
        MissionObjective.EscortShip => "ESCORT CIVILIAN SHIP",
        _ => "ELIMINATE HOSTILES",
    };
}

/// <summary>One map-visible world and its current strategic owner.</summary>
public class CampaignPlanet
{
    public string Id;
    public string SystemName;
    public string Name;
    public string TexturePath;
    public Vector2 MapPosition;
    public PlanetControl Control;
    public int Threat;
}

/// <summary>Runtime system data assembled from one <see cref="CampaignSystemResource"/>.</summary>
public class CampaignSystemDefinition
{
    public string Id;
    public string Name;
    public Vector2 MapPosition;
    public CampaignMission StoryMission;
    public readonly List<CampaignMission> PlanetaryMissions = new();
}

public class ControlChange
{
    public string PlanetName;
    public PlanetControl Before;
    public PlanetControl After;
}

/// <summary>Strategic consequences returned to the debrief presentation.</summary>
public class CampaignResolution
{
    public CampaignMission Mission;
    public readonly List<ControlChange> ControlChanges = new();
    public string CapturedSystemName;
    public int CreditsAwarded;
}

/// <summary>Persistent strategic state. Tactical pilots remain owned by PilotRoster.</summary>
public static class CampaignData
{
    const string SavePath = "user://ace-star-pilot-campaign.cfg";
    const int SaveVersion = 4;
    public const int SystemCaptureCredits = 350;

    // Each system owns its five planetary operations and one story finale.
    // Future systems can be added here without changing campaign progression.
    static readonly string[] SystemPaths =
    {
        "res://Resources/CampaignSystems/orion_spur.tres",
    };

    static readonly List<CampaignPlanet> _planets = new();
    static readonly List<CampaignSystemDefinition> _systems = new();
    static readonly Dictionary<string, CampaignMission> _planetaryMissions = new();
    static readonly HashSet<string> _completedPlanetaryMissions = new();
    static readonly HashSet<string> _completedStories = new();
    static readonly HashSet<string> _capturedSystems = new();
    static readonly HashSet<string> _usedRecruitmentSlots = new();
    static bool _initialized;

    public static CampaignMission SelectedMission { get; private set; }
    public static string SelectedSystem { get; private set; }
    public static int Credits { get; private set; }
    public static int Day { get; private set; }
    public static int LogisticsLevel { get; private set; }
    public static int OperationsLevel { get; private set; }
    public static int SquadronLevel { get; private set; }
    public static IReadOnlyList<CampaignSystemDefinition> Systems { get { Initialize(); return _systems; } }
    public static IReadOnlyList<CampaignPlanet> Planets { get { Initialize(); return _planets; } }
    public static CampaignMission NextStoryMission
    {
        get
        {
            Initialize();
            return _systems.Select(system => GetStoryMissionForSystem(system.Name)).FirstOrDefault(mission => mission != null);
        }
    }

    public static bool HasSave()
    {
        var save = new ConfigFile();
        return save.Load(SavePath) == Error.Ok;
    }

    public static void StartCampaign()
    {
        Initialize();
        SelectedMission = null;
    }

    public static void StartNewCampaign()
    {
        string absolutePath = ProjectSettings.GlobalizePath(SavePath);
        if (FileAccess.FileExists(absolutePath)) DirAccess.RemoveAbsolute(absolutePath);
        _initialized = false;
        _planets.Clear();
        _systems.Clear();
        _planetaryMissions.Clear();
        _completedPlanetaryMissions.Clear();
        _completedStories.Clear();
        _capturedSystems.Clear();
        _usedRecruitmentSlots.Clear();
        SelectedMission = null;
        SelectedSystem = null;
        Credits = 250;
        Day = 1;
        LogisticsLevel = OperationsLevel = SquadronLevel = 0;
        PilotRoster.ResetCampaign();
        Initialize();
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        LoadDefinitions();
        LoadState();
    }

    static void LoadDefinitions()
    {
        foreach (string path in SystemPaths)
        {
            CampaignSystemResource resource = ResourceLoader.Load<CampaignSystemResource>(path);
            if (resource == null)
            {
                GD.PushError($"Missing campaign system resource: {path}");
                continue;
            }
            var system = new CampaignSystemDefinition
            {
                Id = resource.Id,
                Name = resource.DisplayName,
                MapPosition = resource.MapPosition,
            };
            foreach (string missionPath in resource.PlanetMissionPaths)
            {
                MissionDefinitionResource missionResource = ResourceLoader.Load<MissionDefinitionResource>(missionPath);
                if (missionResource == null)
                {
                    GD.PushError($"Missing planetary mission resource: {missionPath}");
                    continue;
                }
                CampaignMission mission = missionResource.CreateMission(system.Id, system.Name);
                system.PlanetaryMissions.Add(mission);
                _planetaryMissions[mission.PlanetId] = mission;
                _planets.Add(new CampaignPlanet
                {
                    Id = mission.PlanetId, SystemName = system.Name, Name = mission.PlanetName,
                    TexturePath = missionResource.PlanetTexture, MapPosition = missionResource.MapPosition,
                    Control = PlanetControl.Contested, Threat = mission.Threat,
                });
            }
            MissionDefinitionResource storyResource = ResourceLoader.Load<MissionDefinitionResource>(resource.StoryMissionPath);
            if (storyResource == null)
                GD.PushError($"Missing system story mission resource: {resource.StoryMissionPath}");
            else
                system.StoryMission = storyResource.CreateMission(system.Id, system.Name);
            _systems.Add(system);
        }
    }

    static void LoadState()
    {
        var save = new ConfigFile();
        if (save.Load(SavePath) != Error.Ok)
        {
            Credits = 250;
            Day = 1;
            LogisticsLevel = OperationsLevel = SquadronLevel = 0;
            Save();
            return;
        }

        int version = (int)save.GetValue("campaign", "save_version", 1);
        if (version < SaveVersion)
        {
            Credits = 250;
            Day = 1;
            LogisticsLevel = OperationsLevel = SquadronLevel = 0;
            foreach (CampaignPlanet planet in _planets) planet.Control = PlanetControl.Contested;
            PilotRoster.ResetCampaign();
            Save();
            return;
        }
        Credits = (int)save.GetValue("campaign", "credits", 250);
        Day = (int)save.GetValue("campaign", "day", 1);
        LogisticsLevel = (int)save.GetValue("flagship", "logistics", 0);
        OperationsLevel = (int)save.GetValue("flagship", "operations", 0);
        SquadronLevel = (int)save.GetValue("flagship", "squadron", 0);
        foreach (CampaignPlanet planet in _planets)
        {
            int stored = (int)save.GetValue("planet", planet.Id, (int)PlanetControl.Contested);
            planet.Control = version switch
            {
                1 => stored switch { (int)PlanetControl.Alliance => PlanetControl.Alliance, (int)PlanetControl.Enemy => PlanetControl.Enemy, _ => PlanetControl.Contested },
                2 => stored > 0 ? PlanetControl.Alliance : stored < 0 ? PlanetControl.Enemy : PlanetControl.Contested,
                _ => (PlanetControl)Mathf.Clamp(stored, (int)PlanetControl.Alliance, (int)PlanetControl.Enemy),
            };
        }
        LoadSet(save, "planetary_mission", "completed", _completedPlanetaryMissions);
        LoadSet(save, "story", "completed", _completedStories);
        LoadSet(save, "system", "captured", _capturedSystems);
        LoadSet(save, "recruitment", "used_slots", _usedRecruitmentSlots);
        PilotRoster.LoadFrom(save);
    }

    static void LoadSet(ConfigFile save, string section, string key, HashSet<string> set)
    {
        string value = (string)save.GetValue(section, key, "");
        foreach (string id in value.Split(',', StringSplitOptions.RemoveEmptyEntries)) set.Add(id);
    }

    static void Save()
    {
        var save = new ConfigFile();
        save.SetValue("campaign", "save_version", SaveVersion);
        save.SetValue("campaign", "credits", Credits);
        save.SetValue("campaign", "day", Day);
        save.SetValue("flagship", "logistics", LogisticsLevel);
        save.SetValue("flagship", "operations", OperationsLevel);
        save.SetValue("flagship", "squadron", SquadronLevel);
        foreach (CampaignPlanet planet in _planets) save.SetValue("planet", planet.Id, (int)planet.Control);
        save.SetValue("planetary_mission", "completed", string.Join(',', _completedPlanetaryMissions));
        save.SetValue("story", "completed", string.Join(',', _completedStories));
        save.SetValue("system", "captured", string.Join(',', _capturedSystems));
        save.SetValue("recruitment", "used_slots", string.Join(',', _usedRecruitmentSlots));
        PilotRoster.SaveTo(save);
        save.Save(SavePath);
    }

    /// <summary>Persists campaign-owned state after roster or hangar changes outside mission resolution.</summary>
    public static void SaveCampaign()
    {
        Initialize();
        Save();
    }

    public static IEnumerable<CampaignPlanet> PlanetsInSystem(string systemName) => Planets.Where(p => p.SystemName == systemName);
    public static CampaignPlanet GetPlanet(string id) => Planets.FirstOrDefault(p => p.Id == id);
    public static CampaignSystemDefinition GetSystem(string systemName) => Systems.FirstOrDefault(system => system.Name == systemName);
    public static SystemControl GetSystemControl(string systemName)
    {
        if (IsSystemStoryCompleted(systemName)) return SystemControl.Alliance;
        if (IsSystemStoryUnlocked(systemName)) return SystemControl.Contested;
        CampaignPlanet[] planets = PlanetsInSystem(systemName).ToArray();
        int alliance = planets.Count(p => p.Control == PlanetControl.Alliance);
        int enemy = planets.Count(p => p.Control == PlanetControl.Enemy);
        return alliance > enemy ? SystemControl.Alliance : enemy > alliance ? SystemControl.Enemy : SystemControl.Contested;
    }
    public static bool IsSystemCaptured(string systemName) => _capturedSystems.Contains(systemName);
    public static int CompletedPlanetaryMissions(string systemName) => GetSystem(systemName)?.PlanetaryMissions.Count(mission => _completedPlanetaryMissions.Contains(mission.Id)) ?? 0;
    public static int PlanetaryMissionCount(string systemName) => GetSystem(systemName)?.PlanetaryMissions.Count ?? 0;
    public static bool IsSystemStoryUnlocked(string systemName)
    {
        CampaignSystemDefinition system = GetSystem(systemName);
        return system != null && system.PlanetaryMissions.Count > 0 && system.PlanetaryMissions.All(mission => _completedPlanetaryMissions.Contains(mission.Id));
    }
    public static bool IsSystemStoryCompleted(string systemName)
    {
        CampaignSystemDefinition system = GetSystem(systemName);
        return system?.StoryMission != null && _completedStories.Contains(system.StoryMission.Id);
    }
    public static bool IsSystemSecured(string systemName) => IsSystemStoryCompleted(systemName);
    public static int ActiveOperationsForSystem(string systemName) => PlanetaryMissionCount(systemName) - CompletedPlanetaryMissions(systemName);

    /// <summary>Returns the planet's authored operation until it has been won.</summary>
    public static List<CampaignMission> GetMissionsForPlanet(string planetId)
    {
        Initialize();
        if (!_planetaryMissions.TryGetValue(planetId, out CampaignMission mission) || _completedPlanetaryMissions.Contains(mission.Id))
            return new List<CampaignMission>();
        return new List<CampaignMission> { mission };
    }

    /// <summary>The system finale appears only after every planetary operation is won.</summary>
    public static CampaignMission GetStoryMissionForSystem(string systemName)
    {
        CampaignSystemDefinition system = GetSystem(systemName);
        if (system?.StoryMission == null || !IsSystemStoryUnlocked(systemName) || _completedStories.Contains(system.StoryMission.Id))
            return null;
        return system.StoryMission;
    }

    public static string StoryGateStatus => StoryGateStatusFor(SelectedSystem ?? Systems.FirstOrDefault()?.Name);
    public static string StoryGateStatusFor(string systemName)
    {
        CampaignSystemDefinition system = GetSystem(systemName);
        if (system == null) return "SYSTEM DATA UNAVAILABLE";
        if (IsSystemStoryCompleted(systemName)) return "SYSTEM STORY COMPLETE";
        int remaining = PlanetaryMissionCount(systemName) - CompletedPlanetaryMissions(systemName);
        return remaining > 0
            ? $"{remaining} PLANETARY OPERATION{(remaining == 1 ? "" : "S")} REMAIN BEFORE THE STORY OPERATION"
            : "STORY OPERATION AVAILABLE";
    }

    public static void Select(CampaignMission mission) => SelectedMission = mission;
    public static void SelectSystem(string systemName) => SelectedSystem = systemName;

    public static CampaignResolution ResolveSelectedMission(bool success)
    {
        if (SelectedMission == null) return null;
        CampaignMission mission = SelectedMission;
        CampaignPlanet planet = GetPlanet(mission.PlanetId);
        var result = new CampaignResolution { Mission = mission };
        Day++;

        if (planet != null)
        {
            PlanetControl before = planet.Control;
            planet.Control = success ? PlanetControl.Alliance : PlanetControl.Enemy;
            if (before != planet.Control)
                result.ControlChanges.Add(new ControlChange { PlanetName = planet.Name, Before = before, After = planet.Control });
        }

        if (mission.IsStory)
        {
            if (success)
            {
                _completedStories.Add(mission.Id);
                result.CreditsAwarded = AwardCredits(mission.Credits);
            }
            // Failed story missions remain available until won.
        }
        else
        {
            if (success)
            {
                _completedPlanetaryMissions.Add(mission.Id);
                result.CreditsAwarded = AwardCredits(mission.Credits);
            }
        }

        if (mission.IsStory && success && IsSystemSecured(mission.SystemName) && _capturedSystems.Add(mission.SystemName))
        {
            result.CapturedSystemName = mission.SystemName;
            Credits += SystemCaptureCredits;
            result.CreditsAwarded += SystemCaptureCredits;
        }

        SelectedMission = null;
        RefreshRecruitmentOffers();
        Save();
        return result;
    }

    static int AwardCredits(int baseCredits)
    {
        int payout = baseCredits + LogisticsLevel * 15;
        Credits += payout;
        return payout;
    }

    public static int UpgradeCost(int currentLevel) => 150 + currentLevel * 150;

    /// <summary>Recruitment costs 100 credits for each offered pilot level.</summary>
    public static int RecruitmentCost(int level) => Mathf.Max(1, level) * 100;

    public static bool RecruitmentSlotAvailable(string shipClassId)
    {
        Initialize();
        return !string.IsNullOrEmpty(shipClassId) && !_usedRecruitmentSlots.Contains(shipClassId);
    }

    public static void UseRecruitmentSlot(string shipClassId)
    {
        Initialize();
        if (!string.IsNullOrEmpty(shipClassId))
            _usedRecruitmentSlots.Add(shipClassId);
    }

    public static void RefreshRecruitmentOffers()
    {
        _usedRecruitmentSlots.Clear();
    }

    public static bool TrySpendCredits(int amount)
    {
        Initialize();
        if (amount < 0 || Credits < amount)
            return false;
        Credits -= amount;
        return true;
    }

    public static bool PurchaseUpgrade(string branch)
    {
        int level = branch switch { "logistics" => LogisticsLevel, "operations" => OperationsLevel, "squadron" => SquadronLevel, _ => -1 };
        int cost = UpgradeCost(level);
        if (level < 0 || level >= 3 || Credits < cost) return false;
        Credits -= cost;
        switch (branch) { case "logistics": LogisticsLevel++; break; case "operations": OperationsLevel++; break; case "squadron": SquadronLevel++; break; }
        Save();
        return true;
    }

    public static bool PurchaseShipUpgrade(Pilot pilot, ShipUpgrade upgrade)
    {
        Initialize();
        if (pilot == null || Credits < ShipUpgrades.Cost || !pilot.CanInstallUpgrade(upgrade))
            return false;
        if (!pilot.InstallUpgrade(upgrade))
            return false;
        Credits -= ShipUpgrades.Cost;
        Save();
        return true;
    }

    public const float HullRepairFraction = 0.25f;
}
