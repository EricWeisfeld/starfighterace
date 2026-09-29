using Godot;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

/// <summary>System operations view. Five planetary missions unlock the system story.</summary>
public partial class CampaignSystem : Node2D
{
    static readonly Vector2 SystemCenter = new(405, 338);
    const float DefaultSystemZoom = 0.68f;
    const float MinSystemZoom = 0.42f;
    const float MaxSystemZoom = 1.45f;
    const float SunBaseSize = 230f;
    const float PlanetBaseSize = 48f;
    const float StarCellSize = 160f;
    const int StarsPerCell = 8;

    readonly Dictionary<CampaignPlanet, Rect2> _planetAreas = new();
    readonly List<AnimatedCelestial> _planetSprites = new();
    CampaignPlanet _selectedPlanet;
    CampaignMission _selectedMission;
    AnimatedCelestial _star;
    Label _title, _intel, _status;
    VBoxContainer _missionList;
    Button _deploy;
    float _pulse;
    float _systemZoom = DefaultSystemZoom;
    Vector2 _systemPan;
    bool _draggingSystem;
    float _dragDistance;

    public override void _Ready()
    {
        CampaignData.Initialize();
        RenderingServer.SetDefaultClearColor(Bg);
        BuildUi(); AddCelestialSprites();
        _selectedPlanet = Planets().FirstOrDefault(planet => CampaignData.GetMissionsForPlanet(planet.Id).Any())
            ?? Planets().FirstOrDefault();
        SetPlanet(_selectedPlanet);
    }
    CampaignPlanet[] Planets() => CampaignData.PlanetsInSystem(CampaignData.SelectedSystem).ToArray();

    void AddCelestialSprites()
    {
        _star = new AnimatedCelestial();
        _star.Setup("res://Assets/CelestialBodies/Star2.png", 200); AddChild(_star);
        CampaignPlanet[] planets = Planets();
        for (int i = 0; i < planets.Length; i++)
        {
            var planet = new AnimatedCelestial();
            planet.Setup(planets[i].TexturePath, 100); AddChild(planet);
            _planetSprites.Add(planet);
        }
        LayoutCelestialSprites();
    }

    static Vector2 PlanetPosition(int index) => SystemCenter + Vector2.FromAngle(-1.2f + index * 2.45f) * (145 + index * 80);
    Vector2 ToSystemView(Vector2 worldPosition) => SystemCenter + (worldPosition - SystemCenter) * _systemZoom + _systemPan;

    void LayoutCelestialSprites()
    {
        if (_star == null) return;
        _star.Size = Vector2.One * SunBaseSize * _systemZoom;
        _star.Position = ToSystemView(SystemCenter) - _star.Size / 2f;
        for (int i = 0; i < _planetSprites.Count; i++)
        {
            AnimatedCelestial planet = _planetSprites[i];
            planet.Size = Vector2.One * PlanetBaseSize * _systemZoom;
            planet.Position = ToSystemView(PlanetPosition(i)) - planet.Size / 2f;
        }
    }

