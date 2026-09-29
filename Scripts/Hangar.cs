using Godot;
using System.Linq;
using static SignalUi;

/// <summary>Scene path shared by screens that link into the hangar bay.</summary>
public static class HangarNavigation
{
    public const string ScenePath = "res://Scenes/Hangar.tscn";
}

/// <summary>
/// Hangar bay deck: the flight roster. Inspect pilots and open their career
/// records. Recruitment, upgrades and the memorial live on their own decks.
/// </summary>
public partial class Hangar : Node2D
{
    public const int RosterCapacity = 12;
    const float ScreenW = 1152f;
    const float ScreenH = 648f;

    Label _headerStatus;
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

        _headerStatus = AthenaDecks.AddHeader(this, ui, AthenaDecks.Hangar, "HANGAR BAY", "FLIGHT ROSTER");

        var rosterBox = new VBoxContainer { Position = new Vector2(56, 118), Size = new Vector2(1040, 462) };
        rosterBox.AddThemeConstantOverride("separation", 12);
        ui.AddChild(rosterBox);
        var rosterHead = new HBoxContainer();
        rosterBox.AddChild(rosterHead);
        Label rosterLabel = SectionLabel("ACTIVE PILOTS");
        rosterLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rosterHead.AddChild(rosterLabel);
        rosterHead.AddChild(Text("OPEN A RECORD TO REVIEW A CAREER", 8, Dim, 3));

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        rosterBox.AddChild(scroll);
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        scroll.AddChild(grid);
        foreach (Pilot pilot in PilotRoster.Living)
            grid.AddChild(BuildPilotCard(pilot));

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

    Control BuildPilotCard(Pilot pilot)
    {
        bool choiceReady = pilot.NeedsCareerChoice;
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(505, 0) };
        panel.AddThemeStyleboxOverride("panel", Box(CellBg,
            choiceReady ? new Color(Accent.R, Accent.G, Accent.B, 0.5f) : Hairline, 1, 14, 12));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        // Identity on the left; the pilot's level is the card's one big numeral,
        // so scanning the roster is scanning levels.
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        box.AddChild(header);
        var icon = new TextureRect
        {
            Texture = pilot.Ship.GetSkin(0).Base,
            CustomMinimumSize = new Vector2(40, 40),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        header.AddChild(icon);
        var identity = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        identity.AddThemeConstantOverride("separation", 1);
        header.AddChild(identity);
        identity.AddChild(Text(pilot.Callsign.ToUpper(), 15, TextBright, 2));
        identity.AddChild(Text($"{pilot.Ship.DisplayName.ToUpper()} · {pilot.Missions} MISSIONS · {pilot.CareerKills} KILLS", 8, Muted, 2));
        var conditionRow = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        conditionRow.AddThemeConstantOverride("separation", 7);
        bool wounded = pilot.Condition == PilotCondition.Wounded;
        conditionRow.AddChild(new StatusDot { DotColor = wounded ? Warning : Positive, CustomMinimumSize = new Vector2(9, 9), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        conditionRow.AddChild(Text(ConditionText(pilot), 8, wounded ? Warning : Positive, 2));
        header.AddChild(conditionRow);
        header.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0) });
        var levelBlock = new VBoxContainer();
        levelBlock.AddThemeConstantOverride("separation", 0);
        Label levelKey = Text("LEVEL", 8, Muted, 2);
        levelKey.HorizontalAlignment = HorizontalAlignment.Right;
        levelKey.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        levelBlock.AddChild(levelKey);
        Label levelNum = Text(pilot.Level.ToString(), 28, TextBright);
        levelNum.HorizontalAlignment = HorizontalAlignment.Right;
        levelNum.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        levelBlock.AddChild(levelNum);
        header.AddChild(levelBlock);

