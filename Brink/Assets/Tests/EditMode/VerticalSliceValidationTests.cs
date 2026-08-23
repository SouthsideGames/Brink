using System;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Vertical slice validation (GDD §34, Phase 12).
    ///
    /// These are not unit tests of a system — they play the game. Each harness
    /// runs a full decade under a distinct playstyle and asserts the design's
    /// own promises: every pillar is viable as a primary playstyle, no single
    /// approach dominates, engagement beats passivity, the world evolves on its
    /// own, and nothing degenerates over a long save.
    /// </summary>
    public class VerticalSliceValidationTests
    {
        const int Months = 120; // ten years

        [SetUp]
        public void SetUp() => GameLog.MirrorToUnityConsole = false;

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- harness ----------

        class PlaythroughResult
        {
            public string playstyle;
            public GameState state;
            public float averageGrade;
            public int totalXP;
            public int skillPointsEarned;
            public int decisionsTaken;
            public int monthsCommandPointsExhausted;
            public float finalPillarTotal;
            public float finalGdp;
        }

        /// <summary>
        /// The real monthly simulation — the same one `GameController` runs.
        ///
        /// This used to be a hand-copied list, and it had silently drifted from
        /// the game by **five systems**: `RegimeSystem`, `TechnologySystem`,
        /// `EndgameSystem`, `TerritorySystem` and `CabinetLifecycle`. Every
        /// balance figure this harness produced was therefore measured in a world
        /// with no coups, no research, no strategic instruments, no value to
        /// holding ground and no cabinet turnover — and reported as if it were
        /// the game.
        ///
        /// A validation harness that does not run the thing it validates is worse
        /// than no harness, because it is trusted. Never re-inline this list;
        /// add monthly systems to `SimulationPipeline`.
        /// </summary>
        static TurnManager BuildSimulation(GameState state)
        {
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            return turns;
        }

        /// <summary>Play a decade with a given monthly decision routine.</summary>
        static PlaythroughResult Play(string playstyle, int seed,
            Action<GameState, TurnManager, PlaythroughResult> monthlyDecisions)
        {
            var state = WorldFactory.CreateDebugWorld(seed);
            state.difficulty = Difficulty.Challenging;
            var turns = BuildSimulation(state);
            var result = new PlaythroughResult { playstyle = playstyle, state = state };

            for (int month = 0; month < Months; month++)
            {
                // A crisis always demands an answer before the month can end.
                while (state.HasOpenCrisis)
                {
                    var crisis = state.activeCrises[0];
                    CrisisSystem.Resolve(state, crisis, ChooseCrisisOption(state, crisis));
                    result.decisionsTaken++;
                }

                // Fill any vacant ministry before doing anything else. Every
                // playstyle needs this, not just the ones that touch the
                // Cabinet: offices now fall empty on their own, and a bot that
                // reaches for a minister who has just died gets a null.
                //
                // The bot takes the ablest candidate — an operator's instinct,
                // and deliberately not the safe one the government would confirm
                // by default, so the two paths stay distinguishable.
                var operatorNation = state.PlayerCountry;
                for (int v = operatorNation.vacancies.Count - 1; v >= 0; v--)
                {
                    var vacancy = operatorNation.vacancies[v];
                    int best = 0;
                    for (int c = 1; c < vacancy.candidates.Count; c++)
                        if (vacancy.candidates[c].competence > vacancy.candidates[best].competence) best = c;
                    if (CabinetLifecycle.Appoint(state, vacancy.office, best)) result.decisionsTaken++;
                }

                // An engaged operator keeps acting while capacity remains, rather
                // than taking one token action a month.
                if (monthlyDecisions != null)
                {
                    for (int action = 0; action < 4; action++)
                    {
                        int cpBefore = state.commandPoints.current;
                        float pcBefore = state.politicalCapital;
                        int influenceBefore = state.influence;

                        monthlyDecisions(state, turns, result);

                        bool spentSomething = state.commandPoints.current != cpBefore
                                              || System.Math.Abs(state.politicalCapital - pcBefore) > 0.001f
                                              || state.influence != influenceBefore;
                        if (!spentSomething) break; // nothing further to do this month
                    }
                }

                SpendSkillPoints(state);

                if (state.commandPoints.current == 0) result.monthsCommandPointsExhausted++;
                turns.EndMonth();
            }

            float gradeTotal = 0f;
            foreach (var evaluation in state.evaluations)
            {
                gradeTotal += (int)evaluation.grade;
                result.skillPointsEarned += evaluation.skillPointsAwarded;
            }
            result.averageGrade = state.evaluations.Count > 0 ? gradeTotal / state.evaluations.Count : 0f;
            result.totalXP = state.strategistXP;

            var player = state.PlayerCountry;
            result.finalPillarTotal = player.pillars.military + player.pillars.economy
                                      + player.pillars.intelligence + player.pillars.diplomacy
                                      + player.pillars.government;
            result.finalGdp = player.economy.gdp;
            return result;
        }

        /// <summary>Pick the crisis option that costs the least political ground.</summary>
        static int ChooseCrisisOption(GameState state, ActiveCrisis crisis)
        {
            int best = 0;
            float bestValue = float.MinValue;
            for (int i = 0; i < crisis.options.Count; i++)
            {
                var option = crisis.options[i];
                float value = option.approvalDelta + option.stabilityDelta + option.unityDelta
                              + option.treasuryDelta / 200f;
                if (value > bestValue) { bestValue = value; best = i; }
            }
            return best;
        }

        /// <summary>Invest skill points as they arrive, cheapest available first.</summary>
        static void SpendSkillPoints(GameState state)
        {
            bool spent = true;
            while (spent && state.skillPoints > 0)
            {
                spent = false;
                foreach (var node in SkillCatalog.Nodes)
                {
                    if (!ProgressionSystem.CanUnlock(state, node.id, out _)) continue;
                    ProgressionSystem.Unlock(state, node.id);
                    spent = true;
                    break;
                }
            }
        }

        // ---------- playstyles ----------

        static void PassivePlay(GameState state, TurnManager turns, PlaythroughResult result)
        {
            // Deliberately does nothing: the control case for player agency.
        }

        static void MilitaryPlay(GameState state, TurnManager turns, PlaythroughResult result)
        {
            var confrontation = state.ActiveConfrontation;

            if (confrontation == null)
            {
                var lane = state.FindLocation("CONTESTED_LANE");
                if (lane != null && lane.ownerId != state.playerCountryId && state.commandPoints.current >= 3)
                {
                    if (ConfrontationSystem.Begin(state, turns, state.playerCountryId, lane.ownerId,
                            ConfrontationObjective.TerritorialConcession, lane.id, PrimaryStrategy.Military) != null)
                    {
                        result.decisionsTaken++;
                        return;
                    }
                }

                // Peacetime is not idle time: train with whoever will train with us.
                foreach (var country in state.countries)
                {
                    if (country.isPlayer) continue;
                    if (!ExerciseSystem.CanExerciseWith(state, country.id, out _)) continue;
                    var scale = state.commandPoints.current >= 3 ? ExerciseScale.Full : ExerciseScale.Standard;
                    if (state.commandPoints.current < ExerciseSystem.CostFor(scale)) break;
                    if (ExerciseSystem.Conduct(state, turns, country.id, scale, ExerciseFocus.Combined) != null)
                        result.decisionsTaken++;
                    return;
                }

                // The standing peacetime work of a military operator: posture,
                // doctrine, sustainment and force structure (GDD §19).
                var mil = state.PlayerCountry.military;

                if (mil.doctrine == MilitaryDoctrine.Balanced && state.commandPoints.current >= MilitarySystem.DoctrineCost)
                {
                    if (MilitarySystem.SetDoctrine(state, turns, MilitaryDoctrine.Deterrence))
                        result.decisionsTaken++;
                    return;
                }

                if (mil.posture == MilitaryPosture.Peacetime && state.commandPoints.current >= 1)
                {
                    if (MilitarySystem.SetPosture(state, turns, MilitaryPosture.Alert)) result.decisionsTaken++;
                    return;
                }

                if (mil.programs.Count < MilitarySystem.MaxPrograms && state.commandPoints.current >= 2)
                {
                    var branch = mil.ground.EffectivePower <= mil.air.EffectivePower
                        ? ForceBranch.Ground
                        : (mil.air.EffectivePower <= mil.naval.EffectivePower ? ForceBranch.Air : ForceBranch.Naval);
                    if (MilitarySystem.BeginProcurement(state, turns, branch, MilitarySystem.ProgramScale.Major))
                        result.decisionsTaken++;
                    return;
                }

                if (mil.logistics < 80f && state.commandPoints.current >= MilitarySystem.LogisticsInvestmentCost)
                    if (MilitarySystem.InvestInLogistics(state, turns)) result.decisionsTaken++;
                return;
            }

            var player = state.PlayerCountry;

            // Sue for terms when the war stops paying for itself.
            if (player.warExhaustion > 45f || player.warSupport < 25f)
            {
                if (ConfrontationSystem.ProposeSettlement(state, confrontation)) result.decisionsTaken++;
                else if (player.warExhaustion > 75f) ConfrontationSystem.ProposeSettlement(state, confrontation, true);
                return;
            }

            if (ConfrontationSystem.OpponentWouldAccept(state, confrontation))
            {
                if (ConfrontationSystem.ProposeSettlement(state, confrontation)) result.decisionsTaken++;
                return;
            }

            if (confrontation.escalation < EscalationState.LimitedConflict)
            {
                var next = (EscalationState)((int)confrontation.escalation + 1);
                if (ConfrontationSystem.SetEscalation(state, turns, confrontation, next)) result.decisionsTaken++;
                return;
            }

            var directive = new OperationDirective
            {
                speedPriority = 55f, casualtyTolerance = 45f, civilianRiskLimit = 25f
            };

            // Sequence rather than charging every month. Softening the works
            // first is what the wider verb list is *for*, and a harness that
            // only ever assaults measures a game nobody plays — which is how the
            // pipeline drift was allowed to hide for so long.
            var objective = state.FindLocation(confrontation.objectiveLocationId);
            var chosen = OperationType.Assault;
            if (objective != null)
            {
                if (objective.defenseValue > 45f
                    && OperationCatalog.CanOrder(state, state.playerCountryId, objective,
                        OperationType.SuppressDefenses))
                    chosen = OperationType.SuppressDefenses;
                else if (!OperationCatalog.CanOrder(state, state.playerCountryId, objective, chosen)
                         && OperationCatalog.CanOrder(state, state.playerCountryId, objective,
                             OperationType.AmphibiousAssault))
                    chosen = OperationType.AmphibiousAssault;
            }

            if (ConfrontationSystem.LaunchOperation(state, turns, confrontation,
                    confrontation.objectiveLocationId, chosen, directive) != null)
                result.decisionsTaken++;
        }

        static void EconomicPlay(GameState state, TurnManager turns, PlaythroughResult result)
        {
            var official = state.FindOfficial(Pillar.Economy);
            if (official == null) return; // office vacant this month; the loop above fills it
            if (official.mode != ControlMode.Directed && state.influence >= 1)
            {
                if (CabinetSystem.SetMode(state, official, ControlMode.Directed)) result.decisionsTaken++;
                CabinetSystem.SetDirective(state, official, "ECO_GROWTH");
                return;
            }

            // Coerce the rival we are least exposed to — but a sane operator runs
            // a few campaigns, not a war against everyone. Sanctioning the whole
            // world is self-destruction, and the model correctly says so.
            const int maxConcurrentRegimes = 3;
            int ourRegimes = 0;
            foreach (var sanction in state.sanctions)
                if (sanction.senderId == state.playerCountryId) ourRegimes++;

            string leastExposed = null;
            float lowestVolume = float.MaxValue;
            if (ourRegimes < maxConcurrentRegimes)
            {
                foreach (var country in state.countries)
                {
                    if (country.isPlayer) continue;
                    if (state.FindSanction(state.playerCountryId, country.id) != null) continue;
                    var link = state.FindTrade(state.playerCountryId, country.id);
                    float volume = link?.volume ?? 0f;
                    if (volume < lowestVolume) { lowestVolume = volume; leastExposed = country.id; }
                }
            }

            if (leastExposed != null && state.commandPoints.current >= 2)
            {
                if (EconomySystem.ImposeSanctions(state, turns, leastExposed, SanctionSeverity.Pressure))
                    result.decisionsTaken++;
                return;
            }

            if (state.commandPoints.current >= 2)
                if (CabinetSystem.TryDirectAction(state, turns, Pillar.Economy)) result.decisionsTaken++;
        }

        static void IntelligencePlay(GameState state, TurnManager turns, PlaythroughResult result)
        {
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (state.FindNetwork(state.playerCountryId, country.id) != null) continue;
                if (state.commandPoints.current < IntelligenceSystem.EstablishNetworkCost) return;
                if (IntelligenceSystem.EstablishNetwork(state, turns, country.id, IntelDomain.Military))
                    result.decisionsTaken++;
                return;
            }

            if (state.PlayerCountry.counterIntel.counterIntelligence < 60f && state.commandPoints.current >= 1)
            {
                if (IntelligenceSystem.StrengthenCounterIntelligence(state, turns)) result.decisionsTaken++;
                return;
            }

            foreach (var network in state.networks)
            {
                if (network.ownerId != state.playerCountryId) continue;
                if (network.penetration < 70f)
                {
                    if (state.commandPoints.current >= 1 &&
                        IntelligenceSystem.ExpandNetwork(state, turns, network.targetId)) result.decisionsTaken++;
                    return;
                }
                if (state.commandPoints.current >= 2 &&
                    IntelligenceSystem.RunCovertOperation(state, turns, network.targetId, CovertOperation.TheftOfPlans))
                    result.decisionsTaken++;
                return;
            }
        }

        static void DiplomaticPlay(GameState state, TurnManager turns, PlaythroughResult result)
        {
            // Warm the coldest relationship we do not already have a treaty with.
            string coldest = null;
            float lowest = float.MaxValue;
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var relationship = state.FindRelationship(state.playerCountryId, country.id);
                if (relationship == null) continue;
                if (relationship.relations < lowest) { lowest = relationship.relations; coldest = country.id; }
            }

            // Try to convert the warmest into a treaty.
            string warmest = null;
            float highest = float.MinValue;
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (state.FindTreaty(state.playerCountryId, country.id) != null) continue;
                var relationship = state.FindRelationship(state.playerCountryId, country.id);
                if (relationship != null && relationship.relations > highest)
                {
                    highest = relationship.relations;
                    warmest = country.id;
                }
            }

            if (warmest != null && highest > 62f && state.commandPoints.current >= DiplomacySystem.TreatyProposalCost)
            {
                var commitments = new List<TreatyCommitment>
                {
                    TreatyCommitment.NonAggression, TreatyCommitment.TradePreference
                };
                if (DiplomacySystem.ProposeTreaty(state, turns, warmest, commitments)) result.decisionsTaken++;
                return;
            }

            // Note: a competent diplomat never breaks a standing treaty to seek a
            // better one. Repudiation costs trust with every state watching, and
            // the system has no amendment path — so this bot does not try.

            // Exercises build the interoperability that makes alliances worth having.
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (!ExerciseSystem.CanExerciseWith(state, country.id, out _)) continue;
                if (state.commandPoints.current < ExerciseSystem.CostFor(ExerciseScale.Standard)) break;
                if (ExerciseSystem.Conduct(state, turns, country.id, ExerciseScale.Standard,
                        ExerciseFocus.Combined) != null) result.decisionsTaken++;
                return;
            }

            if (coldest != null && state.commandPoints.current >= 1)
                if (DiplomacySystem.Outreach(state, turns, coldest)) result.decisionsTaken++;
        }

        static void GovernmentPlay(GameState state, TurnManager turns, PlaythroughResult result)
        {
            var player = state.PlayerCountry;
            var gov = player.government;

            // The one large purchase, taken as soon as it is affordable and the
            // chamber will hear it. Before this existed the pillar had no sink
            // above 7 PC against a cap of 20, so a government operator spent most
            // of the decade sitting at the cap with nothing worth buying — which
            // is most of why this playstyle took 26 decisions in ten years.
            if (state.politicalCapital >= GovernmentSystem.ConsolidateAuthorityCost)
            {
                foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
                {
                    if (AuthoritySystem.AuthorityOver(state, pillar) == AuthoritySystem.AuthorityLevel.Direct)
                        continue;
                    if (!GovernmentSystem.ConsolidateAuthority(state, pillar)) break;
                    result.decisionsTaken++;
                    return;
                }
            }

            if (state.politicalCapital >= GovernmentSystem.InstitutionalReformCost && player.pillars.government < 85f)
            {
                if (GovernmentSystem.InstitutionalReform(state)) result.decisionsTaken++;
                return;
            }

            // Keep the succession orderly before the leader is old enough for it
            // to be somebody else's decision.
            if (gov.leader.age > 62f && gov.successorReadiness < 60f
                && state.politicalCapital >= GovernmentSystem.GroomSuccessorCost)
            {
                if (GovernmentSystem.GroomSuccessor(state)) result.decisionsTaken++;
                return;
            }

            // Audit the weakest desk — competence decides what reaches the
            // terminal at all, so this buys awareness as well as performance.
            Official weakest = null;
            foreach (var official in state.cabinet)
                if (weakest == null || official.competence < weakest.competence) weakest = official;
            if (weakest != null && weakest.competence < 50f
                && state.politicalCapital >= GovernmentSystem.InquiryCost)
            {
                if (GovernmentSystem.LaunchInquiry(state)) result.decisionsTaken++;
                return;
            }

            if (player.governmentApproval < 55f && state.politicalCapital >= GovernmentSystem.PublicMessagingCost)
            {
                if (GovernmentSystem.PublicMessaging(state)) result.decisionsTaken++;
                return;
            }

            // The workhorse, and the reason the pillar now has something to do
            // most months: bought support decays, so it has to be maintained.
            float backing = gov.IsElective ? gov.legislativeSupport : gov.eliteCohesion;
            if (backing < 70f)
            {
                if (state.politicalCapital >= GovernmentSystem.BuildSupportCost
                    && GovernmentSystem.BuildPoliticalSupport(state))
                {
                    result.decisionsTaken++;
                    return;
                }
                if (player.resources.treasury >= GovernmentSystem.PatronageTreasury
                    && GovernmentSystem.DistributePatronage(state))
                {
                    result.decisionsTaken++;
                    return;
                }
            }

            // Keep the Cabinet competent.
            foreach (var official in state.cabinet)
            {
                if (official.competence >= 55f) continue;
                if (state.politicalCapital < GovernmentSystem.DismissOfficialCost * 1.25f) break;
                if (GovernmentSystem.DismissOfficial(state, official.office)) result.decisionsTaken++;
                return;
            }
        }

        static List<PlaythroughResult> RunAllPlaystyles(int seed)
        {
            return new List<PlaythroughResult>
            {
                Play("MILITARY", seed, MilitaryPlay),
                Play("ECONOMY", seed, EconomicPlay),
                Play("INTELLIGENCE", seed, IntelligencePlay),
                Play("DIPLOMACY", seed, DiplomaticPlay),
                Play("GOVERNMENT", seed, GovernmentPlay)
            };
        }

        // ---------- validation ----------

        /// <summary>
        /// Prints the balance picture for the slice. Not an assertion — this is
        /// the report a designer reads when deciding whether the loop is ready
        /// to expand (GDD §34 Phase 12).
        /// </summary>
        [Test]
        public void Report_VerticalSliceBalance()
        {
            GameLog.MirrorToUnityConsole = true;
            const int seed = 20260820;

            var passive = Play("PASSIVE", seed, PassivePlay);
            var runs = RunAllPlaystyles(seed);

            var report = new System.Text.StringBuilder();
            report.AppendLine();
            report.AppendLine("=== VERTICAL SLICE VALIDATION — 10 SIMULATED YEARS ===");
            report.AppendLine($"seed {seed}, difficulty Challenging");
            report.AppendLine();
            report.AppendLine("PLAYSTYLE      AVG GRADE  XP     SP   DECISIONS  CP-DRY MONTHS  PILLARS  GDP");
            report.AppendLine("(grade scale: F=0 D=1 C=2 B=3 A=4 S=5)");

            void Line(PlaythroughResult run)
            {
                report.AppendLine(
                    $"{run.playstyle,-14} {run.averageGrade,6:F2}  {run.totalXP,6} {run.skillPointsEarned,4}   " +
                    $"{run.decisionsTaken,6}      {run.monthsCommandPointsExhausted,6}      " +
                    $"{run.finalPillarTotal,6:F0}  {run.finalGdp,7:F0}");
            }

            Line(passive);
            foreach (var run in runs) Line(run);

            report.AppendLine();
            report.AppendLine("WORLD STATE AFTER A DECADE (diplomatic playthrough)");
            var world = runs[3].state;
            foreach (var country in world.countries)
            {
                report.AppendLine($"  {country.displayName,-16} " +
                                  $"MIL {country.pillars.military,5:F1}  ECO {country.pillars.economy,5:F1}  " +
                                  $"GDP {country.economy.gdp,7:F0}  IDX {country.economy.marketIndex,6:F1}  " +
                                  $"LEADER {country.government.leader.name}");
            }
            report.AppendLine($"  chronicle entries: {world.chronicle.Count}   " +
                              $"treaties: {world.treaties.Count}   confrontations: {world.confrontations.Count}   " +
                              $"sanctions: {world.sanctions.Count}");
            report.AppendLine($"  military playthrough: {runs[0].state.exercises.Count} joint exercises, " +
                              $"{runs[0].state.confrontations.Count} confrontations");

            GameLog.Info("VALIDATION", report.ToString());
            Assert.Pass(report.ToString());
        }

        /// <summary>
        /// Aggregates every playstyle across several seeds. Balance conclusions
        /// drawn from a single seed are noise; this is the table to tune against.
        /// </summary>
        [Test]
        public void Report_MultiSeedBalance()
        {
            GameLog.MirrorToUnityConsole = true;
            int[] seeds = { 11117, 22229, 33331, 44449, 55557 };

            var names = new List<string> { "PASSIVE", "MILITARY", "ECONOMY", "INTELLIGENCE", "DIPLOMACY", "GOVERNMENT" };
            var gradeTotals = new Dictionary<string, float>();
            var xpTotals = new Dictionary<string, float>();
            var decisionTotals = new Dictionary<string, float>();
            var gdpTotals = new Dictionary<string, float>();
            var componentSamples = new Dictionary<string, List<EvaluationRecord>>();
            foreach (var name in names)
            {
                gradeTotals[name] = 0f; xpTotals[name] = 0f;
                decisionTotals[name] = 0f; gdpTotals[name] = 0f;
                componentSamples[name] = new List<EvaluationRecord>();
            }

            foreach (int seed in seeds)
            {
                var runs = new List<PlaythroughResult> { Play("PASSIVE", seed, PassivePlay) };
                runs.AddRange(RunAllPlaystyles(seed));

                foreach (var run in runs)
                {
                    gradeTotals[run.playstyle] += run.averageGrade;
                    xpTotals[run.playstyle] += run.totalXP;
                    decisionTotals[run.playstyle] += run.decisionsTaken;
                    gdpTotals[run.playstyle] += run.finalGdp;
                    componentSamples[run.playstyle].AddRange(run.state.evaluations);
                }
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine();
            report.AppendLine($"=== MULTI-SEED BALANCE — {seeds.Length} SEEDS × 10 YEARS ===");
            report.AppendLine("PLAYSTYLE      MEAN GRADE   MEAN XP   MEAN DECISIONS   MEAN GDP   VS PASSIVE");

            float passiveGrade = gradeTotals["PASSIVE"] / seeds.Length;
            foreach (var name in names)
            {
                float grade = gradeTotals[name] / seeds.Length;
                report.AppendLine(
                    $"{name,-14} {grade,7:F2}   {xpTotals[name] / seeds.Length,8:F0}   " +
                    $"{decisionTotals[name] / seeds.Length,12:F0}   {gdpTotals[name] / seeds.Length,8:F0}   " +
                    $"{(name == "PASSIVE" ? "—" : (grade - passiveGrade).ToString("+0.00;-0.00;0.00"))}");
            }

            // Which component is actually driving each playstyle's grade?
            report.AppendLine();
            report.AppendLine("MEAN EVALUATION COMPONENTS (all years, all seeds)");
            report.AppendLine("PLAYSTYLE       TRAJ   ECON   STAB    POS   CRIS   INIT");
            foreach (var name in names)
            {
                float traj = 0f, econ = 0f, stab = 0f, pos = 0f, cris = 0f, init = 0f;
                int count = 0;
                foreach (var record in componentSamples[name])
                {
                    traj += record.trajectoryScore; econ += record.economyScore;
                    stab += record.stabilityScore; pos += record.positionScore;
                    cris += record.crisisScore; init += record.initiativeScore;
                    count++;
                }
                if (count == 0) continue;
                report.AppendLine($"{name,-14} {traj / count,6:F1} {econ / count,6:F1} {stab / count,6:F1} " +
                                  $"{pos / count,6:F1} {cris / count,6:F1} {init / count,6:F1}");
            }

            report.Append(WarOutcomeReport(seeds));

            GameLog.Info("VALIDATION", report.ToString());
            Assert.Pass(report.ToString());
        }

        /// <summary>
        /// Does fighting achieve anything?
        ///
        /// The military playstyle's ECON evaluation component sits far below
        /// every other playstyle's, and there are two entirely different reasons
        /// that could be true: militarism might be correctly modelled as a losing
        /// proposition unless it takes something, or the bot might simply be
        /// fighting decade-long wars and never capturing anything. Those call for
        /// opposite fixes, so measure before tuning.
        /// </summary>
        static string WarOutcomeReport(int[] seeds)
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine();
            report.AppendLine("MILITARY PLAYSTYLE — DID FIGHTING ACHIEVE ANYTHING?");
            report.AppendLine("SEED    OUR WARS  WORLD  MONTHS AT WAR  OPS  SETTLED  LANE  NET LOC  GDP");

            int totalWars = 0, totalHeld = 0, totalLaneHeld = 0;

            foreach (int seed in seeds)
            {
                var run = Play("MILITARY", seed, MilitaryPlay);
                var state = run.state;

                // Count the player's own confrontations separately from the
                // world's. state.confrontations holds every war on the map
                // including AI-vs-AI, and conflating the two turns a normal
                // decade into a false report of constant player warfare.
                int worldWars = state.confrontations.Count;
                int wars = 0, monthsAtWar = 0, settled = 0;
                foreach (var confrontation in state.confrontations)
                {
                    if (!confrontation.Involves(state.playerCountryId)) continue;
                    wars++;
                    monthsAtWar += confrontation.monthsActive;
                    if (confrontation.resolved) settled++;
                }

                int operations = 0;
                foreach (var entry in state.chronicle)
                    if (entry.category == ChronicleCategory.Military
                        && entry.countryId == state.playerCountryId) operations++;

                int held = 0, originallyOurs = 0;
                foreach (var location in state.locations)
                {
                    if (location.ownerId == state.playerCountryId) held++;
                    if (location.originalOwnerId == state.playerCountryId) originallyOurs++;
                }

                var lane = state.FindLocation("CONTESTED_LANE");
                bool laneHeld = lane != null && lane.ownerId == state.playerCountryId;

                totalWars += wars;
                totalHeld += held - originallyOurs;
                if (laneHeld) totalLaneHeld++;

                report.AppendLine(
                    $"{seed,-7} {wars,8}  {worldWars,5}  {monthsAtWar,13}  {operations,3}  " +
                    $"{settled,7}  {(laneHeld ? "YES" : "no"),4}  {held - originallyOurs,7}  " +
                    $"{run.finalGdp,5:F0}");
            }

            report.AppendLine();
            report.AppendLine(
                $"Across {seeds.Length} seeds: {totalWars} player confrontations, " +
                $"contested lane held at the end of {totalLaneHeld}/{seeds.Length}, " +
                $"net territory {(totalHeld >= 0 ? "+" : "")}{totalHeld}.");

            // Three distinguishable readings, because they call for opposite fixes.
            if (totalLaneHeld < seeds.Length / 2)
                report.AppendLine(
                    "The bot rarely takes its own objective — this is a bot problem, "
                    + "not a balance problem. Fix the playstyle before touching yields.");
            else if (totalHeld <= 0)
                report.AppendLine(
                    "The bot TAKES ITS OBJECTIVE in most seeds and still ends with no net "
                    + "territory: it wins the war it started and loses ground elsewhere while "
                    + "committed. So militarism is not failing to achieve anything — it is "
                    + "paying for a won objective with what it cannot defend meanwhile. "
                    + "Raising conquest yields would not address that; the cost is the "
                    + "opportunity cost of commitment.");
            else
                report.AppendLine(
                    "Fighting nets ground, so a low ECON score is the price of conquest "
                    + "rather than evidence of a bug.");

            return report.ToString();
        }

        [Test]
        public void Decade_CompletesWithoutDegenerating()
        {
            foreach (var run in RunAllPlaystyles(31337))
            {
                var state = run.state;
                var player = state.PlayerCountry;

                Assert.AreEqual(Months, state.date.MonthsSince(state.startDate), $"{run.playstyle} lost months.");
                Assert.AreEqual(10, state.evaluations.Count, $"{run.playstyle} missed an annual evaluation.");

                foreach (var country in state.countries)
                {
                    Assert.IsFalse(float.IsNaN(country.economy.gdp), $"{run.playstyle}: {country.id} GDP became NaN.");
                    Assert.Greater(country.economy.gdp, 0f, $"{run.playstyle}: {country.id} economy vanished.");
                    Assert.IsFalse(float.IsInfinity(country.economy.marketIndex));

                    foreach (Pillar pillar in Enum.GetValues(typeof(Pillar)))
                    {
                        float value = country.pillars.Get(pillar);
                        Assert.GreaterOrEqual(value, 0f, $"{run.playstyle}: {country.id} {pillar} underflowed.");
                        Assert.LessOrEqual(value, 100f, $"{run.playstyle}: {country.id} {pillar} overflowed.");
                    }

                    Assert.GreaterOrEqual(country.stability, 0f);
                    Assert.LessOrEqual(country.stability, 100f);
                    Assert.IsNotEmpty(country.government.leader.name);
                }

                Assert.GreaterOrEqual(player.resources.manpower, 0f, $"{run.playstyle}: manpower went negative.");
                Assert.LessOrEqual(state.notifications.Count, GameState.MaxNotifications);
            }
        }

        [Test]
        public void CapabilityDoesNotSaturateOverALongSave()
        {
            // A save can run for decades (GDD §6). If every power pins at the
            // ceiling in ten years there is nothing left to play for.
            var run = Play("GOVERNMENT", 1717, GovernmentPlay);

            foreach (var country in run.state.countries)
            {
                foreach (Pillar pillar in Enum.GetValues(typeof(Pillar)))
                {
                    Assert.Less(country.pillars.Get(pillar), 98f,
                        $"{country.id} {pillar} saturated at the ceiling after only a decade.");
                }
            }
        }

        [Test]
        public void MarketIndexStaysReadableOverADecade()
        {
            // The index is a readable ASCII chart, not a compounding curve (§20.1).
            var run = Play("ECONOMY", 1818, EconomicPlay);

            foreach (var country in run.state.countries)
            {
                float index = country.economy.marketIndex;
                Assert.Greater(index, 5f, $"{country.id} market index collapsed.");
                Assert.Less(index, 400f,
                    $"{country.id} market index ran away to {index:F0} — the chart becomes meaningless.");
            }
        }

        [Test]
        public void ActiveManagementOutperformsDrift()
        {
            // Delegation stays legitimate — a passive year still passes — but the
            // best grades must belong to operators who actually acted (GDD §25.2).
            const int seed = 2727;
            var passive = Play("PASSIVE", seed, PassivePlay);
            var runs = RunAllPlaystyles(seed);

            float activeTotal = 0f;
            float bestActive = float.MinValue;
            foreach (var run in runs)
            {
                activeTotal += run.averageGrade;
                if (run.averageGrade > bestActive) bestActive = run.averageGrade;
            }

            Assert.Greater(bestActive, passive.averageGrade,
                "No playstyle out-graded doing nothing — the operator has no impact on assessment.");
            Assert.GreaterOrEqual(activeTotal / runs.Count, passive.averageGrade - 0.35f,
                "Active play averaged materially worse than passivity.");
            Assert.GreaterOrEqual(passive.averageGrade, (int)EvaluationGrade.C,
                "A delegated, uneventful decade should still pass — delegation is legitimate.");
        }

        [Test]
        public void EveryPillarIsViableAsAPrimaryPlaystyle()
        {
            // GDD §3: every pillar must be viable as a primary playstyle.
            foreach (var run in RunAllPlaystyles(4242))
            {
                Assert.GreaterOrEqual(run.averageGrade, (int)EvaluationGrade.C,
                    $"{run.playstyle} play averaged below a passing grade — that pillar is not viable.");
                Assert.Greater(run.skillPointsEarned, 0,
                    $"{run.playstyle} play earned no skill points across a decade.");
            }
        }

        [Test]
        public void NoSinglePlaystyleDominates()
        {
            var runs = RunAllPlaystyles(5150);

            float best = float.MinValue, worst = float.MaxValue;
            string bestName = "", worstName = "";
            foreach (var run in runs)
            {
                if (run.averageGrade > best) { best = run.averageGrade; bestName = run.playstyle; }
                if (run.averageGrade < worst) { worst = run.averageGrade; worstName = run.playstyle; }
            }

            Assert.Less(best - worst, 2.5f,
                $"{bestName} ({best:F2}) outclasses {worstName} ({worst:F2}) by more than two grade steps.");
        }

        [Test]
        public void EngagementBeatsPassivity()
        {
            const int seed = 8080;
            var passive = Play("PASSIVE", seed, PassivePlay);
            var runs = RunAllPlaystyles(seed);

            foreach (var run in runs)
            {
                Assert.Greater(run.decisionsTaken, 10,
                    $"{run.playstyle} play barely made any decisions in a decade.");
                Assert.Greater(run.totalXP, passive.totalXP,
                    $"{run.playstyle} play earned no more experience than doing nothing at all.");
            }
        }

        [Test]
        public void CommandPointsAreABindingConstraint()
        {
            // If CP is never exhausted it is not a real scarcity (GDD §7.1).
            var runs = RunAllPlaystyles(6161);
            int constrained = 0;
            foreach (var run in runs)
                if (run.monthsCommandPointsExhausted > 5) constrained++;

            Assert.Greater(constrained, 0,
                "No playstyle ever ran short of Command Points; the core scarcity is not biting.");
        }

        [Test]
        public void WorldEvolvesIndependentlyOverTheDecade()
        {
            var run = Play("DIPLOMACY", 7272, DiplomaticPlay);
            var state = run.state;

            Assert.Greater(state.chronicle.Count, 25,
                "A decade should leave a substantial historical record (GDD §31.3).");

            // Foreign leadership should have turned over somewhere.
            bool leadershipChanged = false;
            foreach (var country in state.countries)
                if (!country.isPlayer && country.government.leader.monthsInOffice < Months)
                    leadershipChanged = true;
            Assert.IsTrue(leadershipChanged, "No foreign government changed hands in ten years.");

            // The AI should have acted on its own initiative.
            int aiNetworks = 0;
            foreach (var network in state.networks)
                if (network.ownerId != state.playerCountryId) aiNetworks++;
            Assert.Greater(aiNetworks, 0, "AI states never pursued their own intelligence objectives.");
        }

        [Test]
        public void PlayerFacesMeaningfulPressureNotJustGrowth()
        {
            // A decade should contain adversity, not a monotonic rise.
            var runs = RunAllPlaystyles(9393);
            bool anyAdversity = false;

            foreach (var run in runs)
            {
                foreach (var evaluation in run.state.evaluations)
                    if (evaluation.grade <= EvaluationGrade.C) anyAdversity = true;
                if (run.state.crisesFacedThisYear > 0) anyAdversity = true;
                // FLASH is now reserved for traffic that demands a decision this
                // month (GDD §28.2) rather than any loud report, which makes it a
                // sharper adversity signal than it was — but also a rarer one, so
                // it stays one disjunct of three rather than the whole test.
                foreach (var notification in run.state.notifications)
                    if (notification.priority == NotificationClass.Flash) anyAdversity = true;
            }

            Assert.IsTrue(anyAdversity,
                "Ten years produced no crisis, no setback and no urgent decision — the loop has no tension.");
        }

        [Test]
        public void ProgressionPacesAcrossADecade()
        {
            var run = Play("GOVERNMENT", 1234, GovernmentPlay);
            var state = run.state;

            Assert.Greater(state.strategistLevel, 1, "The operator never advanced a level in ten years.");
            Assert.Greater(state.unlockedSkills.Count, 2, "Too few skills unlocked to feel like progression.");
            Assert.Less(state.unlockedSkills.Count, SkillCatalog.Nodes.Count,
                "The entire skill catalog was exhausted in a decade — progression has no long tail.");
        }

        [Test]
        public void DecadeIsReproducibleForDebugging()
        {
            var a = Play("MILITARY", 2024, MilitaryPlay);
            var b = Play("MILITARY", 2024, MilitaryPlay);
            Assert.AreEqual(SaveSystem.ToJson(a.state), SaveSystem.ToJson(b.state),
                "Identical play on the same seed must reproduce exactly.");
        }

        [Test]
        public void SaveAndResumeMidDecadeIsSeamless()
        {
            var state = WorldFactory.CreateDebugWorld(4711);
            var turns = BuildSimulation(state);
            var result = new PlaythroughResult { state = state };

            for (int i = 0; i < 60; i++)
            {
                while (state.HasOpenCrisis)
                    CrisisSystem.Resolve(state, state.activeCrises[0], ChooseCrisisOption(state, state.activeCrises[0]));
                DiplomaticPlay(state, turns, result);
                turns.EndMonth();
            }

            // Suspend and resume, then continue the decade.
            var resumed = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var resumedTurns = BuildSimulation(resumed);
            var resumedResult = new PlaythroughResult { state = resumed };

            for (int i = 0; i < 60; i++)
            {
                while (state.HasOpenCrisis)
                    CrisisSystem.Resolve(state, state.activeCrises[0], ChooseCrisisOption(state, state.activeCrises[0]));
                DiplomaticPlay(state, turns, result);
                turns.EndMonth();

                while (resumed.HasOpenCrisis)
                    CrisisSystem.Resolve(resumed, resumed.activeCrises[0], ChooseCrisisOption(resumed, resumed.activeCrises[0]));
                DiplomaticPlay(resumed, resumedTurns, resumedResult);
                resumedTurns.EndMonth();
            }

            Assert.AreEqual(SaveSystem.ToJson(state), SaveSystem.ToJson(resumed),
                "A resumed save must continue identically to an uninterrupted session (GDD §30).");
        }
    }
}
