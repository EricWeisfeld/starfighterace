using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

public partial class RunScreen
{
    // ---------------------------------------------------------- squadron

    /// <summary>Every pilot's full record: hull, maneuvers, upgrades and traits, then the fallen.</summary>
    Control BuildSquadronPage()
    {
        VBoxContainer page = Stack(16);
        page.AddChild(Header($"{Run.Living.Count()} OF {RunState.RosterLimit} PILOTS", "SQUADRON", () =>
        {
            _showSquadron = false;
            Render();
        }, SalvageBadge()));

        (TouchScroll scroll, VBoxContainer content) = ScrollBody();
        page.AddChild(scroll);
        foreach (Pilot pilot in Run.Living)
            content.AddChild(PilotRecord(pilot));
        if (Run.Fallen.Any())
        {
            content.AddChild(Text("FALLEN", FontCaption, Muted, 4));
            foreach (Pilot pilot in Run.Fallen)
                content.AddChild(Text($"{pilot.Callsign} · LV {pilot.Level} {pilot.Ship.DisplayName.ToUpper()} · {Plural(pilot.Kills, "KILL")}", FontCaption, Negative, 1));
        }
        return page;
    }

    static Control PilotRecord(Pilot pilot)
    {
        PanelContainer card = Card();
        VBoxContainer stack = Stack(12);
        card.AddChild(stack);
        stack.AddChild(PilotSummary(pilot));

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

        var tags = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        tags.AddThemeConstantOverride("h_separation", 8);
        tags.AddThemeConstantOverride("v_separation", 8);
        foreach (ShipAbility ability in pilot.Maneuvers)
            tags.AddChild(Tag(ManeuverCatalog.AbilityName(ability).ToUpper(), ChipRole.Dormant));
        foreach (ShipUpgrade upgrade in pilot.Upgrades)
            tags.AddChild(Tag(ShipUpgrades.Get(upgrade).Name.ToUpper(), ChipRole.Gain));
        foreach (Perk perk in pilot.Perks)
            tags.AddChild(Tag(perk.Name.ToUpper(), perk.Positive ? ChipRole.Gain : ChipRole.Impaired));
        if (tags.GetChildCount() > 0)
            stack.AddChild(tags);
        foreach (Perk perk in pilot.Perks)
            stack.AddChild(Text($"{perk.Name}: {perk.Description}", FontMicro, perk.Positive ? Muted : Warning, 0, wrap: true));
        if (pilot.CanRefit)
            stack.AddChild(Text("Eligible for a refit frame at the next promotion.", FontMicro, Accent, 0, wrap: true));
        return card;
    }

    // ---------------------------------------------------------- briefing

