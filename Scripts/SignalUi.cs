using Godot;
using System.Collections.Generic;

/// <summary>
/// Shared "Signal" visual language for every screen: one hairline grid instead of
/// boxed panels, tracked uppercase micro-labels, large light numerals, flat
/// bordered buttons and node-and-rail progression. PilotCareer is the reference
/// implementation of the style.
/// </summary>
public static class SignalUi
{
    // Palette. Color roles are exclusive so state reads at a glance:
    //   Accent blue = interactive, progress, and pending player decisions
    //                 (decisions are the only things that pulse);
    //   Positive    = healthy / gained / player;
    //   Warning     = impaired ONLY (wounds, hull damage, scars);
    //   Negative    = losses and the enemy;
    //   Instinct    = what a pilot has learned (instincts, masteries).
    public static readonly Color Bg = new(0.024f, 0.039f, 0.071f);
    public static readonly Color TextBright = new(0.933f, 0.965f, 1f);
    public static readonly Color Body = new(0.788f, 0.839f, 0.886f);
    public static readonly Color Muted = new(0.48f, 0.58f, 0.68f);
    public static readonly Color Dim = new(0.40f, 0.48f, 0.56f);
    public static readonly Color Accent = new(0.302f, 0.639f, 1f);
    public static readonly Color Positive = new(0.341f, 0.902f, 0.643f);
    public static readonly Color Warning = new(1f, 0.69f, 0.24f);
    public static readonly Color Negative = new(1f, 0.35f, 0.3f);
    public static readonly Color Instinct = new(0.78f, 0.64f, 1f);
    public static readonly Color Hairline = new(0.47f, 0.71f, 0.9f, 0.22f);
    public static readonly Color CellBg = new(0.031f, 0.063f, 0.114f);

    // Mobile type scale in viewport pixels. The portrait viewport is 720 wide,
    // which is about two pixels per density-independent point on a phone, so
    // 28 px body text reads as 14 pt. Nothing a player must read goes below
    // FontCaption.
    public const int FontMicro = 20;
    public const int FontCaption = 22;
    public const int FontSmall = 24;
    public const int FontBody = 28;
    public const int FontTitle = 40;
    public const int FontHeading = 56;
    public const int FontDisplay = 80;

    /// <summary>Minimum height of anything the player taps: 48 pt on a phone.</summary>
    public const float TouchTarget = 96f;
    /// <summary>Horizontal gutter between screen content and the display edge.</summary>
    public const int ScreenGutter = 32;

    static readonly Dictionary<int, FontVariation> TrackedFonts = new();

    /// <summary>Default font with extra letter-spacing, for uppercase micro-labels.</summary>
    public static Font Tracked(int spacing)
    {
        if (!TrackedFonts.TryGetValue(spacing, out FontVariation font))
        {
            font = new FontVariation { BaseFont = ThemeDB.FallbackFont };
            font.SetSpacing(TextServer.SpacingType.Glyph, spacing);
            TrackedFonts[spacing] = font;
        }
        return font;
    }

