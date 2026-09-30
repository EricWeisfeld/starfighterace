using Godot;
using System.Collections.Generic;

/// <summary>
/// A small animated viewscreen on an event page showing what the squadron has
/// found: a gutted freighter, a pod's blinking beacon, a repair swarm, an old
/// munitions cache, an ion storm or a convoy under attack. Built from the
/// ship packs and the battle art; purely decorative.
/// </summary>
public partial class EventScene : Control
{
    const string Nautolan = "res://Assets/Foozle_2DS0014_Void_FleetPack_3/Foozle_2DS0014_Void_EnemyFleet_3/Nautolan/Designs - Base/PNGs/";
    /// <summary>Stage units across the viewscreen's shorter side; the stage scales to fit.</summary>
    const float StageSpan = 420f;

    readonly string _eventId;
    // Full-frame layers sit outside the stage; the stage holds the scene
    // itself, centred and scaled to the viewscreen.
    DrawLayer _sky, _stageBack, _stageFront, _glass;
    Node2D _stage;
    readonly List<(Node2D Node, System.Action<Node2D, float> Move)> _actors = new();
    float _t;

    /// <summary>Engine-required parameterless constructor; use the event overload in code.</summary>
    public EventScene() : this("") { }

    public EventScene(string eventId)
    {
        _eventId = eventId;
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
        TextureRepeat = TextureRepeatEnum.Enabled;
    }

    public override void _Ready()
    {
        _sky = new DrawLayer { Paint = DrawSky };
        AddChild(_sky);
        _stage = new Node2D();
        AddChild(_stage);
        _stageBack = new DrawLayer { Paint = DrawStageBack };
        _stage.AddChild(_stageBack);
        BuildActors();
        _stageFront = new DrawLayer { Paint = DrawStageFront };
        _stage.AddChild(_stageFront);
        _glass = new DrawLayer { Paint = DrawGlass };
        AddChild(_glass);
    }

    void BuildActors()
    {
        switch (_eventId)
        {
            case "derelict":
                Actor(Nautolan + "Nautolan Ship - Battlecruiser - Base.png", 3f, new Color(0.62f, 0.6f, 0.66f),
                    (s, t) => { s.Position = new Vector2(-10f + 6f * Mathf.Sin(t * 0.2f), 0f); s.Rotation = 0.5f + 0.06f * Mathf.Sin(t * 0.25f); });
                break;
            case "stranded":
                Place(new AsteroidSprite(new TerrainFeature(TerrainFeatureType.Asteroid, 0f, 0f, 72f)), (n, t) => n.Position = new Vector2(-105f, -40f));
                break;
            case "drones":
                Actor(ShipTypes.Zt.GetSkin(0).Base, 4f, new Color(0.8f, 0.8f, 0.85f), (s, t) => { s.Position = Vector2.Zero; s.Rotation = -0.3f; });
                for (int i = 0; i < 5; i++)
                {
                    float phase = i * Mathf.Tau / 5f;
                    Actor(Nautolan + "Nautolan Ship - Support - Base.png", 1.4f, Colors.White, (s, t) =>
                    {
                        float a = phase + t * 0.45f;
                        s.Position = DronePosition(a);
                        s.Rotation = a + Mathf.Pi; // nose along the orbit
                    });
                }
                break;
            case "cache":
                Place(new AsteroidSprite(new TerrainFeature(TerrainFeatureType.Asteroid, 0f, 0f, 88f)), (n, t) => n.Position = new Vector2(-120f, -55f));
                break;
            case "storm":
                Place(new NebulaCloud(new TerrainFeature(TerrainFeatureType.Nebula, 0f, 0f, 240f)), (n, t) => n.Position = new Vector2(30f, -60f));
                for (int i = 0; i < 3; i++)
                {
                    var slot = new Vector2((i - 1) * 52f, Mathf.Abs(i - 1) * 28f);
                    Actor(ShipTypes.Scout.GetSkin(0).Base, 2f, Colors.White, (s, t) =>
                    {
                        s.Position = new Vector2(0f, 140f) + slot + new Vector2(0f, 3f * Mathf.Sin(t * 1.3f + slot.X));
                        s.Rotation = 0f;
                    });
                }
                break;
            case "distress":
                Actor(ShipTypes.CivilianDreadnought.GetSkin(0).Base, 2.2f, new Color(0.85f, 0.85f, 0.9f),
                    (s, t) => { s.Position = Convoy; s.Rotation = 1.2f; });
                for (int i = 0; i < 3; i++)
                {
                    float phase = i * 2.1f;
                    Sprite2D raider = Actor(ShipTypes.Scout.GetSkin(1).Base, 2f, Colors.White, (s, t) =>
                    {
                        s.Position = RaiderPosition(phase + t * 0.6f);
                        s.Rotation = phase + t * 0.6f + Mathf.Pi; // flying along the loop
                    });
                    raider.Material = ShipPaint.Enemy;
                }
                break;
        }
    }

