using System;
using Brink.Data;
using Random = System.Random;

namespace Brink.Core
{
    /// <summary>
    /// A government's theory of how it wins, and whether that theory is working
    /// (GDD §24.1).
    ///
    /// Before this, an AI government was a list of month-to-month reactions:
    /// stability dipped so it consolidated, a rival looked strong so it countered.
    /// Averaged over a decade that produces a state with no discernible ambition,
    /// which is a problem for a game whose whole subject is ambition. A path makes
    /// a government *legible* — the player can work out that China is building for
    /// economic primacy rather than for war, and play against that specifically.
    ///
    /// Three properties matter and are tested:
    ///
    /// 1. **A path is chosen from endowments, not assigned.** A resource state
    ///    with no industry does not pursue economic primacy just because the roll
    ///    said so, and a landlocked minor power does not decide to dominate the
    ///    world militarily.
    /// 2. **Progress is measured from the same fog everyone else uses.** A
    ///    government judges how it is doing against *estimates* of its rivals, so
    ///    a deceived state can believe it is winning.
    /// 3. **Sustained failure forces a rethink.** A path that is not working is
    ///    abandoned, which is what stops a beaten state from marching into the sea
    ///    for thirty years.
    /// </summary>
    public static class AIStrategy
    {
        /// <summary>Governments do not pivot lightly; this is the minimum tenure.</summary>
        public const int MinimumMonthsOnPath = 30;

        /// <summary>Progress below this for long enough forces a reconsideration.</summary>
        public const float FailingProgress = 34f;

        /// <summary>
        /// Choose or reconsider this government's path.
        ///
        /// Called every month, but almost always does nothing: pivoting is
        /// expensive in a simulation the same way it is in life, and a government
        /// that re-planned every month would read as noise rather than as
        /// intention.
        /// </summary>
        public static void ReviewPath(GameState state, AIState ai, CountryState country, Random rng)
        {
            ai.monthsOnPath++;
            ai.pathProgress = ProgressOn(state, ai, country, ai.path);

            bool firstChoice = ai.monthsOnPath <= 1 && ai.disposition == 0f;
            bool longEnough = ai.monthsOnPath >= MinimumMonthsOnPath;
            bool failing = ai.pathProgress < FailingProgress && ai.monthsOnPath >= 12;

            // Losing a war is the one shock that forces a rethink immediately.
            // A state that has just been beaten and carries on exactly as before
            // is the single most obviously artificial thing an AI can do.
            bool beaten = RecentlyBeaten(state, country.id);

            if (!firstChoice && !longEnough && !failing && !beaten) return;

            if (ai.disposition == 0f)
                ai.disposition = (float)(rng.NextDouble() * 2.0 - 1.0) * 12f;

            var best = ai.path;
            float bestScore = float.MinValue;

            foreach (StrategicPath candidate in Enum.GetValues(typeof(StrategicPath)))
            {
                float score = Suitability(state, ai, country, candidate);

                // Hidden per-government tilt, so two similar countries do not
                // always reach the same conclusion in every playthrough.
                score += ai.disposition * WeightOfDisposition(candidate);

                // Inertia: staying the course is worth something, or a government
                // oscillates between two nearly-equal paths forever.
                if (candidate == ai.path && !failing && !beaten) score += 10f;

                score += (float)(rng.NextDouble() * 2.0 - 1.0) * 6f;

                if (score <= bestScore) continue;
                bestScore = score;
                best = candidate;
            }

            if (best == ai.path && !firstChoice) return;

            ai.path = best;
            ai.monthsOnPath = 0;
            ai.pathProgress = ProgressOn(state, ai, country, best);
        }

        /// <summary>
        /// How well this path suits the country that would pursue it.
        ///
        /// Endowments first, personality second. A government's character shapes
        /// *how* it pursues a path far more than which one is available to it —
        /// wanting to dominate militarily does not give you an army.
        /// </summary>
        public static float Suitability(GameState state, AIState ai, CountryState country,
            StrategicPath path)
        {
            var profile = ai.profile;
            var pillars = country.pillars;

            switch (path)
            {
                case StrategicPath.EconomicPrimacy:
                    return pillars.economy * 0.8f
                           + country.resources.industrialCapacity * 0.35f
                           + TradeWeight(state, country.id) * 0.25f
                           - profile.aggression * 0.15f;

                case StrategicPath.MilitaryDominance:
                    // The weakness penalty is the important term. Without it an
                    // aggressive personality was enough to make a minor power
                    // decide to dominate the world, and wanting an army is not
                    // the same as having one.
                    return pillars.military * 0.85f
                           + profile.aggression * 0.45f
                           + country.resources.manpower * 0.012f
                           - profile.caution * 0.2f
                           - Math.Max(0f, 45f - pillars.military) * 1.4f;

                case StrategicPath.RegionalHegemony:
                    // The path for a state that is strong near home and nowhere
                    // else — which reach makes measurable rather than notional.
                    return pillars.military * 0.4f
                           + pillars.diplomacy * 0.3f
                           + NeighbourhoodDominance(state, country) * 0.55f
                           + profile.opportunism * 0.25f;

                case StrategicPath.TechnologicalEdge:
                    return pillars.intelligence * 0.4f
                           + pillars.economy * 0.35f
                           + country.resources.industrialCapacity * 0.25f
                           + profile.patience * 0.4f;

                case StrategicPath.InstitutionalWeight:
                    return pillars.diplomacy * 0.9f
                           + TreatyCount(state, country.id) * 9f
                           + profile.patience * 0.25f
                           - profile.aggression * 0.35f;

                default: // Survival
                    // Deliberately unattractive to a healthy state and the obvious
                    // answer for a wounded one, so it reads as a condition a
                    // country falls into rather than a strategy it picks.
                    return 30f
                           + Math.Max(0f, 55f - country.stability) * 1.1f
                           + Math.Max(0f, 45f - pillars.military) * 0.5f
                           + ThreatPressure(state, country.id) * 0.6f
                           + profile.caution * 0.2f;
            }
        }

