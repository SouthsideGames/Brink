using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Goal-driven government AI (GDD Phase 9, §24).
    ///
    /// Every AI government evaluates its interests, threats and opportunities,
    /// generates strategic objectives, and pursues them through the same five
    /// pillars the player uses. Crucially it reasons from *estimates*, not truth:
    /// it reads the same imperfect intelligence layer and can be deceived. Leader
    /// personality produces understandable mistakes, and the spread of profiles
    /// keeps the world from converging into one optimal strategy.
    /// </summary>
    public static class AISystem
    {
        /// <summary>Seed AI reasoning state for every non-player country.</summary>
        public static void SeedAI(GameState state, Random rng)
        {
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;

                // Authored temperament, jittered — not rolled from nothing.
                //
                // Personality used to be drawn uniformly in [20,80], so a country
                // was a different country in every save. That quietly defeated
                // the anti-memorisation design (spec 06 §7b): the world is meant
                // to *answer* a player who repeats an opening, and it cannot do
                // that if the player has no stable read on who they are
                // answering. Learning that this state is patient and cautious has
                // to still be true next time.
                //
                // The jitter is deliberately small — enough that two saves are
                // not identical, not enough to make a nation unrecognisable.
                var profile = WorldFactory.FindProfile(country.id);
                float Vary(float authored) => (float)Math.Round(
                    Clamp(authored + (float)(rng.NextDouble() * 2.0 - 1.0) * 8f), 1);

                state.aiStates.Add(new AIState
                {
                    countryId = country.id,
                    profile = new AIProfile
                    {
                        aggression = Vary(profile?.aggression ?? 50f),
                        caution = Vary(profile?.caution ?? 50f),
                        opportunism = Vary(profile?.opportunism ?? 50f),
                        patience = Vary(profile?.patience ?? 50f)
                    }
                });
            }
        }

        /// <summary>Actions an AI may take per month — a reasoning-quality budget.</summary>
        public static int ActionBudget(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Ruthless: return 3;
                case Difficulty.Challenging: return 2;
                default: return 1;
            }
        }

        /// <summary>
        /// How much weight the AI gives to long-horizon reasoning. Higher
        /// difficulty plans further ahead and coordinates across pillars better.
        /// </summary>
        public static float PlanningHorizon(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Ruthless: return 1f;
                case Difficulty.Challenging: return 0.7f;
                default: return 0.45f;
            }
        }

        // ---------- monthly reasoning ----------

        public static void MonthlyThink(GameState state)
        {
            int monthIndex = state.date.MonthsSince(state.startDate);

            foreach (var ai in state.aiStates)
            {
                var country = state.FindCountry(ai.countryId);
                if (country == null) continue;

                var rng = new Random(unchecked(state.rngSeed * 6700417 + monthIndex * 733 + Hash.Of(ai.countryId)));

                // Observe, then decide what winning looks like, then plan. In that
                // order: a government's read of the world has to be current before
                // it revisits its strategy, or it re-plans on last month's picture.
                UpdatePlayerAssessment(state, ai);
                AIPrediction.Observe(state, ai);
                AIStrategy.ReviewPath(state, ai, country, rng);

                FormObjectives(state, ai, country, rng);
                TrackRivalries(ai);
                Act(state, ai, country, rng);
                ManageOngoingConfrontation(state, ai, country, rng);
                ConsiderRelinquishment(state, country);
                ConsiderStrategicInstruments(state, ai, country, rng);
                ConsiderDetente(state, ai, country, rng);
                ConsiderResearch(state, ai, country, rng);
                ConsiderCoalition(state, ai, country, rng);
                ManageTheBooks(state, ai, country, rng);
                ConsiderRecognition(state, ai, country, rng);
            }
        }

        /// <summary>
        /// Systemic pattern recognition (GDD §24.2): the AI reads what the player
        /// has actually been seen doing, not hidden player state.
        /// </summary>
        static void UpdatePlayerAssessment(GameState state, AIState ai)
        {
            var assessment = ai.playerAssessment;
            string playerId = state.playerCountryId;

            int aggressive = 0, coercion = 0, cooperative = 0;

            // Governments judge a pattern, not a permanent record. Counting every
            // confrontation ever opened meant nine wars over thirty years pinned
            // the player at maximum perceived aggression for the rest of the save,
            // however they behaved afterwards — which removes the whole point of
            // GDD §24.2, that a reputation can be established and then broken.
            const int MemoryWindowMonths = 120;
            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.initiatorId != playerId) continue;
                if (state.date.MonthsSince(confrontation.startDate) > MemoryWindowMonths) continue;
                aggressive++;
            }

            foreach (var sanction in state.sanctions)
                if (sanction.senderId == playerId) coercion++;

            foreach (var treaty in state.treaties)
            {
                if (!treaty.Involves(playerId)) continue;
                if (treaty.broken && treaty.brokenBy == playerId) aggressive++;
                else if (!treaty.broken) cooperative++;
            }

            // Exposed covert action is observable; unexposed action is not.
            int covert = 0;
            foreach (var network in state.networks)
                if (network.ownerId == playerId && network.compromised) covert++;

            assessment.observedAggressiveActs = aggressive;
            assessment.observedEconomicCoercion = coercion;
            assessment.observedCovertActs = covert;
            assessment.observedCooperativeActs = cooperative;

            float pattern = aggressive * 12f + coercion * 8f + covert * 10f - cooperative * 6f;
            assessment.perceivedAggression = Clamp(pattern);

            // A player who looks dangerous raises threat perception over time.
            var relationship = state.FindRelationship(ai.countryId, playerId);
            if (relationship != null && assessment.perceivedAggression > 40f)
            {
                bool playerIsA = relationship.countryA == playerId;
                float current = playerIsA ? relationship.threatPerceptionOfA : relationship.threatPerceptionOfB;
                float raised = Approach(current, assessment.perceivedAggression, 0.05f);
                if (playerIsA) relationship.threatPerceptionOfA = raised;
                else relationship.threatPerceptionOfB = raised;
            }
        }

        /// <summary>Score candidate objectives and keep the best one or two.</summary>
        static void FormObjectives(GameState state, AIState ai, CountryState country, Random rng)
        {
            foreach (var objective in ai.objectives) objective.monthsPursued++;

            // Patient governments stay the course; impatient ones churn.
            int reviewInterval = 3 + (int)(ai.profile.patience / 20f);
            if (ai.objectives.Count > 0 && ai.objectives[0].monthsPursued % reviewInterval != 0)
                return;

            var candidates = new List<AIObjective>();
            float horizon = PlanningHorizon(state.difficulty);

            // --- domestic pressure ---
            //
            // Losing your own chamber or inner circle is a domestic crisis even
            // when the public is content, and it was not one of the tests here.
            // Since stability and approval now have a restoring force, a
            // government that governs adequately keeps both comfortably above
            // these thresholds — so without the backing term this objective, and
            // every instrument that hangs off it, went almost unreached.
            var government = country.government;
            float backing = government.IsElective
                ? government.legislativeSupport
                : government.eliteCohesion;

            if (country.stability < 50f || country.governmentApproval < 40f || backing < 60f)
                candidates.Add(new AIObjective
                {
                    type = AIObjectiveType.ConsolidateHome,
                    // Each term floored at zero. Unfloored, a government with
                    // healthy stability scored *negative* on it and subtracted
                    // from the case for shoring up a chamber that had genuinely
                    // turned on it — so once stability gained a restoring force
                    // and stopped sitting low, this objective became unreachable
                    // and every instrument hanging off it went with it.
                    priority = Math.Max(0f, 60f - country.stability)
                               + Math.Max(0f, 50f - country.governmentApproval) * 0.6f
                               + Math.Max(0f, 60f - backing) * 3f
                });

            // --- resource vulnerability ---
            if (country.resources.energy < 50f || country.resources.strategicMaterials < 45f)
                candidates.Add(new AIObjective
                {
                    type = AIObjectiveType.SecureResources,
                    priority = (55f - Math.Min(country.resources.energy, country.resources.strategicMaterials)) * 1.1f
                });

            // --- rivals, judged from estimates rather than truth ---
            foreach (var other in state.countries)
            {
                if (other.id == country.id) continue;

                var relationship = state.FindRelationship(country.id, other.id);
                if (relationship == null) continue;

                float perceivedStrength = PerceivedStrength(state, ai.countryId, other, IntelDomain.Military);
                float confidenceFactor = EstimateConfidence(state, ai.countryId, other.id, IntelDomain.Military);

                // A state that expects encirclement reads the same evidence as
                // more dangerous. Sensitivity, not paranoia: it still reasons
                // from what it can actually see.
                float threat = NationalTraitCatalog.ThreatSensitivity(country)
                               * relationship.ThreatPerceivedBy(country.id)
                               + Math.Max(0f, perceivedStrength - country.pillars.military) * 0.6f
                               + (50f - relationship.relations) * 0.4f;

                // Cautious governments discount conclusions drawn from thin reporting.
                float cautionPenalty = (1f - confidenceFactor) * ai.profile.caution * 0.5f;

                // Knowing that someone is close to being able to destroy you is
                // not one input among many — it reorders everything (GDD §21).
                float programme = DetectedProgramme(state, country.id, other.id);
                threat += programme;

                // Knowing a rival is close to an instrument that ends you is not
                // a reason to be *more worried* — it is a reason to do something
                // specific about it (spec 14 §8). Until this existed, detection
                // raised threat and produced no distinguishable behaviour, so the
                // most consequential thing intelligence can tell a government
                // changed nothing about what that government did.
                //
                // Deliberately not gated on caution: this is the one conclusion
                // a cautious government is *more* moved by, not less.
                if (programme > 15f)
                    candidates.Add(new AIObjective
                    {
                        type = AIObjectiveType.PreemptProgramme,
                        targetId = other.id,
                        priority = 60f + programme * 1.6f
                    });

                if (threat - cautionPenalty > 35f)
                    candidates.Add(new AIObjective
                    {
                        type = AIObjectiveType.CounterRival,
                        targetId = other.id,
                        priority = (threat - cautionPenalty) * (0.7f + ai.profile.aggression / 200f)
                    });

                // Opportunity: a weakened rival invites pressure. Poor reporting
                // makes a government hesitant, but does not paralyze it.
                // A power visibly tied down elsewhere is weak *here*, whatever
                // its headline strength (GDD §16). This is the other half of
                // theatres: a war on one side of the world becomes somebody
                // else's opening on the other, which is the thing that makes the
                // map structural rather than a picture.
                float perceivedWeakness = Math.Max(0f, country.pillars.military - perceivedStrength);
                if (TheatreSystem.IsOverstretched(state, other.id))
                    perceivedWeakness += TheatreSystem.TotalCommitment(state, other.id) * 9f;

                // A claim needs either an exploitable weakness or a genuine
                // rivalry. The weakness-only gate meant two well-matched rivals
                // could glare at each other for thirty years and never once
                // collide — measured: 0–1 AI-vs-AI wars across three 30-year
                // seeds, which is the whole world at peace with itself while
                // sanctioning itself into depression. Peer rivals do fight; what
                // they need is a reason, and coldness plus a resource prize is
                // one.
                bool deepRivalry = relationship.relations < 25f;
                if ((perceivedWeakness > 8f || deepRivalry) && relationship.relations < 50f)
                {
                    // Ambition is bounded by reach (GDD §16). A government does
                    // not press a claim it has no way to prosecute, so a weak
                    // neighbour is a far more attractive target than an equally
                    // weak state on the other side of the world. Without this,
                    // regional powers picked fights across the planet and
                    // geography was invisible in how the world behaved.
                    //
                    // Applied to AssertClaim only. CounterRival stays unweighted:
                    // a distant threat is still a threat, and the answer to one
                    // you cannot reach is sanctions, alignment and collection —
                    // all of which that objective can already choose.
                    float reach = GeographySystem.ReachFactorTo(state, country.id, other.id);

                    candidates.Add(new AIObjective
                    {
                        type = AIObjectiveType.AssertClaim,
                        targetId = other.id,
                        priority = (perceivedWeakness * 1.5f * (ai.profile.opportunism / 100f)
                                    * (0.6f + horizon * 0.8f)
                                    * (0.5f + 0.5f * confidenceFactor)
                                    + (40f - relationship.relations) * 0.35f // hostility invites pressure
                                    + ResourcePrize(state, country, other.id) * 22f) // their ground answers our shortfall
                                   * reach
                    });
                }

                // Partnership with the friendly and the useful.
                if (DiplomacySystem.Permitted(state, relationship, relationship.relations) > 55f
                    && state.FindTreaty(country.id, other.id) == null)
                    candidates.Add(new AIObjective
                    {
                        type = AIObjectiveType.ExpandInfluence,
                        targetId = other.id,
                        priority = DiplomacySystem.Permitted(state, relationship, relationship.relations) * 0.5f * (0.6f + horizon * 0.6f)
                    });
            }

            // --- counter-play: prepare for what we expect them to do (GDD §24.2) ---
            AddCounterObjectives(state, ai, country, candidates);

            // --- always a fallback ---
            candidates.Add(new AIObjective
            {
                type = AIObjectiveType.BuildCapability,
                priority = 30f + (float)rng.NextDouble() * 12f
            });

            // A government pursues what its strategy actually wants. This is what
            // makes a path more than a label: a state on Economic Primacy will
            // decline a war it could win, because a claim scores below the things
            // that compound.
            foreach (var candidate in candidates)
                candidate.priority += AIStrategy.ObjectiveBias(ai.path, candidate.type);

            // Personality noise: governments are not identical optimizers.
            foreach (var candidate in candidates)
                candidate.priority += (float)(rng.NextDouble() * 2.0 - 1.0) * 12f;

            candidates.Sort((a, b) => b.priority.CompareTo(a.priority));

            // Reasoning quality is how many things a government can hold in mind at
            // once, so this has to be the action budget itself. It used to cap at
            // two while Ruthless was given three actions, which meant the hardest
            // difficulty's extra action point was unreachable through this path.
            // **One pre-emption at a time.**
            //
            // `PreemptProgramme` is raised once per detected foreign programme
            // and scores `60 + programme × 1.6` — a floor of 84, above almost
            // anything else the ladder produces. A state that had detected three
            // programmes therefore raised three objectives of the same type,
            // each of which outscored everything else, and they filled all two
            // or three slots between them.
            //
            // Measured (B1A, 8 seeds × 360 months): of 7,029 country-months
            // where a live `AssertClaim` lost the budget cut, `PreemptProgramme`
            // won 6,340 — 90.2% — and only 26 claims survived the cut in 240
            // world-years. The objective was not outranked by a government
            // weighing different priorities; it was crowded out by repeated
            // copies of one priority.
            //
            // The fix is to the *selection*, not the scoring: nothing here
            // changes what a candidate is worth, how many objectives a
            // government may hold, or what it knows about foreign programmes.
            // The strongest pre-emption still takes its slot — the list is
            // already sorted, so the first one reached is the highest-priority
            // one — and the slots it used to duplicate into are returned to the
            // ladder beneath it.
            int keep = ActionBudget(state.difficulty);
            var kept = new List<AIObjective>();
            bool preemptionTaken = false;
            for (int i = 0; i < candidates.Count && kept.Count < keep; i++)
            {
                var candidate = candidates[i];
                if (candidate.type == AIObjectiveType.PreemptProgramme)
                {
                    if (preemptionTaken) continue;
                    preemptionTaken = true;
                }
                kept.Add(candidate);
            }
            ai.objectives = kept;
        }

        /// <summary>
        /// Objectives raised by what we expect others to do, rather than by what
        /// they have already done (GDD §24.2).
        ///
        /// This is the whole point of building an opponent model. Without it the
        /// world only ever reacts, so a player can run the same opening in every
        /// playthrough and meet the same undefended world. With it, an operator
        /// who always reaches for covert action finds hardened security services
        /// waiting by the third year, and one who always rushes militarily finds
        /// fortified neighbours with friends. The counter is a function of what
        /// the player *does*, which is why resetting does not escape it.
        /// </summary>
        static void AddCounterObjectives(GameState state, AIState ai, CountryState country,
            List<AIObjective> candidates)
        {
            foreach (var other in state.countries)
            {
                if (other.id == country.id) continue;

                float attack = AIPrediction.Expectation(ai, other.id, PredictedMove.Attack);
                float coerce = AIPrediction.Expectation(ai, other.id, PredictedMove.Coerce);
                float subvert = AIPrediction.Expectation(ai, other.id, PredictedMove.Subvert);
                float court = AIPrediction.Expectation(ai, other.id, PredictedMove.Court);

                // A cautious government hesitates to act on an expectation, the
                // same way it hesitates to act on thin reporting elsewhere. It
                // does not refuse — it just needs more before it moves.
                float hesitation = ai.profile.caution * 0.18f;

                if (attack - hesitation > 25f)
                    candidates.Add(new AIObjective
                    {
                        type = AIObjectiveType.HardenDefenses,
                        targetId = other.id,
                        priority = (attack - hesitation) * 0.95f
                    });

                if (coerce - hesitation > 25f)
                    candidates.Add(new AIObjective
                    {
                        type = AIObjectiveType.InsulateEconomy,
                        targetId = other.id,
                        priority = (coerce - hesitation) * 0.8f
                    });

                // ×1.6, and steep on purpose: counter-play answers a pattern the
                // government has *observed*, and strong evidence must outbid
                // speculative ambition or the anti-memorisation design dies
                // quietly — when the world ran hotter, inflated war and rivalry
                // priorities pushed HardenSecurity out of the top-N cut and a
                // decade of caught operations hardened the world by 0.15 points.
                // Steeper scaling, NOT a flat floor: a floor was tried first and
                // hardened everyone against ordinary background suspicion, which
                // raised the no-subversion baseline five points and *shrank* the
                // player-specific signal it was meant to protect. The boost has
                // to live where the evidence is.
                if (subvert - hesitation > 20f)
                    candidates.Add(new AIObjective
                    {
                        type = AIObjectiveType.HardenSecurity,
                        targetId = other.id,
                        priority = (subvert - hesitation) * 1.6f
                    });

                // Someone building a bloc is answered by building one back, which
                // is why this raises an existing objective rather than a new verb.
                if (court - hesitation > 30f)
                    candidates.Add(new AIObjective
                    {
                        type = AIObjectiveType.ExpandInfluence,
                        targetId = other.id,
                        priority = (court - hesitation) * 0.55f
                    });
            }
        }

        /// <summary>
        /// Prepare for an attack we expect (GDD §24.2). Costs treasury and
        /// political capital, like everything else a government does — a state
        /// cannot armour itself for free any more than the player can.
        /// </summary>
        /// <summary>
        /// Treasury a government keeps in hand before discretionary counter-play
        /// spending. Without it, threat-inflated hardening spent every state to
        /// zero the month money appeared — measured on seed 90210, the whole
        /// world ran at ~50 treasury for a decade, so nothing treasury-gated
        /// (research funding, procurement, strategic instruments at 120) could
        /// ever fire again. A government does not spend its last coin on this
        /// month's fear; the reserve is what keeps the long game fundable while
        /// the short one is being played.
        /// </summary>
        public const float DiscretionaryReserve = 250f;

        static bool HardenDefenses(GameState state, AIState ai, CountryState country, Random rng)
        {
            if (country.resources.treasury < 160f + DiscretionaryReserve) return false;
            if (ai.politicalCapital < 1.2f) return false;

            country.resources.treasury -= 160f;
            ai.politicalCapital -= 1.2f;

            // Readiness moves toward its target rather than jumping, so this is a
            // posture decision rather than a number written into the force.
            if (country.military.posture == MilitaryPosture.Peacetime)
                country.military.posture = MilitaryPosture.Alert;

            country.military.missileDefense = Clamp(country.military.missileDefense + 3.5f);
            country.military.logistics = Clamp(country.military.logistics + 1.5f);

            // Fortify the most exposed thing we hold.
            StrategicLocation weakest = null;
            foreach (var location in state.locations)
            {
                if (location.ownerId != country.id) continue;
                if (weakest == null || location.defenseValue < weakest.defenseValue) weakest = location;
            }
            if (weakest != null) weakest.defenseValue = Clamp(weakest.defenseValue + 4f);

            return true;
        }

        /// <summary>
        /// Reduce our exposure to economic pressure we expect (GDD §24.2).
        /// Diversification is slow and expensive, which is why states so often
        /// leave it until the squeeze has started.
        /// </summary>
        static bool InsulateEconomy(GameState state, AIState ai, CountryState country, Random rng)
        {
            if (country.resources.treasury < 200f + DiscretionaryReserve) return false;
            country.resources.treasury -= 200f;

            // Spread trade rather than deepen it: a thinner link to the state we
            // fear, thicker links to everyone else.
            foreach (var link in state.trade)
            {
                if (!link.Involves(country.id)) continue;
                link.volume = Clamp(link.volume + 1.5f);
            }

            country.economy.confidence = Clamp(country.economy.confidence + 1.5f);
            country.resources.energy = Math.Min(
                EconomySystem.EnergyCeilingFor(state, country), country.resources.energy + 1.5f);
            return true;
        }

        /// <summary>
        /// Raise counterintelligence against subversion we expect (GDD §24.2).
        ///
        /// This is the one an operator notices most directly: run enough exposed
        /// covert operations and the world becomes measurably harder to run them
        /// against.
        /// </summary>
        static bool HardenSecurity(GameState state, AIState ai, CountryState country, Random rng)
        {
            if (country.resources.treasury < 90f + DiscretionaryReserve) return false;
            if (ai.politicalCapital < 0.8f) return false;

            country.resources.treasury -= 90f;
            ai.politicalCapital -= 0.8f;
            country.counterIntel.counterIntelligence =
                Clamp(country.counterIntel.counterIntelligence + 3.5f);
            return true;
        }

        /// <summary>Execute actions serving the current objectives.</summary>
        static void Act(GameState state, AIState ai, CountryState country, Random rng)
        {
            ai.actionsThisMonth = 0;
            int budget = ActionBudget(state.difficulty);

            foreach (var objective in ai.objectives)
            {
                if (ai.actionsThisMonth >= budget) break;

                int before = ai.actionsThisMonth;

                // Recorded so a live session can answer "is the world actually
                // doing anything, and is it doing more than one thing" — which is
                // the question the harness answers least well, because the
                // harness only ever watches the aggregate outcome.
                Telemetry.Record(state, TelemetryKind.AiAction, country.id,
                    objective.type.ToString().ToUpperInvariant(),
                    string.IsNullOrEmpty(objective.targetId) ? ai.path.ToString() : objective.targetId);

                switch (objective.type)
                {
                    case AIObjectiveType.BuildCapability:
                        InvestInPillars(state, country, rng);
                        ai.actionsThisMonth++;
                        break;

                    case AIObjectiveType.ConsolidateHome:
                        if (ConsolidateHome(state, ai, country)) ai.actionsThisMonth++;
                        break;

                    case AIObjectiveType.SecureResources:
                        if (SecureResources(state, country)) ai.actionsThisMonth++;
                        break;

                    case AIObjectiveType.PreemptProgramme:
                        if (PreemptProgramme(state, ai, country, objective.targetId, rng)) ai.actionsThisMonth++;
                        break;

                    case AIObjectiveType.CounterRival:
                        if (CounterRival(state, ai, country, objective.targetId, rng)) ai.actionsThisMonth++;
                        break;

                    case AIObjectiveType.ExpandInfluence:
                        if (SeekTreaty(state, country, objective.targetId)) ai.actionsThisMonth++;
                        break;

                    case AIObjectiveType.AssertClaim:
                        if (AssertClaim(state, ai, country, objective.targetId, rng)) ai.actionsThisMonth++;
                        break;

                    case AIObjectiveType.HardenDefenses:
                        if (HardenDefenses(state, ai, country, rng)) ai.actionsThisMonth++;
                        break;

                    case AIObjectiveType.InsulateEconomy:
                        if (InsulateEconomy(state, ai, country, rng)) ai.actionsThisMonth++;
                        break;

                    case AIObjectiveType.HardenSecurity:
                        if (HardenSecurity(state, ai, country, rng)) ai.actionsThisMonth++;
                        break;
                }

                // An objective the government formed and then could not act on is
                // worth seeing: it usually means it cannot afford its own plan.
                if (ai.actionsThisMonth == before)
                    Telemetry.Record(state, TelemetryKind.AiAction, country.id,
                        objective.type.ToString().ToUpperInvariant(),
                        "could not act", success: false);
            }
        }

        /// <summary>
        /// Direct the state's own effort (GDD §24.1).
        ///
        /// This used to *be* the AI's capability growth — a bespoke routine that
        /// raised pillars along the national priority with no people behind it.
        /// Foreign governments have cabinets now, and `CabinetSystem.MonthlyAct`
        /// runs them for every state, so that growth already happens through the
        /// same officials the player has. Doing it here as well would pay a
        /// government twice for one month's work.
        ///
        /// What is left is the part a cabinet does not cover: force structure.
        /// Ministers raise the *pillar*; only procurement writes strength, which
        /// is why `RebuildForces` still has to be reached from somewhere.
        /// </summary>
        static void InvestInPillars(GameState state, CountryState country, Random rng)
        {
            if (country.government.leader.priority == NationalPriority.Security)
                RebuildForces(state, country, rng);
        }

        /// <summary>
        /// Rebuild the force itself, not just the headline pillar. Procurement
        /// and sustainment were player-only verbs, so an AI army could be worn
        /// down by war and by monthly logistics decay and never recover — the
        /// world quietly disarmed itself over a long save while its intelligence
        /// estimates, which read the pillar rather than the force, kept reporting
        /// everyone strong.
        /// </summary>
        static void RebuildForces(GameState state, CountryState country, Random rng)
        {
            var mil = country.military;

            // Sustainment first: it decays every month for everyone, and a force
            // without it is hollow whatever its nominal strength.
            if (mil.logistics < 55f && rng.NextDouble() < 0.5)
            {
                MilitarySystem.InvestInLogisticsBy(state, country.id);
                return;
            }

            // A state at war moves money to the military, if its politics will
            // carry it. Actor-generic, so the world is not locked out of a verb
            // the player has — this codebase's most-repeated bug.
            if (!mil.warFooting && AcquisitionSystem.CanDeclareWarFooting(state, country.id, out _)
                && rng.NextDouble() < 0.35)
            {
                if (AcquisitionSystem.SetWarFootingBy(state, country.id, true)) return;
            }

            // Replace the specific thing that is missing. A branch rebuilt only
            // through aggregate strength would refill evenly, which is not how a
            // government that just lost its carriers actually spends.
            if (rng.NextDouble() < 0.5 && OrderWhatIsShort(state, country, rng)) return;

            if (mil.programs.Count >= MilitarySystem.MaxPrograms) return;

            // Re-equip whichever branch has fallen furthest behind.
            var weakest = ForceBranch.Ground;
            float worst = float.MaxValue;
            foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
            {
                float strength = mil.Get(branch).strength;
                if (strength >= worst) continue;
                worst = strength;
                weakest = branch;
            }

            // Only worth a programme if there is real ground to make up.
            if (worst > 75f) return;

            var scale = worst < 45f ? MilitarySystem.ProgramScale.Major : MilitarySystem.ProgramScale.Modest;
            MilitarySystem.BeginProcurementBy(state, country.id, weakest, scale);
        }

        /// <summary>
        /// A rival is close to an instrument that ends us (GDD §21, spec 14 §8).
        ///
        /// Three responses, in the order a government would actually reach for
        /// them, and all of them are things the player can do too:
        ///
        /// 1. **Get out of the war.** If we are the one they would use it on,
        ///    the cheapest counter is to stop being their problem. A government
        ///    staring at an existential programme will accept terms it would
        ///    have refused a year earlier.
        /// 2. **Build our own.** Deterrence is the classic answer, and it is
        ///    slow, which is exactly why detection has to happen early.
        /// 3. **Reach for the fallback.** If neither is open, do what a
        ///    threatened state does — collect, coerce and align against them.
        /// </summary>
        static bool PreemptProgramme(GameState state, AIState ai, CountryState country,
            string targetId, Random rng)
        {
            var confrontation = state.ActiveConfrontationFor(country.id);
            bool againstThem = confrontation != null
                               && !confrontation.resolved
                               && confrontation.Involves(targetId);

            // 1. Stop being their target.
            if (againstThem && PeaceSystem.ProposeConstructedSettlementBy(
                    state, confrontation, country.id))
            {
                // Suing for terms is public; *why* is not. The original text
                // named the foreign programme that prompted it, which is exactly
                // what `KnownPreparation` exists to keep behind collection — so
                // the entry had to stay secret and the visible half of the event
                // was lost with it. Reworded, the public fact can be public.
                state.AddChronicle(ChronicleCategory.Diplomatic, country.id,
                    $"{country.displayName} has sought terms.", Publicity.Public);
                return true;
            }

            // 2. Build a deterrent of our own.
            //
            // This is a second route into strategic preparation, and it
            // deliberately bypasses `ConsiderStrategicInstruments`' requirement
            // of a rivalry 24 months old. That gate exists so states do not
            // reach for the highest instruments over a passing quarrel; a rival
            // who is visibly close to one is not a passing quarrel, and waiting
            // two years to begin answering it defeats the entire point of
            // detecting it early. Every other gate still applies —
            // `CanPrepare` checks the capability, its maturity, the pillar and
            // the treasury, and `PrepareBy` charges per authorization.
            foreach (EndgameType type in Enum.GetValues(typeof(EndgameType)))
            {
                if (type == EndgameType.TotalMobilization) continue;
                if (country.endgames.ProgressFor(type) >= 100f) continue;
                if (!EndgameSystem.CanPrepare(state, country, type, out _)) continue;
                if (EndgameSystem.PrepareBy(state, country.id, type)) return true;
            }

            // 3. Otherwise, the ordinary tools of a threatened state.
            return CounterRival(state, ai, country, targetId, rng);
        }

        /// <summary>
        /// Attend to the home front (GDD §13).
        ///
        /// This used to write `stability += 1.2`, `approval += 0.8` and
        /// `government += 0.15` directly, every month, for nothing — a verb the
        /// player has no access to at a price the player cannot match. A rival
        /// could therefore ride out any amount of domestic damage the player
        /// inflicted, because repairing it cost the rival exactly zero.
        ///
        /// It now goes through the same two verbs the player uses and pays the
        /// same Political Capital for them, which means a government that has
        /// spent its authority genuinely cannot buy its way out of trouble until
        /// it has rebuilt some.
        /// </summary>
        static bool ConsolidateHome(GameState state, AIState ai, CountryState country)
        {
            // Reform is the deeper fix and the more expensive one; a government
            // reaches for it when the machinery itself is the problem, or when
            // the state is coming apart.
            //
            // Low stability belongs in this test even though reform also raises
            // the Government pillar: `InstitutionalReformBy` is the **only** one
            // of the two verbs that moves stability at all, and this objective is
            // scored on `(60 − stability)`. Narrowing the test to the pillar
            // alone leaves a government reaching for messaging to fix unrest that
            // messaging cannot touch.
            var gov = country.government;
            float backing = gov.IsElective ? gov.legislativeSupport : gov.eliteCohesion;

            // Pick the instrument that matches what is actually wrong rather than
            // walking a fixed ladder. Reform used to be unconditionally first,
            // and since most states sit below the pillar threshold for most of a
            // save, nothing after it was ever reached — every verb added below it
            // was unreachable by any foreign government, which is this codebase's
            // most-repeated bug wearing a new hat.
            //
            // The two problems are genuinely different. Reform raises the
            // machinery, which lifts the support *target* at only ×0.45 of a
            // ×0.3 term; bargaining addresses the chamber directly. A government
            // that has lost its own legislature does not fix that by
            // reorganising a ministry.
            // Every instrument is scored by how badly it is needed, and the
            // government reaches for the worst problem it can currently afford.
            //
            // A ladder was tried twice and failed the same way twice: whatever
            // sits at the top is the only thing that ever runs. Institutional
            // reform crowded out five verbs below it; demoting reform simply let
            // *bargaining* crowd out four more, because bargaining costs 2 PC and
            // almost always succeeds. Scoring is the only shape that lets a rare
            // problem win when it is genuinely the pressing one.
            var options = new List<(float score, Func<bool> act)>
            {
                // The chamber or the inner circle has turned on us.
                (Math.Max(0f, 55f - backing),
                    () => GovernmentSystem.BuildPoliticalSupportBy(state, country.id)
                          || GovernmentSystem.DistributePatronageBy(state, country.id)),

                // The machinery itself is the problem. Reform is the only one of
                // these that moves stability at all, so unrest belongs in its
                // score rather than messaging's.
                (Math.Max(0f, 55f - country.pillars.government)
                 + Math.Max(0f, 40f - country.stability),
                    () => GovernmentSystem.InstitutionalReformBy(state, country.id)),

                // A weak minister is a permanent tax on everything the state does
                // — and on what its own leadership gets told (spec 15).
                (Math.Max(0f, 50f - WeakestCompetence(country)),
                    () => GovernmentSystem.LaunchInquiryBy(state, country.id)),

                // An ageing leader with nobody prepared behind them is how an
                // orderly state becomes a contested one.
                (gov.successorReadiness < 50f ? Math.Max(0f, gov.leader.age - 58f) * 2.5f : 0f,
                    () => GovernmentSystem.GroomSuccessorBy(state, country.id)),

                // Plots are organising faster than the state can govern them away.
                (gov.civicPosture != CivicPosture.Restrictive
                    ? Math.Max(0f, gov.conspiracyLevel - 28f) * 1.8f : 0f,
                    () => GovernmentSystem.SetCivicPostureBy(
                        state, country.id, CivicPosture.Restrictive)),

                // And relaxed again once the danger has passed, or the country
                // pays for an apparatus it no longer needs for the rest of the save.
                (gov.civicPosture == CivicPosture.Restrictive && gov.conspiracyLevel < 12f
                    ? 30f : 0f,
                    () => GovernmentSystem.SetCivicPostureBy(
                        state, country.id, CivicPosture.Standard)),

                // The public has turned. Messaging is the cheap instrument and
                // the one that fixes mood rather than machinery.
                (Math.Max(0f, 55f - country.governmentApproval),
                    () => GovernmentSystem.PublicMessagingBy(state, country.id))
            };

            options.Sort((a, b) => b.score.CompareTo(a.score));

            // Take the worst problem we can actually pay to address. Falling
            // through on a failed attempt matters: a government that cannot
            // afford reform this month should still send its minister out to
            // speak rather than doing nothing at all.
            foreach (var (score, act) in options)
            {
                if (score <= 0f) continue;
                if (act()) return true;
            }

            return GovernmentSystem.PublicMessagingBy(state, country.id);
        }

        /// <summary>
        /// Order whatever this force is shortest of, relative to what a force of
        /// its size should hold.
        ///
        /// Measured against the catalogue baseline rather than against a flat
        /// number, so a small state restocks to its own scale instead of trying
        /// to buy a superpower's carrier fleet.
        /// </summary>
        static bool OrderWhatIsShort(GameState state, CountryState country, Random rng)
        {
            // Shared definition — see AcquisitionSystem.WorstShortfall. This is a
            // *deliberate* purchase on top of the military desk's routine
            // restocking, so it holds to a tighter definition of "short": a
            // government reaching for procurement as a strategic act is
            // responding to a gap its ministry has already failed to close.
            var worst = AcquisitionSystem.WorstShortfall(country, out float worstRatio);
            if (worst == null || worstRatio > 0.82f) return false;

            float count = worst.orderIncrement * (rng.NextDouble() < 0.4 ? 2f : 1f);
            return AcquisitionSystem.OrderBy(state, country.id, worst.kind, count);
        }

        static float WeakestCompetence(CountryState country)
        {
            float worst = 100f;
            foreach (var official in country.cabinet)
                if (official.competence < worst) worst = official.competence;
            return worst;
        }

        /// <summary>
        /// Address a resource vulnerability (GDD §20).
        ///
        /// This used to add energy and materials from nowhere, uncapped — which
        /// quietly undid the endowment model in `EconomySystem`, whose whole
        /// point is that "a country can invest past its endowment only through
        /// capability". An energy-poor archetype could simply stop being
        /// energy-poor by wanting to, and `energyDrag` stopped biting for anyone.
        ///
        /// Investment now costs treasury and can only close the gap toward what
        /// the country actually has — the authored endowment plus whatever
        /// territory and capability have added to it. Genuinely exceeding it
        /// still requires a research programme or taking ground, exactly as it
        /// does for the player.
        /// </summary>
        static bool SecureResources(GameState state, CountryState country)
        {
            const float InvestmentCost = 45f;
            if (country.resources.treasury < InvestmentCost) return false;

            float energyCeiling = EconomySystem.EnergyCeilingFor(state, country);
            float materialsCeiling = EconomySystem.MaterialsCeilingFor(state, country);

            float energyRoom = energyCeiling - country.resources.energy;
            float materialsRoom = materialsCeiling - country.resources.strategicMaterials;

            // Nothing to buy: the shortfall is structural, and the answer is a
            // programme or a trade partner, not money.
            if (energyRoom <= 0.5f && materialsRoom <= 0.5f) return false;

            country.resources.treasury -= InvestmentCost;

            if (energyRoom > 0.5f)
                country.resources.energy = Clamp(country.resources.energy + Math.Min(0.7f, energyRoom));
            if (materialsRoom > 0.5f)
                country.resources.strategicMaterials =
                    Clamp(country.resources.strategicMaterials + Math.Min(0.6f, materialsRoom));

            return true;
        }

        /// <summary>Counter a rival across whichever pillar is currently available.</summary>
        static bool CounterRival(GameState state, AIState ai, CountryState country, string targetId, Random rng)
        {
            if (string.IsNullOrEmpty(targetId)) return false;

            // 1. Collect first — you cannot counter what you cannot see.
            var network = state.FindNetwork(country.id, targetId);
            if (network == null)
            {
                state.networks.Add(new IntelNetwork
                {
                    ownerId = country.id,
                    targetId = targetId,
                    focus = IntelDomain.Military,
                    penetration = 15f
                });
                return true;
            }

            // 2. Harden at home.
            if (country.counterIntel.counterIntelligence < 45f)
            {
                country.counterIntel.counterIntelligence = Clamp(country.counterIntel.counterIntelligence + 3f);
                return true;
            }

            // 3. Mislead them about what we are.
            //
            // Deception was a player-only verb, so the fog ran one way: the AI
            // could be deceived and the player never could. A government that
            // knows it is being watched, and has a service capable of building
            // legends, mounts a programme of its own (GDD §14).
            if (MountDeception(state, ai, country, targetId, rng)) return true;

            // 4. Economic coercion, when the exposure is bearable — and only
            // against a state this government is genuinely at odds with. The
            // same predicate the review uses to *keep* measures, so a regime is
            // imposed for a cause and lifted when the cause is gone; before
            // this a rival was sanctioned at 35% a month for as long as it
            // stayed a rival, and the review could never catch up.
            if (state.FindSanction(country.id, targetId) == null && ai.profile.aggression > 45f
                && EconomySystem.SanctionCauseStands(state, country.id, targetId))
            {
                var link = state.FindTrade(country.id, targetId);
                float exposure = link != null ? link.volume : 0f;
                bool worthIt = exposure < 55f || ai.profile.aggression > 70f;
                if (worthIt && rng.NextDouble() < 0.20)
                {
                    var severity = ai.profile.aggression > 70f ? SanctionSeverity.Coercive : SanctionSeverity.Pressure;
                    return EconomySystem.ImposeSanctionsBy(state, country.id, targetId, severity, "RIVALRY");
                }
            }

            // 5. Otherwise deepen collection.
            network.penetration = Clamp(network.penetration + 4f);
            return true;
        }

        /// <summary>
        /// Run a deception programme against a state that is collecting on us.
        ///
        /// Which way a government lies follows from its position: the weak
        /// overstate to deter, the strong understate to invite a miscalculation
        /// they can punish. Both read as reasonable from the inside, and both are
        /// exactly the kind of understandable mistake the player should be able
        /// to make about them.
        /// </summary>
        static bool MountDeception(GameState state, AIState ai, CountryState country,
            string targetId, Random rng)
        {
            // Building legends takes a real service.
            if (country.pillars.intelligence < 45f) return false;
            if (country.counterIntel.deceptionStrength > 40f) return false;

            // No point deceiving someone who is not looking at us — as far as we
            // can tell. Whether a foreign service is inside us, and how deep, is
            // what counter-intelligence has to *earn*; reading their network's
            // penetration directly was a free mole hunt. We know they are looking
            // if we have caught them at it, or if our own service is strong
            // enough to assume it of a hostile state.
            if (!SuspectsCollection(state, country, targetId)) return false;

            // Difficulty is reasoning quality, never a stat cheat (GDD §24.3):
            // a sharper government notices the opportunity to mislead more often
            // and picks the domain that actually matters, rather than always
            // reaching for Military.
            if (rng.NextDouble() >= DeceptionAppetite(state.difficulty)) return false;

            float perceivedTheirs = PerceivedStrength(state, country.id,
                state.FindCountry(targetId), IntelDomain.Military);
            bool weakerThanThem = country.pillars.military < perceivedTheirs;

            country.counterIntel.deceptionDomain = DeceptionDomainFor(state, country, targetId);
            country.counterIntel.deceptionBias = weakerThanThem ? 1f : -1f;
            country.counterIntel.deceptionStrength =
                Clamp(country.counterIntel.deceptionStrength + 25f + ai.profile.opportunism * 0.15f);
            return true;
        }

        /// <summary>
        /// What to put on the table. Standard difficulty always offers the same
        /// pair; a sharper government reads what this partner actually needs —
        /// security if they feel threatened, trade if they are short.
        /// </summary>
        public static List<TreatyCommitment> TreatyOfferFor(GameState state, CountryState country, string targetId)
        {
            var commitments = new List<TreatyCommitment>
            {
                TreatyCommitment.NonAggression, TreatyCommitment.TradePreference
            };

            if (state.difficulty == Difficulty.Standard) return commitments;

            var relationship = state.FindRelationship(country.id, targetId);
            var target = state.FindCountry(targetId);
            if (relationship == null || target == null) return commitments;

            // Someone who feels surrounded wants a guarantee more than a tariff.
            // Their weakness is read through our estimate of them, never their
            // true pillar: this offer used to be tailored off the target's
            // real military figure, including against the player, which no
            // government can know without collecting.
            if (relationship.ThreatPerceivedBy(targetId) > 45f
                || PerceivedStrength(state, country.id, target, IntelDomain.Military) < 45f)
                commitments.Add(TreatyCommitment.MutualDefense);

            // A threatened partner is offered what *we* can see. Whether they
            // are blind is their secret; whether we have eyes is our own fact.
            if (relationship.ThreatPerceivedBy(targetId) > 45f && country.pillars.intelligence >= 50f)
                commitments.Add(TreatyCommitment.IntelligenceSharing);

            return commitments;
        }

        /// <summary>How readily a government reaches for deception (GDD §24.3).</summary>
        static double DeceptionAppetite(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Ruthless: return 0.55;
                case Difficulty.Challenging: return 0.38;
                default: return 0.25;
            }
        }

        /// <summary>
        /// Which domain to lie about.
        ///
        /// A less capable government always misrepresents its army, because that
        /// is the obvious thing to lie about. A sharper one lies about whatever
        /// the other side is actually looking at — which is far harder to catch
        /// and far more useful, and costs it nothing extra.
        /// </summary>
        static IntelDomain DeceptionDomainFor(GameState state, CountryState country, string targetId)
        {
            if (state.difficulty == Difficulty.Standard) return IntelDomain.Military;

            // A network we have rolled up told us what it was tasked on. One we
            // merely suspect has not, so we protect what we would want hidden:
            // whatever we are strongest in.
            var theirNetwork = state.FindNetwork(targetId, country.id);
            if (theirNetwork != null && theirNetwork.compromised) return theirNetwork.focus;

            var pillars = country.pillars;
            if (pillars.economy >= pillars.military && pillars.economy >= pillars.diplomacy) return IntelDomain.Economic;
            if (pillars.diplomacy >= pillars.military) return IntelDomain.Diplomatic;
            return IntelDomain.Military;
        }

        /// <summary>
        /// Whether this government has reason to believe `targetId` is
        /// collecting against it — from its own side of the fog: a network it
        /// has caught, or a counter-intelligence service good enough to assume
        /// it of a state that regards us coldly.
        /// </summary>
        public static bool SuspectsCollection(GameState state, CountryState country, string targetId)
        {
            var theirNetwork = state.FindNetwork(targetId, country.id);
            if (theirNetwork != null && theirNetwork.compromised) return true;

            var relationship = state.FindRelationship(country.id, targetId);
            bool cold = relationship != null && relationship.relations < 45f;
            return cold && country.counterIntel.counterIntelligence >= 55f;
        }

        /// <summary>
        /// Months over which the world's governments warm to formal treaties
        /// (2026-08 "cold open"). Eleven treaties were signed in the first month
        /// of every game and every one was mutual defence by the third: the
        /// alliance map was finished before a new operator had read the briefing.
        /// The willingness to *sign* now ramps from 15% to 100% over three years;
        /// outreach, trade and the relationships underneath are unchanged.
        /// </summary>
        public const int TreatyWarmUpMonths = 36;

        /// <summary>Months of peace a government keeps after any war of its own before opening another.</summary>
        public const int WarRecoveryMonths = 18;

        public static float TreatyReadiness(GameState state)
        {
            int month = state.date.MonthsSince(state.startDate);
            return 0.15f + 0.85f * Math.Min(1f, month / (float)TreatyWarmUpMonths);
        }

        static bool SeekTreaty(GameState state, CountryState country, string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return false;

            var readinessRng = new Random(unchecked(state.rngSeed * 31 + state.date.MonthsSince(state.startDate) * 7 + Hash.Of(country.id + targetId)));
            if (readinessRng.NextDouble() >= TreatyReadiness(state)) return false;

            // A standing treaty is not a finished relationship: a warm, trusted
            // partner gets asked for the next commitment up — which is how AI
            // blocs solidify into defence pacts, and how alliance obligations
            // get real signatories instead of a world of friendship letters.
            var existing = state.FindTreaty(country.id, targetId);
            if (existing != null)
            {
                if (existing.broken || existing.Has(TreatyCommitment.MutualDefense)) return false;
                var r = state.FindRelationship(country.id, targetId);
                if (r == null || DiplomacySystem.Permitted(state, r, r.relations) < 68f
                    || DiplomacySystem.Permitted(state, r, r.trust) < 55f) return false;

                return DiplomacySystem.DeepenTreatyBy(state, country.id, targetId,
                    new List<TreatyCommitment> { TreatyCommitment.MutualDefense });
            }

            // A more capable diplomacy tailors the offer instead of always
            // tabling the same two commitments (GDD §24.3). Reasoning quality,
            // not a bonus: the terms it picks are ones the other side is more
            // likely to want, which anyone could have chosen.
            var commitments = TreatyOfferFor(state, country, targetId);
            if (DiplomacySystem.ProposeTreatyBy(state, country.id, targetId, commitments)) return true;

            // Refused: warm the relationship instead and try again later.
            var relationship = state.FindRelationship(country.id, targetId);
            if (relationship != null)
            {
                relationship.relations = Clamp(relationship.relations + 1.5f);
                relationship.trust = Clamp(relationship.trust + 0.5f);
            }
            return true;
        }

        /// <summary>Press a claim — opening a confrontation when conditions justify it.</summary>
        /// <summary>
        /// How much of a claimant's authored shortfall the target's ground would
        /// answer, 0..1. Energy- or materials-poor beside a state holding the
        /// matching region type is the classic cause of war this world had all
        /// the pieces for and never used: the regions existed, the shortfalls
        /// existed, and no AI ever connected them.
        /// </summary>
        static float ResourcePrize(GameState state, CountryState claimant, string targetId)
        {
            bool wantsEnergy = claimant.resources.energy < 40f;
            bool wantsMaterials = claimant.resources.strategicMaterials < 40f;
            if (!wantsEnergy && !wantsMaterials) return 0f;

            float prize = 0f;
            foreach (var location in state.locations)
            {
                if (location.ownerId != targetId) continue;
                if (wantsEnergy && location.type == LocationType.EnergyRegion) prize = Math.Max(prize, 1f);
                if (wantsMaterials && location.type == LocationType.MaterialsRegion) prize = Math.Max(prize, 0.8f);
            }
            return prize;
        }

        static bool AssertClaim(GameState state, AIState ai, CountryState country, string targetId, Random rng)
        {
            if (string.IsNullOrEmpty(targetId)) return false;

            // Fronts are priced, not forbidden — the same gate the player is
            // held to. The old check here was a flat "not while anyone involved
            // is busy", which had two consequences the measurements finally made
            // visible (0–1 AI-vs-AI wars in thirty years, three seeds):
            // - a government could never open a second front however strong, and
            // - **a target already fighting elsewhere could not be attacked at
            //   all** — while forty lines up, `IsOverstretched` was adding that
            //   exact opening to this exact objective's weight. The opportunity
            //   was computed every month and structurally unreachable: the
            //   written-but-never-read family, wearing a guard clause.
            if (!ConfrontationSystem.CanOpenAnother(state, country.id, out _)) return false;

            // Domestic weakness argues against foreign adventures. 35, not 40:
            // in a world of standing sanctions regimes much of the roster lives
            // in the high 30s, and the old bar quietly pacified all of it.
            if (country.stability < 35f || country.warExhaustion > 55f) return false;

            // A government that has just fought does not open another war the
            // month its last one closed (2026-08): a passive player was at war
            // 92 confrontation-months per decade because the world's wars ran
            // back to back. Eighteen months of peace after any war of its own.
            foreach (var past in state.confrontations)
                if (past.resolved && past.Involves(country.id)
                    && state.date.MonthsSince(past.startDate) - past.monthsActive < WarRecoveryMonths)
                    return false;

            float commitChance = 0.10f + ai.profile.aggression / 300f + ai.profile.opportunism / 400f;

            // A neighbour's oilfield or mine is a reason, not a backdrop — a
            // state short of what the target's ground supplies commits more
            // readily. This is the world-as-structure work reaching the AI's
            // willingness to actually pull the trigger.
            commitChance += ResourcePrize(state, country, targetId) * 0.10f;

            if (state.difficulty == Difficulty.Ruthless) commitChance += 0.05f;
            if (rng.NextDouble() >= commitChance) return false;

            // Prefer a concrete objective the AI can actually hold — and prefer
            // the ground that answers a shortfall over the first on the list.
            string locationId = null;
            foreach (var location in state.locations)
            {
                if (location.ownerId != targetId) continue;
                if (location.type == LocationType.Capital) continue; // never a realistic demand

                bool prize =
                    (country.resources.energy < 40f && location.type == LocationType.EnergyRegion)
                    || (country.resources.strategicMaterials < 40f
                        && location.type == LocationType.MaterialsRegion);
                if (prize) { locationId = location.id; break; }
                if (locationId == null) locationId = location.id;
            }

            var objective = locationId != null
                ? ConfrontationObjective.TerritorialConcession
                : ConfrontationObjective.Deterrence;

            var confrontation = ConfrontationSystem.BeginBy(state, country.id, targetId,
                objective, locationId, PrimaryStrategy.Military);
            return confrontation != null;
        }

        /// <summary>
        /// Seek terms on one front when the war has stopped paying for itself.
        /// Returns true when a proposal was put (and the month's attention is
        /// spent on it), whether or not the other side took it.
        ///
        /// Wars ran 30+ months because nobody asked for terms until exhaustion
        /// was well past 45 (2026-08). A government now seeks terms sooner, and
        /// any war that has run two years wears on it. A front opened for an
        /// ally is held to the same test as any other, on its own exhaustion.
        /// </summary>
        static bool SeekTermsIfWorn(GameState state, AIState ai, CountryState country,
            Confrontation confrontation, Random rng)
        {
            bool isInitiator = confrontation.initiatorId == country.id;
            float ourExhaustion = isInitiator ? confrontation.initiatorWarExhaustion : confrontation.defenderWarExhaustion;
            float ourMomentum = isInitiator ? confrontation.momentum : -confrontation.momentum;

            bool wantsOut = ourExhaustion > 35f + ai.profile.patience * 0.25f
                            || country.warSupport < 25f
                            || (ourMomentum < -25f && ai.profile.caution > 50f)
                            || (confrontation.monthsActive >= 24 && ourMomentum < 15f);

            if (!wantsOut || confrontation.monthsActive < 2) return false;

            if (PeaceSystem.ProposeConstructedSettlementBy(state, confrontation, country.id))
                return true;

            // Terms refused; a tired or cautious state may simply concede.
            if (ourExhaustion > 70f && rng.NextDouble() < 0.25)
            {
                ConfrontationSystem.ProposeSettlementBy(state, confrontation, country.id, concedeInstead: true);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Run an ongoing confrontation: escalate, act, or seek terms. The AI
        /// weighs its own exhaustion and momentum exactly as the player must.
        /// </summary>
        static void ManageOngoingConfrontation(GameState state, AIState ai, CountryState country, Random rng)
        {
            var fronts = state.ActiveConfrontationsFor(country.id);
            if (fronts.Count == 0) return;

            // A state at war moves money to the military, if its politics will
            // carry it. This lives here, not in the objective budget: when the
            // world ran hotter, war objectives crowded `RebuildForces` out of
            // the top-N cut and **no foreign government ever surged again** —
            // the test that owns the claim failed within one census. Managing a
            // war a government is already in is not a strategic choice competing
            // for attention; it is what the war ministry does with the war it
            // has. Same reasoning that moved routine restocking to the desk.
            if (!country.military.warFooting
                && state.IsAtWar(country.id)
                && AcquisitionSystem.CanDeclareWarFooting(state, country.id, out _)
                && rng.NextDouble() < 0.30)
                AcquisitionSystem.SetWarFootingBy(state, country.id, true);

            // **Every front is examined for terms, not only the first.** The
            // manager used to read `ActiveConfrontationFor`, the first unresolved
            // war in list order, so a state in three wars sought terms in one and
            // the other two — always the later, obligation-opened fronts — had no
            // exit at all: nothing else in the game settles an AI-vs-AI war.
            // Seeking terms on every front is what a foreign ministry does;
            // operations below still go to one front, because that is what an
            // army does.
            for (int i = 0; i < fronts.Count; i++)
            {
                var front = fronts[i];
                if (front.resolved) continue;
                if (SeekTermsIfWorn(state, ai, country, front, rng)) return;
            }

            var confrontation = state.ActiveConfrontationFor(country.id);
            if (confrontation == null || confrontation.resolved) return;

            bool isInitiator = confrontation.initiatorId == country.id;
            float ourMomentum = isInitiator ? confrontation.momentum : -confrontation.momentum;

            // Escalate when confident and aggressive.
            if (confrontation.escalation < EscalationState.LimitedConflict)
            {
                float escalateChance = 0.08f + ai.profile.aggression / 500f + Math.Max(0f, ourMomentum) / 400f;
                if (country.warSupport > 35f && rng.NextDouble() < escalateChance)
                {
                    var next = (EscalationState)((int)confrontation.escalation + 1);
                    ConfrontationSystem.SetEscalationBy(state, confrontation, next, country.id);
                }
                return;
            }

            // At war: prosecute operations against a reachable objective.
            if (rng.NextDouble() < 0.45)
            {
                string targetLocationId = confrontation.objectiveLocationId;
                if (string.IsNullOrEmpty(targetLocationId) || state.FindLocation(targetLocationId)?.ownerId == country.id)
                {
                    string opponentId = confrontation.OpponentOf(country.id);
                    foreach (var location in state.locations)
                    {
                        if (location.ownerId != opponentId || location.type == LocationType.Capital) continue;
                        targetLocationId = location.id;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(targetLocationId)) return;

                var directive = new OperationDirective
                {
                    speedPriority = 40f + ai.profile.aggression * 0.4f,
                    casualtyTolerance = 30f + ai.profile.aggression * 0.5f,
                    // Restraint varies by government; caution restrains collateral risk.
                    civilianRiskLimit = Math.Max(10f, 60f - ai.profile.caution * 0.5f)
                };

                var operationType = ChooseOperation(state, country, ai,
                    state.FindLocation(targetLocationId), confrontation, ourMomentum, rng);
                ConfrontationSystem.LaunchOperationBy(state, confrontation, country.id,
                    targetLocationId, operationType, directive);
            }
        }

        /// <summary>
        /// Which operation a government orders, from its own force structure and
        /// the situation in front of it (GDD §19).
        ///
        /// The AI previously chose between `Siege` and `Assault` and nothing
        /// else, so every verb added for the player was one the world could not
        /// use — the same "AI locked out of player verbs" failure that made every
        /// AI army decay monotonically before procurement was made actor-generic.
        /// A naval power should blockade, an air power should strike, and a
        /// government facing dug-in works should break them first.
        ///
        /// Ordered by what a competent staff would reach for, and every branch
        /// still falls through to `Assault`, because it is the only operation
        /// that takes ground and an AI that never assaults never wins.
        /// </summary>
        public static OperationType ChooseOperation(GameState state, CountryState country, AIState ai,
            StrategicLocation target, Confrontation confrontation, float ourMomentum, Random rng)
        {
            if (target == null) return OperationType.Assault;

            // Never consider an order the world would refuse. Preference has to
            // be expressed inside what is actually possible, or a maritime
            // doctrine spends every month proposing blockades of a landlocked
            // neighbour and achieving nothing.
            var possible = OperationCatalog.AvailableAgainst(state, country.id, target);
            if (possible.Count == 0) return OperationType.Assault;

            bool Can(OperationType type) => possible.Contains(type);

            var mil = country.military;

            // The garrison we *believe* is there, through the same estimate the
            // player plans from — centred on a deceived figure when the holder
            // runs deception. The AI used to read `target.garrison` directly and
            // plan every strike from the truth. Fortifications stay public:
            // works are visible from orbit, a headcount is not.
            float garrison = PerceivedGarrison(state, country.id, target);

            // Dug-in works are worth breaking before spending an army on them,
            // and the effect is permanent, so it stays worth doing once.
            if (Can(OperationType.SuppressDefenses) && target.defenseValue > 50f
                && rng.NextDouble() < 0.55)
                return OperationType.SuppressDefenses;

            // Air power is worth fighting for on its own terms — a government that
            // never contests the sky can be bombed indefinitely for free.
            if (Can(OperationType.CounterAirCampaign) && rng.NextDouble() < 0.3)
                return OperationType.CounterAirCampaign;

            // A maritime power squeezes the country rather than the position.
            // Only worth ordering against someone who actually trades by sea.
            if (mil.naval.EffectivePower > mil.ground.EffectivePower * 0.8f)
            {
                string opponentId = confrontation.OpponentOf(country.id);
                bool tradesAtSea = false;
                foreach (var link in state.trade)
                    if (link.Involves(opponentId) && link.volume > 20f) { tradesAtSea = true; break; }

                if (tradesAtSea)
                {
                    if (Can(OperationType.NavalBlockade) && rng.NextDouble() < 0.35)
                        return OperationType.NavalBlockade;
                    if (Can(OperationType.CommerceRaiding) && rng.NextDouble() < 0.3)
                        return OperationType.CommerceRaiding;
                }

                // Beat their fleet first, then everything else at sea is cheaper.
                if (Can(OperationType.SeaControl) && rng.NextDouble() < 0.3)
                    return OperationType.SeaControl;
                if (Can(OperationType.MineWarfare) && rng.NextDouble() < 0.25)
                    return OperationType.MineWarfare;
            }

            // A government out to break the country rather than the army goes for
            // what makes the war possible at all.
            if (ai.path == StrategicPath.EconomicPrimacy
                && Can(OperationType.StrategicBombing) && rng.NextDouble() < 0.4)
                return OperationType.StrategicBombing;

            // A cautious government reaches for standoff fires and deniable
            // action before it spends its own people.
            if (ai.profile.caution > 60f && garrison > 40f)
            {
                if (Can(OperationType.AirStrike) && rng.NextDouble() < 0.35) return OperationType.AirStrike;
                if (Can(OperationType.CyberOperation) && rng.NextDouble() < 0.3)
                    return OperationType.CyberOperation;
                if (Can(OperationType.SpecialOperation) && rng.NextDouble() < 0.3)
                    return OperationType.SpecialOperation;
            }

            // Starve it before storming it.
            if (Can(OperationType.AirInterdiction) && garrison > 55f && rng.NextDouble() < 0.3)
                return OperationType.AirInterdiction;

            if (ourMomentum < -15f && Can(OperationType.Siege)) return OperationType.Siege;

            // Coming from the sea is expensive and sometimes the only way in.
            if (Can(OperationType.AmphibiousAssault) && !Can(OperationType.Assault))
                return OperationType.AmphibiousAssault;
            if (Can(OperationType.AmphibiousAssault)
                && mil.naval.EffectivePower > mil.ground.EffectivePower
                && rng.NextDouble() < 0.3)
                return OperationType.AmphibiousAssault;

            return Can(OperationType.Assault) ? OperationType.Assault : possible[0];
        }

        // ---------- strategic instruments (GDD §21) ----------

        /// <summary>
        /// Governments other than the player can also reach for decisive
        /// instruments. They prepare quietly over years when a rival looks
        /// permanent, and they only use one when the alternative is worse:
        /// losing a war, or facing something existential themselves.
        /// </summary>
        static void ConsiderStrategicInstruments(GameState state, AIState ai, CountryState country, Random rng)
        {
            var confrontation = state.ActiveConfrontationFor(country.id);

            // 1. Use, when the case for it has become overwhelming.
            if (confrontation != null && !confrontation.resolved)
            {
                string opponentId = confrontation.OpponentOf(country.id);
                bool isInitiator = confrontation.initiatorId == country.id;
                float momentum = isInitiator ? confrontation.momentum : -confrontation.momentum;
                float exhaustion = isInitiator
                    ? confrontation.initiatorWarExhaustion
                    : confrontation.defenderWarExhaustion;

                // Desperation, not opportunity, is what unseals these.
                bool losing = momentum < -30f || exhaustion > 60f || country.stability < 35f;
                bool wasStruckExistentially = SufferedExistentialAttack(state, country.id, opponentId);

                if (losing || wasStruckExistentially)
                {
                    // A cautious government hesitates even here; an aggressive one does not.
                    float willingness = 0.10f
                                        + ai.profile.aggression / 320f
                                        - ai.profile.caution / 500f
                                        + (wasStruckExistentially ? 0.45f : 0f);

                    if (rng.NextDouble() < willingness)
                    {
                        foreach (EndgameType type in Enum.GetValues(typeof(EndgameType)))
                        {
                            if (type == EndgameType.TotalMobilization) continue;
                            if (!EndgameSystem.CanExecuteBy(state, country.id, type, opponentId, out _)) continue;
                            EndgameSystem.ExecuteBy(state, country.id, type, opponentId);
                            return;
                        }
                    }
                }

                // Mobilization is the one instrument a state turns on itself.
                // From strength, not only from desperation (2026-08). A
                // government at war with a state it cannot stand may use a
                // prepared *severe* instrument — destabilization or isolation,
                // the ones that do not escalate the target to Total War — while
                // it is ahead, not only when it is losing. Existential ones stay
                // behind the desperation gate above. Rare per month, so a decade
                // holds a few such moves across the world, not a barrage. Before
                // this the world used an instrument on the player in 1 of 225
                // measured decades.
                var opponentRelationship = state.FindRelationship(country.id, opponentId);
                if (!losing && confrontation.escalation >= EscalationState.LimitedConflict
                    && opponentRelationship != null && opponentRelationship.relations <= 35f)
                {
                    float appetite = 0.03f + ai.profile.opportunism / 1000f + ai.profile.aggression / 1400f;
                    if (rng.NextDouble() < appetite)
                        foreach (EndgameType type in Enum.GetValues(typeof(EndgameType)))
                        {
                            if (EndgameSystem.SeverityOf(type) != StrategicSeverity.Severe) continue;
                            if (!EndgameSystem.CanExecuteBy(state, country.id, type, opponentId, out _)) continue;
                            EndgameSystem.ExecuteBy(state, country.id, type, opponentId);
                            return;
                        }
                }

                if ((losing || confrontation.escalation >= EscalationState.LimitedConflict)
                    && EndgameSystem.CanExecuteBy(state, country.id, EndgameType.TotalMobilization, null, out _)
                    && rng.NextDouble() < 0.25)
                {
                    EndgameSystem.ExecuteBy(state, country.id, EndgameType.TotalMobilization, null);
                    return;
                }
            }

            // 2. Prepare, when a rival has come to look permanent. Preparation is
            //    expensive and slow, so only a sustained threat justifies it.
            //
            //    The affordability check derives from the price the programme
            //    actually charges (×1.5 for a government's prudence), because a
            //    hand-written `400` here was **more than triple the real 120**
            //    — a second, stricter definition of affordability, which is a
            //    repeal of the first. It cost nothing while the world was rich;
            //    the moment the world started fighting its own wars and
            //    treasuries ran near zero, nine states with decade-long
            //    rivalries and matured capabilities could never once fund a
            //    programme the endgame system itself would have sold them.
            // A long-standing rival is one the objective budget has been working
            // against for two years; a state this government simply cannot stand
            // (≤ 30 relations, high perceived threat) counts for the from-strength
            // use below even if the budget never got round to it. Without the
            // fallback the player — who is rarely a *budgeted* rival for 24
            // months — was almost never a *prepared-against* target.
            string rivalId = LongStandingRival(ai) ?? MostHatedState(state, country);
            if (rivalId == null) return;

            // From strength, not only from desperation (2026-08). A prepared
            // *severe* instrument — destabilization or isolation, the ones that do
            // not escalate the target to Total War — can be used against a
            // long-standing rival the government genuinely wants weakened,
            // without waiting to be losing a war to it. Existential instruments
            // stay behind the desperation gate above. Rare per month, so a decade
            // holds one or two such moves across the world, not a barrage.
            if (country.resources.treasury < EndgameSystem.PreparationTreasuryCost * 1.5f) return;

            // Patience is what lets a government fund something for years.
            float commitment = 0.18f + ai.profile.patience / 400f + PlanningHorizon(state.difficulty) * 0.2f;
            if (rng.NextDouble() >= commitment) return;

            var chosen = PreferredInstrument(state, country);
            if (chosen.HasValue) EndgameSystem.PrepareBy(state, country.id, chosen.Value);
        }

        /// <summary>
        /// Whether to admit a breakaway state exists (spec 04 §5b).
        ///
        /// Outside the objective budget, like détente and the books: taking a
        /// position on somebody else's civil war is a diplomatic fact a
        /// government has to face, not a strategy competing for this month's
        /// actions. If it sat in the action cut a busy world would leave every
        /// successor permanently unrecognised, and the whole mechanism would be
        /// a player privilege with no world behind it.
        ///
        /// **The calculation is whose friendship is worth more.** A state close
        /// to the parent will not recognise; one that dislikes the parent will,
        /// and quickly. That is the same reasoning an operator does, which is
        /// what makes the decision legible from the outside.
        /// </summary>
        static void ConsiderRecognition(GameState state, AIState ai, CountryState country, Random rng)
        {
            if (rng.NextDouble() >= 0.20) return;

            foreach (var successor in state.countries)
            {
                if (!DiplomacySystem.IsSuccessor(state, successor)) continue;
                if (!DiplomacySystem.CanRecognise(state, country.id, successor.id, out _)) continue;

                string parentId = DiplomacySystem.ParentOf(state, successor);
                var withParent = state.FindRelationship(country.id, parentId);
                var withSuccessor = state.FindRelationship(country.id, successor.id);
                if (withSuccessor == null) continue;

                // Warmth toward the new state against warmth toward the old one.
                // A treaty with the parent counts heavily: recognising a
                // breakaway from an ally is close to a betrayal.
                float parentTie = withParent == null ? 0f : withParent.relations * 0.5f;
                if (withParent != null && state.FindTreaty(country.id, parentId) != null)
                    parentTie += 25f;

                float appetite = withSuccessor.relations * 0.4f
                                 + (100f - parentTie) * 0.35f
                                 + ai.profile.opportunism * 0.15f;

                if (appetite < 55f) continue;
                DiplomacySystem.RecogniseBy(state, country.id, successor.id);
                return;   // one position a month is plenty
            }
        }

        /// <summary>
        /// Public finance, run from the desk (spec 02 §9).
        ///
        /// **Outside the objective budget, deliberately** — the `ConsiderDetente`
        /// and routine-restocking precedent. Funding the state is governance, not
        /// a strategy competing with starting a war for this month's actions; put
        /// it in the action cut and it goes silent for thirty years the moment
        /// the world gets busy, which is exactly how no foreign government
        /// ordered equipment for an entire measured decade.
        ///
        /// Deliberately dull. A government borrows when it is running out of
        /// money, retires debt and lays in reserves when it is not, and tightens
        /// or loosens the budget according to whether the books or the public are
        /// the more pressing problem. Nothing here is a clever play; the point is
        /// that the AI pays the same prices the operator does.
        /// </summary>
        static void ManageTheBooks(GameState state, AIState ai, CountryState country, Random rng)
        {
            var fiscal = country.fiscal;
            bool broke = country.resources.treasury < DiscretionaryReserve;
            bool flush = country.resources.treasury > DiscretionaryReserve * 6f;
            // A fiscal *crisis* is answered by the finance ministry every month
            // (`FiscalSystem.SteadyTheBooks`, run from the fiscal tick for every
            // non-player state) — austerity, revenue up, a write-down once
            // arrears are deep. It is not here because it is not a strategy
            // competing for the month's attention: the old books had one
            // response to insolvency, a 6%-a-month chance of a restructure, and
            // an austerity branch disarmed by the case it existed for
            // (`booksInTrouble && !publicInTrouble`, when the loop delivers both).
            var condition = FiscalSystem.ConditionOf(state, country);

            // Borrow before the lights go out, not after: research, procurement
            // and every strategic instrument are treasury-gated, and a state that
            // spends its last coin can fund nothing with a lead time.
            if (broke && rng.NextDouble() < 0.35
                && FiscalSystem.CanIssueDebt(state, country.id, out _))
            {
                FiscalSystem.IssueSovereignDebtBy(state, country.id);
                return;
            }

            // Debt that has run away gets written down, at the same reputational
            // price the operator pays for it.
            if (FiscalSystem.DebtToGdp(country) > 150f && fiscal.creditStanding < 30f
                && !fiscal.HasRestructured && rng.NextDouble() < 0.06)
            {
                FiscalSystem.RestructureDebtBy(state, country.id);
                return;
            }

            // Lay in what a blockade would take away, and only what this country
            // is actually short of — an energy-rich state stockpiling energy is
            // the sort of busywork that reads as the AI not understanding itself.
            if (flush && rng.NextDouble() < 0.10)
            {
                var resources = country.resources;
                TradeFocus wanted =
                    resources.foodSecurity < resources.energy
                    && resources.foodSecurity < resources.strategicMaterials ? TradeFocus.Food
                    : resources.energy <= resources.strategicMaterials ? TradeFocus.Energy
                    : TradeFocus.Materials;
                FiscalSystem.BuildReservesBy(state, country.id, wanted);
                return;
            }

            // The standing choice, reviewed rarely. A government that re-plans
            // its budget every month reads as noise, the same reason
            // `AIStrategy` holds a path for thirty months.
            if (rng.NextDouble() >= 0.04) return;

            bool booksInTrouble = FiscalSystem.DebtToGdp(country) > 95f
                                  || fiscal.creditStanding < 40f;
            bool publicInTrouble = country.livingStandards < 38f || country.socialUnrest > 55f;

            // When both are in trouble the books come first if the stress is
            // real: a government that cannot borrow cannot buy the public
            // anything. The old rule chose Balanced here, which is to say it
            // chose nothing at exactly the moment a choice was required.
            bool depression = country.economy.marketIndex < FiscalSystem.DepressionLine;
            BudgetPosture wantedPosture =
                FiscalSystem.AusterityAdvisable(state, country) ? BudgetPosture.Austerity
                : booksInTrouble && !publicInTrouble && !depression ? BudgetPosture.Austerity
                : publicInTrouble && !booksInTrouble ? BudgetPosture.Expansionary
                : BudgetPosture.Balanced;

            if (wantedPosture != fiscal.budgetPosture
                && GovernmentSystem.SpendPoliticalCapitalBy(state, country.id, 1f, "Budget posture"))
                FiscalSystem.SetBudgetPostureBy(state, country.id, wantedPosture);

            // **The revenue lever.** `SetTaxRateBy` had no AI caller at all, so a
            // foreign government could only ever spend less, never raise more.
            // Books in trouble: up, toward the crisis ceiling. Public in trouble
            // with the books sound: down, a little. Otherwise back toward the
            // authored baseline, so the measured world stays comparable.
            float wantedTax = fiscal.taxRate;
            if (booksInTrouble || condition >= FiscalCondition.DeficitFinanced)
                wantedTax = Math.Min(depression ? FiscalSystem.DepressionTaxCeiling : FiscalSystem.CrisisTaxCeiling,
                    fiscal.taxRate + 3f);
            else if (publicInTrouble && FiscalSystem.DebtToGdp(country) < 60f)
                wantedTax = Math.Max(FiscalState.BaselineTaxRate - 5f, fiscal.taxRate - 2f);
            else if (Math.Abs(fiscal.taxRate - FiscalState.BaselineTaxRate) > 0.5f)
                wantedTax = fiscal.taxRate + Math.Sign(FiscalState.BaselineTaxRate - fiscal.taxRate) * 1f;

            if (Math.Abs(wantedTax - fiscal.taxRate) > 0.5f
                && GovernmentSystem.SpendPoliticalCapitalBy(state, country.id, 1f, "Tax rate"))
                FiscalSystem.SetTaxRateBy(state, country.id, wantedTax);
        }

        // ---------- post-war occupation (C5) ----------

        /// <summary>
        /// Months that must have passed since the war ended before a government
        /// will put down ground it took in it.
        ///
        /// The same scale the settlement truce runs on. Immediately after a war
        /// a garrison is holding what the peace was argued over; a government
        /// that walked out the following month would be settling twice.
        /// </summary>
        public const int PostWarRelinquishmentMonths = 24;

        /// <summary>
        /// Whether a government still wants the ground it is sitting on after the
        /// war that took it (GDD §16, §19).
        ///
        /// **Outside the objective budget and deliberately so**, on the
        /// `ConsiderDetente` precedent: deciding you can no longer afford a
        /// garrison is not a strategy competing for this month's actions, it is
        /// housekeeping a finance ministry does whether or not anything else is
        /// happening. Routine restocking spent thirty measured years unreachable
        /// because it sat behind a priority check, and this would go the same way.
        ///
        /// No RNG, no command points, and no difficulty term: a government that
        /// cannot pay for an occupation cannot pay for it on Standard either.
        /// </summary>
        static void ConsiderRelinquishment(GameState state, CountryState country)
        {
            // The operator decides for their own country. There is no AIState for
            // the player, so this is an assertion rather than a branch — but it
            // is the assertion that keeps an occupation the player chose to hold
            // from being handed back by a routine they never ran.
            if (country == null || country.isPlayer) return;

            float bill = TerritorySystem.HoldingBillFor(state, country.id);
            if (bill <= 0f) return;

            // A solvent government keeps what it took. The exit exists because
            // occupation was *unpayable and inescapable*; a state that can plainly
            // fund three years of garrison has no reason to reach for it.
            if (FiscalSystem.ConditionOf(state, country) <= FiscalCondition.CashNegativeButCreditworthy
                && country.resources.treasury >= bill * TerritorySystem.HoldingRunwayMonths)
                return;

            StrategicLocation worst = null;
            float worstBill = -1f;

            for (int i = 0; i < state.locations.Count; i++)
            {
                var location = state.locations[i];
                if (location.ownerId != country.id) continue;
                if (!location.IsOccupied) continue;

                if (!TerritorySystem.CanRelinquish(state, country.id, location.id, out _)) continue;

                if (MonthsSinceWarWith(state, country.id, location.originalOwnerId)
                    < PostWarRelinquishmentMonths) continue;

                // Ground that is quiet *and* answers a shortfall is the one kind
                // worth the bill: it is paying us back in the commodity we are
                // short of, and nobody is shooting at it. Either half failing —
                // a rising on it, or no shortfall to answer — and it is just an
                // expense.
                if (!InsurgencySystem.Denies(state, location)
                    && TerritorySystem.AnswersShortfall(state, country, location)) continue;

                float here = TerritorySystem.HoldingBill(state, location);
                if (here > worstBill) { worstBill = here; worst = location; }
            }

            // One a month. A government withdrawing from everything at once reads
            // as a collapse rather than a decision, and staging it lets the next
            // month's books reflect what the last withdrawal saved.
            if (worst != null) TerritorySystem.RelinquishBy(state, country.id, worst.id);
        }

        /// <summary>
        /// Months since the most recent war between these two ended, or
        /// <see cref="int.MaxValue"/> when they have never fought.
        ///
        /// Uses the established idiom — months since it started, less the months
        /// it ran — rather than a stored end date, exactly as
        /// <see cref="AllianceSystem.RecentlyAtWar"/> does. Never having fought
        /// reads as "long ago": there is no war to wait out.
        /// </summary>
        static int MonthsSinceWarWith(GameState state, string countryId, string otherId)
        {
            int soonest = int.MaxValue;

            for (int i = 0; i < state.confrontations.Count; i++)
            {
                var confrontation = state.confrontations[i];
                if (!confrontation.resolved) continue;
                if (!confrontation.Involves(countryId)) continue;
                if (!confrontation.Involves(otherId)) continue;

                int since = state.date.MonthsSince(confrontation.startDate)
                            - confrontation.monthsActive;
                if (since < soonest) soonest = since;
            }

            return soonest;
        }

        /// <summary>
        /// A sanctioned government asks its way out (spec 02 §4a). Runs outside
        /// the objective budget, like war management — living under sanctions
        /// is a condition, not a strategic choice competing for attention, and
        /// the census showed 40–60 standing AI-AI regimes precisely because no
        /// AI ever had a verb to end one. Costs Political Capital, so relief is
        /// something a government spends standing on, exactly as the player
        /// spends Command Points on it.
        /// </summary>
        static void ConsiderDetente(GameState state, AIState ai, CountryState country, Random rng)
        {
            if (EconomySystem.SanctionPressureOn(state, country.id) <= 0.5f) return;
            if (rng.NextDouble() >= 0.12) return;

            // Ask the heaviest sender that is plausibly persuadable.
            string bestSender = null;
            float bestWeight = 0f;
            foreach (var sanction in state.sanctions)
            {
                if (sanction.targetId != country.id) continue;
                // Screened through our reporting on the sender, not their
                // acceptance function: a government that could read exactly
                // who would relent never wasted a request, which is a perfect
                // oracle the player is not given. With no reporting it asks
                // and may be refused; that is what poor intelligence costs.
                if (EconomySystem.AssessRelief(state, sanction.senderId, country.id) == SettlementOutlook.Unlikely)
                    continue;
                if (sanction.Weight > bestWeight) { bestWeight = sanction.Weight; bestSender = sanction.senderId; }
            }
            if (bestSender == null) return;
            if (!GovernmentSystem.SpendPoliticalCapitalBy(state, country.id, 1.5f, "Seek sanctions relief"))
                return;

            EconomySystem.SeekSanctionsReliefBy(state, country.id, bestSender);
        }

        /// <summary>
        /// Added threat from a decisive programme this government has actually
        /// detected in another state. Routed through KnownPreparation so it
        /// respects collection — an undetected programme frightens nobody, which
        /// is exactly why concealment is worth something.
        /// </summary>
        static float DetectedProgramme(GameState state, string observerId, string targetId)
        {
            float worst = 0f;
            foreach (EndgameType type in Enum.GetValues(typeof(EndgameType)))
            {
                if (type == EndgameType.TotalMobilization) continue;
                float known = EndgameSystem.KnownPreparation(state, observerId, targetId, type);
                if (known < 0f) continue;

                float weight = EndgameSystem.SeverityOf(type) == StrategicSeverity.Existential ? 0.60f : 0.35f;
                float alarm = known * weight;
                if (alarm > worst) worst = alarm;
            }
            return worst;
        }

        /// <summary>
        /// Objectives are re-scored every few months, so their age says nothing
        /// about how long a rivalry has lasted. This accumulates the slower fact
        /// underneath them, and lets it fade when the pressure comes off.
        /// </summary>
        static void TrackRivalries(AIState ai)
        {
            foreach (var rivalry in ai.rivalries) rivalry.stillRival = false;

            foreach (var objective in ai.objectives)
            {
                // Pre-empting a state is the strongest possible way of treating
                // it as a rival, so it has to count here. It also *displaces*
                // CounterRival in the objective list when it scores higher —
                // which silently stopped a rivalry being recorded at exactly the
                // moment it became most serious.
                if (objective.type != AIObjectiveType.CounterRival
                    && objective.type != AIObjectiveType.PreemptProgramme) continue;
                if (string.IsNullOrEmpty(objective.targetId)) continue;

                var rivalry = FindRivalry(ai, objective.targetId);
                if (rivalry == null)
                {
                    rivalry = new Rivalry { countryId = objective.targetId };
                    ai.rivalries.Add(rivalry);
                }
                rivalry.stillRival = true;
                rivalry.coolOff = 0;
                rivalry.months++;
            }

            // Rivalries cool, but far more slowly than they form: three quiet
            // months to forget one month of enmity.
            for (int i = ai.rivalries.Count - 1; i >= 0; i--)
            {
                var rivalry = ai.rivalries[i];
                if (rivalry.stillRival) continue;
                if (++rivalry.coolOff < 3) continue;
                rivalry.coolOff = 0;
                rivalry.months--;
                if (rivalry.months <= 0) ai.rivalries.RemoveAt(i);
            }
        }

        static Rivalry FindRivalry(AIState ai, string countryId)
        {
            for (int i = 0; i < ai.rivalries.Count; i++)
                if (ai.rivalries[i].countryId == countryId) return ai.rivalries[i];
            return null;
        }

        /// <summary>A rival faced long enough to justify building against them.</summary>
        // ---------- research (2026-08) ----------

        /// <summary>
        /// A foreign government funds the capability its posture calls for.
        ///
        /// `TechnologySystem.ConsiderAiResearch` funds programmes along national
        /// priority at 6% a month and never walks a prerequisite chain, and the
        /// pre-fix treasury starved it besides — so no AI state ever held an
        /// instrument capability, and the player faced an instrument in 4 of
        /// 556 measured decades. This is the directed half: the same choice
        /// `PreferredInstrument` makes — the strongest pillar's instrument
        /// capability, walking its prerequisites — with a long-standing rival
        /// making it likelier and patience making it steadier. Same gates as the
        /// player's verb, no CP.
        /// </summary>
        static void ConsiderResearch(GameState state, AIState ai, CountryState country, Random rng)
        {
            if (country.technology.programs.Count >= TechnologySystem.MaxPrograms) return;

            float chance = 0.06f + ai.profile.patience / 600f
                           + (LongStandingRival(ai) != null ? 0.06f : 0f)
                           + PlanningHorizon(state.difficulty) * 0.04f;
            if (rng.NextDouble() >= chance) return;

            // Strongest pillar first, then the rest, so a state builds toward
            // the instrument it could actually use.
            var pillars = new List<Pillar>((Pillar[])Enum.GetValues(typeof(Pillar)));
            pillars.Sort((a, b) => country.pillars.Get(b).CompareTo(country.pillars.Get(a)));

            foreach (var pillar in pillars)
            {
                string goal = EndgameSystem.RequiredCapability(InstrumentFor(pillar));
                string next = NextMissingPrerequisite(country, goal);
                if (next == null) continue;
                if (country.technology.IsResearching(next)) continue;
                if (TechnologySystem.BeginResearchBy(state, country.id, next)) return;
            }
        }

        static EndgameType InstrumentFor(Pillar pillar)
        {
            switch (pillar)
            {
                case Pillar.Military: return EndgameType.StrategicDestruction;
                case Pillar.Economy: return EndgameType.SystemicCollapse;
                case Pillar.Intelligence: return EndgameType.StateDestabilization;
                case Pillar.Diplomacy: return EndgameType.StrategicIsolation;
                default: return EndgameType.TotalMobilization;
            }
        }

        /// <summary>The deepest capability on the way to <paramref name="goal"/> not yet held, or null if held.</summary>
        static string NextMissingPrerequisite(CountryState country, string goal)
        {
            string next = goal;
            for (int guard = 0; guard < 8; guard++)
            {
                if (TechnologySystem.Has(country, next)) return null;
                var definition = CapabilityCatalog.Find(next);
                if (definition == null) return null;
                string missing = null;
                foreach (var prerequisite in definition.prerequisites)
                    if (!TechnologySystem.Has(country, prerequisite)) { missing = prerequisite; break; }
                if (missing == null) return next;
                next = missing;
            }
            return null;
        }

        // ---------- coalitions (2026-08) ----------

        /// <summary>
        /// A government at war asks its friends to stand with it.
        ///
        /// Coalitions formed in 12 of 556 measured decades. AI states could
        /// *join* a defender-led coalition through an alliance obligation and
        /// could never assemble one: `RequestCoalition` was player-only (spec 06
        /// §7 open item). The same verb at the same recruitment test, reached
        /// once a war has become a war, by governments with the standing to ask.
        /// </summary>
        static void ConsiderCoalition(GameState state, AIState ai, CountryState country, Random rng)
        {
            var confrontation = state.ActiveConfrontationFor(country.id);
            if (confrontation == null || confrontation.resolved) return;
            if (confrontation.escalation < EscalationState.LimitedConflict) return;
            if (state.FindCoalitionLedBy(confrontation.id, country.id) != null) return;
            if (country.pillars.diplomacy < 40f) return;

            float chance = 0.12f + ai.profile.opportunism / 500f + ai.profile.aggression / 800f;
            if (rng.NextDouble() >= chance) return;

            DiplomacySystem.RequestCoalitionBy(state, country.id);
        }

        /// <summary>
        /// A government's temperament in the operator's language (2026-08): the
        /// authored personality rendered as reputation, so a posting's neighbours
        /// read as people rather than as four hidden numbers. Empty for the
        /// player's own state — its temperament is whatever the operator makes it.
        /// </summary>
        public static string TemperamentOf(GameState state, string countryId)
        {
            AIState ai = null;
            foreach (var candidate in state.aiStates) if (candidate.countryId == countryId) { ai = candidate; break; }
            if (ai == null) return "";
            var p = ai.profile;
            var words = new List<string>();
            words.Add(p.aggression >= 62f ? "HAWKISH" : p.aggression <= 36f ? "RESTRAINED" : "FIRM");
            if (p.caution >= 64f) words.Add("CAUTIOUS");
            else if (p.caution <= 42f) words.Add("BOLD");
            if (p.opportunism >= 64f) words.Add("OPPORTUNISTIC");
            if (p.patience >= 66f) words.Add("PATIENT");
            else if (p.patience <= 44f) words.Add("IMPATIENT");
            return string.Join(", ", words);
        }

        /// <summary>The state this government most wants weakened, by relations and perceived threat; null if nobody is hated enough.</summary>
        static string MostHatedState(GameState state, CountryState country)
        {
            string worst = null; float score = 0f;
            foreach (var other in state.countries)
            {
                if (other.id == country.id) continue;
                var relationship = state.FindRelationship(country.id, other.id);
                if (relationship == null || relationship.relations > 30f) continue;
                float s = (30f - relationship.relations) + relationship.ThreatPerceivedBy(country.id) * 0.5f;
                if (s > score) { score = s; worst = other.id; }
            }
            return worst;
        }

        static string LongStandingRival(AIState ai)
        {
            string longest = null;
            int best = 0;
            foreach (var rivalry in ai.rivalries)
            {
                if (rivalry.months < 24 || rivalry.months <= best) continue;
                best = rivalry.months;
                longest = rivalry.countryId;
            }
            return longest;
        }

        /// <summary>
        /// Governments build the instrument that fits the state they already are,
        /// preferring one already part-prepared over starting something new.
        /// </summary>
        static EndgameType? PreferredInstrument(GameState state, CountryState country)
        {
            EndgameType? best = null;
            float bestScore = float.MinValue;

            foreach (EndgameType type in Enum.GetValues(typeof(EndgameType)))
            {
                if (!EndgameSystem.CanPrepare(state, country, type, out _)) continue;
                float progress = country.endgames.ProgressFor(type);
                if (progress >= 100f) continue;

                float score = country.pillars.Get(EndgameSystem.PillarOf(type)) + progress * 0.8f;
                if (score <= bestScore) continue;
                bestScore = score;
                best = type;
            }
            return best;
        }

        /// <summary>Whether this state has had an existential instrument used on it by a given actor.</summary>
        static bool SufferedExistentialAttack(GameState state, string countryId, string byId)
        {
            foreach (var record in state.endgameRecords)
            {
                if (record.targetId != countryId || record.actorId != byId) continue;
                if (record.severity != StrategicSeverity.Existential) continue;
                if (state.date.MonthsSince(record.date) <= 12) return true;
            }
            return false;
        }

        // ---------- perception helpers ----------

        /// <summary>
        /// What this observer believes about a target's strength. Falls back to a
        /// cautious public-information guess when no collection exists — the AI
        /// never reads true foreign state.
        /// </summary>
        /// <summary>
        /// A foreign garrison as this government's reporting has it: the
        /// midpoint of the same band the player is shown, or a flat prior of 50
        /// when nothing has been collected. Never the true figure.
        /// </summary>
        public static float PerceivedGarrison(GameState state, string observerId, StrategicLocation location)
        {
            if (location == null) return 50f;
            if (location.ownerId == observerId) return location.garrison;
            if (IntelligenceSystem.TryEstimateGarrison(state, observerId, location,
                    out float low, out float high, out var confidence)
                && confidence != ConfidenceGrade.None)
                return (low + high) * 0.5f;
            return 50f;
        }

        public static float PerceivedStrength(GameState state, string observerId, CountryState target, IntelDomain domain)
        {
            var estimate = IntelligenceSystem.GetEstimate(state, observerId, target.id, domain);
            if (estimate != null && estimate.confidence != ConfidenceGrade.None)
                return estimate.reportedValue;

            // No reporting: fall back to what anyone can observe publicly.
            return 40f + target.economy.marketIndex * 0.1f;
        }

        /// <summary>0..1 confidence weight for an estimate, 0.35 when nothing is known.</summary>
        public static float EstimateConfidence(GameState state, string observerId, string targetId, IntelDomain domain)
        {
            var estimate = IntelligenceSystem.GetEstimate(state, observerId, targetId, domain);
            if (estimate == null) return 0.35f;
            switch (estimate.confidence)
            {
                case ConfidenceGrade.Confirmed: return 1f;
                case ConfidenceGrade.High: return 0.85f;
                case ConfidenceGrade.Moderate: return 0.65f;
                case ConfidenceGrade.Low: return 0.45f;
                default: return 0.35f;
            }
        }

        static float Approach(float current, float target, float rate) => current + (target - current) * rate;
        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
