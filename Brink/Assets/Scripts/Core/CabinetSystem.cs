using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>One selectable Directed-mode priority (GDD §7.2).</summary>
    public class DirectiveDef
    {
        public string id;
        public string label;
        public string description;

        /// <summary>
        /// How far along this instruction is, 0..1 — or **-1 when it has no
        /// finish line**.
        ///
        /// Reported from play: *"I can tell the military leader to prepare for
        /// war but I never know when we are ready."* A directive acted every
        /// month and said nothing, so the operator issued an order into silence
        /// and had to guess when to stop paying for it. Giving an instruction a
        /// measurable goal is what turns delegation into something you can
        /// actually manage.
        ///
        /// -1 is a real answer, not a gap. AUSTERITY and PRESSURE RIVALS are
        /// standing policies — they are a way of running a ministry, not a task
        /// that completes — and inventing a progress bar for them would promise
        /// an end that never comes.
        /// </summary>
        public System.Func<GameState, float> progress;

        /// <summary>What the briefing says the month this instruction is met.</summary>
        public string completion = "";
    }

    /// <summary>One Direct Control action the operator can execute personally.</summary>
    public class DirectActionDef
    {
        public string id;
        public string label;
        public string description;
        public int cpCost;
    }

    /// <summary>
    /// Cabinet behavior (GDD Phase 3, §7, §8): officials act autonomously or
    /// toward a directive each month; the player can take Direct Control at CP
    /// cost. Overrides damage trust, delegation slowly builds it (GDD §7.3).
    /// Deterministic per seed+month so long simulations are reproducible.
    /// </summary>
    public static class CabinetSystem
    {
        // ---------- catalogs ----------

        public static DirectiveDef[] GetDirectives(Pillar pillar)
        {
            switch (pillar)
            {
                case Pillar.Military:
                    return new[]
                    {
                        new DirectiveDef
                        {
                            id = "MIL_READINESS", label = "RAISE READINESS",
                            description = "Prioritize force readiness over budget.",
                            // Done when every branch is trained and manned to a
                            // level that would actually deploy.
                            progress = s => ReadinessProgress(s.PlayerCountry),
                            completion = "THE FORCE IS AT READINESS. Every branch is manned and trained "
                                       + "to deploy. Holding here costs treasury every month."
                        },
                        new DirectiveDef
                        {
                            id = "MIL_CONSERVE", label = "CONSERVE BUDGET",
                            description = "Slow military growth; return savings to treasury."
                            // No finish line: this is a way of running the ministry.
                        },
                        new DirectiveDef
                        {
                            id = MilitaryAdvice.PrepareForWar, label = "PREPARE FOR WAR",
                            description = "Order what we are short of. Expensive, and it takes years to arrive.",
                            // Done when nothing is short of establishment — the
                            // same measure the ordering itself uses, so the bar
                            // and the buying cannot disagree.
                            progress = s =>
                            {
                                AcquisitionSystem.WorstShortfall(s.PlayerCountry, out float ratio);
                                return ratio;
                            },
                            completion = "THE FORCE IS READY. Every branch is at establishment and nothing "
                                       + "further is on order. Continuing to prepare now buys nothing."
                        },
                        new DirectiveDef
                        {
                            id = MilitaryAdvice.DrawDown, label = "DRAW DOWN",
                            description = "Sell hulls and airframes back. Cheaper, and we will miss them."
                            // No finish line: how far to cut is the operator's call.
                        }
                    };
                case Pillar.Economy:
                    return new[]
                    {
                        new DirectiveDef { id = "ECO_GROWTH", label = "PURSUE GROWTH", description = "Expand the economy aggressively." },
                        new DirectiveDef { id = "ECO_AUSTERITY", label = "AUSTERITY", description = "Prioritize treasury surplus over expansion." }
                    };
                case Pillar.Intelligence:
                    return new[]
                    {
                        new DirectiveDef { id = "INT_COLLECTION", label = "EXPAND COLLECTION", description = "Grow foreign collection capability." },
                        new DirectiveDef
                        {
                            id = "INT_COUNTERINTEL", label = "COUNTERINTELLIGENCE",
                            description = "Harden the state against penetration.",
                            progress = s => s.PlayerCountry.counterIntel.counterIntelligence / 75f,
                            completion = "THE SERVICE IS HARDENED. Counterintelligence is at a level that "
                                       + "makes a foreign network expensive to run against us."
                        }
                    };
                case Pillar.Diplomacy:
                    return new[]
                    {
                        new DirectiveDef { id = "DIP_OUTREACH", label = "BROAD OUTREACH", description = "Improve standing with all states." },
                        new DirectiveDef { id = "DIP_PRESSURE", label = "PRESSURE RIVALS", description = "Coordinate pressure; slower goodwill." }
                    };
                default:
                    return new[]
                    {
                        new DirectiveDef
                        {
                            id = "GOV_APPROVAL", label = "PUBLIC APPROVAL",
                            description = "Prioritize popular support.",
                            progress = s => s.PlayerCountry.governmentApproval / 65f,
                            completion = "THE GOVERNMENT IS POPULAR. Approval is comfortable; the ministry "
                                       + "can be turned to something that is not."
                        },
                        new DirectiveDef
                        {
                            id = "GOV_STABILITY", label = "INTERNAL STABILITY",
                            description = "Prioritize order and institutions.",
                            progress = s => s.PlayerCountry.stability / 70f,
                            completion = "THE STATE IS STEADY. Stability is restored and the institutions "
                                       + "are holding on their own."
                        }
                    };
            }
        }

        public static DirectActionDef GetDirectAction(Pillar pillar)
        {
            switch (pillar)
            {
                case Pillar.Military:
                    return new DirectActionDef { id = "DA_MIL", label = "EMERGENCY READINESS DRIVE", description = "Personal push on readiness. Costs treasury.", cpCost = 2 };
                case Pillar.Economy:
                    return new DirectActionDef { id = "DA_ECO", label = "DIRECT STIMULUS ORDER", description = "Inject treasury funds into the economy.", cpCost = 2 };
                case Pillar.Intelligence:
                    return new DirectActionDef { id = "DA_INT", label = "SURGE COLLECTION", description = "Personally task all collection assets.", cpCost = 2 };
                case Pillar.Diplomacy:
                    return new DirectActionDef { id = "DA_DIP", label = "PERSONAL SUMMIT", description = "High-level engagement abroad.", cpCost = 2 };
                default:
                    return new DirectActionDef { id = "DA_GOV", label = "EXECUTIVE ADDRESS", description = "Speak directly to the nation.", cpCost = 1 };
            }
        }

        // ---------- player commands ----------

        /// <summary>
        /// Change an official's control mode. Entering Directed costs 1 Influence
        /// (initial directive included). Returns false when Influence is exhausted.
        /// </summary>
        public static bool SetMode(GameState state, Official official, ControlMode mode)
        {
            if (official.mode == mode) return true;

            // Control modes are the operator's authority over their *own*
            // government. Nothing currently hands this a foreign official — the
            // view iterates the player's cabinet — but now that fifteen other
            // cabinets exist, "no caller does that" is a weaker guarantee than a
            // check, and commanding another country's ministers would be the
            // most fundamental fog break available.
            if (state.PlayerCountry?.FindOfficial(official.office) != official)
            {
                GameLog.Warn("CABINET", "That official does not answer to this government.");
                return false;
            }

            // Taking personal command of a pillar is a constitutional act, not a
            // menu choice (GDD §3). Directing and advising are always ours.
            if (mode == ControlMode.DirectControl
                && !AuthoritySystem.ObtainAuthority(state, official.office))
                return false;

            bool spentInfluence = false;
            if (mode == ControlMode.Directed)
            {
                if (state.influence < 1)
                {
                    GameLog.Warn("CABINET", $"Insufficient Influence to direct {official.displayName}.");
                    return false;
                }
                state.influence--;
                spentInfluence = true;
                official.directiveId = GetDirectives(official.office)[0].id;
            }
            else
            {
                official.directiveId = "";
            }

            official.mode = mode;

            // Only the mode that costs something counts as initiative. Handing an
            // official back their autonomy is free, and crediting it let a player
            // toggle a mode back and forth to bank the whole initiative component
            // of the annual evaluation without spending anything (spec 07 §3).
            if (spentInfluence) ProgressionSystem.RecordInitiative(state);
            GameLog.Info("CABINET", $"{official.displayName} ({official.office}) set to {mode}.");
            return true;
        }

        /// <summary>Change the active directive of a Directed official. Costs 1 Influence.</summary>
        public static bool SetDirective(GameState state, Official official, string directiveId)
        {
            if (official.mode != ControlMode.Directed || official.directiveId == directiveId)
                return false;
            if (state.influence < 1)
            {
                GameLog.Warn("CABINET", $"Insufficient Influence to redirect {official.displayName}.");
                return false;
            }

            state.influence--;
            official.directiveId = directiveId;
            // A new instruction can complete on its own terms.
            official.directiveCompletionReported = false;
            ProgressionSystem.RecordInitiative(state);
            GameLog.Info("CABINET", $"{official.displayName} directed: {directiveId}.");
            return true;
        }

        /// <summary>
        /// Direct Control action (GDD §7.2): the operator acts personally, paying
        /// CP. Bypassing the official damages their trust; loyalty softens it.
        /// </summary>
        public static bool TryDirectAction(GameState state, TurnManager turns, Pillar pillar)
        {
            var action = GetDirectAction(pillar);
            if (!turns.SpendCommandPoints(action.cpCost, action.label))
                return false;

            var player = state.PlayerCountry;
            ApplyPillarEffect(state, player, pillar, directiveId: "", amount: 1.4f);
            if (pillar == Pillar.Military) player.resources.treasury -= 40f;
            if (pillar == Pillar.Economy) player.resources.treasury -= 60f;

            var official = state.FindOfficial(pillar);
            if (official != null)
            {
                float penalty = 2f * (1.5f - official.loyalty / 100f);
                official.trust = Clamp(official.trust - penalty);
            }

            state.AddNotification(NotificationClass.Priority, action.label,
                "Executed under direct operator authority.", state.playerCountryId);
            state.AddChronicle(ChronicleCategory.Political, state.playerCountryId,
                $"Direct intervention: {action.label}.");
            ProgressionSystem.RecordInitiative(state);
            // One bucket, not one per action label: repetition is measured per
            // *kind* of thing done, and direct control is the most repeatable
            // verb in the game.
            ProgressionSystem.AwardXP(state, 12, "Direct control");
            return true;
        }

        // ---------- monthly resolution ----------

        /// <summary>Officials form intentions and act (GDD §6 loop). Wired to ResolveMonth.</summary>
        /// <summary>
        /// Every government's officials do a month's work (GDD §7.2, §8).
        ///
        /// Runs for all sixteen states, not only the player's. A foreign
        /// government's capability used to grow from `AISystem.InvestInPillars`
        /// — a bespoke routine with no people behind it — which meant a rival's
        /// progress had no explanation, nothing to collect against, and nothing a
        /// coup could damage. The same five offices now run every state.
        ///
        /// Control modes remain the **player's** interface to their own
        /// government: a foreign cabinet is always Autonomous, because there is
        /// no operator standing outside it to direct or override anyone.
        /// </summary>
        public static void MonthlyAct(GameState state)
        {
            // A fresh report each month: this describes the month just resolved,
            // not an archive. The permanent record is the chronicle.
            state.cabinetReport.Clear();

            foreach (var country in state.countries)
                MonthlyActFor(state, country);
        }

        static void MonthlyActFor(GameState state, CountryState country)
        {
            if (country == null || country.cabinet.Count == 0) return;
            int monthIndex = state.date.MonthsSince(state.startDate);

            foreach (var official in country.cabinet)
            {
                official.monthsInOffice++;

                if (official.mode == ControlMode.DirectControl)
                {
                    // Sidelined: no autonomous action, trust erodes (GDD §7.3).
                    official.trust = Clamp(official.trust - 0.15f * (1.5f - official.loyalty / 100f));
                    continue;
                }

                // Mixed with the country so sixteen cabinets do not all draw the
                // same variance in the same month — which would make every
                // government in the world have a good year together.
                var rng = new Random(unchecked(state.rngSeed * 92821 + monthIndex * 31
                                               + (int)official.office + Hash.Of(country.id) * 7));

                double variance = (rng.NextDouble() * 2.0 - 1.0) * (official.riskTolerance / 100.0) * 0.3;
                double performance = Math.Max(0.0, Math.Min(1.2, official.competence / 100.0 + variance));
                float amount = (float)(0.05 + performance * 0.25);

                // Major policy settings steer every delegated official — Directed
                // as well as Autonomous (GDD §13). Only Direct Control escapes
                // it, having returned above. The operator's directive decides
                // *how* a ministry works; the government's stated priority still
                // decides what it is resourced to do, which is why paying
                // Influence does not buy a way around the administration.
                amount *= GovernmentSystem.PriorityMultiplierFor(country.government.leader.priority, official.office);

                ApplyPillarEffect(state, country, official.office, official.directiveId, amount);

                // Has this instruction been carried out? Reported once, on the
                // month it happens, so an order given is an order the operator
                // hears back about.
                if (country.isPlayer) CheckDirectiveCompletion(state, official);

                // The military desk replaces losses as routine business, in every
                // country. This is the *only* path by which a foreign government
                // restocks: it used to live four gates deep inside AISystem behind
                // a Security-priority check, and across thirty measured years no
                // foreign state ordered a single piece of equipment while the
                // player could replace anything. It costs treasury on both sides
                // and does nothing to a force already at establishment.
                //
                // Skipped when the operator has given a force-structure
                // instruction, which has already been carried out above and would
                // otherwise be paid for twice in the same month.
                if (official.office == Pillar.Military
                    && official.directiveId != MilitaryAdvice.PrepareForWar
                    && official.directiveId != MilitaryAdvice.DrawDown)
                    AcquisitionSystem.RestockRoutine(state, country.id, (float)performance);

                // What they did, in the operator's own cabinet only. Delegation
                // used to be silent: an official applied their effect and the
                // player saw some numbers move, which makes handing a pillar over
                // feel like switching it off rather than like employing somebody.
                if (country.isPlayer)
                    state.cabinetReport.Add(new CabinetReportLine
                    {
                        pillar = official.office,
                        officialName = official.displayName,
                        ownJudgement = official.mode == ControlMode.Autonomous,
                        summary = DescribeMonth(official, performance)
                    });

                // An official's own news comes up through their own desk, so a
                // weak minister is poor at reporting their own setback — which
                // is the most characteristic thing a weak minister does. This is
                // fair rather than opaque only because CABINET shows the desk's
                // REPORTING quality outright, so a player who is not being told
                // things can see exactly who is not telling them.
                //
                // Filing all of it under Government instead, as the first pass
                // did, meant one weak minister buried every *other* minister's
                // performance news — the opposite of diagnosable.

                // Occasional significant outcomes; appetite scales with risk tolerance.
                double eventChance = 0.04 + official.riskTolerance / 100.0 * 0.06;
                if (rng.NextDouble() < eventChance)
                {
                    bool success = rng.NextDouble() < performance * 0.7 + 0.15;
                    if (success)
                    {
                        ApplyPillarEffect(state, country, official.office, official.directiveId, amount * 3f);
                        official.trust = Clamp(official.trust + 1f);
                        ReportCabinetOutcome(state, country, official, true);
                    }
                    else
                    {
                        ApplyPillarEffect(state, country, official.office, official.directiveId, -amount * 2f);
                        official.trust = Clamp(official.trust - 1f);
                        ReportCabinetOutcome(state, country, official, false);
                    }
                }

                // Delegation slowly builds the relationship.
                official.trust = Clamp(official.trust + (official.mode == ControlMode.Autonomous ? 0.1f : 0.05f));

                // A standing directive is not news every month. It used to be
                // re-announced on the wire 115 times a decade; the CABINET panel
                // already shows what each minister is doing.
            }
        }

        /// <summary>
        /// Report a minister's notable month.
        ///
        /// Our own ministers reach the operator's desk through their own desk's
        /// reporting quality (GDD §28.1). A *foreign* minister's success or
        /// failure is world news, and only becomes ours to know at all through
        /// the intelligence layer — so it is filed as WIRE traffic, at the
        /// Intelligence desk, because that is the service that would surface it.
        /// Reporting it as if it arrived on our own ministry's paperwork would be
        /// a fog leak dressed as a notification.
        /// </summary>
        static void ReportCabinetOutcome(GameState state, CountryState country, Official official, bool success)
        {
            if (country.isPlayer)
            {
                state.AddNotification(
                    success ? NotificationClass.Advisory : NotificationClass.Priority,
                    $"{official.office.ToString().ToUpperInvariant()} {(success ? "INITIATIVE SUCCEEDS" : "SETBACK")}",
                    success
                        ? $"{official.displayName} delivers a notable result."
                        : $"{official.displayName} suffers a public setback.",
                    country.id, desk: ReportingSystem.DeskFor(official.office));
                return;
            }

            // Only a setback abroad is loud enough to travel on its own — and
            // only from a state we actually watch. Unfiltered, this was a quarter
            // of all terminal traffic (265 items a decade, the Treasurer of
            // Australia stumbling every month): a wire nobody reads. A network on
            // the state or a standing treaty with it earns the item.
            if (success) return;

            // A foreign minister's bad month is not news: even gated on states
            // we watch it was the single largest item on the terminal, and as a
            // chronicle line it would breach the thirty-year growth cap. The
            // *change* of a minister still reaches the wire (`CabinetLifecycle`).
            GameLog.Info("CABINET", $"{country.id}: {official.displayName} setback.");
        }

        /// <summary>
        /// Translate an official/operator action into national effects. Directive
        /// ids shift the emphasis; empty id = balanced default behavior.
        /// </summary>
        /// <summary>
        /// One plain sentence on how a delegated official's month went.
        ///
        /// Names the *instruction* where there is one and the official's own
        /// judgement where there is not, because those are different things for
        /// the operator to read: one is their decision being carried out, the
        /// other is somebody else's being made.
        /// </summary>
        static string DescribeMonth(Official official, double performance)
        {
            string quality = performance > 0.75 ? "a strong month"
                : performance > 0.45 ? "a steady month"
                : "a poor month";

            if (official.mode == ControlMode.Directed && !string.IsNullOrEmpty(official.directiveId))
            {
                string label = official.directiveId;
                foreach (var directive in GetDirectives(official.office))
                    if (directive.id == official.directiveId) label = directive.label;

                return $"worked to your instruction ({label.ToLowerInvariant()}) — {quality}";
            }

            return $"ran the pillar on their own judgement — {quality}";
        }

        /// <summary>
        /// A minister told to prepare for war buys what the force is actually
        /// short of, within what the treasury will bear.
        ///
        /// Scaled by how much of the month's work the official got done, so a
        /// competent minister prepares faster — which is the point of appointing
        /// one. What they buy is decided by the largest gap against the
        /// catalogue baseline, so a fleet that lost its carriers replaces
        /// carriers rather than buying more missiles.
        /// </summary>
        /// <summary>
        /// How ready the force is to deploy, 0..1 — the average of what each
        /// branch would actually bring. Readiness alone would call an unsupplied
        /// army ready, which is the thing the operator most needs not to believe.
        /// </summary>
        static float ReadinessProgress(CountryState country)
        {
            if (country == null) return 0f;
            float total = 0f;
            int branches = 0;
            foreach (ForceBranch branch in System.Enum.GetValues(typeof(ForceBranch)))
            {
                var force = country.military.Get(branch);
                if (force.strength <= 0.01f) continue;   // a branch we do not field
                total += System.Math.Min(force.readiness, force.supply) / 78f;
                branches++;
            }
            return branches == 0 ? 1f : total / branches;
        }

        /// <summary>
        /// How far along a directed official's instruction is, 0..1, or -1 when
        /// the instruction has no finish line. Public so views can draw it.
        /// </summary>
        public static float DirectiveProgress(GameState state, Official official)
        {
            if (official == null || official.mode != ControlMode.Directed) return -1f;

            var definition = FindDirective(official.office, official.directiveId);
            if (definition?.progress == null) return -1f;

            float value = definition.progress(state);
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }

        /// <summary>The definition behind an id, or null.</summary>
        public static DirectiveDef FindDirective(Pillar pillar, string directiveId)
        {
            if (string.IsNullOrEmpty(directiveId)) return null;
            foreach (var definition in GetDirectives(pillar))
                if (definition.id == directiveId) return definition;
            return null;
        }

        /// <summary>
        /// Tell the operator the month an instruction is met — once.
        ///
        /// FLASH is deliberately not used: this is good news and requires no
        /// decision, and §28.2 reserves FLASH for a turn the operator cannot take
        /// without answering. It is PRIORITY, which is what puts it on the
        /// briefing where they will actually see it.
        /// </summary>
        static void CheckDirectiveCompletion(GameState state, Official official)
        {
            var definition = FindDirective(official.office, official.directiveId);
            if (definition?.progress == null || string.IsNullOrEmpty(definition.completion))
            {
                official.directiveCompletionReported = false;
                return;
            }

            if (definition.progress(state) < 1f)
            {
                // Fell back below the goal — so it can be reported again when it
                // is next reached, rather than only ever once per save.
                official.directiveCompletionReported = false;
                return;
            }

            if (official.directiveCompletionReported) return;
            official.directiveCompletionReported = true;

            state.AddNotification(NotificationClass.Priority,
                $"{definition.label} — COMPLETE",
                $"{official.title} {official.displayName}: {definition.completion}",
                state.playerCountryId, desk: ReportingSystem.DeskFor(official.office));
        }

        static void ProcureShortfall(GameState state, CountryState country, float amount)
        {
            // PREPARE FOR WAR buys deliberately, against a looser definition of
            // "short" than routine replacement — that is what the operator is
            // paying Influence for. The shortfall itself is defined once, in
            // AcquisitionSystem, so this cannot drift away from what the AI and
            // the routine restock consider a gap.
            var worst = AcquisitionSystem.WorstShortfall(country, out float ratio);
            if (worst == null || ratio > 1.05f) return;

            // **Must out-buy the routine restock it replaces.** A directed
            // minister does not also run `RestockRoutine` — `MonthlyAct` skips it
            // for exactly this directive — so whatever is ordered here is the
            // *whole* of the month's procurement. At `max(0.5, amount/3)` that
            // was around half an increment against routine restocking's
            // `max(0.4, performance)`, which is ~0.8 for a competent minister:
            // PREPARE FOR WAR left the force holding measurably less than
            // leaving the same official alone, and charged Influence for it. The
            // looser shortfall threshold above meant it fired more often and
            // bought less each time, which reads as a directive that does
            // nothing rather than one that is subtly worse than nothing.
            float wanted = worst.orderIncrement * Math.Max(1.2f, amount / 3f);
            float affordable = country.resources.treasury * 0.25f
                               / Math.Max(0.0001f, worst.unitCost);

            float count = Math.Min(wanted, affordable);
            if (count >= 1f) AcquisitionSystem.OrderBy(state, country.id, worst.kind, count);
        }

        /// <summary>
        /// Drawing the force down: hulls and airframes sold back, money returned.
        ///
        /// Sells at a loss, deliberately. Equipment is worth less the moment it
        /// stops being needed, and a drawdown that returned full value would make
        /// oscillating between build-up and sell-off a free way to park money.
        /// </summary>
        static void SellDown(CountryState player, float amount)
        {
            const float ResaleValue = 0.55f;

            AssetProfile richest = null;
            float bestRatio = 0f;

            foreach (var asset in AssetCatalog.All)
            {
                var force = player.military.Get(asset.branch);

                // Measured against establishment, like every other stock
                // comparison — see AcquisitionSystem.EstablishmentFor. Against
                // the branch's own strength every ratio is 1.0 by construction,
                // so "what do we have most of" had no answer either.
                float target = asset.baselineAt100
                               * (AcquisitionSystem.EstablishmentFor(player, asset.branch) / 100f);
                if (target <= 0.01f) continue;

                float ratio = force.inventory.CountOf(asset.kind) / target;
                if (ratio <= bestRatio) continue;

                bestRatio = ratio;
                richest = asset;
            }
            if (richest == null) return;

            var branch = player.military.Get(richest.branch);
            float sold = Math.Min(branch.inventory.CountOf(richest.kind) * 0.08f,
                richest.orderIncrement * Math.Max(0.5f, amount / 3f));
            if (sold < 1f) return;

            branch.inventory.Add(richest.kind, -sold);
            branch.SyncStrength();
            player.resources.treasury += AssetCatalog.CostOf(richest.kind, sold) * ResaleValue;
        }

        static void ApplyPillarEffect(GameState state, CountryState player, Pillar pillar, string directiveId, float amount)
        {
            var p = player.pillars;
            var r = player.resources;
            switch (pillar)
            {
                case Pillar.Military:
                    if (directiveId == "MIL_CONSERVE") { p.military = Growth.Apply(p.military, amount * 0.4f); r.treasury += amount * 8f; }
                    // "Prioritize force readiness over budget": slower growth,
                    // real money out. The readiness itself is not applied here —
                    // it raises the sustainment *target* in
                    // MilitarySystem.ReadinessDirectiveBonus, because readiness
                    // drifts toward a target every month and anything added
                    // directly is erased before the player can feel it.
                    // Until this branch existed the directive fell through to
                    // the no-directive default: it cost 1 Influence and bought
                    // nothing whatsoever.
                    else if (directiveId == "MIL_READINESS") { p.military = Growth.Apply(p.military, amount * 0.6f); r.treasury -= amount * 10f; }

                    // The two instructions that act on the *inventory* rather
                    // than the pillar. A minister told to prepare buys what the
                    // force is short of; one told to draw down sells it back.
                    // Both are the operator steering a delegated pillar rather
                    // than watching outcomes appear, which is the whole point of
                    // delegation being a decision.
                    else if (directiveId == MilitaryAdvice.PrepareForWar) ProcureShortfall(state, player, amount);
                    else if (directiveId == MilitaryAdvice.DrawDown) SellDown(player, amount);

                    else p.military = Growth.Apply(p.military, amount);
                    break;

                case Pillar.Economy:
                    if (directiveId == "ECO_GROWTH") { p.economy = Growth.Apply(p.economy, amount); r.treasury += amount * 6f; }
                    else if (directiveId == "ECO_AUSTERITY") { p.economy = Growth.Apply(p.economy, amount * 0.3f); r.treasury += amount * 16f; }
                    else { p.economy = Growth.Apply(p.economy, amount * 0.7f); r.treasury += amount * 10f; }
                    break;

                case Pillar.Intelligence:
                    if (directiveId == "INT_COUNTERINTEL") { p.intelligence = Growth.Apply(p.intelligence, amount * 0.6f); player.stability = Clamp(player.stability + amount * 0.3f); }
                    else p.intelligence = Growth.Apply(p.intelligence, amount * (directiveId == "INT_COLLECTION" ? 1f : 0.8f));
                    break;

                case Pillar.Diplomacy:
                    if (directiveId == "DIP_PRESSURE") { p.diplomacy = Growth.Apply(p.diplomacy, amount * 0.5f); p.military = Growth.Apply(p.military, amount * 0.2f); }
                    else p.diplomacy = Growth.Apply(p.diplomacy, amount * (directiveId == "DIP_OUTREACH" ? 1f : 0.8f));
                    break;

                default: // Government
                    if (directiveId == "GOV_APPROVAL") player.governmentApproval = Clamp(player.governmentApproval + amount);
                    else if (directiveId == "GOV_STABILITY") player.stability = Clamp(player.stability + amount);
                    else
                    {
                        player.governmentApproval = Clamp(player.governmentApproval + amount * 0.5f);
                        player.stability = Clamp(player.stability + amount * 0.5f);
                    }
                    p.government = Growth.Apply(p.government, amount * 0.3f);
                    break;
            }
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
