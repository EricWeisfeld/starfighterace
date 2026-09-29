using Godot;
using System.Linq;
using static SignalUi;

/// <summary>
/// Memorial hall deck: a quiet wall honoring every pilot lost during the
/// campaign, replacing the old popup dialog.
/// </summary>
public partial class Memorial : Node2D
{
    const float ScreenW = 1152f;
    const float ScreenH = 648f;

    CanvasLayer _ui;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Bg);
        BuildUi();
        GetViewport().SizeChanged += FitUiToViewport;
    }

    void BuildUi()
    {
        var ui = new CanvasLayer();
        _ui = ui;
        AddChild(ui);

        Pilot[] fallen = PilotRoster.Pilots.Where(p => !p.Alive).ToArray();
        Label status = AthenaDecks.AddHeader(this, ui, AthenaDecks.Memorial, "MEMORIAL HALL", "IN HONOR OF THE FALLEN");
        status.Text = $"FALLEN {fallen.Length:00}";

        if (fallen.Length == 0)
        {
            var empty = new VBoxContainer { Position = new Vector2(56, 280), Size = new Vector2(1040, 120) };
            empty.AddThemeConstantOverride("separation", 10);
            ui.AddChild(empty);
            Label headline = Text("NO PILOTS HAVE BEEN LOST", 14, Dim, 6);
            headline.HorizontalAlignment = HorizontalAlignment.Center;
            headline.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            empty.AddChild(headline);
            Label sub = Text("EVERY LAUNCH HAS COME HOME", 9, new Color(Dim.R, Dim.G, Dim.B, 0.7f), 4);
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            sub.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            empty.AddChild(sub);
            FitUiToViewport();
            return;
        }

        var scroll = new ScrollContainer
        {
            Position = new Vector2(146, 140),
            Size = new Vector2(860, 420),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        ui.AddChild(scroll);
        var wall = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(840, 0) };
        wall.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(wall);

        foreach (Pilot pilot in fallen)
        {
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 56) };
            row.AddThemeConstantOverride("separation", 20);
            wall.AddChild(row);
            Label callsign = Text(pilot.Callsign.ToUpper(), 16, TextBright, 3);
            callsign.CustomMinimumSize = new Vector2(220, 0);
            callsign.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(callsign);
            Label hull = Text($"{pilot.Ship.DisplayName.ToUpper()} · LEVEL {pilot.Level}", 9, Muted, 2);
            hull.VerticalAlignment = VerticalAlignment.Center;
            hull.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(hull);
            Label record = Text($"{pilot.Missions} MISSIONS · {pilot.CareerKills} KILLS", 9, Dim, 2);
            record.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(record);
            wall.AddChild(new ColorRect { CustomMinimumSize = new Vector2(0, 1), Color = new Color(Hairline.R, Hairline.G, Hairline.B, 0.12f) });
        }
        FitUiToViewport();
    }

    void FitUiToViewport()
    {
        if (_ui == null)
            return;
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float scale = Mathf.Max(0.01f, Mathf.Min(viewport.X / ScreenW, viewport.Y / ScreenH));
        _ui.Scale = Vector2.One * scale;
        _ui.Offset = (viewport - new Vector2(ScreenW, ScreenH) * scale) * 0.5f;
    }

    public override void _Draw()
    {
        DrawStarfield(this, 3307, ScreenW, ScreenH, 110);
        DrawNebula(this, new Vector2(576, 640), new Color(0.25f, 0.59f, 1f), 12, 24, 0.006f);
    }
}
