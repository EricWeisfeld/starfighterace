using Godot;
using static SignalUi;

/// <summary>
/// Shipyard deck: the three flagship upgrade branches laid out as visible
/// node tracks with costs and effects.
/// </summary>
public partial class Shipyard : Node2D
{
    const float ScreenW = 1152f;
    const float ScreenH = 648f;
    const int MaxLevel = 3;

    record Branch(string Id, string Name, string Effect);

    static readonly Branch[] Branches =
    {
        new("logistics", "LOGISTICS", "Raises credit income: +15 CR per successful operation per level."),
        new("operations", "OPERATIONS", "Improves the rewards offered by generated operations."),
        new("squadron", "SQUADRON SUPPORT", "Adds +3 maximum hull to every deployed fighter per level."),
    };

    Label _headerStatus;
    readonly Button[] _upgradeButtons = new Button[Branches.Length];
    readonly UpgradeTrack[] _tracks = new UpgradeTrack[Branches.Length];
    CanvasLayer _ui;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Bg);
        BuildUi();
        RefreshSummary();
        GetViewport().SizeChanged += FitUiToViewport;
    }

    void BuildUi()
    {
        var ui = new CanvasLayer();
        _ui = ui;
        AddChild(ui);

        _headerStatus = AthenaDecks.AddHeader(this, ui, AthenaDecks.Shipyard, "SHIPYARD", "FLAGSHIP UPGRADES");

        var rows = new VBoxContainer { Position = new Vector2(56, 130), Size = new Vector2(1040, 430) };
        rows.AddThemeConstantOverride("separation", 16);
        ui.AddChild(rows);
        for (int i = 0; i < Branches.Length; i++)
            rows.AddChild(BuildBranchRow(i));

        var note = Text("UPGRADES ARE PERMANENT FOR THE CAMPAIGN · EACH BRANCH CAPS AT L3", 8, Dim, 3);
        note.Position = new Vector2(56, 566);
        ui.AddChild(note);
        FitUiToViewport();
    }

    Control BuildBranchRow(int index)
    {
        Branch branch = Branches[index];
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(0, 118) };
        panel.AddThemeStyleboxOverride("panel", Box(CellBg, Hairline, 1, 20, 16));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 28);
        panel.AddChild(row);

        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        info.AddThemeConstantOverride("separation", 5);
        row.AddChild(info);
        info.AddChild(Text(branch.Name, 14, TextBright, 4));
        Label effect = Text(branch.Effect, 10, Muted, 0, wrap: true);
        effect.CustomMinimumSize = new Vector2(380, 0);
        info.AddChild(effect);

        var track = new UpgradeTrack
        {
            CustomMinimumSize = new Vector2(280, 44),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _tracks[index] = track;
        row.AddChild(track);

        Button upgrade = FlatButton("", 10);
        upgrade.CustomMinimumSize = new Vector2(220, 36);
        upgrade.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        upgrade.Pressed += () =>
        {
            if (CampaignData.PurchaseUpgrade(branch.Id))
                RefreshSummary();
        };
        _upgradeButtons[index] = upgrade;
        row.AddChild(upgrade);
        return panel;
    }

    static int BranchLevel(string id) => id switch
    {
        "logistics" => CampaignData.LogisticsLevel,
        "operations" => CampaignData.OperationsLevel,
        _ => CampaignData.SquadronLevel,
    };

    void RefreshSummary()
    {
        _headerStatus.Text = $"CREDITS {CampaignData.Credits}";
        for (int i = 0; i < Branches.Length; i++)
        {
            int level = BranchLevel(Branches[i].Id);
            _tracks[i].Level = level;
            _tracks[i].QueueRedraw();
            if (level >= MaxLevel)
            {
                _upgradeButtons[i].Text = "MAXIMUM LEVEL";
                _upgradeButtons[i].Disabled = true;
            }
            else
            {
                int cost = CampaignData.UpgradeCost(level);
                _upgradeButtons[i].Text = $"L{level} > L{level + 1} · {cost} CR";
                _upgradeButtons[i].Disabled = CampaignData.Credits < cost;
            }
        }
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
        DrawStarfield(this, 7741, ScreenW, ScreenH);
        DrawNebula(this, new Vector2(990, 130), new Color(0.25f, 0.59f, 1f), 12, 20);
    }
}

/// <summary>Horizontal L0→L3 progression rail: filled nodes up to the current level.</summary>
public partial class UpgradeTrack : Control
{
    public int Level;
    public int Max = 3;

    public override void _Draw()
    {
        var accent = new Color(0.302f, 0.639f, 1f);
        var locked = new Color(0.47f, 0.71f, 0.9f, 0.25f);
        float midY = Size.Y / 2f - 6;
        float step = (Size.X - 24) / Max;
        Font font = SignalUi.Tracked(2);

        for (int i = 0; i < Max; i++)
        {
            Vector2 from = new(12 + i * step, midY);
            Vector2 to = new(12 + (i + 1) * step, midY);
            DrawLine(from + new Vector2(6, 0), to - new Vector2(6, 0), Level > i ? new Color(accent.R, accent.G, accent.B, 0.55f) : locked, 1);
        }
        for (int i = 0; i <= Max; i++)
        {
            var center = new Vector2(12 + i * step, midY);
            bool reached = Level >= i;
            if (reached)
                DrawCircle(center, 5, accent);
            else
                DrawArc(center, 5, 0, Mathf.Tau, 24, locked, 1.5f, true);
            DrawString(font, center + new Vector2(-10, 20), $"L{i}", HorizontalAlignment.Center, 20, 8,
                reached ? new Color(0.788f, 0.839f, 0.886f) : new Color(0.33f, 0.41f, 0.49f));
        }
    }
}
