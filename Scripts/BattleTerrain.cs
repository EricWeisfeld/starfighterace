using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>One circular tactical feature placed on a battle map.</summary>
public class TerrainFeature
{
    public TerrainFeatureType Type;
    public Vector2 Position;
    public float Radius;

    public TerrainFeature(TerrainFeatureType type, float x, float y, float radius)
    {
        Type = type;
        Position = new Vector2(x, y);
        Radius = radius;
    }
}

public enum TerrainFeatureType
{
    Asteroid,
    Nebula,
}

/// <summary>A single squadron's opening position and facing on a battle map.</summary>
public class BattleSpawn
{
    public Vector2 Position;
    public float HeadingDegrees;

    public BattleSpawn(float x, float y, float headingDegrees)
    {
        Position = new Vector2(x, y);
        HeadingDegrees = headingDegrees;
    }
}

/// <summary>
/// Data for a deliberately authored tactical battlefield. Maps are authored
/// in a landscape frame (player squadron on the left, enemies on the right)
/// and turned into the portrait battlefield by <see cref="ToPortrait()"/>.
/// </summary>
public class BattleMapDefinition
{
    public string Id;
    public string DisplayName;
    public string Briefing;
    public TerrainFeature[] Terrain;
    public BattleSpawn[] PlayerSpawns;
    public BattleSpawn[] EnemySpawns;
    public BattleSpawn[] EscortReinforcementSpawns;
    public BattleSpawn EscortSpawn;
    public Vector2 EscortDestination;
    public float EscortDestinationRadius;

    /// <summary>
    /// Returns a copy turned a quarter-turn anticlockwise so the authored
    /// left-to-right approach runs bottom-to-top on a portrait screen: the
    /// player's squadron starts at the bottom and the enemy wing at the top.
    /// </summary>
    public BattleMapDefinition ToPortrait() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Briefing = Briefing,
        Terrain = Terrain.Select(feature =>
        {
            Vector2 p = ToPortrait(feature.Position);
            return new TerrainFeature(feature.Type, p.X, p.Y, feature.Radius);
        }).ToArray(),
        PlayerSpawns = ToPortrait(PlayerSpawns),
        EnemySpawns = ToPortrait(EnemySpawns),
        EscortReinforcementSpawns = ToPortrait(EscortReinforcementSpawns),
        EscortSpawn = EscortSpawn == null ? null : ToPortrait(EscortSpawn),
        // Vector2.Zero means "not authored"; keep it recognisable after the turn.
        EscortDestination = EscortDestination == Vector2.Zero ? Vector2.Zero : ToPortrait(EscortDestination),
        EscortDestinationRadius = EscortDestinationRadius,
    };

    static Vector2 ToPortrait(Vector2 authored) => new(authored.Y, BattleMaps.AuthoredWidth - authored.X);

    static BattleSpawn ToPortrait(BattleSpawn spawn)
    {
        Vector2 p = ToPortrait(spawn.Position);
        return new BattleSpawn(p.X, p.Y, spawn.HeadingDegrees - 90f);
    }

    static BattleSpawn[] ToPortrait(BattleSpawn[] spawns) => spawns?.Select(ToPortrait).ToArray();
}

/// <summary>Battle maps rotate terrain and opening angles instead of always staging a head-on joust.</summary>
public static class BattleMaps
{
    public const string EscortCorridorId = "escort-corridor";
    // Authored (landscape) battlefield size. The portrait arena swaps these.
    public const float AuthoredWidth = 2400f;
    public const float AuthoredHeight = 1350f;

