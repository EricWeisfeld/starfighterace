using Godot;
using static SignalUi;

/// <summary>The game's launch screen: choose the persistent campaign or a self-contained sandbox battle.</summary>
public partial class HomeScreen : Node2D
{
    const float ScreenW = 1152f;
    const float ScreenH = 648f;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Bg);

        var ui = new CanvasLayer();
        AddChild(ui);
        var root = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            OffsetLeft = 366,
            OffsetTop = 84,
            OffsetRight = ScreenW - 366,
            OffsetBottom = ScreenH - 64,
        };
        root.AddThemeConstantOverride("separation", 10);
        ui.AddChild(root);

        var eyebrow = SectionLabel("ALLIANCE FLIGHT COMMAND");
        eyebrow.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(eyebrow);
        var title = Text("ACE STAR PILOT", 50, TextBright, 8);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(title);
        root.AddChild(new ColorRect { CustomMinimumSize = new Vector2(0, 1), Color = Hairline });
        var subtitle = Text("Choose an operation, commander.", 12, Muted);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(subtitle);
        root.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        root.AddChild(ModeOption(
            "NEW CAMPAIGN",
            "Begin a new Helios Sector command and replace the current campaign save.",
            StartNewCampaign));
        if (CampaignData.HasSave())
        {
            root.AddChild(ModeOption(
                "CONTINUE CAMPAIGN",
                "Return to your saved Helios Sector command.",
                StartCampaign));
        }
        root.AddChild(ModeOption(
            "TEST BATTLE",
            "Build any three-ship squad at max level and enter a sandbox skirmish.",
            StartTestBattle));

        var footer = Text("CAMPAIGN PROGRESS IS NOT CHANGED BY TEST BATTLES", 8, Dim, 3);
        footer.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(footer);
    }

    static Control ModeOption(string heading, string description, System.Action action)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 5);
        var button = FlatButton(heading, 13);
        button.CustomMinimumSize = new Vector2(0, 42);
        button.Pressed += action;
        box.AddChild(button);
        var desc = Text(description, 10, Muted, 0, wrap: true);
        desc.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(desc);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        return box;
    }

    void StartCampaign()
    {
        GameSetup.StartCampaign();
        GetTree().ChangeSceneToFile("res://Scenes/CampaignMap.tscn");
    }

    void StartNewCampaign()
    {
        GameSetup.StartNewCampaign();
        GetTree().ChangeSceneToFile("res://Scenes/CampaignMap.tscn");
    }

    void StartTestBattle() => GetTree().ChangeSceneToFile("res://Scenes/TestBattleSelect.tscn");

    public override void _Draw()
    {
        DrawStarfield(this, 14052, ScreenW, ScreenH, 170);
        DrawNebula(this, new Vector2(930, 110), new Color(0.25f, 0.59f, 1f));
        DrawNebula(this, new Vector2(170, 560), new Color(0.16f, 0.86f, 0.75f), 12, 20, 0.005f);

        // Concentric command rings behind the menu, echoing the career page's orbit motif.
        var center = new Vector2(ScreenW / 2f, ScreenH / 2f);
        DrawArc(center, 420, 0, Mathf.Tau, 96, new Color(0.35f, 0.67f, 1f, 0.10f), 1f, true);
        const int dashes = 40;
        for (int i = 0; i < dashes; i++)
        {
            float from = Mathf.Tau * i / dashes;
            DrawArc(center, 300, from, from + Mathf.Tau / dashes * 0.55f, 6, new Color(0.35f, 0.67f, 1f, 0.07f), 1f, true);
        }
    }
}