    Sprite2D Actor(string path, float scale, Color tint, System.Action<Sprite2D, float> move) =>
        Actor(GD.Load<Texture2D>(path), scale, tint, move);

    /// <summary>A ship sprite that moves each frame. Ship art faces up, so rotation 0 points up the screen.</summary>
    Sprite2D Actor(Texture2D texture, float scale, Color tint, System.Action<Sprite2D, float> move)
    {
        var sprite = new Sprite2D { Texture = texture, Scale = Vector2.One * scale, Modulate = tint };
        Place(sprite, (n, t) => move((Sprite2D)n, t));
        return sprite;
    }

    /// <summary>Adds a node to the stage and moves it every frame.</summary>
    void Place(Node2D node, System.Action<Node2D, float> move)
    {
        _stage.AddChild(node);
        _actors.Add((node, move));
    }

    static Vector2 DronePosition(float angle) => new(Mathf.Cos(angle) * 165f, Mathf.Sin(angle) * 95f);
    static readonly Vector2 Convoy = new(-10f, 0f);
    static Vector2 RaiderPosition(float angle) => Convoy + new Vector2(Mathf.Cos(angle) * 175f, Mathf.Sin(angle) * 110f);

    public override void _Process(double delta)
    {
        _t += (float)delta;
        _stage.Position = Size / 2f;
        _stage.Scale = Vector2.One * Mathf.Min(Size.X, Size.Y) / StageSpan;
        foreach ((Node2D node, System.Action<Node2D, float> move) in _actors)
            move(node, _t);
        _sky.QueueRedraw();
        _stageBack.QueueRedraw();
        _stageFront.QueueRedraw();
        _glass.QueueRedraw();
    }

    /// <summary>A pulse that is on for a short window of every period.</summary>
    static float Blink(float t, float period, float phase = 0f, float on = 0.12f)
    {
        float f = Mathf.PosMod(t / period + phase, 1f);
        return f < on ? 1f - f / on : 0f;
    }

    static void Glow(DrawLayer layer, Vector2 at, float radius, Color color) =>
        layer.DrawTextureRect(ShipPaint.SoftDot, new Rect2(at - Vector2.One * radius, Vector2.One * radius * 2f), false, color);

    Color Wash => _eventId switch
    {
        "derelict" => new Color(0.55f, 0.32f, 0.16f),
        "stranded" => new Color(0.14f, 0.46f, 0.36f),
        "drones" => new Color(0.16f, 0.46f, 0.40f),
        "cache" => new Color(0.56f, 0.44f, 0.14f),
        "storm" => new Color(0.22f, 0.30f, 0.72f),
        "distress" => new Color(0.62f, 0.16f, 0.16f),
        _ => new Color(0.2f, 0.35f, 0.7f),
    };

    void DrawSky(DrawLayer layer)
    {
        var frame = new Rect2(Vector2.Zero, Size);
        layer.DrawRect(frame, new Color(0.02f, 0.035f, 0.07f));
        // Two drifting star layers from the shared sky.
        for (int i = 0; i < 2; i++)
            layer.DrawTextureRectRegion(SpaceBackdrop.StarField(i), frame, new Rect2(new Vector2(_t * (6f + 10f * i), 0f), Size));
        Glow(layer, Size / 2f, Mathf.Max(Size.X, Size.Y) * 0.45f, new Color(Wash, 0.22f));
    }

    void DrawStageBack(DrawLayer layer)
    {
        if (_eventId == "cache")
            DrawCache(layer, new Vector2(70f, 25f));
    }

