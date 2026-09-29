using Godot;
using static SignalUi;

/// <summary>Temporary: isolates the clipped-glyph-top bug across label configurations.</summary>
public partial class DebugLabelTest : Node2D
{
    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Bg);
        var ui = new CanvasLayer();
        AddChild(ui);

        // 1: standalone positioned, size 8, tracked 3 (matches map legend)
        var a = Text("1 ALLIANCE CONTESTED ENEMY CAPTURE", 8, Body, 3);
        a.Position = new Vector2(40, 40);
        ui.AddChild(a);

        // 2: standalone positioned, size 8, no tracking
        var b = Text("2 ALLIANCE CONTESTED ENEMY CAPTURE", 8, Body);
        b.Position = new Vector2(40, 80);
        ui.AddChild(b);

        // 3: standalone positioned, size 8, tracked 3, explicit size
        var c = Text("3 ALLIANCE CONTESTED ENEMY CAPTURE", 8, Body, 3);
        c.Position = new Vector2(40, 120);
        c.Size = new Vector2(500, 24);
        ui.AddChild(c);

        // 4: VBox, 14-label above 8-label (matches hangar identity, no icon sibling)
        var vbox = new VBoxContainer { Position = new Vector2(40, 170) };
        vbox.AddThemeConstantOverride("separation", 1);
        vbox.AddChild(Text("4 VIPER", 14, TextBright, 2));
        vbox.AddChild(Text("4 S1 KESTREL LEVEL 1", 8, Body, 2));
        ui.AddChild(vbox);

        // 5: HBox with a 40px-tall sibling, then the same VBox (exact hangar header layout)
        var hbox = new HBoxContainer { Position = new Vector2(40, 240) };
        hbox.AddThemeConstantOverride("separation", 10);
        hbox.AddChild(new Control { CustomMinimumSize = new Vector2(40, 40) });
        var identity = new VBoxContainer();
        identity.AddThemeConstantOverride("separation", 1);
        identity.AddChild(Text("5 VIPER", 14, TextBright, 2));
        identity.AddChild(Text("5 S1 KESTREL LEVEL 1", 8, Body, 2));
        hbox.AddChild(identity);
        ui.AddChild(hbox);

        // 6/7: standalone, sizes 9 and 10, tracked 3
        var f = Text("6 ALLIANCE CONTESTED ENEMY CAPTURE", 9, Body, 3);
        f.Position = new Vector2(40, 320);
        ui.AddChild(f);
        var g = Text("7 ALLIANCE CONTESTED ENEMY CAPTURE", 10, Body, 3);
        g.Position = new Vector2(40, 360);
        ui.AddChild(g);

        var watcher = new DebugLabelShot();
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, watcher);
    }
}

public partial class DebugLabelShot : Node
{
    double _elapsed;

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_elapsed < 1.0)
            return;
        DirAccess.MakeDirRecursiveAbsolute("res://debug_shots");
        GetViewport().GetTexture().GetImage().SavePng("res://debug_shots/labeltest.png");
        GetTree().Quit();
    }
}
