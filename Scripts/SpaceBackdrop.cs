using Godot;
using System.Collections.Generic;

/// <summary>
/// How one sky looks: its colour wash and the planet hanging in it, where it
/// sits on screen (as a fraction of the view), how big it is and how bright.
/// </summary>
public sealed record MapLook(Color WashA, Color WashB, string Planet, Vector2 PlanetAnchor,
    float PlanetSize = 300f, float PlanetLight = 0.34f);

/// <summary>
/// The sky behind battles and menus, drawn on its own layer below everything:
/// a soft colour wash, three star layers that drift at different speeds, and
/// a slowly turning planet. In battle the stars move with the camera; on menu
/// pages they drift on their own. Purely visual.
/// The star sheets in CelestialBodies are 12000px wide, past what many phone
/// GPUs can hold in one texture, so only the 6000px planet sheets are used.
/// </summary>
public partial class SpaceBackdrop : CanvasLayer
{
    const string Bodies = "res://Assets/CelestialBodies/";

    static readonly MapLook Default = new(new Color(0.18f, 0.30f, 0.62f), new Color(0.35f, 0.20f, 0.55f), "GasPlanet1", new Vector2(0.95f, 0.30f));

    static readonly Dictionary<string, MapLook> Looks = new()
    {
        ["shard-run"] = new(new Color(0.62f, 0.30f, 0.12f), new Color(0.40f, 0.14f, 0.22f), "LavaPlanet1", new Vector2(0.95f, 0.28f)),
        ["cobalt-veil"] = new(new Color(0.14f, 0.32f, 0.70f), new Color(0.28f, 0.20f, 0.62f), "FrozenPlanet1", new Vector2(0.05f, 0.34f)),
        ["broken-ring"] = new(new Color(0.12f, 0.40f, 0.48f), new Color(0.20f, 0.24f, 0.46f), "BaronPlanet1", new Vector2(0.95f, 0.62f)),
        ["open-drift"] = new(new Color(0.10f, 0.42f, 0.46f), new Color(0.12f, 0.20f, 0.44f), "WetPlanet1", new Vector2(0.05f, 0.28f)),
        ["rubble-belt"] = new(new Color(0.52f, 0.22f, 0.16f), new Color(0.30f, 0.18f, 0.32f), "RockyPlanet1", new Vector2(0.95f, 0.36f)),
        ["crossing"] = new(new Color(0.50f, 0.34f, 0.14f), new Color(0.32f, 0.18f, 0.52f), "GasPlanet1", new Vector2(0.05f, 0.60f)),
        ["monolith"] = new(new Color(0.46f, 0.16f, 0.46f), new Color(0.20f, 0.18f, 0.52f), "FrozenPlanet1", new Vector2(0.95f, 0.26f)),
        ["shallows"] = new(new Color(0.12f, 0.44f, 0.34f), new Color(0.12f, 0.26f, 0.50f), "WetPlanet1", new Vector2(0.95f, 0.56f)),
        ["gravel-field"] = new(new Color(0.26f, 0.32f, 0.44f), new Color(0.30f, 0.22f, 0.40f), "BaronPlanet1", new Vector2(0.05f, 0.30f)),
        ["knife-fight"] = new(new Color(0.60f, 0.14f, 0.14f), new Color(0.36f, 0.12f, 0.36f), "LavaPlanet1", new Vector2(0.95f, 0.64f)),
    };

    /// <summary>The look for a map id, or a neutral blue sky for unknown maps.</summary>
    public static MapLook For(string mapId) => mapId != null && Looks.TryGetValue(mapId, out MapLook look) ? look : Default;

    // Menu skies: bigger planets, a little brighter, since nothing moves in front of them.
    public static readonly MapLook Home = new(new Color(0.18f, 0.36f, 0.72f), new Color(0.10f, 0.46f, 0.44f), "WetPlanet1", new Vector2(0.72f, 0.49f), 400f, 0.42f);
    public static readonly MapLook QuickBattle = new(new Color(0.12f, 0.44f, 0.40f), new Color(0.30f, 0.20f, 0.52f), "BaronPlanet1", new Vector2(0.96f, 0.14f), 300f, 0.40f);