    public static Label Text(string value, int size, Color color, int tracking = 0, bool wrap = false)
    {
        var label = new Label { Text = value };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        if (tracking > 0)
        {
            Font font = Tracked(tracking);
            label.AddThemeFontOverride("font", font);
        }
        if (wrap)
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    public static Label SectionLabel(string value) => Text(value, 9, Muted, 4);

    public static StyleBoxFlat Box(Color fill, Color border, int borderWidth, int marginH, int marginV)
    {
        var style = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        style.SetBorderWidthAll(borderWidth);
        style.ContentMarginLeft = style.ContentMarginRight = marginH;
        style.ContentMarginTop = style.ContentMarginBottom = marginV;
        return style;
    }

    /// <summary>Standard translucent screen panel: dark cell fill, hairline border.</summary>
    public static StyleBoxFlat Panel(int marginH = 16, int marginV = 14) =>
        Box(new Color(CellBg.R, CellBg.G, CellBg.B, 0.72f), Hairline, 1, marginH, marginV);

    public static Button FlatButton(string text, int fontSize)
    {
        var button = new Button { Text = text };
        button.AddThemeFontOverride("font", Tracked(2));
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.AddThemeColorOverride("font_color", Accent);
        button.AddThemeColorOverride("font_hover_color", TextBright);
        button.AddThemeColorOverride("font_pressed_color", TextBright);
        button.AddThemeColorOverride("font_disabled_color", Dim);
        button.AddThemeStyleboxOverride("normal", Box(new Color(Accent.R, Accent.G, Accent.B, 0.06f), new Color(Accent.R, Accent.G, Accent.B, 0.45f), 1, 12, 6));
        button.AddThemeStyleboxOverride("hover", Box(new Color(Accent.R, Accent.G, Accent.B, 0.14f), new Color(Accent.R, Accent.G, Accent.B, 0.8f), 1, 12, 6));
        button.AddThemeStyleboxOverride("pressed", Box(new Color(Accent.R, Accent.G, Accent.B, 0.2f), Accent, 1, 12, 6));
        button.AddThemeStyleboxOverride("disabled", Box(new Color(0, 0, 0, 0), new Color(0.47f, 0.71f, 0.9f, 0.15f), 1, 12, 6));
        return button;
    }

    /// <summary>
    /// Finger-sized button. The primary style is a filled accent block and is
    /// reserved for the one action that moves the player forward on a screen.
    /// </summary>
    public static Button TouchButton(string text, bool primary = false, int fontSize = FontBody)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, TouchTarget) };
        button.AddThemeFontOverride("font", Tracked(2));
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.FocusMode = Control.FocusModeEnum.None;
        if (primary)
        {
            var ink = new Color(0.02f, 0.06f, 0.12f);
            button.AddThemeColorOverride("font_color", ink);
            button.AddThemeColorOverride("font_hover_color", ink);
            button.AddThemeColorOverride("font_pressed_color", ink);
            button.AddThemeColorOverride("font_disabled_color", Dim);
            button.AddThemeStyleboxOverride("normal", Box(Accent, Accent, 0, 24, 12));
            button.AddThemeStyleboxOverride("hover", Box(Accent.Lightened(0.15f), Accent, 0, 24, 12));
            button.AddThemeStyleboxOverride("pressed", Box(Accent.Darkened(0.2f), Accent, 0, 24, 12));
            button.AddThemeStyleboxOverride("disabled", Box(new Color(Accent.R, Accent.G, Accent.B, 0.12f), new Color(Accent.R, Accent.G, Accent.B, 0.3f), 2, 24, 12));
        }
        else
        {
            button.AddThemeColorOverride("font_color", Accent);
            button.AddThemeColorOverride("font_hover_color", TextBright);
            button.AddThemeColorOverride("font_pressed_color", TextBright);
            button.AddThemeColorOverride("font_disabled_color", Dim);
            button.AddThemeStyleboxOverride("normal", Box(new Color(Accent.R, Accent.G, Accent.B, 0.08f), new Color(Accent.R, Accent.G, Accent.B, 0.55f), 2, 24, 12));
            button.AddThemeStyleboxOverride("hover", Box(new Color(Accent.R, Accent.G, Accent.B, 0.16f), new Color(Accent.R, Accent.G, Accent.B, 0.85f), 2, 24, 12));
            button.AddThemeStyleboxOverride("pressed", Box(new Color(Accent.R, Accent.G, Accent.B, 0.24f), Accent, 2, 24, 12));
            button.AddThemeStyleboxOverride("disabled", Box(Colors.Transparent, new Color(0.47f, 0.71f, 0.9f, 0.18f), 2, 24, 12));
        }
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return button;
    }

    /// <summary>
    /// Top and bottom insets, in viewport pixels, that keep content clear of
    /// notches, rounded corners and gesture bars. Zero on desktop.
    /// </summary>
    public static (float Top, float Bottom) SafeInsets(Viewport viewport)
    {
        if (!OS.HasFeature("mobile"))
            return (0f, 0f);
        Rect2I safe = DisplayServer.GetDisplaySafeArea();
        Vector2I window = DisplayServer.WindowGetSize();
        if (safe.Size.Y <= 0 || window.Y <= 0)
            return (0f, 0f);
        float scale = viewport.GetVisibleRect().Size.Y / window.Y;
        return (Mathf.Max(0, safe.Position.Y) * scale, Mathf.Max(0, window.Y - safe.End.Y) * scale);
    }

    /// <summary>
    /// Full-screen layout root for a portrait screen: a canvas layer holding a
    /// margin container inset by the screen gutter and the device safe area.
    /// </summary>
    public static MarginContainer ScreenRoot(Node owner, int extraTop = 24, int extraBottom = 24)
    {
        var layer = new CanvasLayer();
        owner.AddChild(layer);
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        (float top, float bottom) = SafeInsets(owner.GetViewport());
        margin.AddThemeConstantOverride("margin_left", ScreenGutter);
        margin.AddThemeConstantOverride("margin_right", ScreenGutter);
        margin.AddThemeConstantOverride("margin_top", extraTop + (int)top);
        margin.AddThemeConstantOverride("margin_bottom", extraBottom + (int)bottom);
        layer.AddChild(margin);
        return margin;
    }

    /// <summary>Vertical stack with a fixed gap, the building block of portrait screens.</summary>
    public static VBoxContainer Stack(int separation = 16)
    {
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    /// <summary>Horizontal row with a fixed gap.</summary>
    public static HBoxContainer Row(int separation = 16)
    {
        var box = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    /// <summary>Card surface used for list items and panels on portrait screens.</summary>
    public static PanelContainer Card(int marginH = 24, int marginV = 20, Color? border = null)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Box(new Color(CellBg.R, CellBg.G, CellBg.B, 0.92f), border ?? Hairline, 2, marginH, marginV));
        return panel;
    }

    /// <summary>
    /// Changes scene once the current input has finished processing. Changing
    /// scene removes the old one from the tree at once, so doing it directly
    /// from a button's press handler strands the touch release that follows.
    /// </summary>
    public static void ChangeScene(Node from, string path) =>
        from.GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, path);

    /// <summary>"1 KILL", "3 KILLS": a count with its noun in the right number.</summary>
    public static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "S")}";

    /// <summary>Readable status tag for portrait screens, coloured by its <see cref="ChipRole"/>.</summary>
    public static Control Tag(string label, ChipRole role)
    {
        (Color fill, Color border, Color text) = ChipColors(role);
        var panel = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", Box(fill, border, 2, 12, 4));
        Label tagText = Text(label, FontMicro, text, 2);
        tagText.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(tagText);
        return panel;
    }

    /// <summary>Big-number-over-small-key stat cell used inside hairline grids.</summary>
    public static Control StatCell(string value, string key)
    {
        var cell = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 54) };
        cell.AddThemeStyleboxOverride("panel", Box(CellBg, Colors.Transparent, 0, 12, 8));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        cell.AddChild(box);
        box.AddChild(Text(value, 16, TextBright));
        box.AddChild(Text(key, 8, Muted, 2));
        return cell;
    }

    /// <summary>Chip fill/border/text colors for one <see cref="ChipRole"/>.</summary>
    internal static (Color Fill, Color Border, Color Text) ChipColors(ChipRole role) => role switch
    {
        ChipRole.Decision => (Accent, Accent, new Color(0.04f, 0.09f, 0.15f)),
        ChipRole.Gain => (new Color(Positive.R, Positive.G, Positive.B, 0.07f), new Color(Positive.R, Positive.G, Positive.B, 0.55f), Positive),
        ChipRole.Impaired => (new Color(Warning.R, Warning.G, Warning.B, 0.07f), new Color(Warning.R, Warning.G, Warning.B, 0.55f), Warning),
        ChipRole.Loss => (new Color(Negative.R, Negative.G, Negative.B, 0.07f), new Color(Negative.R, Negative.G, Negative.B, 0.55f), Negative),
        ChipRole.Instinct => (new Color(Instinct.R, Instinct.G, Instinct.B, 0.07f), new Color(Instinct.R, Instinct.G, Instinct.B, 0.55f), Instinct),
        _ => (Colors.Transparent, Hairline, Muted),
    };

    /// <summary>
    /// Small bordered state tag. Decision chips are filled blue and pulse;
    /// every other role is a quiet outline in its semantic color.
    /// </summary>
    public static Control Chip(string label, ChipRole role, string tooltip = null)
    {
        (Color fill, Color border, Color text) = ChipColors(role);
        var panel = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        panel.AddThemeStyleboxOverride("panel", Box(fill, border, 1, 8, 2));
        if (!string.IsNullOrEmpty(tooltip))
            panel.TooltipText = tooltip;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(row);
        if (role == ChipRole.Decision)
            row.AddChild(new PulseDot { DotColor = text, CustomMinimumSize = new Vector2(8, 8), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        Label chipText = Text(label, 9, text, 2);
        chipText.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(chipText);
        return panel;
    }

    /// <summary>
    /// Compact hairline-ruled stat row: tiny key over a light value, one column
    /// per stat, dividers between columns. Fits inside roster/candidate cards.
    /// </summary>
    public static Control StatRow(params (string Key, string Value, Color? ValueColor)[] stats)
    {
        var frame = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = Hairline };
        style.BorderWidthTop = style.BorderWidthBottom = 1;
        style.ContentMarginTop = style.ContentMarginBottom = 7;
        frame.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        frame.AddChild(row);
        for (int i = 0; i < stats.Length; i++)
        {
            if (i > 0)
                row.AddChild(new ColorRect { CustomMinimumSize = new Vector2(1, 0), Color = Hairline });
            var cell = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            cell.AddThemeConstantOverride("separation", 1);
            cell.AddChild(Text(stats[i].Key, 8, Muted, 2));
            cell.AddChild(Text(stats[i].Value, 15, stats[i].ValueColor ?? Body));
            row.AddChild(cell);
        }
        return frame;
    }

    /// <summary>Sparse star backdrop shared by every screen's _Draw.</summary>
    public static void DrawStarfield(CanvasItem canvas, ulong seed, float width, float height, int count = 150)
    {
        var rng = new RandomNumberGenerator { Seed = seed };
        for (int i = 0; i < count; i++)
        {
            var point = new Vector2(rng.RandfRange(0, width), rng.RandfRange(0, height));
            canvas.DrawCircle(point, rng.RandfRange(0.4f, 1.2f), new Color(0.74f, 0.84f, 1f, rng.RandfRange(0.05f, 0.28f)));
        }
    }

    /// <summary>Faint radial glow approximated with stacked translucent discs.</summary>
    public static void DrawNebula(CanvasItem canvas, Vector2 center, Color color, int layers = 14, float step = 22f, float alpha = 0.008f)
    {
        for (int i = layers; i >= 1; i--)
            canvas.DrawCircle(center, i * step, new Color(color.R, color.G, color.B, alpha));
    }
}

