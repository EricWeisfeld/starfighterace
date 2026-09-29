using Godot;
using static SignalUi;

/// <summary>
/// The game's portrait launch screen: start or continue a roguelite run, or
/// jump into a quick battle.
/// </summary>
public partial class HomeScreen : Node2D
{
    bool _confirmNewRun;
    VBoxContainer _actions;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Bg);

        MarginContainer root = ScreenRoot(this, extraTop: 96, extraBottom: 40);
        VBoxContainer page = Stack(20);
        root.AddChild(page);

        page.AddChild(Text("ALLIANCE FLIGHT COMMAND", FontCaption, Muted, 4));
        page.AddChild(Text("ACE STAR\nPILOT", FontDisplay, TextBright, 8));
        page.AddChild(new ColorRect { CustomMinimumSize = new Vector2(0, 2), Color = Hairline });
        page.AddChild(Text("Plan every turn. Fly every arc. Bring your pilots home.", FontBody, Body, 0, wrap: true));

        page.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        _actions = Stack(12);
        page.AddChild(_actions);
        BuildActions();
    }

    void BuildActions()
    {
        foreach (Node child in _actions.GetChildren())
            child.QueueFree();

        bool hasRun = RunState.HasSave;
        if (hasRun)
        {
            Button resume = TouchButton("CONTINUE RUN", primary: true);
            resume.Pressed += () =>
            {
                if (RunState.Load() != null)
                    ChangeScene(this, "res://Scenes/Run.tscn");
            };
            _actions.AddChild(resume);
        }

        Button newRun = TouchButton(_confirmNewRun ? "TAP AGAIN TO ABANDON YOUR RUN" : "NEW RUN", primary: !hasRun);
        newRun.Pressed += () =>
        {
            if (hasRun && !_confirmNewRun)
            {
                _confirmNewRun = true;
                BuildActions();
                return;
            }
            RunState.EndAndDelete();
            RunState.StartNew();
            ChangeScene(this, "res://Scenes/Run.tscn");
        };
        _actions.AddChild(newRun);
        _actions.AddChild(Text("Three sectors, one squadron. Pilots who die stay dead.", FontCaption, Muted, 0, wrap: true));

        _actions.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12), MouseFilter = Control.MouseFilterEnum.Ignore });
        Button quick = TouchButton("QUICK BATTLE");
        quick.Pressed += () => ChangeScene(this, "res://Scenes/TestBattleSelect.tscn");
        _actions.AddChild(quick);
        _actions.AddChild(Text("Pick up to three max-level ships and a battlefield.", FontCaption, Muted, 0, wrap: true));
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawStarfield(this, 14052, size.X, size.Y, 220);
        DrawNebula(this, new Vector2(size.X * 0.85f, size.Y * 0.18f), new Color(0.25f, 0.59f, 1f), 18, 26);
        DrawNebula(this, new Vector2(size.X * 0.1f, size.Y * 0.8f), new Color(0.16f, 0.86f, 0.75f), 14, 24, 0.005f);

        // Concentric command rings behind the title.
        var center = new Vector2(size.X * 0.7f, size.Y * 0.3f);
        DrawArc(center, 420, 0, Mathf.Tau, 96, new Color(0.35f, 0.67f, 1f, 0.10f), 2f, true);
        const int dashes = 40;
        for (int i = 0; i < dashes; i++)
        {
            float from = Mathf.Tau * i / dashes;
            DrawArc(center, 300, from, from + Mathf.Tau / dashes * 0.55f, 6, new Color(0.35f, 0.67f, 1f, 0.07f), 2f, true);
        }
    }
}
