using Godot;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

/// <summary>Routes from a roster view to a specific pilot's persistent career record.</summary>
public static class PilotCareerNavigation
{
    public const string ScenePath = "res://Scenes/PilotCareer.tscn";
    public static Pilot SelectedPilot { get; private set; }
    public static string ReturnScene { get; private set; } = HangarNavigation.ScenePath;

    public static void Open(SceneTree tree, Pilot pilot, string returnScene)
    {
        SelectedPilot = pilot;
        ReturnScene = returnScene;
        tree.ChangeSceneToFile(ScenePath);
    }
}

/// <summary>
/// Detailed pilot record built around an airframe schematic: the ship silhouette
/// anchors the page, hardpoints hang off it as callout cells, the selected
/// hardpoint's module catalog lives in the right rail, and the maneuver loadout
/// docks underneath as a chip row. The level-3 hull frame decision replaces the
/// schematic with a two-candidate compare until it is made.
/// </summary>
public partial class PilotCareer : Node2D
{
    const float ScreenW = 1152f;
    const float ScreenH = 648f;

    // Center schematic geometry on the 1152x648 canvas.
    static readonly Vector2 SchematicPos = new(340, 158);
    static readonly Vector2 SchematicSize = new(576, 318);
    // Hardpoint cells around the ship: below-left, above-right, below-right.
    static readonly Vector2[] CellPositions = { new(348, 404), new(696, 176), new(696, 404) };
    static readonly Vector2 CellSize = new(206, 62);
    // Schematic-local callout endpoints per cell: ship edge -> cell edge.
    static readonly (Vector2 A, Vector2 B)[] CalloutEnds =
    {
        (new Vector2(232, 206), new Vector2(150, 246)),
        (new Vector2(340, 100), new Vector2(420, 80)),
        (new Vector2(344, 206), new Vector2(420, 246)),
    };

    CanvasLayer _ui;
    int _selectedSlot = -1;

