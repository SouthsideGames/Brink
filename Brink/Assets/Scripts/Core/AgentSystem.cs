using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>What an approach to a foreign official is trying to achieve.</summary>
    public enum AgentAction
    {
        /// <summary>Months of quiet contact. Cheap, slow, hard to see.</summary>
        Cultivate = 0,

        /// <summary>Ask the question. Either they are ours or the approach is blown.</summary>
        Recruit = 1,

        /// <summary>Ruin them publicly. Loud, effective, and attributable.</summary>
        Discredit = 2
    }

    /// <summary>
    /// Operations against a person (GDD §14 amendment).
    ///
    /// **The dossier was a surface with no verb.** `IntelligenceView` already
    /// renders every foreign minister's name at penetration ≥20 and a competence
    /// band at ≥55, gated in stages, with a comment in the code explaining why a
    /// rival's incompetent economy minister "is a real, exploitable fact about
    /// them". The player could read all of it and do nothing with any of it.
    ///
    /// Everything needed already existed: `Official` carries competence, loyalty
    /// and trust; `CabinetSystem.MonthlyAct` runs every state's cabinet, so a
    /// foreign minister's quality genuinely drives that country's pillar;
    /// `RegimeSystem` already reads `conspiracyBackerId`, so a turned official has
    /// somewhere to feed. This adds two fields and three verbs.
    ///
    /// The shape is deliberately *unlike* the other covert operations, because
    /// the audit's finding was that intelligence has three verbs that are all the
    /// same verb — pick a country, press, one roll. This one has a state:
    ///
    /// - **Cultivation is months of work.** You cannot walk up to a foreign
    ///   minister and ask. An approach accumulates, and can be abandoned or
    ///   discovered before it ever pays.
    /// - **Who you approach matters.** A minister with low loyalty to their own
    ///   government is a candidate; a loyal one is a way to get caught.
    /// - **What you get is what they are.** A recruited economy minister reports
    ///   on the economy. The access is the person, not a generic bonus.
    /// </summary>
    public static class AgentSystem
    {
        public const int CultivateCost = 1;
        public const int RecruitCost = 2;
        public const int DiscreditCost = 2;

        /// <summary>Cultivation needed before an approach can be made at all.</summary>
        public const float RecruitThreshold = 55f;

        /// <summary>Penetration needed to reach a foreign cabinet at all.</summary>
        public const float MinimumPenetration = 35f;

        public static int CostOf(AgentAction action)
        {
            switch (action)
            {
                case AgentAction.Recruit: return RecruitCost;
                case AgentAction.Discredit: return DiscreditCost;
                default: return CultivateCost;
            }
        }

        // ---------- what is possible ----------

        public static bool CanAct(GameState state, string actorId, string targetCountryId,
            Official official, AgentAction action, out string reason)
        {
            if (official == null) { reason = "No such official."; return false; }

            var network = state.FindNetwork(actorId, targetCountryId);
            if (network == null)
            {
                reason = "NO NETWORK THERE — nothing is collecting against them.";
                return false;
            }
            if (network.compromised)
            {
                reason = "THE NETWORK IS BURNED — rebuild it before approaching anybody.";
                return false;
            }
            if (network.penetration < MinimumPenetration)
            {
                reason = $"ACCESS TOO SHALLOW — {MinimumPenetration:F0} penetration needed to "
                       + $"reach a minister, we have {network.penetration:F0}.";
                return false;
            }

            if (official.recruitedById == actorId && action != AgentAction.Discredit)
            {
                reason = "ALREADY OURS.";
                return false;
            }

            if (action == AgentAction.Recruit && official.cultivation < RecruitThreshold)
            {
                reason = $"NOT CULTIVATED ENOUGH — {official.cultivation:F0} of "
                       + $"{RecruitThreshold:F0}. Asking now would be asking a stranger.";
                return false;
            }

            reason = "";
            return true;
        }

        // ---------- doing it ----------

        /// <summary>Actor-generic. Spends no Command Points — see <see cref="Run"/>.</summary>
        public static bool RunBy(GameState state, string actorId, string targetCountryId,
            Official official, AgentAction action)
        {
            if (!CanAct(state, actorId, targetCountryId, official, action, out _)) return false;

            var network = state.FindNetwork(actorId, targetCountryId);
            var target = state.FindCountry(targetCountryId);
            var actor = state.FindCountry(actorId);
            if (target == null || actor == null) return false;

            var rng = new Random(unchecked(
                state.rngSeed * 40503 + state.NextActionSequence() * 7717 + Hash.Of(official.id)));

            switch (action)
            {
                case AgentAction.Cultivate: return Cultivate(state, actor, target, official, network, rng);
                case AgentAction.Recruit: return Recruit(state, actor, target, official, network, rng);
                default: return Discredit(state, actor, target, official, network, rng);
            }
        }

        /// <summary>Player order: spends CP and records the initiative.</summary>
        public static bool Run(GameState state, TurnManager turns, string targetCountryId,
            Official official, AgentAction action)
        {
            if (!AuthoritySystem.EnsureAuthority(state, Pillar.Intelligence)) return false;
            if (!CanAct(state, state.playerCountryId, targetCountryId, official, action, out string reason))
            {
                GameLog.Warn("INTEL", reason);
                return false;
            }

            if (!turns.SpendCommandPoints(CostOf(action), $"Agent operation: {action}")) return false;
            if (!RunBy(state, state.playerCountryId, targetCountryId, official, action)) return false;

            ProgressionSystem.AwardXP(state, action == AgentAction.Recruit ? 28 : 12,
                $"Agent operation: {action}");
            ProgressionSystem.RecordInitiative(state);
            return true;
        }

        // ---------- the three verbs ----------

        /// <summary>
        /// Quiet contact. Progress depends on how well disposed they already are
        /// toward leaving — a minister with poor standing in their own government
        /// is a candidate; a loyal one is how services get caught.
        /// </summary>
        static bool Cultivate(GameState state, CountryState actor, CountryState target,
            Official official, IntelNetwork network, Random rng)
        {
            float receptiveness = Clamp01(
                (100f - official.loyalty) * 0.6f + (100f - official.trust) * 0.4f) / 100f;

            float progress = 6f + receptiveness * 22f + network.penetration * 0.08f;
            official.cultivation = Clamp(official.cultivation + progress);

            // Every contact is a chance to be seen. Low, because this is the
            // patient option — but it accumulates over the months it takes.
            if (rng.NextDouble() < 0.05 + target.counterIntel.counterIntelligence / 900.0)
                Blown(state, actor, target, official, network, "an approach to");

            if (actor.isPlayer)
                state.AddNotification(NotificationClass.Advisory, "CONTACT MAINTAINED",
                    $"{official.displayName} ({target.displayName}) — cultivation now "
                    + $"{official.cultivation:F0}. Recruitment needs {RecruitThreshold:F0}.",
                    target.id, desk: ReportingDesk.Intelligence);

            return true;
        }

        /// <summary>
        /// The question. Either they come across or the approach is blown — and
        /// being refused is worse than never having asked, because now they know.
        /// </summary>
        static bool Recruit(GameState state, CountryState actor, CountryState target,
            Official official, IntelNetwork network, Random rng)
        {
            float chance = Clamp01(
                official.cultivation / 140f
                + (100f - official.loyalty) / 260f
                - target.counterIntel.counterIntelligence / 320f);

            if (rng.NextDouble() > chance)
            {
                official.cultivation = Clamp(official.cultivation - 30f);
                official.loyalty = Clamp(official.loyalty + 8f);   // the approach hardened them
                Blown(state, actor, target, official, network, "an attempt to recruit");
                return true;
            }

            official.recruitedById = actor.id;
            official.loyalty = Clamp(official.loyalty - 25f);

            // An agent inside a cabinet is a way in. Their office decides what
            // they can see — access is the person, not a generic bonus.
            var domain = DomainOf(official.office);
            if (domain != null)
            {
                var estimate = state.FindEstimate(actor.id, target.id, domain.Value);
                if (estimate != null) estimate.confidence = ConfidenceGrade.High;
            }
            network.penetration = Clamp(network.penetration + 14f);

            if (actor.isPlayer)
                state.AddNotification(NotificationClass.Priority, "AGENT RECRUITED",
                    $"{official.title} {official.displayName} of {target.displayName} has agreed "
                    + "to co-operate. They will keep their post, and what crosses their desk "
                    + "will reach ours.", target.id, desk: ReportingDesk.Intelligence);

            state.AddChronicle(ChronicleCategory.Intelligence, actor.id,
                $"An official of {target.displayName} was turned.", Publicity.Secret);
            return true;
        }

        /// <summary>
        /// Ruin them. Effective and loud: the office is vacated, but somebody did
        /// this and everyone can see it was done.
        /// </summary>
        static bool Discredit(GameState state, CountryState actor, CountryState target,
            Official official, IntelNetwork network, Random rng)
        {
            float chance = Clamp01(
                0.35f + network.penetration / 240f + official.cultivation / 300f
                - target.counterIntel.counterIntelligence / 300f);

            if (rng.NextDouble() > chance)
            {
                Blown(state, actor, target, official, network, "an attempt to discredit");
                return true;
            }

            official.competence = Clamp(official.competence - 22f);
            official.trust = Clamp(official.trust - 30f);
            official.cultivation = 0f;
            official.recruitedById = "";     // no use to anyone now

            target.stability = Clamp(target.stability - 2.5f);
            target.government.eliteCohesion = Clamp(target.government.eliteCohesion - 4f);

            state.AddChronicle(ChronicleCategory.Political, target.id,
                $"{target.displayName}: {official.title} {official.displayName} is engulfed "
                + "in scandal.", Publicity.Public);

            if (actor.isPlayer)
                state.AddNotification(NotificationClass.Priority, "OFFICIAL DISCREDITED",
                    $"{official.displayName} of {target.displayName} is finished. Their ministry "
                    + "will be run badly for a while, and somebody there will ask who benefited.",
                    target.id, desk: ReportingDesk.Intelligence);
            return true;
        }

        // ---------- being caught ----------

        /// <summary>
        /// What it costs when an approach is discovered. Deliberately the same
        /// currency as `IntelligenceSystem`'s exposure: standing with them, not
        /// national capability. A cost with no recovery path under the conditions
        /// that cause it is a disqualification rather than a price.
        /// </summary>
        static void Blown(GameState state, CountryState actor, CountryState target,
            Official official, IntelNetwork network, string what)
        {
            official.cultivation = 0f;
            network.penetration = Clamp(network.penetration - 12f);
            target.counterIntel.counterIntelligence =
                Clamp(target.counterIntel.counterIntelligence + 5f);

            var relationship = state.FindRelationship(actor.id, target.id);
            if (relationship != null)
            {
                relationship.relations = Clamp(relationship.relations - 7f);
                relationship.trust = Clamp(relationship.trust - 10f);
                relationship.AddMemory(state.date,
                    $"Caught {what} one of our ministers.", 1.1f);
            }

            if (actor.isPlayer)
                state.AddNotification(NotificationClass.Priority, "APPROACH BLOWN",
                    $"{target.displayName} has detected {what} {official.displayName}. "
                    + "Their services are alert and the file is closed.",
                    target.id, desk: ReportingDesk.Intelligence);

            state.AddChronicle(ChronicleCategory.Intelligence, actor.id,
                $"{actor.displayName} was caught {what} an official of {target.displayName}.",
                Publicity.Public);
        }

        // ---------- what an agent is worth, monthly ----------

        /// <summary>
        /// An agent in place keeps reporting. Small, steady, and it decays the
        /// moment they are gone — the value is the access, not a permanent buff.
        /// </summary>
        public static void MonthlyUpdate(GameState state)
        {
            foreach (var country in state.countries)
            {
                foreach (var official in country.cabinet)
                {
                    if (string.IsNullOrEmpty(official.recruitedById)) continue;

                    var network = state.FindNetwork(official.recruitedById, country.id);
                    if (network == null || network.compromised)
                    {
                        // No way to run them any more. They are not ours in any
                        // sense that matters.
                        official.recruitedById = "";
                        continue;
                    }

                    network.penetration = Clamp(network.penetration + 1.4f);
                    official.loyalty = Clamp(official.loyalty - 0.2f);
                }
            }
        }

        /// <summary>
        /// Which collection domain an office can see into, or null.
        ///
        /// Deliberately a local copy rather than a call to `IntelReadout` — that
        /// lives in `Brink.UI`, and a simulation system reaching into the
        /// presentation layer would be exactly backwards. The intelligence
        /// service is opaque to itself, which is why that office maps to nothing:
        /// recruiting a rival's spymaster gets you a person, not a window.
        /// </summary>
        static IntelDomain? DomainOf(Pillar office)
        {
            switch (office)
            {
                case Pillar.Military: return IntelDomain.Military;
                case Pillar.Economy: return IntelDomain.Economic;
                case Pillar.Diplomacy: return IntelDomain.Diplomatic;
                case Pillar.Government: return IntelDomain.Political;
                default: return null;
            }
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
        static float Clamp01(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