    static readonly MapLook[] SectorLooks =
    {
        new(new Color(0.16f, 0.34f, 0.70f), new Color(0.30f, 0.20f, 0.60f), "FrozenPlanet1", new Vector2(0.92f, 0.74f), 360f, 0.40f),
        new(new Color(0.10f, 0.44f, 0.40f), new Color(0.14f, 0.24f, 0.52f), "WetPlanet1", new Vector2(0.92f, 0.74f), 360f, 0.40f),
        new(new Color(0.62f, 0.32f, 0.12f), new Color(0.42f, 0.14f, 0.24f), "LavaPlanet1", new Vector2(0.92f, 0.74f), 360f, 0.40f),
    };

    /// <summary>The sky over a run's sector (1-based): ice, then ocean, then fire.</summary>
    public static MapLook Sector(int sector) => SectorLooks[Mathf.Clamp(sector - 1, 0, SectorLooks.Length - 1)];

    /// <summary>A battle's sky, which moves with the battle camera.</summary>
    public static SpaceBackdrop ForBattle(string mapId) => new(For(mapId), followCamera: true);

    /// <summary>A menu page's sky, which drifts slowly by itself.</summary>
    public static SpaceBackdrop ForMenu(MapLook look) => new(look, followCamera: false);

    /// <summary>One repeating star tile: how much it drifts with the camera, and how its stars look.</summary>
    sealed record StarLayer(float Parallax, int Count, int Size, float MinAlpha, float MaxAlpha, int Seed);

    static readonly StarLayer[] StarLayers =
    {
        new(0.06f, 220, 1, 0.18f, 0.45f, 11),
        new(0.16f, 70, 1, 0.45f, 0.85f, 23),
        new(0.30f, 18, 2, 0.60f, 0.95f, 37),
    };

    const int TileSize = 512;
    static readonly Dictionary<int, ImageTexture> StarTiles = new();

    /// <summary>How fast a menu sky drifts, in star-tile pixels per second.</summary>
    static readonly Vector2 MenuDrift = new(4f, -11f);

    MapLook _look;
    readonly bool _followCamera;
    readonly List<(Sprite2D Sprite, float Parallax)> _stars = new();
    Sprite2D _washA, _washB;
    AnimatedCelestial _planet;
    double _time;

    /// <summary>Engine-required parameterless constructor; use <see cref="ForBattle"/> or <see cref="ForMenu"/> in code.</summary>
    public SpaceBackdrop() : this(Default, followCamera: false) { }

    SpaceBackdrop(MapLook look, bool followCamera)
    {
        _look = look;
        _followCamera = followCamera;
        Layer = -1;
    }

    public override void _Ready()
    {
        _washA = Wash();
        _washB = Wash();
        foreach (StarLayer layer in StarLayers)
        {
            var sprite = new Sprite2D
            {
                Texture = StarTile(layer),
                Centered = false,
                RegionEnabled = true,
                TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
            };
            AddChild(sprite);
            _stars.Add((sprite, layer.Parallax));
        }

        ApplyLook();
        Place();
    }

    /// <summary>Changes the wash and planet, for a run moving into a new sector.</summary>
    public void SetLook(MapLook look)
    {
        if (look == _look)
            return;
        _look = look;
        if (IsInsideTree())
            ApplyLook();
    }

    void ApplyLook()
    {
        _washA.Modulate = new Color(_look.WashA, 0.20f);
        _washB.Modulate = new Color(_look.WashB, 0.16f);
        _planet?.QueueFree();
        _planet = new AnimatedCelestial();
        // About 75 seconds a turn: slow enough to read as distant.
        _planet.Setup($"{Bodies}{_look.Planet}.png", 100, framesPerSecond: 4f);
        _planet.Size = Vector2.One * _look.PlanetSize;
        // Pushed back into the dark, and a little cool so it reads as distant.
        _planet.Modulate = new Color(_look.PlanetLight, _look.PlanetLight, _look.PlanetLight * 1.2f);
        AddChild(_planet);
    }