    Pilot Pilot => PilotCareerNavigation.SelectedPilot;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Bg);
        _ui = new CanvasLayer();
        AddChild(_ui);
        Populate();
        GetViewport().SizeChanged += FitUiToViewport;
    }

    void Rebuild()
    {
        foreach (Node child in _ui.GetChildren())
            child.QueueFree();
        Populate();
    }

    // --- page ------------------------------------------------------------------

    void Populate()
    {
        if (Pilot == null)
        {
            _ui.AddChild(Text("No pilot record selected.", 20, TextBright));
            return;
        }
        if (_selectedSlot < 0 || _selectedSlot >= Pilot.Ship.UpgradeSlots.Length)
            _selectedSlot = DefaultSlot();

        BuildHeader();
        BuildAirframeColumn();
        if (Pilot.NeedsHullChoice)
            BuildHullDecision();
        else
            BuildSchematic();
        BuildManeuverSection();
        BuildRightRail();

        var back = FlatButton("<  RETURN TO HANGAR", 10);
        back.Position = new Vector2(56, 596);
        back.Size = new Vector2(210, 32);
        back.Pressed += () => GetTree().ChangeSceneToFile(PilotCareerNavigation.ReturnScene);
        _ui.AddChild(back);
        FitUiToViewport();
    }

    /// <summary>The first empty hardpoint, so the module catalog is useful on entry.</summary>
    int DefaultSlot()
    {
        for (int i = 0; i < Pilot.Ship.UpgradeSlots.Length; i++)
            if (!Pilot.UpgradeInSlot(Pilot.Ship.UpgradeSlots[i]).HasValue)
                return i;
        return 0;
    }

    // The panels are designed on a 1152 x 648 canvas.  Scale and center that
    // canvas whenever the window is smaller so no part of the career record is
    // clipped at the right or bottom edge.
    void FitUiToViewport()
    {
        if (_ui == null)
            return;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float scale = Mathf.Max(0.01f, Mathf.Min(viewport.X / ScreenW, viewport.Y / ScreenH));
        _ui.Scale = Vector2.One * scale;
        _ui.Offset = (viewport - new Vector2(ScreenW, ScreenH) * scale) * 0.5f;
    }

    void BuildHeader()
    {
        var head = new HBoxContainer { Position = new Vector2(56, 34), Size = new Vector2(1040, 76) };
        head.AddThemeConstantOverride("separation", 24);
        _ui.AddChild(head);

        var callsign = Text(Pilot.Callsign.ToUpper(), 44, TextBright, 6);
        callsign.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        head.AddChild(callsign);

        var metaBox = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        metaBox.AddThemeConstantOverride("margin_bottom", 10);
        metaBox.AddChild(Text($"PILOT CAREER · {Pilot.CareerDisplayName.ToUpper()}", 9, Muted, 4));
        head.AddChild(metaBox);

        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        bool wounded = Pilot.Condition == PilotCondition.Wounded;
        bool decision = Pilot.NeedsCareerChoice;
        var statusBox = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        statusBox.AddThemeConstantOverride("margin_bottom", 12);
        var statusRow = new HBoxContainer();
        statusRow.AddThemeConstantOverride("separation", 10);
        var dotSize = new Vector2(10, 10);
        if (!wounded && decision)
            statusRow.AddChild(new PulseDot { DotColor = Accent, CustomMinimumSize = dotSize, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        else
            statusRow.AddChild(new StatusDot { DotColor = wounded ? Warning : Positive, CustomMinimumSize = dotSize, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        string status = wounded ? $"WOUNDED · {Pilot.RecoveryMissionsRemaining} MISSIONS"
            : Pilot.NeedsHullChoice ? "HULL FRAME DECISION"
            : Pilot.NeedsManeuverChoice ? "MANEUVER SLOT OPEN"
            : "READY FOR DEPLOYMENT";
        Color statusColor = wounded ? Warning : decision ? Accent : Positive;
        statusRow.AddChild(Text($"CREDITS {CampaignData.Credits} · {status}", 10, statusColor, 4));
        statusBox.AddChild(statusRow);
        head.AddChild(statusBox);

        _ui.AddChild(new ColorRect { Position = new Vector2(56, 118), Size = new Vector2(1040, 1), Color = Hairline });
    }

    // --- left column: level, stats, perks ----------------------------------------

    void BuildAirframeColumn()
    {
        var box = new VBoxContainer { Position = new Vector2(56, 140), Size = new Vector2(252, 440) };
        box.AddThemeConstantOverride("separation", 8);
        _ui.AddChild(box);
        box.AddChild(SectionLabel("AIRFRAME"));

        var levelRow = new HBoxContainer();
        levelRow.AddThemeConstantOverride("separation", 14);
        levelRow.AddChild(Text(Pilot.Level.ToString(), 42, TextBright));
        var levelCap = Text("PILOT\nLEVEL", 9, Muted, 3);
        levelCap.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        levelRow.AddChild(levelCap);
        box.AddChild(levelRow);

        int xpToNext = PilotRoster.XpToNext(Pilot.Level);
        box.AddChild(new XpTrack { Ratio = xpToNext > 0 ? (float)Pilot.Xp / xpToNext : 1f, CustomMinimumSize = new Vector2(0, 8) });
        box.AddChild(Text($"{Pilot.Xp} / {xpToNext} XP · {Pilot.Missions} MISSIONS · {Pilot.CareerKills} KILLS", 9, Muted, 2));

        var gridFrame = new PanelContainer();
        gridFrame.AddThemeStyleboxOverride("panel", Box(Hairline, Colors.Transparent, 0, 1, 1));
        box.AddChild(gridFrame);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 1);
        grid.AddThemeConstantOverride("v_separation", 1);
        gridFrame.AddChild(grid);

        int maxHp = Perks.EffectiveMaxHp(Pilot, Pilot.Ship.MaxHp);
        int maxShield = ShipUpgrades.MaxShield(Pilot);
        int shieldRegen = Perks.EffectiveShieldRegen(Pilot, ShipUpgrades.BaseShieldRegen(Pilot));
        float damage = Perks.DisplayDamage(Pilot, Pilot.Ship.ShotDamage);
        float accuracy = Perks.DisplayAccuracy(Pilot, ShipUpgrades.BaseAccuracy(Pilot));
        float evasion = Perks.DisplayEvasion(Pilot, Pilot.Ship.Evasion);
        grid.AddChild(StatCellSrc($"{Mathf.Max(0, maxHp - Pilot.HullDamage)}/{maxHp}", "HULL", null,
            Pilot.HullDamage > 0 ? Warning : (Color?)null));
        grid.AddChild(StatCellSrc(maxShield.ToString(), $"SHIELD · +{shieldRegen}/TURN",
            Pilot.HasUpgrade(ShipUpgrade.ShieldsCapacity) ? $"+{ShipUpgrades.ShieldCapacityBonus} SHIELD CAPACITOR"
            : Pilot.HasUpgrade(ShipUpgrade.ShieldsRegen) ? $"+{ShipUpgrades.ShieldRegenBonus}/TURN FLUX RECYCLER" : null));
        grid.AddChild(StatCellSrc($"{accuracy * 100:0}%", "GUNNERY",
            Pilot.HasUpgrade(ShipUpgrade.GunsAccuracy) ? $"+{ShipUpgrades.AccuracyBonus * 100:0}% TARGETING ARRAY" : null));
        grid.AddChild(StatCellSrc($"{evasion * 100:0}%", "EVASION", null));
        grid.AddChild(StatCellSrc($"{damage:0.#}", "DAMAGE",
            Pilot.HasUpgrade(ShipUpgrade.GunsExtraShot) ? $"+{ShipUpgrades.ExtraShots} SHOT BURST LOADER" : null));
        grid.AddChild(StatCellSrc($"{ShipUpgrades.BaseNormalTurnLimitDegrees(Pilot):0}°", "MAX NORMAL TURN",
            Pilot.HasUpgrade(ShipUpgrade.EngineTurn) ? $"+{ShipUpgrades.NormalTurnLimitBonusDegrees:0}° VECTOR NOZZLES" : null));

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        box.AddChild(SectionLabel("PERKS"));
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 96),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        box.AddChild(scroll);
        var perkList = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        perkList.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(perkList);
        if (Pilot.Perks.Count == 0)
        {
            perkList.AddChild(Text("No traits earned yet.", 10, Muted, 0, wrap: true));
        }
        else
        {
            foreach (Perk perk in Pilot.Perks)
            {
                perkList.AddChild(Text(perk.Name.ToUpper(), 10, perk.Positive ? Positive : Warning, 2));
                perkList.AddChild(Text(perk.Description, 9, Muted, 0, wrap: true));
                perkList.AddChild(new ColorRect { CustomMinimumSize = new Vector2(0, 1), Color = new Color(Hairline.R, Hairline.G, Hairline.B, 0.14f) });
            }
        }
    }

    /// <summary>Stat cell with an optional provenance line naming the installed module.</summary>
    static Control StatCellSrc(string value, string key, string source, Color? valueColor = null)
    {
        var cell = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 54) };
        cell.AddThemeStyleboxOverride("panel", Box(CellBg, Colors.Transparent, 0, 12, 8));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        cell.AddChild(box);
        box.AddChild(Text(value, 16, valueColor ?? TextBright));
        box.AddChild(Text(key, 8, Muted, 2));
        if (source != null)
            box.AddChild(Text(source, 8, Positive, 1));
        return cell;
    }

    // --- center: airframe schematic with hardpoint callouts ----------------------

    void BuildSchematic()
    {
        _ui.AddChild(SectionAt($"LOADOUT SCHEMATIC · {Pilot.Ship.DisplayName.ToUpper()}", new Vector2(340, 140)));

        var schematic = new FrameSchematic
        {
            ClassId = Pilot.ClassId,
            Position = SchematicPos,
            Size = SchematicSize,
            ShipRadius = 78f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _ui.AddChild(schematic);

        ShipUpgradeSlot[] slots = Pilot.Ship.UpgradeSlots;
        for (int i = 0; i < slots.Length && i < CellPositions.Length; i++)
        {
            schematic.Callouts.Add((CalloutEnds[i].A, CalloutEnds[i].B, i == _selectedSlot));
            _ui.AddChild(HardpointCellUi(i, slots[i]));
        }

        // Before the hull frame is chosen the second hardpoint is only a promise;
        // show it as a dormant ghost cell hanging off a faint callout.
        if (Pilot.Ship == ShipTypes.BaseClass(Pilot.ClassId) && slots.Length < CellPositions.Length)
        {
            int index = slots.Length;
            schematic.Callouts.Add((CalloutEnds[index].A, CalloutEnds[index].B, false));
            var ghost = new PanelContainer { Position = CellPositions[index], Size = CellSize };
            ghost.AddThemeStyleboxOverride("panel",
                Box(Colors.Transparent, new Color(Hairline.R, Hairline.G, Hairline.B, 0.12f), 1, 10, 8));
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 3);
            ghost.AddChild(box);
            box.AddChild(Text("+1 HARDPOINT", 8, Dim, 2));
            box.AddChild(Text("WITH HULL FRAME · LEVEL 3", 8, Dim, 1));
            _ui.AddChild(ghost);
        }
    }

    Control HardpointCellUi(int index, ShipUpgradeSlot slot)
    {
        ShipUpgrade? installed = Pilot.UpgradeInSlot(slot);
        bool selected = index == _selectedSlot;
        var cell = new SchematicCell { Position = CellPositions[index], Size = CellSize };
        StyleBoxFlat Style(float borderAlpha) => Box(new Color(CellBg.R, CellBg.G, CellBg.B, 0.85f),
            selected ? new Color(Accent.R, Accent.G, Accent.B, borderAlpha) : Hairline, 1, 10, 8);
        cell.AddThemeStyleboxOverride("panel", Style(0.65f));
        cell.MouseEntered += () => cell.AddThemeStyleboxOverride("panel",
            Box(new Color(CellBg.R, CellBg.G, CellBg.B, 0.85f), new Color(Accent.R, Accent.G, Accent.B, 0.8f), 1, 10, 8));
        cell.MouseExited += () => cell.AddThemeStyleboxOverride("panel", Style(0.65f));
        cell.Pressed += () =>
        {
            _selectedSlot = index;
            Rebuild();
        };

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 3);
        cell.AddChild(box);
        box.AddChild(Text($"{ShipUpgrades.SlotName(slot)} HARDPOINT", 8, selected ? Accent : Muted, 2));
        if (installed.HasValue)
        {
            ShipUpgradeDefinition definition = ShipUpgrades.Get(installed.Value);
            box.AddChild(Text(definition.Name.ToUpper(), 10, TextBright, 1));
            box.AddChild(Text(definition.Description, 8, Positive, 0));
        }
        else
        {
            box.AddChild(Text("EMPTY", 10, Body, 1));
            box.AddChild(Text($"{ShipUpgrades.ForSlot(slot).Count()} MODULES AVAILABLE >", 8, selected ? Accent : Muted, 1));
        }
        return cell;
    }

    // --- center, level-3 state: the permanent hull frame decision ----------------

    void BuildHullDecision()
    {
        var label = new HBoxContainer { Position = new Vector2(340, 138) };
        label.AddThemeConstantOverride("separation", 8);
        label.AddChild(new PulseDot { DotColor = Accent, CustomMinimumSize = new Vector2(8, 8), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        label.AddChild(Text("HULL FRAME · CHOOSE ONE — PERMANENT", 9, Accent, 4));
        _ui.AddChild(label);

        ShipType[] branches = ShipTypes.HullBranches(Pilot.ClassId);
        for (int i = 0; i < branches.Length && i < 2; i++)
            _ui.AddChild(HullCandidateCard(branches[i], new Vector2(340 + i * 302, 164)));
    }

    Control HullCandidateCard(ShipType hull, Vector2 position)
    {
        var panel = new PanelContainer { Position = position, Size = new Vector2(274, 318) };
        panel.AddThemeStyleboxOverride("panel",
            Box(new Color(CellBg.R, CellBg.G, CellBg.B, 0.85f), new Color(Accent.R, Accent.G, Accent.B, 0.5f), 1, 12, 10));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);

        box.AddChild(new FrameSchematic
        {
            ClassId = Pilot.ClassId,
            Ghost = true,
            DrawRings = false,
            ShipRadius = 34f,
            CustomMinimumSize = new Vector2(0, 78),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        box.AddChild(Text(hull.DisplayName.ToUpper(), 13, TextBright, 2));

        ShipType current = Pilot.Ship;
        box.AddChild(StatRow(
            ("HULL", hull.MaxHp.ToString(), DeltaColor(hull.MaxHp, current.MaxHp)),
            ("SHD", hull.MaxShield.ToString(), DeltaColor(hull.MaxShield, current.MaxShield)),
            ("DMG", hull.ShotDamage.ToString(), DeltaColor(hull.ShotDamage, current.ShotDamage))));
        box.AddChild(StatRow(
            ("ACC", $"{hull.Accuracy * 100:0}%", DeltaColor(hull.Accuracy, current.Accuracy)),
            ("EVA", $"{hull.Evasion * 100:0}%", DeltaColor(hull.Evasion, current.Evasion)),
            ("MAX TURN", $"{hull.NormalTurnLimitDegrees:0}°", DeltaColor(hull.NormalTurnLimitDegrees, current.NormalTurnLimitDegrees))));

        ShipUpgradeSlot extraSlot = hull.UpgradeSlots.Last();
        box.AddChild(Text($"ADDS {ShipUpgrades.SlotName(extraSlot)} HARDPOINT", 9, Accent, 2));
        box.AddChild(Text(hull.Description, 9, Muted, 0, wrap: true));

        var choose = FlatButton("SELECT", 9);
        choose.CustomMinimumSize = new Vector2(0, 30);
        choose.Pressed += () =>
        {
            if (Pilot.ChooseHull(hull))
            {
                CampaignData.SaveCampaign();
                _selectedSlot = -1;
                Rebuild();
            }
        };
        box.AddChild(choose);
        return panel;
    }

    static Color? DeltaColor(float candidate, float current) =>
        Mathf.IsEqualApprox(candidate, current) ? null : candidate > current ? Positive : Warning;

    // --- maneuver loadout ---------------------------------------------------------

    void BuildManeuverSection()
    {
        var box = new VBoxContainer { Position = new Vector2(340, 496), Size = new Vector2(566, 140) };
        box.AddThemeConstantOverride("separation", 8);
        _ui.AddChild(box);

        if (Pilot.NeedsManeuverChoice)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new PulseDot { DotColor = Accent, CustomMinimumSize = new Vector2(8, 8), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            row.AddChild(Text($"MANEUVER LOADOUT · {Pilot.Maneuvers.Count} / {Pilot.ManeuverSlots} · OPEN SLOT — EQUIP ONE", 9, Accent, 4));
            box.AddChild(row);
        }
        else
        {
            box.AddChild(SectionLabel($"MANEUVER LOADOUT · {Pilot.Maneuvers.Count} / {Pilot.ManeuverSlots} SLOTS"));
        }

        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 8);
        box.AddChild(flow);
        foreach (ShipAbility ability in Pilot.Ship.ManeuverPool)
            flow.AddChild(ManeuverChip(ability));

        int nextLevel = Pilot.Ship.ManeuverSlotLevels.Where(level => level > Pilot.Level).DefaultIfEmpty(0).Min();
        string next = nextLevel > 0 ? $"NEXT SLOT AT LEVEL {nextLevel}" : "ALL SLOTS EARNED";
        box.AddChild(Text($"{next} · CLICK TO EQUIP OR UNEQUIP", 8, Muted, 2));
    }

    Button ManeuverChip(ShipAbility ability)
    {
        bool equipped = Pilot.HasManeuver(ability);
        bool slotFree = Pilot.Maneuvers.Count < Pilot.ManeuverSlots;
        var chip = new Button { Text = AbilityName(ability).ToUpper() };
        chip.AddThemeFontOverride("font", Tracked(2));
        chip.AddThemeFontSizeOverride("font_size", 9);
        if (equipped)
        {
            chip.AddThemeColorOverride("font_color", Positive);
            chip.AddThemeColorOverride("font_hover_color", TextBright);
            chip.AddThemeColorOverride("font_pressed_color", TextBright);
            chip.AddThemeStyleboxOverride("normal", Box(new Color(Positive.R, Positive.G, Positive.B, 0.07f), new Color(Positive.R, Positive.G, Positive.B, 0.5f), 1, 10, 4));
            chip.AddThemeStyleboxOverride("hover", Box(new Color(Positive.R, Positive.G, Positive.B, 0.14f), new Color(Positive.R, Positive.G, Positive.B, 0.8f), 1, 10, 4));
            chip.AddThemeStyleboxOverride("pressed", Box(new Color(Positive.R, Positive.G, Positive.B, 0.2f), Positive, 1, 10, 4));
        }
        else
        {
            chip.AddThemeColorOverride("font_color", Body);
            chip.AddThemeColorOverride("font_hover_color", TextBright);
            chip.AddThemeColorOverride("font_pressed_color", TextBright);
            chip.AddThemeColorOverride("font_disabled_color", Dim);
            chip.AddThemeStyleboxOverride("normal", Box(Colors.Transparent, Hairline, 1, 10, 4));
            chip.AddThemeStyleboxOverride("hover", Box(new Color(Accent.R, Accent.G, Accent.B, 0.08f), new Color(Accent.R, Accent.G, Accent.B, 0.6f), 1, 10, 4));
            chip.AddThemeStyleboxOverride("pressed", Box(new Color(Accent.R, Accent.G, Accent.B, 0.16f), Accent, 1, 10, 4));
            chip.AddThemeStyleboxOverride("disabled", Box(Colors.Transparent, new Color(Hairline.R, Hairline.G, Hairline.B, 0.1f), 1, 10, 4));
            chip.Disabled = !slotFree;
        }
        chip.Pressed += () =>
        {
            if (equipped)
                Pilot.UnequipManeuver(ability);
            else if (!Pilot.EquipManeuver(ability))
                return;
            CampaignData.SaveCampaign();
            Rebuild();
        };
        return chip;
    }

    // --- right rail: module catalog for the selected hardpoint --------------------

    void BuildRightRail()
    {
        _ui.AddChild(new ColorRect { Position = new Vector2(920, 140), Size = new Vector2(1, 440), Color = new Color(Hairline.R, Hairline.G, Hairline.B, 0.14f) });
        var rail = new VBoxContainer { Position = new Vector2(938, 140), Size = new Vector2(158, 440) };
        rail.AddThemeConstantOverride("separation", 10);
        _ui.AddChild(rail);

        if (Pilot.NeedsHullChoice)
        {
            rail.AddChild(SectionLabel("BRIEFING"));
            rail.AddChild(Text("The hull frame decision is permanent and sets this pilot's second hardpoint.", 9, Muted, 0, wrap: true));
            rail.AddChild(Text("Installed modules and equipped maneuvers carry over to the new frame.", 9, Muted, 0, wrap: true));
            return;
        }

        ShipUpgradeSlot slot = Pilot.Ship.UpgradeSlots[_selectedSlot];
        ShipUpgrade? installed = Pilot.UpgradeInSlot(slot);
        rail.AddChild(SectionLabel($"{ShipUpgrades.SlotName(slot)} MODULES"));

        if (installed.HasValue)
        {
            ShipUpgradeDefinition definition = ShipUpgrades.Get(installed.Value);
            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", Box(new Color(CellBg.R, CellBg.G, CellBg.B, 0.85f), new Color(Positive.R, Positive.G, Positive.B, 0.45f), 1, 10, 8));
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            panel.AddChild(box);
            box.AddChild(Text(definition.Name.ToUpper(), 10, TextBright, 1));
            box.AddChild(Text(definition.Description, 9, Muted, 0, wrap: true));
            box.AddChild(Text("INSTALLED · PERMANENT", 8, Positive, 2));
            rail.AddChild(panel);
            return;
        }

        foreach (ShipUpgradeDefinition definition in ShipUpgrades.ForSlot(slot))
        {
            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", Box(new Color(CellBg.R, CellBg.G, CellBg.B, 0.85f), Hairline, 1, 10, 8));
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            panel.AddChild(box);
            box.AddChild(Text(definition.Name.ToUpper(), 10, TextBright, 1));
            box.AddChild(Text(definition.Description, 9, Muted, 0, wrap: true));
            var install = FlatButton($"INSTALL · {ShipUpgrades.Cost} CR", 8);
            install.Disabled = CampaignData.Credits < ShipUpgrades.Cost;
            ShipUpgrade upgrade = definition.Id;
            install.Pressed += () =>
            {
                if (CampaignData.PurchaseShipUpgrade(Pilot, upgrade))
                    Rebuild();
            };
            box.AddChild(install);
            rail.AddChild(panel);
        }
        rail.AddChild(Text($"PERMANENT ONCE INSTALLED · SQUAD CREDITS {CampaignData.Credits}", 8, Muted, 1, wrap: true));
    }

    static Control SectionAt(string text, Vector2 position)
    {
        Label label = SectionLabel(text);
        label.Position = position;
        return label;
    }

    static string AbilityName(ShipAbility ability) => ability switch
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
        DrawStarfield(this, 38119, ScreenW, ScreenH);
        DrawNebula(this, new Vector2(900, 110), new Color(0.25f, 0.59f, 1f));
        DrawNebula(this, new Vector2(170, 560), new Color(0.16f, 0.86f, 0.75f), 12, 20, 0.005f);
    }
}

