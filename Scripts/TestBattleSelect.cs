using Godot;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

/// <summary>
/// What a quick battle stages: your ships and their level, the enemy wing,
/// the battlefield and the opening. Kept for the whole session, so a rematch
/// or a change to one setting doesn't mean building the fight again.
/// </summary>
public static class QuickBattleSetup
{
    public const int SquadSize = 3;
    /// <summary>Every opening places up to six enemy ships.</summary>
    public const int WingSize = 6;

    public static readonly List<ShipType> Squad = new() { ShipTypes.Scout, ShipTypes.Raptor, ShipTypes.Zt };
    public static readonly List<ShipType> Wing = new() { ShipTypes.Scout, ShipTypes.Raptor, ShipTypes.Zt };
    public static int Level = Pilot.MaxLevel;
    public static int MapIndex;
    public static BattleOpening Opening = BattleOpening.HeadOn;
    /// <summary>The wing's first ship flies as an ace, refitted a step ahead of your squadron.</summary>
    public static bool AceLeads;

    /// <summary>Openings a quick battle can stage: all but the ambush, which needs reinforcements.</summary>
    public static readonly BattleOpening[] Openings =
    {
        BattleOpening.HeadOn, BattleOpening.LongApproach, BattleOpening.Flanked,
        BattleOpening.Pincer, BattleOpening.RunningFight, BattleOpening.Bounced,
    };

    public static bool CanLaunch => Squad.Count > 0 && Wing.Count > 0;

    /// <summary>Sets up the battle scene for this fight. One-sided openings come from a random side.</summary>
    public static void Start()
    {
        BattleMapDefinition map = BattleMaps.All[MapIndex];
        var forces = new BattleMission
        {
            Name = "QUICK BATTLE",
            MapId = map.Id,
            EnemySquad = Wing.ToArray(),
            EnemyManeuvers = Pilot.MaxManeuvers,
            Aces = AceLeads ? new[] { Aces.Callsigns[GD.RandRange(0, Aces.Callsigns.Length - 1)] } : System.Array.Empty<string>(),
            AceRefits = Mathf.Min(3, Refits.TierFor(Level) + 1),
            Opening = Opening,
            OpeningMirrored = GD.Randf() < 0.5f,
        };
        GameSetup.StartQuickBattle(Squad, Level, map, forces);
    }
}

/// <summary>
/// Quick battle setup, laid out for a portrait phone: your ships and their
/// level, the enemy wing, the battlefield, the opening, and the experimental
/// chase rules. Pilots are disposable sandbox pilots and never touch run data.
/// </summary>
public partial class TestBattleSelect : Node2D
{
    readonly Button[] _squadSlots = new Button[QuickBattleSetup.SquadSize];
    readonly Button[] _wingSlots = new Button[QuickBattleSetup.WingSize];
    readonly List<Button> _squadAdds = new(), _wingAdds = new();
    readonly List<Button> _levelButtons = new(), _mapButtons = new(), _openingButtons = new();
    readonly Dictionary<Experiments.Rule, Button> _ruleButtons = new();
    Label _squadLabel, _wingLabel, _levelLabel, _mapBriefing, _openingSummary;
    Button _ace, _launch;