    void BuildUi()
    {
        var ui = new CanvasLayer(); AddChild(ui);
        _title = Text("", 28, TextBright, 6); _title.Position = new Vector2(56, 22); ui.AddChild(_title);
        var hint = Text("PLANETARY OPERATIONS · EVERY WORLD HAS ONE ACTIVE MISSION", 9, Muted, 4); hint.Position = new Vector2(57, 62); ui.AddChild(hint);
        _status = Text("", 10, Muted, 2); _status.Position = new Vector2(57, 82); ui.AddChild(_status);
        var navigationHint = Text("DRAG SPACE TO PAN - MOUSE WHEEL TO ZOOM", 8, Dim, 2); navigationHint.Position = new Vector2(57, 108); ui.AddChild(navigationHint);
        var panel = new PanelContainer { Position = new Vector2(785, 122), Size = new Vector2(343, 430) }; panel.AddThemeStyleboxOverride("panel", Panel(18, 16)); ui.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 9); panel.AddChild(box);
        box.AddChild(SectionLabel("PLANETARY INTEL"));
        _intel = Text("", 11, Body, 0, wrap: true); _intel.CustomMinimumSize = new Vector2(0, 120); box.AddChild(_intel);
        box.AddChild(SectionLabel("OPERATIONS"));
        _missionList = new VBoxContainer(); _missionList.AddThemeConstantOverride("separation", 4); box.AddChild(_missionList);
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        _deploy = FlatButton("ASSEMBLE SQUADRON  >", 12); _deploy.CustomMinimumSize = new Vector2(0, 40); _deploy.Pressed += Deploy; box.AddChild(_deploy);
        Button back = FlatButton("<  STAR MAP", 10); back.Position = new Vector2(56, 596); back.Size = new Vector2(150, 32); back.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/CampaignMap.tscn"); ui.AddChild(back);
        Button hangar = FlatButton("FLAGSHIP ATHENA", 10); hangar.Position = new Vector2(218, 596); hangar.Size = new Vector2(180, 32); hangar.Pressed += () => FlagshipNavigation.Open(GetTree(), "res://Scenes/CampaignSystem.tscn"); ui.AddChild(hangar);
    }

    void SetPlanet(CampaignPlanet planet)
    {
        _selectedPlanet = planet; _selectedMission = null;
        if (_missionList != null)
            foreach (Node child in _missionList.GetChildren()) child.QueueFree();
        _title.Text = $"{CampaignData.SelectedSystem} SYSTEM";
        if (planet == null) { _intel.Text = "No planetary bodies charted."; _deploy.Disabled = true; return; }
        _status.Text = $"DAY {CampaignData.Day:00}    //    SYSTEM {CampaignData.GetSystemControl(planet.SystemName).ToString().ToUpper()}    //    CREDITS {CampaignData.Credits:0000}\n{CampaignData.StoryGateStatus}";
        _intel.Text = $"{planet.Name}\n\nCONTROL: {planet.Control.ToString().ToUpper()}\nTHREAT LEVEL {planet.Threat}\n\nComplete this operation to advance the system campaign.";
        CampaignMission mission = CampaignData.GetMissionsForPlanet(planet.Id).FirstOrDefault();
        if (mission != null)
        {
            string type = "PLANETARY";
            Label details = Text($"{type} · {mission.ObjectiveLabel}\nTHREAT {mission.Threat} · +{mission.Credits} CREDITS", 9, Body, 2);
            if (mission.EffectiveEnemyDifficulty > mission.Threat)
                details.Text = $"{type} · {mission.ObjectiveLabel}\nTHREAT {mission.Threat} · HOSTILES {mission.EffectiveEnemyDifficulty} · +{mission.Credits} CREDITS";
            details.CustomMinimumSize = new Vector2(0, 48);
            _missionList.AddChild(details);
        }
        CampaignMission story = CampaignData.GetStoryMissionForSystem(CampaignData.SelectedSystem);
        if (story != null)
        {
            _missionList.AddChild(SectionLabel("SYSTEM STORY"));
            Label details = Text($"STORY - {story.ObjectiveLabel}\nTHREAT {story.Threat} - +{story.Credits} CREDITS", 9, Body, 2);
            details.CustomMinimumSize = new Vector2(0, 48);
            _missionList.AddChild(details);
        }
        SetMission(mission ?? story); QueueRedraw();
    }

    void SetMission(CampaignMission mission)
    {
        _selectedMission = mission; _deploy.Disabled = mission == null;
        if (mission == null || _selectedPlanet == null) return;
        string type = mission.IsStory ? "SYSTEM STORY OPERATION" : "PLANETARY OPERATION";
        string hostileStrength = mission.EffectiveEnemyDifficulty > mission.Threat
            ? $"\nHOSTILE STRENGTH {mission.EffectiveEnemyDifficulty} (+{mission.EffectiveEnemyDifficulty - mission.Threat} ESCORT RESPONSE)"
            : "";
        string location = mission.IsStory ? mission.SystemName : _selectedPlanet.Name;
        string control = mission.IsStory ? CampaignData.GetSystemControl(mission.SystemName).ToString().ToUpper() : _selectedPlanet.Control.ToString().ToUpper();
        _intel.Text = $"{location}\n\nCONTROL: {control}\nTHREAT LEVEL {mission.Threat}{hostileStrength}\n\n{type}\n{mission.ObjectiveLabel}\nREWARD: {mission.Credits} CREDITS\n\n{mission.Briefing}";
    }

    void Deploy() { if (_selectedMission != null) { CampaignData.Select(_selectedMission); GetTree().ChangeSceneToFile("res://Scenes/SelectScreen.tscn"); } }
    public override void _Process(double delta) { _pulse += (float)delta; QueueRedraw(); }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                float previousZoom = _systemZoom;
                float multiplier = mouse.ButtonIndex == MouseButton.WheelUp ? 1.13f : 1f / 1.13f;
                _systemZoom = Mathf.Clamp(_systemZoom * multiplier, MinSystemZoom, MaxSystemZoom);
                Vector2 pointer = mouse.Position;
                Vector2 worldOffset = (pointer - SystemCenter - _systemPan) / previousZoom;
                _systemPan = pointer - SystemCenter - worldOffset * _systemZoom;
                LayoutCelestialSprites();
                QueueRedraw();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (mouse.ButtonIndex != MouseButton.Left)
                return;
            if (mouse.Pressed)
            {
                _draggingSystem = true;
                _dragDistance = 0f;
                return;
            }
            if (!_draggingSystem)
                return;
            _draggingSystem = false;
            if (_dragDistance > 6f)
                return;
            Vector2 point = mouse.Position;
            foreach (var pair in _planetAreas)
                if (pair.Value.HasPoint(point)) { SetPlanet(pair.Key); return; }
            return;
        }

        if (input is InputEventMouseMotion motion && _draggingSystem && motion.ButtonMask.HasFlag(MouseButtonMask.Left))
        {
            _dragDistance += motion.Relative.Length();
            _systemPan += motion.Relative;
            LayoutCelestialSprites();
            QueueRedraw();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Draw()
    {
        DrawStarField();
        Vector2 center = ToSystemView(SystemCenter); CampaignPlanet[] planets = Planets(); _planetAreas.Clear();
        for (int i = 0; i < planets.Length; i++)
        {
            float radius = (145 + i * 80) * _systemZoom;
            DrawArc(center, radius, 0, Mathf.Tau, 72, new Color(.2f, .55f, .75f, .18f), Mathf.Max(0.8f, _systemZoom), true);
            DrawPlanet(planets[i], ToSystemView(PlanetPosition(i)));
        }
    }

    void DrawStarField()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        Vector2 worldMin = SystemCenter + (-SystemCenter - _systemPan) / _systemZoom;
        Vector2 worldMax = SystemCenter + (viewport - SystemCenter - _systemPan) / _systemZoom;
        int minX = Mathf.FloorToInt(Mathf.Min(worldMin.X, worldMax.X) / StarCellSize) - 1;
        int maxX = Mathf.FloorToInt(Mathf.Max(worldMin.X, worldMax.X) / StarCellSize) + 1;
        int minY = Mathf.FloorToInt(Mathf.Min(worldMin.Y, worldMax.Y) / StarCellSize) - 1;
        int maxY = Mathf.FloorToInt(Mathf.Max(worldMin.Y, worldMax.Y) / StarCellSize) + 1;

        for (int cellX = minX; cellX <= maxX; cellX++)
        for (int cellY = minY; cellY <= maxY; cellY++)
        for (int starIndex = 0; starIndex < StarsPerCell; starIndex++)
        {
            Vector2 worldPosition = new(
                (cellX + StarRandom(cellX, cellY, starIndex, 0)) * StarCellSize,
                (cellY + StarRandom(cellX, cellY, starIndex, 1)) * StarCellSize);
            float brightness = StarRandom(cellX, cellY, starIndex, 2);
            DrawCircle(ToSystemView(worldPosition), .4f + brightness * .8f, new Color(.74f, .84f, 1f, .05f + brightness * .27f));
        }
    }

    static float StarRandom(int cellX, int cellY, int starIndex, int channel)
    {
        uint value = (uint)cellX * 0x8da6b343u ^ (uint)cellY * 0xd8163841u ^ (uint)starIndex * 0xcb1ab31fu ^ (uint)(channel + 4012);
        value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15; value *= 0x846ca68bu; value ^= value >> 16;
        return (value & 0x00ffffffu) / 16777215f;
    }

    static Color ControlColor(PlanetControl control) => control switch { PlanetControl.Alliance => Positive, PlanetControl.Enemy => Negative, _ => Accent };
    void DrawPlanet(CampaignPlanet planet, Vector2 pos)
    {
        bool selected = planet == _selectedPlanet; Color c = ControlColor(planet.Control);
        float markerRadius = 25f * _systemZoom;
        if (selected) DrawArc(pos, (40 + Mathf.Sin(_pulse * 3) * 3) * _systemZoom, 0, Mathf.Tau, 32, new Color(c, .5f), 1.5f, true);
        DrawCircle(pos, markerRadius, new Color(CellBg.R, CellBg.G, CellBg.B, .92f)); DrawArc(pos, markerRadius, 0, Mathf.Tau, 24, new Color(c, selected ? 1 : .65f), selected ? 2.4f : 1.2f, true);
        float labelOffset = 40f * _systemZoom;
        float labelWidth = 120f * _systemZoom;
        DrawString(ThemeDB.FallbackFont, pos + new Vector2(-labelWidth / 2f, labelOffset), planet.Name, HorizontalAlignment.Center, labelWidth, Mathf.Max(9, Mathf.RoundToInt(13 * _systemZoom)), new Color(.84f, .92f, 1f));
        DrawString(ThemeDB.FallbackFont, pos + new Vector2(-labelWidth / 2f, labelOffset + 15f * _systemZoom), planet.Control.ToString().ToUpper(), HorizontalAlignment.Center, labelWidth, Mathf.Max(8, Mathf.RoundToInt(10 * _systemZoom)), c);
        float hitRadius = Mathf.Max(38f, 66f * _systemZoom);
        _planetAreas[planet] = new Rect2(pos - Vector2.One * hitRadius, Vector2.One * hitRadius * 2f);
    }
}