    public static readonly BattleMapDefinition ShardRun = new()
    {
        Id = "shard-run",
        DisplayName = "SHARD RUN",
        Briefing = "Broken asteroids split the center into narrow firing lanes.",
        Terrain = new[]
        {
            new TerrainFeature(TerrainFeatureType.Asteroid, 1080, 390, 58),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1170, 510, 78),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1235, 650, 46),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1300, 770, 70),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1220, 910, 54),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1120, 1030, 65),
        },
        PlayerSpawns = new[]
        {
            new BattleSpawn(760, 350, 48), new BattleSpawn(690, 675, 4), new BattleSpawn(780, 1000, -42),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1640, 315, 132), new BattleSpawn(1710, 675, 176), new BattleSpawn(1620, 1035, 222),
            new BattleSpawn(1880, 470, 152), new BattleSpawn(1880, 865, 208), new BattleSpawn(1810, 1160, 225),
        },
    };

    public static readonly BattleMapDefinition CobaltVeil = new()
    {
        Id = "cobalt-veil",
        DisplayName = "COBALT VEIL",
        Briefing = "Ionized nebulae drag at ships that start a turn inside them and scatter shots fired through them.",
        Terrain = new[]
        {
            new TerrainFeature(TerrainFeatureType.Nebula, 1010, 475, 165),
            new TerrainFeature(TerrainFeatureType.Nebula, 1430, 850, 185),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1215, 665, 58),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1325, 740, 44),
        },
        PlayerSpawns = new[]
        {
            new BattleSpawn(690, 450, 26), new BattleSpawn(830, 735, -28), new BattleSpawn(650, 1000, 52),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1750, 340, 154), new BattleSpawn(1610, 690, 208), new BattleSpawn(1790, 930, 142),
            new BattleSpawn(1920, 500, 164), new BattleSpawn(1930, 805, 196), new BattleSpawn(1850, 1120, 218),
        },
    };

    public static readonly BattleMapDefinition BrokenRing = new()
    {
        Id = "broken-ring",
        DisplayName = "BROKEN RING",
        Briefing = "A shattered orbital ring creates cover, traps, and two contested crossings.",
        Terrain = new[]
        {
            new TerrainFeature(TerrainFeatureType.Asteroid, 835, 365, 68),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1040, 290, 56),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1380, 330, 74),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1570, 500, 52),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1560, 855, 65),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1360, 1040, 58),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1030, 1065, 72),
            new TerrainFeature(TerrainFeatureType.Asteroid, 830, 890, 54),
            new TerrainFeature(TerrainFeatureType.Nebula, 1200, 665, 150),
        },
        PlayerSpawns = new[]
        {
            new BattleSpawn(710, 315, 62), new BattleSpawn(560, 675, -3), new BattleSpawn(700, 1030, -20),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1690, 305, 150), new BattleSpawn(1840, 675, 183), new BattleSpawn(1700, 1045, 210),
            new BattleSpawn(1950, 470, 145), new BattleSpawn(1960, 855, 215), new BattleSpawn(1860, 1170, 232),
        },
    };

    public static readonly BattleMapDefinition OpenDrift = new()
    {
        Id = "open-drift",
        DisplayName = "OPEN DRIFT",
        Briefing = "Open space with a few drifting rocks to break line of fire.",
        Terrain = new[]
        {
            new TerrainFeature(TerrainFeatureType.Asteroid, 1270, 420, 70),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1360, 960, 58),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1010, 760, 44),
            new TerrainFeature(TerrainFeatureType.Asteroid, 960, 1110, 34),
            new TerrainFeature(TerrainFeatureType.Asteroid, 920, 250, 30),
        },
        PlayerSpawns = new[]
        {
            new BattleSpawn(760, 470, 0), new BattleSpawn(720, 675, 0), new BattleSpawn(760, 880, 0),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1620, 470, 180), new BattleSpawn(1660, 675, 180), new BattleSpawn(1620, 880, 180),
            new BattleSpawn(1770, 300, 170), new BattleSpawn(1770, 1050, 190), new BattleSpawn(1830, 675, 180),
        },
    };

    public static readonly BattleMapDefinition RubbleBelt = new()
    {
        Id = "rubble-belt",
        DisplayName = "RUBBLE BELT",
        Briefing = "A wall of rubble splits the field. Thread one of its two gaps or scrape through.",
        Terrain = new[]
        {
            new TerrainFeature(TerrainFeatureType.Asteroid, 971, 56, 30),
            new TerrainFeature(TerrainFeatureType.Asteroid, 982, 149, 46),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1036, 227, 30),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1072, 311, 40),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1066, 397, 30),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1178, 634, 40),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1202, 709, 36),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1206, 791, 40),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1319, 1027, 30),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1325, 1123, 40),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1357, 1208, 36),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1414, 1285, 40),
        },
        PlayerSpawns = new[]
        {
            new BattleSpawn(720, 470, 0), new BattleSpawn(680, 675, 0), new BattleSpawn(720, 880, 0),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1640, 470, 180), new BattleSpawn(1680, 675, 180), new BattleSpawn(1640, 880, 180),
            new BattleSpawn(1790, 300, 170), new BattleSpawn(1790, 1050, 190), new BattleSpawn(1850, 675, 180),
        },
    };

    public static readonly BattleMapDefinition Crossing = new()
    {
        Id = "crossing",
        DisplayName = "CROSSING",
        Briefing = "The enemy wing crosses ahead of you from the right. Turn in to meet it.",
        Terrain = new[]
        {
            new TerrainFeature(TerrainFeatureType.Asteroid, 1140, 700, 55),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1440, 500, 40),
            new TerrainFeature(TerrainFeatureType.Asteroid, 870, 960, 42),
        },
        PlayerSpawns = new[]
        {
            new BattleSpawn(750, 250, 35), new BattleSpawn(670, 420, 35), new BattleSpawn(600, 150, 35),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1380, 1060, 270), new BattleSpawn(1230, 1140, 270), new BattleSpawn(1520, 1000, 270),
            new BattleSpawn(1640, 1190, 260), new BattleSpawn(1080, 1210, 280), new BattleSpawn(1780, 1150, 255),
        },
    };

    public static readonly BattleMapDefinition Monolith = new()
    {
        Id = "monolith",
        DisplayName = "MONOLITH",
        Briefing = "One huge rock blocks the direct line. Pick a side and go around it.",
        Terrain = new[]
        {
            new TerrainFeature(TerrainFeatureType.Asteroid, 1200, 675, 150),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1440, 340, 38),
            new TerrainFeature(TerrainFeatureType.Asteroid, 960, 1010, 38),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1470, 1080, 26),
            new TerrainFeature(TerrainFeatureType.Asteroid, 930, 270, 26),
        },
        PlayerSpawns = new[]
        {
            new BattleSpawn(760, 470, 0), new BattleSpawn(720, 675, 0), new BattleSpawn(760, 880, 0),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1630, 470, 180), new BattleSpawn(1670, 675, 180), new BattleSpawn(1630, 880, 180),
            new BattleSpawn(1780, 300, 170), new BattleSpawn(1780, 1050, 190), new BattleSpawn(1840, 675, 180),
        },
    };

    public static readonly BattleMapDefinition Shallows = new()
    {
        Id = "shallows",
        DisplayName = "SHALLOWS",
        Briefing = "Two small nebula pockets at the edges: cover from fire, at the cost of a shorter move.",
        Terrain = new[]
        {
            new TerrainFeature(TerrainFeatureType.Nebula, 1250, 220, 140),
            new TerrainFeature(TerrainFeatureType.Nebula, 1140, 1130, 140),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1290, 600, 50),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1070, 820, 40),
        },
        PlayerSpawns = new[]
        {
            new BattleSpawn(760, 470, 0), new BattleSpawn(720, 675, 0), new BattleSpawn(760, 880, 0),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1620, 470, 180), new BattleSpawn(1660, 675, 180), new BattleSpawn(1620, 880, 180),
            new BattleSpawn(1770, 300, 170), new BattleSpawn(1770, 1050, 190), new BattleSpawn(1830, 675, 180),
        },
    };

    public static readonly BattleMapDefinition GravelField = new()
    {
        Id = "gravel-field",
        DisplayName = "GRAVEL FIELD",
        Briefing = "Small rocks scattered everywhere. Long, careless curves will scrape.",
        Terrain = new[]
        {
            new TerrainFeature(TerrainFeatureType.Asteroid, 1366, 127, 22),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1349, 307, 18),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1369, 471, 26),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1389, 677, 22),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1395, 838, 26),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1364, 1038, 22),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1361, 1238, 26),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1193, 229, 26),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1228, 374, 18),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1183, 586, 22),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1179, 756, 26),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1212, 924, 18),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1203, 1130, 22),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1027, 124, 18),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1041, 322, 18),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1058, 491, 26),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1036, 686, 22),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1003, 843, 26),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1047, 1000, 18),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1001, 1208, 22),
        },
        PlayerSpawns = new[]
        {
            new BattleSpawn(740, 470, 0), new BattleSpawn(700, 675, 0), new BattleSpawn(740, 880, 0),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1640, 470, 180), new BattleSpawn(1680, 675, 180), new BattleSpawn(1640, 880, 180),
            new BattleSpawn(1790, 300, 170), new BattleSpawn(1790, 1050, 190), new BattleSpawn(1850, 675, 180),
        },
    };

    public static readonly BattleMapDefinition KnifeFight = new()
    {
        Id = "knife-fight",
        DisplayName = "KNIFE FIGHT",
        Briefing = "A close start with rocks straight ahead. Choose a side on the first turn.",
        Terrain = new[]
        {
            new TerrainFeature(TerrainFeatureType.Asteroid, 1210, 630, 48),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1165, 745, 38),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1260, 700, 30),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1200, 240, 40),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1220, 1110, 40),
        },
        PlayerSpawns = new[]
        {
            new BattleSpawn(850, 470, 0), new BattleSpawn(810, 675, 0), new BattleSpawn(850, 880, 0),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1550, 470, 180), new BattleSpawn(1590, 675, 180), new BattleSpawn(1550, 880, 180),
            new BattleSpawn(1700, 300, 170), new BattleSpawn(1700, 1050, 190), new BattleSpawn(1760, 675, 180),
        },
    };

    /// <summary>
    /// A long escort lane. The transport needs about fourteen clear turns to
    /// reach the jump zone, while the hostile wing approaches from an offset
    /// interception screen instead of starting in immediate firing range.
    /// </summary>
    public static readonly BattleMapDefinition EscortCorridor = new()
    {
        Id = EscortCorridorId,
        DisplayName = "JUMP CORRIDOR",
        Briefing = "Protect the transport through the long approach and deliver it to the marked jump zone.",
        Terrain = new[]
        {
            new TerrainFeature(TerrainFeatureType.Asteroid, 810, 315, 72),
            new TerrainFeature(TerrainFeatureType.Asteroid, 980, 420, 48),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1040, 1020, 68),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1210, 900, 52),
            new TerrainFeature(TerrainFeatureType.Nebula, 1450, 300, 180),
            new TerrainFeature(TerrainFeatureType.Nebula, 1580, 1080, 190),
        },
        PlayerSpawns = new[]
        {
            new BattleSpawn(330, 440, 8), new BattleSpawn(270, 675, 0), new BattleSpawn(330, 910, -8),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1420, 380, 158), new BattleSpawn(1510, 675, 180), new BattleSpawn(1420, 970, 202),
            new BattleSpawn(1740, 470, 166), new BattleSpawn(1770, 820, 194), new BattleSpawn(1960, 675, 180),
        },
        EscortReinforcementSpawns = new[]
        {
            new BattleSpawn(1650, 270, 160), new BattleSpawn(1800, 500, 174), new BattleSpawn(1840, 850, 188),
            new BattleSpawn(1660, 1080, 200), new BattleSpawn(2020, 390, 174), new BattleSpawn(2070, 960, 186),
        },
        EscortSpawn = new BattleSpawn(360, 675, 0),
        EscortDestination = new Vector2(2050, 675),
        EscortDestinationRadius = 95f,
    };

    /// <summary>The maps ordinary battles are fought on.</summary>
    public static readonly BattleMapDefinition[] Battlefields =
    {
        ShardRun, CobaltVeil, BrokenRing, OpenDrift, RubbleBelt, Crossing, Monolith, Shallows, GravelField, KnifeFight,
    };

    public static readonly BattleMapDefinition[] All = Battlefields.Append(EscortCorridor).ToArray();

    /// <summary>The portrait battlefield for the battle that is about to start.</summary>
    public static BattleMapDefinition ForCurrentBattle() => AuthoredForCurrentBattle().ToPortrait();

    static BattleMapDefinition AuthoredForCurrentBattle()
    {
        if (GameSetup.IsTestBattle)
            return GameSetup.TestBattleMap ?? ShardRun;

        return ById(GameSetup.Mission?.MapId);
    }

    public static BattleMapDefinition ById(string id) => All.FirstOrDefault(map => map.Id == id) ?? BrokenRing;
}

