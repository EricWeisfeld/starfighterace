using Godot;
using static SignalUi;

/// <summary>
/// The game's portrait launch screen. Quick Battle is the playable mode while
/// the campaign is rebuilt as roguelite runs.
/// </summary>
public partial class HomeScreen : Node2D
{
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

        Button quick = TouchButton("QUICK BATTLE", primary: true);
        quick.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/TestBattleSelect.tscn");
        page.AddChild(quick);
        page.AddChild(Text("Pick up to three ships and a battlefield, then fight.", FontCaption, Muted, 0, wrap: true));

        page.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12), MouseFilter = Control.MouseFilterEnum.Ignore });

        Button campaign = TouchButton("CAMPAIGN");
        campaign.Disabled = true;
        page.AddChild(campaign);
        page.AddChild(Text("Being rebuilt as short roguelite runs.", FontCaption, Muted, 0, wrap: true));
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