    /// <summary>The battle at the active stop: objective, enemy wing, reward, and who flies.</summary>
    Control BuildBriefingPage()
    {
        RunNode node = Run.ActiveNode;
        BattleMission mission = Run.ActiveMission;
        RunNodeKind kind = node.BattleKind ?? RunNodeKind.Skirmish;
        List<Pilot> deployable = Run.Deployable();
        if (!_briefingPicksInitialized)
        {
            _briefingPicksInitialized = true;
            _briefingPicks.Clear();
            foreach (Pilot pilot in deployable.OrderByDescending(p => p.Hull / (float)p.MaxHull).ThenByDescending(p => p.Level).Take(RunState.SquadLimit))
                _briefingPicks.Add(pilot.Callsign);
        }

        VBoxContainer page = Stack(16);
        page.AddChild(Header(RunContent.KindName(kind), mission.Name, trailing: SalvageBadge()));
        (TouchScroll scroll, VBoxContainer content) = ScrollBody();
        page.AddChild(scroll);

        PanelContainer brief = Card();
        VBoxContainer briefStack = Stack(10);
        brief.AddChild(briefStack);
        if (node.Kind == RunNodeKind.Event && Run.EventResult != null)
            briefStack.AddChild(Text(Run.EventResult, FontCaption, Warning, 0, wrap: true));
        briefStack.AddChild(Text(mission.ObjectiveLabel, FontBody, TextBright, 2));
        briefStack.AddChild(Text(mission.Briefing, FontCaption, Body, 0, wrap: true));
        BattleMapDefinition map = BattleMaps.ById(mission.MapId);
        briefStack.AddChild(Text($"{map.DisplayName} · {map.Briefing}", FontCaption, Muted, 0, wrap: true));
        int reward = RunContent.SalvageReward(kind, Run.Sector) + node.BonusSalvage;
        briefStack.AddChild(Text($"THREAT {mission.Threat} · REWARD +{reward} SALVAGE", FontCaption, Warning, 2));
        content.AddChild(brief);

        content.AddChild(Text($"ENEMY WING · {mission.EnemySquad.Length} SHIPS", FontCaption, Muted, 4));
        var wing = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        wing.AddThemeConstantOverride("h_separation", 12);
        foreach (ShipType enemy in mission.EnemySquad)
        {
            VBoxContainer box = Stack(0);
            box.AddChild(ShipIcon(enemy, 72f, team: 1));
            Label name = Text(enemy.DisplayName.ToUpper(), FontMicro, Muted, 1);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            box.AddChild(name);
            wing.AddChild(box);
        }
        content.AddChild(wing);

        bool flyingWounded = !Run.Ready.Any();
        content.AddChild(Text($"CHOOSE UP TO {RunState.SquadLimit} PILOTS · {_briefingPicks.Count} SELECTED", FontCaption, Muted, 4));
        if (flyingWounded)
            content.AddChild(Text("Nobody is fit to fly, so the wounded are going up anyway.", FontCaption, Warning, 0, wrap: true));
        foreach (Pilot pilot in deployable)
        {
            bool picked = _briefingPicks.Contains(pilot.Callsign);
            var card = new TapCard();
            card.Tapped += () =>
            {
                if (!_briefingPicks.Remove(pilot.Callsign) && _briefingPicks.Count < RunState.SquadLimit)
                    _briefingPicks.Add(pilot.Callsign);
                Render();
            };
            card.AddChild(PilotSummary(pilot, picked ? "FLYING" : "STANDING BY"));
            if (picked)
                card.AddThemeStyleboxOverride("panel", Box(new Color(Accent, 0.12f), Accent, 3, 16, 12));
            content.AddChild(card);
        }
        foreach (Pilot pilot in Run.Living.Except(deployable))
            content.AddChild(Text($"{pilot.Callsign} is wounded and sits this one out.", FontCaption, Muted, 0, wrap: true));

        Button launch = TouchButton("LAUNCH", primary: true);
        launch.Disabled = _briefingPicks.Count == 0;
        launch.Pressed += () =>
        {
            List<Pilot> squad = deployable.Where(p => _briefingPicks.Contains(p.Callsign)).ToList();
            Run.LaunchBattle(squad);
            Go("res://Scenes/Battle.tscn");
        };
        page.AddChild(launch);
        return page;
    }

    // --------------------------------------------------------- promotion

    /// <summary>One waiting promotion: pick a card, then confirm.</summary>
    Control BuildPromotionPage()
    {
        PendingPromotion promotion = Run.Promotions[0];
        Pilot pilot = Run.Pilots.First(p => p.Callsign == promotion.Callsign);
        VBoxContainer page = Stack(16);
        string eyebrow = Run.Promotions.Count > 1 ? $"PROMOTION · {Run.Promotions.Count} WAITING" : "PROMOTION";
        page.AddChild(Header(eyebrow, $"{pilot.Callsign} {promotion.Reason}"));
        page.AddChild(PilotSummary(pilot));
        page.AddChild(Text("CHOOSE ONE", FontCaption, Muted, 4));

        (TouchScroll scroll, VBoxContainer content) = ScrollBody(14);
        page.AddChild(scroll);
        for (int i = 0; i < promotion.Cards.Count; i++)
        {
            int index = i;
            PromotionCard card = promotion.Cards[i];
            (string title, string category, string text) = RunContent.DescribeCard(card, pilot);
            var tap = new TapCard();
            tap.Tapped += () =>
            {
                _selectedCard = index;
                Render();
            };
            if (_selectedCard == index)
                tap.AddThemeStyleboxOverride("panel", Box(new Color(Accent, 0.14f), Accent, 3, 16, 12));
            HBoxContainer row = Row(16);
            row.AddChild(CardIcon(card));
            VBoxContainer words = Stack(4);
            words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            words.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            words.AddChild(Text(category, FontMicro, CardColor(card.Kind), 3));
            words.AddChild(Text(title, FontBody, TextBright, 2));
            words.AddChild(Text(text, FontCaption, Body, 0, wrap: true));
            row.AddChild(words);
            tap.AddChild(row);
            content.AddChild(tap);
        }

        Button take = TouchButton(_selectedCard == null ? "TAP A CARD" : "TAKE IT", primary: true);
        take.Disabled = _selectedCard == null;
        take.Pressed += () =>
        {
            Run.ChoosePromotion(_selectedCard ?? 0);
            _selectedCard = null;
            Render();
        };
        page.AddChild(take);
        return page;
    }

