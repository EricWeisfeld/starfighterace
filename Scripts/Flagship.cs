using Godot;
using System.Linq;
using static SignalUi;

/// <summary>Routes the Flagship deck plan back to whichever campaign screen opened it.</summary>
public static class FlagshipNavigation
{
    public const string ScenePath = "res://Scenes/Flagship.tscn";
    public static string ReturnScene { get; private set; } = "res://Scenes/CampaignMap.tscn";

    public static void Open(SceneTree tree, string returnScene)
    {
        ReturnScene = returnScene;
        tree.ChangeSceneToFile(ScenePath);
    }
}

/// <summary>One boardable compartment of the Athena.</summary>
public record AthenaDeck(string Code, string Name, string ScenePath);

/// <summary>Shared deck registry plus the common sub-scene header chrome.</summary>
public static class AthenaDecks
{
    public const int Hangar = 0;
    public const int CrewQuarters = 1;
    public const int Shipyard = 2;
    public const int Memorial = 3;

    public static readonly AthenaDeck[] All =
    {
        new("01", "HANGAR BAY", "res://Scenes/Hangar.tscn"),
        new("02", "TRAINING GROUNDS", "res://Scenes/Recruitment.tscn"),
        new("03", "SHIPYARD", "res://Scenes/Shipyard.tscn"),
        new("04", "MEMORIAL HALL", "res://Scenes/Memorial.tscn"),
    };

    /// <summary>
    /// Standard sub-scene header: title, meta label, header status slot, mini
    /// deck locator, hairline and the return-to-deck-plan button.
    /// Returns the status label for the scene to fill.
    /// </summary>
    public static Label AddHeader(Node scene, CanvasLayer ui, int deck, string title, string meta)
    {
        var head = new HBoxContainer { Position = new Vector2(56, 24), Size = new Vector2(1040, 66) };
        head.AddThemeConstantOverride("separation", 24);
        ui.AddChild(head);
        Label titleLabel = Text(title, 34, TextBright, 6);
        titleLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        head.AddChild(titleLabel);
        var metaBox = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        metaBox.AddThemeConstantOverride("margin_bottom", 8);
        metaBox.AddChild(Text($"ATHENA · DECK {All[deck].Code} · {meta}", 9, Muted, 4));
        head.AddChild(metaBox);
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var statusBox = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        statusBox.AddThemeConstantOverride("margin_bottom", 9);
        Label status = Text("", 10, Muted, 3);
        statusBox.AddChild(status);
        head.AddChild(statusBox);
        var locator = new DeckSchematic
        {
            Mini = true,
            Highlight = deck,
            CustomMinimumSize = new Vector2(140, 42),
            SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
            TooltipText = $"ATHENA · {All[deck].Name}",
        };
        head.AddChild(locator);
        ui.AddChild(new ColorRect { Position = new Vector2(56, 100), Size = new Vector2(1040, 1), Color = Hairline });

        Button back = FlatButton("<  DECK PLAN", 10);
        back.Position = new Vector2(56, 596);
        back.Size = new Vector2(210, 32);
        back.Pressed += () => ((Node2D)scene).GetTree().ChangeSceneToFile(FlagshipNavigation.ScenePath);
        ui.AddChild(back);
        return status;
    }
}

/// <summary>
/// Flagship home screen: a hairline deck-plan cutaway of the Athena. Hovering a
/// compartment fills the right rail with its readout; clicking boards it.
/// </summary>
public partial class Flagship : Node2D
{
    const float ScreenW = 1152f;
    const float ScreenH = 648f;

