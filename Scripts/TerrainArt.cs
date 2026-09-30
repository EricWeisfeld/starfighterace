using Godot;

/// <summary>
/// A pixel-art asteroid built from its terrain circle: a lumpy outline hugging
/// the collision radius, a cratered surface, and lighting from the top-left
/// that stays put while the rock slowly turns. Only the picture moves; the
/// rock's collision circle is unchanged.
/// </summary>
public partial class AsteroidSprite : Sprite2D
{
    /// <summary>World units per rock pixel, close to the ships' pixel size.</summary>
    const float TexelSize = 2f;
    static readonly Vector2 LightFrom = new Vector2(-0.6f, -0.8f).Normalized();

    // The texture stores surface detail (craters, bumps) in red, surface tone
    // in green and the rock's shape in alpha. The shader lights a sphere with
    // that detail pressed into it, bands the light into a few pixel-art steps
    // and inks a dark outline.
    const string Code = @"
shader_type canvas_item;
uniform vec2 light = vec2(-0.6, -0.8);
uniform vec2 texel = vec2(0.01);
uniform float relief = 8.0;
uniform float cover = 0.9;
uniform vec3 shade : source_color = vec3(0.07, 0.07, 0.10);
uniform vec3 body : source_color = vec3(0.31, 0.30, 0.34);
uniform vec3 lit : source_color = vec3(0.70, 0.66, 0.60);
uniform vec3 ink : source_color = vec3(0.03, 0.035, 0.05);
varying vec4 tint;
void vertex() { tint = COLOR; }
void fragment() {
    vec4 t = texture(TEXTURE, UV);
    if (t.a < 0.5) discard;
    float around = min(min(texture(TEXTURE, UV - vec2(texel.x, 0.0)).a, texture(TEXTURE, UV + vec2(texel.x, 0.0)).a),
                       min(texture(TEXTURE, UV - vec2(0.0, texel.y)).a, texture(TEXTURE, UV + vec2(0.0, texel.y)).a));
    if (around < 0.5) {
        COLOR = vec4(ink, 1.0) * tint;
    } else {
        float dx = texture(TEXTURE, UV - vec2(texel.x, 0.0)).r - texture(TEXTURE, UV + vec2(texel.x, 0.0)).r;
        float dy = texture(TEXTURE, UV - vec2(0.0, texel.y)).r - texture(TEXTURE, UV + vec2(0.0, texel.y)).r;
        vec2 p = (UV * 2.0 - 1.0) / cover;
        vec3 n = normalize(vec3(p * 0.85 + vec2(dx, dy) * relief, sqrt(max(0.08, 1.0 - dot(p, p)))));
        float d = clamp(dot(n, normalize(vec3(light, 0.42))) * 1.1, 0.0, 1.0);
        d = floor(d * 4.0 + 0.5) / 4.0;
        vec3 c = d < 0.5 ? mix(shade, body, d * 2.0) : mix(body, lit, (d - 0.5) * 2.0);
        COLOR = vec4(c * t.g, 1.0) * tint;
    }
}";

    static Shader _shader;
    float _spin;

    /// <summary>Engine-required parameterless constructor; use the feature overload in code.</summary>
    public AsteroidSprite() { }

    public AsteroidSprite(TerrainFeature rock)
    {
        int seed = Mathf.Abs((int)(rock.Position.X * 73856093f) ^ (int)(rock.Position.Y * 19349663f));
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        Position = rock.Position;
        Rotation = rng.RandfRange(0f, Mathf.Tau);
        _spin = Mathf.DegToRad(rng.RandfRange(2f, 6f)) * (rng.Randf() < 0.5f ? -1f : 1f);
        Texture = Build(rock.Radius, seed, out int size);
        Scale = Vector2.One * TexelSize;
        var material = new ShaderMaterial { Shader = _shader ??= new Shader { Code = Code } };
        material.SetShaderParameter("texel", Vector2.One / size);
        material.SetShaderParameter("relief", size * 0.12f);
        material.SetShaderParameter("cover", rock.Radius / TexelSize / (size / 2f));
        Material = material;
        UpdateLight();
    }

    public override void _Process(double delta)
    {
        Rotation += _spin * (float)delta;
        UpdateLight();
    }

    // The light stays fixed in the world, so turn it against the rock's spin.
    void UpdateLight() => (Material as ShaderMaterial)?.SetShaderParameter("light", LightFrom.Rotated(-Rotation));

    static ImageTexture Build(float radius, int seed, out int size)
    {
        float r = radius / TexelSize;
        size = Mathf.CeilToInt(r * 2f * 1.1f) + 4;
        float center = size / 2f;
        var outline = new FastNoiseLite { Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 2.2f };
        var surface = new FastNoiseLite { Seed = seed + 1, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 3.2f / r };
        var tone = new FastNoiseLite { Seed = seed + 2, NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 1.6f / r };
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };

