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
            new BattleSpawn(500, 350, 48), new BattleSpawn(430, 675, 4), new BattleSpawn(520, 1000, -42),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1900, 315, 132), new BattleSpawn(1970, 675, 176), new BattleSpawn(1880, 1035, 222),
            new BattleSpawn(2140, 470, 152), new BattleSpawn(2140, 865, 208), new BattleSpawn(2070, 1160, 225),
        },
    };

    public static readonly BattleMapDefinition CobaltVeil = new()
    {
        Id = "cobalt-veil",
        DisplayName = "COBALT VEIL",
        Briefing = "Ionized nebulae slow ships and make shots unreliable.",
        Terrain = new[]
        {
            new TerrainFeature(TerrainFeatureType.Nebula, 1010, 475, 230),
            new TerrainFeature(TerrainFeatureType.Nebula, 1430, 850, 260),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1215, 665, 58),
            new TerrainFeature(TerrainFeatureType.Asteroid, 1325, 740, 44),
        },
        PlayerSpawns = new[]
        {
            new BattleSpawn(430, 450, 26), new BattleSpawn(570, 735, -28), new BattleSpawn(390, 1000, 52),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1970, 340, 154), new BattleSpawn(1830, 690, 208), new BattleSpawn(2010, 930, 142),
            new BattleSpawn(2140, 500, 164), new BattleSpawn(2150, 805, 196), new BattleSpawn(2070, 1120, 218),
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
            new BattleSpawn(510, 315, 62), new BattleSpawn(360, 675, -3), new BattleSpawn(500, 1030, -58),
        },
        EnemySpawns = new[]
        {
            new BattleSpawn(1890, 305, 118), new BattleSpawn(2040, 675, 183), new BattleSpawn(1900, 1045, 238),
            new BattleSpawn(2150, 470, 145), new BattleSpawn(2160, 855, 215), new BattleSpawn(2060, 1170, 232),
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

    public static readonly BattleMapDefinition[] All = { ShardRun, CobaltVeil, BrokenRing, EscortCorridor };

    /// <summary>The portrait battlefield for the battle that is about to start.</summary>
    public static BattleMapDefinition ForCurrentBattle() => AuthoredForCurrentBattle().ToPortrait();

    static BattleMapDefinition AuthoredForCurrentBattle()
    {
        if (GameSetup.IsTestBattle)
            return GameSetup.TestBattleMap ?? ShardRun;

        return ById(GameSetup.Mission?.MapId);
    }

    public static BattleMapDefinition ById(string id) => id switch
    {
        "shard-run" => ShardRun,
        "cobalt-veil" => CobaltVeil,
        EscortCorridorId => EscortCorridor,
        _ => BrokenRing,
    };
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
