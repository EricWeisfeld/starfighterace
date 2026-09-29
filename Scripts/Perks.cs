using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A pilot trait. Positive traits are instincts: one per pilot, picked when
/// they join, and meant to shape how that pilot flies. The rest are scars,
/// picked up from mishaps in battle and treated at repair docks. Both only
/// take effect in a situation the pilot flies into, never as a flat stat
/// change: flat numbers belong to the ship.
/// </summary>
public class Perk
{
    public string Id;
    public string Name;
    public string Description;
    /// <summary>For scars: what in a battle risks this scar.</summary>
    public string EarnHint;
    public bool Positive;

    public bool IsScar => !Positive;
    public string KindLabel => Positive ? "INSTINCT" : "SCAR";
}

public static class Perks
{
    /// <summary>Chance a scar sticks after a battle it was risked in.</summary>
    public const float ScarChanceAfterWin = 0.2f;
    public const float ScarChanceAfterLoss = 0.4f;
    public const float SurvivorsGuiltSecondDeathChanceBonus = 0.20f;

    public const float BaseEjectChance = 0.6f;

    public const float StalkerEvasionBonus = 0.20f;
    public const float CoolUnderFireAccuracyBonus = 0.20f;
    public const float CoolUnderFireThreshold = 0.50f;
    public const float FinisherDamageMultiplier = 1.25f;
    public const float FinisherHullThreshold = 0.40f;
    public const float TailGunnerAccuracyBonus = 0.20f;
    /// <summary>How far off the target's tail the shooter can be and still count as behind it.</summary>
    public const float TailGunnerRearAngleDegrees = 60f;
    public const float LongShotDamageMultiplier = 1.35f;
    /// <summary>Hits from beyond this fraction of weapon range count as long shots.</summary>
    public const float LongShotRangeFraction = 2f / 3f;
    public const int BrawlerExtraShots = 2;
    /// <summary>Volleys from within this fraction of weapon range count as close in.</summary>
    public const float BrawlerRangeFraction = 1f / 3f;
    public const int SteadyShieldRegenBonus = 2;
    public const float DaredevilEvasionBonus = 0.15f;

    public const float HesitantRangeMultiplier = 0.80f;
    public const float SurvivorsGuiltCombatPenalty = 0.15f;
    public const int GunShyLostShots = 2;
    public const float GunShyOpeningDamageThreshold = 0.60f;
    public const float RattledEvasionPenalty = 0.15f;
    public const float RattledThreshold = 0.50f;
    public const float TunnelVisionAccuracyPenalty = 0.15f;
    public const int TunnelVisionMinimumShots = 8;
    public const float TunnelVisionMaximumAccuracy = 0.35f;
    public const float EngineShyFullThrottleTurnMultiplier = 0.75f;
    /// <summary>Hull fraction below which a pilot has been in real trouble; risks Rattled.</summary>
    public const float LowHullThreshold = 0.25f;

    // ------------------------------------------------------------ instincts

    public static readonly Perk Stalker = new()
    {
        Id = "stalker", Name = "Stalker", Positive = true,
        Description = $"+{StalkerEvasionBonus * 100:0}% evasion each turn until you open fire.",
    };
    public static readonly Perk CoolUnderFire = new()
    {
        Id = "cool-under-fire", Name = "Cool Under Fire", Positive = true,
        Description = $"+{CoolUnderFireAccuracyBonus * 100:0}% accuracy while below half hull.",
    };
    public static readonly Perk Finisher = new()
    {
        Id = "finisher", Name = "Finisher", Positive = true,
        Description = $"+{(FinisherDamageMultiplier - 1f) * 100:0}% damage against enemies below {FinisherHullThreshold * 100:0}% hull.",
    };
    public static readonly Perk SecondChance = new()
    {
        Id = "second-chance", Name = "Second Chance", Positive = true,
        Description = "Once per battle, a shot that would destroy you leaves you at 1 hull, and no shot can finish you for the rest of that turn.",
    };
    public static readonly Perk TailGunner = new()
    {
        Id = "tail-gunner", Name = "Tail Gunner", Positive = true,
        Description = $"+{TailGunnerAccuracyBonus * 100:0}% accuracy against a target flying away from you.",
    };
    public static readonly Perk LongShot = new()
    {
        Id = "long-shot", Name = "Long Shot", Positive = true,
        Description = $"Hits from the outer third of your range deal +{(LongShotDamageMultiplier - 1f) * 100:0}% damage.",
    };
    public static readonly Perk Ace = new()
    {
        Id = "ace", Name = "Ace", Positive = true,
        Description = "A kill resets all your maneuver cooldowns and refills your shields.",
    };
    public static readonly Perk Brawler = new()
    {
        Id = "brawler", Name = "Brawler", Positive = true,
        Description = $"Volleys fired from the inner third of your range have +{BrawlerExtraShots} shots.",
    };
    public static readonly Perk Steady = new()
    {
        Id = "steady", Name = "Steady", Positive = true,
        Description = $"+{SteadyShieldRegenBonus} shield regeneration after a turn flown without a special maneuver.",
    };
    public static readonly Perk Daredevil = new()
    {
        Id = "daredevil", Name = "Daredevil", Positive = true,
        Description = $"+{DaredevilEvasionBonus * 100:0}% evasion on turns flown at full throttle or faster.",
    };

