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
        AddChild(SpaceBackdrop.ForMenu(SpaceBackdrop.Home));

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

        // A save from an older version of the game can't be continued.
        bool hasRun = RunState.HasSave && (RunState.Current ?? RunState.Load()) != null;
        if (RunState.HasSave && !hasRun)
            _actions.AddChild(Text("Your saved run is from an older version and can't be continued.", FontCaption, Warning, 0, wrap: true));
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

        // Concentric command rings, orbiting the planet behind the title.
        Vector2 center = size * SpaceBackdrop.Home.PlanetAnchor;
        DrawArc(center, 420, 0, Mathf.Tau, 96, new Color(0.35f, 0.67f, 1f, 0.10f), 2f, true);
        const int dashes = 40;
        for (int i = 0; i < dashes; i++)
        {
            float from = Mathf.Tau * i / dashes;
            DrawArc(center, 300, from, from + Mathf.Tau / dashes * 0.55f, 6, new Color(0.35f, 0.67f, 1f, 0.07f), 2f, true);
        }
    }
}