    static Color CardColor(CardKind kind) => kind switch
    {
        CardKind.Maneuver => Accent,
        CardKind.Upgrade => Positive,
        CardKind.Frame => Warning,
        _ => new Color(0.75f, 0.6f, 1f),
    };

    static Control CardIcon(PromotionCard card)
    {
        var size = new Vector2(88, 88);
        switch (card.Kind)
        {
            case CardKind.Maneuver when Enum.TryParse(card.Id, out ShipAbility ability):
                ManeuverInfo info = ManeuverCatalog.All.FirstOrDefault(m => m.Ability == ability);
                if (info != null)
                    return new ManeuverGlyph { Action = info.Action, Tint = info.Color, CustomMinimumSize = size, MouseFilter = Control.MouseFilterEnum.Ignore };
                break;
            case CardKind.Frame:
                return ShipIcon(ShipTypes.FromId(card.Id), 88f);
        }
        var badge = new CardBadge { Kind = card.Kind, CustomMinimumSize = size };
        return badge;
    }

    // ------------------------------------------------------- repair dock

    Control BuildDockPage()
    {
        VBoxContainer page = Stack(16);
        page.AddChild(Header("SPEND SALVAGE", "REPAIR DOCK", trailing: SalvageBadge()));
        page.AddChild(Text($"Repairs cost {RunState.RepairCostPerHull} salvage per hull point. Treating a wound costs {RunState.TreatWoundCost}.",
            FontCaption, Muted, 0, wrap: true));
        (TouchScroll scroll, VBoxContainer content) = ScrollBody();
        page.AddChild(scroll);
        foreach (Pilot pilot in Run.Living)
        {
            PanelContainer card = Card();
            VBoxContainer stack = Stack(12);
            card.AddChild(stack);
            stack.AddChild(PilotSummary(pilot));
            HBoxContainer actions = Row(12);
            int cost = Run.RepairCost(pilot);
            if (cost > 0)
            {
                Button repair = TouchButton($"REPAIR · {cost}", fontSize: FontCaption);
                repair.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                repair.Disabled = cost > Run.Salvage;
                repair.Pressed += () =>
                {
                    Run.Repair(pilot);
                    Render();
                };
                actions.AddChild(repair);
            }
            if (pilot.IsWounded)
            {
                Button treat = TouchButton($"TREAT WOUND · {RunState.TreatWoundCost}", fontSize: FontCaption);
                treat.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                treat.Disabled = RunState.TreatWoundCost > Run.Salvage;
                treat.Pressed += () =>
                {
                    Run.TreatWound(pilot);
                    Render();
                };
                actions.AddChild(treat);
            }
            if (actions.GetChildCount() == 0)
                actions.AddChild(Text("Ready to fly. Nothing to fix.", FontCaption, Positive, 0));
            stack.AddChild(actions);
            content.AddChild(card);
        }
        Button leave = TouchButton("LEAVE DOCK", primary: true);
        leave.Pressed += () =>
        {
            Run.CompleteActiveNode();
            Render();
        };
        page.AddChild(leave);
        return page;
    }

    // ----------------------------------------------------------- recruit

