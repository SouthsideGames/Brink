using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// The multilateral chamber (GDD §15.2, §20, §28).
    ///
    /// **There was no room where everybody was present.** Diplomacy was bilateral
    /// outreach, bilateral treaties and coalitions assembled for one war and
    /// dissolved after it. Sanctions were unilateral by construction: an operator
    /// who wanted five states to squeeze one had to persuade five states one at a
    /// time and then watch each of them pay full blowback for doing it. There was
    /// no public finding a state could be made to wear, and no way at all to act
    /// on somebody else's war except by joining it.
    ///
    /// Four ideas hold it up:
    ///
    /// 1. **Votes are read, never stored.** How a government votes is computed
    ///    from the six dimensions of `Relationship` — standing with the mover,
    ///    hostility to the subject, what it buys from them, what it has signed
    ///    with them. There is no second opinion model to drift out of step with
    ///    the diplomatic one, and a state that has just been bought off votes
    ///    differently the same month.
    /// 2. **The veto is the point, not a flaw.** A permanent member can stop
    ///    anything, so a great power cannot be censured while it has a friend in
    ///    the chamber — which is the first thing anybody learns about such a body.
    ///    It is not free: vetoing is a public act, and the states that voted yes
    ///    remember who spent it.
    /// 3. **Calling a vote you lose costs you.** Otherwise the correct play is to
    ///    table a motion every month and see what sticks. A failed motion is the
    ///    mover misreading the room, and the room notices.
    /// 4. **One motion at a time, worldwide.** A chamber that resolves four things
    ///    a month is a ticker. The scarcity is what makes a seat on the agenda
    ///    worth spending Command Points on.
    ///
    /// What passing actually buys is deliberately mechanical rather than
    /// atmospheric: a mandate makes coercion measurably cheaper to sustain
    /// (`EconomySystem.SanctionBlowbackFor`), stops the target talking their way
    /// out of it (`EconomySystem.AgeSanctions`, `EconomySystem.SeekSanctionsReliefBy`), and a
    /// censure is real weight in a confrontation
    /// (`ConfrontationSystem.StrategicPressure`). A resolution that only printed a
    /// line in the chronicle would be the "written but never read" bug with a
    /// gavel.
    /// </summary>
    public static class CouncilSystem
    {
        /// <summary>Seats that can stop a motion.</summary>
        public const int PermanentSeats = 5;

        /// <summary>Command capacity for the player to put something to the chamber.</summary>
        public const int MotionCost = 2;

        /// <summary>What an AI government pays in standing to do the same.</summary>
        public const float MotionPoliticalCost = 2f;

        /// <summary>Months between motions, worldwide. The agenda is scarce.</summary>
        public const int AgendaCooldownMonths = 4;

        /// <summary>Months a mandate and a censure stand for.</summary>
        public const int MandateMonths = 36;
        public const int CensureMonths = 24;

        /// <summary>Treasury each supporting state puts into a relief motion.</summary>
        public const float ReliefContribution = 280f;

        // ---------- standing findings ----------

        /// <summary>True while measures against this state are multilaterally authorised.</summary>
        public static bool SanctionsMandated(GameState state, string subjectId)
        {
            if (state?.council == null || string.IsNullOrEmpty(subjectId)) return false;
            foreach (var mandate in state.council.mandates)
                if (mandate.subjectId == subjectId && mandate.monthsRemaining > 0) return true;
            return false;
        }

        /// <summary>True while a public finding against this state still stands.</summary>
        public static bool IsCensured(GameState state, string subjectId)
        {
            if (state?.council == null || string.IsNullOrEmpty(subjectId)) return false;
            foreach (var censure in state.council.censures)
                if (censure.subjectId == subjectId && censure.monthsRemaining > 0) return true;
            return false;
        }

        // ---------- the chamber itself ----------

        /// <summary>
        /// Fill the permanent seats, once, from the capability the world opened
        /// with. Lazy rather than done in `WorldFactory` so an existing save picks
        /// a chamber up on load without a migration step — and deterministic, so
        /// the same world always seats the same five.
        /// </summary>
        public static void EnsureSeated(GameState state)
        {
            if (state.council == null) state.council = new CouncilState();
            if (state.council.permanentMembers.Count > 0) return;

            var ranked = new List<CountryState>(state.countries);
            ranked.Sort((a, b) =>
            {
                float sa = Weight(a), sb = Weight(b);
                int byWeight = sb.CompareTo(sa);
                return byWeight != 0 ? byWeight : string.CompareOrdinal(a.id, b.id);
            });

            int seats = Math.Min(PermanentSeats, ranked.Count);
            for (int i = 0; i < seats; i++)
                state.council.permanentMembers.Add(ranked[i].id);
        }

        static float Weight(CountryState country)
            => country.pillars.military + country.pillars.economy
             + country.pillars.diplomacy * 0.5f;

        // ---------- what could be put to it ----------

        /// <summary>
        /// The motions this state could raise right now, with the subject each
        /// would name. Derived entirely from live world state: the chamber has no
        /// agenda of its own and cannot invent a grievance.
        /// </summary>
        public static List<CouncilMotion> AvailableMotions(GameState state, string moverId)
        {
            var motions = new List<CouncilMotion>();
            EnsureSeated(state);

            foreach (var subject in state.countries)
            {
                if (subject.id == moverId) continue;

                // Aggression: they are prosecuting a war they started.
                foreach (var confrontation in state.confrontations)
                {
                    if (confrontation.resolved) continue;
                    if (confrontation.initiatorId != subject.id) continue;
                    if (confrontation.escalation < EscalationState.LimitedConflict) continue;

                    motions.Add(Draft(state, MotionKind.Condemnation, moverId, subject.id,
                        $"That {subject.displayName} be condemned for the prosecution of its war."));
                    break;
                }

                // Occupation: they are sitting on somebody else's ground.
                if (TerritorySystem.OccupiedValue(state, subject.id) > 20f
                    && !Already(motions, MotionKind.Condemnation, subject.id))
                    motions.Add(Draft(state, MotionKind.Condemnation, moverId, subject.id,
                        $"That {subject.displayName} be condemned for its occupation of "
                        + "foreign territory."));

                // Subversion: they have been caught arming somebody's insurgency.
                foreach (var insurgency in state.insurgencies)
                {
                    if (!insurgency.sponsorExposed || insurgency.sponsorId != subject.id) continue;
                    if (Already(motions, MotionKind.Condemnation, subject.id)) break;

                    motions.Add(Draft(state, MotionKind.Condemnation, moverId, subject.id,
                        $"That {subject.displayName} be condemned for arming an insurgency "
                        + "in another state."));
                    break;
                }

                // Measures: somebody is already squeezing them and wants company.
                if (!SanctionsMandated(state, subject.id) && SendersAgainst(state, subject.id) >= 2)
                    motions.Add(Draft(state, MotionKind.SanctionsMandate, moverId, subject.id,
                        $"That measures against {subject.displayName} be authorised."));

                // Relief: the one motion that is not aimed at anybody. Either the
                // country is destitute, or it is carrying a crisis that started
                // somewhere else — which is the case the chamber exists for.
                if (subject.livingStandards < 30f)
                    motions.Add(Draft(state, MotionKind.Relief, moverId, subject.id,
                        $"That the chamber fund relief for {subject.displayName}."));
                else if (subject.displacement.hosted >= 12f)
                    motions.Add(Draft(state, MotionKind.Relief, moverId, subject.id,
                        $"That the chamber share the cost {subject.displayName} is carrying "
                        + "for people displaced from elsewhere."));
            }

            return motions;
        }

        static bool Already(List<CouncilMotion> motions, MotionKind kind, string subjectId)
        {
            foreach (var motion in motions)
                if (motion.kind == kind && motion.subjectId == subjectId) return true;
            return false;
        }

        static int SendersAgainst(GameState state, string subjectId)
        {
            int senders = 0;
            foreach (var sanction in state.sanctions)
                if (sanction.targetId == subjectId) senders++;
            return senders;
        }

        static CouncilMotion Draft(GameState state, MotionKind kind, string moverId,
            string subjectId, string summary)
            => new CouncilMotion
            {
                id = $"MOT_{kind}_{subjectId}_{state.date.SortKey}",
                kind = kind,
                moverId = moverId,
                subjectId = subjectId,
                raised = state.date,
                outcome = MotionOutcome.Pending,
                summary = summary
            };

        public static bool CanRaise(GameState state, string moverId, out string reason)
        {
            EnsureSeated(state);

            int monthIndex = state.date.MonthsSince(state.startDate);
            if (monthIndex - state.council.lastMotionMonth < AgendaCooldownMonths)
            {
                int wait = AgendaCooldownMonths - (monthIndex - state.council.lastMotionMonth);
                reason = $"THE AGENDA IS TAKEN — {wait} month(s) until the chamber sits again.";
                return false;
            }

            if (state.FindCountry(moverId) == null) { reason = "No such state."; return false; }

            reason = "";
            return true;
        }

        // ---------- how a government votes ----------

        /// <summary>
        /// How this state would vote: positive is yes, negative is no, near zero
        /// is an abstention.
        ///
        /// Every term is a live read of the existing diplomatic model. That is
        /// deliberate — it means a vote can be *bought* through exactly the same
        /// instruments that buy anything else, and an operator who has spent a
        /// decade building trade dependence discovers what it was for.
        /// </summary>
        public static float VoteScore(GameState state, CouncilMotion motion, string voterId)
        {
            if (voterId == motion.subjectId) return motion.kind == MotionKind.Relief ? 40f : -100f;
            if (voterId == motion.moverId) return 100f;

            var withMover = state.FindRelationship(voterId, motion.moverId);
            var withSubject = state.FindRelationship(voterId, motion.subjectId);
            if (withMover == null || withSubject == null) return 0f;

            // Who asked matters as much as what is being asked.
            float loyalty = (withMover.relations - 50f) * 0.45f
                          + (withMover.strategicAlignment - 50f) * 0.25f
                          + (withMover.trust - 50f) * 0.15f;

            // **A bloc votes as a bloc.** This is what makes one worth founding:
            // the chamber is the room where acting together is the whole point,
            // and until blocs existed there was no way to arrive in it as a side.
            if (BlocSystem.SameBloc(state, voterId, motion.moverId)) loyalty += 30f;
            else if (BlocSystem.SameBloc(state, voterId, motion.subjectId)) loyalty -= 34f;
            else if (BlocSystem.OpposedBlocs(state, voterId, motion.moverId)) loyalty -= 14f;

            if (motion.kind == MotionKind.Relief)
            {
                // Nobody is being accused, so this turns on goodwill toward the
                // subject and on whether we can spare the money.
                var voter = state.FindCountry(voterId);
                float generosity = (withSubject.relations - 40f) * 0.30f
                                 + (voter != null ? voter.reciprocity - 50f : 0f) * 0.25f;
                if (voter != null && voter.resources.treasury < ReliefContribution * 3f)
                    generosity -= 25f;
                return loyalty * 0.6f + generosity;
            }

            float hostility = (45f - withSubject.relations) * 0.55f
                            + Math.Max(0f, withSubject.ThreatPerceivedBy(voterId) - 45f) * 0.40f;

            // You do not vote to sanction your own supplier, however you feel
            // about them. This is the term that makes trade dependence a
            // diplomatic asset rather than only an economic one.
            float interest = -withSubject.DependenceOf(voterId) * 0.55f;

            var treaty = state.FindTreaty(voterId, motion.subjectId);
            if (treaty != null)
            {
                interest -= 18f;
                if (treaty.HasActive(state, TreatyCommitment.MutualDefense)) interest -= 22f;
            }

            // A mandate is a bigger ask than a form of words, and states that
            // would happily sign a condemnation balk at authorising measures.
            float threshold = motion.kind == MotionKind.SanctionsMandate ? 14f : 0f;

            return loyalty + hostility + interest - threshold;
        }

        // ---------- putting one to the chamber ----------

        /// <summary>Actor-generic. Resolves immediately: the chamber sits, votes and rises.</summary>
        public static CouncilMotion RaiseBy(GameState state, string moverId, CouncilMotion motion)
        {
            if (motion == null) return null;
            if (!CanRaise(state, moverId, out _)) return null;

            EnsureSeated(state);
            motion.moverId = moverId;
            state.council.lastMotionMonth = state.date.MonthsSince(state.startDate);

            var supporters = new List<string>();
            var opponents = new List<string>();

            foreach (var country in state.countries)
            {
                float score = VoteScore(state, motion, country.id);
                if (score > 12f) { motion.yes++; supporters.Add(country.id); }
                else if (score < -12f) { motion.no++; opponents.Add(country.id); }
                else motion.abstain++;
            }

            // A permanent member that votes against it kills it, whatever the
            // count. The subject's own seat counts: a great power cannot be
            // censured by this chamber, which is the first true thing anybody
            // learns about one.
            foreach (string opponentId in opponents)
            {
                if (!state.council.IsPermanent(opponentId)) continue;
                motion.outcome = MotionOutcome.Vetoed;
                motion.vetoedById = opponentId;
                break;
            }

            if (motion.outcome == MotionOutcome.Pending)
                motion.outcome = motion.yes > motion.no && motion.yes >= 3
                    ? MotionOutcome.Passed
                    : MotionOutcome.Failed;

            state.council.record.Add(motion);

            switch (motion.outcome)
            {
                case MotionOutcome.Passed: Carry(state, motion, supporters); break;
                case MotionOutcome.Vetoed: Vetoed(state, motion, supporters); break;
                default: Lost(state, motion); break;
            }

            Report(state, motion);
            return motion;
        }

        /// <summary>Player order: spends CP and records the initiative.</summary>
        public static CouncilMotion Raise(GameState state, TurnManager turns, CouncilMotion motion)
        {
            if (!AuthoritySystem.EnsureAuthority(state, Pillar.Diplomacy)) return null;
            if (!CanRaise(state, state.playerCountryId, out string reason))
            {
                GameLog.Warn("DIPL", reason);
                return null;
            }

            if (!turns.SpendCommandPoints(MotionCost, $"Council motion: {motion.kind}")) return null;

            var resolved = RaiseBy(state, state.playerCountryId, motion);
            if (resolved == null) return null;

            ProgressionSystem.AwardXP(state,
                resolved.outcome == MotionOutcome.Passed ? 26 : 10,
                $"Council motion: {motion.kind}");
            ProgressionSystem.RecordInitiative(state);
            return resolved;
        }

        // ---------- outcomes ----------

        static void Carry(GameState state, CouncilMotion motion, List<string> supporters)
        {
            var subject = state.FindCountry(motion.subjectId);
            if (subject == null) return;

            switch (motion.kind)
            {
                case MotionKind.Condemnation:
                    state.council.censures.Add(new CouncilCensure
                    { subjectId = subject.id, monthsRemaining = CensureMonths });

                    subject.reciprocity = Clamp(subject.reciprocity - 8f);
                    foreach (string supporterId in supporters)
                    {
                        if (supporterId == subject.id) continue;
                        var relationship = state.FindRelationship(supporterId, subject.id);
                        if (relationship == null) continue;
                        relationship.relations = Clamp(relationship.relations - 6f);
                        relationship.SetThreatPerceivedBy(supporterId,
                            Clamp(relationship.ThreatPerceivedBy(supporterId) + 4f));
                    }
                    break;

                case MotionKind.SanctionsMandate:
                    state.council.mandates.Add(new CouncilMandate
                    { subjectId = subject.id, monthsRemaining = MandateMonths });

                    // The finding alone moves money before a single new measure
                    // is imposed: being formally sanctionable is a fact about a
                    // country that its creditors read.
                    subject.economy.confidence = Clamp(subject.economy.confidence - 6f);
                    break;

                default: // Relief
                    float raised = 0f;
                    foreach (string supporterId in supporters)
                    {
                        var supporter = state.FindCountry(supporterId);
                        if (supporter == null || supporter.id == subject.id) continue;
                        if (supporter.resources.treasury < ReliefContribution) continue;

                        supporter.resources.treasury -= ReliefContribution;
                        raised += ReliefContribution;

                        var relationship = state.FindRelationship(supporterId, subject.id);
                        if (relationship == null) continue;
                        relationship.relations = Clamp(relationship.relations + 5f);
                        relationship.trust = Clamp(relationship.trust + 3f);
                    }

                    subject.resources.treasury += raised;
                    subject.livingStandards = Clamp(subject.livingStandards + raised / 400f);
                    subject.publicGrievance = Clamp(subject.publicGrievance - raised / 600f);
                    break;
            }

            // The mover gets the credit, from the people who agreed with them.
            foreach (string supporterId in supporters)
            {
                if (supporterId == motion.moverId) continue;
                var relationship = state.FindRelationship(motion.moverId, supporterId);
                if (relationship == null) continue;
                relationship.relations = Clamp(relationship.relations + 2.5f);
                relationship.strategicAlignment = Clamp(relationship.strategicAlignment + 2f);
            }

            state.AddChronicle(ChronicleCategory.Diplomatic, motion.subjectId,
                $"The chamber carries: {motion.summary} ({motion.yes}-{motion.no}-{motion.abstain})",
                Publicity.Public);
        }

        static void Vetoed(GameState state, CouncilMotion motion, List<string> supporters)
        {
            var blocker = state.FindCountry(motion.vetoedById);
            if (blocker == null) return;

            // Stopping something everybody else wanted is not free. This is what
            // keeps the veto from being a purely defensive superpower — using it
            // spends standing with every state that voted for the motion.
            foreach (string supporterId in supporters)
            {
                if (supporterId == blocker.id) continue;
                var relationship = state.FindRelationship(blocker.id, supporterId);
                if (relationship == null) continue;
                relationship.relations = Clamp(relationship.relations - 3.5f);
                relationship.trust = Clamp(relationship.trust - 2f);
            }

            state.AddChronicle(ChronicleCategory.Diplomatic, blocker.id,
                $"{blocker.displayName} blocks: {motion.summary}", Publicity.Public);
        }

        static void Lost(GameState state, CouncilMotion motion)
        {
            // Calling a vote you cannot win is a public misreading of the room,
            // and the state you named remembers being named.
            var relationship = state.FindRelationship(motion.moverId, motion.subjectId);
            if (relationship != null)
            {
                relationship.relations = Clamp(relationship.relations - 4f);
                relationship.AddMemory(state.date, "Moved against us and lost the vote.", 0.8f);
            }

            var mover = state.FindCountry(motion.moverId);
            if (mover != null) mover.reciprocity = Clamp(mover.reciprocity - 2f);

            state.AddChronicle(ChronicleCategory.Diplomatic, motion.moverId,
                $"The chamber rejects: {motion.summary} "
                + $"({motion.yes}-{motion.no}-{motion.abstain})", Publicity.Public);
        }

        static void Report(GameState state, CouncilMotion motion)
        {
            string verdict;
            switch (motion.outcome)
            {
                case MotionOutcome.Passed: verdict = "CARRIED"; break;
                case MotionOutcome.Vetoed:
                    var blocker = state.FindCountry(motion.vetoedById);
                    verdict = $"BLOCKED BY {(blocker != null ? blocker.displayName.ToUpperInvariant() : "A PERMANENT MEMBER")}";
                    break;
                default: verdict = "REJECTED"; break;
            }

            bool ours = motion.moverId == state.playerCountryId
                        || motion.subjectId == state.playerCountryId;

            state.AddNotification(
                ours ? NotificationClass.Priority : NotificationClass.Wire,
                $"CHAMBER: {verdict}",
                $"{motion.summary} Vote {motion.yes}-{motion.no}-{motion.abstain}.",
                motion.subjectId, desk: ReportingDesk.Diplomacy);
        }

        // ---------- the monthly tick ----------

        public static void MonthlyUpdate(GameState state)
        {
            EnsureSeated(state);

            for (int i = state.council.mandates.Count - 1; i >= 0; i--)
            {
                var mandate = state.council.mandates[i];
                mandate.monthsRemaining--;
                if (mandate.monthsRemaining > 0) continue;

                var subject = state.FindCountry(mandate.subjectId);
                state.council.mandates.RemoveAt(i);
                if (subject == null) continue;

                state.AddChronicle(ChronicleCategory.Diplomatic, subject.id,
                    $"The authorisation of measures against {subject.displayName} expires.",
                    Publicity.Public);
            }

            for (int i = state.council.censures.Count - 1; i >= 0; i--)
            {
                var censure = state.council.censures[i];
                censure.monthsRemaining--;
                if (censure.monthsRemaining <= 0) state.council.censures.RemoveAt(i);
            }

            // Trim the record the way the notification list is trimmed. A save
            // that runs fifty years should not carry six hundred old votes.
            if (state.council.record.Count > 120)
                state.council.record.RemoveRange(0, state.council.record.Count - 120);

            ConsiderMotions(state);
        }

        /// <summary>
        /// Whether any foreign government puts something to the chamber this
        /// month.
        ///
        /// One mover is chosen, not sixteen considered in turn — the agenda holds
        /// one motion, so a loop that let every state try would simply mean the
        /// first state in the list always spoke.
        /// </summary>
        static void ConsiderMotions(GameState state)
        {
            int monthIndex = state.date.MonthsSince(state.startDate);
            if (monthIndex - state.council.lastMotionMonth < AgendaCooldownMonths) return;

            var rng = new Random(unchecked(state.rngSeed * 7333 + monthIndex * 131));
            if (rng.NextDouble() > 0.5) return;

            CouncilMotion best = null;
            string bestMover = null;
            float bestScore = 0f;

            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (country.resources.treasury < AISystem.DiscretionaryReserve) continue;

                foreach (var motion in AvailableMotions(state, country.id))
                {
                    // A government only spends the agenda on something it expects
                    // to carry. Counting the room first is what stops the chamber
                    // filling with motions nobody supports.
                    float support = 0f;
                    foreach (var voter in state.countries)
                        support += VoteScore(state, motion, voter.id) > 12f ? 1f : 0f;

                    float value = motion.kind == MotionKind.Relief ? support : support * 1.4f;
                    if (value <= bestScore) continue;

                    bestScore = value;
                    best = motion;
                    bestMover = country.id;
                }
            }

            if (best == null || bestScore < 4f) return;

            if (!GovernmentSystem.SpendPoliticalCapitalBy(
                    state, bestMover, MotionPoliticalCost, "Council motion")) return;

            RaiseBy(state, bestMover, best);
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