    void DrawStageFront(DrawLayer layer)
    {
        switch (_eventId)
        {
            case "derelict":
            {
                // Loose power arcing in the hull.
                (Vector2 At, float Period, float Phase)[] sparks =
                {
                    (new Vector2(-36f, -72f), 1.7f, 0.1f), (new Vector2(48f, 24f), 2.3f, 0.5f), (new Vector2(-12f, 84f), 1.3f, 0.8f),
                };
                var hull = new Vector2(-10f + 6f * Mathf.Sin(_t * 0.2f), 0f);
                float turn = 0.5f + 0.06f * Mathf.Sin(_t * 0.25f);
                foreach ((Vector2 at, float period, float phase) in sparks)
                {
                    float k = Blink(_t, period, phase);
                    if (k <= 0f)
                        continue;
                    Vector2 p = hull + at.Rotated(turn);
                    Glow(layer, p, 16f, new Color(1f, 0.7f, 0.3f, 0.9f * k));
                    layer.DrawCircle(p, 3f, new Color(1f, 0.95f, 0.8f, k));
                }
                break;
            }
            case "stranded":
            {
                // The pod turns slowly, its beacon blinking and pinging outward.
                var pod = new Vector2(80f, 25f + 6f * Mathf.Sin(_t * 0.7f));
                for (int ring = 0; ring < 2; ring++)
                {
                    float k = Mathf.PosMod(_t / 2.2f + ring * 0.5f, 1f);
                    layer.DrawArc(pod, 18f + 130f * k, 0f, Mathf.Tau, 48, new Color(0.35f, 1f, 0.55f, 0.5f * (1f - k)), 2f, true);
                }
                layer.DrawSetTransform(pod, _t * 0.4f, Vector2.One * 1.6f);
                layer.DrawRect(new Rect2(-14f, -9f, 28f, 18f), new Color(0.05f, 0.06f, 0.09f));
                layer.DrawRect(new Rect2(-12f, -7f, 24f, 14f), new Color(0.72f, 0.78f, 0.86f));
                layer.DrawRect(new Rect2(-12f, 1f, 24f, 6f), new Color(0.48f, 0.54f, 0.64f));
                layer.DrawRect(new Rect2(4f, -5f, 6f, 5f), new Color(0.3f, 0.6f, 0.9f));
                layer.DrawSetTransform(Vector2.Zero);
                if (Mathf.PosMod(_t, 1f) < 0.45f)
                {
                    Glow(layer, pod, 20f, new Color(0.35f, 1f, 0.55f, 0.7f));
                    layer.DrawCircle(pod, 4f, new Color(0.7f, 1f, 0.8f));
                }
                break;
            }
            case "drones":
                // Each drone takes a turn welding at the hull.
                for (int i = 0; i < 5; i++)
                {
                    float k = Blink(_t, 1.6f, i / 5f, 0.35f);
                    if (k <= 0f)
                        continue;
                    Vector2 from = DronePosition(i * Mathf.Tau / 5f + _t * 0.45f);
                    Vector2 to = from * 0.2f;
                    layer.DrawLine(from, to, new Color(0.35f, 1f, 0.6f, 0.35f * k), 7f);
                    layer.DrawLine(from, to, new Color(0.85f, 1f, 0.9f, 0.9f * k), 2.5f);
                    Glow(layer, to, 14f, new Color(0.5f, 1f, 0.7f, 0.8f * k));
                }
                break;
            case "distress":
            {
                for (int ring = 0; ring < 2; ring++)
                {
                    float k = Mathf.PosMod(_t / 1.8f + ring * 0.5f, 1f);
                    layer.DrawArc(Convoy, 40f + 170f * k, 0f, Mathf.Tau, 56, new Color(1f, 0.3f, 0.25f, 0.45f * (1f - k)), 2.5f, true);
                }
                // Raiders' fire raking the convoy.
                for (int i = 0; i < 3; i++)
                {
                    float k = Blink(_t, 0.9f, i * 0.33f, 0.3f);
                    if (k <= 0f)
                        continue;
                    Vector2 raider = RaiderPosition(i * 2.1f + _t * 0.6f);
                    Vector2 dir = (Convoy - raider).Normalized();
                    Vector2 head = raider + dir * (1f - k) * (raider.DistanceTo(Convoy) - 40f);
                    layer.DrawLine(head, head - dir * 30f, new Color(1f, 0.55f, 0.25f, 0.9f), 3.5f);
                    if (k < 0.2f)
                        Glow(layer, head, 16f, new Color(1f, 0.7f, 0.3f, 0.9f));
                }
                break;
            }
        }
    }

