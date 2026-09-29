using Godot;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

/// <summary>
/// Focused pre-battle squad selection. Persistent roster operations live in
/// the Hangar; this screen chooses up to three pilots for the current mission.
/// </summary>
public partial class SelectScreen : Node2D
{
    const float ScreenW = 1152f;
    const float ScreenH = 648f;

    readonly List<Pilot> _squad = new();
    readonly List<PilotCard> _cards = new();
    readonly Button[] _slotButtons = new Button[3];
    Button _launchBtn;

    class PilotCard
    {
        public Pilot Pilot;
        public TextureRect Icon;
        public Label HullLine;
        public Label Condition;
        public Label Stats;
        public Label Progression;
        public Button Add;
    }

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Bg);

        var ui = new CanvasLayer();
        AddChild(ui);
        var root = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 7);
        ui.AddChild(root);

        var mission = CampaignData.SelectedMission;
        _squad.AddRange(GameSetup.PlayerPilots.Where(p => p.CanDeploy).Distinct().Take(3));
        var title = Text("ASSEMBLE YOUR SQUADRON", 28, TextBright, 6);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(title);

        var subtitle = Text(mission == null
            ? "PICK 1–3 PILOTS FOR A TACTICAL ENGAGEMENT"
            : $"{mission.SystemName.ToUpper()} · {mission.PlanetName.ToUpper()} · THREAT LEVEL {mission.Threat}", 9, Muted, 3);
        if (mission != null && mission.EffectiveEnemyDifficulty > mission.Threat)
            subtitle.Text += $" · HOSTILE STRENGTH {mission.EffectiveEnemyDifficulty}";
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(subtitle);
        var rule = new CenterContainer();
        rule.AddChild(new ColorRect { CustomMinimumSize = new Vector2(560, 1), Color = Hairline });
        root.AddChild(rule);

        var rosterCenter = new CenterContainer();
        root.AddChild(rosterCenter);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(1060, 420),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        rosterCenter.AddChild(scroll);
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 10);
        scroll.AddChild(grid);
        foreach (Pilot pilot in PilotRoster.Living)
            grid.AddChild(BuildCard(pilot));

        var slotRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        slotRow.AddThemeConstantOverride("separation", 10);
        root.AddChild(slotRow);
        slotRow.AddChild(Text("YOUR SQUAD", 9, Muted, 4));
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            var button = new Button
            {
                CustomMinimumSize = new Vector2(48, 48),
                ExpandIcon = true,
                TooltipText = "Empty slot",
            };
            button.AddThemeStyleboxOverride("normal", Box(CellBg, Hairline, 1, 4, 4));
            button.AddThemeStyleboxOverride("hover", Box(CellBg, new Color(Accent.R, Accent.G, Accent.B, 0.7f), 1, 4, 4));
            button.AddThemeStyleboxOverride("pressed", Box(CellBg, Accent, 1, 4, 4));
            button.Pressed += () => RemoveSlot(slot);
            _slotButtons[i] = button;
            slotRow.AddChild(button);
        }

        // Keep the three screen actions anchored to their intended positions:
        // sector map left, flagship centered, and launch right.
        var actionRow = new HBoxContainer { CustomMinimumSize = new Vector2(1060, 38) };
        actionRow.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        root.AddChild(actionRow);
        var mapCell = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            CustomMinimumSize = new Vector2(210, 38),
        };
        mapCell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var flagshipCell = new CenterContainer { CustomMinimumSize = new Vector2(210, 38) };
        flagshipCell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var launchCell = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            CustomMinimumSize = new Vector2(210, 38),
        };
        launchCell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actionRow.AddChild(mapCell);
        actionRow.AddChild(flagshipCell);
        actionRow.AddChild(launchCell);

        var mapButton = FlatButton("<  SECTOR MAP", 10);
        mapButton.CustomMinimumSize = new Vector2(140, 34);
        mapButton.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/CampaignMap.tscn");
        mapCell.AddChild(mapButton);

        var hangarButton = FlatButton("FLAGSHIP", 10);
        hangarButton.CustomMinimumSize = new Vector2(150, 34);
        hangarButton.Pressed += () =>
        {
            GameSetup.PlayerPilots = new List<Pilot>(_squad);
            FlagshipNavigation.Open(GetTree(), "res://Scenes/SelectScreen.tscn");
        };
        flagshipCell.AddChild(hangarButton);

        _launchBtn = FlatButton("LAUNCH  >", 13);
        _launchBtn.Disabled = true;
        _launchBtn.CustomMinimumSize = new Vector2(210, 38);
        _launchBtn.Pressed += () =>
        {
            GameSetup.PlayerPilots = new List<Pilot>(_squad);
            GetTree().ChangeSceneToFile("res://Scenes/Battle.tscn");
        };
        launchCell.AddChild(_launchBtn);

        Refresh();
    }

    Control BuildCard(Pilot pilot)
    {
        var card = new PilotCard { Pilot = pilot };
        _cards.Add(card);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(340, 0) };
        panel.AddThemeStyleboxOverride("panel", Box(CellBg, Hairline, 1, 10, 8));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        panel.AddChild(box);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 9);
        box.AddChild(header);
        card.Icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(42, 42),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        header.AddChild(card.Icon);
        var nameBox = new VBoxContainer();
        nameBox.AddThemeConstantOverride("separation", 1);
        header.AddChild(nameBox);
        var callsign = Text(pilot.Callsign.ToUpper(), 14, TextBright, 2);
        callsign.TooltipText = $"Missions flown: {pilot.Missions}\nCareer kills: {pilot.CareerKills}";
        nameBox.AddChild(callsign);
        card.HullLine = Text("", 8, Muted, 2);
        nameBox.AddChild(card.HullLine);

        card.Condition = Text("", 8, Muted, 2);
        box.AddChild(card.Condition);

        int xpToNext = PilotRoster.XpToNext(pilot.Level);
        var xpBar = new XpTrack
        {
            Ratio = xpToNext > 0 ? (float)pilot.Xp / xpToNext : 1f,
            CustomMinimumSize = new Vector2(0, 6),
            TooltipText = $"{pilot.Xp} / {xpToNext} XP to next level",
        };
        box.AddChild(xpBar);
        box.AddChild(BuildPerkLine(pilot));

        // The stat readout is deliberately dense. Let it wrap inside the card
        // instead of increasing the grid's minimum width past the viewport.
        card.Stats = Text("", 9, Body, 0, wrap: true);
        box.AddChild(card.Stats);
        card.Progression = Text("", 8, Muted, 0, wrap: true);
        card.Progression.CustomMinimumSize = new Vector2(0, 25);
        box.AddChild(card.Progression);

        card.Add = FlatButton("", 9);
        card.Add.CustomMinimumSize = new Vector2(0, 24);
        card.Add.Pressed += () =>
        {
            if (pilot.CanDeploy && _squad.Count < 3 && !_squad.Contains(pilot))
            {
                _squad.Add(pilot);
                Refresh();
            }
        };
        box.AddChild(card.Add);
        RefreshCard(card);
        return panel;
    }

    static Control BuildPerkLine(Pilot pilot)
    {
        var line = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        line.AddThemeFontSizeOverride("normal_font_size", 9);
        if (pilot.Perks.Count == 0)
            line.Text = "[color=#5f7a92]No traits yet[/color]";
        else
        {
            line.Text = string.Join("  ·  ", pilot.Perks.Select(p =>
                $"[color={(p.Positive ? "#57e6a4" : "#ffb03d")}]{p.Name}[/color]"));
            line.TooltipText = string.Join("\n", pilot.Perks.Select(p => $"{p.Name}: {p.Description}"));
        }
        return line;
    }

    void RefreshCard(PilotCard card)
    {
        Pilot pilot = card.Pilot;
        ShipType type = pilot.Ship;
        card.Icon.Texture = type.GetSkin(0).Base;
        card.HullLine.Text = $"{type.DisplayName.ToUpper()}  ·  LV {pilot.Level}  ·  FIXED CLASS";

        card.HullLine.Text = $"{pilot.CareerDisplayName.ToUpper()}  //  LV {pilot.Level}";

        switch (pilot.Condition)
        {
            case PilotCondition.Wounded:
                card.Condition.Text = $"WOUNDED  ·  RECOVERY {pilot.RecoveryMissionsRemaining} MISSION{(pilot.RecoveryMissionsRemaining == 1 ? "" : "S")}";
                card.Condition.AddThemeColorOverride("font_color", Warning);
                break;
            default:
                card.Condition.Text = pilot.NeedsHullChoice ? "HULL DIRECTION REQUIRED · OPEN PILOT RECORD" : "READY";
                card.Condition.AddThemeColorOverride("font_color", pilot.NeedsHullChoice ? Warning : Positive);
                break;
        }

        int hp = Perks.EffectiveMaxHp(pilot, type.MaxHp);
        int maxShield = ShipUpgrades.MaxShield(pilot);
        int shieldRegen = Perks.EffectiveShieldRegen(pilot, ShipUpgrades.BaseShieldRegen(pilot));
        float damage = Perks.DisplayDamage(pilot, type.ShotDamage);
        float accuracy = Perks.DisplayAccuracy(pilot, ShipUpgrades.BaseAccuracy(pilot));
        float evasion = Perks.DisplayEvasion(pilot, type.Evasion);
        float maxMove = Perks.ApplyNormalMoveLimitModifiers(pilot, ShipUpgrades.BaseNormalMoveLimit(pilot));
        float maxThrottleTurn = Perks.ApplyNormalTurnLimitModifiers(pilot,
            ShipUpgrades.BaseNormalTurnLimitDegrees(pilot), true);
        float range = Perks.EffectiveFireRange(pilot, Fighter.FireRange);
        card.Stats.Text = $"SHIELD {maxShield}/{maxShield}  +{shieldRegen}/TURN   HULL {Mathf.Max(0, hp - pilot.HullDamage)}/{hp}   DMG {damage:0.#}   ACC {accuracy * 100:0}%   EVA {evasion * 100:0}%   MAX TURN {maxThrottleTurn:0}°   MAX NORMAL MOVE {maxMove:0}   RNG {range:0}";
        card.Progression.Text = type.ManeuverPool.Length == 0
            ? "No class maneuvers assigned yet."
            : $"HARDPOINTS {pilot.Upgrades.Count}/{type.UpgradeSlots.Length} · MANEUVERS {pilot.Maneuvers.Count}/{pilot.ManeuverSlots} · " + string.Join("  ·  ", type.ManeuverPool.Select(ability =>
                $"{AbilityDisplayName(ability)} {(pilot.HasManeuver(ability) ? "✓" : "—")}"));
    }

    void RemoveSlot(int slot)
    {
        if (slot < _squad.Count)
        {
            _squad.RemoveAt(slot);
            Refresh();
        }
    }

    void Refresh()
    {
        for (int i = 0; i < 3; i++)
        {
            _slotButtons[i].Icon = i < _squad.Count ? _squad[i].Ship.GetSkin(0).Base : null;
            _slotButtons[i].TooltipText = i < _squad.Count ? $"{_squad[i].Callsign} — click to remove" : "Empty slot";
        }
        foreach (PilotCard card in _cards)
        {
            RefreshCard(card);
            bool inSquad = _squad.Contains(card.Pilot);
            card.Add.Text = inSquad ? "IN SQUAD  ✓" : card.Pilot.CanDeploy ? "ADD TO SQUAD" : "UNAVAILABLE";
            card.Add.Disabled = !card.Pilot.CanDeploy || inSquad || _squad.Count >= 3;
        }
        _launchBtn.Disabled = _squad.Count < 1;
    }

    static string AbilityDisplayName(ShipAbility ability) => ability switch
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
        DrawStarfield(this, 4321, ScreenW, ScreenH);
        DrawNebula(this, new Vector2(940, 90), new Color(0.25f, 0.59f, 1f), 12, 20);
    }
}
