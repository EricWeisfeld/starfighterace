using Godot;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

/// <summary>
/// Portrait battle HUD. The top bar carries the turn, the squadron and the
/// objective; the bottom bar carries the selected ship, its maneuvers and the
/// Engage button, all within thumb reach. The battlefield shows through the
/// band between the bars, and the camera frames the action inside that band.
/// </summary>
public partial class BattleHud : CanvasLayer
{
    static BattleManager Mgr => BattleManager.Instance;

    PanelContainer _top, _bottom;
    Label _turnLabel, _phaseLabel, _objectiveLabel;
    readonly List<SquadChip> _chips = new();
    Label _shipName, _shipStats, _summary;
    /// <summary>The selected pilot's instincts and scars; run battles only.</summary>
    Label _instincts, _scars;
    GridContainer _maneuverGrid;
    readonly List<ManeuverButton> _maneuverButtons = new();
    string _gridSignature;
    Button _undo, _engage;
    Control _menu;
    Button _retreat;
    int _framesLaidOut;

    /// <summary>True once both bars have been laid out, so the camera can frame around them.</summary>
    public bool LayoutReady => _framesLaidOut >= 2;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // the menu works while the battle is paused
        (float safeTop, float safeBottom) = SafeInsets(GetViewport());
        BuildTopBar(safeTop);
        BuildBottomBar(safeBottom);
        BuildMenu();
    }

    static StyleBoxFlat BarStyle(bool top)
    {
        var style = new StyleBoxFlat { BgColor = new Color(Bg.R, Bg.G, Bg.B, 0.9f), BorderColor = Hairline };
        if (top)
            style.BorderWidthBottom = 2;
        else
            style.BorderWidthTop = 2;
        return style;
    }

    void BuildTopBar(float safeTop)
    {
        // The bars swallow touches so a tap on them never reaches the map below.
        _top = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        _top.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _top.AddThemeStyleboxOverride("panel", BarStyle(top: true));
        AddChild(_top);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_top", 14 + (int)safeTop);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        _top.AddChild(margin);
        VBoxContainer stack = Stack(8);
        margin.AddChild(stack);

        HBoxContainer row = Row(10);
        stack.AddChild(row);
        VBoxContainer turnBlock = Stack(0);
        turnBlock.CustomMinimumSize = new Vector2(112, 0);
        turnBlock.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _turnLabel = Text("TURN 1", FontBody, TextBright, 2);
        _phaseLabel = Text("PLAN", FontMicro, Accent, 3);
        turnBlock.AddChild(_turnLabel);
        turnBlock.AddChild(_phaseLabel);
        row.AddChild(turnBlock);

        HBoxContainer squad = Row(10);
        squad.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(squad);
        foreach (Fighter fighter in Mgr.PlayerFighters)
        {
            var chip = new SquadChip { Fighter = fighter };
            chip.Pressed += () => Mgr.SelectFighter(chip.Fighter, focus: true);
            _chips.Add(chip);
            squad.AddChild(chip);
        }

        var menuButton = new MenuGlyphButton { CustomMinimumSize = new Vector2(80, 88) };
        menuButton.Pressed += ToggleMenu;
        row.AddChild(menuButton);

        _objectiveLabel = Text("", FontCaption, Muted, 2);
        _objectiveLabel.ClipText = true;
        stack.AddChild(_objectiveLabel);
    }

    void BuildBottomBar(float safeBottom)
    {
        _bottom = new PanelContainer { GrowVertical = Control.GrowDirection.Begin, MouseFilter = Control.MouseFilterEnum.Stop };
        _bottom.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _bottom.AddThemeStyleboxOverride("panel", BarStyle(top: false));
        AddChild(_bottom);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 16 + (int)safeBottom);
        _bottom.AddChild(margin);
        VBoxContainer stack = Stack(12);
        margin.AddChild(stack);

        HBoxContainer row = Row(12);
        stack.AddChild(row);
        VBoxContainer identity = Stack(2);
        identity.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        identity.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _shipName = Text("", FontBody, TextBright, 2);
        _shipName.ClipText = true;
        _shipStats = Text("", FontCaption, Muted, 1);
        _shipStats.ClipText = true;
        identity.AddChild(_shipName);
        identity.AddChild(_shipStats);
        if (!GameSetup.IsTestBattle)
        {
            // Always present, even when empty, so selecting ships never
            // changes the bar's height and shifts the map.
            HBoxContainer traits = Row(10);
            traits.CustomMinimumSize = new Vector2(0, 26);
            _instincts = Text("", FontMicro, Instinct, 1);
            _instincts.ClipText = true;
            _instincts.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            // Scars keep their full width; instincts clip first if space runs out.
            _scars = Text("", FontMicro, Warning, 1);
            traits.AddChild(_instincts);
            traits.AddChild(_scars);
            identity.AddChild(traits);
        }
        row.AddChild(identity);

        _undo = TouchButton("UNDO", fontSize: FontCaption);
        _undo.CustomMinimumSize = new Vector2(112, TouchTarget);
        _undo.Pressed += () => Mgr.UndoLastOrder();
        row.AddChild(_undo);
        _engage = TouchButton("ENGAGE", primary: true);
        _engage.CustomMinimumSize = new Vector2(196, TouchTarget);
        _engage.Pressed += () => Mgr.RequestEngage();
        row.AddChild(_engage);

        _maneuverGrid = new GridContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _maneuverGrid.AddThemeConstantOverride("h_separation", 10);
        _maneuverGrid.AddThemeConstantOverride("v_separation", 10);
        stack.AddChild(_maneuverGrid);

        _summary = Text("", FontCaption, Muted, 0, wrap: true);
        _summary.CustomMinimumSize = new Vector2(0, 62);
        stack.AddChild(_summary);
    }

    void BuildMenu()
    {
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.72f), Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);
        _menu = shade;

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        shade.AddChild(center);
        PanelContainer card = Card(32, 28);
        card.CustomMinimumSize = new Vector2(600, 0);
        center.AddChild(card);
        VBoxContainer stack = Stack(16);
        card.AddChild(stack);

        stack.AddChild(Text("PAUSED", FontTitle, TextBright, 4));
        Button resume = TouchButton("RESUME", primary: true);
        resume.Pressed += ToggleMenu;
        stack.AddChild(resume);
        Button overview = TouchButton("VIEW WHOLE BATTLE");
        overview.Pressed += () =>
        {
            ToggleMenu();
            Mgr.FrameBattle();
        };
        stack.AddChild(overview);
        _retreat = TouchButton("RETREAT");
        _retreat.Pressed += () =>
        {
            ToggleMenu();
            Mgr.Retreat();
        };
        stack.AddChild(_retreat);
        stack.AddChild(Text("Ends the battle as a loss. Your pilots come home.", FontCaption, Muted, 0, wrap: true));
        Button quit = TouchButton("QUIT TO TITLE");
        quit.Pressed += () =>
        {
            GetTree().Paused = false;
            ChangeScene(this, "res://Scenes/HomeScreen.tscn");
        };
        stack.AddChild(quit);
        stack.AddChild(Text(GameSetup.IsTestBattle
            ? "Leaves this quick battle."
            : "The run was saved when this battle began. Continue to replay it from the start.",
            FontCaption, Muted, 0, wrap: true));

        stack.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        stack.AddChild(Text("CONTROLS", FontCaption, Muted, 3));
        foreach (string line in new[]
        {
            "Drag a ship's ghost to steer it.",
            "Tap a maneuver to change how it flies.",
            "Tap an enemy to preview your firing window.",
            "Drag or pinch the map to look around.",
            "Double-tap empty space to see everything.",
            "Engage when ready: ships without orders hold course.",
        })
        {
            stack.AddChild(Text(line, FontCaption, Body, 0, wrap: true));
        }
    }

    /// <summary>Opens or closes the pause menu. The battle is paused while it is open.</summary>
    public void ToggleMenu()
    {
        _menu.Visible = !_menu.Visible;
        GetTree().Paused = _menu.Visible;
        _retreat.Disabled = !Mgr.CanRetreat;
    }

    public override void _ExitTree()
    {
        // Never leave the next scene paused.
        if (GetTree() != null)
            GetTree().Paused = false;
    }

    public override void _Process(double delta)
    {
        if (Mgr == null || !Visible)
            return;

        bool planning = Mgr.CurrentPhase == BattleManager.Phase.Planning;
        Fighter selected = Mgr.Selected;

        _turnLabel.Text = $"TURN {Mgr.TurnNumber}";
        _phaseLabel.Text = planning ? "PLAN" : "ENGAGED";
        _phaseLabel.AddThemeColorOverride("font_color", planning ? Accent : Warning);

        string notice = Mgr.Notice;
        _objectiveLabel.Text = notice ?? Mgr.ObjectiveText;
        _objectiveLabel.AddThemeColorOverride("font_color", notice != null ? Warning : Muted);

        foreach (SquadChip chip in _chips)
        {
            chip.IsSelected = planning && chip.Fighter == selected;
            chip.Planning = planning;
            chip.QueueRedraw();
        }

        _maneuverGrid.Visible = planning && selected != null;
        _summary.Visible = planning;
        _undo.Visible = planning;
        _undo.Disabled = !Mgr.CanUndo;
        _engage.Visible = planning;
        _engage.Text = Mgr.AwaitingFatalConfirm ? "CONFIRM" : "ENGAGE";

        if (planning && selected != null)
        {
            _shipName.Text = $"{BattleManager.CallsignOf(selected)} · {selected.Type.DisplayName.ToUpper()}";
            _shipStats.Text = $"HULL {Mathf.Max(0, selected.Hp)}/{selected.MaxHp} · SHIELD {selected.Shield}/{selected.MaxShield}";
            RefreshTraits(selected);
            RefreshManeuvers(selected);
            RefreshSummary(selected);
        }
        else if (!planning)
        {
            int alive = Mgr.PlayerFighters.Count(f => f.IsAlive);
            _shipName.Text = "MANEUVERS EXECUTING";
            _shipStats.Text = $"{alive} OF {Mgr.PlayerFighters.Count} SHIPS FLYING";
            RefreshTraits(null);
        }

        Mgr.Camera.InsetTop = _top.Size.Y;
        Mgr.Camera.InsetBottom = _bottom.Size.Y;
        if (_top.Size.Y > 0 && _bottom.Size.Y > 0)
            _framesLaidOut++;
    }

    void RefreshTraits(Fighter fighter)
    {
        if (_instincts == null)
            return;
        Pilot pilot = fighter?.Pilot;
        _instincts.Text = pilot == null ? "" : string.Join(" · ", pilot.Instincts.Select(p => p.Name.ToUpper()));
        _scars.Text = pilot == null || !pilot.Scars.Any() ? "" : string.Join(" · ", pilot.Scars.Select(p => p.Name.ToUpper()));
        _scars.Visible = _scars.Text.Length > 0;
    }

    void RefreshManeuvers(Fighter fighter)
    {
        List<ManeuverInfo> actions = ManeuverCatalog.ActionsFor(fighter);
        string signature = fighter.GetInstanceId() + ":" + string.Join(",", actions.Select(a => a.Action));
        if (signature != _gridSignature)
        {
            _gridSignature = signature;
            foreach (ManeuverButton button in _maneuverButtons)
                button.QueueFree();
            _maneuverButtons.Clear();
            _maneuverGrid.Columns = actions.Count <= 4 ? Mathf.Max(1, actions.Count) : (actions.Count + 1) / 2;
            foreach (ManeuverInfo info in actions)
            {
                var button = new ManeuverButton(info);
                button.Pressed += () => Mgr.ChooseAction(button.Info.Action);
                _maneuverButtons.Add(button);
                _maneuverGrid.AddChild(button);
            }
        }

        foreach (ManeuverButton button in _maneuverButtons)
            button.Refresh(fighter, Mgr.PendingTargetAction);
    }

    void RefreshSummary(Fighter fighter)
    {
        if (Mgr.AwaitingFatalConfirm)
        {
            string names = string.Join(", ", Mgr.ShipsOnFatalCourse().Select(BattleManager.CallsignOf));
            SetSummary($"{names} will fly into an asteroid. Tap CONFIRM to engage anyway.", Negative);
            return;
        }
        if (Mgr.PendingTargetAction is ManeuverAction pending)
        {
            SetSummary($"Tap an enemy ship to use {ManeuverCatalog.Get(pending).Name}. Tap the button again to cancel.", Accent);
            return;
        }

        float turn = fighter.PlannedTurnAngleRadians ?? 0f;
        if (Mgr.PathHitsAsteroid(fighter, fighter.PlannedManeuver, turn, fighter.PlannedPathDistance))
        {
            SetSummary("This path flies into an asteroid. The ship will be destroyed.", Negative);
            return;
        }
        int scrape = Mgr.PathScrapeDamage(fighter, fighter.PlannedManeuver, turn, fighter.PlannedPathDistance);
        if (scrape > 0)
        {
            SetSummary($"This path scrapes an asteroid for {scrape} hull damage.", Warning);
            return;
        }

        ManeuverInfo info = ManeuverCatalog.ForManeuver(fighter.PlannedManeuver);
        SetSummary(info.Summary(fighter) + (info.Directional ? " Tap again to switch sides." : ""), Muted);
    }

    void SetSummary(string text, Color color)
    {
        _summary.Text = text;
        _summary.AddThemeColorOverride("font_color", color);
    }
}

