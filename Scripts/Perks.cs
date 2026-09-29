using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>A pilot trait earned (or suffered) after combat.</summary>
public class Perk
{
    public string Id;
    public string Name;
    public string Description;
    public string EarnHint;
    public bool Positive;
}

public static class Perks
{
    public const float GainChance = 0.4f;
    public const float TripleKillGainChance = 0.6f;
    public const float RecruitmentTraitChance = 0.5f;
    public const float RecruitmentPositiveTraitChance = 0.5f;
    public const float NegativeWinChance = 0.2f; // a negative trait can stick even after a win, just less often
    public const float BaseEjectChance = 0.6f;
    public const float SurvivorEjectBonus = 0.2f;
    public const float EdgeRangeFraction = 0.85f;
    public const int EdgeHitsRequired = 3;
    public const float PhantomEvasionBonus = 0.10f;
    public const float RapidFireRateBonus = 0.10f;
    public const float RapidFireAccuracyMultiplier = 0.90f;
    public const int RapidFireMinimumHits = 6;
    public const float ShakyHandsAccuracyPenalty = 0.05f;
    public const int ShakyHandsMinimumShots = 8;
    public const float ShakyHandsMaximumAccuracy = 0.35f;
    public const float EngineShyNormalMoveLimitMultiplier = 0.90f;
    // More available turn angle produces tighter turns. This is deliberately
    // smaller than the movement-distance penalty that defines the trait.
    public const float EngineShyNormalTurnLimitMultiplier = 1.05f;
    public const float RattledEvasionPenalty = 0.05f;
    public const float GunShyFireRatePenalty = 0.10f;
    public const float GunShyOpeningDamageThreshold = 0.60f;
    public const float RecklessFullThrottleNormalTurnLimitMultiplier = 0.90f;
    public const float HesitantRangeMultiplier = 0.90f;
    public const float SurvivorsGuiltCombatPenalty = 0.10f;
    public const float SurvivorsGuiltSecondDeathChanceBonus = 0.20f;
    public const float DeadeyeAccuracyBonus = 0.05f;
    public const int DeadeyeMinimumShots = 8;
    public const float DeadeyeRequiredAccuracy = 0.75f;
    public const float CoolUnderFireAccuracyBonus = 0.10f;
    public const float CoolUnderFireThreshold = 0.50f;
    public const float CoolUnderFireEarnThreshold = 0.35f;
    public const float HardToKillHullMultiplier = 1.10f;
    public const float HardToKillEarnThreshold = 0.25f;
    public const int ShieldDisciplineRegenBonus = 1;
    public const int ShieldDisciplineTurnsRequired = 3;
    public const float HotshotDamageMultiplier = 1.05f;
    public const float HotshotAccuracyBonus = 0.05f;
    public const float FinisherDamageMultiplier = 1.15f;
    public const float FinisherHullThreshold = 0.35f;
    public const int HotshotKillsRequired = 2;
    public const float AfterburnerNormalMoveLimitMultiplier = 1.08f;
    public const int AfterburnerFullThrottleNormalMovesRequired = 4;

