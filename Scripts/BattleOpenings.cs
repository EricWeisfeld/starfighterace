using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>How the two wings meet at the start of a battle.</summary>
public enum BattleOpening
{
    /// <summary>The map's own starts: the wings meet nose to nose.</summary>
    HeadOn,
    /// <summary>The enemy starts much further off, so the approach is yours to shape.</summary>
    LongApproach,
    /// <summary>The enemy cuts in across your path from one side.</summary>
    Flanked,
    /// <summary>The enemy splits and closes from both sides.</summary>
    Pincer,
    /// <summary>Both wings fly the same way, side by side and out of range.</summary>
    RunningFight,
    /// <summary>The enemy starts behind you.</summary>
    Bounced,
    /// <summary>Head-on, but reinforcements come in behind your squadron.</summary>
    Ambush,
}

/// <summary>Where each side starts a battle, and where enemy reinforcements come in.</summary>
public class BattleDeployment
{
    public BattleSpawn[] PlayerSpawns;
    public BattleSpawn[] EnemySpawns;
    /// <summary>Spots reinforcements may use; null brings them in behind your squadron.</summary>
    public BattleSpawn[] WaveSpawns;
}

/// <summary>
/// Battle openings: each battle stop picks one, so the same map can start as
/// a joust, a crossing, a pincer or a bounce. Openings place ships in the
/// portrait arena's own coordinates and move any start that lands on a rock or
/// in gas to the nearest clear spot.
/// </summary>
public static class BattleOpenings
{
    const float W = BattleManager.ArenaW, H = BattleManager.ArenaH;
    /// <summary>A long approach moves the enemy wing this much further away.</summary>
    const float LongApproachShift = 330f;
    /// <summary>Reinforcements in an ambush come in this far behind your squadron.</summary>
    const float AmbushDistance = 720f;

    public static string Name(BattleOpening opening) => opening switch
    {
        BattleOpening.LongApproach => "LONG APPROACH",
        BattleOpening.Flanked => "FLANKED",
        BattleOpening.Pincer => "PINCER",
        BattleOpening.RunningFight => "RUNNING FIGHT",
        BattleOpening.Bounced => "BOUNCED",
        BattleOpening.Ambush => "AMBUSH",
        _ => "HEAD-ON",
    };

    /// <summary>One sentence for the briefing. With no side given, a one-sided opening says "either side".</summary>
    public static string Summary(BattleOpening opening, bool? mirrored) => opening switch
    {
        BattleOpening.LongApproach => "The enemy starts far off. You have time to pick your angle.",
        BattleOpening.Flanked => $"The enemy cuts in across your path from {mirrored switch { null => "either side", true => "the right", _ => "the left" }}.",
        BattleOpening.Pincer => "The enemy splits and closes from both sides.",
        BattleOpening.RunningFight => $"Both wings fly the same way, out of range, with the enemy on {mirrored switch { null => "either side", true => "your left", _ => "your right" }}.",
        BattleOpening.Bounced => "The enemy jumps you from behind.",
        BattleOpening.Ambush => "The enemy meets you head-on. Its reinforcements come in behind you.",
        _ => "The wings meet nose to nose.",
    };

    /// <summary>The short call-out at the start of the battle.</summary>
    public static string Callout(BattleOpening opening, bool mirrored) => opening switch
    {
        BattleOpening.LongApproach => "LONG APPROACH · ENEMY FAR OFF",
        BattleOpening.Flanked => $"FLANKED · ENEMY ON YOUR {(mirrored ? "RIGHT" : "LEFT")}",
        BattleOpening.Pincer => "PINCER · ENEMIES ON BOTH SIDES",
        BattleOpening.RunningFight => $"RUNNING FIGHT · ENEMY ON YOUR {(mirrored ? "LEFT" : "RIGHT")}",
        BattleOpening.Bounced => "BOUNCED · ENEMY BEHIND YOU",
        BattleOpening.Ambush => "AMBUSH · REINFORCEMENTS WILL COME FROM BEHIND",
        _ => "HEAD-ON",
    };