/// <summary>
/// One ship in the top bar: callsign, shield and hull bars, and whether it
/// has orders this turn. Tapping it selects the ship and focuses the camera.
/// </summary>
public partial class SquadChip : Button
{
    public Fighter Fighter;
    public bool IsSelected;
    public bool Planning;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 88);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        FocusMode = FocusModeEnum.None;
        var empty = new StyleBoxEmpty();
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            AddThemeStyleboxOverride(state, empty);
    }

    public override void _Draw()
    {
        Font font = ThemeDB.FallbackFont;
        var rect = new Rect2(Vector2.Zero, Size);
        bool alive = Fighter.IsAlive;
        Color border = IsSelected ? SignalUi.Accent : SignalUi.Hairline;
        DrawRect(rect, new Color(SignalUi.CellBg, IsSelected ? 1f : 0.8f));
        DrawRect(rect, border, false, IsSelected ? 3f : 2f);

        string name = BattleManager.CallsignOf(Fighter);
        DrawString(font, new Vector2(12, 30), name, HorizontalAlignment.Left, Size.X - 24, SignalUi.FontCaption,
            alive ? SignalUi.TextBright : SignalUi.Negative);

        float barWidth = Size.X - 24;
        float shield = Fighter.MaxShield > 0 ? Mathf.Clamp(Fighter.Shield / (float)Fighter.MaxShield, 0f, 1f) : 0f;
        float hull = Mathf.Clamp(Fighter.Hp / (float)Fighter.MaxHp, 0f, 1f);
        DrawRect(new Rect2(12, 42, barWidth, 5), new Color(0.2f, 0.5f, 1f, 0.2f));
        DrawRect(new Rect2(12, 42, barWidth * shield, 5), new Color(0.35f, 0.7f, 1f));
        DrawRect(new Rect2(12, 50, barWidth, 7), new Color(1f, 1f, 1f, 0.12f));
        Color hullColor = hull < 0.35f ? SignalUi.Warning : SignalUi.Positive;
        DrawRect(new Rect2(12, 50, barWidth * hull, 7), hullColor);

        (string status, Color color) = !alive ? ("DOWN", SignalUi.Negative)
            : !Planning ? ("FLYING", SignalUi.Muted)
            : BattleManager.HasOrders(Fighter) ? ("ORDERS SET", SignalUi.Positive)
            : ("HOLDING", SignalUi.Muted);
        DrawString(font, new Vector2(12, 80), status, HorizontalAlignment.Left, Size.X - 24, SignalUi.FontMicro, color);
    }
}