    // ---------------------------------------------------------------- scars

    public static readonly Perk Hesitant = new()
    {
        Id = "hesitant", Name = "Hesitant", Positive = false,
        Description = $"-{(1f - HesitantRangeMultiplier) * 100:0}% weapon range until landing a hit each battle.",
        EarnHint = "Risked by winning a battle without landing a hit.",
    };
    public static readonly Perk SurvivorsGuilt = new()
    {
        Id = "survivors-guilt", Name = "Survivor's Guilt", Positive = false,
        Description = $"-{SurvivorsGuiltCombatPenalty * 100:0}% accuracy and evasion once a squadmate is shot down.",
        EarnHint = "Risked by coming home when a squadmate does not.",
    };
    public static readonly Perk GunShy = new()
    {
        Id = "gun-shy", Name = "Gun Shy", Positive = false,
        Description = $"Volleys are {GunShyLostShots} shots shorter on a turn you have taken hull damage.",
        EarnHint = "Risked by being shot down, or by a brutal first exchange.",
    };
    public static readonly Perk Rattled = new()
    {
        Id = "rattled", Name = "Rattled", Positive = false,
        Description = $"-{RattledEvasionPenalty * 100:0}% evasion while below half hull.",
        EarnHint = "Risked by being shot down, or by limping home below a quarter hull.",
    };
    public static readonly Perk TunnelVision = new()
    {
        Id = "tunnel-vision", Name = "Tunnel Vision", Positive = false,
        Description = $"-{TunnelVisionAccuracyPenalty * 100:0}% accuracy when switching to a new target.",
        EarnHint = "Risked by missing most of your shots in a battle.",
    };
    public static readonly Perk EngineShy = new()
    {
        Id = "engine-shy", Name = "Engine Shy", Positive = false,
        Description = $"Turns {(1f - EngineShyFullThrottleTurnMultiplier) * 100:0}% less sharply at full throttle.",
        EarnHint = "Risked by hitting an asteroid.",
    };

    public static readonly Perk[] Instincts = { Stalker, CoolUnderFire, Finisher, SecondChance, TailGunner, LongShot, Ace, Brawler, Steady, Daredevil };
    public static readonly Perk[] Scars = { Hesitant, SurvivorsGuilt, GunShy, Rattled, TunnelVision, EngineShy };
    public static readonly Perk[] All = Instincts.Concat(Scars).ToArray();

    /// <summary>
    /// Instincts that were reworked or retired. Saves may still name the old
    /// id, which loads as its replacement or the closest current instinct.
    /// </summary>
    static readonly Dictionary<string, string> RenamedIds = new()
    {
        ["phantom"] = "stalker",
        ["trigger-happy"] = "brawler",
        ["survivor"] = "second-chance",
        ["wingman"] = "daredevil",
    };

    public static Perk ById(string id) =>
        All.FirstOrDefault(perk => perk.Id == id) ??
        (id != null && RenamedIds.TryGetValue(id, out string renamed) ? ById(renamed) : null);

    /// <summary>Scars this battle record risks.</summary>
    public static List<Perk> EligibleScars(Fighter f, bool won, int squadmateDeaths = 0)
    {
        var list = new List<Perk>();
        if (f.Ejected)
        {
            list.Add(GunShy);
            list.Add(Rattled);
        }
        if (f.IsAlive && f.DroppedBelowQuarterHull)
            list.Add(Rattled);
        if (f.TurnOneDamage > f.MaxHp * GunShyOpeningDamageThreshold)
            list.Add(GunShy);
        if (f.ShotsFired >= TunnelVisionMinimumShots && f.HitsLanded / (float)f.ShotsFired <= TunnelVisionMaximumAccuracy)
            list.Add(TunnelVision);
        if (f.HitAsteroid)
            list.Add(EngineShy);
        if (won && f.HitsLanded == 0)
            list.Add(Hesitant);
        if (squadmateDeaths > 0)
            list.Add(SurvivorsGuilt);
        return list.Distinct().Where(p => !f.Pilot.Perks.Contains(p)).ToList();
    }

    /// <summary>
    /// Post-battle scar check for one surviving pilot: at most one scar, and
    /// usually none. Instincts are never rolled here; they come from level-ups.
    /// </summary>
    public static Perk RollScar(Fighter f, bool won, int squadDeaths = 0)
    {
        float chance = won ? ScarChanceAfterWin : ScarChanceAfterLoss;
        List<Perk> scars = EligibleScars(f, won, squadDeaths);
        if (scars.Remove(SurvivorsGuilt))
        {
            float guiltChance = chance + (squadDeaths >= 2 ? SurvivorsGuiltSecondDeathChanceBonus : 0f);
            if (GD.Randf() < guiltChance)
                return Learn(f, SurvivorsGuilt);
        }
        if (scars.Count > 0 && GD.Randf() < chance)
            return Learn(f, scars[GD.RandRange(0, scars.Count - 1)]);
        return null;
    }

    static Perk Learn(Fighter f, Perk perk)
    {
        f.Pilot.Perks.Add(perk);
        return perk;
    }
}
