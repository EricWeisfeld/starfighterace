using Godot;

/// <summary>
/// A fast round, drawn as its faction's pixel-art shot glowing in the team
/// colour with a short tracer tail. Hits the first enemy fighter it passes near.
/// </summary>
public partial class Bullet : Node2D
{
    Vector2 _vel;
    float _maxDist;
    float _traveled;
    Fighter _shooter;
    int _team;
    Color _color;
    int _damage;
    float _damageMultiplier = 1f; // fire-time bonuses, such as Long Shot
    bool _canHit; // misses are decided at fire time and just fly past
    ObjectiveShip _objectiveTarget;
    Sprite2D _sprite;
    float _age;

    public void Init(Fighter shooter, Vector2 pos, Vector2 vel, float maxDist, Color color, int damage, bool canHit,
        float damageMultiplier = 1f)
    {
        _shooter = shooter;
        _damageMultiplier = damageMultiplier;
        _team = shooter.Team;
        Position = pos;
        _vel = vel;
        _maxDist = maxDist;
        _color = color;
        _damage = damage;
        _canHit = canHit;
    }

    public void InitObjective(Fighter shooter, ObjectiveShip target, Vector2 pos, Vector2 vel, float maxDist, Color color, int damage, bool canHit,
        float damageMultiplier = 1f)
    {
        Init(shooter, pos, vel, maxDist, color, damage, canHit, damageMultiplier);
        _objectiveTarget = target;
    }

    public override void _Ready()
    {
        Material = ShipPaint.Additive;
        ZIndex = 4;
        _sprite = new Sprite2D
        {
            Texture = ShotArt.Texture(_team),
            Hframes = ShotArt.Frames(_team),
            Material = ShipPaint.ShotGlow(_team),
            Scale = Vector2.One * 2f,
            Rotation = _vel.Angle() + Mathf.Pi / 2f,
        };
        AddChild(_sprite);
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        _sprite.Frame = (int)(_age * 16f) % _sprite.Hframes;
        // Rounds that miss fade out at the end of their flight instead of popping.
        _sprite.Modulate = new Color(1f, 1f, 1f, EndFade);
        QueueRedraw();

        Vector2 previousPosition = Position;
        Vector2 step = _vel * (float)delta;
        Position += step;
        _traveled += step.Length();

        var mgr = BattleManager.Instance;
        if (mgr != null && mgr.ShotBlocked(previousPosition, Position))
        {
            mgr.SpawnImpact(Position, _vel, ImpactSparks.Kind.Rock);
            QueueFree();
            return;
        }

        if (_canHit)
        {
            if (_objectiveTarget != null && _objectiveTarget.IsAlive && Position.DistanceTo(_objectiveTarget.Position) < 24f)
            {
                int hullBefore = _objectiveTarget.Hp;
                _objectiveTarget.TakeDamage(_shooter?.RollShotDamage(null, _damageMultiplier) ?? _damage);
                _shooter?.RecordHit(false);
                mgr.SpawnImpact(Position, _vel, _objectiveTarget.Hp < hullBefore ? ImpactSparks.Kind.Hull : ImpactSparks.Kind.Shield);
                QueueFree();
                return;
            }
            foreach (Fighter f in mgr.GetTeam(1 - _team))
            {
                if (f.IsAlive && Position.DistanceTo(f.Position) < 16f)
                {
                    int hullBefore = f.Hp;
                    f.TakeHit(_shooter?.RollShotDamage(f, _damageMultiplier) ?? _damage);
                    _shooter?.RecordHit(!f.IsAlive);
                    mgr.SpawnImpact(Position, _vel, f.Hp < hullBefore ? ImpactSparks.Kind.Hull : ImpactSparks.Kind.Shield);
                    QueueFree();
                    return;
                }
            }
        }

        if (_traveled > _maxDist)
            QueueFree();
    }

    float EndFade => Mathf.Clamp((_maxDist - _traveled) / 70f, 0f, 1f);

    public override void _Draw()
    {
        float s = BattleManager.Instance?.ScreenToWorldScale ?? 1f;
        float fade = EndFade;
        Vector2 back = -_vel.Normalized();
        float length = Mathf.Min(_traveled, 54f);
        var tail = new[] { Vector2.Zero, back * length };
        DrawPolylineColors(tail, new[] { new Color(_color, 0.55f * fade), new Color(_color, 0f) }, Mathf.Max(5f, 4f * s));
        DrawPolylineColors(tail, new[] { new Color(1f, 1f, 1f, 0.6f * fade), new Color(1f, 1f, 1f, 0f) }, Mathf.Max(1.5f, 1.4f * s));
        float glow = Mathf.Max(11f, 9f * s);
        DrawTextureRect(ShipPaint.SoftDot, new Rect2(-glow, -glow, glow * 2f, glow * 2f), false, new Color(_color, 0.45f * fade));
    }
}

/// <summary>Short-lived expanding hit flash.</summary>
public partial class Flash : Node2D
{
    const float Life = 0.25f;
    float _t;

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_t >= Life)
        {
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float k = _t / Life;
        DrawCircle(Vector2.Zero, 4f + 14f * k, new Color(1f, 0.85f, 0.4f, 0.7f * (1f - k)));
    }
}
