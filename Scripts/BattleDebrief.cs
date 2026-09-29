using Godot;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

/// <summary>Everything the debrief needs to describe a finished battle.</summary>
public class BattleReport
{
    public bool Won;
    public string Headline;
    public Color HeadlineColor;
    public CampaignMission Mission;
    public string MapName;
    public CampaignResolution Resolution;
    /// <summary>Campaign pilot outcomes; empty for quick battles.</summary>
    public List<PilotResult> Results = new();
    /// <summary>The player's fighters as they ended the battle.</summary>
    public List<Fighter> Squad = new();
    public int Turns;
}

/// <summary>
/// Portrait post-battle report: outcome, headline numbers, one card per
/// pilot, strategic results, and a fixed Continue button in thumb reach.
/// </summary>
public partial class BattleDebrief : CanvasLayer
{
    readonly BattleReport _report;

    /// <summary>Engine-required parameterless constructor; use the report overload in code.</summary>
    public BattleDebrief() { }

    public BattleDebrief(BattleReport report) => _report = report;

    bool IsQuickBattle => GameSetup.IsTestBattle;

    public override void _Ready()
    {
        Layer = 5;
        var backdrop = new ColorRect { Color = Bg };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);

        MarginContainer root = ScreenRoot(this, extraTop: 40, extraBottom: 24);
        ((CanvasLayer)root.GetParent()).Layer = Layer + 1;
        VBoxContainer page = Stack(20);
        root.AddChild(page);

        var scroll = new TouchScroll { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        page.AddChild(scroll);
        VBoxContainer content = Stack(20);
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(content);

        string place = (_report.Mission?.PlanetName ?? _report.MapName ?? "").ToUpper();
        string context = IsQuickBattle ? $"QUICK BATTLE · {place}" : $"MISSION DEBRIEF · {place}";
        content.AddChild(Text(context, FontCaption, Muted, 3));
        content.AddChild(Text(_report.Headline, _report.Headline.Length > 10 ? FontHeading : FontDisplay, _report.HeadlineColor, 6));
        content.AddChild(BuildKpis());

        content.AddChild(Text("SQUADRON", FontCaption, Muted, 4));
        if (_report.Results.Count > 0)
        {
            foreach (PilotResult result in _report.Results)
                content.AddChild(BuildPilotCard(result));
        }
        else
        {
            foreach (Fighter fighter in _report.Squad)
                content.AddChild(BuildShipCard(fighter));
        }

        if (_report.Resolution is { } resolution &&
            (resolution.ControlChanges.Count > 0 || resolution.CreditsAwarded > 0 || resolution.CapturedSystemName != null))
            content.AddChild(BuildStrategic(resolution));

        List<Pilot> decisions = IsQuickBattle
            ? new List<Pilot>()
            : PilotRoster.Living.Where(p => p.NeedsCareerChoice).ToList();
        if (decisions.Count > 0)
        {
            string label = decisions.Count == 1
                ? $"{decisions[0].Callsign.ToUpper()} HAS A DECISION WAITING"
                : $"{decisions.Count} PILOT DECISIONS WAITING";
            Button hangar = TouchButton(label + "  >", fontSize: FontCaption);
            hangar.Pressed += () => GetTree().ChangeSceneToFile(HangarNavigation.ScenePath);
            page.AddChild(hangar);
        }

        Button next = TouchButton("CONTINUE", primary: true);
        next.Pressed += () => GetTree().ChangeSceneToFile(
            IsQuickBattle ? "res://Scenes/HomeScreen.tscn" : "res://Scenes/CampaignMap.tscn");
        page.AddChild(next);
    }

    Control BuildKpis()
    {
        var grid = new GridContainer { Columns = 4, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 12);
        int kills = _report.Squad.Sum(f => f.Kills);
        int lost = _report.Squad.Count(f => !f.IsAlive);
        AddKpi(grid, kills.ToString(), "KILLS", TextBright);
        if (_report.Results.Count > 0)
            AddKpi(grid, $"+{_report.Results.Sum(r => r.XpGained)}", "SQUAD XP", TextBright);
        else
            AddKpi(grid, _report.Turns.ToString(), "TURNS", TextBright);
        int credits = _report.Resolution?.CreditsAwarded ?? 0;
        if (credits > 0)
            AddKpi(grid, $"+{credits}", "CREDITS", Positive);
        AddKpi(grid, lost.ToString(), "LOST", lost > 0 ? Negative : Muted);
        return grid;
    }

    static void AddKpi(Container parent, string value, string key, Color valueColor)
    {
        VBoxContainer box = Stack(0);
        box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        box.AddChild(Text(value, FontHeading, valueColor));
        box.AddChild(Text(key, FontMicro, Muted, 3));
        parent.AddChild(box);
    }

