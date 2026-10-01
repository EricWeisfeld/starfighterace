using Godot;
using System.Linq;

/// <summary>
/// Shared materials and textures for battle art: the enemy recolour, glowing
/// shots and soft light blobs. Built in code once and reused by every ship.
/// </summary>
public static class ShipPaint
{
    /// <summary>The colour every enemy hull is repainted in.</summary>
    public static readonly Color EnemyHull = new(1f, 0.30f, 0.20f);
    /// <summary>An enemy ace's livery: a near-black hull whose highlights are gold.</summary>
    public static readonly Color AceHull = new(0.32f, 0.10f, 0.12f);
    public static readonly Color AceGold = new(1f, 0.82f, 0.32f);
    /// <summary>Your squadron's shots, glows and sparks.</summary>
    public static readonly Color PlayerGlow = new(0.40f, 0.88f, 1f);
    /// <summary>Enemy shots and sparks.</summary>
    public static readonly Color EnemyGlow = new(1f, 0.52f, 0.22f);

    public static Color TeamGlow(int team) => team == 0 ? PlayerGlow : EnemyGlow;

    // Repaints a sprite along a dark-to-colour-to-highlight ramp by
    // brightness, so the pixel shading survives but the whole hull reads as
    // one colour. Modulate still applies, so hit flashes keep working.
    const string RecolourCode = @"
shader_type canvas_item;
uniform vec3 paint : source_color = vec3(1.0, 0.3, 0.2);
uniform vec3 highlight : source_color = vec3(1.0);
uniform float strength = 0.85;
uniform float gain = 1.35;
varying vec4 tint;
void vertex() { tint = COLOR; }
void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    float l = clamp(dot(tex.rgb, vec3(0.299, 0.587, 0.114)) * gain, 0.0, 1.0);
    vec3 ramp = l < 0.5 ? paint * (l * 2.0) : mix(paint, highlight, (l - 0.5) * 1.7);
    COLOR = vec4(mix(tex.rgb, ramp, strength), tex.a) * tint;
}";

    // The same ramp, added onto the scene: shot sprites glow in their team colour.
    const string GlowCode = @"
shader_type canvas_item;
render_mode blend_add;
uniform vec3 paint : source_color = vec3(0.4, 0.9, 1.0);
varying vec4 tint;
void vertex() { tint = COLOR; }
void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    float l = clamp(max(tex.r, max(tex.g, tex.b)) * 1.2, 0.0, 1.0);
    vec3 ramp = l < 0.6 ? paint * (l / 0.6) : mix(paint, vec3(1.0), (l - 0.6) * 2.0);
    COLOR = vec4(ramp * tex.a, tex.a) * tint;
}";

    static ShaderMaterial _enemy, _ace;
    static ShaderMaterial _playerShot, _enemyShot;
    static CanvasItemMaterial _additive;
    static Texture2D _softDot;

    public static ShaderMaterial Enemy => _enemy ??= Recolour(EnemyHull, Colors.White);
    public static ShaderMaterial Ace => _ace ??= Recolour(AceHull, AceGold);

    public static ShaderMaterial Recolour(Color paint, Color highlight)
    {
        var material = new ShaderMaterial { Shader = new Shader { Code = RecolourCode } };
        material.SetShaderParameter("paint", new Vector3(paint.R, paint.G, paint.B));
        material.SetShaderParameter("highlight", new Vector3(highlight.R, highlight.G, highlight.B));
        return material;
    }

    public static ShaderMaterial ShotGlow(int team) => team == 0
        ? _playerShot ??= Glow(PlayerGlow)
        : _enemyShot ??= Glow(EnemyGlow);

    static ShaderMaterial Glow(Color paint)
    {
        var material = new ShaderMaterial { Shader = new Shader { Code = GlowCode } };
        material.SetShaderParameter("paint", new Vector3(paint.R, paint.G, paint.B));
        return material;
    }

    /// <summary>Adds light instead of covering what's underneath: sparks, flashes, trails.</summary>
    public static CanvasItemMaterial Additive => _additive ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };

    /// <summary>A white dot that fades to nothing at its edge, for glows and light pools.</summary>
    public static Texture2D SoftDot => _softDot ??= new GradientTexture2D
    {
        Width = 128,
        Height = 128,
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(1f, 0.5f),
        Gradient = new Gradient
        {
            Offsets = new[] { 0f, 0.35f, 1f },
            Colors = new[] { Colors.White, new Color(1, 1, 1, 0.45f), new Color(1, 1, 1, 0f) },
        },
    };
}

