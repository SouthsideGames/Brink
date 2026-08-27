using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Alliance obligations (GDD §15.2).
    ///
    /// A mutual defense commitment is only meaningful if it is actually called
    /// upon. When a confrontation reaches Limited Conflict, every state that
    /// guaranteed the defender must decide: honor the commitment and join the
    /// war, or repudiate it and take the consequences. Nobody is forced — but
    /// "commitments may be broken, and trustworthiness and future diplomacy
    /// suffer."
    ///
    /// Three things changed here in 2026-08 (user decision), and they are the
    /// substance of this file:
    ///
    /// 1. **A guarantee can come from a bloc, not only a bilateral treaty.**
    ///    <see cref="GuarantorsOf"/> is the one definition of "who is obliged to
    ///    defend this state", and it reads both. Building an alliance out of
    ///    `Treaty` alone meant N² agreements against a `PactAnxiety` term that
    ///    penalises stacking them.
    ///
    /// 2. **Honouring opens a real war.** It used to add the ally to a coalition
    ///    — a strength multiplier on somebody else's defence — so answering a
    ///    treaty call produced no front, no objective, no orders, and nothing on
    ///    the MILITARY screen. The operator was told they had entered a war they
    ///    could not fight. Now <see cref="ConfrontationSystem.BeginObligationBy"/>
    ///    opens a confrontation *and* the coalition, so the alliance both fights
    ///    and coordinates.
    ///
    /// 3. **It cascades.** `InvokeObligations` used to walk only the defender's
    ///    treaties, so an aggressor's own alliance was never called and a war
    ///    between two blocs was impossible by construction. Now each entry is
    ///    itself an attack, and the newly-attacked party's guarantors answer for
    ///    themselves — which is how three states and three states become one war
    ///    between six, each government having decided for its own reasons.
    ///
    /// The cascade terminates because a pair may only have one confrontation
    /// between them and `obligationsInvoked` fires once per confrontation; the
    /// depth guard is belt and braces against an authoring mistake, not the
    /// mechanism.
    /// </summary>
    public static class AllianceSystem
    {
        /// <summary>Crisis definition id used when the player is the one called upon.</summary>
        public const string PlayerObligationCrisisId = "ALLIANCE_OBLIGATION";

        /// <summary>
        /// How far one attack may propagate through the alliance graph in a
        /// single tick. Four is past anything the authored world can reach with
        /// three blocs of a handful of states each.
        /// </summary>
        public const int MaxCascadeDepth = 4;

        static int cascadeDepth;

        /// <summary>
        /// Who is obliged to defend <paramref name="defenderId"/>, and by what.
        ///
        /// One definition, read by the call-in, by the UI and by the AI's own
        /// reasoning — because a guarantee the game honours and a guarantee the
        /// game displays must be the same guarantee.
        /// </summary>
        public static List<Guarantor> GuarantorsOf(GameState state, string defenderId,
            string aggressorId)
        {
            var found = new List<Guarantor>();
            if (state == null || string.IsNullOrEmpty(defenderId)) return found;

            // Bilateral pacts.
            foreach (var treaty in state.treaties)
            {
                if (treaty.broken || !treaty.Has(TreatyCommitment.MutualDefense)) continue;
                if (!treaty.Involves(defenderId)) continue;

                string ally = treaty.PartnerOf(defenderId);
                if (ally == aggressorId || ally == defenderId) continue;
                if (Contains(found, ally)) continue;

                found.Add(new Guarantor
                {
                    countryId = ally,
                    sourceName = "our treaty with them",
                    fromBloc = false
                });
            }

            // And the bloc, if it carries the commitment. A bloc obliges every
            // member to every other member — that is what makes it one alliance
            // rather than a list of states that happen to agree.
            var bloc = BlocSystem.BlocOf(state, defenderId);
            if (bloc != null && bloc.Guarantees(TreatyCommitment.MutualDefense))
            {
                foreach (string memberId in bloc.PartnersOf(defenderId))
                {
                    if (memberId == aggressorId) continue;

                    // A bloc guarantee is the stronger claim: it is public, it is
                    // owed to everybody at once, and walking away from it is seen
                    // by every other member. Where a state is bound both ways, the
                    // bloc is the one that is invoked.
                    for (int i = found.Count - 1; i >= 0; i--)
                        if (found[i].countryId == memberId) found.RemoveAt(i);

                    found.Add(new Guarantor
                    {
                        countryId = memberId,
                        sourceName = bloc.name,
                        fromBloc = true,
                        blocId = bloc.id
                    });
                }
            }

            return found;
        }

        /// <summary>Whether this obligation still stands, re-read from the world.</summary>
        static bool StillObliged(GameState state, Guarantor guarantor, string defenderId,
            string aggressorId)
        {
            foreach (var current in GuarantorsOf(state, defenderId, aggressorId))
                if (current.countryId == guarantor.countryId) return true;
            return false;
        }

        static bool Contains(List<Guarantor> list, string countryId)
        {
            foreach (var entry in list) if (entry.countryId == countryId) return true;
            return false;
        }

        /// <summary>One state's obligation to defend another, and where it comes from.</summary>
        public class Guarantor
        {
            public string countryId;
            public string sourceName = "";
            public bool fromBloc;
            public string blocId = "";
        }

        /// <summary>
        /// Called when a confrontation first reaches Limited Conflict, and again
        /// by <see cref="ConfrontationSystem.BeginObligationBy"/> for each state
        /// the widening war newly puts on the defensive.
        /// </summary>
        public static void InvokeObligations(GameState state, Confrontation confrontation)
        {
            if (confrontation == null || confrontation.resolved) return;
            if (confrontation.obligationsInvoked) return;
            confrontation.obligationsInvoked = true;

            if (cascadeDepth >= MaxCascadeDepth)
            {
                GameLog.Warn("ALLIANCE",
                    $"Cascade depth {MaxCascadeDepth} reached; obligations not propagated further.");
                return;
            }

            string defenderId = confrontation.defenderId;
            string aggressorId = confrontation.initiatorId;

            // Copy first: honoring opens confrontations and can modify state.
            var guarantors = GuarantorsOf(state, defenderId, aggressorId);
            if (guarantors.Count == 0) return;

            cascadeDepth++;
            try
            {
                foreach (var guarantor in guarantors)
                {
                    // Already fighting the aggressor: the obligation is discharged
                    // by the war they are in, and asking again would raise a crisis
                    // over a decision already taken.
                    if (ConfrontationSystem.ExistingBetween(state, guarantor.countryId, aggressorId) != null)
                        continue;

                    // The list was taken before the first answer. An earlier
                    // repudiation can expel somebody and take a two-member bloc
                    // down with them, and asking a state to honour an alliance
                    // that no longer exists is worse than not asking: it is a
                    // decision about nothing, and answering it would still cost.
                    if (!StillObliged(state, guarantor, defenderId, aggressorId)) continue;

                    if (guarantor.countryId == state.playerCountryId)
                        RaisePlayerObligation(state, confrontation, guarantor);
                    else
                        ResolveAiObligation(state, confrontation, guarantor);
                }
            }
            finally
            {
                cascadeDepth--;
            }
        }

        // ---------- the player's decision ----------

        static void RaisePlayerObligation(GameState state, Confrontation confrontation,
            Guarantor guarantor)
        {
            var defender = state.FindCountry(confrontation.defenderId);
            var aggressor = state.FindCountry(confrontation.initiatorId);

            // Say plainly what honouring will actually cost, because the whole
            // point of the cascade is that the answer is no longer obvious: the
            // aggressor has guarantors of their own, and they will be asked next.
            var theirSide = GuarantorsOf(state, confrontation.initiatorId, confrontation.defenderId);
            string warning = "";
            if (theirSide.Count > 0)
            {
                var names = new List<string>();
                foreach (var entry in theirSide)
                {
                    var country = state.FindCountry(entry.countryId);
                    if (country != null) names.Add(country.displayName);
                }
                if (names.Count > 0)
                    warning = $" {aggressor?.displayName} is itself guaranteed by "
                            + $"{string.Join(", ", names)}; entering this war will put the "
                            + "same question to them.";
            }

            var crisis = new ActiveCrisis
            {
                defId = PlayerObligationCrisisId,
                title = "ALLIANCE OBLIGATION INVOKED",
                body = $"{aggressor?.displayName} has opened hostilities against " +
                       $"{defender?.displayName}. Our commitment under {guarantor.sourceName} " +
                       $"has been invoked.{warning} The treaty is explicit. The decision is not.",
                startDate = state.date,
                subjectCountryId = confrontation.defenderId,
                contextId = confrontation.id,
                options = new List<CrisisOption>
                {
                    new CrisisOption
                    {
                        label = "HONOR THE COMMITMENT",
                        description = "Enter the war on their side. A real front opens against "
                                    + $"{aggressor?.displayName}.",
                        resultText = $"We have entered the conflict alongside {defender?.displayName}.",
                        approvalDelta = -4, stabilityDelta = -2
                    },
                    new CrisisOption
                    {
                        label = "REPUDIATE THE COMMITMENT",
                        description = "Stay out. Expect sanctions, cancelled trade, and a "
                                    + "signature nobody values.",
                        resultText = $"We have declined to act. {defender?.displayName} stands alone.",
                        approvalDelta = 2
                    }
                }
            };

            state.activeCrises.Add(crisis);
            state.crisesFacedThisYear++;
            state.AddNotification(NotificationClass.Flash, crisis.title,
                "Immediate decision required. End Month is suspended.", confrontation.defenderId);
            state.AddChronicle(ChronicleCategory.Diplomatic, state.playerCountryId,
                $"Defense obligation to {defender?.displayName} invoked.");
            GameLog.Warn("ALLIANCE", "Player defense obligation invoked.");
        }

        /// <summary>
        /// Apply the player's answer. Called by <see cref="CrisisSystem.Resolve"/>
        /// when the crisis is an alliance obligation.
        /// </summary>
        public static void ApplyPlayerDecision(GameState state, bool honored)
            => ApplyPlayerDecision(state, honored, null);

        /// <summary>
        /// Apply the player's answer to a specific invocation.
        ///
        /// The crisis carries the confrontation id because the cascade can put
        /// more than one obligation in front of the operator at once, and
        /// answering the second by scanning for the first is how an operator ends
        /// up in a war they did not agree to enter.
        /// </summary>
        public static void ApplyPlayerDecision(GameState state, bool honored, string confrontationId)
        {
            var confrontation = FindObligationConfrontation(state, confrontationId);
            if (confrontation == null) return;

            var guarantor = GuarantorFor(state, confrontation, state.playerCountryId);
            if (guarantor == null) return;

            if (honored) Honor(state, confrontation, guarantor);
            else Repudiate(state, confrontation, guarantor);
        }

        /// <summary>The war whose obligation is being answered.</summary>
        static Confrontation FindObligationConfrontation(GameState state, string confrontationId)
        {
            if (!string.IsNullOrEmpty(confrontationId))
            {
                foreach (var confrontation in state.confrontations)
                    if (confrontation.id == confrontationId && !confrontation.resolved)
                        return confrontation;
            }

            // Old saves carry no id on the crisis; fall back to the scan that
            // shipped before the cascade existed.
            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved || !confrontation.obligationsInvoked) continue;
                if (GuarantorFor(state, confrontation, state.playerCountryId) != null)
                    return confrontation;
            }
            return null;
        }

        /// <summary>This state's obligation in this war, or null if it has none.</summary>
        static Guarantor GuarantorFor(GameState state, Confrontation confrontation, string countryId)
        {
            foreach (var guarantor in GuarantorsOf(state, confrontation.defenderId, confrontation.initiatorId))
                if (guarantor.countryId == countryId) return guarantor;
            return null;
        }

        // ---------- AI decisions ----------

        static void ResolveAiObligation(GameState state, Confrontation confrontation, Guarantor guarantor)
        {
            if (HonorWillingness(state, confrontation, guarantor.countryId) >= 50f)
                Honor(state, confrontation, guarantor);
            else
                Repudiate(state, confrontation, guarantor);
        }

        /// <summary>
        /// How willing a signatory is to actually fight. Warmth and shared threat
        /// argue for honoring; exhaustion, instability and dependence on the
        /// aggressor argue for finding a reason not to.
        /// </summary>
        public static float HonorWillingness(GameState state, Confrontation confrontation, string allyId)
        {
            var ally = state.FindCountry(allyId);
            if (ally == null) return 0f;

            var toDefender = state.FindRelationship(allyId, confrontation.defenderId);
            var toAggressor = state.FindRelationship(allyId, confrontation.initiatorId);
            if (toDefender == null || toAggressor == null) return 0f;

            float willingness = 30f
                                + toDefender.relations * 0.35f
                                + toDefender.trust * 0.25f
                                + toDefender.interoperability * 0.15f
                                + toAggressor.ThreatPerceivedBy(allyId) * 0.30f
                                - toAggressor.DependenceOf(allyId) * 0.45f
                                - ally.warExhaustion * 0.35f
                                - Math.Max(0f, 55f - ally.stability) * 0.5f
                                - Math.Max(0f, 45f - ally.warSupport) * 0.3f;

            // An honorable state's own record weighs on the decision.
            willingness += toDefender.memoryWeight * 1.2f;

            // A public commitment to a named side is harder to walk away from
            // than a bilateral one: every other member is watching, and the
            // stronger the bloc believes in itself the more it costs to be the
            // one who would not come.
            var bloc = BlocSystem.BlocOf(state, allyId);
            if (bloc != null && bloc.Guarantees(TreatyCommitment.MutualDefense)
                && bloc.Has(confrontation.defenderId))
                willingness += 8f + bloc.cohesion * 0.18f;

            // What we are already carrying. A state fighting two wars is not
            // eager for a third, and this is where the multi-front pressure
            // reaches the diplomacy of it rather than only the fighting.
            willingness -= TheatreSystem.TotalCommitment(state, allyId) * 9f;

            return willingness;
        }

        // ---------- outcomes ----------

        static void Honor(GameState state, Confrontation confrontation, Guarantor guarantor)
        {
            string allyId = guarantor.countryId;
            var ally = state.FindCountry(allyId);
            var defender = state.FindCountry(confrontation.defenderId);
            if (ally == null) return;

            // Join the defender's coalition, creating it if this is the first ally.
            // The coalition is how the alliance *coordinates* — it feeds operation
            // resolution on the defender's front. The confrontation opened below is
            // how it *fights*. Both, because one without the other is either a war
            // nobody helps with or help in a war nobody is having.
            var coalition = state.FindCoalitionLedBy(confrontation.id, confrontation.defenderId);
            if (coalition == null)
            {
                coalition = new Coalition
                {
                    id = $"COAL_DEF_{confrontation.id}",
                    leaderId = confrontation.defenderId,
                    confrontationId = confrontation.id,
                    targetId = confrontation.initiatorId
                };
                coalition.memberIds.Add(confrontation.defenderId);
                state.coalitions.Add(coalition);
            }
            if (!coalition.memberIds.Contains(allyId)) coalition.memberIds.Add(allyId);

            ally.military.alertPosture = true;
            ally.warSupport = Clamp(ally.warSupport - 5f);

            var toDefender = state.FindRelationship(allyId, confrontation.defenderId);
            if (toDefender != null)
            {
                toDefender.relations = Clamp(toDefender.relations + 12f);
                toDefender.trust = Clamp(toDefender.trust + 15f);
                toDefender.AddMemory(state.date, "Honored defense commitment", 6f);
            }

            // Honouring in a bloc is seen by every member of it, not only by the
            // state that was defended.
            if (guarantor.fromBloc)
            {
                var bloc = BlocSystem.Find(state, guarantor.blocId);
                if (bloc != null)
                {
                    bloc.cohesion = Clamp(bloc.cohesion + 5f);
                    foreach (string memberId in bloc.PartnersOf(allyId))
                    {
                        if (memberId == confrontation.defenderId) continue;
                        var withMember = state.FindRelationship(allyId, memberId);
                        if (withMember == null) continue;
                        withMember.trust = Clamp(withMember.trust + 7f);
                        withMember.AddMemory(state.date, "Came when the bloc was called", 2f);
                    }
                }
            }

            var toAggressor = state.FindRelationship(allyId, confrontation.initiatorId);
            if (toAggressor != null)
            {
                toAggressor.relations = Clamp(toAggressor.relations - 30f);
                toAggressor.trust = Clamp(toAggressor.trust - 15f);
                toAggressor.AddMemory(state.date, "Entered war against us", -5f);
            }

            bool playerInvolved = allyId == state.playerCountryId
                                  || confrontation.Involves(state.playerCountryId);
            state.AddNotification(playerInvolved ? NotificationClass.Priority : NotificationClass.Wire,
                "ALLIANCE HONORED",
                $"{ally.displayName} enters the conflict alongside {defender?.displayName}.", allyId,
                desk: ReportingDesk.Diplomacy);
            state.AddChronicle(ChronicleCategory.Diplomatic, allyId,
                $"Honored defense commitment to {defender?.displayName}.", Publicity.Public);
            GameLog.Info("ALLIANCE", $"{allyId} honored its commitment to {confrontation.defenderId}.");

            // And now they are actually at war — a front they can fight on, not a
            // line in a notification. This is also what continues the cascade.
            ConfrontationSystem.BeginObligationBy(
                state, allyId, confrontation.initiatorId, confrontation.defenderId);
        }

        /// <summary>
        /// Walking away.
        ///
        /// **The cost is standing, never capability.** The old version charged
        /// `pillars.diplomacy -= 8`, which is the same mistake this project
        /// already fixed for intelligence exposure: a national capability hit has
        /// no recovery path for the operator who incurred it, so it functions as
        /// a slow disqualification rather than a price. Everything below is
        /// recoverable, and every part of it is something the world *does* rather
        /// than a number that quietly falls.
        /// </summary>
        static void Repudiate(GameState state, Confrontation confrontation, Guarantor guarantor)
        {
            string allyId = guarantor.countryId;
            var ally = state.FindCountry(allyId);
            var defender = state.FindCountry(confrontation.defenderId);
            if (ally == null) return;

            var treaty = state.FindTreaty(allyId, confrontation.defenderId);
            if (treaty != null && treaty.Has(TreatyCommitment.MutualDefense))
            {
                treaty.broken = true;
                treaty.brokenBy = allyId;
            }

            var toDefender = state.FindRelationship(allyId, confrontation.defenderId);
            if (toDefender != null)
            {
                toDefender.relations = Clamp(toDefender.relations - 35f);
                toDefender.trust = Clamp(toDefender.trust - 45f);
                toDefender.AddMemory(state.date, "Abandoned us when the treaty was invoked", -10f);
            }

            // Everyone recalculates what this state's signature is worth.
            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(allyId)) continue;
                if (relationship.Involves(confrontation.defenderId)) continue;
                relationship.trust = Clamp(relationship.trust - 12f);
                relationship.AddMemory(state.date, "Observed an abandoned defense commitment", -2.5f);
            }

            // The people who were counting on us answer for themselves.
            var abandoned = CollectAbandoned(state, confrontation, guarantor, allyId);
            PunishRepudiation(state, allyId, confrontation.defenderId, abandoned);

            // And a bloc puts them out. A member who would not come when the bloc
            // was called is not a member; leaving that unresolved would make the
            // guarantee mean nothing to everyone still in it.
            if (guarantor.fromBloc)
            {
                var bloc = BlocSystem.Find(state, guarantor.blocId);
                if (bloc != null)
                {
                    bloc.cohesion = Clamp(bloc.cohesion - 14f);
                    BlocSystem.Expel(state, bloc, allyId,
                        $"it would not answer the call for {defender?.displayName}");
                }
            }

            bool playerInvolved = allyId == state.playerCountryId
                                  || confrontation.Involves(state.playerCountryId);
            state.AddNotification(playerInvolved ? NotificationClass.Priority : NotificationClass.Wire,
                "ALLIANCE REPUDIATED",
                $"{ally.displayName} declines to honor its commitment to {defender?.displayName}. " +
                "Its guarantees are now discounted everywhere.", allyId, desk: ReportingDesk.Diplomacy);
            state.AddChronicle(ChronicleCategory.Diplomatic, allyId,
                $"Repudiated defense commitment to {defender?.displayName}.", Publicity.Public);
            GameLog.Warn("ALLIANCE", $"{allyId} repudiated its commitment to {confrontation.defenderId}.");
        }

        /// <summary>
        /// Everyone with standing to be angry: the state that was let down, and
        /// anybody who shared the guarantee with the state that walked away.
        /// </summary>
        static List<string> CollectAbandoned(GameState state, Confrontation confrontation,
            Guarantor guarantor, string allyId)
        {
            var angry = new List<string> { confrontation.defenderId };

            if (guarantor.fromBloc)
            {
                var bloc = BlocSystem.Find(state, guarantor.blocId);
                if (bloc != null)
                    foreach (string memberId in bloc.PartnersOf(allyId))
                        if (!angry.Contains(memberId)) angry.Add(memberId);
            }
            else
            {
                // Co-guarantors of the same state, bilaterally.
                foreach (var other in GuarantorsOf(state, confrontation.defenderId, confrontation.initiatorId))
                    if (other.countryId != allyId && !angry.Contains(other.countryId))
                        angry.Add(other.countryId);
            }

            angry.Remove(allyId);
            return angry;
        }

        /// <summary>
        /// What abandonment actually costs, beyond a relations number.
        ///
        /// Sanctions and cancelled trade, so the price is felt in the treasury
        /// and the sectors rather than only in a readout — and a grievance heavy
        /// enough that the AI's own rivalry reasoning can carry it, in its own
        /// time, to a confrontation. Nothing here is scripted revenge: it moves
        /// the inputs the world already reasons from.
        /// </summary>
        static void PunishRepudiation(GameState state, string allyId, string defenderId,
            List<string> abandoned)
        {
            var ally = state.FindCountry(allyId);
            if (ally == null) return;

            foreach (string angryId in abandoned)
            {
                var angry = state.FindCountry(angryId);
                if (angry == null || angryId == allyId) continue;

                var pair = state.FindRelationship(angryId, allyId);
                if (pair == null) continue;

                // How close they were to the state that was let down decides how
                // hard they take it. Somebody's own guarantor walking away is a
                // different matter from a distant member's disapproval.
                float closeness = angryId == defenderId ? 100f : ClosenessTo(state, angryId, defenderId);

                if (angryId != defenderId)
                {
                    pair.relations = Clamp(pair.relations - closeness * 0.12f);
                    pair.trust = Clamp(pair.trust - closeness * 0.14f);
                    pair.AddMemory(state.date, "Would not come when the alliance was called", -4f);
                }

                // A betrayed state stops seeing the abandoner as a partner and
                // starts seeing them as a problem. This is the term the AI's own
                // rivalry reasoning reads, so an abandoned ally can, in its own
                // time and on its own judgement, become an enemy.
                pair.SetThreatPerceivedBy(angryId,
                    Clamp(pair.ThreatPerceivedBy(angryId) + 10f + closeness * 0.10f));
                pair.strategicAlignment = Clamp(pair.strategicAlignment - 18f);

                // Sanctions, scaled by how personally they take it. A truce is not
                // a shield here — but `ImposeSanctionsBy` owns that rule, so this
                // simply asks and accepts the answer.
                var severity = closeness >= 70f ? SanctionSeverity.Coercive
                             : closeness >= 40f ? SanctionSeverity.Pressure
                             : SanctionSeverity.Routine;
                EconomySystem.ImposeSanctionsBy(state, angryId, allyId, severity);

                CancelPreferentialTrade(state, angryId, allyId);
            }

            if (allyId == state.playerCountryId)
                state.AddNotification(NotificationClass.Priority, "THE BILL FOR STANDING ASIDE",
                    $"{abandoned.Count} state(s) have answered our refusal — sanctions imposed, "
                    + "preferential terms withdrawn. Our guarantees are discounted everywhere, "
                    + "and at least one of them now regards us as a threat rather than a friend.",
                    defenderId, desk: ReportingDesk.Diplomacy);
        }

        /// <summary>How much <paramref name="observerId"/> cares about the abandoned state.</summary>
        static float ClosenessTo(GameState state, string observerId, string defenderId)
        {
            if (observerId == defenderId) return 100f;

            var pair = state.FindRelationship(observerId, defenderId);
            if (pair == null) return 0f;

            float closeness = pair.relations * 0.5f + pair.trust * 0.3f
                            + pair.strategicAlignment * 0.2f;
            if (BlocSystem.SameBloc(state, observerId, defenderId)) closeness += 15f;
            return Clamp(closeness);
        }

        /// <summary>
        /// Withdraw the preferential terms. A state that will not fight for you
        /// does not keep the trade you gave it for being an ally.
        ///
        /// The commitment is removed rather than the whole treaty broken: the
        /// non-aggression clause between two states that no longer trust each
        /// other is precisely the clause worth keeping.
        /// </summary>
        static void CancelPreferentialTrade(GameState state, string angryId, string allyId)
        {
            var treaty = state.FindTreaty(angryId, allyId);
            if (treaty != null && !treaty.broken
                && treaty.commitments.Remove(TreatyCommitment.TradePreference))
            {
                for (int i = treaty.clauses.Count - 1; i >= 0; i--)
                    if (treaty.clauses[i].commitment == TreatyCommitment.TradePreference)
                        treaty.clauses.RemoveAt(i);

                if (allyId == state.playerCountryId || angryId == state.playerCountryId)
                    state.AddNotification(NotificationClass.Advisory, "PREFERENTIAL TERMS WITHDRAWN",
                        $"{state.FindCountry(angryId)?.displayName} has withdrawn preferential "
                        + "trade terms.", angryId, desk: ReportingDesk.Economy);
            }

            var link = state.FindTrade(angryId, allyId);
            if (link == null) return;

            // The trade itself thins. Not embargoed — that is what sanctions are
            // for, and doubling the same consequence would price one act twice.
            link.volume = Math.Max(0f, link.volume * 0.65f);
            link.tariff = Math.Min(100f, link.tariff + 15f);
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
