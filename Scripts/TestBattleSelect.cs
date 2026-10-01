using Godot;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

/// <summary>
/// Quick Battle setup, laid out for a portrait phone: pick a battlefield,
/// fill up to three squadron slots from the hangar list, launch. Pilots are
/// disposable max-level sandbox pilots and never touch campaign data.
/// </summary>
public partial class TestBattleSelect : Node2D
{
    const int SquadSize = 3;
    readonly List<ShipType> _squad = new();
    readonly List<TapCard> _shipCards = new();
    readonly List<Button> _mapButtons = new();
    readonly Button[] _slots = new Button[SquadSize];
    Label _squadLabel, _mapBriefing;
    Button _launch;
    int _mapIndex;

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
        titleBlock.AddChild(Text($"MAX-LEVEL PILOTS · LEVEL {Pilot.MaxLevel}", FontCaption, Muted, 2));
        header.AddChild(titleBlock);
        page.AddChild(header);

        page.AddChild(Text("BATTLEFIELD", FontCaption, Muted, 4));
        var maps = new GridContainer { Columns = 3, MouseFilter = Control.MouseFilterEnum.Ignore };
        maps.AddThemeConstantOverride("h_separation", 12);
        maps.AddThemeConstantOverride("v_separation", 12);
        for (int i = 0; i < BattleMaps.All.Length; i++)
        {
            int index = i;
            Button map = SelectableButton(BattleMaps.All[i].DisplayName);
            map.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            map.Pressed += () =>
            {
                _mapIndex = index;
                Refresh();
            };
            _mapButtons.Add(map);
            maps.AddChild(map);
        }
        page.AddChild(maps);
        _mapBriefing = Text("", FontCaption, Body, 0, wrap: true);
        _mapBriefing.CustomMinimumSize = new Vector2(0, 60);
        page.AddChild(_mapBriefing);

        _squadLabel = Text("", FontCaption, Muted, 4);
        page.AddChild(_squadLabel);
        HBoxContainer slots = Row(12);
        for (int i = 0; i < SquadSize; i++)
        {
            int slot = i;
            var button = SelectableButton("");
            button.CustomMinimumSize = new Vector2(0, 132);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.IconAlignment = HorizontalAlignment.Center;
            button.VerticalIconAlignment = VerticalAlignment.Top;
            button.ExpandIcon = true;
            button.AddThemeFontSizeOverride("font_size", FontMicro);
            button.Pressed += () => Remove(slot);
            _slots[i] = button;
            slots.AddChild(button);
        }
        page.AddChild(slots);

        page.AddChild(Text("HANGAR · TAP A SHIP TO ADD IT", FontCaption, Muted, 4));
        var scroll = new TouchScroll { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        page.AddChild(scroll);
        VBoxContainer list = Stack(12);
        list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(list);
        foreach (ShipType ship in ShipTypes.SandboxHulls)
            list.AddChild(BuildShipRow(ship));

        _launch = TouchButton("LAUNCH", primary: true);
        _launch.Pressed += Launch;
        page.AddChild(_launch);
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

    Control BuildShipRow(ShipType ship)
    {
        // The whole card is the add button.
        var card = new TapCard { CustomMinimumSize = new Vector2(0, 150) };
        card.Tapped += () => Add(ship);
        _shipCards.Add(card);
        HBoxContainer row = Row(16);
        card.AddChild(row);
        row.AddChild(new TextureRect
        {
            Texture = ship.GetSkin(0).Icon,
            CustomMinimumSize = new Vector2(96, 96),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        VBoxContainer info = Stack(4);
        info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        info.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        info.AddChild(Text(ship.DisplayName.ToUpper(), FontBody, TextBright, 2));
        info.AddChild(Text($"HULL {ship.MaxHp} · SHIELD {ship.MaxShield} · DMG {ship.ShotDamage} · EVA {ship.Evasion * 100:0}%", FontMicro, Body, 1));
        string maneuvers = string.Join(" · ", ship.ManeuverPool.Take(Pilot.MaxManeuvers).Select(a => ManeuverCatalog.AbilityName(a).ToUpper()));
        Label pool = Text(maneuvers, FontMicro, Muted, 1, wrap: true);
        info.AddChild(pool);
        row.AddChild(info);
        return card;
    }

    void Add(ShipType ship)
    {
        if (_squad.Count < SquadSize)
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
        for (int i = 0; i < _mapButtons.Count; i++)
            SetSelected(_mapButtons[i], i == _mapIndex);
        _mapBriefing.Text = BattleMaps.All[_mapIndex].Briefing;

        _squadLabel.Text = $"SQUADRON · {_squad.Count}/{SquadSize} · TAP TO REMOVE";
        for (int i = 0; i < _slots.Length; i++)
        {
            bool occupied = i < _squad.Count;
            _slots[i].Icon = occupied ? _squad[i].GetSkin(0).Base : null;
            _slots[i].Text = occupied ? _squad[i].DisplayName.ToUpper() : "EMPTY";
            SetSelected(_slots[i], occupied);
        }
        foreach (TapCard card in _shipCards)
            card.Disabled = _squad.Count >= SquadSize;
        _launch.Disabled = _squad.Count == 0;
    }

    void Launch()
    {
        GameSetup.StartTestBattle(_squad, BattleMaps.All[_mapIndex]);
        ChangeScene(this, "res://Scenes/Battle.tscn");
    }

}