    static TextureRect ShipIcon(ShipType ship, bool faded)
    {
        return new TextureRect
        {
            Texture = ship.GetSkin(0).Base,
            CustomMinimumSize = new Vector2(88, 88),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Modulate = faded ? new Color(1, 1, 1, 0.4f) : Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    /// <summary>Card header: ship icon, name and class on the left, fate tag on the right.</summary>
    static HBoxContainer CardHeader(ShipType ship, string name, bool alive, string fate, ChipRole fateRole)
    {
        HBoxContainer header = Row(16);
        header.AddChild(ShipIcon(ship, !alive));
        VBoxContainer identity = Stack(4);
        identity.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        identity.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        identity.AddChild(Text(name, FontBody, alive ? TextBright : Negative, 2));
        identity.AddChild(Text(ship.DisplayName.ToUpper(), FontCaption, Muted, 2));
        header.AddChild(identity);
        header.AddChild(Tag(fate, fateRole));
        return header;
    }

    Control BuildShipCard(Fighter fighter)
    {
        PanelContainer card = Card();
        VBoxContainer stack = Stack(12);
        card.AddChild(stack);
        bool alive = fighter.IsAlive;
        stack.AddChild(CardHeader(fighter.Type, BattleManager.CallsignOf(fighter), alive,
            alive ? "RETURNED" : fighter.Ejected ? "EJECTED" : "LOST", alive ? ChipRole.Gain : fighter.Ejected ? ChipRole.Impaired : ChipRole.Loss));
        string hull = alive ? $"HULL {fighter.Hp}/{fighter.MaxHp}" : "SHOT DOWN";
        stack.AddChild(Text($"{fighter.Kills} KILLS · {fighter.HitsLanded}/{fighter.ShotsFired} HITS · {hull}", FontCaption, Body, 1));
        return card;
    }

    Control BuildPilotCard(PilotResult r)
    {
        PanelContainer card = Card();
        VBoxContainer stack = Stack(12);
        card.AddChild(stack);

        (string fate, ChipRole fateRole, string fateNote) = r switch
        {
            { Survived: false, Ejected: true } => ("KIA", ChipRole.Loss, "Ejected, but lost in enemy space."),
            { Survived: false } => ("KIA", ChipRole.Loss, "Shot down. No ejection."),
            { Ejected: true } => ("WOUNDED", ChipRole.Impaired,
                $"Ejected and {(_report.Won ? "recovered" : "escaped")}. Out for {r.Pilot.RecoveryMissionsRemaining} missions."),
            _ => ("RETURNED", ChipRole.Gain,
                r.Pilot.HullDamage > 0 ? $"{r.Pilot.HullDamage} hull damage still to repair." : "Hull fully repaired."),
        };
        stack.AddChild(CardHeader(r.Pilot.Ship, r.Pilot.Callsign.ToUpper(), r.Survived, fate, fateRole));
        stack.AddChild(Text(fateNote, FontCaption, Muted, 0, wrap: true));

        if (r.Survived)
        {
            HBoxContainer xpRow = Row(16);
            xpRow.AddChild(Text($"{r.Kills} KILLS", FontCaption, Body, 2));
            xpRow.AddChild(Text($"+{r.XpGained} XP", FontCaption, r.LevelsGained > 0 ? Positive : Body, 2));
            int xpToNext = PilotRoster.XpToNext(r.Pilot.Level);
            xpRow.AddChild(new XpTrack
            {
                Ratio = xpToNext > 0 ? (float)r.Pilot.Xp / xpToNext : 1f,
                CustomMinimumSize = new Vector2(0, 12),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            stack.AddChild(xpRow);

            var advancement = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            advancement.AddThemeConstantOverride("h_separation", 8);
            advancement.AddThemeConstantOverride("v_separation", 8);
            if (r.LevelsGained > 0)
                advancement.AddChild(Tag($"LEVEL {r.Pilot.Level}", ChipRole.Gain));
            if (r.ManeuverSlotsGained > 0)
                advancement.AddChild(Tag($"+{r.ManeuverSlotsGained} MANEUVER SLOT", ChipRole.Gain));
            if (advancement.GetChildCount() > 0)
                stack.AddChild(advancement);
            if (r.NewPerk != null)
            {
                stack.AddChild(Tag(r.NewPerk.Name.ToUpper(), r.NewPerk.Positive ? ChipRole.Gain : ChipRole.Impaired));
                stack.AddChild(Text(r.NewPerk.Description, FontCaption, Muted, 0, wrap: true));
            }
        }
        return card;
    }

    static Control BuildStrategic(CampaignResolution resolution)
    {
        VBoxContainer stack = Stack(8);
        stack.AddChild(Text("STRATEGIC", FontCaption, Muted, 4));
        foreach (ControlChange change in resolution.ControlChanges)
        {
            bool secured = change.After == PlanetControl.Alliance;
            stack.AddChild(Text($"{change.PlanetName.ToUpper()} NOW {change.After.ToString().ToUpper()}", FontBody, secured ? Positive : Negative, 1));
        }
        if (resolution.CreditsAwarded > 0)
            stack.AddChild(Text($"+{resolution.CreditsAwarded} CREDITS", FontBody, Positive, 1));
        if (resolution.CapturedSystemName != null)
            stack.AddChild(Text($"{resolution.CapturedSystemName.ToUpper()} CAPTURED · +{CampaignData.SystemCaptureCredits} CREDITS", FontBody, Positive, 1));
        return stack;
    }
}
