using Godot;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

public partial class RunScreen
{
    /// <summary>The pilot whose page is open over whatever was showing, or null.</summary>
    Pilot _detailPilot;

    void OpenPilot(Pilot pilot)
    {
        _detailPilot = pilot;
        Render();
    }

    /// <summary>
    /// Everything about one pilot and their ship: the ship's numbers with its
    /// modules fitted, each module, maneuvers and masteries, the instinct, any
    /// scars, and their record.
    /// </summary>
    Control BuildPilotPage()
    {
        Pilot pilot = _detailPilot;
        VBoxContainer page = Stack(16);
        page.AddChild(Header($"LV {pilot.Level} PILOT", pilot.Callsign, () =>
        {
            _detailPilot = null;
            Render();
        }));
        (TouchScroll scroll, VBoxContainer content) = ScrollBody(12);
        page.AddChild(scroll);

        content.AddChild(PilotIdentityCard(pilot));

        content.AddChild(Text("SHIP STATS", FontCaption, Muted, 4));
        content.AddChild(ShipStatsCard(pilot));

        content.AddChild(Text($"MODULES · {pilot.Upgrades.Count}", FontCaption, Muted, 4));
        PanelContainer modules = Card();
        VBoxContainer moduleRows = Stack(14);
        modules.AddChild(moduleRows);
        foreach (ShipUpgradeDefinition module in pilot.Upgrades.Select(ShipUpgrades.Get).OrderBy(module => module.Slot))
            moduleRows.AddChild(ModuleRow(module));
        if (pilot.Upgrades.Count == 0)
            moduleRows.AddChild(Text("No modules yet. Level-ups and module crates fit them, as many as you take.",
                FontCaption, Muted, 0, wrap: true));
        content.AddChild(modules);

        content.AddChild(Text($"MANEUVERS · {pilot.Maneuvers.Count} OF {Pilot.MaxManeuvers}", FontCaption, Muted, 4));
        PanelContainer maneuvers = Card();
        VBoxContainer maneuverRows = Stack(16);
        maneuvers.AddChild(maneuverRows);
        foreach (ShipAbility ability in pilot.Maneuvers)
            maneuverRows.AddChild(ManeuverRow(pilot, ability));
        List<ShipAbility> unlearned = pilot.UnlearnedManeuvers.ToList();
        if (pilot.Maneuvers.Count < Pilot.MaxManeuvers && unlearned.Count > 0)
            maneuverRows.AddChild(Text("CAN LEARN ONE OF · " + string.Join(" · ", unlearned.Select(a => ManeuverCatalog.AbilityName(a).ToUpper())),
                FontMicro, Muted, 2, wrap: true));
        content.AddChild(maneuvers);

        content.AddChild(Text("INSTINCT", FontCaption, Muted, 4));
        PanelContainer instinct = Card();
        VBoxContainer instinctStack = Stack(4);
        instinct.AddChild(instinctStack);
        Perk trait = pilot.Instincts.FirstOrDefault();
        if (trait == null)
            instinctStack.AddChild(Text("No instinct.", FontCaption, Muted, 0));
        else
        {
            instinctStack.AddChild(Text(trait.Name.ToUpper(), FontBody, Instinct, 2));
            instinctStack.AddChild(Text(trait.Description, FontCaption, Body, 0, wrap: true));
        }
        content.AddChild(instinct);

        content.AddChild(Text("SCARS", FontCaption, Muted, 4));
        PanelContainer scars = Card();
        VBoxContainer scarStack = Stack(12);
        scars.AddChild(scarStack);
        if (!pilot.Scars.Any())
            scarStack.AddChild(Text("No scars.", FontCaption, Positive, 0));
        foreach (Perk scar in pilot.Scars)
        {
            VBoxContainer words = Stack(4);
            words.AddChild(Text(scar.Name.ToUpper(), FontBody, Warning, 2));
            words.AddChild(Text(scar.Description, FontCaption, Body, 0, wrap: true));
            scarStack.AddChild(words);
        }
        if (pilot.Scars.Any())
            scarStack.AddChild(Text("A repair dock's medic treats one scar per visit; waiting out an ion storm can fade one.",
                FontMicro, Muted, 0, wrap: true));
        content.AddChild(scars);
        return page;
    }