/// <summary>
/// Hairline schematic of a pilot's airframe: simple per-class silhouette inside
/// orbital rings, with optional callout lines running to hardpoint cells.
/// Ghost mode draws a faint unfilled outline for hull-candidate cards.
/// </summary>
public partial class FrameSchematic : Control
{
    public string ClassId = "scout";
    public bool Ghost;
    public bool DrawRings = true;
    public float ShipRadius = 78f;
    /// <summary>Callout segments in local coordinates: ship-edge anchor to cell edge.</summary>
    public readonly List<(Vector2 A, Vector2 B, bool Active)> Callouts = new();

    // Right half of each silhouette in unit space, nose up: first and last
    // points sit on the centerline so the left side is a clean mirror.
    static readonly Vector2[] ScoutHalf =
    {
        new(0, -1f), new(0.16f, -0.42f), new(0.82f, 0.3f), new(0.54f, 0.42f),
        new(0.2f, 0.26f), new(0.18f, 0.65f), new(0.36f, 0.92f), new(0.12f, 0.8f), new(0, 0.86f),
    };
    static readonly Vector2[] RaptorHalf =
    {
        new(0, -0.85f), new(0.3f, -0.48f), new(0.34f, -0.05f), new(0.96f, 0.35f),
        new(0.9f, 0.52f), new(0.42f, 0.46f), new(0.5f, 0.9f), new(0.2f, 0.82f), new(0, 0.9f),
    };
    static readonly Vector2[] ZtHalf =
    {
        new(0, -0.9f), new(0.34f, -0.62f), new(0.42f, -0.1f), new(0.78f, 0.2f),
        new(0.8f, 0.62f), new(0.5f, 0.55f), new(0.44f, 0.92f), new(0.16f, 0.85f), new(0, 0.92f),
    };