        /// <summary>
        /// 0..100 how well the path is going, judged from what this government can
        /// actually see. A deceived state can believe it is winning.
        /// </summary>
        public static float ProgressOn(GameState state, AIState ai, CountryState country,
            StrategicPath path)
        {
            switch (path)
            {
                case StrategicPath.EconomicPrimacy:
                    return Normalized(country.pillars.economy,
                        BestRivalPillar(state, ai, country, IntelDomain.Economic));

                case StrategicPath.MilitaryDominance:
                    return Normalized(country.pillars.military,
                        BestRivalPillar(state, ai, country, IntelDomain.Military));

                case StrategicPath.RegionalHegemony:
                    return Clamp(NeighbourhoodDominance(state, country) * 1.4f);

                case StrategicPath.TechnologicalEdge:
                    return Clamp(30f + MaturedCapabilities(country) * 11f);

                case StrategicPath.InstitutionalWeight:
                    return Clamp(20f + TreatyCount(state, country.id) * 12f
                                 + country.pillars.diplomacy * 0.35f);

                default: // Survival
                    return Clamp(country.stability * 0.5f + country.nationalUnity * 0.3f
                                 + Math.Max(0f, 40f - ThreatPressure(state, country.id)) * 0.5f);
            }
        }

        /// <summary>
        /// How much this path wants a given objective pursued (GDD §24.1).
        ///
        /// This is where a path stops being a label. A government on
        /// EconomicPrimacy genuinely will not reach for a war it could win,
        /// because the scoring puts a claim below the things that compound.
        /// </summary>
        public static float ObjectiveBias(StrategicPath path, AIObjectiveType objective)
        {
            switch (path)
            {
                case StrategicPath.EconomicPrimacy:
                    switch (objective)
                    {
                        case AIObjectiveType.BuildCapability: return 16f;
                        case AIObjectiveType.SecureResources: return 20f;
                        case AIObjectiveType.InsulateEconomy: return 14f;
                        case AIObjectiveType.ExpandInfluence: return 10f;
                        case AIObjectiveType.AssertClaim: return -22f;
                        default: return 0f;
                    }

                case StrategicPath.MilitaryDominance:
                    switch (objective)
                    {
                        case AIObjectiveType.AssertClaim: return 22f;
                        case AIObjectiveType.CounterRival: return 14f;
                        case AIObjectiveType.BuildCapability: return 10f;
                        case AIObjectiveType.HardenDefenses: return 6f;
                        case AIObjectiveType.ExpandInfluence: return -8f;
                        default: return 0f;
                    }

                case StrategicPath.RegionalHegemony:
                    switch (objective)
                    {
                        case AIObjectiveType.AssertClaim: return 14f;
                        case AIObjectiveType.ExpandInfluence: return 14f;
                        case AIObjectiveType.CounterRival: return 10f;
                        default: return 0f;
                    }

                case StrategicPath.TechnologicalEdge:
                    switch (objective)
                    {
                        case AIObjectiveType.BuildCapability: return 22f;
                        case AIObjectiveType.HardenSecurity: return 14f;
                        case AIObjectiveType.AssertClaim: return -16f;
                        default: return 0f;
                    }

                case StrategicPath.InstitutionalWeight:
                    switch (objective)
                    {
                        case AIObjectiveType.ExpandInfluence: return 24f;
                        case AIObjectiveType.InsulateEconomy: return 8f;
                        case AIObjectiveType.AssertClaim: return -26f;
                        case AIObjectiveType.CounterRival: return -8f;
                        default: return 0f;
                    }

                default: // Survival
                    switch (objective)
                    {
                        case AIObjectiveType.ConsolidateHome: return 22f;
                        case AIObjectiveType.HardenDefenses: return 20f;
                        case AIObjectiveType.SecureResources: return 12f;
                        case AIObjectiveType.ExpandInfluence: return 8f;
                        case AIObjectiveType.AssertClaim: return -30f;
                        default: return 0f;
                    }
            }
        }