    static readonly ShipType[] Lines = { ShipTypes.Scout, ShipTypes.Raptor, ShipTypes.Zt };

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Bg);
        AddChild(SpaceBackdrop.ForMenu(SpaceBackdrop.QuickBattle));
        BuildUi();
        Refresh();
    }

    void BuildUi()
    {
        MarginContainer root = ScreenRoot(this, extraTop: 32, extraBottom: 32);
        VBoxContainer page = Stack(18);
        root.AddChild(page);

        HBoxContainer header = Row(16);
        Button back = TouchButton("<", fontSize: FontTitle);
        back.CustomMinimumSize = new Vector2(TouchTarget, TouchTarget);
        back.Pressed += () => ChangeScene(this, "res://Scenes/HomeScreen.tscn");
        header.AddChild(back);
        VBoxContainer titleBlock = Stack(0);
        titleBlock.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        titleBlock.AddChild(Text("QUICK BATTLE", FontTitle, TextBright, 4));
        titleBlock.AddChild(Text("SET UP ANY FIGHT", FontCaption, Muted, 2));
        header.AddChild(titleBlock);
        page.AddChild(header);

        var scroll = new TouchScroll { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        page.AddChild(scroll);
        VBoxContainer body = Stack(14);
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(body);

        // Your squadron.
        _squadLabel = Text("", FontCaption, Muted, 4);
        body.AddChild(_squadLabel);
        body.AddChild(SlotGrid(_squadSlots, 3, slot => Remove(QuickBattleSetup.Squad, slot)));
        body.AddChild(AddRow(_squadAdds, ship => Add(QuickBattleSetup.Squad, QuickBattleSetup.SquadSize, ship)));
        _levelLabel = Text("", FontCaption, Muted, 4);
        body.AddChild(_levelLabel);
        HBoxContainer levels = Row(10);
        for (int level = 1; level <= Pilot.MaxLevel; level++)
        {
            int chosen = level;
            Button button = SelectableButton(level.ToString());
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.Pressed += () =>
            {
                QuickBattleSetup.Level = chosen;
                Refresh();
            };
            _levelButtons.Add(button);
            levels.AddChild(button);
        }
        body.AddChild(levels);
        body.AddChild(Text("Tap a ship to remove it. Level 1 pilots know their signature maneuver; from level 2, the first two of their line.",
            FontMicro, Muted, 0, wrap: true));

        // The enemy wing.
        body.AddChild(Spacer(10));
        _wingLabel = Text("", FontCaption, Muted, 4);
        body.AddChild(_wingLabel);
        body.AddChild(SlotGrid(_wingSlots, 3, slot => Remove(QuickBattleSetup.Wing, slot)));
        body.AddChild(AddRow(_wingAdds, ship => Add(QuickBattleSetup.Wing, QuickBattleSetup.WingSize, ship)));
        _ace = SelectableButton("");
        _ace.Pressed += () =>
        {
            QuickBattleSetup.AceLeads = !QuickBattleSetup.AceLeads;
            Refresh();
        };
        body.AddChild(_ace);
        body.AddChild(Text("Tap a ship to remove it. Enemies fly the first two maneuvers of their line, at base numbers. An ace is refitted a step ahead of your squadron.",
            FontMicro, Muted, 0, wrap: true));

        // Battlefield.
        body.AddChild(Spacer(10));
        body.AddChild(Text("BATTLEFIELD", FontCaption, Muted, 4));
        body.AddChild(ChoiceGrid(_mapButtons, BattleMaps.All.Select(m => m.DisplayName), 3, index =>
        {
            QuickBattleSetup.MapIndex = index;
            Refresh();
        }));
        _mapBriefing = Text("", FontCaption, Body, 0, wrap: true);
        body.AddChild(_mapBriefing);

        // Opening.
        body.AddChild(Spacer(10));
        body.AddChild(Text("OPENING", FontCaption, Muted, 4));
        body.AddChild(ChoiceGrid(_openingButtons, QuickBattleSetup.Openings.Select(BattleOpenings.Name), 2, index =>
        {
            QuickBattleSetup.Opening = QuickBattleSetup.Openings[index];
            Refresh();
        }));
        _openingSummary = Text("", FontCaption, Body, 0, wrap: true);
        body.AddChild(_openingSummary);

        // Experimental chase rules.
        body.AddChild(Spacer(10));
        body.AddChild(Text("EXPERIMENTS", FontCaption, Muted, 4));
        body.AddChild(Text("Rules for following an enemy, to try out. Each is saved on this device and applies to runs too while it's on.",
            FontMicro, Muted, 0, wrap: true));
        foreach (Experiments.Rule rule in Experiments.All)
        {
            Button toggle = SelectableButton("");
            toggle.Pressed += () =>
            {
                Experiments.Set(rule, !Experiments.IsOn(rule));
                Refresh();
            };
            _ruleButtons[rule] = toggle;
            body.AddChild(toggle);
            body.AddChild(Text(Experiments.Description(rule), FontMicro, Body, 0, wrap: true));
        }
        body.AddChild(Spacer(8));

        _launch = TouchButton("LAUNCH", primary: true);
        _launch.Pressed += Launch;
        page.AddChild(_launch);
    }

    static Control Spacer(float height) =>
        new Control { CustomMinimumSize = new Vector2(0, height), MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>A grid of ship slots; tapping a filled slot empties it.</summary>
    static GridContainer SlotGrid(Button[] slots, int columns, System.Action<int> remove)
    {
        var grid = new GridContainer { Columns = columns, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        for (int i = 0; i < slots.Length; i++)
        {
            int slot = i;
            Button button = SelectableButton("");
            button.CustomMinimumSize = new Vector2(0, 120);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.IconAlignment = HorizontalAlignment.Center;
            button.VerticalIconAlignment = VerticalAlignment.Top;
            button.ExpandIcon = true;
            button.AddThemeFontSizeOverride("font_size", FontMicro);
            button.Pressed += () => remove(slot);
            slots[i] = button;
            grid.AddChild(button);
        }
        return grid;
    }

    /// <summary>One add button per class line.</summary>
    static HBoxContainer AddRow(List<Button> buttons, System.Action<ShipType> add)
    {
        HBoxContainer row = Row(12);
        foreach (ShipType ship in Lines)
        {
            Button button = TouchButton($"+ {ShortName(ship)}", fontSize: FontCaption);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.Pressed += () => add(ship);
            buttons.Add(button);
            row.AddChild(button);
        }
        return row;
    }

    /// <summary>A grid of choices; exactly one is lit (see <see cref="Refresh"/>).</summary>
    static GridContainer ChoiceGrid(List<Button> buttons, IEnumerable<string> names, int columns, System.Action<int> choose)
    {
        var grid = new GridContainer { Columns = columns, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        int index = 0;
        foreach (string name in names)
        {
            int chosen = index++;
            Button button = SelectableButton(name);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.AddThemeFontSizeOverride("font_size", FontMicro);
            button.ClipText = true;
            button.Pressed += () => choose(chosen);
            buttons.Add(button);
            grid.AddChild(button);
        }
        return grid;
    }

    /// <summary>Outlined button whose border lights up when selected (see <see cref="SetSelected"/>).</summary>
    static Button SelectableButton(string text)
    {
        Button button = TouchButton(text, fontSize: FontCaption);
        SetSelected(button, false);
        return button;
    }

    static void SetSelected(Button button, bool selected)
    {
        Color border = selected ? Accent : Hairline;
        Color fill = selected ? new Color(Accent, 0.14f) : new Color(CellBg, 0.9f);
        StyleBoxFlat style = Box(fill, border, selected ? 3 : 2, 12, 8);
        button.AddThemeStyleboxOverride("normal", style);
        button.AddThemeStyleboxOverride("hover", style);
        button.AddThemeStyleboxOverride("pressed", Box(new Color(Accent, 0.22f), Accent, 3, 12, 8));
        button.AddThemeColorOverride("font_color", selected ? TextBright : Body);
        button.AddThemeColorOverride("font_hover_color", selected ? TextBright : Body);
    }

    void Add(List<ShipType> side, int size, ShipType ship)
    {
        if (side.Count >= size)
            return;
        side.Add(ship);
        Refresh();
    }

    void Remove(List<ShipType> side, int slot)
    {
        if (slot >= side.Count)
            return;
        side.RemoveAt(slot);
        Refresh();
    }

    void Refresh()
    {
        var squad = QuickBattleSetup.Squad;
        var wing = QuickBattleSetup.Wing;
        _squadLabel.Text = $"YOUR SQUADRON · {squad.Count}/{QuickBattleSetup.SquadSize}";
        FillSlots(_squadSlots, squad, team: 0, label: (_, ship) => ShortName(ship));
        foreach (Button add in _squadAdds)
            add.Disabled = squad.Count >= QuickBattleSetup.SquadSize;
        int refit = Refits.TierFor(QuickBattleSetup.Level);
        _levelLabel.Text = $"PILOT LEVEL · LV {QuickBattleSetup.Level} · {Refits.Name(refit)}";
        for (int i = 0; i < _levelButtons.Count; i++)
            SetSelected(_levelButtons[i], i + 1 == QuickBattleSetup.Level);

        _wingLabel.Text = $"ENEMY WING · {wing.Count}/{QuickBattleSetup.WingSize}";
        FillSlots(_wingSlots, wing, team: 1,
            label: (slot, ship) => slot == 0 && QuickBattleSetup.AceLeads ? $"ACE {ShortName(ship)}" : ShortName(ship));
        foreach (Button add in _wingAdds)
            add.Disabled = wing.Count >= QuickBattleSetup.WingSize;
        _ace.Text = QuickBattleSetup.AceLeads
            ? $"ACE LEADS THE WING · ON · {Refits.Name(Mathf.Min(3, refit + 1))}"
            : "ACE LEADS THE WING · OFF";
        SetSelected(_ace, QuickBattleSetup.AceLeads);

        for (int i = 0; i < _mapButtons.Count; i++)
            SetSelected(_mapButtons[i], i == QuickBattleSetup.MapIndex);
        _mapBriefing.Text = BattleMaps.All[QuickBattleSetup.MapIndex].Briefing;

        for (int i = 0; i < _openingButtons.Count; i++)
            SetSelected(_openingButtons[i], QuickBattleSetup.Openings[i] == QuickBattleSetup.Opening);
        _openingSummary.Text = BattleOpenings.Summary(QuickBattleSetup.Opening, mirrored: null);

        foreach ((Experiments.Rule rule, Button button) in _ruleButtons)
        {
            bool on = Experiments.IsOn(rule);
            button.Text = $"{Experiments.Name(rule)} · {(on ? "ON" : "OFF")}";
            SetSelected(button, on);
        }
        _launch.Disabled = !QuickBattleSetup.CanLaunch;
    }

    static string ShortName(ShipType ship) => ShipTypes.ClassName(ShipTypes.ClassIdForHull(ship.Id)).ToUpper();

    static void FillSlots(Button[] slots, List<ShipType> ships, int team, System.Func<int, ShipType, string> label)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            bool occupied = i < ships.Count;
            slots[i].Icon = occupied ? ships[i].GetSkin(team).Icon : null;
            slots[i].Text = occupied ? label(i, ships[i]) : "EMPTY";
            slots[i].Disabled = !occupied;
            SetSelected(slots[i], occupied);
        }
    }

    void Launch()
    {
        if (!QuickBattleSetup.CanLaunch)
            return;
        QuickBattleSetup.Start();
        ChangeScene(this, "res://Scenes/Battle.tscn");
    }
}
