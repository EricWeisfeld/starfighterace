using Godot;
using System.Linq;
using static SignalUi;

/// <summary>
/// Crew quarters deck: compare candidate ship classes side by side and sign
/// rookies onto the roster.
/// </summary>
public partial class Recruitment : Node2D
{
    const float ScreenW = 1152f;
    const float ScreenH = 648f;

    Label _headerStatus;
    Label _signedNotice;
    Button[] _recruitButtons;
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

        _headerStatus = AthenaDecks.AddHeader(this, ui, AthenaDecks.CrewQuarters, "CREW QUARTERS", "RECRUITMENT");

        var row = new HBoxContainer { Position = new Vector2(56, 130), Size = new Vector2(1040, 408) };
        row.AddThemeConstantOverride("separation", 24);
        ui.AddChild(row);
        _recruitButtons = new Button[ShipTypes.RecruitableClasses.Length];
        for (int i = 0; i < ShipTypes.RecruitableClasses.Length; i++)
            row.AddChild(BuildCandidateCard(ShipTypes.RecruitableClasses[i], i));

        _signedNotice = Text("RECRUITS ARE 1 LEVEL BELOW YOUR FLEET AVERAGE · TRAITS ARE RANDOM", 8, Dim, 3);
        _signedNotice.Position = new Vector2(56, 556);
        ui.AddChild(_signedNotice);
        FitUiToViewport();
    }

    Control BuildCandidateCard(ShipType shipClass, int index)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", Box(CellBg, Hairline, 1, 18, 16));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        box.AddChild(header);
        var identity = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        identity.AddThemeConstantOverride("separation", 2);
        header.AddChild(identity);
        identity.AddChild(Text(shipClass.DisplayName.ToUpper(), 18, TextBright, 3));
        identity.AddChild(Text(RoleTag(shipClass), 8, Dim, 3));
        var icon = new TextureRect
        {
            Texture = shipClass.GetSkin(0).Base,
            CustomMinimumSize = new Vector2(44, 44),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        header.AddChild(icon);

        box.AddChild(StatRow(
            ("HULL", shipClass.MaxHp.ToString(), null),
            ("SHD", $"{shipClass.MaxShield} +{shipClass.ShieldRegenPerTurn}/T", null),
            ("DMG", $"{shipClass.ShotDamage:0.#}", null),
            ("ACC", $"{shipClass.Accuracy * 100:0}%", null),
            ("EVA", $"{shipClass.Evasion * 100:0}%", null)));
        box.AddChild(Text($"MAX NORMAL TURN ±{shipClass.NormalTurnLimitDegrees:0}° · NORMAL MOVE {shipClass.NormalMoveMinDistance:0}-{shipClass.NormalMoveMaxDistance:0}", 9, Muted, 2));
        box.AddChild(Text(shipClass.Description, 10, Muted, 0, wrap: true));
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var frameNote = new HBoxContainer();
        frameNote.AddChild(Chip($"STARTING {ShipUpgrades.SlotName(shipClass.UpgradeSlots[0])} SLOT · HULL BRANCH AT L3", ChipRole.Dormant));
        box.AddChild(frameNote);

        Button recruit = FlatButton("RECRUIT", 10);
        recruit.CustomMinimumSize = new Vector2(0, 34);
        recruit.Pressed += () => Recruit(shipClass);
        _recruitButtons[index] = recruit;
        box.AddChild(recruit);
        return panel;
    }

    static string RoleTag(ShipType shipClass) => shipClass.Id switch
    {
        "scout" => "LIGHT INTERCEPTOR",
        "raptor" => "STRIKE FRAME",
        "zt" => "HEAVY GUN PLATFORM",
        _ => "STARFIGHTER",
    };

    void Recruit(ShipType shipClass)
    {
        if (PilotRoster.Living.Count() >= Hangar.RosterCapacity)
            return;
        int offeredLevel = PilotRoster.RecruitmentLevel;
        int cost = CampaignData.RecruitmentCost(offeredLevel);
        if (!CampaignData.TrySpendCredits(cost))
            return;

        Pilot pilot = PilotRoster.Recruit(shipClass, offeredLevel, rollRecruitmentTrait: true);
        CampaignData.UseRecruitmentSlot(shipClass.Id);
        CampaignData.SaveCampaign();
        string trait = pilot.Perks.Count > 0
            ? $" · TRAIT: {pilot.Perks[0].Name.ToUpper()}"
            : " · NO TRAIT";
        _signedNotice.Text = $"SIGNED · {pilot.Callsign.ToUpper()} · LV {pilot.Level} · {cost} CR{trait}";
        _signedNotice.AddThemeColorOverride("font_color", Positive);
        RefreshSummary();
    }

    void RefreshSummary()
    {
        int living = PilotRoster.Living.Count();
        bool full = living >= Hangar.RosterCapacity;
        _headerStatus.Text = full
            ? $"ROSTER {living:00}/{Hangar.RosterCapacity} · FULL"
            : $"ROSTER {living:00}/{Hangar.RosterCapacity} · FLEET AVG LV {PilotRoster.AverageLivingLevel} · CREDITS {CampaignData.Credits:0000}";
        RefreshOffers(full);
    }

    void RefreshOffers(bool full)
    {
        int offeredLevel = PilotRoster.RecruitmentLevel;
        int cost = CampaignData.RecruitmentCost(offeredLevel);
        for (int i = 0; i < _recruitButtons.Length; i++)
        {
            Button recruit = _recruitButtons[i];
            ShipType shipClass = ShipTypes.RecruitableClasses[i];
            bool available = CampaignData.RecruitmentSlotAvailable(shipClass.Id);
            recruit.Text = available
                ? $"RECRUIT · LV {offeredLevel} · {cost} CR"
                : "SLOT EMPTY · REFRESH AFTER MISSION";
            recruit.Disabled = full || !available || CampaignData.Credits < cost;
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
        DrawStarfield(this, 5513, ScreenW, ScreenH);
        DrawNebula(this, new Vector2(160, 120), new Color(0.25f, 0.59f, 1f), 12, 20);
    }
}