    /// <summary>
    /// Picks a battle stop's opening. A run's first fight is always a plain
    /// approach; a bounce waits until the squadron has a fight or two behind
    /// it; an ambush needs reinforcements.
    /// </summary>
    public static BattleOpening Pick(RandomNumberGenerator rng, int sector, int layer, bool hasWave)
    {
        if (sector == 1 && layer == 1)
            return rng.Randf() < 0.5f ? BattleOpening.HeadOn : BattleOpening.LongApproach;
        var options = new List<(BattleOpening Opening, float Weight)>
        {
            (BattleOpening.HeadOn, 1.5f),
            (BattleOpening.LongApproach, 2f),
            (BattleOpening.Flanked, 2f),
            (BattleOpening.Pincer, 2f),
            (BattleOpening.RunningFight, 2f),
        };
        if (sector > 1 || layer > 2)
            options.Add((BattleOpening.Bounced, 1f));
        if (hasWave)
            options.Add((BattleOpening.Ambush, 1.5f));
        float roll = rng.Randf() * options.Sum(o => o.Weight);
        foreach ((BattleOpening opening, float weight) in options)
        {
            roll -= weight;
            if (roll < 0f)
                return opening;
        }
        return options[^1].Opening;
    }

    /// <summary>Whether a mirrored opening plays differently: only the one-sided ones do.</summary>
    public static bool Sided(BattleOpening opening) =>
        opening is BattleOpening.Flanked or BattleOpening.RunningFight or BattleOpening.Bounced;

    /// <summary>Lays out both wings on a portrait battlefield.</summary>
    public static BattleDeployment Deploy(BattleMapDefinition map, BattleOpening opening, bool mirrored)
    {
        mirrored &= Sided(opening);
        BattleSpawn[] players = map.PlayerSpawns;
        BattleSpawn[] enemies;
        BattleSpawn[] waves = map.EnemySpawns;
        switch (opening)
        {
            case BattleOpening.LongApproach:
                enemies = map.EnemySpawns
                    .Select(s => Spawn(s.Position.X, Mathf.Max(150f, s.Position.Y - LongApproachShift), s.HeadingDegrees))
                    .ToArray();
                waves = null;
                break;
            case BattleOpening.Flanked:
                enemies = new[]
                {
                    Spawn(130, 1150, 15), Spawn(250, 1000, 22), Spawn(130, 870, 28),
                    Spawn(260, 760, 32), Spawn(120, 640, 38), Spawn(110, 1320, 8),
                };
                break;
            case BattleOpening.Pincer:
                enemies = new[]
                {
                    Spawn(180, 900, 60), Spawn(1170, 900, 120), Spawn(110, 1080, 45),
                    Spawn(1240, 1080, 135), Spawn(300, 740, 70), Spawn(1050, 740, 110),
                };
                break;
            case BattleOpening.RunningFight:
                players = new[] { Spawn(240, 1760, -90), Spawn(420, 1680, -90), Spawn(330, 1900, -90) };
                enemies = new[]
                {
                    Spawn(1060, 1540, -90), Spawn(1200, 1420, -90), Spawn(920, 1440, -90),
                    Spawn(1180, 1660, -90), Spawn(1050, 1300, -90), Spawn(930, 1640, -90),
                };
                break;
            case BattleOpening.Bounced:
            {
                Vector2 c = Centroid(players.Select(s => s.Position));
                enemies = new[]
                {
                    Spawn(c.X - 220, c.Y + 520, -80), Spawn(c.X + 200, c.Y + 560, -100), Spawn(c.X, c.Y + 640, -90),
                    Spawn(c.X - 380, c.Y + 660, -75), Spawn(c.X + 360, c.Y + 700, -105), Spawn(c.X - 60, c.Y + 480, -88),
                };
                break;
            }
            case BattleOpening.Ambush:
                enemies = map.EnemySpawns;
                waves = null;
                break;
            default:
                return new BattleDeployment { PlayerSpawns = players, EnemySpawns = map.EnemySpawns, WaveSpawns = map.EnemySpawns };
        }

        // The map's own starts are already clear; moved ones are cleared of
        // rocks and gas, and kept apart from every other ship.
        bool ownPlayers = players == map.PlayerSpawns, ownEnemies = enemies == map.EnemySpawns;
        if (mirrored)
        {
            if (!ownPlayers)
                players = players.Select(Mirror).ToArray();
            enemies = enemies.Select(Mirror).ToArray();
            ownEnemies = false;
        }
        var taken = players.Concat(enemies).Where((_, i) => i < players.Length ? ownPlayers : ownEnemies)
            .Select(s => s.Position).ToList();
        if (!ownPlayers)
            players = players.Select(s => Cleared(s, map, taken)).ToArray();
        if (!ownEnemies)
            enemies = enemies.Select(s => Cleared(s, map, taken)).ToArray();
        // A long approach brings reinforcements in where the wing started.
        if (opening == BattleOpening.LongApproach)
            waves = enemies;
        return new BattleDeployment { PlayerSpawns = players, EnemySpawns = enemies, WaveSpawns = waves };
    }