    void DrawGlass(DrawLayer layer)
    {
        if (_eventId == "storm")
            DrawLightning(layer);
        // Viewscreen trim: faint scanlines, a hairline edge and bright corner brackets.
        for (float y = 0f; y < Size.Y; y += 4f)
            layer.DrawLine(new Vector2(0f, y), new Vector2(Size.X, y), new Color(0f, 0f, 0f, 0.12f), 1f);
        layer.DrawRect(new Rect2(Vector2.One, Size - Vector2.One * 2f), SignalUi.Hairline, false, 2f);
        const float arm = 22f;
        var corner = new Color(SignalUi.Accent, 0.8f);
        foreach (Vector2 c in new[] { Vector2.Zero, new Vector2(Size.X, 0f), new Vector2(0f, Size.Y), Size })
        {
            var inward = new Vector2(c.X > 0f ? -1f : 1f, c.Y > 0f ? -1f : 1f);
            Vector2 at = c + inward * 2f;
            layer.DrawLine(at, at + new Vector2(inward.X * arm, 0f), corner, 3f);
            layer.DrawLine(at, at + new Vector2(0f, inward.Y * arm), corner, 3f);
        }
    }

    /// <summary>A sealed munitions crate in the rock's lee, pixel by pixel, its warning light blinking.</summary>
    void DrawCache(DrawLayer layer, Vector2 at)
    {
        const float px = 5f;
        void Block(float x, float y, float w, float h, Color c) => layer.DrawRect(new Rect2(at + new Vector2(x, y) * px, new Vector2(w, h) * px), c);
        Block(-17, -11, 34, 22, new Color(0.04f, 0.05f, 0.07f));
        Block(-16, -10, 32, 20, new Color(0.30f, 0.33f, 0.28f));
        Block(-16, -10, 32, 3, new Color(0.40f, 0.44f, 0.37f));
        Block(-16, 7, 32, 3, new Color(0.20f, 0.22f, 0.19f));
        for (int i = 0; i < 8; i++)
            Block(-16 + i * 4, -2, 2, 4, i % 2 == 0 ? new Color(0.95f, 0.72f, 0.18f) : new Color(0.08f, 0.08f, 0.08f));
        Block(-12, -7, 6, 4, new Color(0.22f, 0.25f, 0.21f));
        Block(6, -7, 6, 4, new Color(0.22f, 0.25f, 0.21f));
        float k = Blink(_t, 1.4f, 0f, 0.4f);
        Block(13, -9, 2, 2, new Color(1f, 0.25f, 0.2f, 0.35f + 0.65f * k));
        if (k > 0f)
            Glow(layer, at + new Vector2(14f, -8f) * px, 18f, new Color(1f, 0.3f, 0.2f, 0.7f * k));
    }

    /// <summary>Forks of lightning inside the storm, one strike every second or so.</summary>
    void DrawLightning(DrawLayer layer)
    {
        const float period = 1.35f;
        float k = Blink(_t, period, 0f, 0.18f);
        if (k <= 0f)
            return;
        var rng = new RandomNumberGenerator { Seed = (ulong)Mathf.FloorToInt(_t / period) * 7919 + 13 };
        var p = new Vector2(rng.RandfRange(Size.X * 0.25f, Size.X * 0.85f), 0f);
        var points = new List<Vector2> { p };
        float bottom = rng.RandfRange(Size.Y * 0.35f, Size.Y * 0.6f);
        while (p.Y < bottom)
        {
            p += new Vector2(rng.RandfRange(-26f, 26f), rng.RandfRange(14f, 30f));
            points.Add(p);
        }
        Vector2[] bolt = points.ToArray();
        layer.DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.6f, 0.7f, 1f, 0.08f * k));
        layer.DrawPolyline(bolt, new Color(0.5f, 0.7f, 1f, 0.35f * k), 8f);
        layer.DrawPolyline(bolt, new Color(0.9f, 0.95f, 1f, k), 2.5f);
    }
}

/// <summary>A node that draws whatever its owner hands it, for layering procedural art between sprites.</summary>
public partial class DrawLayer : Node2D
{
    public System.Action<DrawLayer> Paint;

    public override void _Draw() => Paint?.Invoke(this);
}
