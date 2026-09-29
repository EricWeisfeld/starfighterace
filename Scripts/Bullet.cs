using Godot;

/// <summary>A fast tracer round. Hits the first enemy fighter it passes near.</summary>
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

    public override void _Process(double delta)
    {
        Vector2 previousPosition = Position;
        Vector2 step = _vel * (float)delta;
        Position += step;
        _traveled += step.Length();

        var mgr = BattleManager.Instance;
        if (mgr != null && mgr.ShotBlocked(previousPosition, Position))
        {
            mgr.SpawnFlash(Position);
            QueueFree();
            return;
        }

        if (_canHit)
        {
            if (_objectiveTarget != null && _objectiveTarget.IsAlive && Position.DistanceTo(_objectiveTarget.Position) < 24f)
            {
                _objectiveTarget.TakeDamage(_shooter?.RollShotDamage(null, _damageMultiplier) ?? _damage);
                _shooter?.RecordHit(false);
                mgr.SpawnFlash(Position);
                QueueFree();
                return;
            }
            foreach (Fighter f in mgr.GetTeam(1 - _team))
            {
                if (f.IsAlive && Position.DistanceTo(f.Position) < 16f)
                {
                    f.TakeHit(_shooter?.RollShotDamage(f, _damageMultiplier) ?? _damage);
                    _shooter?.RecordHit(!f.IsAlive);
                    mgr.SpawnFlash(Position);
                    QueueFree();
                    return;
                }
            }
        }

        if (_traveled > _maxDist)
            QueueFree();
    }

    public override void _Draw()
    {
        DrawLine(Vector2.Zero, -_vel.Normalized() * 12f, _color, 2.5f);
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