/// <summary>Draws authored terrain below ships without requiring scene-node assets.</summary>
public partial class TerrainLayer : Node2D
{
    readonly IEnumerable<TerrainFeature> _terrain;
    float _drawnScale = -1f;

    /// <summary>Engine-required parameterless constructor; use the terrain overload in code.</summary>
    public TerrainLayer() => _terrain = System.Array.Empty<TerrainFeature>();

    public TerrainLayer(IEnumerable<TerrainFeature> terrain) => _terrain = terrain;

    // Labels keep a constant on-screen size, so redraw when the zoom changes.
    public override void _Process(double delta)
    {
        float scale = BattleManager.Instance?.ScreenToWorldScale ?? 1f;
        if (!Mathf.IsEqualApprox(scale, _drawnScale))
            QueueRedraw();
    }

    public override void _Draw()
    {
        _drawnScale = BattleManager.Instance?.ScreenToWorldScale ?? 1f;
        int labelSize = Mathf.Max(1, Mathf.RoundToInt(SignalUi.FontMicro * _drawnScale));
        foreach (TerrainFeature feature in _terrain)
        {
            if (feature.Type == TerrainFeatureType.Nebula)
            {
                DrawCircle(feature.Position, feature.Radius, new Color(0.20f, 0.42f, 0.72f, 0.10f));
                DrawCircle(feature.Position, feature.Radius * 0.72f, new Color(0.36f, 0.24f, 0.72f, 0.11f));
                DrawArc(feature.Position, feature.Radius, 0, Mathf.Tau, 48, new Color(0.38f, 0.65f, 1f, 0.42f), 2f, true);
                Vector2 extent = ThemeDB.FallbackFont.GetStringSize("NEBULA", HorizontalAlignment.Left, -1f, labelSize);
                DrawString(ThemeDB.FallbackFont, feature.Position + new Vector2(-extent.X / 2f, labelSize * 0.35f), "NEBULA",
                    HorizontalAlignment.Left, -1f, labelSize, new Color(0.56f, 0.76f, 1f, 0.58f));
                continue;
            }

            DrawCircle(feature.Position, feature.Radius + 5f, new Color(0f, 0f, 0f, 0.40f));
            DrawCircle(feature.Position, feature.Radius, new Color(0.20f, 0.23f, 0.30f, 1f));
            DrawCircle(feature.Position - new Vector2(feature.Radius * 0.20f, feature.Radius * 0.22f), feature.Radius * 0.50f,
                new Color(0.30f, 0.34f, 0.42f, 1f));
            DrawArc(feature.Position, feature.Radius, 0, Mathf.Tau, 32, new Color(0.62f, 0.68f, 0.78f, 0.52f), 1.5f * Mathf.Max(1f, _drawnScale), true);
        }
    }
}