/// <summary>A maneuver-bar button: flight-path pictogram, name and a status line.</summary>
public partial class ManeuverButton : Button
{
    public readonly ManeuverInfo Info;
    readonly ManeuverGlyph _glyph;
    readonly Label _name, _status;
    bool? _lastActive;

    /// <summary>Engine-required parameterless constructor; use the info overload in code.</summary>
    public ManeuverButton() { }

    public ManeuverButton(ManeuverInfo info)
    {
        Info = info;
        CustomMinimumSize = new Vector2(0, 120);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        FocusMode = FocusModeEnum.None;
        ClipContents = true;

        VBoxContainer stack = SignalUi.Stack(2);
        stack.SetAnchorsPreset(LayoutPreset.FullRect);
        stack.OffsetTop = 8;
        stack.OffsetBottom = -6;
        AddChild(stack);
        _glyph = new ManeuverGlyph
        {
            Action = info.Action,
            Tint = info.Color,
            CustomMinimumSize = new Vector2(0, 42),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stack.AddChild(_glyph);
        _name = SignalUi.Text(info.Name, SignalUi.FontCaption, SignalUi.TextBright, 1);
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        _name.ClipText = true;
        _name.MouseFilter = MouseFilterEnum.Ignore;
        stack.AddChild(_name);
        _status = SignalUi.Text("", SignalUi.FontMicro, SignalUi.Muted, 1);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.ClipText = true;
        _status.MouseFilter = MouseFilterEnum.Ignore;
        stack.AddChild(_status);
    }

    public void Refresh(Fighter fighter, ManeuverAction? pendingTarget)
    {
        int cooldown = ManeuverCatalog.CooldownTurns(fighter, Info);
        bool active = Info.TargetsEnemy
            ? pendingTarget == Info.Action
            : fighter.PlannedManeuver == Info.Maneuver;
        float side = Mathf.Sign(fighter.PlannedTurnAngleRadians ?? 0f);

        Disabled = cooldown > 0;
        _glyph.Side = active && Info.Directional && side != 0f ? side : 1f;
        _glyph.Tint = Disabled ? new Color(Info.Color, 0.35f) : Info.Color;
        _glyph.QueueRedraw();
        _name.AddThemeColorOverride("font_color", Disabled ? SignalUi.Dim : SignalUi.TextBright);

        // Only state the player cannot see elsewhere; the border shows which
        // maneuver is active.
        string status = "";
        Color statusColor = Info.Color;
        if (cooldown > 0)
        {
            status = cooldown == 1 ? "NEXT TURN" : $"IN {cooldown} TURNS";
            statusColor = SignalUi.Muted;
        }
        else if (active && Info.Directional)
        {
            status = side < 0f ? "LEFT" : "RIGHT";
        }
        else if (active && Info.TargetsEnemy)
        {
            status = "TAP ENEMY";
        }
        else if (Info.Action == ManeuverAction.HunterLock && fighter.HunterLockTarget != null && fighter.HunterLockTarget.IsAlive)
        {
            status = "LOCKED";
        }
        else if (Info.Ability is ShipAbility ability && fighter.HasMastered(ability))
        {
            status = "MASTERED";
            statusColor = TraitCallout.InstinctColor;
        }
        _status.Text = status;
        _status.AddThemeColorOverride("font_color", statusColor);

        if (_lastActive != active)
        {
            _lastActive = active;
            ApplyStyle(active);
        }
    }

    void ApplyStyle(bool active)
    {
        Color c = Info.Color;
        StyleBoxFlat normal = SignalUi.Box(new Color(c, active ? 0.2f : 0.05f), new Color(c, active ? 1f : 0.35f), active ? 3 : 2, 6, 6);
        StyleBoxFlat pressed = SignalUi.Box(new Color(c, 0.3f), c, 3, 6, 6);
        StyleBoxFlat disabled = SignalUi.Box(new Color(0, 0, 0, 0), new Color(SignalUi.Hairline, 0.5f), 2, 6, 6);
        AddThemeStyleboxOverride("normal", normal);
        AddThemeStyleboxOverride("hover", normal);
        AddThemeStyleboxOverride("pressed", pressed);
        AddThemeStyleboxOverride("disabled", disabled);
        AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }
}

/// <summary>Square button with a drawn three-bar menu icon (the default font lacks one).</summary>
public partial class MenuGlyphButton : Button
{
    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        var style = SignalUi.Box(new Color(SignalUi.Accent, 0.06f), new Color(SignalUi.Accent, 0.45f), 2, 0, 0);
        AddThemeStyleboxOverride("normal", style);
        AddThemeStyleboxOverride("hover", style);
        AddThemeStyleboxOverride("pressed", SignalUi.Box(new Color(SignalUi.Accent, 0.2f), SignalUi.Accent, 2, 0, 0));
        AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    public override void _Draw()
    {
        Vector2 c = Size / 2f;
        for (int i = -1; i <= 1; i++)
            DrawLine(c + new Vector2(-18, i * 12), c + new Vector2(18, i * 12), SignalUi.Accent, 4f);
    }
}