/// <summary>Orbital rings framing a ship sprite: one solid, one dashed.</summary>
public partial class OrbitRings : Control
{
    public override void _Draw()
    {
        Vector2 center = Size / 2f;
        DrawArc(center, 62, 0, Mathf.Tau, 64, new Color(0.35f, 0.67f, 1f, 0.25f), 1, true);
        const int dashes = 24;
        for (int i = 0; i < dashes; i++)
        {
            float from = Mathf.Tau * i / dashes;
            DrawArc(center, 47, from, from + Mathf.Tau / dashes * 0.55f, 6, new Color(0.35f, 0.67f, 1f, 0.16f), 1, true);
        }
    }
}

/// <summary>Thin XP progress track with a glowing fill.</summary>
public partial class XpTrack : Control
{
    public float Ratio;

    public override void _Draw()
    {
        float mid = Size.Y / 2f;
        DrawRect(new Rect2(0, mid - 1, Size.X, 2), new Color(0.47f, 0.71f, 0.9f, 0.18f));
        float width = Size.X * Mathf.Clamp(Ratio, 0f, 1f);
        if (width <= 0)
            return;
        DrawRect(new Rect2(0, mid - 3, width, 6), new Color(0.302f, 0.639f, 1f, 0.22f));
        DrawRect(new Rect2(0, mid - 2, width, 4), new Color(0.302f, 0.639f, 1f));
    }
}