    public static readonly Perk LongShot = new()
    {
        Id = "long-shot", Name = "Long Shot", Positive = true,
        Description = "+10% weapon range.",
        EarnHint = "Chance to earn by landing several hits from the edge of your range in a victory.",
    };
    public static readonly Perk BadUnderPressure = new()
    {
        Id = "bad-under-pressure", Name = "Bad Under Pressure", Positive = false,
        Description = "-20% max hull.",
        EarnHint = "Risked by getting shot down — more likely in a defeat.",
    };
    public static readonly Perk ShakyHands = new()
    {
        Id = "shaky-hands", Name = "Shaky Hands", Positive = false,
        Description = "-5% accuracy.",
        EarnHint = "Risked by landing some, but no more than 35%, of at least 8 shots.",
    };
    public static readonly Perk EngineShy = new()
    {
        Id = "engine-shy", Name = "Engine Shy", Positive = false,
        Description = "-10% maximum normal movement distance; +5% normal turning.",
        EarnHint = "Risked by colliding with an asteroid.",
    };
    public static readonly Perk Rattled = new()
    {
        Id = "rattled", Name = "Rattled", Positive = false,
        Description = "-5% evasion.",
        EarnHint = "Risked by falling below 25% hull and returning in the damaged ship.",
    };
    public static readonly Perk GunShy = new()
    {
        Id = "gun-shy", Name = "Gun Shy", Positive = false,
        Description = "Fire 10% slower.",
        EarnHint = "Risked by losing more than 60% hull in the first turn where anyone takes damage.",
    };
    public static readonly Perk Reckless = new()
    {
        Id = "reckless", Name = "Reckless", Positive = false,
        Description = "-10% turning at maximum normal throttle.",
        EarnHint = "Risked by colliding with an asteroid.",
    };
    public static readonly Perk Hesitant = new()
    {
        Id = "hesitant", Name = "Hesitant", Positive = false,
        Description = "-10% weapon range until landing your first hit each battle.",
        EarnHint = "Risked by winning without landing a hit.",
    };
    public static readonly Perk SurvivorsGuilt = new()
    {
        Id = "survivors-guilt", Name = "Survivor's Guilt", Positive = false,
        Description = "-10% accuracy and evasion after a squadmate is shot down each battle.",
        EarnHint = "Risked by surviving a mission where a squadmate dies; more likely if two die.",
    };
    public static readonly Perk Survivor = new()
    {
        Id = "survivor", Name = "Survivor", Positive = true,
        Description = "+20% chance to eject when shot down.",
        EarnHint = "Chance to earn by ejecting and making it home from a victory.",
    };
    public static readonly Perk Phantom = new()
    {
        Id = "phantom", Name = "Phantom", Positive = true,
        Description = "+10% evasion until taking damage each battle.",
        EarnHint = "Chance to earn by winning without taking damage.",
    };
    public static readonly Perk RapidFire = new()
    {
        Id = "rapid-fire", Name = "Rapid Fire", Positive = true,
        Description = "Fire 10% faster, but suffer -10% accuracy.",
        EarnHint = "Chance to earn by landing the most hits of any ship in a victory (minimum 6).",
    };
    public static readonly Perk Deadeye = new()
    {
        Id = "deadeye", Name = "Deadeye", Positive = true,
        Description = "+5% accuracy.",
        EarnHint = "Chance to earn by landing at least 75% of 8 or more shots in a victory.",
    };
    public static readonly Perk CoolUnderFire = new()
    {
        Id = "cool-under-fire", Name = "Cool Under Fire", Positive = true,
        Description = "+10% accuracy while below 50% hull.",
        EarnHint = "Chance to earn by destroying an enemy while below 35% hull in a victory.",
    };
    public static readonly Perk HardToKill = new()
    {
        Id = "hard-to-kill", Name = "Hard to Kill", Positive = true,
        Description = "+10% maximum hull.",
        EarnHint = "Chance to earn by falling below 25% hull, surviving, and winning.",
    };
    public static readonly Perk ShieldDiscipline = new()
    {
        Id = "shield-discipline", Name = "Shield Discipline", Positive = true,
        Description = "+1 shield regeneration per turn.",
        EarnHint = "Chance to earn by taking shield damage in 3 turns without suffering hull damage in a victory.",
    };
    public static readonly Perk Hotshot = new()
    {
        Id = "hotshot", Name = "Hotshot", Positive = true,
        Description = "+5% weapon damage and accuracy.",
        EarnHint = "Chance to earn by destroying at least 2 enemies in a victory; higher after 3.",
    };
    public static readonly Perk Finisher = new()
    {
        Id = "finisher", Name = "Finisher", Positive = true,
        Description = "+15% weapon damage against enemies below 35% hull.",
        EarnHint = "Chance to earn by destroying at least 2 enemies in a victory; higher after 3.",
    };
    public static readonly Perk Afterburner = new()
    {
        Id = "afterburner", Name = "Afterburner", Positive = true,
        Description = "+8% maximum normal movement distance.",
        EarnHint = "Chance to earn by using maximum normal throttle for 4 turns without hitting an asteroid in a victory.",
    };

    public static readonly Perk[] All =
    {
        LongShot, BadUnderPressure, ShakyHands, EngineShy, Rattled, GunShy, Reckless, Hesitant, SurvivorsGuilt,
        Survivor, Phantom, RapidFire, Deadeye,
        CoolUnderFire, HardToKill, ShieldDiscipline, Hotshot, Finisher, Afterburner,
    };

