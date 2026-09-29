using Godot;

/// <summary>Animated fixed-cell celestial sprite sheet used by the campaign maps.</summary>
public partial class AnimatedCelestial : TextureRect
{
    AtlasTexture _atlas;
    int _frameSize, _columns, _frames, _frame;
    double _elapsed;

    public void Setup(string sheetPath, int frameSize, float framesPerSecond = 12f)
    {
        Texture2D sheet = GD.Load<Texture2D>(sheetPath);
        _frameSize = frameSize;
        _columns = Mathf.Max(1, sheet.GetWidth() / frameSize);
        _frames = _columns * Mathf.Max(1, sheet.GetHeight() / frameSize);
        _atlas = new AtlasTexture { Atlas = sheet, Region = new Rect2(0, 0, frameSize, frameSize) };
        Texture = _atlas;
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.KeepAspectCentered;
        MouseFilter = MouseFilterEnum.Ignore;
        SetMeta("frame_time", 1f / framesPerSecond);
    }

    public override void _Process(double delta)
    {
        if (_frames <= 1) return;
        _elapsed += delta;
        double frameTime = (float)GetMeta("frame_time");
        if (_elapsed < frameTime) return;
        _elapsed %= frameTime;
        _frame = (_frame + 1) % _frames;
        _atlas.Region = new Rect2((_frame % _columns) * _frameSize, (_frame / _columns) * _frameSize, _frameSize, _frameSize);
    }
}
