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
    //   Warning     = impaired ONLY (wounds, hull damage, negative traits);
    //   Negative    = losses and the enemy.
    public static readonly Color Bg = new(0.024f, 0.039f, 0.071f);
    public static readonly Color TextBright = new(0.933f, 0.965f, 1f);
    public static readonly Color Body = new(0.788f, 0.839f, 0.886f);
    public static readonly Color Muted = new(0.373f, 0.478f, 0.573f);
    public static readonly Color Dim = new(0.33f, 0.41f, 0.49f);
    public static readonly Color Accent = new(0.302f, 0.639f, 1f);
    public static readonly Color Positive = new(0.341f, 0.902f, 0.643f);
    public static readonly Color Warning = new(1f, 0.69f, 0.24f);
    public static readonly Color Negative = new(1f, 0.35f, 0.3f);
    public static readonly Color Hairline = new(0.47f, 0.71f, 0.9f, 0.22f);
    public static readonly Color CellBg = new(0.031f, 0.063f, 0.114f);

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
/// Side-elevation cutaway of the flagship, sectioned into boardable decks.
/// Full size it is interactive (hover highlights a deck, click boards it);
/// Mini renders a stretched locator strip for sub-scene headers.
/// </summary>
public partial class DeckSchematic : Control
{
    // Design-space geometry (640x300), nose to the right. Deck x-ranges run
    // bow to stern in AthenaDecks order: hangar, crew, shipyard, memorial.
    static readonly (float X1, float X2)[] DeckSpans = { (410, 540), (280, 410), (150, 280), (60, 150) };
    static readonly Vector2[] HullOutline =
    {
        new(620, 150), new(540, 100), new(150, 82), new(60, 116),
        new(34, 150), new(60, 184), new(150, 218), new(540, 200),
    };

    public bool Mini;
    public int Highlight = -1;
    public string[] Labels;   // full mode only; "01 HANGAR BAY" etc, indexed like DeckSpans

    public event System.Action<int> DeckHovered;
    public event System.Action<int> DeckSelected;

    static float TopY(float x) =>
        x >= 540 ? Mathf.Lerp(100, 150, (x - 540) / 80f) :
        x >= 150 ? Mathf.Lerp(82, 100, (x - 150) / 390f) :
        x >= 60 ? Mathf.Lerp(116, 82, (x - 60) / 90f) :
        Mathf.Lerp(150, 116, (x - 34) / 26f);

    static float BottomY(float x) =>
        x >= 540 ? Mathf.Lerp(200, 150, (x - 540) / 80f) :
        x >= 150 ? Mathf.Lerp(218, 200, (x - 150) / 390f) :
        x >= 60 ? Mathf.Lerp(184, 218, (x - 60) / 90f) :
        Mathf.Lerp(150, 184, (x - 34) / 26f);

    (float Sx, float Sy, Vector2 Off) Transform()
    {
        if (Mini)
            return (Size.X / 640f, Size.Y / 300f, Vector2.Zero);
        float scale = Mathf.Min(Size.X / 640f, Size.Y / 300f);
        return (scale, scale, (Size - new Vector2(640, 300) * scale) * 0.5f);
    }

