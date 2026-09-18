using System;
using System.Collections.Generic;
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
            if (!CanOpenAgainst(state, initiatorId, defenderId, objective, objectiveLocationId, out string blocked))
            {
                GameLog.Warn("CONFRONT", blocked);
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
        /// <summary>
        /// Whether a confrontation against *this* state over *this* objective may
        /// open at all: a settled pair is under truce for
        /// <see cref="SettlementTruceMonths"/>, and a territorial demand must name
        /// ground the defender actually holds — a war with Russia over a place
        /// Brazil has since taken is not a war with Russia.
        /// </summary>
        public static bool CanOpenAgainst(GameState state, string initiatorId, string defenderId,
            ConfrontationObjective objective, string objectiveLocationId, out string reason)
        {
            reason = "";
            var relationship = state.FindRelationship(initiatorId, defenderId);
            if (relationship != null && relationship.settlementTruceMonths > 0)
            {
                reason = $"A settlement with {state.FindCountry(defenderId)?.displayName ?? defenderId} " +
                         $"binds us for {relationship.settlementTruceMonths} more month(s).";
                return false;
            }
            if (objective == ConfrontationObjective.TerritorialConcession && !string.IsNullOrEmpty(objectiveLocationId))
            {
                var location = state.FindLocation(objectiveLocationId);
                if (location != null && location.ownerId != defenderId)
                {
                    reason = $"{location.displayName} is not held by " +
                             $"{state.FindCountry(defenderId)?.displayName ?? defenderId}.";
                    return false;
                }
            }
            return true;
        }

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

            if (!CanOpenAgainst(state, initiatorId, defenderId, objective, objectiveLocationId, out string blocked))
            {
                if (initiatorId == state.playerCountryId) GameLog.Warn("CONFRONT", blocked);
                return null;
            }

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

            // A détente does not survive a declaration of war.
            var pairRelationship = state.FindRelationship(initiatorId, defenderId);
            if (pairRelationship != null) pairRelationship.sanctionsTruceMonths = 0;

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
        /// Enter a war because a defence commitment was honoured (GDD §15.2).
        ///
        /// **Deliberately bypasses `CanOpenAnother` and the settlement truce.**
        /// Those gates exist to stop a state *choosing* more war than it can
        /// fight; they have no business refusing a war somebody else has started.
        /// A `MaxCommitment` ceiling that can block an alliance call-in would make
        /// the game forbid the operator from keeping their word — a refusal they
        /// could not see, which this project has already learned reads as a broken
        /// control rather than as a rule.
        ///
        /// The cost of a wider war is still real; it is simply priced rather than
        /// prohibited. `TheatreSystem.FocusFactor` drags every operation by how
        /// much of the force is committed elsewhere, so a state that honours three
        /// pacts at once fights badly on all three fronts. That is the same
        /// "priced, never gated" rule escalation and geography already follow.
        ///
        /// Opens at Limited Conflict because that is what it is: honouring a
        /// mutual defence commitment is entering a war already being fought, not
        /// opening a period of tension.
        /// </summary>
        public static Confrontation BeginObligationBy(GameState state, string allyId,
            string aggressorId, string onBehalfOfId, string rootConfrontationId = null)
        {
            if (allyId == aggressorId) return null;

            // Already fighting them: the obligation is discharged by the war we
            // are in. There is no second war between the same pair.
            var existing = ExistingBetween(state, allyId, aggressorId);
            if (existing != null) return existing;

            var ally = state.FindCountry(allyId);
            var aggressor = state.FindCountry(aggressorId);
            if (ally == null || aggressor == null) return null;

            var confrontation = new Confrontation
            {
                id = $"CONF_{state.date.SortKey}_{state.confrontations.Count}",
                initiatorId = allyId,
                defenderId = aggressorId,
                objective = ConfrontationObjective.Deterrence,
                objectiveLocationId = "",
                primaryStrategy = PrimaryStrategy.Military,
                escalation = EscalationState.LimitedConflict,
                startDate = state.date,
                theatre = TheatreSystem.Of(aggressorId),
                // A satellite of the war it was joined for: it closes with it,
                // and the cascade reads it to tell a defensive call from an
                // offensive one.
                obligationRootId = rootConfrontationId ?? "",
                obligationOnBehalfOfId = onBehalfOfId ?? ""
            };
            state.confrontations.Add(confrontation);

            // A sanctions truce does not survive a shooting war. The *settlement*
            // truce is left standing: it stops this pair choosing a new war with
            // each other, which is not what this is, and erasing it meant two
            // states that had settled last year were put back at war with no
            // memory of having made peace.
            var pair = state.FindRelationship(allyId, aggressorId);
            if (pair != null) pair.sanctionsTruceMonths = 0;

            ally.military.alertPosture = true;

            var onBehalfOf = state.FindCountry(onBehalfOfId);
            string because = onBehalfOf == null ? "" : $" in defence of {onBehalfOf.displayName}";

            if (aggressorId == state.playerCountryId)
                state.AddNotification(NotificationClass.Flash, "A NEW BELLIGERENT AGAINST US",
                    $"{ally.displayName} has entered the war against us{because}. "
                    + "We are now fighting on another front.", allyId, desk: ReportingDesk.Military);
            else if (allyId == state.playerCountryId)
                state.AddNotification(NotificationClass.Priority, "WE ARE AT WAR",
                    $"Honouring our commitment{because} has put us at war with "
                    + $"{aggressor.displayName}. The front is open and orders may be given.",
                    aggressorId, desk: ReportingDesk.Military);
            else
                state.AddNotification(NotificationClass.Wire, "THE WAR WIDENS",
                    $"{ally.displayName} enters the war against {aggressor.displayName}{because}.",
                    allyId, desk: ReportingDesk.Military);

            state.AddChronicle(ChronicleCategory.Military, allyId,
                $"Entered the war against {aggressor.displayName}{because}.", Publicity.Public);
            GameLog.Info("CONFRONT", $"{allyId} enters war vs {aggressorId} on obligation.");

            // And this is itself an attack, so whoever guaranteed the aggressor is
            // now being asked the same question. This is the cascade: each entry
            // creates a newly-attacked party, and that party's guarantors answer
            // for themselves. It is how a pact between three states and a pact
            // between three others becomes one war between six.
            AllianceSystem.InvokeObligations(state, confrontation);

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

                // **Breaking an arms-control agreement** (spec 04 §5f). Priced,
                // never blocked — §18.1's rule that escalation states must not
                // hard-gate what an operator may do. Going to open conflict with
                // a partner we signed a cap with is a treaty violation, and it
                // is treated as one: the agreement breaks, and everyone watching
                // adjusts what our signature is worth.
                if (target >= EscalationState.LimitedConflict)
                {
                    string other = confrontation.initiatorId == player.id
                        ? confrontation.defenderId : confrontation.initiatorId;
                    var pact = state.FindTreaty(player.id, other);
                    if (pact != null && !pact.broken
                        && pact.Carries(state, player.id, TreatyCommitment.ArmsControl))
                    {
                        DiplomacySystem.BreakTreatyBy(state, player.id, other);
                        state.AddNotification(NotificationClass.Priority, "ARMS CONTROL BROKEN",
                            "Opening hostilities against a state we signed a limitation with "
                            + "has voided the agreement, and everyone watching noticed.",
                            other, desk: ReportingDesk.Diplomacy);
                    }
                }

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
            if (!WithinEscalationLimit(confrontation, operationType, directive))
            {
                GameLog.Warn("CONFRONT", "The order's escalation limit forbids the conflict this would open.");
                return null;
            }

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

        /// <summary>
        /// The coalition weight an operation would draw, exactly as the launch
        /// path computes it.
        ///
        /// Extracted so a *preview* of the odds and the resolution that follows
        /// it cannot disagree — the same reasoning that made `ComputePowers`
        /// one function shared by the minister's advice and the outcome. The
        /// defensive-programme panel forecasts every verb before it is ordered,
        /// and forecasting with no partners while resolving with them would make
        /// the printed percentage quietly wrong during a war.
        /// </summary>
        public static float CoalitionSupportFor(GameState state, Confrontation confrontation,
            string actorId, OperationType operationType)
            => confrontation == null
                ? 0f
                : DiplomacySystem.CoalitionStrength(state, confrontation, actorId, operationType);

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
                if (!WithinEscalationLimit(confrontation, operationType, directive)) return null;
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
            float coalitionSupport = CoalitionSupportFor(state, confrontation, attackerId, operationType);

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
                // ours to have — and it is only *our* wire if we have a stake:
                // a partner, a coalition, ground we hold nearby, or a network
                // watching. Every operation on earth used to arrive here (125 a
                // decade); the chronicle still records all of them.
                var owner = state.FindCountry(target.ownerId);
                bool ourStake = WorldWire.Watches(state, attackerId)
                                || WorldWire.Watches(state, target.ownerId)
                                || (owner != null && state.FindTreaty(state.playerCountryId, owner.id) != null);
                if (ourStake)
                    state.AddNotification(NotificationClass.Wire,
                        $"FOREIGN OPERATION — {target.displayName}", record.summary, attackerId,
                        desk: ReportingDesk.Military);
            }
            return record;
        }

        public static bool WithinEscalationLimit(Confrontation confrontation,
            OperationType operationType, OperationDirective directive)
        {
            bool escalatory = OperationCatalog.For(operationType)?.targeting != OperationTargeting.OwnGround
                              && operationType != OperationType.Withdraw;
            if (!escalatory || confrontation == null
                || confrontation.escalation >= EscalationState.LimitedConflict) return true;
            return directive != null && directive.escalationLimit >= EscalationState.LimitedConflict;
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
            if (confrontation.monthsUntilNextOffer > 0) confrontation.monthsUntilNextOffer--;

            var initiator = state.FindCountry(confrontation.initiatorId);
            var defender = state.FindCountry(confrontation.defenderId);

            if (confrontation.escalation >= EscalationState.LimitedConflict)
            {
                float drain = confrontation.escalation == EscalationState.TotalWar ? 2.2f : 1.1f;

                // A war that is being won sustains itself (2026-08). The monthly
                // bill used to be identical for the side taking ground and the
                // side losing it, so a decade of victories cost exactly what a
                // decade of defeats did and the military playstyle graded below
                // doing nothing while winning 15–1. The side with clear momentum
                // tires at half the rate and its public does not turn on it.
                float initiatorFactor = confrontation.momentum >= WinningMomentum ? WinningDrainFactor : 1f;
                float defenderFactor = confrontation.momentum <= -WinningMomentum ? WinningDrainFactor : 1f;

                confrontation.initiatorWarExhaustion += drain * initiatorFactor;
                confrontation.defenderWarExhaustion += drain * defenderFactor;
                Causal.Apply(state, initiator.id, CausalMetric.WarExhaustion,
                    CausalReason.ActiveFighting, ref initiator.warExhaustion,
                    Clamp(initiator.warExhaustion + drain * 0.5f * initiatorFactor),
                    CausalCategory.Military, CausalKind.Direct, CausalVisibility.Known, defender.id);
                Causal.Apply(state, defender.id, CausalMetric.WarExhaustion,
                    CausalReason.ActiveFighting, ref defender.warExhaustion,
                    Clamp(defender.warExhaustion + drain * 0.5f * defenderFactor),
                    CausalCategory.Military, CausalKind.Direct, CausalVisibility.Known, initiator.id);

                // Long wars erode support and approval (GDD §12: historical memory).
                if (initiatorFactor >= 1f)
                {
                    initiator.warSupport = Clamp(initiator.warSupport - drain * 0.7f);
                    initiator.governmentApproval = Clamp(initiator.governmentApproval - drain * 0.35f);
                }
                if (defenderFactor >= 1f)
                    defender.warSupport = Clamp(defender.warSupport - drain * 0.5f);

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
            // A government choosing to signal is a public act, so the signal is
            // theirs to send — but what they would actually sign is a matter for
            // our reporting, and the wording must not promise otherwise.
            state.AddNotification(NotificationClass.Priority, "OPPONENT SIGNALS TERMS",
                $"{opponent.displayName} is signalling readiness to negotiate a settlement. " +
                "Open MILITARY and scroll to NEGOTIATED SETTLEMENT to put terms to them; " +
                "our staff's recommendation there is only as good as our reporting on their politics.",
                opponent.id, desk: ReportingDesk.Military);
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

            // A standing public finding against them is real weight at the table.
            // Without this a carried condemnation would be a line in the
            // chronicle and nothing else — the "written but never read" bug with
            // a gavel.
            if (CouncilSystem.IsCensured(state, opponent.id)) isolation += 8f;

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

            // A foreign government's terms reach the operator as a decision,
            // never as a fait accompli.
            if (opponent.isPlayer && proposerId != state.playerCountryId)
            {
                OfferTermsToPlayer(state, confrontation, proposerId, null);
                return false;
            }

            if (!WouldAcceptTermsFrom(state, confrontation, proposerId))
            {
                state.AddNotification(NotificationClass.Advisory, "TERMS REJECTED",
                    $"{opponent.displayName} rejects the proposed settlement. The confrontation continues.",
                    opponent.id, desk: ReportingDesk.Military);
                GameLog.Info("CONFRONT", "Settlement rejected; opponent still prefers resistance.");
                return false;
            }

            Settle(state, confrontation, proposerId);
            return true;
        }

        /// <summary>
        /// Apply accepted terms. **The objective belongs to the initiator.** A
        /// settlement proposed by the side that made the demand delivers the
        /// demand; one proposed by the side that was demanded of is a withdrawal
        /// of the claim and delivers nothing.
        ///
        /// This used to hand the objective to whoever *proposed* — so a defender
        /// suing for peace "ceded" ground it already owned (recorded as "United
        /// States cedes Western Siberian Fields"), and a war with Russia over a
        /// location Brazil had since taken transferred Brazil's title to the
        /// proposer. A cession now also requires that the ground is actually held
        /// by one of the two parties.
        /// </summary>
        static void Settle(GameState state, Confrontation confrontation, string proposerId)
        {
            var proposer = state.FindCountry(proposerId);
            var opponent = state.FindCountry(confrontation.OpponentOf(proposerId));
            bool proposerIsClaimant = proposerId == confrontation.initiatorId;

            string summary;
            if (!proposerIsClaimant)
            {
                // The defender's terms are the status quo. The initiator accepted
                // them, so the claim is withdrawn and nothing changes hands.
                summary = $"Settlement: {opponent.displayName} withdraws its claim against " +
                          $"{proposer.displayName}. Nothing changes hands.";
            }
            else
            {
                switch (confrontation.objective)
                {
                    case ConfrontationObjective.TerritorialConcession:
                    {
                        var loc = state.FindLocation(confrontation.objectiveLocationId);
                        if (loc != null && (loc.ownerId == opponent.id || loc.ownerId == proposer.id))
                        {
                            // A settled cession is recognised title, not occupation.
                            TerritorySystem.Cede(state, loc, proposerId);
                            summary = $"Settlement: {opponent.displayName} cedes {loc.displayName}.";
                        }
                        else if (loc != null)
                        {
                            var holder = state.FindCountry(loc.ownerId);
                            summary = $"Settlement: {opponent.displayName} renounces {loc.displayName}, " +
                                      $"but it is held by {holder?.displayName ?? loc.ownerId}. Nothing changes hands.";
                        }
                        else summary = $"Settlement: {opponent.displayName} accepts our terms.";
                        proposer.pillars.diplomacy = Clamp(proposer.pillars.diplomacy - 3f);
                        break;
                    }
                    case ConfrontationObjective.ResourceAccess:
                        proposer.resources.strategicMaterials = Clamp(proposer.resources.strategicMaterials + 12f);
                        proposer.resources.energy = Clamp(proposer.resources.energy + 8f);
                        summary = $"Settlement: resource access secured from {opponent.displayName}.";
                        break;
                    case ConfrontationObjective.PolicyReversal:
                        opponent.pillars.government = Clamp(opponent.pillars.government - 5f);
                        proposer.pillars.diplomacy = Clamp(proposer.pillars.diplomacy + 4f);
                        summary = $"Settlement: {opponent.displayName} reverses the contested policy.";
                        break;
                    default:
                        proposer.pillars.military = Clamp(proposer.pillars.military + 3f);
                        summary = $"Settlement: {opponent.displayName} stands down. Deterrence established.";
                        break;
                }
            }

            proposer.governmentApproval = Clamp(proposer.governmentApproval + 6f);
            if (proposerId == state.playerCountryId)
            {
                ProgressionSystem.RecordInitiative(state);
                ProgressionSystem.AwardXP(state, 80, "Confrontation settled on our terms");
            }
            Close(state, confrontation, proposerId, proposerIsClaimant, summary);
        }

        // ---------- terms offered to the player ----------

        /// <summary>Crisis definition id used when a foreign government offers the player terms.</summary>
        public const string TermsOfferedCrisisId = "TERMS_OFFERED";

        /// <summary>Months a refused offer waits before the same government asks again.</summary>
        public const int OfferCooldownMonths = 6;

        /// <summary>
        /// Months after any settlement before the same two states can open a new
        /// confrontation against each other. Without it a settlement bound
        /// nobody: the harness re-declared the same war eighteen times in a
        /// decade, one month apart, each ended by the same offer.
        /// </summary>
        public const int SettlementTruceMonths = 24;

        /// <summary>
        /// A foreign government's terms arrive as a decision, not as a fait
        /// accompli. `ProposeSettlementBy` used to close the player's war on the
        /// player's behalf whenever their *computed* willingness cleared the bar
        /// — a peace imposed without a decision, which the design forbids for a
        /// war (§18.1) and should forbid for its ending too.
        /// </summary>
        /// <summary>Raise a player decision containing the exact package offered.</summary>
        public static bool OfferConstructedTermsToPlayer(GameState state,
            Confrontation confrontation, string proposerId, PeaceProposal proposal)
            => OfferTermsToPlayer(state, confrontation, proposerId, proposal);

        static bool OfferTermsToPlayer(GameState state, Confrontation confrontation,
            string proposerId, PeaceProposal proposal)
        {
            if (confrontation.monthsUntilNextOffer > 0) return false;
            foreach (var open in state.activeCrises)
                if (open.defId == TermsOfferedCrisisId && open.subjectCountryId == proposerId) return false;

            var proposer = state.FindCountry(proposerId);
            bool proposerIsClaimant = proposerId == confrontation.initiatorId;
            string objective = ObjectiveText(state, confrontation);
            bool constructed = proposal != null && proposal.terms.Count > 0;
            bool demandsConcession = proposerIsClaimant;
            if (constructed)
                foreach (var term in proposal.terms)
                    if (PeaceSystem.IsDemand(term)) { demandsConcession = true; break; }
            string package = constructed
                ? string.Join(", ", proposal.terms.ConvertAll(PeaceSystem.DescribeReceived))
                : "";

            var crisis = new ActiveCrisis
            {
                defId = TermsOfferedCrisisId,
                subjectCountryId = proposerId,
                contextId = confrontation.id,
                title = "TERMS OFFERED",
                body = constructed
                    ? $"{proposer?.displayName} offers a settlement: {package}."
                    : proposerIsClaimant
                        ? $"{proposer?.displayName} offers to end the confrontation if we concede its objective: {objective}."
                        : $"{proposer?.displayName} offers to end the confrontation on the status quo. We would withdraw our claim: {objective}.",
                startDate = state.date,
                options = new List<CrisisOption>
                {
                    new CrisisOption
                    {
                        label = "ACCEPT THE TERMS",
                        description = constructed
                            ? "The war ends and the listed terms are applied exactly."
                            : proposerIsClaimant
                                ? "The war ends and they get what they demanded. Giving way costs standing."
                                : "The war ends and nothing changes hands.",
                        resultText = $"Terms accepted. The confrontation with {proposer?.displayName} is over.",
                        // Yielding to a demand is a concession and priced like
                        // one (see the concede path); accepting a status-quo
                        // offer is not.
                        approvalDelta = demandsConcession ? -6f : 0f,
                        stabilityDelta = demandsConcession ? -2f : 0f
                    },
                    new CrisisOption
                    {
                        label = "REFUSE",
                        description = "The confrontation continues. They will not ask again for some months.",
                        resultText = $"Terms refused. {proposer?.displayName} will not ask again soon."
                    }
                }
            };
            if (constructed) crisis.offeredPeaceTerms.AddRange(proposal.terms);

            state.activeCrises.Add(crisis);
            state.crisesFacedThisYear++;
            confrontation.monthsUntilNextOffer = OfferCooldownMonths;
            state.AddNotification(NotificationClass.Flash, crisis.title,
                "Immediate decision required. End Month is suspended.", proposerId);
            GameLog.Info("CONFRONT", $"{proposerId} offers the player terms.");
            return true;
        }

        /// <summary>Apply the player's answer to an offer. Called by <see cref="CrisisSystem"/>.</summary>
        public static void ApplyOfferDecision(GameState state, ActiveCrisis crisis, bool accepted)
        {
            string proposerId = crisis.subjectCountryId;
            var confrontation = !string.IsNullOrEmpty(crisis.contextId)
                ? state.FindConfrontation(crisis.contextId)
                : ExistingBetween(state, state.playerCountryId, proposerId);
            if (confrontation == null) return;

            if (!accepted)
            {
                confrontation.monthsUntilNextOffer = OfferCooldownMonths;
                GameLog.Info("CONFRONT", "Player refused offered terms.");
                return;
            }
            if (crisis.offeredPeaceTerms != null && crisis.offeredPeaceTerms.Count > 0)
            {
                var proposal = new PeaceProposal();
                proposal.terms.AddRange(crisis.offeredPeaceTerms);
                PeaceSystem.AcceptOfferedTerms(state, confrontation, proposerId, proposal,
                    CausalCategory.PlayerDecision, nameof(GameController.ResolveCrisis));
            }
            else Settle(state, confrontation, proposerId);
        }

        /// <summary>
        /// Who won, and why — measured from the world at the moment the war ends.
        ///
        /// The rules are checked in priority order, and the order *is* the design:
        ///
        /// 1. **Conquest overrides the stated objective.** A war opened to reduce
        ///    a rival's army that finishes with every one of their locations in
        ///    our hands is a victory, whatever the paperwork said. This is the
        ///    case an objective-only test gets wrong.
        /// 2. **The declared objective.** What the operator said they wanted
        ///    before anyone fired, which is the honest test for most wars.
        /// 3. **The balance of the terms.** A settlement where one side gave up
        ///    materially more than it got is a defeat even if nothing was taken.
        /// 4. **The balance of damage.** When nothing else separates them, the
        ///    side that was hurt far worse relative to its size lost.
        /// 5. **Stalemate.** The default, and a real answer.
        ///
        /// Casualties are compared *relative to the force that took them*, or a
        /// large power would be judged the loser of every war it fought simply for
        /// having more people to lose.
        /// </summary>
        public static WarVerdict DetermineVerdict(GameState state, Confrontation confrontation,
            out string reason)
        {
            var initiator = state.FindCountry(confrontation.initiatorId);
            var defender = state.FindCountry(confrontation.defenderId);

            // ---- 1. total conquest overrides everything ----
            if (HoldsAllGroundOf(state, confrontation.initiatorId, confrontation.defenderId))
            {
                reason = $"Every location {defender?.displayName} held is in our hands.";
                return WarVerdict.InitiatorVictory;
            }
            if (HoldsAllGroundOf(state, confrontation.defenderId, confrontation.initiatorId))
            {
                reason = $"{defender?.displayName} holds every location we had.";
                return WarVerdict.DefenderVictory;
            }

            // ---- 2. the objective that was declared ----
            if (confrontation.objective == ConfrontationObjective.TerritorialConcession
                && !string.IsNullOrEmpty(confrontation.objectiveLocationId))
            {
                var prize = state.FindLocation(confrontation.objectiveLocationId);
                if (prize != null)
                {
                    if (prize.ownerId == confrontation.initiatorId)
                    {
                        reason = $"{prize.displayName} was the objective, and we hold it.";
                        return WarVerdict.InitiatorVictory;
                    }
                    // Holding what was demanded of you is winning a defensive war.
                    if (prize.ownerId == confrontation.defenderId)
                    {
                        reason = $"{prize.displayName} was demanded of us and we still hold it.";
                        return WarVerdict.DefenderVictory;
                    }
                }
            }

            // ---- 3. what the terms cost each side ----
            float termBalance = SettlementBalanceFor(state, confrontation);
            if (Math.Abs(termBalance) >= 2f)
            {
                reason = termBalance > 0f
                    ? "The settlement took more from them than it cost us."
                    : "The settlement cost us more than it took from them.";
                return termBalance > 0f ? WarVerdict.InitiatorVictory : WarVerdict.DefenderVictory;
            }

            // ---- 4. who was hurt worse, relative to their size ----
            float initiatorHurt = RelativeHarm(initiator, confrontation.initiatorCasualties,
                confrontation.initiatorWarExhaustion);
            float defenderHurt = RelativeHarm(defender, confrontation.defenderCasualties,
                confrontation.defenderWarExhaustion);

            if (Math.Abs(initiatorHurt - defenderHurt) >= 0.35f)
            {
                bool weFaredBetter = initiatorHurt < defenderHurt;
                reason = weFaredBetter
                    ? "Neither objective was met, but they were hurt far worse than we were."
                    : "Neither objective was met, and we were hurt far worse than they were.";
                return weFaredBetter ? WarVerdict.InitiatorVictory : WarVerdict.DefenderVictory;
            }

            reason = "Nobody achieved what they set out to. The war settled nothing.";
            return WarVerdict.Stalemate;
        }

        /// <summary>
        /// Post the result to both countries' records.
        ///
        /// Only escalations that actually became fighting count. A standoff that
        /// stayed at Tension and was talked down is not a war anybody won, and
        /// counting it would make the record a tally of diplomatic incidents.
        /// </summary>
        static void RecordWarResult(GameState state, Confrontation confrontation)
        {
            if (confrontation.escalation < EscalationState.LimitedConflict) return;

            var initiator = state.FindCountry(confrontation.initiatorId);
            var defender = state.FindCountry(confrontation.defenderId);

            switch (confrontation.verdict)
            {
                case WarVerdict.InitiatorVictory:
                    if (initiator != null) { initiator.warsWon++; VictoryDividend(state, initiator); }
                    if (defender != null) { defender.warsLost++; DefeatBill(defender); }
                    break;
                case WarVerdict.DefenderVictory:
                    if (defender != null) { defender.warsWon++; VictoryDividend(state, defender); }
                    if (initiator != null) { initiator.warsLost++; DefeatBill(initiator); }
                    break;
                default:
                    if (initiator != null) initiator.warsDrawn++;
                    if (defender != null) defender.warsDrawn++;
                    break;
            }
        }

        /// <summary>
        /// What winning a war is worth at home. Until the 2026-08 playtest a
        /// verdict changed a counter and nothing else: the winner had paid every
        /// month of exhaustion, approval and treasury the war cost and got no
        /// rally for it, so across 888 measured decades the military playstyle —
        /// the only one that ever gains ground — graded at or below doing
        /// nothing. A won war now lifts the public's mood and confidence in the
        /// government and lets the country breathe; a lost one deepens the
        /// wound. Both are one-off store writes, not targets, because a verdict
        /// is an event.
        /// </summary>
        /// <summary>Momentum at which a side is clearly winning and its monthly war bill eases.</summary>
        public const float WinningMomentum = 25f;
        /// <summary>Exhaustion multiplier for the winning side; its support and approval stop draining.</summary>
        public const float WinningDrainFactor = 0.5f;

        public const float VictoryApproval = 8f, VictoryUnity = 5f, VictoryWarSupport = 12f,
            VictoryStability = 3f, VictoryExhaustionRelief = 15f;
        public const float DefeatApproval = 5f, DefeatWarSupport = 8f, DefeatUnity = 3f;

        static void VictoryDividend(GameState state, CountryState country)
        {
            country.governmentApproval = Clamp(country.governmentApproval + VictoryApproval);
            country.nationalUnity = Clamp(country.nationalUnity + VictoryUnity);
            country.warSupport = Clamp(country.warSupport + VictoryWarSupport);
            country.stability = Clamp(country.stability + VictoryStability);
            Causal.Apply(state, country.id, CausalMetric.WarExhaustion,
                CausalReason.Victory, ref country.warExhaustion,
                Clamp(country.warExhaustion - VictoryExhaustionRelief), CausalCategory.Military);
            country.pillars.military = Growth.Apply(country.pillars.military, 2f);
        }

        static void DefeatBill(CountryState country)
        {
            country.governmentApproval = Clamp(country.governmentApproval - DefeatApproval);
            country.warSupport = Clamp(country.warSupport - DefeatWarSupport);
            country.nationalUnity = Clamp(country.nationalUnity - DefeatUnity);
        }

        /// <summary>Does <paramref name="holderId"/> control every location that is originally <paramref name="ownerId"/>'s?</summary>
        static bool HoldsAllGroundOf(GameState state, string holderId, string ownerId)
        {
            bool any = false;
            foreach (var location in state.locations)
            {
                if (location.originalOwnerId != ownerId) continue;
                any = true;
                if (location.ownerId != holderId) return false;
            }
            return any;
        }

        /// <summary>
        /// How much of what was agreed fell on each side. Positive favours the
        /// initiator. Zero when the war ended without terms.
        /// </summary>
        static float SettlementBalanceFor(GameState state, Confrontation confrontation)
        {
            float balance = 0f;
            foreach (var settlement in state.settlements)
            {
                if (settlement.confrontationId != confrontation.id) continue;

                // A term is either something the proposer extracted or something
                // they gave up — the enum already draws that line, so the balance
                // is just the difference, oriented to the initiator.
                float forProposer = 0f;
                foreach (var term in settlement.terms)
                    forProposer += PeaceSystem.IsDemand(term) ? 1f : -1f;

                balance += settlement.proposerId == confrontation.initiatorId
                    ? forProposer
                    : -forProposer;
            }
            return balance;
        }

        /// <summary>
        /// Damage taken as a share of what the country could absorb. Relative,
        /// because an absolute casualty comparison declares the larger power the
        /// loser of every war it fights.
        /// </summary>
        static float RelativeHarm(CountryState country, float casualties, float exhaustion)
        {
            if (country == null) return 0f;
            float force = Math.Max(1f, country.military.TotalPower * MilitarySystem.PowerScale);
            return casualties / force + exhaustion / 100f;
        }

        static void Close(GameState state, Confrontation confrontation, string proposerId,
            bool objectiveAchieved, string summary)
        {
            Finish(state, confrontation, proposerId, objectiveAchieved, summary);

            // **A guarantor's war ends with the war it was joined for.** A front
            // opened by honouring a guarantee has no objective of its own — it
            // exists to defend an ally — so once that ally has settled, the
            // satellite front is fighting for nothing. Before this, obligation
            // fronts carried Deterrence and no location, were never the war the
            // AI managed (it manages the first in the list), and so had no exit
            // at all: they drained exhaustion and treasury on both sides for the
            // rest of the save. Measured: 21 of 22 AI wars in a world were these.
            if (confrontation.IsObligationEntry) return;
            for (int i = 0; i < state.confrontations.Count; i++)
            {
                var satellite = state.confrontations[i];
                if (satellite.resolved || satellite.obligationRootId != confrontation.id) continue;

                var ally = state.FindCountry(satellite.initiatorId);
                var behalf = state.FindCountry(satellite.obligationOnBehalfOfId);
                string text = $"The war {ally?.displayName ?? satellite.initiatorId} joined" +
                              (behalf != null ? $" in defence of {behalf.displayName}" : "") +
                              " has ended; the front closes with it.";
                Finish(state, satellite, satellite.initiatorId, false, text);
            }
        }

        static void Finish(GameState state, Confrontation confrontation, string proposerId,
            bool objectiveAchieved, string summary)
        {
            confrontation.resolved = true;
            confrontation.outcomeSummary = summary;

            // A settlement binds both parties for a while (see SettlementTruceMonths).
            var pair = state.FindRelationship(confrontation.initiatorId, confrontation.defenderId);
            if (pair != null) pair.settlementTruceMonths = SettlementTruceMonths;

            // Judged from the world, not from which path closed the war.
            confrontation.verdict = DetermineVerdict(state, confrontation, out string reason);
            confrontation.verdictReason = reason;
            RecordWarResult(state, confrontation);

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

                Causal.Apply(state, country.id, CausalMetric.WarExhaustion,
                    CausalReason.SettlementRelief, ref country.warExhaustion,
                    Clamp(country.warExhaustion - 10f), CausalCategory.Military);
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
