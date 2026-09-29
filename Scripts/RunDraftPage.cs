using Godot;
using System.Linq;
using static SignalUi;

public partial class RunScreen
{
    /// <summary>The draft slot whose ship is being chosen, or null on the draft page itself.</summary>
    int? _shipPickerSlot;

    /// <summary>
    /// The start of a run: each pilot gets a ship (any of the nine frames) and
    /// one of three instincts offered to them. Both stay with them for the run.
    /// </summary>
    Control BuildDraftPage()
    {
        VBoxContainer page = Stack(16);
        page.AddChild(Header("NEW RUN", "YOUR SQUADRON"));
        page.AddChild(Text("Pick a ship and an instinct for each pilot. Both stay with them for the whole run.",
            FontCaption, Body, 0, wrap: true));
        (TouchScroll scroll, VBoxContainer content) = ScrollBody(12);
        page.AddChild(scroll);
        for (int i = 0; i < Run.Draft.Count; i++)
            AddDraftPilot(content, i);

        Button start = TouchButton(Run.DraftReady ? "START RUN" : "PICK AN INSTINCT FOR EACH PILOT", primary: true);
        start.Disabled = !Run.DraftReady;
        start.Pressed += () =>
        {
            Run.FinishDraft();
            Render();
        };
        page.AddChild(start);
        return page;
    }

    void AddDraftPilot(Container content, int slot)
    {
        DraftPilot draft = Run.Draft[slot];
        ShipType frame = draft.Frame;
        content.AddChild(Text($"PILOT {slot + 1} · {draft.Callsign}", FontCaption, Muted, 4));

        // The ship: tap to choose another.
        var ship = new TapCard();
        ship.Tapped += () =>
        {
            _shipPickerSlot = slot;
            Render();
        };
        HBoxContainer row = Row(16);
        row.AddChild(ShipIcon(frame, 88f));
        VBoxContainer words = Stack(4);
        words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        words.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        HBoxContainer title = Row(12);
        Label name = Text(frame.DisplayName.ToUpper(), FontBody, TextBright, 2);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.AddChild(name);
        title.AddChild(Text("CHANGE", FontMicro, Accent, 3));
        words.AddChild(title);
        words.AddChild(Text(ShipStatLine(frame), FontMicro, Body, 1, wrap: true));
        words.AddChild(Text(SlotLine(frame), FontMicro, Positive, 2, wrap: true));
        row.AddChild(words);
        ship.AddChild(row);
        content.AddChild(ship);

        foreach (string id in draft.InstinctOffers)
        {
            Perk perk = Perks.ById(id);
            if (perk == null)
                continue;
            var instinct = new TapCard { Selected = draft.Instinct == id };
            instinct.Tapped += () =>
            {
                Run.SetDraftInstinct(slot, id);
                Render();
            };
            VBoxContainer text = Stack(2);
            text.AddChild(Text(perk.Name.ToUpper(), FontCaption, Instinct, 2));
            text.AddChild(Text(perk.Description, FontMicro, Body, 0, wrap: true));
            instinct.AddChild(text);
            content.AddChild(instinct);
        }
    }

    /// <summary>Every frame, grouped by class line, with the maneuvers each line learns.</summary>
    Control BuildShipPickerPage(int slot)
    {
        DraftPilot draft = Run.Draft[slot];
        VBoxContainer page = Stack(16);
        page.AddChild(Header("CHOOSE A SHIP", draft.Callsign, () =>
        {
            _shipPickerSlot = null;
            Render();
        }));
        (TouchScroll scroll, VBoxContainer content) = ScrollBody(12);
        page.AddChild(scroll);
        foreach (var line in ShipTypes.PlayerFrames.GroupBy(frame => ShipTypes.ClassIdForHull(frame.Id)))
        {
            ShipAbility[] pool = line.First().ManeuverPool;
            content.AddChild(Text($"{ShipTypes.ClassName(line.Key).ToUpper()} LINE", FontCaption, Muted, 4));
            content.AddChild(Text($"Starts with {ManeuverCatalog.AbilityName(pool[0])}. Learns " +
                string.Join(", ", pool.Skip(1).Select(ManeuverCatalog.AbilityName)) + ".", FontMicro, Muted, 0, wrap: true));
            foreach (ShipType frame in line)
                content.AddChild(ShipOption(slot, frame, frame == draft.Frame));
        }
        return page;
    }

    Control ShipOption(int slot, ShipType frame, bool selected)
    {
        var tap = new TapCard { Selected = selected };
        tap.Tapped += () =>
        {
            Run.SetDraftFrame(slot, frame);
            _shipPickerSlot = null;
            Render();
        };
        HBoxContainer row = Row(16);
        row.AddChild(ShipIcon(frame, 96f));
        VBoxContainer words = Stack(4);
        words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        words.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        words.AddChild(Text(frame.DisplayName.ToUpper(), FontBody, TextBright, 2));
        words.AddChild(Text(frame.Description, FontCaption, Body, 0, wrap: true));
        words.AddChild(Text(ShipStatLine(frame), FontMicro, Body, 1, wrap: true));
        words.AddChild(Text(SlotLine(frame), FontMicro, Positive, 2, wrap: true));
        row.AddChild(words);
        tap.AddChild(row);
        return tap;
    }

    // Non-breaking spaces keep each stat's name and number on one line.
    static string ShipStatLine(ShipType frame) =>
        $"HULL\u00A0{frame.MaxHp} · SHIELD\u00A0{frame.MaxShield} · DMG\u00A0{frame.ShotDamage} · ACC\u00A0{frame.Accuracy * 100:0}% · " +
        $"EVA\u00A0{frame.Evasion * 100:0}% · TURN\u00A0{frame.NormalTurnLimitDegrees:0}° · SPEED\u00A0{frame.NormalMoveMaxDistance:0}";

    static string SlotLine(ShipType frame) =>
        $"{frame.UpgradeSlots.Length}\u00A0SLOTS · " + string.Join(" · ", frame.UpgradeSlots.Select(ShipUpgrades.SlotName));
}