        int xpToNext = PilotRoster.XpToNext(pilot.Level);
        var xpRow = new HBoxContainer();
        xpRow.AddThemeConstantOverride("separation", 10);
        box.AddChild(xpRow);
        var xp = new XpTrack
        {
            Ratio = xpToNext > 0 ? (float)pilot.Xp / xpToNext : 1f,
            CustomMinimumSize = new Vector2(0, 6),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        xpRow.AddChild(xp);
        xpRow.AddChild(Text($"{pilot.Xp} / {xpToNext} → LV {pilot.Level + 1}", 8, Muted, 1));

        int hp = Perks.EffectiveMaxHp(pilot, pilot.Ship.MaxHp);
        int hullNow = Mathf.Max(0, hp - pilot.HullDamage);
        int maxShield = ShipUpgrades.MaxShield(pilot);
        int shieldRegen = Perks.EffectiveShieldRegen(pilot, ShipUpgrades.BaseShieldRegen(pilot));
        float damage = Perks.DisplayDamage(pilot, pilot.Ship.ShotDamage);
        float accuracy = Perks.DisplayAccuracy(pilot, ShipUpgrades.BaseAccuracy(pilot));
        float evasion = Perks.DisplayEvasion(pilot, pilot.Ship.Evasion);
        box.AddChild(StatRow(
            ("HULL", $"{hullNow}/{hp}", hullNow < hp ? Warning : (Color?)null),
            ("SHIELD", $"{maxShield} +{shieldRegen}/T", null),
            ("DAMAGE", $"{damage:0.#}", null),
            ("ACCURACY", $"{accuracy * 100:0}%", null),
            ("EVASION", $"{evasion * 100:0}%", null)));

        // Traits and class track as chips: green = gained, amber = impairment,
        // hairline = dormant future unlocks.
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 6);
        chips.AddThemeConstantOverride("v_separation", 6);
        box.AddChild(chips);
        foreach (Perk perk in pilot.Perks)
            chips.AddChild(Chip(perk.Name.ToUpper(), perk.Positive ? ChipRole.Gain : ChipRole.Impaired,
                $"{perk.Name}\n{perk.Description}"));
        foreach (ShipAbility ability in pilot.Maneuvers)
            chips.AddChild(Chip(AbilityName(ability).ToUpper(), ChipRole.Gain));
        foreach (ShipUpgrade upgrade in pilot.Upgrades)
            chips.AddChild(Chip(ShipUpgrades.Get(upgrade).Name.ToUpper(), ChipRole.Gain));
        if (pilot.NeedsHullChoice)
            chips.AddChild(Chip("LEVEL 3 HULL CHOICE", ChipRole.Decision));
        chips.AddChild(Chip($"MANEUVER SLOTS {pilot.Maneuvers.Count}/{pilot.ManeuverSlots}",
            choiceReady ? ChipRole.Dormant : ChipRole.Gain));
        if (chips.GetChildCount() == 0)
            chips.AddChild(Chip("NO TRAITS", ChipRole.Dormant));

        if (choiceReady)
        {
            // The pending decision is the card's only blue, pulsing element —
            // the strip replaces the plain record button and opens the career page.
            var strip = new AttentionStrip(pilot.NeedsHullChoice ? "HULL DIRECTION REQUIRED" : "MANEUVER SLOT READY", pilot.NeedsHullChoice ? "CHOOSE  >" : "EQUIP  >");
            strip.Pressed += () => PilotCareerNavigation.Open(GetTree(), pilot, HangarNavigation.ScenePath);
            box.AddChild(strip);
        }
        else
        {
            Button career = FlatButton("PILOT RECORD", 9);
            career.CustomMinimumSize = new Vector2(0, 26);
            career.Pressed += () => PilotCareerNavigation.Open(GetTree(), pilot, HangarNavigation.ScenePath);
            box.AddChild(career);
        }
        return panel;
    }

    void RefreshSummary()
    {
        Pilot[] living = PilotRoster.Living.ToArray();
        int ready = living.Count(p => p.Condition == PilotCondition.Ready);
        _headerStatus.Text = $"ACTIVE {living.Length:00} · READY {ready:00} · CAPACITY {RosterCapacity:00}";
    }

    static string ConditionText(Pilot pilot) => pilot.Condition switch
    {
        PilotCondition.Wounded => $"WOUNDED · {pilot.RecoveryMissionsRemaining}",
        _ => "READY",
    };

    public static string AbilityName(ShipAbility ability) => ability switch
    {
        ShipAbility.UTurn => "U-Turn",
        ShipAbility.BreakTurn => "Break Turn",
        ShipAbility.EngineBoost => "Engine Boost",
        ShipAbility.RotatingGuns => "Rotating Guns",
        ShipAbility.SuppressionFire => "Suppression Fire",
        ShipAbility.EmergencyThrusters => "Emergency Thrusters",
        ShipAbility.SnapTurn => "Snap Turn",
        ShipAbility.HunterLock => "Hunter Lock",
        ShipAbility.PursuitBurn => "Pursuit Burn",
        ShipAbility.EcmJink => "ECM Jink",
        ShipAbility.SensorScramble => "Sensor Scramble",
        ShipAbility.GhostRun => "Ghost Run",
        ShipAbility.EvasiveDodge => "Evasive Dodge",
        _ => ability.ToString(),
    };

    public override void _Draw()
    {
        DrawStarfield(this, 9127, ScreenW, ScreenH);
        DrawNebula(this, new Vector2(980, 560), new Color(0.25f, 0.59f, 1f), 12, 20);
    }
}
