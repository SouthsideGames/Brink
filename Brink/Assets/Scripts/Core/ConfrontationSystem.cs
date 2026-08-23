using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Confrontation lifecycle (GDD Phase 4, §18, §26): begin with an objective
    /// and a Primary Strategy, escalate or de-escalate (skipping levels is
    /// allowed but costly), run operations, and settle by negotiation rather
    /// than a surrender button. The opponent continually evaluates whether
    /// resistance still beats accepting terms.
    /// </summary>
    public static class ConfrontationSystem
    {
        /// <summary>CP cost to open a confrontation.</summary>
        public const int OpenCost = 2;

        /// <summary>CP cost per escalation step; abrupt jumps add an Escalation Premium (GDD §7.1).</summary>
        public const int EscalationStepCost = 1;

        public static Confrontation Begin(
            GameState state, TurnManager turns,
            string initiatorId, string defenderId,
            ConfrontationObjective objective, string objectiveLocationId,
            PrimaryStrategy strategy)
        {
            if (state.ActiveConfrontationFor(initiatorId) != null)
            {
                GameLog.Warn("CONFRONT", "A confrontation is already active.");
                return null;
            }
            // Check every condition BeginBy enforces before charging for it — a
            // state already locked in someone else's confrontation cannot be
            // confronted, and discovering that after the spend cost the player
            // the command points for nothing.
            if (state.ActiveConfrontationFor(defenderId) != null)
            {
                GameLog.Warn("CONFRONT",
                    $"{state.FindCountry(defenderId)?.displayName ?? defenderId} is already committed elsewhere.");
                return null;
            }
            if (!turns.SpendCommandPoints(OpenCost, "Open confrontation"))
                return null;

            var opened = BeginBy(state, initiatorId, defenderId, objective, objectiveLocationId, strategy);
            if (opened != null)
            {
                ProgressionSystem.RecordInitiative(state);
                ProgressionSystem.AwardXP(state, 20, "Confrontation opened");
            }
            return opened;
        }

        /// <summary>
        /// Open a confrontation without charging player Command Points. AI states
        /// use this directly; the player goes through <see cref="Begin"/>.
        /// </summary>
        /// <summary>
        /// The most simultaneous commitment any state can sustain, in the units
        /// `TheatreSystem` counts (roughly "one limited war" = 1).
        ///
        /// A ceiling rather than a ban. Below it a second front is expensive; at
        /// it, the army has run out. Set so that two limited wars are possible
        /// and painful, and a total war plus anything else is not.
        /// </summary>
        public const float MaxCommitment = 2.2f;

        /// <summary>An unresolved confrontation between exactly these two states.</summary>
        public static Confrontation ExistingBetween(GameState state, string a, string b)
        {
            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved) continue;
                if (confrontation.Involves(a) && confrontation.Involves(b)) return confrontation;
            }
            return null;
        }

        /// <summary>
        /// Whether this state can take on another front, and if not, why.
        ///
        /// The reason is returned because a refusal the operator cannot explain
        /// reads as a bug — the same rule the operation catalogue follows.
        /// </summary>
        public static bool CanOpenAnother(GameState state, string countryId, out string reason)
        {
            reason = "";
            float committed = TheatreSystem.TotalCommitment(state, countryId);
            if (committed < MaxCommitment) return true;

            reason = "The force is already committed to the limit. Settle or wind down an " +
                     "existing confrontation before opening another.";
            return false;
        }

        public static Confrontation BeginBy(
            GameState state,
            string initiatorId, string defenderId,
            ConfrontationObjective objective, string objectiveLocationId,
            PrimaryStrategy strategy)
        {
            // Two states already fighting *each other* cannot open a second war
            // between the same pair — that is the same war. Fighting somebody
            // else at the same time is a different question, answered below.
            if (ExistingBetween(state, initiatorId, defenderId) != null) return null;

            // A second front is a decision, not a prohibition (GDD §16, §18.1).
            //
            // This used to be a hard block: one confrontation per state, full
            // stop. That made the map decoration — a war in the Atlantic and a
            // war in the Indo-Pacific were indistinguishable because you could
            // never have both — and it left a military operator with nothing to
            // do the moment their one war settled.
            //
            // The constraint is real but it is now *priced*: every extra
            // commitment drags operation power in every other theatre
            // (`TheatreSystem.FocusFactor`), and a visibly overstretched power
            // invites the pressure it cannot answer. Beyond a hard ceiling the
            // state simply cannot sustain another front, which is a fact about
            // armies rather than a rule about turns.
            if (!CanOpenAnother(state, initiatorId, out string why))
            {
                if (initiatorId == state.playerCountryId) GameLog.Warn("CONFRONT", why);
                return null;
            }

            var confrontation = new Confrontation
            {
                id = $"CONF_{state.date.SortKey}_{state.confrontations.Count}",
                initiatorId = initiatorId,
                defenderId = defenderId,
                objective = objective,
                objectiveLocationId = objectiveLocationId ?? "",
                primaryStrategy = strategy,
                escalation = EscalationState.Tension,
                startDate = state.date,
                theatre = TheatreSystem.Of(defenderId)
            };
            state.confrontations.Add(confrontation);

            var initiator = state.FindCountry(initiatorId);
            var defender = state.FindCountry(defenderId);

            if (defenderId == state.playerCountryId)
            {
                state.AddNotification(NotificationClass.Flash, "CONFRONTATION OPENED AGAINST US",
                    $"{initiator?.displayName} has moved against us. Objective: " +
                    $"{ObjectiveText(state, confrontation)}.", initiatorId,
                    desk: ReportingDesk.Military);
            }
            else if (initiatorId == state.playerCountryId)
            {
                state.AddNotification(NotificationClass.Priority, "CONFRONTATION OPENED",
                    $"Objective: {ObjectiveText(state, confrontation)} vs {defender?.displayName}. " +
                    $"Primary strategy: {Phrase.Of(strategy)}.", initiatorId);
            }
            else
            {
                state.AddNotification(NotificationClass.Wire, "FOREIGN CONFRONTATION",
                    $"{initiator?.displayName} has opened a confrontation with {defender?.displayName}.", initiatorId,
                    desk: ReportingDesk.Military);
            }
            state.AddChronicle(ChronicleCategory.Diplomatic, initiatorId,
                $"Confrontation opened against {defender?.displayName}: {ObjectiveText(state, confrontation)}.",
                Publicity.Public);
            GameLog.Info("CONFRONT", $"Confrontation opened vs {defenderId} ({strategy}).");
            return confrontation;
        }

        /// <summary>
        /// Change escalation level. Jumping multiple levels is permitted when
        /// capability allows, but each skipped level adds an Escalation Premium
        /// in CP and political cost (GDD §18.1). De-escalation is free of premium.
        /// </summary>
        public static bool SetEscalation(GameState state, TurnManager turns, Confrontation confrontation, EscalationState target)
        {
            if (confrontation.resolved) return false;
            int steps = (int)target - (int)confrontation.escalation;
            if (steps == 0) return false;

            var player = state.PlayerCountry;
            bool applied = ApplyEscalation(state, turns, confrontation, target, player, steps);
            if (applied) ProgressionSystem.RecordInitiative(state);
            return applied;
        }

        /// <summary>
        /// Escalation by an AI actor. Pays the same political premium for abrupt
        /// jumps, but has no player Command Point pool to draw on.
        /// </summary>
        public static bool SetEscalationBy(GameState state, Confrontation confrontation,
            EscalationState target, string actorId)
        {
            if (confrontation.resolved) return false;
            int steps = (int)target - (int)confrontation.escalation;
            if (steps == 0) return false;

            var actor = state.FindCountry(actorId);
            if (actor == null) return false;
            return ApplyEscalation(state, null, confrontation, target, actor, steps);
        }

        static bool ApplyEscalation(GameState state, TurnManager turns, Confrontation confrontation,
            EscalationState target, CountryState player, int steps)
        {

            if (steps > 0)
            {
                int premium = Math.Max(0, steps - 1); // abrupt escalation premium
                if (turns != null)
                    premium = Math.Max(0, premium - (int)ProgressionSystem.EffectValue(state, SkillEffect.EscalationDiscipline));
                int cost = EscalationStepCost * steps + premium;
                if (turns != null && !turns.SpendCommandPoints(cost, $"Escalate to {target}"))
                    return false;

                confrontation.escalationPressure += steps * 12f;
                if (premium > 0)
                {
                    player.stability = Clamp(player.stability - premium * 2f);
                    player.warSupport = Clamp(player.warSupport - premium * 3f);
                    state.AddNotification(NotificationClass.Priority, "ABRUPT ESCALATION",
                        "Skipping escalation levels carried a political premium.", player.id,
                        desk: ReportingDesk.Military);
                }
                if (target >= EscalationState.LimitedConflict)
                    player.military.alertPosture = true;

                // Crossing into open conflict calls in defense commitments.
                if (target >= EscalationState.LimitedConflict)
                    AllianceSystem.InvokeObligations(state, confrontation);
            }
            else
            {
                confrontation.escalationPressure = Math.Max(0f, confrontation.escalationPressure + steps * 8f);
                state.AddNotification(NotificationClass.Advisory, "DE-ESCALATION",
                    $"Posture reduced to {Phrase.Of(target)}.", player.id, desk: ReportingDesk.Military);
            }

            confrontation.escalation = target;
            state.AddChronicle(ChronicleCategory.Military, player.id,
                $"Escalation state: {Phrase.Of(target)}.", Publicity.Public);
            GameLog.Info("CONFRONT", $"Escalation set to {target}.");
            return true;
        }

        /// <summary>
        /// What this order will cost the operator right now, all in. The order
        /// screen prints this and <see cref="LaunchOperation"/> charges it — one
        /// source of truth, because the affordability gating reads the printed
        /// "[N CP]" tag and silently stops working if the two drift apart.
        /// </summary>
        public static int OperationCostFor(GameState state, Confrontation confrontation,
            OperationType operationType)
        {
            int baseCost = MilitarySystem.OperationCost(operationType);

            // Escalation states describe the situation; they do not gate orders
            // (GDD §18.1). Opening fire from below Limited Conflict is allowed —
            // it simply carries the confrontation up with it, and is priced for
            // the abruptness rather than refused. Defensive work on our own
            // ground is not opening fire and is not surcharged for it.
            bool escalatory = OperationCatalog.For(operationType)?.targeting != OperationTargeting.OwnGround
                              && operationType != OperationType.Withdraw;
            if (escalatory
                && confrontation != null
                && confrontation.escalation < EscalationState.LimitedConflict)
                baseCost += 2;

            return ProgressionSystem.DiscountedCost(state, baseCost, SkillEffect.OperationEfficiency);
        }

        /// <summary>
        /// Whether this order needs a confrontation to exist at all.
        ///
        /// Defensive programmes conducted on ground we already hold are peacetime
        /// national work — fortifying a position, pacifying occupied territory,
        /// escorting our own shipping, building the shield. Requiring a war for
        /// them made every one of them reachable only once it was too late to
        /// matter, which is the worst possible time to start building a defence.
        /// </summary>
        public static bool RequiresConfrontation(OperationType operationType)
            => OperationCatalog.For(operationType)?.targeting != OperationTargeting.OwnGround;

        /// <summary>Player launches an operation (Direct Control; costs CP).</summary>
        public static OperationRecord LaunchOperation(
            GameState state, TurnManager turns, Confrontation confrontation,
            string targetLocationId, OperationType operationType, OperationDirective directive)
        {
            if (RequiresConfrontation(operationType) && (confrontation == null || confrontation.resolved))
                return null;
            if (confrontation != null && confrontation.resolved) confrontation = null;

            var target = state.FindLocation(targetLocationId);
            if (target == null) return null;

            int cost = OperationCostFor(state, confrontation, operationType);
            if (!turns.SpendCommandPoints(cost, $"{operationType} at {target.displayName}"))
                return null;

            var record = LaunchOperationBy(state, confrontation, state.playerCountryId,
                targetLocationId, operationType, directive);
            if (record != null)
            {
                ProgressionSystem.RecordInitiative(state);
                ProgressionSystem.AwardXP(state, record.success ? 18 : 6, "Operation conducted");
            }
            return record;
        }

        /// <summary>Operation launched by any actor. AI states use the same resolution.</summary>
        public static OperationRecord LaunchOperationBy(GameState state, Confrontation confrontation,
            string attackerId, string targetLocationId, OperationType operationType, OperationDirective directive)
        {
            if (RequiresConfrontation(operationType) && (confrontation == null || confrontation.resolved))
                return null;
            if (confrontation != null && confrontation.resolved) confrontation = null;

            var target = state.FindLocation(targetLocationId);
            if (target == null) return null;

            // Whether the order is possible at all — whose ground it is, whether
            // we have the fleet or the air force it needs, whether the target is
            // even reachable by sea. One gate, shared with the order screen, so
            // what the operator is offered and what the simulation accepts can
            // never disagree.
            if (!OperationCatalog.CanOrder(state, attackerId, target, operationType, out string blocked))
            {
                if (attackerId == state.playerCountryId)
                    GameLog.Warn("CONFRONT", $"Operation refused: {blocked}.");
                return null;
            }

            // Defensive operations on our own ground are not acts of war and do
            // not carry the confrontation upward — fortifying a position we hold
            // must not be the thing that starts the shooting.
            bool escalatory = OperationCatalog.For(operationType)?.targeting != OperationTargeting.OwnGround
                              && operationType != OperationType.Withdraw;

            // An overt operation *is* limited conflict. Rather than refusing the
            // order, the act carries the confrontation with it (GDD §18.1) —
            // but no further than the order allowed. An escalation limit lets an
            // operator commit forces without also handing over the decision to
            // widen the war (GDD §19); the operation is simply refused if it
            // could not be conducted within the limit given.
            if (escalatory && confrontation != null
                && confrontation.escalation < EscalationState.LimitedConflict)
            {
                if (directive.escalationLimit < EscalationState.LimitedConflict)
                {
                    if (attackerId == state.playerCountryId)
                        GameLog.Warn("CONFRONT",
                            "The order's escalation limit forbids the conflict this would open.");
                    return null;
                }
                SetEscalationBy(state, confrontation, EscalationState.LimitedConflict, attackerId);
            }

            int monthIndex = state.date.MonthsSince(state.startDate);

            // A peacetime programme has no confrontation to count operations
            // against, so the sequence comes from the world's own action counter.
            // It must be `NextActionSequence()` — the counter *advances*. Reading
            // the raw field seeded every programme in a given month identically,
            // so twenty consecutive orders were one draw repeated twenty times
            // and a failed roll could never be retried. That is the covert-op RNG
            // collision this project has already fixed once, inverted.
            //
            // Both branches stay deterministic: the counter advances the same way
            // on a replay, and neither may be a wall clock (GDD §34).
            int sequence = confrontation != null
                ? confrontation.operations.Count
                : state.NextActionSequence();
            var rng = new Random(unchecked(state.rngSeed * 15485863 + monthIndex * 97
                                           + sequence + Hash.Of(attackerId)));

            // Coalition partners add real weight to the operation (GDD §15.2),
            // contributing the branches this kind of operation actually uses.
            // Nobody joins us in fortifying our own ground.
            float coalitionSupport = confrontation == null
                ? 0f
                : DiplomacySystem.CoalitionStrength(state, confrontation, attackerId, operationType);

            var record = MilitarySystem.ResolveOperation(
                state, confrontation, attackerId, target, operationType, directive, rng, coalitionSupport);

            // Taking the last of a country's ground ends the war on the spot.
            // Checked here rather than in the monthly tick so the operation that
            // finishes it is the one that reports it.
            if (confrontation != null) ConquestSystem.CheckForTotalConquest(state, confrontation);

            if (attackerId == state.playerCountryId)
            {
                // An operation that has already resolved is a report, not a
                // decision — FLASH is reserved for what needs answering now
                // (GDD §28.2), and a war generated a FLASH every single month.
                // Peacetime programmes are quieter still: routine national work
                // does not deserve the same weight as a battle.
                state.AddNotification(
                    confrontation == null ? NotificationClass.Advisory : NotificationClass.Priority,
                    confrontation == null
                        ? $"PROGRAMME {(record.success ? "COMPLETE" : "SETBACK")} — {target.displayName}"
                        : $"OPERATION {(record.success ? "SUCCESS" : "FAILURE")} — {target.displayName}",
                    Explained(record), attackerId, desk: ReportingDesk.Military);
            }
            else if (target.ownerId == state.playerCountryId
                     || (confrontation != null && confrontation.Involves(state.playerCountryId)))
            {
                // An attack on us that failed is the cheapest lesson available:
                // what held here is what to build at the next position along.
                // Reporting only "they failed" wastes it.
                state.AddNotification(NotificationClass.Priority,
                    $"ENEMY OPERATION — {target.displayName}", Explained(record), attackerId,
                    desk: ReportingDesk.Military);
            }
            else
            {
                // Somebody else's war. The outcome is public; the analysis is not
                // ours to have.
                state.AddNotification(NotificationClass.Wire,
                    $"FOREIGN OPERATION — {target.displayName}", record.summary, attackerId,
                    desk: ReportingDesk.Military);
            }
            return record;
        }

        /// <summary>The outcome, followed by why it went that way (GDD §28.1).</summary>
        static string Explained(OperationRecord record)
            => string.IsNullOrEmpty(record.explanation)
                ? record.summary
                : $"{record.summary}\n{record.explanation}";

        /// <summary>
        /// Monthly confrontation tick: exhaustion, attrition of political
        /// support, and the opponent's own decision to resist or seek terms.
        /// </summary>
        public static void MonthlyTick(GameState state)
        {
            // Every live confrontation resolves, including wars the player is not
            // party to — the world does not wait for the player (GDD §17).
            for (int i = 0; i < state.confrontations.Count; i++)
            {
                var confrontation = state.confrontations[i];
                if (!confrontation.resolved)
                    TickConfrontation(state, confrontation);
            }
        }

        static void TickConfrontation(GameState state, Confrontation confrontation)
        {
            confrontation.monthsActive++;

            var initiator = state.FindCountry(confrontation.initiatorId);
            var defender = state.FindCountry(confrontation.defenderId);

            if (confrontation.escalation >= EscalationState.LimitedConflict)
            {
                float drain = confrontation.escalation == EscalationState.TotalWar ? 2.2f : 1.1f;
                confrontation.initiatorWarExhaustion += drain;
                confrontation.defenderWarExhaustion += drain;
                initiator.warExhaustion = Clamp(initiator.warExhaustion + drain * 0.5f);
                defender.warExhaustion = Clamp(defender.warExhaustion + drain * 0.5f);

                // Long wars erode support and approval (GDD §12: historical memory).
                initiator.warSupport = Clamp(initiator.warSupport - drain * 0.7f);
                defender.warSupport = Clamp(defender.warSupport - drain * 0.5f);
                initiator.governmentApproval = Clamp(initiator.governmentApproval - drain * 0.35f);

                initiator.resources.treasury -= 35f * drain;
                defender.resources.treasury -= 30f * drain;
            }
            else if (confrontation.escalation >= EscalationState.Crisis)
            {
                // A crisis nobody defuses gets more dangerous by standing there
                // (GDD §18.1). Without this the only inputs to pressure were
                // one-off — a step up, a sanction, an operation — against a
                // standing decay, so it could never reach the boil point and the
                // mechanic never fired once in a decade of measured play.
                //
                // At +3/month a standoff that neither side resolves crosses over
                // in a little over a year. That is the intended shape: the cost
                // of leaving a crisis open is that eventually it is not your
                // decision any more.
                confrontation.escalationPressure += 3f;
            }
            else
            {
                confrontation.escalationPressure = Math.Max(0f, confrontation.escalationPressure - 2f);
            }

            CheckPressureBoilover(state, confrontation);

            if (confrontation.Involves(state.playerCountryId))
                EvaluateOpponentSettlement(state, confrontation);
        }

        /// <summary>
        /// Add hidden escalation pressure between two states, if they are in a
        /// confrontation. Called by the non-military systems: sanctions and
        /// covert action wind a situation up even though neither fires a shot,
        /// which is precisely GDD §18.1's point.
        /// </summary>
        public static void AddPressure(GameState state, string actorId, string targetId, float amount)
        {
            var confrontation = state.ActiveConfrontationFor(actorId);
            if (confrontation == null || confrontation.resolved) return;
            if (!confrontation.Involves(targetId)) return;

            confrontation.escalationPressure =
                Math.Max(0f, confrontation.escalationPressure + amount);
        }

        /// <summary>
        /// Hidden escalation pressure underlying the visible state (GDD §18.1).
        ///
        /// The visible level is what the world can see; pressure is what is
        /// actually building underneath it. A confrontation sitting quietly at
        /// Tension with pressure near the top is one incident away from a
        /// shooting war, and eventually gets there without anyone choosing it.
        ///
        /// Boiling over does not run away: the step itself adds pressure but the
        /// release takes more off, so the situation has to be wound up again
        /// before it can climb further. It stops at Limited Conflict — crossing
        /// into a wider war is a decision, never an accident.
        /// </summary>
        static void CheckPressureBoilover(GameState state, Confrontation confrontation)
        {
            const float BoilPoint = 70f;
            if (confrontation.escalationPressure < BoilPoint) return;
            if (confrontation.escalation >= EscalationState.LimitedConflict) return;

            var next = (EscalationState)((int)confrontation.escalation + 1);
            SetEscalationBy(state, confrontation, next, confrontation.initiatorId);
            confrontation.escalationPressure -= 25f;

            if (confrontation.Involves(state.playerCountryId))
                state.AddNotification(NotificationClass.Priority, "SITUATION DETERIORATES",
                    "Pressure has outrun the diplomacy holding it. The confrontation " +
                    $"has moved to {next.ToString().ToUpperInvariant()} without anyone choosing it.",
                    confrontation.OpponentOf(state.playerCountryId), desk: ReportingDesk.Military);
        }

        /// <summary>
        /// The opponent decides whether resistance is still preferable to
        /// accepting terms (GDD §18.2). AI may reject unreasonable demands even
        /// while losing, and overreach can prolong an otherwise winnable war.
        /// </summary>
        static void EvaluateOpponentSettlement(GameState state, Confrontation confrontation)
        {
            if (confrontation.monthsActive < 2) return;
            if (SettlementWillingness(state, confrontation, state.playerCountryId) <= 35f) return;

            var opponent = state.FindCountry(confrontation.OpponentOf(state.playerCountryId));
            // Say where and what to press. "They are prepared to negotiate" told
            // the operator an opportunity existed and left them hunting for a
            // verb that, at the time, did not exist.
            state.AddNotification(NotificationClass.Priority, "OPPONENT SIGNALS TERMS",
                $"{opponent.displayName} is prepared to negotiate a settlement. " +
                "Open MILITARY and scroll to NEGOTIATED SETTLEMENT — the terms they would " +
                "sign today are listed there, with one control to accept them.", opponent.id,
                desk: ReportingDesk.Military);
        }

        /// <summary>
        /// How willing the opponent is to accept the player's terms. Weighs their
        /// exhaustion and lost momentum against their public support, institutional
        /// strength, and how existential the demand is.
        /// </summary>
        static float SettlementWillingness(GameState state, Confrontation confrontation, string proposerId)
        {
            float willingness = BaseSettlementWillingness(state, confrontation, proposerId);

            // The legacy single-objective path prices the demand here. The terms
            // system (PeaceSystem) prices each demand explicitly instead, so it
            // uses BaseSettlementWillingness and must not be double-charged.
            if (confrontation.objective == ConfrontationObjective.TerritorialConcession)
            {
                var loc = state.FindLocation(confrontation.objectiveLocationId);
                if (loc != null && loc.type == LocationType.Capital)
                {
                    // Ceding the seat of state ends state continuity; a government
                    // negotiates that away only in total collapse (GDD §22).
                    willingness -= 120f;
                }
                else
                {
                    willingness -= 12f;
                }
                if (loc != null && loc.ownerId == proposerId)
                    willingness += 30f; // possession is leverage
            }

            return willingness;
        }

        /// <summary>CP cost of changing the Primary Strategy mid-confrontation.</summary>
        public const int PivotCost = 3;

        /// <summary>
        /// Strategic Pivot (GDD §18.2): change the Primary Strategy of a running
        /// confrontation. Always possible, never free — it costs command
        /// attention, surrenders momentum, and tells the world we could not make
        /// our first approach work.
        /// </summary>
        public static bool Pivot(GameState state, TurnManager turns,
            Confrontation confrontation, PrimaryStrategy strategy)
        {
            if (confrontation == null || confrontation.resolved) return false;
            if (confrontation.primaryStrategy == strategy) return false;
            if (!turns.SpendCommandPoints(PivotCost, $"Pivot to {strategy}")) return false;

            var player = state.PlayerCountry;
            confrontation.primaryStrategy = strategy;

            // Effort already spent in the old domain does not transfer.
            bool weAreInitiator = confrontation.initiatorId == player.id;
            confrontation.momentum += weAreInitiator ? -12f : 12f;
            player.warSupport = Clamp(player.warSupport - 5f);

            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 12, "Strategic pivot");

            state.AddNotification(NotificationClass.Priority, "STRATEGIC PIVOT",
                $"Primary strategy against {state.FindCountry(confrontation.OpponentOf(player.id))?.displayName} " +
                $"is now {strategy.ToString().ToUpperInvariant()}. The effort already spent does not carry over.",
                confrontation.OpponentOf(player.id));
            state.AddChronicle(ChronicleCategory.Military, player.id,
                $"Primary strategy changed to {Phrase.Of(strategy)}.");
            return true;
        }

        /// <summary>
        /// Non-military coercion, weighted by the Primary Strategy we committed
        /// to (GDD §18.2).
        ///
        /// Every domain applies some pressure, because a government under real
        /// economic or political strain is more willing to stop whatever we said
        /// our approach was. But the domain we actually committed to counts for
        /// far more: choosing a Primary Strategy means concentrating effort, and
        /// concentrated effort is what breaks a government's resolve. This is what
        /// makes an economic or political campaign a way to win rather than a
        /// flavour label on a war.
        /// </summary>
        public static float StrategicPressure(GameState state, Confrontation confrontation, string proposerId)
        {
            var opponent = state.FindCountry(confrontation.OpponentOf(proposerId));
            if (opponent == null) return 0f;

            float Weight(PrimaryStrategy domain)
                => confrontation.primaryStrategy == domain ? 1f : 0.35f;

            // --- economic strain we are inflicting ---
            float sanctionPressure = EconomySystem.SanctionPressureOn(state, opponent.id);
            float economic = sanctionPressure * 9f
                             + Math.Max(0f, -opponent.economy.growthRate) * 2.2f
                             + Math.Max(0f, 50f - opponent.economy.confidence) * 0.18f;
            economic *= Weight(PrimaryStrategy.Economic);

            // --- political and institutional strain ---
            float political = Math.Max(0f, 55f - opponent.stability) * 0.22f
                              + opponent.government.conspiracyLevel * 0.12f
                              + Math.Max(0f, 50f - opponent.governmentApproval) * 0.10f;
            political *= Weight(PrimaryStrategy.IntelligencePolitical);

            // --- diplomatic isolation: who is left standing with them ---
            float isolation = 0f;
            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(opponent.id)) continue;
                string other = relationship.PartnerOf(opponent.id);
                if (other == proposerId) continue;
                isolation += Math.Max(0f, 45f - relationship.relations) * 0.035f;
            }
            var theirCoalition = state.FindCoalitionLedBy(confrontation.id, opponent.id);
            if (theirCoalition == null || theirCoalition.dissolved) isolation += 5f;
            isolation *= Weight(PrimaryStrategy.Diplomatic);

            // --- the ground itself ---
            float territorial = TerritorySystem.LostValue(state, opponent.id) * 0.10f;
            territorial *= Weight(PrimaryStrategy.Military);

            return economic + political + isolation + territorial;
        }

        /// <summary>
        /// How badly the opponent wants the fighting to stop, independent of what
        /// is being asked of them. This is what a set of terms is priced against.
        /// </summary>
        static float BaseSettlementWillingness(GameState state, Confrontation confrontation, string proposerId)
        {
            bool proposerIsInitiator = proposerId == confrontation.initiatorId;
            var opponent = state.FindCountry(confrontation.OpponentOf(proposerId));
            float opponentExhaustion = proposerIsInitiator ? confrontation.defenderWarExhaustion : confrontation.initiatorWarExhaustion;
            float momentumAgainstOpponent = proposerIsInitiator ? confrontation.momentum : -confrontation.momentum;

            float willingness = opponentExhaustion * 0.8f + momentumAgainstOpponent * 0.6f
                                - opponent.warSupport * 0.35f - opponent.pillars.government * 0.12f;

            // Nobody settles while the situation is still winding up. Hidden
            // pressure is the reason a confrontation that looks quiet on paper
            // cannot actually be closed (GDD §18.1).
            willingness -= confrontation.escalationPressure * 0.15f;

            // Pressure applied outside the battlefield counts too (GDD §18.2,
            // §20). Without this, willingness came only from war exhaustion,
            // momentum, war support and the government pillar — none of which
            // sanctions, covert action or isolation touch — so four of the five
            // Primary Strategies had no route to their objective at all.
            willingness += StrategicPressure(state, confrontation, proposerId);

            // Coercive credibility: our terms are taken more seriously.
            if (proposerId == state.playerCountryId)
                willingness += ProgressionSystem.EffectValue(state, SkillEffect.SettlementLeverage);

            // A force built and postured to deter is more believable when it
            // demands terms (GDD §19).
            var proposer = state.FindCountry(proposerId);
            if (proposer != null)
            {
                if (proposer.military.doctrine == MilitaryDoctrine.Deterrence) willingness += 10f;
                if (proposer.military.posture == MilitaryPosture.Forward) willingness += 6f;

                // Verification lets a rival believe terms they cannot audit
                // themselves, which is what actually closes a settlement (GDD §11).
                willingness += TechnologySystem.Effectiveness(proposer, "CAP_VERIFICATION") * 12f;
            }

            return willingness;
        }

        /// <summary>Whether the opponent would accept terms proposed by <paramref name="proposerId"/>.</summary>
        public static bool WouldAcceptTermsFrom(GameState state, Confrontation confrontation, string proposerId)
            => SettlementWillingness(state, confrontation, proposerId) > 20f;

        /// <summary>
        /// How badly the opponent wants this to stop, before any specific terms
        /// are priced against it (see <see cref="PeaceSystem"/>).
        /// </summary>
        public static float SettlementWillingnessFor(GameState state, Confrontation confrontation, string proposerId)
            => BaseSettlementWillingness(state, confrontation, proposerId);

        /// <summary>Close a confrontation that ended in a negotiated settlement.</summary>
        public static void CloseWithSettlement(GameState state, Confrontation confrontation,
            string proposerId, string summary)
            => Close(state, confrontation, proposerId, true, summary);

        /// <summary>Whether the opponent would currently accept the player's terms.</summary>
        public static bool OpponentWouldAccept(GameState state, Confrontation confrontation)
            => WouldAcceptTermsFrom(state, confrontation, state.playerCountryId);

        /// <summary>
        /// Attempt a negotiated settlement. Returns true when terms are accepted
        /// and the confrontation closes; false when the opponent refuses —
        /// overreaching prolongs the war (GDD §26).
        /// </summary>
        public static bool ProposeSettlement(GameState state, Confrontation confrontation, bool concedeInstead = false)
            => ProposeSettlementBy(state, confrontation, state.playerCountryId, concedeInstead);

        /// <summary>Settlement proposed by any actor — AI states negotiate identically.</summary>
        public static bool ProposeSettlementBy(GameState state, Confrontation confrontation,
            string proposerId, bool concedeInstead = false)
        {
            if (confrontation == null || confrontation.resolved) return false;
            var player = state.FindCountry(proposerId);
            var opponent = state.FindCountry(confrontation.OpponentOf(proposerId));

            if (concedeInstead)
            {
                Close(state, confrontation, proposerId, false,
                    $"{player.displayName} concedes terms to {opponent.displayName}. Objective abandoned.");
                player.warSupport = Clamp(player.warSupport - 10f);
                player.governmentApproval = Clamp(player.governmentApproval - 8f);
                return true;
            }

            if (!WouldAcceptTermsFrom(state, confrontation, proposerId))
            {
                state.AddNotification(NotificationClass.Advisory, "TERMS REJECTED",
                    $"{opponent.displayName} rejects the proposed settlement. The confrontation continues.",
                    opponent.id, desk: ReportingDesk.Military);
                GameLog.Info("CONFRONT", "Settlement rejected; opponent still prefers resistance.");
                return false;
            }

            // Terms accepted: apply the objective.
            string summary;
            switch (confrontation.objective)
            {
                case ConfrontationObjective.TerritorialConcession:
                {
                    var loc = state.FindLocation(confrontation.objectiveLocationId);
                    // A settled cession is recognised title, not occupation.
                    if (loc != null) TerritorySystem.Cede(state, loc, proposerId);
                    summary = $"Settlement: {opponent.displayName} cedes {loc?.displayName}.";
                    player.pillars.diplomacy = Clamp(player.pillars.diplomacy - 3f);
                    break;
                }
                case ConfrontationObjective.ResourceAccess:
                    player.resources.strategicMaterials = Clamp(player.resources.strategicMaterials + 12f);
                    player.resources.energy = Clamp(player.resources.energy + 8f);
                    summary = $"Settlement: resource access secured from {opponent.displayName}.";
                    break;
                case ConfrontationObjective.PolicyReversal:
                    opponent.pillars.government = Clamp(opponent.pillars.government - 5f);
                    player.pillars.diplomacy = Clamp(player.pillars.diplomacy + 4f);
                    summary = $"Settlement: {opponent.displayName} reverses the contested policy.";
                    break;
                default:
                    player.pillars.military = Clamp(player.pillars.military + 3f);
                    summary = $"Settlement: {opponent.displayName} stands down. Deterrence established.";
                    break;
            }

            player.governmentApproval = Clamp(player.governmentApproval + 6f);
            if (proposerId == state.playerCountryId)
            {
                ProgressionSystem.RecordInitiative(state);
                ProgressionSystem.AwardXP(state, 80, "Confrontation settled on our terms");
            }
            Close(state, confrontation, proposerId, true, summary);
            return true;
        }

        static void Close(GameState state, Confrontation confrontation, string proposerId,
            bool objectiveAchieved, string summary)
        {
            confrontation.resolved = true;
            confrontation.outcomeSummary = summary;

            // Both parties stand down.
            foreach (var id in new[] { confrontation.initiatorId, confrontation.defenderId })
            {
                var country = state.FindCountry(id);
                if (country == null) continue;

                // Stand down from a *wartime* alert only. Clearing the flag
                // outright also discarded a deliberately chosen Forward posture —
                // a skill-gated verb costing 2 CP to assume and real upkeep to
                // hold — with no notification, every time a war ended.
                if (country.military.posture == MilitaryPosture.Alert)
                    country.military.posture = MilitaryPosture.Peacetime;

                country.warExhaustion = Clamp(country.warExhaustion - 10f);
            }

            bool playerInvolved = confrontation.Involves(state.playerCountryId);
            state.AddNotification(
                playerInvolved ? NotificationClass.Priority : NotificationClass.Wire,
                objectiveAchieved ? "CONFRONTATION SETTLED" : "CONFRONTATION CONCEDED",
                summary, proposerId, desk: ReportingDesk.Military);
            state.AddChronicle(ChronicleCategory.Diplomatic, proposerId, summary, Publicity.Public);
            GameLog.Info("CONFRONT", summary);
        }

        public static string ObjectiveText(GameState state, Confrontation confrontation)
        {
            switch (confrontation.objective)
            {
                case ConfrontationObjective.TerritorialConcession:
                    return $"Secure {state.FindLocation(confrontation.objectiveLocationId)?.displayName ?? "territory"}";
                case ConfrontationObjective.ResourceAccess: return "Secure resource access";
                case ConfrontationObjective.PolicyReversal: return "Force policy reversal";
                default: return "Establish deterrence";
            }
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