/// <summary>Pixel-art shot sprites from the ship packs: a bolt for your squadron, a slug for the enemy.</summary>
public static class ShotArt
{
    const string Nairan = "res://Assets/Foozle_2DS0013_Void_FleetPack_2/Foozle_2DS0013_Void_EnemyFleet_2/Nairan";
    const string Klaed = "res://Assets/Foozle_2DS0012_Void_FleetPack_1/Foozle_2DS0012_Void_EnemyFleet_1/Kla'ed";

    static Texture2D _player, _enemy;

    public static Texture2D Texture(int team) => team == 0
        ? _player ??= GD.Load<Texture2D>($"{Nairan}/Weapon Effects - Projectiles/PNGs/Nairan - Bolt.png")
        : _enemy ??= GD.Load<Texture2D>($"{Klaed}/Projectiles/PNGs/Kla'ed - Bullet.png");

    public static int Frames(int team) => team == 0 ? 5 : 4;
}

/// <summary>A brief muzzle flare riding a ship's nose as it fires.</summary>
public partial class MuzzleFlash : Node2D
{
    const float Life = 0.09f;
    public Fighter Shooter;
    public Color Glow = Colors.White;
    float _t;

    public override void _Ready()
    {
        Material = ShipPaint.Additive;
        ZIndex = 5;
        Follow();
    }

    void Follow()
    {
        if (!IsInstanceValid(Shooter))
            return;
        Position = Shooter.Position + Vector2.FromAngle(Shooter.GunHeading) * (Shooter.VisualRadius * 0.85f);
        Rotation = Shooter.GunHeading;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_t >= Life)
        {
            QueueFree();
            return;
        }
        Follow();
        QueueRedraw();
    }

    public override void _Draw()
    {
        float k = 1f - _t / Life;
        float size = 10f + 6f * k;
        DrawTextureRect(ShipPaint.SoftDot, new Rect2(-size, -size, size * 2f, size * 2f), false, new Color(Glow, 0.8f * k));
        DrawLine(Vector2.Zero, new Vector2(size * 1.1f, 0f), new Color(1f, 1f, 1f, k), 2.5f);
        DrawLine(new Vector2(0f, -size * 0.45f), new Vector2(0f, size * 0.45f), new Color(Glow, 0.8f * k), 2f);
    }
}

/// <summary>
/// Sparks thrown off an impact: orange for hull hits, the shield's blue when a
/// shield soaks the shot, grey dust when a rock stops it. Also used, bigger,
/// for a ship's destruction.
/// </summary>
public partial class ImpactSparks : Node2D
{
    public enum Kind { Hull, Shield, Rock, Kill }

    public Kind Type = Kind.Hull;
    /// <summary>Which way the shot was travelling; sparks spray back and sideways from it.</summary>
    public Vector2 Incoming = Vector2.Zero;

    struct Spark
    {
        public Vector2 Pos, Vel;
        public float Life, Age;
    }

    Spark[] _sparks;
    float _t, _life;
    Color _hot, _cool;

