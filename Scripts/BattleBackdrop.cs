using Godot;
using System.Collections.Generic;

/// <summary>How one battlefield's sky looks: its colour wash and the body hanging in it.</summary>
public sealed record MapLook(Color WashA, Color WashB, string Planet, Vector2 PlanetAnchor);

/// <summary>
/// The sky behind a battle, drawn on its own layer below the world: a soft
/// colour wash, three star layers that drift at different speeds as the camera
/// moves, and a slowly turning planet picked per map. Purely visual.
/// The star sheets in CelestialBodies are 12000px wide, past what many phone
/// GPUs can hold in one texture, so only the 6000px planet sheets are used.
/// </summary>
public partial class BattleBackdrop : CanvasLayer
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
        ["escort-corridor"] = new(new Color(0.16f, 0.30f, 0.58f), new Color(0.40f, 0.26f, 0.16f), "GasPlanet1", new Vector2(0.95f, 0.30f)),
    };

    /// <summary>The look for a map id, or a neutral blue sky for unknown maps.</summary>
    public static MapLook For(string mapId) => mapId != null && Looks.TryGetValue(mapId, out MapLook look) ? look : Default;

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

    readonly MapLook _look;
    readonly List<(Sprite2D Sprite, float Parallax)> _stars = new();
    Sprite2D _washA, _washB;
    AnimatedCelestial _planet;
    const float PlanetSize = 300f; // three screen pixels per sheet pixel

    /// <summary>Engine-required parameterless constructor; use the map overload in code.</summary>
    public BattleBackdrop() : this(null) { }

    public BattleBackdrop(string mapId)
    {
        _look = For(mapId);
        Layer = -1;
    }

    public override void _Ready()
    {
        _washA = Wash(_look.WashA, 0.20f);
        _washB = Wash(_look.WashB, 0.16f);
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

        _planet = new AnimatedCelestial();
        _planet.Setup($"{Bodies}{_look.Planet}.png", 100, framesPerSecond: 7f);
        _planet.Size = Vector2.One * PlanetSize;
        _planet.Modulate = new Color(0.34f, 0.34f, 0.42f); // pushed back into the dark
        AddChild(_planet);
        Place();
    }

    Sprite2D Wash(Color color, float alpha)
    {
        var sprite = new Sprite2D { Texture = ShipPaint.SoftDot, Modulate = new Color(color, alpha) };
        AddChild(sprite);
        return sprite;
    }

    public override void _Process(double delta) => Place();

    void Place()
    {
        Vector2 view = GetViewport().GetVisibleRect().Size;
        Camera2D camera = GetViewport().GetCamera2D();
        Vector2 cameraPos = camera?.GetScreenCenterPosition() ?? new Vector2(BattleManager.ArenaW, BattleManager.ArenaH) / 2f;
        float zoom = camera?.Zoom.X ?? 1f;
        // How far the camera sits from the arena's middle, in screen pixels.
        Vector2 drift = (cameraPos - new Vector2(BattleManager.ArenaW, BattleManager.ArenaH) / 2f) * zoom;

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

        _planet.Position = view * _look.PlanetAnchor - Vector2.One * PlanetSize / 2f - drift * 0.05f;
    }

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
