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
                if (!treaty.Carries(state, ally, TreatyCommitment.MutualDefense)) continue;
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

            /// <summary>
            /// Whether this call is *defensive* — the guaranteed state was
            /// attacked — or *offensive*: the guaranteed state is fighting on
            /// the side that started the war and has now been answered. See
            /// <see cref="IsDirectCall"/>.
            /// </summary>
            public bool direct = true;
        }

        /// <summary>
        /// How much less willing a signatory is to enter a war on the side that
        /// started it. A mutual defence guarantee is a promise to a state that
        /// is attacked, not to one that attacks; answering an offensive call is
        /// a choice, judged like any other war of choice.
        /// </summary>
        public const float IndirectPenalty = 20f;

        /// <summary>
        /// Whether a call-in on this confrontation is defensive.
        ///
        /// **This is the defensive / offensive distinction the cascade lacked.**
        /// `InvokeObligations` walks the confrontation's `defenderId`, whoever
        /// that is. On the original war the defender is the victim, so the call
        /// is defensive. On a front opened by a guarantor, the "defender" is the
        /// original aggressor (or a state that came in on its side) — the same
        /// guarantee then read as an obligation to fight *for* the state that
        /// started the shooting, which is how a defensive pact became an
        /// unconditional recursive world-war switch (21 of 22 AI wars measured
        /// were such entries).
        ///
        /// The call is direct when the newly attacked party stands on the
        /// defending side of the root war; offensive when it stands on the
        /// aggressing side. Sides are read from the satellite chain, never
        /// stored twice.
        /// </summary>
        public static bool IsDirectCall(GameState state, Confrontation confrontation)
        {
            if (confrontation == null) return true;
            if (!confrontation.IsObligationEntry) return true;
            var root = state.FindConfrontation(confrontation.obligationRootId);
            if (root == null) return true;
            return SideOf(state, root, confrontation.defenderId, 0) >= 0;
        }

        /// <summary>
        /// Whether a front opened by honouring a guarantee was a *defensive*
        /// entry — the state it was joined for stood on the defending side of
        /// the root war. The measurement counterpart of <see cref="IsDirectCall"/>,
        /// which asks the forward question (is a further call-in on this front
        /// defensive?); this asks the backward one (was this front's own entry?).
        /// </summary>
        public static bool IsDefensiveEntry(GameState state, Confrontation confrontation)
        {
            if (confrontation == null || !confrontation.IsObligationEntry) return false;
            var root = state.FindConfrontation(confrontation.obligationRootId);
            if (root == null) return true;
            return SideOf(state, root, confrontation.obligationOnBehalfOfId, 0) >= 0;
        }

        /// <summary>+1 on the defending side of `root`, −1 on the aggressing side, 0 unknown.</summary>
        static int SideOf(GameState state, Confrontation root, string countryId, int depth)
        {
            if (string.IsNullOrEmpty(countryId) || depth > 8) return 0;
            if (countryId == root.defenderId) return 1;
            if (countryId == root.initiatorId) return -1;

            for (int i = 0; i < state.confrontations.Count; i++)
            {
                var c = state.confrontations[i];
                if (c.obligationRootId != root.id) continue;
                if (c.initiatorId == countryId)
                    return SideOf(state, root, c.obligationOnBehalfOfId, depth + 1);
            }
            return 0;
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

            // Depth is checked *before* the flag is set: a war that hit the
            // guard used to be marked as handled and permanently skipped its
            // guarantors, rather than simply not propagating this tick.
            if (cascadeDepth >= MaxCascadeDepth)
            {
                GameLog.Warn("ALLIANCE",
                    $"Cascade depth {MaxCascadeDepth} reached; obligations not propagated further.");
                return;
            }
            confrontation.obligationsInvoked = true;

            string defenderId = confrontation.defenderId;
            string aggressorId = confrontation.initiatorId;
            bool direct = IsDirectCall(state, confrontation);

            // Copy first: honoring opens confrontations and can modify state.
            var guarantors = GuarantorsOf(state, defenderId, aggressorId);
            if (guarantors.Count == 0) return;

            cascadeDepth++;
            try
            {
                foreach (var guarantor in guarantors)
                {
                    guarantor.direct = direct;
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

            // An offensive call reads differently and costs differently: our
            // partner is fighting on the side that started this war, and a
            // defence guarantee is not a promise to join an attack. Declining
            // costs standing with them alone.
            bool direct = guarantor.direct;
            string body = direct
                ? $"{aggressor?.displayName} has opened hostilities against " +
                  $"{defender?.displayName}. Our commitment under {guarantor.sourceName} " +
                  $"has been invoked.{warning} The treaty is explicit. The decision is not."
                : $"{defender?.displayName} is fighting on the side that started this war, and " +
                  $"{aggressor?.displayName} has now engaged them. They ask us to come in under " +
                  $"{guarantor.sourceName}. A defence guarantee does not oblige us to join an " +
                  $"attack; joining is a choice, and declining costs us standing with them alone.{warning}";

            var crisis = new ActiveCrisis
            {
                defId = PlayerObligationCrisisId,
                title = direct ? "ALLIANCE OBLIGATION INVOKED" : "AN ALLY ASKS US INTO ITS WAR",
                body = body,
                startDate = state.date,
                subjectCountryId = confrontation.defenderId,
                contextId = confrontation.id,
                options = new List<CrisisOption>
                {
                    new CrisisOption
                    {
                        label = direct ? "HONOR THE COMMITMENT" : "JOIN THEIR WAR",
                        description = "Enter the war on their side. A real front opens against "
                                    + $"{aggressor?.displayName}.",
                        resultText = $"We have entered the conflict alongside {defender?.displayName}.",
                        approvalDelta = direct ? -4 : -6, stabilityDelta = -2
                    },
                    new CrisisOption
                    {
                        label = direct ? "REPUDIATE THE COMMITMENT" : "DECLINE",
                        description = direct
                            ? "Stay out. Expect sanctions, cancelled trade, and a "
                              + "signature nobody values."
                            : "Stay out of a war they chose. They will remember; nobody else will hold it against us.",
                        resultText = $"We have declined to act. {defender?.displayName} stands alone.",
                        approvalDelta = direct ? 2 : 1
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
        public static bool ApplyPlayerDecision(GameState state, bool honored)
            => ApplyPlayerDecision(state, honored, null, null);

        /// <summary>
        /// Apply the player's answer to a specific invocation.
        ///
        /// The crisis carries the confrontation id because the cascade can put
        /// more than one obligation in front of the operator at once, and
        /// answering the second by scanning for the first is how an operator ends
        /// up in a war they did not agree to enter.
        /// </summary>
        public static bool ApplyPlayerDecision(GameState state, bool honored, string confrontationId)
            => ApplyPlayerDecision(state, honored, confrontationId, null);

        /// <summary>
        /// Apply the player's answer, neutralising the crisis option if the
        /// obligation has evaporated since it was raised.
        ///
        /// **A decision can be overtaken before it is taken.** The cascade can
        /// leave two obligations open at once, and answering the first can
        /// dissolve the alliance behind the second — repudiating expels us from
        /// the bloc, and a two-member bloc dies with the expulsion. The stale
        /// crisis stays on the table, and `CrisisSystem.Resolve` would then apply
        /// its deltas and report its `resultText` — telling the operator "we have
        /// entered the conflict alongside them" when no front opened and nothing
        /// happened. An outcome the game cannot honour is worse than a refusal:
        /// it is the terminal lying about the world.
        ///
        /// So the option is rewritten in place before `Resolve` reads it. This is
        /// deliberately *not* done by removing the crisis from
        /// `state.activeCrises`: `LapseUnanswered` walks that list by index and
        /// removes as it goes, so mutating it from inside a decision would make
        /// the lapse path drop the wrong element. `CrisisOption` is a reference
        /// `Resolve` already holds, which makes this the one edit that is safe on
        /// both paths.
        /// </summary>
        public static bool ApplyPlayerDecision(GameState state, bool honored,
            string confrontationId, CrisisOption option)
        {
            var confrontation = FindObligationConfrontation(state, confrontationId);
            var guarantor = confrontation == null
                ? null : GuarantorFor(state, confrontation, state.playerCountryId);

            if (confrontation == null || guarantor == null)
            {
                ReportOvertaken(state, confrontation, option);
                return false;
            }

            if (honored) Honor(state, confrontation, guarantor);
            else Repudiate(state, confrontation, guarantor);
            return true;
        }

        /// <summary>
        /// Say that the call no longer stands, and make sure the crisis cannot
        /// claim otherwise.
        /// </summary>
        static void ReportOvertaken(GameState state, Confrontation confrontation, CrisisOption option)
        {
            var defender = confrontation == null
                ? null : state.FindCountry(confrontation.defenderId);
            string who = defender == null ? "the state that called on us" : defender.displayName;

            string text = $"The call from {who} has been overtaken: we no longer carry that "
                        + "commitment. Nothing was decided because there was nothing left to "
                        + "decide.";

            if (option != null)
            {
                option.resultText = text;
                option.treasuryDelta = 0f;
                option.stabilityDelta = 0f;
                option.approvalDelta = 0f;
                option.unityDelta = 0f;
                option.effectId = "";
            }

            state.AddNotification(NotificationClass.Priority, "OBLIGATION OVERTAKEN", text,
                confrontation?.defenderId, desk: ReportingDesk.Diplomacy);
            GameLog.Warn("ALLIANCE", "An obligation was answered after it had already lapsed.");
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
            float willingness = HonorWillingness(state, confrontation, guarantor.countryId);

            if (guarantor.direct)
            {
                if (willingness >= 50f) Honor(state, confrontation, guarantor);
                else Repudiate(state, confrontation, guarantor);
                return;
            }

            // An offensive call is a war of choice and is judged like one: the
            // same gates `AssertClaim` puts on a war the government picks for
            // itself, and a stiffer bar on top. Declining is not a betrayal.
            if (willingness - IndirectPenalty >= 50f
                && CanJoinOffensively(state, guarantor.countryId))
                Honor(state, confrontation, guarantor);
            else
                Repudiate(state, confrontation, guarantor);
        }

        /// <summary>
        /// The gates a government puts on a war of its own choosing, applied to
        /// joining an ally's offensive war: room for another front, a domestic
        /// base that will carry it, and no war of its own just ended.
        /// </summary>
        public static bool CanJoinOffensively(GameState state, string countryId)
        {
            var country = state.FindCountry(countryId);
            if (country == null) return false;
            if (!ConfrontationSystem.CanOpenAnother(state, countryId, out _)) return false;
            if (country.stability < 35f || country.warExhaustion > 55f) return false;
            return !RecentlyAtWar(state, countryId);
        }

        /// <summary>A war of this state's own ended inside `AISystem.WarRecoveryMonths`.</summary>
        public static bool RecentlyAtWar(GameState state, string countryId)
        {
            foreach (var past in state.confrontations)
                if (past.resolved && past.Involves(countryId)
                    && state.date.MonthsSince(past.startDate) - past.monthsActive < AISystem.WarRecoveryMonths)
                    return true;
            return false;
        }

        /// <summary>
        /// How willing a signatory is to actually fight. Warmth and shared threat
        /// argue for honoring; exhaustion, instability, dependence on the
        /// aggressor, distance and the wars already being fought argue for
        /// finding a reason not to.
        ///
        /// **Recalibrated 2026-09.** The old form was `30 + relations×0.35 +
        /// trust×0.25 − …`, which at the world's neutral defaults (50/50) read
        /// 60 against a threshold of 50 — two states with no history and no
        /// shared enemy honoured a defence pact with ten points to spare, a
        /// bloc member started near 80, and the only brake was −9 per front.
        /// It took ~3.4 concurrent wars to make a bare signatory hesitate, and
        /// states were measured carrying eight. Warmth is now counted *relative
        /// to neutral*, so an indifferent signatory declines; a warm bilateral
        /// partner honours when it is free to; a bloc member honours through a
        /// second front and hesitates at a third. Distance and a war just
        /// finished weigh too — both were absent, so a guarantor on the far side
        /// of the planet answered identically to a neighbour.
        /// </summary>
        public static float HonorWillingness(GameState state, Confrontation confrontation, string allyId)
        {
            var ally = state.FindCountry(allyId);
            if (ally == null) return 0f;

            var toDefender = state.FindRelationship(allyId, confrontation.defenderId);
            var toAggressor = state.FindRelationship(allyId, confrontation.initiatorId);
            if (toDefender == null || toAggressor == null) return 0f;

            // Relations read as the warmth the world permits — honouring is standing
            // beside them. **Trust is deliberately left underlying**: this function
            // asks whether the ally will come when called, and trust is precisely the
            // belief that commitments will be honoured (GDD §15.1). Capping it would
            // let a third state's alignment make an ally look unreliable, which is a
            // disposition claim rival gravity is not allowed to make.
            float willingness = 18f
                                + (DiplomacySystem.Permitted(state, toDefender, toDefender.relations) - 50f) * 0.55f
                                + (toDefender.trust - 50f) * 0.45f
                                + toDefender.interoperability * 0.15f
                                + toAggressor.ThreatPerceivedBy(allyId) * 0.30f
                                - toAggressor.DependenceOf(allyId) * 0.45f
                                - ally.warExhaustion * 0.45f
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
            willingness -= TheatreSystem.TotalCommitment(state, allyId) * LoadWeight;

            // A war just finished: the army is home, the public is tired of it.
            if (RecentlyAtWar(state, allyId)) willingness -= RecoveryWeight;

            // How far away the fighting is. A guarantee is easier to honour on
            // the border than across an ocean, and reach is what says which.
            float reach = GeographySystem.ReachFactorTo(state, allyId, confrontation.initiatorId);
            willingness -= (1f - reach) * DistanceWeight;

            return willingness;
        }

        /// <summary>Willingness lost per unit of commitment already carried.</summary>
        public const float LoadWeight = 12f;

        /// <summary>Willingness lost while recovering from a war of one's own.</summary>
        public const float RecoveryWeight = 10f;

        /// <summary>Willingness lost at the far end of the reach scale.</summary>
        public const float DistanceWeight = 25f;

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
            // The new front is a satellite of the *root* war, so it closes when
            // that war closes and the cascade can tell which side it is on.
            string rootId = confrontation.IsObligationEntry ? confrontation.obligationRootId : confrontation.id;
            ConfrontationSystem.BeginObligationBy(
                state, allyId, confrontation.initiatorId, confrontation.defenderId, rootId);
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

            // Declining to join an ally's *offensive* war is not repudiating a
            // guarantee. The guarantee was to defend them; they were not
            // attacked, they attacked. It costs standing with the state that
            // asked, and nothing with anyone else.
            if (!guarantor.direct || !IsDirectCall(state, confrontation))
            {
                Decline(state, confrontation, guarantor);
                return;
            }

            var treaty = state.FindTreaty(allyId, confrontation.defenderId);
            if (treaty != null && treaty.Carries(state, allyId, TreatyCommitment.MutualDefense))
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
        /// Staying out of a war an ally chose. No treaty is broken, no bloc
        /// expels, nobody else recalculates our signature — the partner who asked
        /// remembers, and that is the whole bill.
        /// </summary>
        static void Decline(GameState state, Confrontation confrontation, Guarantor guarantor)
        {
            string allyId = guarantor.countryId;
            var ally = state.FindCountry(allyId);
            var asker = state.FindCountry(confrontation.defenderId);
            if (ally == null) return;

            var toAsker = state.FindRelationship(allyId, confrontation.defenderId);
            if (toAsker != null)
            {
                toAsker.relations = Clamp(toAsker.relations - 10f);
                toAsker.trust = Clamp(toAsker.trust - 8f);
                toAsker.AddMemory(state.date, "Would not join our war", -3f);
            }

            bool playerInvolved = allyId == state.playerCountryId
                                  || confrontation.Involves(state.playerCountryId);
            state.AddNotification(playerInvolved ? NotificationClass.Priority : NotificationClass.Wire,
                "STAYS OUT OF AN ALLY'S WAR",
                $"{ally.displayName} declines to join {asker?.displayName}'s war. A defence " +
                "guarantee was not a promise to attack.", allyId, desk: ReportingDesk.Diplomacy);
            state.AddChronicle(ChronicleCategory.Diplomatic, allyId,
                $"Declined to join {asker?.displayName}'s offensive war.", Publicity.Public);
            GameLog.Info("ALLIANCE", $"{allyId} declined an offensive call from {confrontation.defenderId}.");
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
                EconomySystem.ImposeSanctionsBy(state, angryId, allyId, severity, "REPUDIATION");

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

            float closeness = DiplomacySystem.Permitted(state, pair, pair.relations) * 0.5f
                            + DiplomacySystem.Permitted(state, pair, pair.trust) * 0.3f
                            + DiplomacySystem.Permitted(state, pair, pair.strategicAlignment) * 0.2f;
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
