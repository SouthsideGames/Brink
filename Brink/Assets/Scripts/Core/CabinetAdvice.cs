using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>What an official thinks the operator should do in their pillar.</summary>
    public class PillarRecommendation
    {
        public Pillar pillar;

        /// <summary>The action, as it would appear on a button.</summary>
        public string action = "";

        /// <summary>Who or what it acts on, if anything.</summary>
        public string targetName = "";

        /// <summary>One line in the official's own voice.</summary>
        public string rationale = "";

        /// <summary>0..1 how far this desk's judgement can be trusted.</summary>
        public float reliability;
    }

    /// <summary>
    /// The cabinet's counsel, across every pillar (GDD §7.2, §8).
    ///
    /// **The rule, and it is symmetrical.** An official is either running their
    /// pillar or advising on it, never both:
    ///
    /// - **Delegated** (Autonomous or Directed) — they act, and the operator
    ///   reads what they did in the monthly briefing. No advice, because the
    ///   operator is not the one deciding.
    /// - **Direct Control** — the operator decides, so the official advises
    ///   instead. This gives Direct Control an upside it never had: it costs
    ///   Command Points and the official's trust, and in exchange you get their
    ///   professional read on what you are about to do.
    ///
    /// **Advice is worth exactly what the official is worth.** Every
    /// recommendation carries a reliability drawn from competence, and a poor
    /// desk gives poor counsel in the same confident voice as a good one — you
    /// are weighing a person, not reading an oracle. That is what makes an
    /// appointment made two years ago something you feel today.
    ///
    /// Military operations have their own richer advisor (`MilitaryAdvice`),
    /// because an operation has a target and a verb and needs odds. This covers
    /// the other four pillars, where the decision is which instrument to reach
    /// for rather than where to point it.
    /// </summary>
    public static class CabinetAdvice
    {
        /// <summary>Competence below which a desk's judgement is not worth much.</summary>
        public const float Unreliable = 35f;

        public static Official OfficialFor(GameState state, Pillar pillar)
            => state.PlayerCountry?.FindOfficial(pillar);

        /// <summary>
        /// Whether this pillar should be showing counsel rather than acting.
        ///
        /// The single gate the whole feature turns on. Advice while an official
        /// is delegated would be the game recommending an action to the operator
        /// that the official is already taking — noise at best, and a suggestion
        /// to interfere at worst.
        /// </summary>
        public static bool ShouldAdvise(GameState state, Pillar pillar)
        {
            var official = OfficialFor(state, pillar);
            return official != null && official.mode == ControlMode.DirectControl;
        }

        public static float ReliabilityOf(Official official)
            => official == null ? 0f : Math.Max(0f, Math.Min(1f, official.competence / 100f));

        /// <summary>
        /// The counsel for one pillar, or null when the office is vacant, is
        /// delegated, or genuinely has nothing to suggest.
        /// </summary>
        public static PillarRecommendation For(GameState state, Pillar pillar)
        {
            if (!ShouldAdvise(state, pillar)) return null;

            var official = OfficialFor(state, pillar);
            var player = state.PlayerCountry;
            if (player == null) return null;

            var recommendation = Build(state, player, pillar);
            if (recommendation == null) return null;

            recommendation.pillar = pillar;
            recommendation.reliability = ReliabilityOf(official);

            // A weak desk sometimes reaches for the wrong instrument entirely.
            // Deterministic per (official, pillar, month) so it does not flicker
            // as the screen refreshes.
            if (official.competence < Unreliable)
            {
                int seed = unchecked(Hash.Of(official.id) + (int)pillar * 7919
                                     + state.date.MonthsSince(state.startDate));
                if ((seed & 0x3) == 0)
                {
                    var alternative = Fallback(state, player, pillar);
                    if (alternative != null)
                    {
                        alternative.pillar = pillar;
                        alternative.reliability = recommendation.reliability;
                        return alternative;
                    }
                }
            }

            return recommendation;
        }

        /// <summary>The line shown above a pillar's controls.</summary>
        public static string Header(GameState state, PillarRecommendation recommendation)
        {
            var official = OfficialFor(state, recommendation.pillar);
            if (official == null) return "";

            string trust = official.competence >= 70f ? "Their record is good"
                : official.competence >= Unreliable ? "Their record is mixed"
                : "Their judgement has been poor";

            string target = string.IsNullOrEmpty(recommendation.targetName)
                ? "" : $" — {recommendation.targetName.ToUpperInvariant()}";

            return $"{official.title.ToUpperInvariant()} {official.displayName.ToUpperInvariant()} "
                   + $"RECOMMENDS: {recommendation.action.ToUpperInvariant()}{target}\n"
                   + $"   \"{recommendation.rationale}\"\n"
                   + $"   {trust} (competence {official.competence:F0}). "
                   + "You are running this pillar; the decision is yours.";
        }

        // ---------- what each desk would do ----------

        static PillarRecommendation Build(GameState state, CountryState player, Pillar pillar)
        {
            switch (pillar)
            {
                case Pillar.Economy: return EconomyAdvice(state, player);
                case Pillar.Intelligence: return IntelligenceAdvice(state, player);
                case Pillar.Diplomacy: return DiplomacyAdvice(state, player);
                case Pillar.Government: return GovernmentAdvice(state, player);
                default: return null;
            }
        }

        static PillarRecommendation EconomyAdvice(GameState state, CountryState player)
        {
            var economy = player.economy;

            if (player.resources.energy < 45f)
                return new PillarRecommendation
                {
                    action = "Open trade for energy",
                    rationale = "We are short of energy and every sector runs on it. A supply "
                                + "agreement is cheaper than the shortage."
                };

            if (player.resources.strategicMaterials < 40f)
                return new PillarRecommendation
                {
                    action = "Secure strategic materials",
                    rationale = "Industry and procurement both draw on materials we do not have. "
                                + "Trade for them, or take ground that produces them."
                };

            if (economy.inflation > 9f)
                return new PillarRecommendation
                {
                    action = "Tighten policy",
                    rationale = $"Inflation at {economy.inflation:F1}% is doing more political "
                                + "damage than the growth is buying."
                };

            var rival = ColdestRival(state);
            if (rival != null && state.IsAtWar(player.id))
                return new PillarRecommendation
                {
                    action = "Impose sanctions",
                    targetName = rival.displayName,
                    rationale = "If we are fighting them anyway, we should be squeezing them "
                                + "economically at the same time."
                };

            if (economy.growthRate < 1f)
                return new PillarRecommendation
                {
                    action = "Pursue growth",
                    rationale = $"Growth at {economy.growthRate:F1}% is not keeping pace. "
                                + "Everything else we want is downstream of it."
                };

            return new PillarRecommendation
            {
                action = "Expand trade",
                rationale = "The books are sound. This is the moment to build dependence in "
                            + "our favour rather than to spend."
            };
        }

        static PillarRecommendation IntelligenceAdvice(GameState state, CountryState player)
        {
            var rival = ColdestRival(state);

            bool haveNetwork = false;
            bool anyCompromised = false;
            foreach (var network in state.networks)
            {
                if (network.ownerId != player.id) continue;
                if (rival != null && network.targetId == rival.id) haveNetwork = true;
                if (network.compromised) anyCompromised = true;
            }

            if (anyCompromised)
                return new PillarRecommendation
                {
                    action = "Counterintelligence sweep",
                    rationale = "We have been rolled up somewhere. Before we build anything new "
                                + "we should find out how they got in."
                };

            if (rival != null && !haveNetwork)
                return new PillarRecommendation
                {
                    action = "Establish a network",
                    targetName = rival.displayName,
                    rationale = "We are reasoning about them from public information. Everything "
                                + "else this service can do needs collection first."
                };

            if (player.counterIntel.counterIntelligence < 45f)
                return new PillarRecommendation
                {
                    action = "Harden counterintelligence",
                    rationale = "Our own house is more open than theirs. That is a decision "
                                + "somebody else gets to exploit."
                };

            return new PillarRecommendation
            {
                action = "Deepen collection",
                targetName = rival?.displayName ?? "",
                rationale = "Our estimates are usable but coarse. Depth is what turns a band "
                            + "into a number worth acting on."
            };
        }

        static PillarRecommendation DiplomacyAdvice(GameState state, CountryState player)
        {
            var confrontation = state.ActiveConfrontation;
            if (confrontation != null)
            {
                var opponent = state.FindCountry(confrontation.OpponentOf(player.id));
                return new PillarRecommendation
                {
                    action = "Assemble a coalition",
                    targetName = opponent?.displayName ?? "",
                    rationale = "We are fighting alone. Partners add real weight to every "
                                + "operation and cost far less than the losses they prevent."
                };
            }

            CountryState coldest = null, warmest = null;
            float lowest = float.MaxValue, highest = float.MinValue;
            foreach (var country in state.countries)
            {
                if (country.id == player.id) continue;
                var relationship = state.FindRelationship(player.id, country.id);
                if (relationship == null) continue;

                if (relationship.relations < lowest) { lowest = relationship.relations; coldest = country; }
                if (relationship.relations > highest
                    && state.FindTreaty(player.id, country.id) == null)
                { highest = relationship.relations; warmest = country; }
            }

            if (coldest != null && lowest < 30f)
                return new PillarRecommendation
                {
                    action = "Diplomatic outreach",
                    targetName = coldest.displayName,
                    rationale = "Relations with them are cold enough to invite pressure. "
                                + "Talking is the cheapest instrument we have."
                };

            if (warmest != null && highest > 60f)
                return new PillarRecommendation
                {
                    action = "Propose a treaty",
                    targetName = warmest.displayName,
                    rationale = "They are as well disposed as they are likely to get. "
                                + "An agreement now costs less than one made under pressure."
                };

            return new PillarRecommendation
            {
                action = "Diplomatic outreach",
                targetName = coldest?.displayName ?? "",
                rationale = "Nothing is urgent. Standing is built in quiet months and spent "
                            + "in loud ones."
            };
        }

        static PillarRecommendation GovernmentAdvice(GameState state, CountryState player)
        {
            var gov = player.government;
            float backing = gov.IsElective ? gov.legislativeSupport : gov.eliteCohesion;

            if (backing < 45f)
                return new PillarRecommendation
                {
                    action = gov.IsElective ? "Bargain with the chamber" : "Accommodate the elite",
                    rationale = gov.IsElective
                        ? $"Support at {backing:F0} is thin enough to start refusing us things. "
                          + "It has to be rebuilt before it is needed."
                        : $"Cohesion at {backing:F0} is where governments start being replaced "
                          + "from inside."
                };

            if (player.socialUnrest > 55f)
                return new PillarRecommendation
                {
                    action = "Address the unrest",
                    rationale = $"Unrest at {player.socialUnrest:F0} is organised, not merely "
                                + "unhappy. It feeds everything that ends governments."
                };

            if (player.governmentApproval < 40f)
                return new PillarRecommendation
                {
                    action = "Public messaging",
                    rationale = "The public has stopped giving us the benefit of the doubt. "
                                + "Cheap to fix now, expensive later."
                };

            if (gov.leader.age > 66f && gov.successorReadiness < 40f)
                return new PillarRecommendation
                {
                    action = "Prepare a successor",
                    rationale = "The leadership is ageing and there is no settled answer to what "
                                + "comes next. That question gets answered by somebody."
                };

            if (state.politicalCapital >= GovernmentSystem.ConsolidateAuthorityCost)
                return new PillarRecommendation
                {
                    action = "Consolidate authority",
                    rationale = "We are holding political capital with nothing to spend it on. "
                                + "Permanent authority is the one purchase that keeps paying."
                };

            return new PillarRecommendation
            {
                action = "Institutional reform",
                rationale = "The machinery works. Now is when it is cheapest to make it work "
                            + "better."
            };
        }

        /// <summary>
        /// What a poor desk reaches for instead: plausible, and wrong for the
        /// moment. Not random noise — an incompetent minister has real opinions,
        /// they are simply not the ones the situation calls for.
        /// </summary>
        static PillarRecommendation Fallback(GameState state, CountryState player, Pillar pillar)
        {
            switch (pillar)
            {
                case Pillar.Economy:
                    return new PillarRecommendation
                    {
                        action = "Austerity",
                        rationale = "Whatever else is true, a surplus is never wrong."
                    };
                case Pillar.Intelligence:
                    return new PillarRecommendation
                    {
                        action = "Run a covert operation",
                        rationale = "We should be doing something they can feel."
                    };
                case Pillar.Diplomacy:
                    return new PillarRecommendation
                    {
                        action = "Diplomatic outreach",
                        rationale = "More talking is rarely the wrong answer."
                    };
                case Pillar.Government:
                    return new PillarRecommendation
                    {
                        action = "Public messaging",
                        rationale = "The country needs to hear from us."
                    };
                default:
                    return null;
            }
        }

        static CountryState ColdestRival(GameState state)
        {
            CountryState coldest = null;
            float lowest = float.MaxValue;
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var relationship = state.FindRelationship(state.playerCountryId, country.id);
                if (relationship == null || relationship.relations >= lowest) continue;
                lowest = relationship.relations;
                coldest = country;
            }
            return coldest;
        }
    }
}
