using Godot;

/// <summary>
/// A turning planet from a fixed-cell sprite sheet. Each sheet frame fades
/// into the next instead of snapping to it, so a slow spin glides rather than
/// stepping a whole sheet pixel several times a second.
/// </summary>
public partial class AnimatedCelestial : Control
{
    AtlasTexture _current, _next;
    TextureRect _nextLayer;
    int _frameSize, _columns, _frames, _shownFrame = -1;
    float _framesPerSecond;
    double _position;

    public void Setup(string sheetPath, int frameSize, float framesPerSecond = 12f)
    {
        Texture2D sheet = GD.Load<Texture2D>(sheetPath);
        _frameSize = frameSize;
        _framesPerSecond = framesPerSecond;
        _columns = Mathf.Max(1, sheet.GetWidth() / frameSize);
        _frames = _columns * Mathf.Max(1, sheet.GetHeight() / frameSize);
        _current = new AtlasTexture { Atlas = sheet };
        _next = new AtlasTexture { Atlas = sheet };
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(Layer(_current));
        _nextLayer = Layer(_next);
        AddChild(_nextLayer);
        Show(0.0);
    }

    static TextureRect Layer(Texture2D texture)
    {
        var layer = new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        layer.SetAnchorsPreset(LayoutPreset.FullRect);
        return layer;
    }

    public override void _Process(double delta)
    {
        if (_frames <= 1)
            return;
        _position = (_position + delta * _framesPerSecond) % _frames;
        Show(_position);
    }

    void Show(double position)
    {
        int frame = (int)position;
        if (frame != _shownFrame)
        {
            _shownFrame = frame;
            _current.Region = Cell(frame);
            _next.Region = Cell((frame + 1) % _frames);
        }
        // The next frame fades in over the current one as the spin reaches it.
        _nextLayer.Modulate = new Color(1f, 1f, 1f, (float)(position - frame));
    }

    Rect2 Cell(int frame) => new((frame % _columns) * _frameSize, (frame / _columns) * _frameSize, _frameSize, _frameSize);
}
