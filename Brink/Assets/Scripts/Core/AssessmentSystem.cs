using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Scores the first-launch assessment and generates the starting posting
    /// (GDD §5). Hidden scoring is never surfaced as numbers, and controlled
    /// randomness stops the assessment becoming a solved recipe.
    /// </summary>
    public static class AssessmentSystem
    {
        /// <summary>
        /// Score a set of answers (one option index per question, in catalog order).
        /// </summary>
        public static AssessmentResult Evaluate(IList<int> answers, int seed)
        {
            var rng = new Random(seed);
            var doctrine = new DoctrineProfile();
            var questions = AssessmentCatalog.Questions;

            for (int i = 0; i < questions.Count && i < answers.Count; i++)
            {
                int choice = answers[i];
                if (choice < 0 || choice >= questions[i].options.Length) continue;
                var scores = questions[i].options[choice].scores;

                doctrine.force += scores.force;
                doctrine.secrecy += scores.secrecy;
                doctrine.coalition += scores.coalition;
                doctrine.order += scores.order;
                doctrine.horizon += scores.horizon;

                doctrine.militaryAffinity += scores.militaryAffinity;
                doctrine.economyAffinity += scores.economyAffinity;
                doctrine.intelligenceAffinity += scores.intelligenceAffinity;
                doctrine.diplomacyAffinity += scores.diplomacyAffinity;
                doctrine.governmentAffinity += scores.governmentAffinity;
            }

            // Controlled randomness: identical answers do not produce identical
            // postings, so the assessment cannot be reduced to a recipe (GDD §5).
            float Jitter() => (float)(rng.NextDouble() * 2.0 - 1.0) * 1.8f;
            doctrine.force += Jitter();
            doctrine.secrecy += Jitter();
            doctrine.coalition += Jitter();
            doctrine.order += Jitter();
            doctrine.horizon += Jitter();
            doctrine.militaryAffinity += Jitter();
            doctrine.economyAffinity += Jitter();
            doctrine.intelligenceAffinity += Jitter();
            doctrine.diplomacyAffinity += Jitter();
            doctrine.governmentAffinity += Jitter();

            var result = new AssessmentResult
            {
                doctrine = doctrine,
                assignedCountryId = AssignPosting(doctrine),
                startingPriority = PriorityFor(doctrine),
                traits = TraitsFor(doctrine, rng)
            };

            result.doctrineText = DescribeDoctrine(doctrine);
            result.classificationText = DescribeClassification(doctrine);
            return result;
        }

        /// <summary>
        /// Match the operator profile to the nation whose strategic character
        /// it best suits. The player may override this before accepting.
        ///
        /// Defaults to the Standard roster — the world size a new game proposes.
        /// The assessment screen recomputes through the overload when the
        /// operator picks a different theatre scale, so a posting can never
        /// name a country the chosen world does not contain.
        /// </summary>
        public static string AssignPosting(DoctrineProfile doctrine)
            => AssignPosting(doctrine, WorldFactory.RosterFor(WorldSize.Standard));

        /// <summary>Best-fit posting restricted to one world's roster.</summary>
        public static string AssignPosting(DoctrineProfile doctrine, string[] allowedIds)
        {
            var allowed = new HashSet<string>(allowedIds);
            float best = float.MinValue;
            string bestId = WorldFactory.PlayerCountryId;

            foreach (var profile in WorldFactory.Profiles)
            {
                if (!allowed.Contains(profile.id)) continue;
                // Fit = how well the operator's instincts match the nation's
                // existing strengths and institutional character.
                float fit =
                    doctrine.militaryAffinity * (profile.military / 100f)
                    + doctrine.economyAffinity * (profile.economy / 100f)
                    + doctrine.intelligenceAffinity * (profile.intelligence / 100f)
                    + doctrine.diplomacyAffinity * (profile.diplomacy / 100f)
                    + doctrine.governmentAffinity * (profile.government / 100f);

                bool elective = profile.governmentType == GovernmentType.PresidentialRepublic
                                || profile.governmentType == GovernmentType.ParliamentaryRepublic;

                // An order-first operator fits a centralized system; a coalition
                // instinct fits an elective one.
                fit += elective ? doctrine.coalition * 0.35f : doctrine.order * 0.35f;
                fit += elective ? -doctrine.order * 0.15f : -doctrine.coalition * 0.15f;

                if (fit > best) { best = fit; bestId = profile.id; }
            }
            return bestId;
        }

        public static NationalPriority PriorityFor(DoctrineProfile doctrine)
        {
            switch (doctrine.StrongestPillar())
            {
                case Pillar.Military:
                case Pillar.Intelligence: return NationalPriority.Security;
                case Pillar.Economy: return NationalPriority.Prosperity;
                case Pillar.Diplomacy: return NationalPriority.Influence;
                default: return NationalPriority.Cohesion;
            }
        }

        /// <summary>
        /// National traits carry a strength and a matching vulnerability
        /// (GDD §10). Two are drawn from the profile, with some randomness.
        /// </summary>
        public static List<NationalTrait> TraitsFor(DoctrineProfile doctrine, Random rng)
        {
            var pool = new List<NationalTrait>();

            void Consider(bool condition, string id, string name, string description)
            {
                if (condition) pool.Add(new NationalTrait { id = id, name = name, description = description });
            }

            Consider(doctrine.force > 4f, "TRAIT_FORWARD", "Forward Posture",
                "Forces are held at high readiness. Deterrence is credible; upkeep is expensive and neighbors are wary.");
            Consider(doctrine.secrecy > 4f, "TRAIT_OPAQUE", "Opaque State",
                "The state guards its own information well. Foreign services struggle; allies trust us less.");
            Consider(doctrine.coalition > 4f, "TRAIT_COALITION", "Coalition Instinct",
                "Partnership is reflexive. Relationships start warmer; unilateral action costs more standing.");
            Consider(doctrine.order > 4f, "TRAIT_ORDER", "Institutional Discipline",
                "The machinery of state is orderly. Stability is high; reform meets entrenched resistance.");
            Consider(doctrine.horizon > 4f, "TRAIT_PATIENT", "Long Horizon",
                "Planning outlasts administrations. Investment compounds; immediate crises are handled less deftly.");
            Consider(doctrine.economyAffinity > 5f, "TRAIT_INDUSTRIAL", "Industrial Depth",
                "Productive capacity runs deep. Sustainment is strong; the economy is exposed to trade shocks.");
            Consider(doctrine.intelligenceAffinity > 5f, "TRAIT_TRADECRAFT", "Institutional Tradecraft",
                "Collection and counterintelligence are cultural. Reporting is sharper; scandal is costlier.");
            Consider(doctrine.diplomacyAffinity > 5f, "TRAIT_CONVENING", "Convening Power",
                "Others come to the table when we call. Diplomacy is potent; expectations are high.");
            Consider(doctrine.militaryAffinity > 5f, "TRAIT_MARTIAL", "Martial Tradition",
                "Service is respected. War support holds longer; militarism colors every option.");

            // Always have something to draw from.
            Consider(true, "TRAIT_PRAGMATIC", "Pragmatic Establishment",
                "No fixed doctrine. Flexible across pillars; excels at none by default.");

            var chosen = new List<NationalTrait>();
            while (chosen.Count < 2 && pool.Count > 0)
            {
                int index = rng.Next(pool.Count);
                chosen.Add(pool[index]);
                pool.RemoveAt(index);
            }
            return chosen;
        }

        // ---------- in-universe descriptions (never raw numbers) ----------

        public static string DescribeDoctrine(DoctrineProfile doctrine)
        {
            var parts = new List<string>();

            parts.Add(doctrine.force > 5f ? "Inclined to apply pressure early."
                : doctrine.force < 1f ? "Reluctant to reach for coercion."
                : "Measured in the use of pressure.");

            parts.Add(doctrine.secrecy > 5f ? "Prefers instruments that leave no fingerprints."
                : doctrine.secrecy < 1f ? "Prefers to act in the open."
                : "Comfortable with both open and quiet methods.");

            parts.Add(doctrine.coalition > 5f ? "Works through partners by instinct."
                : doctrine.coalition < 1f ? "Trusts our own hand above any partner's."
                : "Builds partnerships where they serve.");

            parts.Add(doctrine.horizon > 5f ? "Plans well beyond the current administration."
                : doctrine.horizon < 0f ? "Focused on the immediate result."
                : "Balances the near and far term.");

            return string.Join(" ", parts);
        }

        public static string DescribeClassification(DoctrineProfile doctrine)
        {
            switch (doctrine.StrongestPillar())
            {
                case Pillar.Military: return "OPERATOR CLASSIFICATION: STRATEGIC COMMAND TRACK";
                case Pillar.Economy: return "OPERATOR CLASSIFICATION: ECONOMIC STATECRAFT TRACK";
                case Pillar.Intelligence: return "OPERATOR CLASSIFICATION: CLANDESTINE SERVICE TRACK";
                case Pillar.Diplomacy: return "OPERATOR CLASSIFICATION: FOREIGN AFFAIRS TRACK";
                default: return "OPERATOR CLASSIFICATION: EXECUTIVE GOVERNANCE TRACK";
            }
        }

        /// <summary>
        /// Apply the assessment to a freshly generated world: starting priority,
        /// national traits and their structural effects.
        /// </summary>
        public static void ApplyToWorld(GameState state, AssessmentResult result)
        {
            var player = state.PlayerCountry;
            if (player == null || result == null) return;

            state.assessment = result;
            player.government.leader.priority = result.startingPriority;

            foreach (var trait in result.traits)
                ApplyTrait(state, player, trait);

            state.AddChronicle(ChronicleCategory.System, player.id,
                $"Operator assigned to {player.displayName}. {result.classificationText}");
            foreach (var trait in result.traits)
                state.AddChronicle(ChronicleCategory.System, player.id, $"National character: {trait.name}.");
        }

        /// <summary>Each trait grants a real strength and a real vulnerability.</summary>
        static void ApplyTrait(GameState state, CountryState player, NationalTrait trait)
        {
            switch (trait.id)
            {
                case "TRAIT_FORWARD":
                    player.military.ground.readiness = Clamp(player.military.ground.readiness + 10f);
                    player.military.air.readiness = Clamp(player.military.air.readiness + 10f);
                    player.resources.treasury -= 150f;
                    RaiseThreatPerceptionOfUs(state, player.id, 8f);
                    break;

                case "TRAIT_OPAQUE":
                    player.counterIntel.counterIntelligence = Clamp(player.counterIntel.counterIntelligence + 15f);
                    AdjustAllRelationships(state, player.id, trustDelta: -8f);
                    break;

                case "TRAIT_COALITION":
                    AdjustAllRelationships(state, player.id, relationsDelta: 10f, trustDelta: 6f);
                    break;

                case "TRAIT_ORDER":
                    player.stability = Clamp(player.stability + 10f);
                    player.government.legislativeSupport = Clamp(player.government.legislativeSupport + 5f);
                    player.pillars.government = Clamp(player.pillars.government - 3f); // reform resistance
                    break;

                case "TRAIT_PATIENT":
                    player.economy.confidence = Clamp(player.economy.confidence + 8f);
                    player.resources.industrialCapacity = Clamp(player.resources.industrialCapacity + 5f);
                    player.governmentApproval = Clamp(player.governmentApproval - 5f);
                    break;

                case "TRAIT_INDUSTRIAL":
                    player.resources.industrialCapacity = Clamp(player.resources.industrialCapacity + 12f);
                    player.economy.GetSector(EconomicSector.Industry).output =
                        Clamp(player.economy.GetSector(EconomicSector.Industry).output + 8f);
                    player.resources.strategicMaterials = Clamp(player.resources.strategicMaterials - 8f);
                    break;

                case "TRAIT_TRADECRAFT":
                    player.pillars.intelligence = Clamp(player.pillars.intelligence + 6f);
                    player.counterIntel.counterIntelligence = Clamp(player.counterIntel.counterIntelligence + 8f);
                    player.nationalUnity = Clamp(player.nationalUnity - 5f);
                    break;

                case "TRAIT_CONVENING":
                    player.pillars.diplomacy = Clamp(player.pillars.diplomacy + 8f);
                    AdjustAllRelationships(state, player.id, relationsDelta: 5f);
                    player.governmentApproval = Clamp(player.governmentApproval - 4f);
                    break;

                case "TRAIT_MARTIAL":
                    player.warSupport = Clamp(player.warSupport + 15f);
                    player.pillars.military = Clamp(player.pillars.military + 4f);
                    player.pillars.diplomacy = Clamp(player.pillars.diplomacy - 4f);
                    break;

                default: // TRAIT_PRAGMATIC
                    player.resources.treasury += 120f;
                    break;
            }
        }

        static void AdjustAllRelationships(GameState state, string countryId,
            float relationsDelta = 0f, float trustDelta = 0f)
        {
            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(countryId)) continue;
                relationship.relations = Clamp(relationship.relations + relationsDelta);
                relationship.trust = Clamp(relationship.trust + trustDelta);
            }
        }

        static void RaiseThreatPerceptionOfUs(GameState state, string countryId, float amount)
        {
            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(countryId)) continue;
                if (relationship.countryA == countryId)
                    relationship.threatPerceptionOfA = Clamp(relationship.threatPerceptionOfA + amount);
                else
                    relationship.threatPerceptionOfB = Clamp(relationship.threatPerceptionOfB + amount);
            }
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