/// <summary>Gutter cell of a progression rail: node circle plus connecting line.</summary>
public partial class RailGutter : Control
{
    public bool Done;
    public bool Locked;
    public bool Last;

    public override void _Draw()
    {
        var accent = new Color(0.302f, 0.639f, 1f);
        var center = new Vector2(8, 9);
        if (!Last)
            DrawLine(new Vector2(8, 17), new Vector2(8, Size.Y), Locked ? new Color(0.47f, 0.71f, 0.9f, 0.14f) : new Color(accent.R, accent.G, accent.B, 0.45f), 1);
        if (Done)
            DrawCircle(center, 5, accent);
        else
            DrawArc(center, 5, 0, Mathf.Tau, 24, Locked ? new Color(0.33f, 0.44f, 0.55f) : accent, 1.5f, true);
    }
}

/// <summary>Small round status indicator (the default font lacks a bullet glyph).</summary>
public partial class StatusDot : Control
{
    public Color DotColor = Colors.White;

    public override void _Draw() => DrawCircle(Size / 2f, 4, DotColor);
}

/// <summary>Semantic role of a <see cref="SignalUi.Chip"/> — see the palette color-role rules.</summary>
public enum ChipRole
{
    /// <summary>A player decision is waiting. Blue, filled, pulses.</summary>
    Decision,
    /// <summary>Healthy or gained: level-ups, new abilities, positive traits.</summary>
    Gain,
    /// <summary>Impaired: wounds, hull damage, negative traits. Nothing else is amber.</summary>
    Impaired,
    /// <summary>Lost: deaths, failed objectives.</summary>
    Loss,
    /// <summary>Dormant or informational: future unlocks, empty slots.</summary>
    Dormant,
    /// <summary>Pilot learning: instincts and mastered maneuvers. Purple.</summary>
    Instinct,
}

