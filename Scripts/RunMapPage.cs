using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

public partial class RunScreen
{
    /// <summary>
    /// The sector map: header with the menu, the branching route from the
    /// entrance (bottom) to the boss (top), the squadron at a glance, and a
    /// sheet describing the tapped stop with a Go button.
    /// </summary>
    Control BuildMapPage()
    {
        VBoxContainer page = Stack(16);

        Button menu = TouchButton("MENU", fontSize: FontCaption);
        menu.CustomMinimumSize = new Vector2(120, TouchTarget);
        menu.Pressed += () =>
        {
            _confirmAbandon = false;
            _showMenu = !_showMenu;
            Render();
        };
        HBoxContainer header = Row(16);
        Control title = Header($"SECTOR {Run.Sector} OF {RunContent.SectorCount}", Run.SectorName);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        header.AddChild(menu);
        page.AddChild(header);

        if (_showMenu)
        {
            page.AddChild(BuildMapMenu());
            return page;
        }

        if (!string.IsNullOrEmpty(Run.Notice))
        {
            PanelContainer notice = Card(20, 14, new Color(Accent, 0.6f));
            notice.AddChild(Text(Run.Notice, FontCaption, Body, 0, wrap: true));
            page.AddChild(notice);
        }

        var map = new SectorMapView
        {
            Run = Run,
            SelectedId = _selectedNodeId,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        map.NodeTapped += id =>
        {
            _selectedNodeId = id;
            Render();
        };
        page.AddChild(map);

        // Squadron at a glance; tapping opens the full squadron page.
        var squad = new TapCard();
        squad.Tapped += () =>
        {
            _showSquadron = true;
            Render();
        };
        HBoxContainer squadRow = Row(12);
        foreach (Pilot pilot in Run.Living)
            squadRow.AddChild(new PilotChip { Pilot = pilot, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        squad.AddChild(squadRow);
        page.AddChild(squad);

        page.AddChild(BuildStopSheet());
        return page;
    }

    bool _showMenu;

    Control BuildMapMenu()
    {
        VBoxContainer stack = Stack(16);
        stack.AddChild(Spacer());
        Button resume = TouchButton("BACK TO THE MAP", primary: true);
        resume.Pressed += () =>
        {
            _showMenu = false;
            Render();
        };
        stack.AddChild(resume);
        Button home = TouchButton("SAVE AND QUIT");
        home.Pressed += () => Go("res://Scenes/HomeScreen.tscn");
        stack.AddChild(home);
        Button abandon = TouchButton(_confirmAbandon ? "TAP AGAIN TO ABANDON" : "ABANDON RUN");
        abandon.AddThemeColorOverride("font_color", Negative);
        abandon.Pressed += () =>
        {
            if (!_confirmAbandon)
            {
                _confirmAbandon = true;
                Render();
                return;
            }
            RunState.EndAndDelete();
            Go("res://Scenes/HomeScreen.tscn");
        };
        stack.AddChild(abandon);
        stack.AddChild(Text("The run saves after every stop, so you can close the game at any time.", FontCaption, Muted, 0, wrap: true));
        stack.AddChild(Spacer());
        return stack;
    }

    /// <summary>
    /// Height of the stop sheet. The sheet lives in a holder of exactly this
    /// height, so no stop's text can resize the map above it and move the stops.
    /// </summary>
    const float StopSheetHeight = 330f;

    /// <summary>
    /// What the tapped stop is, and the button to travel there. Every state
    /// fills the same four slots (title, two-line description, detail line,
    /// button), so tapping between stops changes text, never layout.
    /// </summary>
    Control BuildStopSheet()
    {
        // A plain Control does not grow to fit its children, unlike a
        // container; clipping keeps any overlong text inside the sheet.
        var holder = new Control
        {
            CustomMinimumSize = new Vector2(0, StopSheetHeight),
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        PanelContainer sheet = Card(24, 18);
        sheet.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        holder.AddChild(sheet);
        VBoxContainer stack = Stack(12);
        sheet.AddChild(stack);

        RunNode node = _selectedNodeId is int id ? Run.Node(id) : null;
        string title = "CHOOSE YOUR NEXT STOP";
        Color titleColor = TextBright;
        string summary = "Tap a glowing stop on the map. Routes only lead upward.";
        string detail = "";
        string action = "SELECT A STOP";
        bool canGo = false;

        if (node != null)
        {
            title = RunContent.KindName(node.Kind);
            titleColor = SectorMapView.KindColor(node.Kind);
            summary = RunContent.KindSummary(node.Kind, Run.Sector);
            detail = StopDetail(node);
            bool isBattle = node.BattleKind != null;
            if (node.Visited)
                action = "ALREADY VISITED";
            else if (!Run.Reachable.Contains(node))
                action = "NOT ON YOUR ROUTE";
            else
            {
                action = isBattle ? "FLY TO BATTLE" : "SET COURSE";
                canGo = !isBattle || Run.Deployable().Count > 0;
            }
        }

        Label titleLabel = Text(title, FontBody, titleColor, 3);
        titleLabel.ClipText = true;
        stack.AddChild(titleLabel);
        Label summaryLabel = Text(summary, FontCaption, Body, 0, wrap: true);
        summaryLabel.MaxLinesVisible = 2;
        summaryLabel.CustomMinimumSize = new Vector2(0, 68);
        stack.AddChild(summaryLabel);
        Label detailLabel = Text(detail, FontCaption, Muted, 2);
        detailLabel.ClipText = true;
        detailLabel.CustomMinimumSize = new Vector2(0, 30);
        stack.AddChild(detailLabel);
        stack.AddChild(Spacer()); // pins the button to the bottom of the sheet

        Button go = TouchButton(action, primary: true);
        go.Disabled = !canGo;
        if (canGo)
        {
            go.Pressed += () =>
            {
                Run.EnterNode(node);
                _selectedNodeId = null;
                _briefingPicksInitialized = false;
                _eventBattleAcknowledged = false;
                Render();
            };
        }
        stack.AddChild(go);
        return holder;
    }

    /// <summary>One line of numbers about a stop: the fight's size and any bonus, or what it offers.</summary>
    string StopDetail(RunNode node)
    {
        if (node.BattleKind is RunNodeKind kind)
        {
            BattleMission mission = RunContent.BuildMission(node, Run.Sector, kind);
            string waves = mission.Reinforcements.Length > 0 ? $" +{mission.Reinforcements.Length}" : "";
            string aces = mission.Aces.Length switch { 0 => "", 1 => " · ACE", _ => $" · {mission.Aces.Length} ACES" };
            string detail = $"THREAT {mission.Threat} · {Plural(mission.EnemySquad.Length, "HOSTILE")}{waves}{aces}";
            return kind == RunNodeKind.Elite ? detail + " · MODULE CRATE" : detail;
        }
        return node.Kind switch
        {
            RunNodeKind.Repair => "FULL REPAIRS · TREAT ONE SCAR",
            RunNodeKind.Recruit => $"ROSTER {Run.Living.Count()}/{RunState.RosterLimit}",
            _ => "OUTCOME UNKNOWN",
        };
    }
}

/// <summary>
/// Draws a sector's stops as a route graph, entrance at the bottom and boss at
/// the top, and reports taps on stops. Reachable stops pulse.
/// </summary>
public partial class SectorMapView : Control
{
    public RunState Run;
    public int? SelectedId;
    public event Action<int> NodeTapped;

    const float NodeRadius = 38f;
    const float BossRadius = 52f;
    double _time;
    Vector2 _pressPosition;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Stop;

    public override void _Process(double delta)
    {
        _time += delta;
        QueueRedraw();
    }

    public static Color KindColor(RunNodeKind kind) => kind switch
    {
        RunNodeKind.Skirmish => new Color(1f, 0.55f, 0.45f),
        RunNodeKind.Elite => SignalUi.Warning,
        RunNodeKind.Boss => SignalUi.Negative,
        RunNodeKind.Repair => SignalUi.Positive,
        RunNodeKind.Recruit => SignalUi.Accent,
        _ => new Color(0.75f, 0.6f, 1f),
    };

    Vector2 PositionOf(RunNode node)
    {
        int layers = Run.Nodes.Max(n => n.Layer);
        float top = BossRadius + 30f;
        float bottom = Size.Y - NodeRadius - 40f;
        float y = Mathf.Lerp(bottom, top, (node.Layer - 1) / (float)Mathf.Max(1, layers - 1));
        float x = Size.X * (node.Slot + 0.5f) / node.SlotCount;
        return new Vector2(x, y);
    }

    /// <summary>Where a stop is drawn, in viewport coordinates.</summary>
    public Vector2 GlobalPositionOfNode(int id) => GetGlobalTransformWithCanvas() * PositionOf(Run.Node(id));

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
            return;
        if (button.Pressed)
        {
            _pressPosition = button.Position;
            return;
        }
        if (button.Position.DistanceTo(_pressPosition) > 24f)
            return;
        RunNode hit = Run.Nodes
            .OrderBy(node => PositionOf(node).DistanceTo(button.Position))
            .FirstOrDefault(node => PositionOf(node).DistanceTo(button.Position) <= NodeRadius + 28f);
        if (hit != null)
        {
            AcceptEvent();
            NodeTapped?.Invoke(hit.Id);
        }
    }

    public override void _Draw()
    {
        if (Run == null || Run.Nodes.Count == 0)
            return;
        var reachable = new HashSet<int>(Run.Reachable.Select(n => n.Id));
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_time * 4f);

        // Routes first, so stops sit on top of them.
        foreach (RunNode node in Run.Nodes)
        {
            foreach (int nextId in node.Next)
            {
                RunNode next = Run.Node(nextId);
                bool travelled = node.Visited && next.Visited;
                bool open = node.Id == Run.CurrentNodeId && reachable.Contains(nextId);
                Color color = travelled ? SignalUi.Accent
                    : open ? new Color(SignalUi.Accent, 0.5f + 0.4f * pulse)
                    : new Color(SignalUi.Hairline, 0.5f);
                DrawDashedOrSolid(PositionOf(node), PositionOf(next), color, travelled || open ? 4f : 2f, dashed: !travelled && !open);
            }
        }

        // The route in from the sector entrance.
        Vector2 entrance = new(Size.X / 2f, Size.Y - 8f);
        foreach (RunNode first in Run.Nodes.Where(n => n.Layer == 1))
        {
            bool open = Run.CurrentNodeId < 0;
            bool travelled = first.Visited;
            DrawDashedOrSolid(entrance, PositionOf(first),
                travelled ? SignalUi.Accent : open ? new Color(SignalUi.Accent, 0.5f + 0.4f * pulse) : new Color(SignalUi.Hairline, 0.5f),
                travelled || open ? 4f : 2f, dashed: !travelled && !open);
        }

        Font font = SignalUi.Display;
        foreach (RunNode node in Run.Nodes)
        {
            Vector2 center = PositionOf(node);
            float radius = node.Kind == RunNodeKind.Boss ? BossRadius : NodeRadius;
            Color color = KindColor(node.Kind);
            bool isReachable = reachable.Contains(node.Id);
            bool isCurrent = node.Id == Run.CurrentNodeId;
            bool dim = !isReachable && !node.Visited && !isCurrent;

            Color fill = node.Visited ? new Color(color, 0.18f) : new Color(SignalUi.Bg, 0.95f);
            DrawCircle(center, radius, fill);
            if (isReachable)
                DrawCircle(center, radius + 6f + 6f * pulse, new Color(color, 0.12f));
            DrawArc(center, radius, 0f, Mathf.Tau, 48, dim ? new Color(color, 0.35f) : color, isReachable ? 4f : 2.5f, true);
            if (node.Id == SelectedId)
                DrawArc(center, radius + 10f, 0f, Mathf.Tau, 48, SignalUi.TextBright, 3f, true);

            DrawKindIcon(node.Kind, center, radius * 0.5f, dim ? new Color(color, 0.45f) : color);
            if (node.Visited && !isCurrent)
                DrawLine(center + new Vector2(-radius * 0.7f, radius * 0.7f), center + new Vector2(radius * 0.7f, -radius * 0.7f),
                    new Color(SignalUi.Muted, 0.8f), 3f, true);

            string label = RunContent.KindName(node.Kind);
            int size = SignalUi.FontMicro;
            Vector2 extent = font.GetStringSize(label, HorizontalAlignment.Left, -1f, size);
            DrawString(font, center + new Vector2(-extent.X / 2f, radius + 26f), label, HorizontalAlignment.Left, -1f, size,
                dim ? SignalUi.Dim : SignalUi.Body);
        }

        // The squadron's position: the current stop, or the entrance.
        Vector2 marker = Run.CurrentNodeId >= 0 ? PositionOf(Run.CurrentNode) + new Vector2(NodeRadius + 20f, -NodeRadius + 6f) : entrance + new Vector2(0, -26f);
        Pilot lead = Run.Living.FirstOrDefault();
        if (lead != null)
        {
            // The lead's hull, cropped and fitted into a 48px box.
            Texture2D texture = lead.Ship.GetSkin(0).Icon;
            Vector2 fit = texture.GetSize() * (48f / Mathf.Max(texture.GetWidth(), texture.GetHeight()));
            DrawTextureRect(texture, new Rect2(marker - fit / 2f, fit), false);
        }
    }

    void DrawDashedOrSolid(Vector2 from, Vector2 to, Color color, float width, bool dashed)
    {
        if (!dashed)
        {
            DrawLine(from, to, color, width, true);
            return;
        }
        float length = from.DistanceTo(to);
        Vector2 dir = (to - from) / Mathf.Max(length, 0.001f);
        for (float t = 0; t < length; t += 18f)
            DrawLine(from + dir * t, from + dir * Mathf.Min(t + 9f, length), color, width, true);
    }

    /// <summary>Simple drawn pictograms; the default font has no symbol glyphs.</summary>
    void DrawKindIcon(RunNodeKind kind, Vector2 c, float r, Color color)
    {
        const float w = 4f;
        switch (kind)
        {
            case RunNodeKind.Skirmish:
                DrawLine(c + new Vector2(-r, -r), c + new Vector2(r, r), color, w, true);
                DrawLine(c + new Vector2(r, -r), c + new Vector2(-r, r), color, w, true);
                break;
            case RunNodeKind.Elite:
            case RunNodeKind.Boss:
                var star = new Vector2[11];
                for (int i = 0; i < 10; i++)
                    star[i] = c + Vector2.Up.Rotated(i * Mathf.Pi / 5f) * (i % 2 == 0 ? r * 1.1f : r * 0.45f);
                star[10] = star[0];
                if (kind == RunNodeKind.Boss)
                    DrawColoredPolygon(star[..10], new Color(color, 0.8f));
                DrawPolyline(star, color, w, true);
                break;
            case RunNodeKind.Repair:
                DrawLine(c + new Vector2(0, -r), c + new Vector2(0, r), color, w * 1.6f, true);
                DrawLine(c + new Vector2(-r, 0), c + new Vector2(r, 0), color, w * 1.6f, true);
                break;
            case RunNodeKind.Recruit:
                DrawArc(c + new Vector2(0, -r * 0.45f), r * 0.42f, 0f, Mathf.Tau, 20, color, w, true);
                DrawArc(c + new Vector2(0, r * 1.05f), r * 0.85f, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 16, color, w, true);
                break;
            default:
                Font font = SignalUi.Display;
                int size = Mathf.RoundToInt(r * 2.2f);
                Vector2 extent = font.GetStringSize("?", HorizontalAlignment.Left, -1f, size);
                DrawString(font, c + new Vector2(-extent.X / 2f, size * 0.36f), "?", HorizontalAlignment.Left, -1f, size, color);
                break;
        }
    }
}

/// <summary>Compact pilot token for the map: callsign, hull bar and status line.</summary>
public partial class PilotChip : Control
{
    public Pilot Pilot;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 100);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        Font font = SignalUi.Display;
        // Shrink long callsigns to fit a five-pilot row rather than overlap.
        int size = SignalUi.FontCaption;
        while (size > 16 && font.GetStringSize(Pilot.Callsign, HorizontalAlignment.Left, -1f, size).X > Size.X)
            size -= 2;
        DrawString(font, new Vector2(0, 24), Pilot.Callsign, HorizontalAlignment.Left, Size.X, size, SignalUi.TextBright);
        DrawString(font, new Vector2(0, 50), $"LV {Pilot.Level}", HorizontalAlignment.Left, Size.X, SignalUi.FontMicro, SignalUi.Muted);
        float fraction = Mathf.Clamp(Pilot.Hull / (float)Pilot.MaxHull, 0f, 1f);
        DrawRect(new Rect2(0, 60, Size.X, 8), new Color(1, 1, 1, 0.12f));
        DrawRect(new Rect2(0, 60, Size.X * fraction, 8), fraction < 0.35f ? SignalUi.Warning : SignalUi.Positive);
    }
}