        /// <summary>Plain-language label, for the intelligence readout and the chronicle.</summary>
        public static string Describe(StrategicPath path)
        {
            switch (path)
            {
                case StrategicPath.EconomicPrimacy: return "economic primacy";
                case StrategicPath.MilitaryDominance: return "military dominance";
                case StrategicPath.RegionalHegemony: return "regional hegemony";
                case StrategicPath.TechnologicalEdge: return "a technological edge";
                case StrategicPath.InstitutionalWeight: return "institutional weight";
                default: return "survival";
            }
        }

        // ---------- measurements ----------

        /// <summary>Which paths the hidden disposition pushes toward. Sum is zero.</summary>
        static float WeightOfDisposition(StrategicPath path)
        {
            switch (path)
            {
                case StrategicPath.MilitaryDominance: return 1f;
                case StrategicPath.EconomicPrimacy: return 0.6f;
                case StrategicPath.RegionalHegemony: return 0.2f;
                case StrategicPath.TechnologicalEdge: return -0.2f;
                case StrategicPath.InstitutionalWeight: return -0.6f;
                default: return -1f;
            }
        }

        /// <summary>
        /// The strongest rival's pillar *as this government believes it to be*.
        /// Routed through the estimate so the fog applies to self-assessment too.
        /// </summary>
        static float BestRivalPillar(GameState state, AIState ai, CountryState country,
            IntelDomain domain)
        {
            float best = 1f;
            foreach (var other in state.countries)
            {
                if (other.id == country.id) continue;
                float perceived = AISystem.PerceivedStrength(state, country.id, other, domain);
                if (perceived > best) best = perceived;
            }
            return best;
        }

        /// <summary>50 when level with the best rival, 100 when twice as strong.</summary>
        static float Normalized(float ours, float theirs)
            => Clamp(50f * (ours / Math.Max(1f, theirs)));

        /// <summary>
        /// How much of what this country can actually reach it dominates. Uses the
        /// same reach model operations do, so "regional" means the region the
        /// force can be brought to bear in rather than a hand-drawn one.
        /// </summary>
        public static float NeighbourhoodDominance(GameState state, CountryState country)
        {
            float weighted = 0f;
            float total = 0f;

            foreach (var other in state.countries)
            {
                if (other.id == country.id) continue;
                float reach = GeographySystem.ReachFactorTo(state, country.id, other.id);
                if (reach < 0.6f) continue; // not our neighbourhood

                float relationship = state.FindRelationship(country.id, other.id)?.relations ?? 50f;
                // Our own pillar against our *estimate* of theirs — the fog every
                // other read in this file already uses. Reading the neighbour's
                // true military figure here decided which path a government
                // adopted and whether it judged the path to be failing.
                float edge = country.pillars.military
                             - AISystem.PerceivedStrength(state, country.id, other, IntelDomain.Military);

                weighted += reach * (Clamp(50f + edge) * 0.6f + relationship * 0.4f);
                total += reach;
            }

            return total < 0.01f ? 25f : weighted / total;
        }

        static float TradeWeight(GameState state, string countryId)
        {
            float total = 0f;
            foreach (var link in state.trade)
                if (link.Involves(countryId)) total += link.volume;
            return Math.Min(100f, total * 0.35f);
        }

        static int TreatyCount(GameState state, string countryId)
        {
            int count = 0;
            foreach (var treaty in state.treaties)
                if (treaty.Involves(countryId) && !treaty.broken) count++;
            return count;
        }

        static int MaturedCapabilities(CountryState country)
        {
            int count = 0;
            foreach (var capability in country.technology.capabilities)
                if (capability.maturity > 60f) count++;
            return count;
        }

        /// <summary>How hard the world is currently leaning on this state.</summary>
        static float ThreatPressure(GameState state, string countryId)
        {
            float pressure = 0f;
            foreach (var confrontation in state.confrontations)
                if (!confrontation.resolved && confrontation.Involves(countryId)) pressure += 30f;
            foreach (var sanction in state.sanctions)
                if (sanction.targetId == countryId) pressure += sanction.Weight * 0.4f;
            return Math.Min(100f, pressure);
        }

        /// <summary>Did this state lose a confrontation in the last couple of years?</summary>
        static bool RecentlyBeaten(GameState state, string countryId)
        {
            foreach (var confrontation in state.confrontations)
            {
                if (!confrontation.resolved) continue;
                if (!confrontation.Involves(countryId)) continue;
                if (state.date.MonthsSince(confrontation.startDate) > 30) continue;

                bool weInitiated = confrontation.initiatorId == countryId;
                float ourMomentum = weInitiated ? confrontation.momentum : -confrontation.momentum;
                if (ourMomentum < -20f) return true;
            }
            return false;
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