    Vector2[] Half() => ClassId switch
    {
        "raptor" => RaptorHalf,
        "zt" => ZtHalf,
        _ => ScoutHalf,
    };

    public override void _Draw()
    {
        Vector2 center = Size / 2f;
        if (DrawRings)
        {
            DrawArc(center, ShipRadius * 1.32f, 0, Mathf.Tau, 72, new Color(0.35f, 0.67f, 1f, 0.22f), 1, true);
            const int dashes = 28;
            for (int i = 0; i < dashes; i++)
            {
                float from = Mathf.Tau * i / dashes;
                DrawArc(center, ShipRadius * 1.06f, from, from + Mathf.Tau / dashes * 0.55f, 6, new Color(0.35f, 0.67f, 1f, 0.14f), 1, true);
            }
        }

        Vector2[] half = Half();
        var points = new List<Vector2>();
        foreach (Vector2 p in half)
            points.Add(center + p * ShipRadius);
        for (int i = half.Length - 2; i >= 1; i--)
            points.Add(center + new Vector2(-half[i].X, half[i].Y) * ShipRadius);
        points.Add(points[0]);
        Vector2[] outline = points.ToArray();

        if (!Ghost)
            DrawColoredPolygon(outline[..^1], new Color(SignalUi.Accent.R, SignalUi.Accent.G, SignalUi.Accent.B, 0.05f));
        Color line = Ghost ? new Color(0.55f, 0.75f, 0.95f, 0.4f) : new Color(0.55f, 0.76f, 0.96f, 0.75f);
        DrawPolyline(outline, line, Ghost ? 1f : 1.2f, true);
        DrawLine(center + new Vector2(0, -0.5f) * ShipRadius, center + new Vector2(0, 0.55f) * ShipRadius,
            new Color(line.R, line.G, line.B, line.A * 0.35f), 1);

        foreach ((Vector2 a, Vector2 b, bool active) in Callouts)
        {
            Color callout = active
                ? new Color(SignalUi.Accent.R, SignalUi.Accent.G, SignalUi.Accent.B, 0.6f)
                : new Color(0.47f, 0.71f, 0.9f, 0.3f);
            DrawLine(a, b, callout, 1);
            DrawCircle(a, 2.5f, active ? SignalUi.Accent : callout);
        }
    }
}

/// <summary>Clickable hardpoint cell hanging off the airframe schematic.</summary>
public partial class SchematicCell : PanelContainer
{
    public event System.Action Pressed;

    public SchematicCell()
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            Pressed?.Invoke();
    }
}
