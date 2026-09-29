using Godot;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

/// <summary>Sector command view. Every system is available; each world has one active mission.</summary>
public partial class CampaignMap : Node2D
{
    const float ScreenW = 1152f;
    const float ScreenH = 648f;
    const float StarCellSize = 128f;
    const int StarsPerCell = 12;

    readonly Dictionary<string, Rect2> _systemAreas = new();
    string _selectedSystem;
    Label _systemTitle, _systemInfo, _status;
    CanvasLayer _ui;
    Vector2 _mapBaseOffset;
    Vector2 _mapPan;
    float _dragDistance;
    bool _dragging;
    float _pulse;

    public override void _Ready()
    {
        CampaignData.Initialize();
        RenderingServer.SetDefaultClearColor(Bg);
        BuildUi(); AddStarSprites();
        _selectedSystem = Systems().First();
        SetSystem(_selectedSystem);
        GetViewport().SizeChanged += FitToViewport;
        FitToViewport();
    }

    IEnumerable<string> Systems() => CampaignData.Systems.Select(system => system.Name);

    void AddStarSprites()
    {
        foreach (string system in Systems())
        {
            Vector2 position = CampaignData.GetSystem(system).MapPosition;
            var star = new AnimatedCelestial { Position = position - new Vector2(40, 40), Size = new Vector2(80, 80) };
            star.Setup("res://Assets/CelestialBodies/Star1.png", 200);
            AddChild(star);
        }
    }

    void BuildUi()
    {
        var ui = new CanvasLayer(); _ui = ui; AddChild(ui);

        var head = new HBoxContainer { Position = new Vector2(56, 28), Size = new Vector2(1040, 62) };
        head.AddThemeConstantOverride("separation", 24);
        ui.AddChild(head);
        var title = Text("SECTOR COMMAND", 30, TextBright, 6);
        title.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        head.AddChild(title);
        var metaBox = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        metaBox.AddThemeConstantOverride("margin_bottom", 7);
        metaBox.AddChild(Text("HELIOS SECTOR · FLAGSHIP ATHENA", 9, Muted, 4));
        head.AddChild(metaBox);
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var statusBox = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        statusBox.AddThemeConstantOverride("margin_bottom", 9);
        _status = Text("", 10, Muted, 3);
        statusBox.AddChild(_status);
        head.AddChild(statusBox);
        ui.AddChild(new ColorRect { Position = new Vector2(56, 100), Size = new Vector2(1040, 1), Color = Hairline });

        var panel = new PanelContainer { Position = new Vector2(805, 122), Size = new Vector2(323, 380) };
        panel.AddThemeStyleboxOverride("panel", Panel(18, 16)); ui.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 10); panel.AddChild(box);
        box.AddChild(SectionLabel("STAR SYSTEM"));
        _systemTitle = Text("", 22, TextBright, 3); box.AddChild(_systemTitle);
        _systemInfo = Text("", 11, Body, 0, wrap: true); _systemInfo.CustomMinimumSize = new Vector2(0, 150); box.AddChild(_systemInfo);
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        Button enter = FlatButton("VIEW OPERATIONS  >", 12); enter.CustomMinimumSize = new Vector2(0, 40); enter.Pressed += EnterSystem; box.AddChild(enter);

        Button hangar = FlatButton("FLAGSHIP ATHENA", 10);
        hangar.Position = new Vector2(805, 514); hangar.Size = new Vector2(323, 32);
        hangar.Pressed += () => FlagshipNavigation.Open(GetTree(), "res://Scenes/CampaignMap.tscn");
        ui.AddChild(hangar);