    /// <summary>Positive perks this battle record qualifies for. Only wins teach them.</summary>
    public static List<Perk> EligiblePositive(Fighter f, IReadOnlyCollection<Fighter> combatants = null)
    {
        var list = new List<Perk>();
        if (f.EdgeHits >= EdgeHitsRequired)
            list.Add(LongShot);
        if (f.Ejected)
            list.Add(Survivor);
        if (!f.TookDamage)
            list.Add(Phantom);
        int mostHits = combatants?.Count > 0 ? combatants.Max(other => other.HitsLanded) : f.HitsLanded;
        if (f.HitsLanded >= RapidFireMinimumHits && f.HitsLanded == mostHits)
            list.Add(RapidFire);
        if (f.ShotsFired >= DeadeyeMinimumShots && f.HitsLanded / (float)f.ShotsFired >= DeadeyeRequiredAccuracy)
            list.Add(Deadeye);
        if (f.LowHullKills > 0)
            list.Add(CoolUnderFire);
        if (f.IsAlive && f.DroppedBelowQuarterHull)
            list.Add(HardToKill);
        if (f.ShieldDamageTurns >= ShieldDisciplineTurnsRequired && !f.HullDamageTaken)
            list.Add(ShieldDiscipline);
        if (f.Kills >= HotshotKillsRequired)
        {
            list.Add(Hotshot);
            list.Add(Finisher);
        }
        if (f.FullThrottleNormalMoveCount >= AfterburnerFullThrottleNormalMovesRequired && !f.HitAsteroid)
            list.Add(Afterburner);
        return WithoutOwned(f, list);
    }

    /// <summary>Negative perks this battle record risks, win or lose.</summary>
    public static List<Perk> EligibleNegative(Fighter f, bool won, int squadmateDeaths = 0)
    {
        var list = new List<Perk>();
        if (!f.IsAlive)
            list.Add(BadUnderPressure);
        if (f.ShotsFired >= ShakyHandsMinimumShots && f.HitsLanded > 0 &&
            f.HitsLanded / (float)f.ShotsFired <= ShakyHandsMaximumAccuracy)
            list.Add(ShakyHands);
        if (f.HitAsteroid)
        {
            list.Add(EngineShy);
            list.Add(Reckless);
        }
        if (f.IsAlive && f.DroppedBelowQuarterHull)
            list.Add(Rattled);
        if (f.TurnOneDamage > f.MaxHp * GunShyOpeningDamageThreshold)
            list.Add(GunShy);
        if (won && f.HitsLanded == 0)
            list.Add(Hesitant);
        if (squadmateDeaths > 0)
            list.Add(SurvivorsGuilt);
        return WithoutOwned(f, list);
    }

    static List<Perk> WithoutOwned(Fighter f, List<Perk> list) =>
        list.Where(p => !f.Pilot.Perks.Contains(p)).ToList();

    /// <summary>
    /// Post-battle perk gate for one surviving pilot: at most one perk, and the
    /// roll usually fails. Negative traits are checked first; being shot down can
    /// change a pilot even in a victory, though it is less likely than in a defeat.
    /// </summary>
    public static Perk Roll(Fighter f, bool won, IReadOnlyCollection<Fighter> combatants = null,
        int squadmateDeaths = 0)
    {
        float negativeChance = won ? NegativeWinChance : GainChance;
        List<Perk> negatives = EligibleNegative(f, won, squadmateDeaths);
        if (negatives.Remove(SurvivorsGuilt))
        {
            float guiltChance = negativeChance +
                (squadmateDeaths >= 2 ? SurvivorsGuiltSecondDeathChanceBonus : 0f);
            if (GD.Randf() < guiltChance)
                return Learn(f, SurvivorsGuilt);
        }
        if (negatives.Count > 0 && GD.Randf() < negativeChance)
            return Learn(f, negatives);

        if (won)
        {
            List<Perk> positives = EligiblePositive(f, combatants);
            bool earnedTripleKillReward = f.Kills >= 3 &&
                positives.Any(perk => perk == Hotshot || perk == Finisher);
            float positiveChance = earnedTripleKillReward ? TripleKillGainChance : GainChance;
            if (positives.Count > 0 && GD.Randf() < positiveChance)
                return Learn(f, positives);
        }
        return null;
    }

    /// <summary>Rolls the optional random trait attached to a new recruit.</summary>
    public static Perk RollRecruitmentTrait(Pilot pilot)
    {
        if (pilot == null || GD.Randf() >= RecruitmentTraitChance)
            return null;

        bool positive = GD.Randf() < RecruitmentPositiveTraitChance;
        Perk[] pool = All.Where(perk => perk.Positive == positive && !pilot.Perks.Contains(perk)).ToArray();
        if (pool.Length == 0)
            return null;

        Perk perk = pool[GD.RandRange(0, pool.Length - 1)];
        pilot.Perks.Add(perk);
        return perk;
    }