    /// <summary>The ship, its role and line, its hull, and the pilot's XP and record.</summary>
    Control PilotIdentityCard(Pilot pilot)
    {
        ShipType frame = pilot.Ship;
        PanelContainer card = Card();
        VBoxContainer stack = Stack(12);
        card.AddChild(stack);

        HBoxContainer row = Row(16);
        row.AddChild(ShipIcon(frame, 128f, faded: !pilot.Alive));
        VBoxContainer words = Stack(4);
        words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        words.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        HBoxContainer title = Row(12);
        Label name = Text(pilot.FrameName, FontBody, TextBright, 2);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.AddChild(name);
        (string status, ChipRole role) = StatusOf(pilot);
        title.AddChild(Tag(status, role));
        words.AddChild(title);
        words.AddChild(RoleLabel(frame, $" · {ShipTypes.ClassName(pilot.ClassId).ToUpper()} LINE"));
        words.AddChild(Text(frame.Description, FontCaption, Body, 0, wrap: true));
        row.AddChild(words);
        stack.AddChild(row);

        if (pilot.Alive)
        {
            HBoxContainer hullRow = Row(12);
            hullRow.AddChild(new HullBar
            {
                Fraction = pilot.Hull / (float)pilot.MaxHull,
                CustomMinimumSize = new Vector2(0, 12),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
            string hull = $"HULL {pilot.Hull}/{pilot.MaxHull}";
            if (pilot.HullDamage > 0)
                hull += " · REPAIR AT A DOCK";
            hullRow.AddChild(Text(hull, FontMicro, Body, 1));
            stack.AddChild(hullRow);
        }

        int xpToNext = Pilot.XpToNext(pilot.Level);
        HBoxContainer xpRow = Row(12);
        xpRow.AddChild(Text(pilot.IsMaxLevel ? "MAX LEVEL" : $"XP {pilot.Xp}/{xpToNext}", FontMicro, Muted, 2));
        if (!pilot.IsMaxLevel)
            xpRow.AddChild(new XpTrack
            {
                Ratio = xpToNext > 0 ? pilot.Xp / (float)xpToNext : 1f,
                CustomMinimumSize = new Vector2(0, 10),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        stack.AddChild(xpRow);
        stack.AddChild(Text($"{Plural(pilot.Kills, "KILL")} · {Plural(pilot.Battles, "BATTLE")}", FontMicro, Muted, 2));
        return card;
    }

    /// <summary>
    /// The ship's numbers at its refit with its modules fitted, two to a row.
    /// What the modules add is shown beside each number; the refit is named
    /// above them.
    /// </summary>
    static Control ShipStatsCard(Pilot pilot)
    {
        ShipStats bare = ShipStats.Frame(pilot.Ship).Refitted(pilot.Refit);
        ShipStats s = pilot.Stats;
        PanelContainer card = Card();
        VBoxContainer stack = Stack(14);
        card.AddChild(stack);
        int next = Refits.Levels.FirstOrDefault(level => level > pilot.Level);
        string refit = pilot.Refit == 0
            ? "MK I · no refits yet"
            : $"{Refits.Name(pilot.Refit)} · hull, shields, regen and damage +{(Refits.Multiplier(pilot.Refit) - 1f) * 100:0}% over Mk I";
        stack.AddChild(Text(refit + (next > 0 ? $". Next refit at level {next}." : "."), FontCaption, Positive, 0, wrap: true));
        var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 14);
        stack.AddChild(grid);

        void Add(string label, string value, params (float Delta, string Text)[] changes)
        {
            VBoxContainer tile = Stack(2);
            tile.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            tile.AddChild(Text(label, FontMicro, Muted, 3));
            HBoxContainer line = Row(10);
            line.AddChild(Text(value, FontTitle, TextBright, 1));
            foreach ((float delta, string text) in changes.Where(c => Mathf.Abs(c.Delta) > 0.001f))
            {
                Label change = Text(text, FontCaption, delta > 0 ? Positive : Warning, 1);
                change.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                line.AddChild(change);
            }
            tile.AddChild(line);
            grid.AddChild(tile);
        }

        Add("HULL", s.MaxHull.ToString(), (s.MaxHull - bare.MaxHull, Signed(s.MaxHull - bare.MaxHull)));
        Add("SHIELDS", s.MaxShield.ToString(), (s.MaxShield - bare.MaxShield, Signed(s.MaxShield - bare.MaxShield)));
        Add("SHIELD REGEN", $"{s.ShieldRegen}/TURN", (s.ShieldRegen - bare.ShieldRegen, Signed(s.ShieldRegen - bare.ShieldRegen)));
        Add("DAMAGE", $"{s.ShotDamage}/SHOT", (s.ShotDamage - bare.ShotDamage, Signed(s.ShotDamage - bare.ShotDamage)));
        Add("SHOTS", $"{s.ShotsMin}–{s.ShotsMax}", (s.ShotsMin - bare.ShotsMin, Signed(s.ShotsMin - bare.ShotsMin)));
        Add("ACCURACY", Percent(s.Accuracy), (s.Accuracy - bare.Accuracy, Signed((s.Accuracy - bare.Accuracy) * 100f, "%")));
        Add("EVASION", Percent(s.Evasion), (s.Evasion - bare.Evasion, Signed((s.Evasion - bare.Evasion) * 100f, "%")));
        Add("FIRING CONE", $"{s.FireConeDeg * 2f:0}°", (s.FireConeDeg - bare.FireConeDeg, Signed((s.FireConeDeg - bare.FireConeDeg) * 2f, "°")));
        Add("RANGE", $"{Fighter.FireRange:0}");
        Add("TURN", $"{s.TurnDeg:0}°", (s.TurnDeg - bare.TurnDeg, Signed(s.TurnDeg - bare.TurnDeg, "°")));
        // A lower minimum move is an improvement: the ship can fly slower.
        Add("SPEED", $"{s.MinMove:0}–{s.MaxMove:0}",
            (bare.MinMove - s.MinMove, Signed(s.MinMove - bare.MinMove) + " MIN"),
            (s.MaxMove - bare.MaxMove, Signed(s.MaxMove - bare.MaxMove) + " MAX"));

        stack.AddChild(Text("Instincts, scars and maneuvers only apply in their moment, so they aren't in these numbers.",
            FontMicro, Muted, 0, wrap: true));
        return card;
    }

    static Control ModuleRow(ShipUpgradeDefinition module)
    {
        VBoxContainer row = Stack(4);
        row.AddChild(Text($"{ShipUpgrades.SlotName(module.Slot)} · {module.Name.ToUpper()}", FontCaption, Positive, 2));
        row.AddChild(Text(module.Description, FontCaption, Body, 0, wrap: true));
        return row;
    }

    static Control ManeuverRow(Pilot pilot, ShipAbility ability)
    {
        bool mastered = pilot.HasMastered(ability);
        HBoxContainer row = Row(14);
        ManeuverInfo info = ManeuverCatalog.All.FirstOrDefault(m => m.Ability == ability);
        if (info != null)
            row.AddChild(new ManeuverGlyph
            {
                Action = info.Action,
                Tint = mastered ? Instinct : info.Color,
                CustomMinimumSize = new Vector2(64, 64),
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        VBoxContainer words = Stack(4);
        words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        HBoxContainer title = Row(10);
        Label name = Text(ManeuverCatalog.AbilityName(ability).ToUpper(), FontBody, TextBright, 2);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.AddChild(name);
        if (mastered)
            title.AddChild(Tag("MASTERED", ChipRole.Instinct));
        words.AddChild(title);
        words.AddChild(Text(ManeuverCatalog.Blurb(ability), FontCaption, Body, 0, wrap: true));
        string mastery = Masteries.Describe(ability, pilot.Ship);
        if (mastery.Length > 0)
            words.AddChild(Text(mastered ? mastery : $"Mastery: {mastery}", FontMicro, mastered ? Instinct : Muted, 0, wrap: true));
        row.AddChild(words);
        return row;
    }

    static string Signed(float value, string unit = "") =>
        $"{(value > 0 ? "+" : "−")}{Mathf.Abs(value):0}{unit}";

    static string Percent(float value) => $"{value * 100f:0}%";
}
