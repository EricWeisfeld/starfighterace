using Godot;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

/// <summary>
/// The run's home between battles. It shows exactly one page, chosen from the
/// run's state: the end of the run, a waiting promotion, the stop the
/// squadron is at, or the sector map. Every action saves and re-renders.
/// </summary>
public partial class RunScreen : Node2D
{
    MarginContainer _root;
    bool _showSquadron;
    int? _selectedNodeId;
    int? _selectedCard;
    readonly HashSet<string> _briefingPicks = new();
    bool _briefingPicksInitialized;
    bool _confirmAbandon;

    RunState Run => RunState.Current;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Bg);
        if (RunState.Current == null && RunState.Load() == null)
        {
            ChangeScene(this, "res://Scenes/HomeScreen.tscn");
            return;
        }
        // A battle that was interrupted picks up from its start checkpoint.
        if (Run.BattleInProgress && Run.ResumeBattle())
        {
            ChangeScene(this, "res://Scenes/Battle.tscn");
            return;
        }
        _root = ScreenRoot(this, extraTop: 28, extraBottom: 28);
        Render();
    }

    public override void _Notification(int what)
    {
        // Android's back gesture returns from a sub-page.
        if (what != NotificationWMGoBackRequest)
            return;
        if (_refitCallsign != null)
        {
            _refitCallsign = null;
            Render();
        }
        else if (_showSquadron)
        {
            _showSquadron = false;
            Render();
        }
    }

    bool _renderQueued;

    /// <summary>
    /// Rebuilds the visible page on the next idle frame. Deferring matters:
    /// most rebuilds are requested from a button's own press handler, and the
    /// button must not be freed while it is still handling that press.
    /// </summary>
    void Render()
    {
        if (_renderQueued)
            return;
        _renderQueued = true;
        CallDeferred(MethodName.RenderNow);
    }

    void RenderNow()
    {
        _renderQueued = false;
        // Hide the old page and let it free at the end of the frame. Removing
        // it from the tree now would strand a touch that is still being
        // released on one of its buttons.
        foreach (Node child in _root.GetChildren())
        {
            if (child is CanvasItem item)
                item.Visible = false;
            child.QueueFree();
        }

        Control page;
        if (Run.Outcome != RunOutcome.InProgress)
            page = BuildEndPage();
        else if (Run.Promotions.Count > 0)
            page = BuildPromotionPage();
        else if (_showSquadron)
            page = BuildSquadronPage();
        else if (Run.ActiveNode is { } node)
            page = node.Kind switch
            {
                RunNodeKind.Repair => _refitCallsign != null ? BuildRefitPage() : BuildDockPage(),
                RunNodeKind.Recruit => BuildRecruitPage(),
                // An event that turned into a fight shows its outcome first,
                // then the briefing once the player moves on.
                RunNodeKind.Event when node.EventBattle == null || !_eventBattleAcknowledged => BuildEventPage(),
                _ => BuildBriefingPage(),
            };
        else
            page = BuildMapPage();
        _root.AddChild(page);
        QueueRedraw();
    }

    bool _eventBattleAcknowledged;

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawStarfield(this, 51177 + (ulong)(Run?.Sector ?? 0), size.X, size.Y, 200);
        DrawNebula(this, new Vector2(size.X * 0.8f, size.Y * 0.25f), new Color(0.25f, 0.59f, 1f), 16, 24);
    }

    // ------------------------------------------------------------ helpers

    /// <summary>Page title block: a small eyebrow over a large title, with an optional back button.</summary>
    static Control Header(string eyebrow, string title, System.Action back = null, Control trailing = null)
    {
        HBoxContainer row = Row(16);
        if (back != null)
        {
            Button button = TouchButton("<", fontSize: FontTitle);
            button.CustomMinimumSize = new Vector2(TouchTarget, TouchTarget);
            button.Pressed += back;
            row.AddChild(button);
        }
        VBoxContainer block = Stack(2);
        block.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        block.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        block.AddChild(Text(eyebrow, FontCaption, Muted, 3));
        Label heading = Text(title, FontTitle, TextBright, 4);
        heading.ClipText = true;
        block.AddChild(heading);
        row.AddChild(block);
        if (trailing != null)
            row.AddChild(trailing);
        return row;
    }

    /// <summary>Right-aligned salvage readout for page headers.</summary>
    Control SalvageBadge()
    {
        VBoxContainer box = Stack(0);
        box.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        Label key = Text("SALVAGE", FontMicro, Muted, 3);
        key.HorizontalAlignment = HorizontalAlignment.Right;
        Label value = Text(Run.Salvage.ToString(), FontTitle, Warning, 2);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        box.AddChild(key);
        box.AddChild(value);
        return box;
    }

    static TextureRect ShipIcon(ShipType ship, float size = 88f, int team = 0, bool faded = false) => new()
    {
        Texture = ship.GetSkin(team).Base,
        CustomMinimumSize = new Vector2(size, size),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        Modulate = faded ? new Color(1, 1, 1, 0.4f) : Colors.White,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    static (string Label, ChipRole Role) StatusOf(Pilot pilot) => pilot.Condition switch
    {
        PilotCondition.KIA => ("KIA", ChipRole.Loss),
        _ when pilot.HullDamage > 0 => ("DAMAGED", ChipRole.Impaired),
        _ => ("READY", ChipRole.Gain),
    };

    /// <summary>Icon, callsign, frame and level on the left; status tag on the right; hull bar beneath.</summary>
    static Control PilotSummary(Pilot pilot, string trailingNote = null)
    {
        VBoxContainer stack = Stack(10);
        HBoxContainer header = Row(16);
        header.AddChild(ShipIcon(pilot.Ship, 80f, faded: !pilot.Alive));
        VBoxContainer identity = Stack(2);
        identity.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        identity.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        identity.AddChild(Text(pilot.Callsign, FontBody, pilot.Alive ? TextBright : Negative, 2));
        identity.AddChild(Text($"LV {pilot.Level} · {pilot.Ship.DisplayName.ToUpper()}", FontCaption, Muted, 2));
        header.AddChild(identity);
        (string status, ChipRole role) = StatusOf(pilot);
        header.AddChild(Tag(status, role));
        stack.AddChild(header);
        if (pilot.Alive)
        {
            HBoxContainer hullRow = Row(12);
            hullRow.AddChild(new HullBar
            {
                Fraction = pilot.Hull / (float)pilot.MaxHull,
                CustomMinimumSize = new Vector2(0, 12),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
            string hull = $"HULL {pilot.Hull}/{pilot.MaxHull}";
            if (trailingNote != null)
                hull += " · " + trailingNote;
            hullRow.AddChild(Text(hull, FontMicro, Body, 1));
            stack.AddChild(hullRow);
        }
        return stack;
    }

    static Control Spacer() => new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>Scrollable page body that fills the space between header and footer.</summary>
    static (TouchScroll Scroll, VBoxContainer Content) ScrollBody(int separation = 16)
    {
        var scroll = new TouchScroll { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        VBoxContainer content = Stack(separation);
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(content);
        return (scroll, content);
    }

    void Go(string scene) => ChangeScene(this, scene);
}

/// <summary>A thin horizontal hull gauge that turns amber when low.</summary>
public partial class HullBar : Control
{
    public float Fraction = 1f;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(1f, 1f, 1f, 0.12f));
        float f = Mathf.Clamp(Fraction, 0f, 1f);
        Color color = f < 0.35f ? SignalUi.Warning : SignalUi.Positive;
        DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X * f, Size.Y)), color);
    }
}