    static Perk Learn(Fighter f, List<Perk> pool)
    {
        Perk perk = pool[GD.RandRange(0, pool.Count - 1)];
        return Learn(f, perk);
    }

    static Perk Learn(Fighter f, Perk perk)
    {
        f.Pilot.Perks.Add(perk);
        return perk;
    }

    public static int EffectiveMaxHp(Pilot pilot, int baseMaxHp)
    {
        float multiplier = pilot?.Perks.Contains(BadUnderPressure) == true ? 0.8f : 1f;
        if (pilot?.Perks.Contains(HardToKill) == true)
            multiplier *= HardToKillHullMultiplier;
        return Mathf.RoundToInt(baseMaxHp * multiplier);
    }

    public static int EffectiveShieldRegen(Pilot pilot, int baseRegen) =>
        baseRegen + (pilot?.Perks.Contains(ShieldDiscipline) == true ? ShieldDisciplineRegenBonus : 0);

    public static float DisplayAccuracy(Pilot pilot, float baseAccuracy)
    {
        float accuracy = baseAccuracy + (pilot?.Perks.Contains(Deadeye) == true ? DeadeyeAccuracyBonus : 0f);
        if (pilot?.Perks.Contains(Hotshot) == true)
            accuracy += HotshotAccuracyBonus;
        if (pilot?.Perks.Contains(ShakyHands) == true)
            accuracy -= ShakyHandsAccuracyPenalty;
        if (pilot?.Perks.Contains(RapidFire) == true)
            accuracy *= RapidFireAccuracyMultiplier;
        return Mathf.Clamp(accuracy, 0f, 1f);
    }

    public static float DisplayEvasion(Pilot pilot, float baseEvasion)
    {
        float evasion = baseEvasion;
        if (pilot?.Perks.Contains(Phantom) == true)
            evasion += PhantomEvasionBonus;
        if (pilot?.Perks.Contains(Rattled) == true)
            evasion -= RattledEvasionPenalty;
        return Mathf.Clamp(evasion, 0f, 0.95f);
    }

    public static float DisplayDamage(Pilot pilot, int baseDamage) =>
        baseDamage * (pilot?.Perks.Contains(Hotshot) == true ? HotshotDamageMultiplier : 1f);

    public static float DamageMultiplierAgainst(Pilot pilot, Fighter target)
    {
        float multiplier = pilot?.Perks.Contains(Hotshot) == true ? HotshotDamageMultiplier : 1f;
        if (pilot?.Perks.Contains(Finisher) == true && target != null &&
            target.Hp < target.MaxHp * FinisherHullThreshold)
            multiplier *= FinisherDamageMultiplier;
        return multiplier;
    }

    /// <summary>Applies traits that modify the upper bound of normal movement distance.</summary>
    public static float ApplyNormalMoveLimitModifiers(Pilot pilot, float normalMoveLimit) =>
        normalMoveLimit
        * (pilot?.Perks.Contains(Afterburner) == true ? AfterburnerNormalMoveLimitMultiplier : 1f)
        * (pilot?.Perks.Contains(EngineShy) == true ? EngineShyNormalMoveLimitMultiplier : 1f);

    /// <summary>
    /// Applies normal-flight handling traits to the maximum total turn angle.
    /// Higher values permit tighter turns; turn radius is derived from distance
    /// and turn angle, so it is intentionally not represented as a stat here.
    /// </summary>
    public static float ApplyNormalTurnLimitModifiers(Pilot pilot, float normalTurnLimitDegrees,
        bool isAtFullNormalThrottle)
    {
        float multiplier = pilot?.Perks.Contains(EngineShy) == true
            ? EngineShyNormalTurnLimitMultiplier
            : 1f;
        if (isAtFullNormalThrottle && pilot?.Perks.Contains(Reckless) == true)
            multiplier *= RecklessFullThrottleNormalTurnLimitMultiplier;
        return normalTurnLimitDegrees * multiplier;
    }

    public static float EffectiveFireRange(Pilot pilot, float baseRange, bool beforeFirstHit = true) =>
        baseRange
        * (pilot?.Perks.Contains(LongShot) == true ? 1.1f : 1f)
        * (beforeFirstHit && pilot?.Perks.Contains(Hesitant) == true ? HesitantRangeMultiplier : 1f);
}
