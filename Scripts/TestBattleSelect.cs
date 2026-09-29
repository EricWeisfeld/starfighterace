using Godot;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

/// <summary>Sandbox squad builder. It creates disposable level-cap pilots and never touches campaign data.</summary>
public partial class TestBattleSelect : Node2D
{
    const float ScreenW = 1152f;
    const float ScreenH = 648f;
    readonly List<ShipType> _squad = new();
    readonly List<Button> _addButtons = new();
    readonly Button[] _slots = new Button[3];
    Button _launch;
    OptionButton _mapPicker;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Bg);
        BuildUi();
        Refresh();
    }

    void BuildUi()
    {
        var ui = new CanvasLayer();
        AddChild(ui);
        var root = new VBoxContainer { OffsetLeft = 42, OffsetTop = 24, OffsetRight = ScreenW - 42, OffsetBottom = ScreenH - 24 };
        root.AddThemeConstantOverride("separation", 10);
        ui.AddChild(root);

        var title = Text("TEST BATTLE", 28, TextBright, 6);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(title);
        var hint = Text($"MAX-LEVEL SQUAD BUILDER · EVERY TEST PILOT DEPLOYS AT LEVEL {PilotRoster.MaxLevel} WITH ITS FULL ABILITY TRACK", 9, Muted, 3);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(hint);
        var rule = new CenterContainer();
        rule.AddChild(new ColorRect { CustomMinimumSize = new Vector2(560, 1), Color = Hairline });
        root.AddChild(rule);

        var mapRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        mapRow.AddThemeConstantOverride("separation", 10);
        root.AddChild(mapRow);
        mapRow.AddChild(Text("BATTLE MAP", 9, Muted, 4));
        _mapPicker = new OptionButton { CustomMinimumSize = new Vector2(420, 34), TooltipText = "Choose the terrain layout for this test battle." };
        _mapPicker.AddThemeFontSizeOverride("font_size", 11);
        foreach (BattleMapDefinition map in BattleMaps.All)
            _mapPicker.AddItem($"{map.DisplayName} — {map.Briefing}");
        mapRow.AddChild(_mapPicker);

        var cardScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 285),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            TooltipText = "Scroll to browse the available ships."
        };
        root.AddChild(cardScroll);
        var cards = new GridContainer { Columns = 5, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cards.AddThemeConstantOverride("h_separation", 12);
        cards.AddThemeConstantOverride("v_separation", 12);
        cardScroll.AddChild(cards);
        foreach (ShipType ship in ShipTypes.SandboxHulls)
            cards.AddChild(BuildShipCard(ship));

        var squadRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        squadRow.AddThemeConstantOverride("separation", 10);
        root.AddChild(squadRow);
        squadRow.AddChild(Text("TEST SQUAD", 9, Muted, 4));
        for (int i = 0; i < _slots.Length; i++)
        {
            int slot = i;
            var button = new Button { CustomMinimumSize = new Vector2(170, 56), TooltipText = "Empty slot" };
            button.AddThemeFontSizeOverride("font_size", 10);
            button.AddThemeColorOverride("font_color", Body);
            button.AddThemeStyleboxOverride("normal", Box(CellBg, Hairline, 1, 6, 4));
            button.AddThemeStyleboxOverride("hover", Box(CellBg, new Color(Accent.R, Accent.G, Accent.B, 0.7f), 1, 6, 4));
            button.AddThemeStyleboxOverride("pressed", Box(CellBg, Accent, 1, 6, 4));
            button.Pressed += () => Remove(slot);
            _slots[i] = button;
            squadRow.AddChild(button);
        }
        var actionRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        actionRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(actionRow);
        _launch = FlatButton("LAUNCH TEST BATTLE  >", 13);
        _launch.Disabled = true;
        _launch.CustomMinimumSize = new Vector2(245, 42);
        _launch.Pressed += Launch;
        actionRow.AddChild(_launch);
        var back = FlatButton("<  HOME", 10);
        back.CustomMinimumSize = new Vector2(110, 42);
        back.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/HomeScreen.tscn");
        actionRow.AddChild(back);
    }

    Control BuildShipCard(ShipType ship)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(196, 270) };
        panel.AddThemeStyleboxOverride("panel", Box(CellBg, Hairline, 1, 12, 10));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);
        var image = new TextureRect
        {
            Texture = ship.GetSkin(0).Base,
            CustomMinimumSize = new Vector2(0, 80),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        box.AddChild(image);
        var name = Text(ship.DisplayName.ToUpper(), 14, TextBright, 2);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(name);
        var description = Text(ship.Description, 9, Muted, 0, wrap: true);
        description.CustomMinimumSize = new Vector2(0, 39);
        box.AddChild(description);
        box.AddChild(Text($"SHD {ship.MaxShield} +{ship.ShieldRegenPerTurn}/T · HULL {ship.MaxHp} · DMG {ship.ShotDamage} · EVA {ship.Evasion * 100:0}%", 9, Body));
        string abilities = ship.ManeuverPool.Length == 0 ? "FULL TRACK · BASE SYSTEMS" : $"FULL TRACK · {string.Join(" · ", ship.ManeuverPool.Select(AbilityName).Select(name => name.ToUpper()))}";
        var ability = Text(abilities, 8, Muted, 0, wrap: true);
        ability.CustomMinimumSize = new Vector2(0, 32);
        box.AddChild(ability);
        Button add = FlatButton("ADD TO SQUAD", 9);
        add.CustomMinimumSize = new Vector2(0, 28);
        add.Pressed += () => Add(ship);
        _addButtons.Add(add);
        box.AddChild(add);
        return panel;
    }

    void Add(ShipType ship)
    {
        if (_squad.Count < 3)
        {
            _squad.Add(ship);
            Refresh();
        }
    }

    void Remove(int slot)
    {
        if (slot < _squad.Count)
        {
            _squad.RemoveAt(slot);
            Refresh();
        }
    }

    void Refresh()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            bool occupied = i < _squad.Count;
            _slots[i].Icon = occupied ? _squad[i].GetSkin(0).Base : null;
            _slots[i].Text = occupied ? $"{_squad[i].DisplayName}\nLV {PilotRoster.MaxLevel}" : "EMPTY";
            _slots[i].TooltipText = occupied ? $"{_squad[i].DisplayName}, level {PilotRoster.MaxLevel}. Click to remove." : "Empty slot";
        }
        foreach (Button add in _addButtons)
            add.Disabled = _squad.Count >= 3;
        _launch.Disabled = _squad.Count != 3;
    }

    void Launch()
    {
        GameSetup.StartTestBattle(_squad, BattleMaps.All[_mapPicker.Selected]);
        GetTree().ChangeSceneToFile("res://Scenes/Battle.tscn");
    }

    static string AbilityName(ShipAbility ability) => ability switch
    {
        ShipAbility.UTurn => "U-Turn",
        ShipAbility.BreakTurn => "Break Turn",
        ShipAbility.EngineBoost => "Engine Boost",
        ShipAbility.RotatingGuns => "Rotating Guns",
        ShipAbility.SuppressionFire => "Suppression Fire",
        ShipAbility.EmergencyThrusters => "Emergency Thrusters",
        ShipAbility.SnapTurn => "Snap Turn",
        ShipAbility.HunterLock => "Hunter Lock",
        ShipAbility.PursuitBurn => "Pursuit Burn",
        ShipAbility.EcmJink => "ECM Jink",
        ShipAbility.SensorScramble => "Sensor Scramble",
        ShipAbility.GhostRun => "Ghost Run",
        ShipAbility.EvasiveDodge => "Evasive Dodge",
        _ => ability.ToString(),
    };

    public override void _Draw()
    {
        DrawStarfield(this, 7342, ScreenW, ScreenH);
        DrawNebula(this, new Vector2(200, 100), new Color(0.16f, 0.86f, 0.75f), 12, 20, 0.005f);
    }
}