    Control BuildRecruitPage()
    {
        int living = Run.Living.Count();
        VBoxContainer page = Stack(16);
        page.AddChild(Header($"ROSTER {living}/{RunState.RosterLimit}", "RECRUIT", trailing: SalvageBadge()));
        (TouchScroll scroll, VBoxContainer content) = ScrollBody();
        page.AddChild(scroll);
        if (living >= RunState.RosterLimit)
            content.AddChild(Text("The squadron is full.", FontCaption, Warning, 0, wrap: true));
        if (Run.RecruitOffers.Count == 0)
            content.AddChild(Text("Nobody else is looking for work here.", FontCaption, Muted, 0, wrap: true));
        foreach (RecruitOffer offer in Run.RecruitOffers.ToList())
        {
            Pilot recruit = offer.Pilot.ToPilot();
            PanelContainer card = Card();
            VBoxContainer stack = Stack(12);
            card.AddChild(stack);
            stack.AddChild(PilotSummary(recruit));
            var tags = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            tags.AddThemeConstantOverride("h_separation", 8);
            tags.AddThemeConstantOverride("v_separation", 8);
            foreach (ShipAbility ability in recruit.Maneuvers)
                tags.AddChild(Tag(ManeuverCatalog.AbilityName(ability).ToUpper(), ChipRole.Dormant));
            foreach (ShipUpgrade upgrade in recruit.Upgrades)
                tags.AddChild(Tag(ShipUpgrades.Get(upgrade).Name.ToUpper(), ChipRole.Gain));
            foreach (Perk perk in recruit.Perks)
                tags.AddChild(Tag(perk.Name.ToUpper(), perk.Positive ? ChipRole.Gain : ChipRole.Impaired));
            stack.AddChild(tags);
            Button hire = TouchButton($"HIRE · {offer.Cost} SALVAGE", fontSize: FontCaption);
            hire.Disabled = offer.Cost > Run.Salvage || living >= RunState.RosterLimit;
            hire.Pressed += () =>
            {
                Run.Hire(offer);
                Render();
            };
            stack.AddChild(hire);
            content.AddChild(card);
        }
        Button leave = TouchButton("MOVE ON", primary: true);
        leave.Pressed += () =>
        {
            Run.CompleteActiveNode();
            Render();
        };
        page.AddChild(leave);
        return page;
    }

    // ------------------------------------------------------------- event

    Control BuildEventPage()
    {
        RunEvent runEvent = Run.ActiveEvent;
        RunNode node = Run.ActiveNode;
        VBoxContainer page = Stack(20);
        page.AddChild(Header("SIGNAL", runEvent.Title, trailing: SalvageBadge()));
        page.AddChild(Text(runEvent.Text, FontBody, Body, 0, wrap: true));
        page.AddChild(Spacer());

        if (Run.EventResult == null)
        {
            for (int i = 0; i < runEvent.Options.Length; i++)
            {
                int index = i;
                RunEventOption option = runEvent.Options[i];
                bool available = option.Available(Run);
                Button choose = TouchButton(option.Label, primary: i == 0);
                choose.Disabled = !available;
                choose.Pressed += () =>
                {
                    Run.ChooseEventOption(index);
                    Render();
                };
                page.AddChild(choose);
                page.AddChild(Text(available ? option.Detail : "Not possible right now.", FontCaption, Muted, 0, wrap: true));
            }
            return page;
        }

        PanelContainer result = Card(24, 20, new Color(Accent, 0.6f));
        result.AddChild(Text(Run.EventResult, FontBody, TextBright, 0, wrap: true));
        page.AddChild(result);
        if (node.EventBattle != null)
        {
            Button fight = TouchButton("TO BATTLE", primary: true);
            fight.Pressed += () =>
            {
                _eventBattleAcknowledged = true;
                _briefingPicksInitialized = false;
                Render();
            };
            page.AddChild(fight);
        }
        else
        {
            Button next = TouchButton("CONTINUE", primary: true);
            next.Pressed += () =>
            {
                Run.CompleteActiveNode();
                Render();
            };
            page.AddChild(next);
        }
        return page;
    }