    public override void _Draw()
    {
        (float sx, float sy, Vector2 off) = Transform();
        Vector2 Map(float x, float y) => off + new Vector2(x * sx, y * sy);

        if (!Mini)
        {
            // Faint orbit ellipse behind the hull.
            var orbit = new Vector2[41];
            for (int i = 0; i <= 40; i++)
            {
                float angle = Mathf.Tau * i / 40f;
                orbit[i] = Map(320 + 300 * Mathf.Cos(angle), 150 + 120 * Mathf.Sin(angle));
            }
            DrawPolyline(orbit, new Color(0.47f, 0.71f, 0.9f, 0.09f), 1, true);
        }

        var hull = new Vector2[HullOutline.Length + 1];
        for (int i = 0; i < HullOutline.Length; i++)
            hull[i] = Map(HullOutline[i].X, HullOutline[i].Y);
        hull[^1] = hull[0];
        DrawColoredPolygon(hull[..^1], new Color(SignalUi.CellBg.R, SignalUi.CellBg.G, SignalUi.CellBg.B, 0.85f));

        // Engine wash aft of the hull.
        Color engine = new(SignalUi.Accent.R, SignalUi.Accent.G, SignalUi.Accent.B, 0.7f);
        float engineWidth = Mini ? 1f : 2f;
        DrawLine(Map(18, 136), Map(40, 136), engine, engineWidth);
        DrawLine(Map(12, 150), Map(36, 150), new Color(engine.R, engine.G, engine.B, 0.45f), engineWidth);
        DrawLine(Map(18, 164), Map(40, 164), engine, engineWidth);

        if (Highlight >= 0 && Highlight < DeckSpans.Length)
        {
            (float x1, float x2) = DeckSpans[Highlight];
            var quad = new[] { Map(x1, TopY(x1)), Map(x2, TopY(x2)), Map(x2, BottomY(x2)), Map(x1, BottomY(x1)) };
            DrawColoredPolygon(quad, new Color(SignalUi.Accent.R, SignalUi.Accent.G, SignalUi.Accent.B, Mini ? 0.35f : 0.14f));
            if (!Mini)
            {
                var edge = new Vector2[5];
                quad.CopyTo(edge, 0);
                edge[4] = quad[0];
                DrawPolyline(edge, new Color(SignalUi.Accent.R, SignalUi.Accent.G, SignalUi.Accent.B, 0.7f), 1, true);
            }
        }

        DrawPolyline(hull, new Color(0.47f, 0.71f, 0.9f, 0.45f), 1, true);
        var divider = new Color(0.47f, 0.71f, 0.9f, 0.3f);
        foreach (float x in new[] { 150f, 280f, 410f, 540f })
            DrawLine(Map(x, TopY(x)), Map(x, BottomY(x)), divider, 1);

        if (Mini || Labels == null)
            return;

        Font font = SignalUi.Tracked(3);
        int fontSize = Mathf.Max(8, Mathf.RoundToInt(11 * sx));
        for (int i = 0; i < DeckSpans.Length && i < Labels.Length; i++)
        {
            (float x1, float x2) = DeckSpans[i];
            float cx = (x1 + x2) / 2f;
            bool above = i == 0 || i == 2;   // hangar + shipyard label above, others below
            bool active = i == Highlight;
            Color leader = active
                ? new Color(SignalUi.Accent.R, SignalUi.Accent.G, SignalUi.Accent.B, 0.6f)
                : new Color(0.47f, 0.71f, 0.9f, 0.35f);
            if (above)
                DrawLine(Map(cx, TopY(cx)), Map(cx, 44), leader, 1);
            else
                DrawLine(Map(cx, BottomY(cx)), Map(cx, 256), leader, 1);
            DrawString(font, Map(cx - 110, above ? 36 : 274), Labels[i],
                HorizontalAlignment.Center, 220 * sx, fontSize, active ? SignalUi.Accent : SignalUi.Muted);
        }
    }

    int DeckAt(Vector2 local)
    {
        (float sx, float sy, Vector2 off) = Transform();
        float x = (local.X - off.X) / sx;
        float y = (local.Y - off.Y) / sy;
        for (int i = 0; i < DeckSpans.Length; i++)
        {
            if (x >= DeckSpans[i].X1 && x <= DeckSpans[i].X2 && y >= TopY(x) - 3 && y <= BottomY(x) + 3)
                return i;
        }
        return -1;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (Mini)
            return;
        if (@event is InputEventMouseMotion motion)
        {
            int deck = DeckAt(motion.Position);
            MouseDefaultCursorShape = deck >= 0 ? CursorShape.PointingHand : CursorShape.Arrow;
            if (deck >= 0 && deck != Highlight)
            {
                Highlight = deck;
                QueueRedraw();
                DeckHovered?.Invoke(deck);
            }
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
        {
            int deck = DeckAt(click.Position);
            if (deck >= 0)
                DeckSelected?.Invoke(deck);
        }
    }
}