    CanvasLayer _ui;
    DeckSchematic _schematic;
    Label _deckHeading;
    Label _readoutNum;
    Label _readoutUnit;
    StatusDot _statusDot;
    Label _statusText;
    Label _shipStatus;
    Button _board;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Bg);
        BuildUi();
        UpdateRail(AthenaDecks.Hangar);
        GetViewport().SizeChanged += FitUiToViewport;
    }

    void BuildUi()
    {
        var ui = new CanvasLayer();
        _ui = ui;
        AddChild(ui);

        var head = new HBoxContainer { Position = new Vector2(56, 24), Size = new Vector2(1040, 66) };
        head.AddThemeConstantOverride("separation", 24);
        ui.AddChild(head);
        Label title = Text("ATHENA", 34, TextBright, 6);
        title.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        head.AddChild(title);
        var metaBox = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        metaBox.AddThemeConstantOverride("margin_bottom", 8);
        metaBox.AddChild(Text("DECK PLAN · SELECT A COMPARTMENT", 9, Muted, 4));
        head.AddChild(metaBox);
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var credits = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        credits.AddThemeConstantOverride("separation", 0);
        Label creditsKey = Text("CREDITS", 9, Muted, 4);
        creditsKey.HorizontalAlignment = HorizontalAlignment.Right;
        creditsKey.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        credits.AddChild(creditsKey);
        Label creditsValue = Text(CampaignData.Credits.ToString(), 24, TextBright, 2);
        creditsValue.HorizontalAlignment = HorizontalAlignment.Right;
        creditsValue.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        credits.AddChild(creditsValue);
        head.AddChild(credits);
        ui.AddChild(new ColorRect { Position = new Vector2(56, 100), Size = new Vector2(1040, 1), Color = Hairline });

        // Ship-wide signals: anything that needs the player, surfaced at the top
        // of the hierarchy so a pending decision is visible from the deck plan.
        var signals = new HBoxContainer { Position = new Vector2(56, 108) };
        signals.AddThemeConstantOverride("separation", 8);
        ui.AddChild(signals);
        int decisions = PilotRoster.Living.Count(p => p.NeedsCareerChoice);
        int woundedPilots = PilotRoster.Living.Count(p => p.Condition == PilotCondition.Wounded);
        if (decisions > 0)
            signals.AddChild(Chip(decisions == 1
                ? "1 PILOT DECISION READY · HANGAR BAY"
                : $"{decisions} PILOT DECISIONS READY · HANGAR BAY", ChipRole.Decision));
        if (woundedPilots > 0)
            signals.AddChild(Chip($"{woundedPilots} WOUNDED", ChipRole.Impaired));

        _schematic = new DeckSchematic
        {
            Position = new Vector2(40, 136),
            Size = new Vector2(710, 336),
            Highlight = AthenaDecks.Hangar,
            Labels = AthenaDecks.All.Select(d => $"{d.Code} {d.Name}").ToArray(),
        };
        _schematic.DeckHovered += UpdateRail;
        _schematic.DeckSelected += Board;
        ui.AddChild(_schematic);

        var rail = new VBoxContainer { Position = new Vector2(812, 126), Size = new Vector2(284, 440) };
        rail.AddThemeConstantOverride("separation", 8);
        ui.AddChild(rail);
        _deckHeading = SectionLabel("");
        rail.AddChild(_deckHeading);
        var readout = new HBoxContainer();
        readout.AddThemeConstantOverride("separation", 10);
        rail.AddChild(readout);
        _readoutNum = Text("", 38, TextBright, 2);
        readout.AddChild(_readoutNum);
        _readoutUnit = Text("", 10, Muted, 3);
        _readoutUnit.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        var unitBox = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        unitBox.AddThemeConstantOverride("margin_bottom", 10);
        unitBox.AddChild(_readoutUnit);
        readout.AddChild(unitBox);
        var statusRow = new HBoxContainer();
        statusRow.AddThemeConstantOverride("separation", 7);
        rail.AddChild(statusRow);
        _statusDot = new StatusDot { DotColor = Positive, CustomMinimumSize = new Vector2(9, 9), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        statusRow.AddChild(_statusDot);
        _statusText = Text("", 9, Positive, 2);
        statusRow.AddChild(_statusText);

        rail.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        rail.AddChild(new ColorRect { CustomMinimumSize = new Vector2(0, 1), Color = Hairline });
        rail.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        rail.AddChild(SectionLabel("SHIP STATUS"));
        _shipStatus = Text("", 10, Muted, 0, wrap: true);
        rail.AddChild(_shipStatus);
        rail.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        _board = FlatButton("", 10);
        _board.CustomMinimumSize = new Vector2(0, 36);
        _board.Pressed += () => Board(_schematic.Highlight);
        rail.AddChild(_board);

        Button back = FlatButton("<  RETURN", 10);
        back.Position = new Vector2(56, 596);
        back.Size = new Vector2(210, 32);
        back.Pressed += () => GetTree().ChangeSceneToFile(FlagshipNavigation.ReturnScene);
        ui.AddChild(back);
        FitUiToViewport();
    }

    void UpdateRail(int deck)
    {
        Pilot[] living = PilotRoster.Living.ToArray();
        int ready = living.Count(p => p.Condition == PilotCondition.Ready);
        int wounded = living.Count(p => p.Condition == PilotCondition.Wounded);
        int fallen = PilotRoster.Pilots.Count(p => !p.Alive);
        AthenaDeck info = AthenaDecks.All[deck];
        _deckHeading.Text = $"{info.Code} · {info.Name}";

        switch (deck)
        {
            case AthenaDecks.Hangar:
                _readoutNum.Text = $"{living.Length:00}";
                _readoutUnit.Text = "PILOTS";
                SetStatus(wounded > 0 ? Warning : Positive, $"READY {ready:00} · WOUNDED {wounded:00}");
                break;
            case AthenaDecks.CrewQuarters:
                _readoutNum.Text = $"{living.Length}/{Hangar.RosterCapacity}";
                _readoutUnit.Text = "ROSTER";
                bool full = living.Length >= Hangar.RosterCapacity;
                SetStatus(full ? Warning : Positive, full ? "ROSTER FULL" : $"{ShipTypes.RecruitableClasses.Length} CLASSES RECRUITING");
                break;
            case AthenaDecks.Shipyard:
                _readoutNum.Text = CampaignData.Credits.ToString();
                _readoutUnit.Text = "CREDITS";
                int[] levels = { CampaignData.LogisticsLevel, CampaignData.OperationsLevel, CampaignData.SquadronLevel };
                bool maxed = levels.All(level => level >= 3);
                int cheapest = levels.Where(level => level < 3).Select(CampaignData.UpgradeCost).DefaultIfEmpty(0).Min();
                if (maxed)
                    SetStatus(Dim, "ALL BRANCHES AT MAXIMUM");
                else if (CampaignData.Credits >= cheapest)
                    SetStatus(Positive, $"UPGRADE AVAILABLE · {cheapest} CR");
                else
                    SetStatus(Muted, $"NEXT UPGRADE · {cheapest} CR");
                break;
            default:
                _readoutNum.Text = $"{fallen:00}";
                _readoutUnit.Text = "FALLEN";
                SetStatus(fallen > 0 ? Negative : Dim, fallen > 0 ? "IN MEMORIAM" : "NO PILOTS LOST");
                break;
        }

        _shipStatus.Text = $"ROSTER {living.Length}/{Hangar.RosterCapacity}\n" +
            $"LOGISTICS L{CampaignData.LogisticsLevel} · OPERATIONS L{CampaignData.OperationsLevel} · SQUADRON L{CampaignData.SquadronLevel}\n" +
            $"SYSTEM CAPTURE PAYOUT +{CampaignData.SystemCaptureCredits} CR";
        _board.Text = $"BOARD {info.Name}";
    }

    void SetStatus(Color color, string text)
    {
        _statusDot.DotColor = color;
        _statusDot.QueueRedraw();
        _statusText.Text = text;
        _statusText.AddThemeColorOverride("font_color", color);
    }

    void Board(int deck) => GetTree().ChangeSceneToFile(AthenaDecks.All[deck].ScenePath);

    void FitUiToViewport()
    {
        if (_ui == null)
            return;
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float scale = Mathf.Max(0.01f, Mathf.Min(viewport.X / ScreenW, viewport.Y / ScreenH));
        _ui.Scale = Vector2.One * scale;
        _ui.Offset = (viewport - new Vector2(ScreenW, ScreenH) * scale) * 0.5f;
    }

    public override void _Draw()
    {
        DrawStarfield(this, 4211, ScreenW, ScreenH);
        DrawNebula(this, new Vector2(210, 540), new Color(0.25f, 0.59f, 1f), 12, 20);
    }
}
