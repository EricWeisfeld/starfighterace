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

        stack.AddChild(Text("SHIP", FontMicro, Muted, 3));
        stack.AddChild(ShipLoadout(pilot));
        stack.AddChild(Text("PILOT", FontMicro, Muted, 3));
        stack.AddChild(PilotSkills(pilot));
        foreach (Perk perk in pilot.Perks)
            stack.AddChild(Text($"{perk.Name}: {perk.Description}", FontMicro, perk.Positive ? Muted : Warning, 0, wrap: true));
        if (pilot.CanRefit)
            stack.AddChild(Text("Ready for a refit into a new frame at any repair dock.", FontMicro, Accent, 0, wrap: true));
        return card;
    }

    static HFlowContainer TagFlow()
    {
        var flow = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 8);
        return flow;
    }

    /// <summary>The ship's slots: the fitted module in each, or EMPTY.</summary>
    static Control ShipLoadout(Pilot pilot)
    {
        HFlowContainer flow = TagFlow();
        foreach (ShipUpgradeSlot slot in pilot.Ship.UpgradeSlots)
        {
            string slotName = ShipUpgrades.SlotName(slot);
            flow.AddChild(pilot.ModuleIn(slot) is ShipUpgrade module
                ? Tag($"{slotName} · {ShipUpgrades.Get(module).Name.ToUpper()}", ChipRole.Gain)
                : Tag($"{slotName} · EMPTY", ChipRole.Dormant));
        }
        return flow;
    }

    /// <summary>What the pilot has learned: maneuvers (mastered ones marked), instincts, then scars.</summary>
    static Control PilotSkills(Pilot pilot)
    {
        HFlowContainer flow = TagFlow();
        foreach (ShipAbility ability in pilot.Maneuvers)
        {
            string name = ManeuverCatalog.AbilityName(ability).ToUpper();
            flow.AddChild(pilot.HasMastered(ability)
                ? Tag($"{name} · MASTERED", ChipRole.Instinct)
                : Tag(name, ChipRole.Dormant));
        }
        foreach (Perk perk in pilot.Instincts)
            flow.AddChild(Tag(perk.Name.ToUpper(), ChipRole.Instinct));
        foreach (Perk perk in pilot.Scars)
            flow.AddChild(Tag($"SCAR · {perk.Name.ToUpper()}", ChipRole.Impaired));
        return flow;
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
            VBoxContainer pick = Stack(8);
            pick.AddChild(PilotSummary(pilot, picked ? "FLYING" : "STANDING BY"));
            if (pilot.Instincts.Any())
                pick.AddChild(Text(string.Join(" · ", pilot.Instincts.Select(p => p.Name.ToUpper())), FontMicro, Instinct, 1, wrap: true));
            if (pilot.Scars.Any())
                pick.AddChild(Text("SCARS · " + string.Join(" · ", pilot.Scars.Select(p => p.Name.ToUpper())), FontMicro, Warning, 1, wrap: true));
            card.AddChild(pick);
            card.Selected = picked;
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

    /// <summary>
    /// One waiting choice: a pilot's level-up (pilot growth only) or an
    /// elite wing's module crate (a free module for one ship). Pick a card,
    /// then confirm.
    /// </summary>
    Control BuildPromotionPage()
    {
        PendingPromotion promotion = Run.Promotions[0];
        VBoxContainer page = Stack(16);
        string waiting = Run.Promotions.Count > 1 ? $" · {Run.Promotions.Count} WAITING" : "";
        if (promotion.IsCrate)
        {
            page.AddChild(Header($"ELITE SALVAGE{waiting}", "MODULE CRATE"));
            page.AddChild(Text("Pick one module. It's fitted free to the ship named on the card.", FontCaption, Body, 0, wrap: true));
        }
        else
        {
            Pilot pilot = Run.Pilots.First(p => p.Callsign == promotion.Callsign);
            page.AddChild(Header($"PROMOTION{waiting}", $"{pilot.Callsign} {promotion.Reason}"));
            page.AddChild(PilotSummary(pilot));
            page.AddChild(Text("PILOTS LEARN BY FLYING. CHOOSE ONE", FontCaption, Muted, 4));
        }

        (TouchScroll scroll, VBoxContainer content) = ScrollBody(14);
        page.AddChild(scroll);
        for (int i = 0; i < promotion.Cards.Count; i++)
        {
            int index = i;
            PromotionCard card = promotion.Cards[i];
            Pilot target = Run.PilotFor(promotion, card);
            if (target == null)
                continue;
            (string title, string category, string text) = RunContent.DescribeCard(card, target);
            var tap = new TapCard { Selected = _selectedCard == index };
            tap.Tapped += () =>
            {
                _selectedCard = index;
                Render();
            };
            HBoxContainer row = Row(16);
            row.AddChild(CardIcon(card, target));
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
        CardKind.Module => Positive,
        _ => Instinct,
    };

    static Control CardIcon(PromotionCard card, Pilot pilot)
    {
        var size = new Vector2(88, 88);
        if (card.Kind is CardKind.Maneuver or CardKind.Mastery && Enum.TryParse(card.Id, out ShipAbility ability))
        {
            ManeuverInfo info = ManeuverCatalog.All.FirstOrDefault(m => m.Ability == ability);
            if (info != null)
                return new ManeuverGlyph
                {
                    Action = info.Action,
                    Tint = card.Kind == CardKind.Mastery ? Instinct : info.Color,
                    CustomMinimumSize = size,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
        }
        if (card.Kind == CardKind.Module)
        {
            // A crate card shows whose ship the module goes on.
            VBoxContainer box = Stack(0);
            box.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            box.AddChild(ShipIcon(pilot.Ship, 88f));
            return box;
        }
        return new CardBadge { Kind = card.Kind, CustomMinimumSize = size };
    }

    // ------------------------------------------------------- repair dock

    ShipUpgrade? _selectedModule;
    string _refitCallsign;

    /// <summary>
    /// The run's shop. Modules on sale sit at the top: tap one, then fit it to
    /// a ship below. Each ship card also offers repairs, wound and scar
    /// treatment, and a refit once its pilot is ready.
    /// </summary>
    Control BuildDockPage()
    {
        if (_selectedModule is ShipUpgrade chosen && !Run.DockStock.Contains(chosen))
            _selectedModule = null;

        VBoxContainer page = Stack(16);
        page.AddChild(Header("SPEND SALVAGE", "REPAIR DOCK", trailing: SalvageBadge()));
        (TouchScroll scroll, VBoxContainer content) = ScrollBody();
        page.AddChild(scroll);

        content.AddChild(Text(Run.DockStock.Count == 0 ? "MODULES · SOLD OUT" : "MODULES FOR SALE · TAP ONE TO FIT IT",
            FontCaption, Muted, 4));
        foreach (ShipUpgrade module in Run.DockStock)
        {
            content.AddChild(StockCard(module));
            if (_selectedModule == module)
                content.AddChild(FitPicker(module));
        }

        content.AddChild(Text("SQUADRON", FontCaption, Muted, 4));
        foreach (Pilot pilot in Run.Living)
            content.AddChild(DockPilotCard(pilot));

        Button leave = TouchButton("LEAVE DOCK", primary: true);
        leave.Pressed += () =>
        {
            _selectedModule = null;
            Run.CompleteActiveNode();
            Render();
        };
        page.AddChild(leave);
        return page;
    }

    Control StockCard(ShipUpgrade module)
    {
        ShipUpgradeDefinition definition = ShipUpgrades.Get(module);
        bool affordable = definition.Cost <= Run.Salvage;
        bool fitsSomeone = Run.Living.Any(p => p.CanInstall(module));
        var tap = new TapCard { Selected = _selectedModule == module, Disabled = !affordable || !fitsSomeone };
        tap.Tapped += () =>
        {
            _selectedModule = _selectedModule == module ? null : module;
            Render();
        };
        HBoxContainer row = Row(16);
        row.AddChild(new CardBadge { Kind = CardKind.Module, CustomMinimumSize = new Vector2(72, 72) });
        VBoxContainer words = Stack(2);
        words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        words.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        words.AddChild(Text($"{ShipUpgrades.SlotName(definition.Slot)} MODULE", FontMicro, Positive, 3));
        words.AddChild(Text(definition.Name.ToUpper(), FontBody, TextBright, 2));
        words.AddChild(Text(definition.Description, FontCaption, Body, 0, wrap: true));
        if (!fitsSomeone)
            words.AddChild(Text(Run.Living.Any(p => p.HasSlot(definition.Slot))
                    ? "Already fitted on every ship that could take it."
                    : $"No ship in the squadron has a {ShipUpgrades.SlotName(definition.Slot)} slot.",
                FontMicro, Muted, 0, wrap: true));
        row.AddChild(words);
        Label price = Text(definition.Cost.ToString(), FontTitle, affordable ? Warning : Dim, 1);
        price.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(price);
        tap.AddChild(row);
        return tap;
    }

    /// <summary>Right under the chosen module: one button per ship that can take it.</summary>
    Control FitPicker(ShipUpgrade module)
    {
        ShipUpgradeDefinition definition = ShipUpgrades.Get(module);
        PanelContainer panel = Card(20, 16, new Color(Accent, 0.6f));
        VBoxContainer stack = Stack(10);
        panel.AddChild(stack);
        stack.AddChild(Text($"FIT {definition.Name.ToUpper()} TO", FontMicro, Accent, 3));
        foreach (Pilot pilot in Run.Living.Where(p => p.CanInstall(module)))
        {
            string swap = pilot.ModuleIn(definition.Slot) is ShipUpgrade old ? $" · REPLACES {ShipUpgrades.Get(old).Name.ToUpper()}" : "";
            Button fit = TouchButton($"{pilot.Callsign}{swap} · {definition.Cost}", primary: true, fontSize: FontCaption);
            fit.Disabled = definition.Cost > Run.Salvage;
            fit.Pressed += () =>
            {
                Run.BuyModule(pilot, module);
                _selectedModule = null;
                Render();
            };
            stack.AddChild(fit);
        }
        return panel;
    }

    Control DockPilotCard(Pilot pilot)
    {
        PanelContainer card = Card();
        VBoxContainer stack = Stack(12);
        card.AddChild(stack);
        stack.AddChild(PilotSummary(pilot));
        stack.AddChild(ShipLoadout(pilot));

        var actions = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        actions.AddThemeConstantOverride("h_separation", 12);
        actions.AddThemeConstantOverride("v_separation", 12);
        void AddAction(Button button)
        {
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            actions.AddChild(button);
        }
        int repairCost = Run.RepairCost(pilot);
        if (repairCost > 0)
        {
            Button repair = TouchButton($"REPAIR · {repairCost}", fontSize: FontCaption);
            repair.Disabled = repairCost > Run.Salvage;
            repair.Pressed += () =>
            {
                Run.Repair(pilot);
                Render();
            };
            AddAction(repair);
        }
        if (pilot.IsWounded)
        {
            Button treat = TouchButton($"TREAT WOUND · {RunState.TreatWoundCost}", fontSize: FontCaption);
            treat.Disabled = RunState.TreatWoundCost > Run.Salvage;
            treat.Pressed += () =>
            {
                Run.TreatWound(pilot);
                Render();
            };
            AddAction(treat);
        }
        if (pilot.CanRefit)
        {
            Button refit = TouchButton($"REFIT · {RunState.RefitCost}", fontSize: FontCaption);
            refit.Disabled = RunState.RefitCost > Run.Salvage;
            refit.Pressed += () =>
            {
                _refitCallsign = pilot.Callsign;
                _selectedModule = null;
                Render();
            };
            AddAction(refit);
        }
        if (actions.GetChildCount() > 0)
            stack.AddChild(actions);

        // Scar names can be long, so each treatment gets a full-width button.
        foreach (Perk scar in pilot.Scars.ToList())
        {
            stack.AddChild(Text($"{scar.Name}: {scar.Description}", FontMicro, Warning, 0, wrap: true));
            Button cure = TouchButton($"TREAT {scar.Name.ToUpper()} · {RunState.TreatScarCost}", fontSize: FontCaption);
            cure.Disabled = RunState.TreatScarCost > Run.Salvage;
            cure.Pressed += () =>
            {
                Run.TreatScar(pilot, scar);
                Render();
            };
            stack.AddChild(cure);
        }

        if (actions.GetChildCount() == 0 && !pilot.Scars.Any())
            stack.AddChild(Text("Ready to fly. Nothing to fix.", FontCaption, Positive, 0));
        return card;
    }

    /// <summary>Choosing a refit frame for one pilot: both options side by side with what changes.</summary>
    Control BuildRefitPage()
    {
        Pilot pilot = Run.Living.FirstOrDefault(p => p.Callsign == _refitCallsign);
        if (pilot == null || !pilot.CanRefit)
        {
            _refitCallsign = null;
            return BuildDockPage();
        }
        VBoxContainer page = Stack(16);
        page.AddChild(Header($"REFIT · {RunState.RefitCost} SALVAGE", pilot.Callsign, () =>
        {
            _refitCallsign = null;
            Render();
        }, SalvageBadge()));
        page.AddChild(Text($"Choose {pilot.Callsign}'s new frame. Modules move across; the frame is for the rest of the run.",
            FontCaption, Body, 0, wrap: true));
        (TouchScroll scroll, VBoxContainer content) = ScrollBody();
        page.AddChild(scroll);
        foreach (ShipType frame in ShipTypes.HullBranches(pilot.ClassId))
        {
            PanelContainer card = Card();
            VBoxContainer stack = Stack(10);
            card.AddChild(stack);
            HBoxContainer header = Row(16);
            header.AddChild(ShipIcon(frame, 112f));
            VBoxContainer identity = Stack(4);
            identity.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            identity.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            identity.AddChild(Text(frame.DisplayName.ToUpper(), FontBody, TextBright, 2));
            identity.AddChild(Text(frame.Description, FontCaption, Body, 0, wrap: true));
            header.AddChild(identity);
            stack.AddChild(header);
            stack.AddChild(Text(RunContent.FrameDelta(pilot.Ship, frame), FontCaption, Accent, 0, wrap: true));
            stack.AddChild(Text("SLOTS · " + string.Join(" · ", frame.UpgradeSlots.Select(ShipUpgrades.SlotName)), FontCaption, Positive, 2));
            Button buy = TouchButton($"REFIT INTO {frame.DisplayName.ToUpper()} · {RunState.RefitCost}", primary: true, fontSize: FontCaption);
            buy.Disabled = RunState.RefitCost > Run.Salvage;
            buy.Pressed += () =>
            {
                Run.BuyRefit(pilot, frame);
                _refitCallsign = null;
                Render();
            };
            stack.AddChild(buy);
            content.AddChild(card);
        }
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
            stack.AddChild(PilotSkills(recruit));
            foreach (Perk perk in recruit.Instincts)
                stack.AddChild(Text($"{perk.Name}: {perk.Description}", FontMicro, Muted, 0, wrap: true));
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

/// <summary>Round badge for module, instinct and mastery cards, which have no path to show.</summary>
public partial class CardBadge : Control
{
    public CardKind Kind;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        Vector2 c = Size / 2f;
        float r = Mathf.Min(Size.X, Size.Y) * 0.38f;
        bool hardware = Kind == CardKind.Module;
        Color color = hardware ? SignalUi.Positive : SignalUi.Instinct;
        DrawCircle(c, r, new Color(color, 0.12f));
        DrawArc(c, r, 0f, Mathf.Tau, 36, color, 3f, true);
        if (hardware)
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