/// <summary>Slowly breathing dot; reserved for pending-decision markers.</summary>
public partial class PulseDot : Control
{
    public Color DotColor = Colors.White;
    double _time;

    public override void _Process(double delta)
    {
        _time += delta;
        Modulate = new Color(1, 1, 1, 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin((float)_time * 4f)));
    }

    public override void _Draw() => DrawCircle(Size / 2f, 3, DotColor);
}

/// <summary>
/// Full-width clickable strip announcing a pending decision: pulsing dot,
/// label on the left, action verb on the right. The strip itself is the button.
/// </summary>
public partial class AttentionStrip : PanelContainer
{
    public event System.Action Pressed;

    static StyleBoxFlat Style(float fillAlpha, float borderAlpha)
    {
        Color a = SignalUi.Accent;
        return SignalUi.Box(new Color(a.R, a.G, a.B, fillAlpha), new Color(a.R, a.G, a.B, borderAlpha), 1, 12, 8);
    }

    /// <summary>Engine-required parameterless constructor; use the label/action one in code.</summary>
    public AttentionStrip() { }

    public AttentionStrip(string label, string action)
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
        AddThemeStyleboxOverride("panel", Style(0.1f, 1f));
        MouseEntered += () => AddThemeStyleboxOverride("panel", Style(0.18f, 1f));
        MouseExited += () => AddThemeStyleboxOverride("panel", Style(0.1f, 1f));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        AddChild(row);
        row.AddChild(new PulseDot { DotColor = SignalUi.Accent, CustomMinimumSize = new Vector2(8, 8), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        Label text = SignalUi.Text(label, 10, SignalUi.Accent, 3);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(text);
        row.AddChild(SignalUi.Text(action, 10, SignalUi.Accent, 3));
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            Pressed?.Invoke();
    }
}

/// <summary>
/// Vertical scroll area for touch screens. Dragging anywhere inside it
/// scrolls, including on top of <see cref="TapCard"/> children, and a drag is
/// never mistaken for a tap. Works the same with a desktop mouse.
/// </summary>
public partial class TouchScroll : ScrollContainer
{
    const float DragThreshold = 16f;
    bool _pressed;
    Vector2 _pressPosition;
    int _pressScroll;