    public override void _Ready()
    {
        Material = ShipPaint.Additive;
        ZIndex = 6;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        (int count, float speed, _life, _hot, _cool) = Type switch
        {
            Kind.Shield => (7, 110f, 0.3f, new Color(0.85f, 0.97f, 1f), new Color(0.3f, 0.65f, 1f)),
            Kind.Rock => (6, 70f, 0.35f, new Color(0.8f, 0.78f, 0.74f), new Color(0.35f, 0.34f, 0.36f)),
            Kind.Kill => (22, 190f, 0.75f, new Color(1f, 0.95f, 0.75f), new Color(1f, 0.35f, 0.1f)),
            _ => (9, 150f, 0.35f, new Color(1f, 0.93f, 0.7f), new Color(1f, 0.45f, 0.12f)),
        };
        Vector2 back = Incoming == Vector2.Zero ? Vector2.Zero : -Incoming.Normalized();
        _sparks = new Spark[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 dir = Vector2.FromAngle(rng.RandfRange(0f, Mathf.Tau));
            if (back != Vector2.Zero)
                dir = (dir + back * 0.9f).Normalized();
            _sparks[i] = new Spark
            {
                Vel = dir * speed * rng.RandfRange(0.4f, 1.15f),
                Life = _life * rng.RandfRange(0.55f, 1f),
            };
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        if (_t >= _life)
        {
            QueueFree();
            return;
        }
        for (int i = 0; i < _sparks.Length; i++)
        {
            _sparks[i].Age += dt;
            _sparks[i].Pos += _sparks[i].Vel * dt;
            _sparks[i].Vel *= 1f - Mathf.Min(1f, 3.5f * dt);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float flashLife = Type == Kind.Kill ? 0.35f : 0.14f;
        if (_t < flashLife)
        {
            float k = 1f - _t / flashLife;
            float size = (Type == Kind.Kill ? 46f : 14f) * (0.6f + 0.6f * (1f - k));
            DrawTextureRect(ShipPaint.SoftDot, new Rect2(-size, -size, size * 2f, size * 2f), false, new Color(_hot, 0.9f * k));
        }
        if (Type == Kind.Kill)
        {
            // A shock ring that races out and fades.
            float k = _t / _life;
            DrawArc(Vector2.Zero, 10f + 90f * Mathf.Sqrt(k), 0f, Mathf.Tau, 40, new Color(_cool, 0.7f * (1f - k)), 3f, true);
        }
        foreach (Spark s in _sparks)
        {
            if (s.Age >= s.Life)
                continue;
            float k = 1f - s.Age / s.Life;
            Color c = _cool.Lerp(_hot, k);
            DrawLine(s.Pos, s.Pos - s.Vel * 0.045f, new Color(c, k), Type == Kind.Kill ? 3f : 2f);
        }
    }
}

/// <summary>
/// Damage floating up off a ship. Hits landing close together add into one
/// number so a barrage reads as a single total. Blue is damage the shield
/// soaked; the warm number is hull.
/// </summary>
public partial class DamageNumber : Node2D
{
    const float Life = 1.1f;
    /// <summary>Hits within this many seconds of the last one join its number.</summary>
    const float MergeWindow = 0.45f;
    static readonly Color ShieldColor = new(0.55f, 0.82f, 1f);
    static readonly Color HullColor = new(1f, 0.86f, 0.45f);

    public Fighter Anchor;
    int _shield, _hull;
    float _t, _sinceHit;
    Vector2 _lastAnchor;
    // Older numbers on the same ship climb out of the way of a new one.
    float _lift, _shownLift;

    public bool CanMerge => _sinceHit < MergeWindow;

    public void Add(int shield, int hull)
    {
        _shield += shield;
        _hull += hull;
        _sinceHit = 0f;
        _t = Mathf.Min(_t, 0.12f); // restart the fade; keep the rise going
    }

    public override void _Ready() => ZIndex = 21;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        _sinceHit += dt;
        if (_t >= Life)
        {
            QueueFree();
            return;
        }
        if (IsInstanceValid(Anchor))
            _lastAnchor = Anchor.Position;
        Position = _lastAnchor;
        _shownLift = Mathf.MoveToward(_shownLift, _lift, dt * 240f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        float s = BattleManager.Instance?.ScreenToWorldScale ?? 1f;
        float k = _t / Life;
        float alpha = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
        float pop = 1f + 0.35f * Mathf.Max(0f, 1f - _sinceHit / 0.15f);
        Font font = SignalUi.Display;
        int size = Mathf.Max(1, Mathf.RoundToInt(SignalUi.FontBody * pop * s));
        string text = _hull > 0 && _shield > 0 ? $"-{_hull}" : $"-{_hull + _shield}";
        Color color = _hull > 0 ? HullColor : ShieldColor;
        float radius = IsInstanceValid(Anchor) ? Anchor.VisualRadius : 20f;
        Vector2 extent = font.GetStringSize(text, HorizontalAlignment.Left, -1f, size);
        var baseline = new Vector2(-extent.X / 2f, -radius * 0.4f - (18f + 40f * Mathf.Sqrt(k) + _shownLift) * s);
        DrawString(font, baseline + new Vector2(2f, 2f) * s, text, HorizontalAlignment.Left, -1f, size, new Color(0f, 0f, 0f, 0.8f * alpha));
        DrawString(font, baseline, text, HorizontalAlignment.Left, -1f, size, new Color(color, alpha));
        if (_hull > 0 && _shield > 0)
        {
            // Shield soak shown small beside the hull number.
            int small = Mathf.Max(1, Mathf.RoundToInt(SignalUi.FontMicro * s));
            var at = baseline + new Vector2(extent.X + 4f * s, -2f * s);
            DrawString(font, at, $"-{_shield}", HorizontalAlignment.Left, -1f, small, new Color(ShieldColor, alpha));
        }
    }

    /// <summary>Adds to this ship's live number if there is one, or starts a new one.</summary>
    public static void Show(Node layer, Fighter ship, int shield, int hull)
    {
        if (layer == null || shield + hull <= 0)
            return;
        DamageNumber live = layer.GetChildren().OfType<DamageNumber>().LastOrDefault(n => n.Anchor == ship && n.CanMerge);
        if (live == null)
        {
            foreach (DamageNumber older in layer.GetChildren().OfType<DamageNumber>().Where(n => n.Anchor == ship))
                older._lift += 30f;
            live = new DamageNumber { Anchor = ship, Position = ship.Position, _lastAnchor = ship.Position };
            layer.AddChild(live);
        }
        live.Add(shield, hull);
    }
}

/// <summary>
/// A chaff cloud: a ring of metal glints that twinkle where a ship dropped
/// it. Shots through it lose accuracy as through a nebula (see
/// <see cref="BattleManager.ShotObscured"/>); it lasts a set number of turns.
/// </summary>
public partial class ChaffCloud : Node2D
{
    public float Radius = 90f;
    public int TurnsLeft = 2;
    Vector2[] _glints;
    float[] _phase;
    float _time;
    bool _fading;

    public override void _Ready()
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)GetInstanceId() };
        _glints = new Vector2[70];
        _phase = new float[_glints.Length];
        for (int i = 0; i < _glints.Length; i++)
        {
            // Square root keeps the scatter even across the disc.
            _glints[i] = Vector2.FromAngle(rng.Randf() * Mathf.Tau) * Radius * Mathf.Sqrt(rng.Randf());
            _phase[i] = rng.Randf() * Mathf.Tau;
        }
        Modulate = new Color(1, 1, 1, 0);
        CreateTween().TweenProperty(this, "modulate:a", 1f, 0.4f);
    }

    /// <summary>Fades the cloud out and removes it.</summary>
    public void Dissipate()
    {
        if (_fading)
            return;
        _fading = true;
        Tween fade = CreateTween();
        fade.TweenProperty(this, "modulate:a", 0f, 0.6f);
        fade.TweenCallback(Callable.From(QueueFree));
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var silver = new Color(0.82f, 0.88f, 1f);
        DrawCircle(Vector2.Zero, Radius, new Color(silver, 0.05f));
        DrawArc(Vector2.Zero, Radius, 0f, Mathf.Tau, 48, new Color(silver, 0.22f), 1.5f, true);
        for (int i = 0; i < _glints.Length; i++)
        {
            float twinkle = 0.5f + 0.5f * Mathf.Sin(_time * 5f + _phase[i]);
            Vector2 drift = new Vector2(Mathf.Sin(_time * 0.7f + _phase[i]), Mathf.Cos(_time * 0.6f + _phase[i])) * 3f;
            float size = 1.5f + twinkle * 1.5f;
            DrawRect(new Rect2(_glints[i] + drift - Vector2.One * size / 2f, Vector2.One * size), new Color(silver, 0.25f + 0.65f * twinkle));
        }
    }
}

/// <summary>
/// A tractor beam's flash: a pulsing beam from the ship to the enemy it
/// caught, and a fading trail from where the enemy was dragged from.
/// </summary>
public partial class TractorBeamFx : Node2D
{
    public Fighter From, To;
    public Vector2 Origin;
    float _age;
    const float Lifetime = 1.1f;

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= Lifetime || !IsInstanceValid(From) || !IsInstanceValid(To))
        {
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float fade = 1f - _age / Lifetime;
        var beam = new Color(0.4f, 0.95f, 0.9f);
        float pulse = 0.6f + 0.4f * Mathf.Sin(_age * 30f);
        DrawLine(From.Position, To.Position, new Color(beam, 0.25f * fade), 10f, true);
        DrawLine(From.Position, To.Position, new Color(beam, 0.85f * fade * pulse), 3f, true);
        DrawLine(Origin, To.Position, new Color(beam, 0.4f * fade), 2f, true);
        DrawArc(Origin, 14f, 0f, Mathf.Tau, 20, new Color(beam, 0.5f * fade), 2f, true);
    }
}
