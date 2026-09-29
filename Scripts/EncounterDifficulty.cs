using Godot;
using System;

/// <summary>One rolled enemy formation: a trade-off between ship count and combat stats.</summary>
public readonly struct EnemyEncounter
{
    public readonly int ShipCount;
    public readonly float StatMultiplier;

    public EnemyEncounter(int shipCount, int statBonusPercent)
    {
        ShipCount = shipCount;
        StatMultiplier = 1f + statBonusPercent / 100f;
    }
}

/// <summary>
/// The campaign's ten-step enemy difficulty curve. Each level selects either
/// an elite wing, its usual force, or a larger lower-stat patrol; those two
/// pressure levers deliberately never peak together.
/// </summary>
public static class EncounterDifficulty
{
    public const int MinLevel = 1;
    public const int MaxLevel = 10;
    public const int EscortLevelOffset = 2;

    // [level - 1, profile] where profile 0 = quality, 1 = typical, 2 = numbers.
    static readonly EnemyEncounter[,] Profiles =
    {
        { new(2, 5),  new(2, 0),  new(3, 0)  },
        { new(2, 10), new(2, 4),  new(3, 0)  },
        { new(2, 15), new(3, 8),  new(3, 2)  },
        { new(3, 20), new(3, 12), new(3, 5)  },
        { new(3, 25), new(3, 16), new(4, 8)  },
        { new(3, 30), new(4, 20), new(4, 12) },
        { new(3, 36), new(4, 25), new(5, 15) },
        { new(4, 42), new(4, 30), new(5, 19) },
        { new(4, 47), new(5, 35), new(5, 23) },
        { new(4, 52), new(5, 40), new(6, 28) },
    };

    /// <summary>Rolls typical formations 50% of the time, with each biased variant at 25%.</summary>
    public static EnemyEncounter Roll(int level, RandomNumberGenerator rng)
    {
        int profile = rng.Randf() < 0.25f ? 0 : rng.Randf() < 2f / 3f ? 1 : 2;
        return Profiles[Mathf.Clamp(level, MinLevel, MaxLevel) - 1, profile];
    }

    /// <summary>
    /// Escort attackers are equipped to destroy a capital transport as well as
    /// its fighter screen, so their combat roll is two levels above the
    /// campaign threat shown on the mission board.
    /// </summary>
    public static int CombatLevel(int missionLevel, MissionObjective objective) => Mathf.Clamp(
        missionLevel + (objective == MissionObjective.EscortShip ? EscortLevelOffset : 0),
        MinLevel, MaxLevel);

    public static EnemyEncounter RollForMission(int missionLevel, MissionObjective objective, RandomNumberGenerator rng) =>
        Roll(CombatLevel(missionLevel, objective), rng);

    /// <summary>Stable seed for authored missions so their setup does not change across sessions.</summary>
    public static ulong StableSeed(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        ulong hash = offset;
        foreach (char character in value ?? string.Empty)
        {
            hash ^= character;
            hash *= prime;
        }
        return hash;
    }
}
