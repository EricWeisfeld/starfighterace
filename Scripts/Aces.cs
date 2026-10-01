using Godot;

/// <summary>
/// Enemy aces: individual pilots who make a fight harder, and the biggest
/// threat on the field. One flies with every elite wing and every sector
/// boss (two with the last boss). An ace's hull is refitted one step ahead
/// of your squadron, it aims and dodges better, flies its line's first two
/// maneuvers mastered, and plans against the orders you actually gave.
/// Aces are painted black and gold.
/// </summary>
public static class Aces
{
    public const float AccuracyBonus = 0.10f;
    public const float EvasionBonus = 0.10f;

    /// <summary>Ace callsigns. None of them is a callsign your own pilots use.</summary>
    public static readonly string[] Callsigns =
    {
        "VEX", "KORR", "SHRIKE", "MALICE", "NOX", "THORN",
        "SKARN", "VANTA", "HALBERD", "MORDANT", "CINDER", "KESH",
    };

    /// <summary>How many aces a battle stop brings.</summary>
    public static int CountFor(RunNodeKind kind, int sector) => kind switch
    {
        RunNodeKind.Elite => 1,
        RunNodeKind.Boss => sector >= 3 ? 2 : 1,
        _ => 0,
    };

    /// <summary>Refits on an ace's hull: Mk II in sector 1, Mk III in sector 2, Mk IV in sector 3.</summary>
    public static int RefitsFor(int sector) => Mathf.Clamp(sector, 1, 3);
}