    // --------------------------------------------------------- run end

    Control BuildEndPage()
    {
        bool won = Run.Outcome == RunOutcome.Victory;
        VBoxContainer page = Stack(16);
        page.AddChild(Text(won ? "RUN COMPLETE" : "RUN OVER", FontCaption, Muted, 4));
        page.AddChild(Text(won ? "VICTORY" : "DEFEAT", FontDisplay, won ? Positive : Negative, 6));
        int sectorsCleared = won ? RunContent.SectorCount : Run.Sector - 1;

        var grid = new GridContainer { Columns = 3, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 12);
        AddEndStat(grid, $"{sectorsCleared}/{RunContent.SectorCount}", "SECTORS");
        AddEndStat(grid, Run.BattlesWon.ToString(), "BATTLES WON");
        AddEndStat(grid, Run.TotalKills.ToString(), "KILLS");
        page.AddChild(grid);

        (TouchScroll scroll, VBoxContainer content) = ScrollBody();
        page.AddChild(scroll);
        if (Run.Living.Any())
        {
            content.AddChild(Text("CAME HOME", FontCaption, Muted, 4));
            foreach (Pilot pilot in Run.Living)
                content.AddChild(Text($"{pilot.Callsign} · LV {pilot.Level} {pilot.Ship.DisplayName.ToUpper()} · {Plural(pilot.Kills, "KILL")}", FontCaption, Body, 1));
        }
        if (Run.Fallen.Any())
        {
            content.AddChild(Text("FALLEN", FontCaption, Muted, 4));
            foreach (Pilot pilot in Run.Fallen)
                content.AddChild(Text($"{pilot.Callsign} · LV {pilot.Level} {pilot.Ship.DisplayName.ToUpper()} · {Plural(pilot.Kills, "KILL")}", FontCaption, Negative, 1));
        }

        Button again = TouchButton("NEW RUN", primary: true);
        again.Pressed += () =>
        {
            RunState.EndAndDelete();
            RunState.StartNew();
            Render();
        };
        page.AddChild(again);
        Button home = TouchButton("HOME");
        home.Pressed += () =>
        {
            RunState.EndAndDelete();
            Go("res://Scenes/HomeScreen.tscn");
        };
        page.AddChild(home);
        return page;
    }

    static void AddEndStat(Container parent, string value, string key)
    {
        VBoxContainer box = Stack(0);
        box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        box.AddChild(Text(value, FontHeading, TextBright));
        box.AddChild(Text(key, FontMicro, Muted, 3));
        parent.AddChild(box);
    }
}

/// <summary>Round badge for upgrade and trait cards, which have no ship or path to show.</summary>
public partial class CardBadge : Control
{
    public CardKind Kind;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        Vector2 c = Size / 2f;
        float r = Mathf.Min(Size.X, Size.Y) * 0.38f;
        Color color = Kind == CardKind.Upgrade ? SignalUi.Positive : new Color(0.75f, 0.6f, 1f);
        DrawCircle(c, r, new Color(color, 0.12f));
        DrawArc(c, r, 0f, Mathf.Tau, 36, color, 3f, true);
        if (Kind == CardKind.Upgrade)
        {
            // An upward chevron: an improvement to the ship.
            DrawPolyline(new[] { c + new Vector2(-r * 0.5f, r * 0.2f), c + new Vector2(0, -r * 0.35f), c + new Vector2(r * 0.5f, r * 0.2f) }, color, 4f, true);
            DrawPolyline(new[] { c + new Vector2(-r * 0.5f, r * 0.55f), c + new Vector2(0, 0), c + new Vector2(r * 0.5f, r * 0.55f) }, color, 4f, true);
        }
        else
        {
            // A four-point star: something the pilot has learned.
            var star = new Vector2[9];
            for (int i = 0; i < 8; i++)
                star[i] = c + Vector2.Up.Rotated(i * Mathf.Pi / 4f) * (i % 2 == 0 ? r * 0.7f : r * 0.22f);
            star[8] = star[0];
            DrawPolyline(star, color, 3f, true);
        }
    }
}