    Sprite2D Wash()
    {
        var sprite = new Sprite2D { Texture = ShipPaint.SoftDot };
        AddChild(sprite);
        return sprite;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        Place();
    }

    void Place()
    {
        Vector2 view = GetViewport().GetVisibleRect().Size;
        var arenaMiddle = new Vector2(BattleManager.ArenaW, BattleManager.ArenaH) / 2f;
        Camera2D camera = _followCamera ? GetViewport().GetCamera2D() : null;
        // Menus have no camera: a virtual one glides slowly so the stars drift.
        Vector2 cameraPos = camera?.GetScreenCenterPosition() ?? arenaMiddle + MenuDrift * (float)_time / 0.16f;
        float zoom = camera?.Zoom.X ?? 1f;
        // How far the camera sits from the arena's middle, in screen pixels.
        Vector2 drift = camera == null ? Vector2.Zero : (cameraPos - arenaMiddle) * zoom;

        foreach ((Sprite2D sprite, float parallax) in _stars)
        {
            // Far layers barely react to zoom; nearer ones follow it a little.
            float layerZoom = 1f + (zoom - 1f) * parallax;
            sprite.Scale = Vector2.One * layerZoom;
            sprite.RegionRect = new Rect2(cameraPos * parallax - view / (2f * layerZoom), view / layerZoom);
        }

        float washSize = Mathf.Max(view.X, view.Y) * 1.1f;
        _washA.Scale = Vector2.One * washSize / ShipPaint.SoftDot.GetWidth();
        _washB.Scale = Vector2.One * washSize * 0.8f / ShipPaint.SoftDot.GetWidth();
        _washA.Position = new Vector2(view.X * 0.25f, view.Y * 0.30f) - drift * 0.02f;
        _washB.Position = new Vector2(view.X * 0.80f, view.Y * 0.72f) - drift * 0.03f;

        // Whole pixels only: a planet between pixels shimmers as its sheet pixels change width.
        _planet.Position = (view * _look.PlanetAnchor - Vector2.One * _look.PlanetSize / 2f - drift * 0.05f).Round();
    }

    /// <summary>One of the sky's repeating star tiles, far (0) to near (2), for other scenes to reuse.</summary>
    public static Texture2D StarField(int layer) => StarTile(StarLayers[Mathf.Clamp(layer, 0, StarLayers.Length - 1)]);

    /// <summary>A seamless tile of pixel stars, built once per layer.</summary>
    static ImageTexture StarTile(StarLayer layer)
    {
        if (StarTiles.TryGetValue(layer.Seed, out ImageTexture cached))
            return cached;
        var image = Image.CreateEmpty(TileSize, TileSize, false, Image.Format.Rgba8);
        var rng = new RandomNumberGenerator { Seed = (ulong)layer.Seed };
        Color[] tints = { new(1f, 1f, 1f), new(0.72f, 0.84f, 1f), new(1f, 0.92f, 0.74f), new(0.86f, 0.80f, 1f) };
        for (int i = 0; i < layer.Count; i++)
        {
            int x = rng.RandiRange(0, TileSize - 1), y = rng.RandiRange(0, TileSize - 1);
            var color = new Color(tints[rng.RandiRange(0, tints.Length - 1)], rng.RandfRange(layer.MinAlpha, layer.MaxAlpha));
            for (int dx = 0; dx < layer.Size; dx++)
                for (int dy = 0; dy < layer.Size; dy++)
                    image.SetPixel((x + dx) % TileSize, (y + dy) % TileSize, color);
            // The biggest stars get a faint one-pixel cross.
            if (layer.Size > 1 && rng.Randf() < 0.5f)
            {
                var glint = new Color(color, color.A * 0.35f);
                image.SetPixel((x - 1 + TileSize) % TileSize, y, glint);
                image.SetPixel((x + layer.Size) % TileSize, y, glint);
                image.SetPixel(x, (y - 1 + TileSize) % TileSize, glint);
                image.SetPixel(x, (y + layer.Size) % TileSize, glint);
            }
        }
        ImageTexture texture = ImageTexture.CreateFromImage(image);
        StarTiles[layer.Seed] = texture;
        return texture;
    }
}