        // A few craters, kept off the rim so the outline stays a rock.
        int craterCount = Mathf.Clamp(Mathf.RoundToInt(r / 6f), 3, 10);
        var craters = new (Vector2 At, float Radius, float Depth)[craterCount];
        for (int i = 0; i < craterCount; i++)
        {
            float along = Mathf.Sqrt(rng.Randf()) * r * 0.7f;
            craters[i] = (Vector2.FromAngle(rng.RandfRange(0f, Mathf.Tau)) * along,
                Mathf.Max(2.5f, r * rng.RandfRange(0.07f, 0.2f)), rng.RandfRange(0.14f, 0.26f));
        }

        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f - center, y + 0.5f - center);
                float angle = p.Angle();
                // Lumps sit between 96% and 108% of the collision radius.
                float edge = r * (1.02f + 0.06f * outline.GetNoise2D(Mathf.Cos(angle), Mathf.Sin(angle)));
                float d = p.Length();
                if (d > edge)
                    continue;
                float h = 0.06f * surface.GetNoise2D(p.X, p.Y);
                float albedo = 0.88f + 0.12f * tone.GetNoise2D(p.X, p.Y);
                foreach ((Vector2 at, float craterRadius, float depth) in craters)
                {
                    // Ragged bowls with a low, soft lip rather than drawn circles.
                    Vector2 offset = p - at;
                    float ragged = 1f + 0.18f * surface.GetNoise2D(offset.Angle() * 3f, craterRadius);
                    float c = offset.Length() / (craterRadius * ragged);
                    if (c < 1f)
                    {
                        h -= depth * (1f - c * c);
                        albedo -= 0.06f;
                    }
                    else if (c < 1.5f)
                    {
                        float lip = (c - 1f) / 0.5f;
                        h += depth * 0.18f * Mathf.Sin(lip * Mathf.Pi);
                    }
                }
                image.SetPixel(x, y, new Color(Mathf.Clamp(h * 1.4f + 0.55f, 0f, 1f), Mathf.Clamp(albedo, 0f, 1f), 0f, 1f));
            }
        return ImageTexture.CreateFromImage(image);
    }
}

/// <summary>
/// A nebula as drifting, softly banded gas. The cloud thins toward the
/// feature's edge; the exact boundary is drawn separately while planning.
/// </summary>
public partial class NebulaCloud : Sprite2D
{
    /// <summary>The quad is larger than the gas so wisps can spill past the edge.</summary>
    const float Overscan = 1.3f;
    /// <summary>World units per cloud pixel, a touch coarser than the ships.</summary>
    const float CellSize = 3f;

    const string Code = @"
shader_type canvas_item;
uniform vec3 color_a : source_color = vec3(0.22, 0.45, 0.90);
uniform vec3 color_b : source_color = vec3(0.48, 0.28, 0.86);
uniform float seed = 0.0;
uniform float cover = 0.77;
uniform float cells = 128.0;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++) { v += a * noise(p); p = p * 2.03 + 17.0; a *= 0.5; }
    return v;
}
void fragment() {
    vec2 uv = (floor(UV * cells) + 0.5) / cells;
    vec2 p = uv * 2.0 - 1.0;
    float d = length(p) / cover;
    vec2 q = p * 2.4 + vec2(seed, seed * 1.7);
    float t = TIME * 0.035;
    float n = fbm(q + vec2(t, -t * 0.7));
    float m = fbm(q * 1.8 + n * 1.5 - vec2(t * 0.6, t));
    float density = 1.0 - smoothstep(0.45, 1.08, d + (m - 0.5) * 0.7);
    float a = density * (0.15 + 0.85 * m * m * 1.6);
    a = floor(a * 6.0 + 0.5) / 6.0;
    vec3 col = mix(color_a, color_b, smoothstep(0.35, 0.65, n)) + vec3(0.18) * smoothstep(0.6, 0.8, m);
    COLOR = vec4(col, a * 0.5);
}";

    static Shader _shader;
    static ImageTexture _blank;

    /// <summary>Engine-required parameterless constructor; use the feature overload in code.</summary>
    public NebulaCloud() { }

    public NebulaCloud(TerrainFeature gas)
    {
        Position = gas.Position;
        float quad = gas.Radius * 2f * Overscan;
        Texture = _blank ??= ImageTexture.CreateFromImage(Image.CreateEmpty(4, 4, false, Image.Format.Rgba8));
        Scale = Vector2.One * quad / 4f;
        var material = new ShaderMaterial { Shader = _shader ??= new Shader { Code = Code } };
        material.SetShaderParameter("seed", (gas.Position.X * 0.013f + gas.Position.Y * 0.007f) % 50f);
        material.SetShaderParameter("cover", 1f / Overscan);
        material.SetShaderParameter("cells", quad / CellSize);
        Material = material;
    }
}