    /// <summary>
    /// Ambush reinforcements: in a line abreast behind your squadron, facing it,
    /// clear of rocks, gas and every ship already on the field.
    /// </summary>
    public static BattleSpawn[] BehindSquad(IReadOnlyCollection<Fighter> squad, int count, BattleMapDefinition map,
        IEnumerable<Vector2> occupied)
    {
        Vector2 c = Centroid(squad.Select(f => f.Position));
        Vector2 facing = squad.Aggregate(Vector2.Zero, (sum, f) => sum + Vector2.FromAngle(f.Heading));
        float heading = facing.LengthSquared() > 0.01f ? facing.Angle() : -Mathf.Pi / 2f;
        Vector2 back = -Vector2.FromAngle(heading), side = back.Orthogonal();
        var taken = occupied.ToList();
        return Enumerable.Range(0, count).Select(i =>
        {
            float lateral = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 200f;
            Vector2 p = c + back * AmbushDistance + side * lateral;
            p = new Vector2(Mathf.Clamp(p.X, 100f, W - 100f), Mathf.Clamp(p.Y, 120f, H - 120f));
            return Cleared(Spawn(p.X, p.Y, Mathf.RadToDeg((c - p).Angle())), map, taken);
        }).ToArray();
    }

    static BattleSpawn Spawn(float x, float y, float headingDegrees) => new(x, y, headingDegrees);

    static BattleSpawn Mirror(BattleSpawn s) => Spawn(W - s.Position.X, s.Position.Y, 180f - s.HeadingDegrees);

    static Vector2 Centroid(IEnumerable<Vector2> points)
    {
        var list = points.ToList();
        return list.Count == 0 ? new Vector2(W / 2f, H / 2f) : list.Aggregate(Vector2.Zero, (a, b) => a + b) / list.Count;
    }

    /// <summary>The start moved to the nearest spot clear of rocks, gas, the edge and other ships.</summary>
    static BattleSpawn Cleared(BattleSpawn spawn, BattleMapDefinition map, List<Vector2> taken)
    {
        Vector2 p = spawn.Position;
        for (int ring = 0; ring <= 14; ring++)
        {
            int steps = ring == 0 ? 1 : 12;
            for (int k = 0; k < steps; k++)
            {
                Vector2 q = p + Vector2.FromAngle(k * Mathf.Tau / steps) * ring * 35f;
                if (!Fits(q, map, taken))
                    continue;
                taken.Add(q);
                return Spawn(q.X, q.Y, spawn.HeadingDegrees);
            }
        }
        taken.Add(p);
        return spawn;
    }

    static bool Fits(Vector2 p, BattleMapDefinition map, List<Vector2> taken) =>
        p.X >= 90f && p.X <= W - 90f && p.Y >= 110f && p.Y <= H - 110f &&
        map.Terrain.All(t => p.DistanceTo(t.Position) > t.Radius + (t.Type == TerrainFeatureType.Asteroid ? 75f : 15f)) &&
        taken.All(q => q.DistanceTo(p) > 110f);
}