    /// <summary>True while the current press has turned into a scroll.</summary>
    public bool IsDragging { get; private set; }

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        // The built-in drag only works when the platform reports a touch
        // screen, and child buttons block it; this container scrolls itself.
        ScrollDeadzone = 100000;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed)
            {
                _pressed = true;
                IsDragging = false;
                _pressPosition = button.GlobalPosition;
                _pressScroll = ScrollVertical;
            }
            else
            {
                _pressed = false;
                // Children see the release before this container does, so
                // they can still tell it ended a drag; clear it afterwards.
                CallDeferred(MethodName.EndDrag);
            }
        }
        else if (@event is InputEventMouseMotion motion && _pressed)
        {
            float dy = motion.GlobalPosition.Y - _pressPosition.Y;
            if (!IsDragging && Mathf.Abs(dy) > DragThreshold)
                IsDragging = true;
            if (IsDragging)
            {
                ScrollVertical = _pressScroll - Mathf.RoundToInt(dy);
                AcceptEvent();
            }
        }
    }

    void EndDrag() => IsDragging = false;
}

/// <summary>
/// A card that acts as one large button. It fires <see cref="Tapped"/> only
/// for a real tap, so it can live inside a <see cref="TouchScroll"/>.
/// </summary>
public partial class TapCard : PanelContainer
{
    public event System.Action Tapped;
    bool _pressed;
    bool _disabled;
    StyleBoxFlat _normal, _pressedStyle, _disabledStyle;

    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
            ApplyStyle();
        }
    }

    /// <summary>Marks the card as the current choice: an accent border that stays until cleared.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            ApplyStyle();
        }
    }

    bool _selected;
    StyleBoxFlat _selectedStyle;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Pass;
        _normal = SignalUi.Box(new Color(SignalUi.CellBg, 0.92f), SignalUi.Hairline, 2, 16, 12);
        _pressedStyle = SignalUi.Box(new Color(SignalUi.Accent, 0.16f), SignalUi.Accent, 2, 16, 12);
        _selectedStyle = SignalUi.Box(new Color(SignalUi.Accent, 0.14f), SignalUi.Accent, 3, 16, 12);
        _disabledStyle = SignalUi.Box(new Color(SignalUi.CellBg, 0.5f), new Color(SignalUi.Hairline, 0.4f), 2, 16, 12);
        ApplyStyle();
    }

    void ApplyStyle()
    {
        if (_normal == null)
            return;
        AddThemeStyleboxOverride("panel", _disabled ? _disabledStyle : _pressed ? _pressedStyle : _selected ? _selectedStyle : _normal);
        Modulate = _disabled ? new Color(1, 1, 1, 0.55f) : Colors.White;
    }

    public override void _GuiInput(InputEvent @event)
    {
        TouchScroll scroll = FindScroll();
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed)
            {
                _pressed = !_disabled;
            }
            else
            {
                bool tapped = _pressed && !_disabled && scroll?.IsDragging != true &&
                    GetGlobalRect().HasPoint(button.GlobalPosition);
                _pressed = false;
                if (tapped)
                    Tapped?.Invoke();
            }
            ApplyStyle();
        }
        else if (@event is InputEventMouseMotion && _pressed && scroll?.IsDragging == true)
        {
            _pressed = false;
            ApplyStyle();
        }
    }

    TouchScroll FindScroll()
    {
        for (Node node = GetParent(); node != null; node = node.GetParent())
            if (node is TouchScroll scroll)
                return scroll;
        return null;
    }
}