        ui.AddChild(new ColorRect { Position = new Vector2(56, 596), Size = new Vector2(400, 1), Color = new Color(Hairline.R, Hairline.G, Hairline.B, 0.12f) });
        var legend = Text("ALLIANCE · CONTESTED · ENEMY — CAPTURE A SYSTEM FOR A CREDIT PAYOUT", 8, Dim, 3);
        legend.Position = new Vector2(56, 606);
        ui.AddChild(legend);
    }

    void FitToViewport()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float scale = Mathf.Max(0.01f, Mathf.Min(viewport.X / ScreenW, viewport.Y / ScreenH));
        Vector2 offset = (viewport - new Vector2(ScreenW, ScreenH) * scale) * .5f;
        Scale = Vector2.One * scale;
        _mapBaseOffset = offset;
        Position = _mapBaseOffset + _mapPan;
        if (_ui != null) { _ui.Scale = Vector2.One * scale; _ui.Offset = offset; }
    }

    void SetSystem(string system)
    {
        _selectedSystem = system;
        CampaignPlanet[] planets = CampaignData.PlanetsInSystem(system).ToArray();
        int alliance = planets.Count(p => p.Control == PlanetControl.Alliance);
        int enemy = planets.Count(p => p.Control == PlanetControl.Enemy);
        int completed = CampaignData.CompletedPlanetaryMissions(system);
        int total = CampaignData.PlanetaryMissionCount(system);
        string capture = CampaignData.IsSystemCaptured(system)
            ? $"SYSTEM CAPTURED  //  +{CampaignData.SystemCaptureCredits} CREDITS CLAIMED"
            : $"CAPTURE REWARD  //  +{CampaignData.SystemCaptureCredits} CREDITS";
        _systemTitle.Text = system;
        _systemInfo.Text = $"{planets.Length} planetary bodies charted. Alliance controls {alliance}; enemy controls {enemy}.\n\nPLANETARY OPERATIONS: {completed} / {total} COMPLETE\nSYSTEM STATUS: {CampaignData.GetSystemControl(system).ToString().ToUpper()}\n{capture}\n\n{CampaignData.StoryGateStatusFor(system)}";
        _status.Text = $"DAY {CampaignData.Day:00} · CREDITS {CampaignData.Credits:0000} · FLAGSHIP READY";
        QueueRedraw();
    }

    void EnterSystem() { CampaignData.SelectSystem(_selectedSystem); GetTree().ChangeSceneToFile("res://Scenes/CampaignSystem.tscn"); }
    public override void _Process(double delta) { _pulse += (float)delta; QueueRedraw(); }
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left)
        {
            if (mouse.Pressed) { _dragging = true; _dragDistance = 0; return; }
            if (!_dragging) return;
            _dragging = false;
            if (_dragDistance > 6) return;
            Vector2 point = ToLocal(GetGlobalMousePosition());
            foreach (var pair in _systemAreas) if (pair.Value.HasPoint(point)) { SetSystem(pair.Key); return; }
            return;
        }
        if (input is not InputEventMouseMotion motion || !_dragging || !motion.ButtonMask.HasFlag(MouseButtonMask.Left)) return;
        _dragDistance += motion.Relative.Length(); _mapPan += motion.Relative; Position = _mapBaseOffset + _mapPan;
    }

    public override void _Draw()
    {
        DrawStarField();
        DrawRoute("ORION SPUR", "LYRA VEIL"); DrawRoute("ORION SPUR", "CYGNUS REACH"); DrawRoute("LYRA VEIL", "DRACO GATE"); DrawRoute("CYGNUS REACH", "DRACO GATE"); DrawRoute("DRACO GATE", "HELIOS CROWN");
        _systemAreas.Clear(); foreach (string system in Systems()) DrawSystem(system);
    }

    void DrawStarField()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        Vector2 localMin = -Position / Scale;
        Vector2 localMax = (viewport - Position) / Scale;
        int minX = Mathf.FloorToInt(Mathf.Min(localMin.X, localMax.X) / StarCellSize) - 1;
        int maxX = Mathf.FloorToInt(Mathf.Max(localMin.X, localMax.X) / StarCellSize) + 1;
        int minY = Mathf.FloorToInt(Mathf.Min(localMin.Y, localMax.Y) / StarCellSize) - 1;
        int maxY = Mathf.FloorToInt(Mathf.Max(localMin.Y, localMax.Y) / StarCellSize) + 1;

        for (int cellX = minX; cellX <= maxX; cellX++)
        for (int cellY = minY; cellY <= maxY; cellY++)
        for (int starIndex = 0; starIndex < StarsPerCell; starIndex++)
        {
            Vector2 starPosition = new(
                (cellX + StarRandom(cellX, cellY, starIndex, 0)) * StarCellSize,
                (cellY + StarRandom(cellX, cellY, starIndex, 1)) * StarCellSize);
            float brightness = StarRandom(cellX, cellY, starIndex, 2);
            DrawCircle(starPosition, .4f + brightness * .8f, new Color(.74f, .84f, 1f, .05f + brightness * .27f));
        }
    }

    static float StarRandom(int cellX, int cellY, int starIndex, int channel)
    {
        uint value = (uint)cellX * 0x8da6b343u ^ (uint)cellY * 0xd8163841u ^ (uint)starIndex * 0xcb1ab31fu ^ (uint)(channel + 18773);
        value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15; value *= 0x846ca68bu; value ^= value >> 16;
        return (value & 0x00ffffffu) / 16777215f;
    }

    void DrawRoute(string a, string b)
    {
        CampaignSystemDefinition first = CampaignData.GetSystem(a);
        CampaignSystemDefinition second = CampaignData.GetSystem(b);
        if (first != null && second != null)
            DrawDashed(first.MapPosition, second.MapPosition, new Color(.25f, .56f, .76f, .28f));
    }
    void DrawDashed(Vector2 a, Vector2 b, Color color) { float length = a.DistanceTo(b); var dir = (b - a).Normalized(); for (float d = 0; d < length; d += 12) DrawLine(a + dir * d, a + dir * Mathf.Min(d + 6, length), color, 2, true); }
    static Color ControlColor(SystemControl control) => control switch { SystemControl.Alliance => Positive, SystemControl.Enemy => Negative, _ => Accent };

    void DrawSystem(string name)
    {
        Vector2 p = CampaignData.GetSystem(name).MapPosition; bool selected = name == _selectedSystem; Color accent = ControlColor(CampaignData.GetSystemControl(name));
        if (selected) DrawArc(p, 54 + Mathf.Sin(_pulse * 3) * 2, 0, Mathf.Tau, 36, new Color(accent, .48f), 1.5f, true);
        DrawCircle(p, 48, new Color(CellBg.R, CellBg.G, CellBg.B, .94f)); DrawArc(p, 48, 0, Mathf.Tau, 28, new Color(accent, selected ? 1 : .68f), selected ? 2.5f : 1.2f, true);
        DrawString(ThemeDB.FallbackFont, p + new Vector2(-70, 63), name, HorizontalAlignment.Center, 140, 13, new Color(.8f, .9f, 1f, .94f));
        DrawString(ThemeDB.FallbackFont, p + new Vector2(-70, 79), CampaignData.GetSystemControl(name).ToString().ToUpper(), HorizontalAlignment.Center, 140, 10, accent);
        CampaignMission nextStory = CampaignData.NextStoryMission;
        if (nextStory?.SystemName == name) DrawString(ThemeDB.FallbackFont, p + new Vector2(-7, -56), "◆", HorizontalAlignment.Center, 14, 15, new Color(.95f, .8f, .34f));
        _systemAreas[name] = new Rect2(p - new Vector2(78, 78), new Vector2(156, 156));
    }
}
