using Godot;
using System.Collections.Generic;

/// <summary>
/// Battle camera for a touch screen. It frames world regions inside the band
/// of the screen the HUD leaves uncovered, eases between framings, and
/// accepts pinch, pan and wheel input. Any manual gesture suspends automatic
/// framing until the battle asks for a new framing.
/// </summary>
public partial class BattleCameraRig : Camera2D
{
    public const float MinZoomLevel = 0.28f;
    public const float MaxZoomLevel = 2.2f;
    /// <summary>World-space margin kept around a framed region.</summary>
    const float FramePadding = 90f;
    /// <summary>Exponential easing rate for framing transitions, per second.</summary>
    const float EaseRate = 9f;

    /// <summary>Screen pixels hidden under the HUD at the top and bottom.</summary>
    public float InsetTop, InsetBottom;
    /// <summary>World area the camera may show, grown to include every ship.</summary>
    public Rect2 Bounds = new(0, 0, BattleManager.ArenaW, BattleManager.ArenaH);

    Vector2 _targetPosition;
    float _targetZoom = 1f;
    bool _easing;
    System.Func<IEnumerable<Vector2>> _follow;

    public float ZoomLevel => Zoom.X;
    Vector2 ScreenSize => GetViewportRect().Size;

    public override void _Ready()
    {
        // A fighter may fly beyond the authored map; bounds are enforced by
        // ClampToBounds instead of the camera's built-in limits.
        LimitLeft = LimitTop = -1_000_000;
        LimitRight = LimitBottom = 1_000_000;
        _targetPosition = Position;
        _targetZoom = Zoom.X;
    }

    /// <summary>Viewport-space rectangle not covered by the HUD.</summary>
    public Rect2 VisibleScreenRect
    {
        get
        {
            Vector2 size = ScreenSize;
            return new Rect2(0, InsetTop, size.X, Mathf.Max(1f, size.Y - InsetTop - InsetBottom));
        }
    }

    public Vector2 ScreenToWorld(Vector2 screen) => Position + (screen - ScreenSize / 2f) / Zoom.X;

    public Vector2 WorldToScreen(Vector2 world) => (world - Position) * Zoom.X + ScreenSize / 2f;

    /// <summary>Eases the camera so the given world points fill the visible band.</summary>
    public void Frame(IEnumerable<Vector2> points, float maxZoom = 1.35f, bool instant = false)
    {
        _follow = null;
        SetFrameTarget(points, maxZoom);
        if (instant)
            SnapToTarget();
        else
            _easing = true;
    }

    /// <summary>
    /// Keeps re-framing a changing set of points every frame, used while the
    /// turn executes. A manual gesture ends following.
    /// </summary>
    public void Follow(System.Func<IEnumerable<Vector2>> points)
    {
        _follow = points;
        _easing = true;
    }

    public void StopFollowing() => _follow = null;

    /// <summary>Drags the view with a finger: world content moves with the screen delta.</summary>
    public void PanByScreen(Vector2 screenDelta)
    {
        CancelAutomation();
        Position -= screenDelta / Zoom.X;
        ClampToBounds();
    }

    /// <summary>Zooms by a factor while keeping the world point under the screen anchor fixed.</summary>
    public void ZoomAtScreen(Vector2 screenAnchor, float factor)
    {
        CancelAutomation();
        Vector2 worldAnchor = ScreenToWorld(screenAnchor);
        float zoom = Mathf.Clamp(Zoom.X * factor, MinZoomLevel, MaxZoomLevel);
        Zoom = Vector2.One * zoom;
        Position += worldAnchor - ScreenToWorld(screenAnchor);
        ClampToBounds();
    }

    void CancelAutomation()
    {
        _easing = false;
        _follow = null;
    }

    public override void _Process(double delta)
    {
        if (_follow != null)
            SetFrameTarget(_follow(), 1.1f);
        if (!_easing)
            return;

        // Engine time is slowed during execution; ease on real time so the
        // camera keeps pace with what the player sees.
        float realDelta = (float)delta / (float)Mathf.Max(Engine.TimeScale, 0.01);
        float t = 1f - Mathf.Exp(-EaseRate * realDelta);
        float zoom = Mathf.Lerp(Zoom.X, _targetZoom, t);
        Zoom = Vector2.One * zoom;
        Position = Position.Lerp(_targetPosition, t);
        if (_follow == null && Position.DistanceTo(_targetPosition) < 0.5f && Mathf.Abs(zoom - _targetZoom) < 0.001f)
            SnapToTarget();
    }

    void SnapToTarget()
    {
        _easing = false;
        Zoom = Vector2.One * _targetZoom;
        Position = _targetPosition;
    }

    void SetFrameTarget(IEnumerable<Vector2> points, float maxZoom)
    {
        Rect2? region = null;
        foreach (Vector2 point in points)
            region = region?.Expand(point) ?? new Rect2(point, Vector2.Zero);
        if (region == null)
            return;

        Rect2 world = region.Value.Grow(FramePadding);
        Rect2 visible = VisibleScreenRect;
        float zoom = Mathf.Min(visible.Size.X / world.Size.X, visible.Size.Y / world.Size.Y);
        _targetZoom = Mathf.Clamp(zoom, MinZoomLevel, Mathf.Min(maxZoom, MaxZoomLevel));
        // The camera centres on the whole viewport; offset it so the region
        // centres in the visible band between the HUD bars instead.
        Vector2 bandOffset = (ScreenSize / 2f - visible.GetCenter()) / _targetZoom;
        _targetPosition = world.GetCenter() + bandOffset;
    }

    /// <summary>
    /// Keeps the view over the battle. When the view is wider than the
    /// allowed area it centres on it rather than pinning one edge.
    /// </summary>
    void ClampToBounds()
    {
        Vector2 half = ScreenSize / (2f * Zoom.X);
        Rect2 area = Bounds.Grow(320f);
        Position = new Vector2(ClampAxis(Position.X, area.Position.X, area.End.X, half.X),
            ClampAxis(Position.Y, area.Position.Y, area.End.Y, half.Y));
    }

    static float ClampAxis(float value, float min, float max, float half)
    {
        if (max - min <= half * 2f)
            return (min + max) / 2f;
        return Mathf.Clamp(value, min + half, max - half);
    }
}
