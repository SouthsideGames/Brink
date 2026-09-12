using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Strategist progression (GDD Phase 10, §25): continuous XP from meaningful
    /// decisions, an annual evaluation graded against circumstances rather than a
    /// conquest checklist, and skill points invested into five branching trees.
    /// </summary>
    public static class ProgressionSystem
    {
        /// <summary>XP required to reach each level beyond the first.</summary>
        public static int XPForLevel(int level) => level <= 1 ? 0 : 200 * (level - 1) * level / 2;

        // ---------- XP ----------

        /// <summary>
        /// Record a deliberate operator decision for the annual evaluation.
        ///
        /// Call this wherever a player action succeeds and commits a resource —
        /// Command Points, Political Capital or Influence. It is deliberately
        /// **explicit** rather than inferred from XP magnitude: measurement showed
        /// that inferring it made the metric track "which pillar happens to award
        /// large XP per action" instead of how engaged the operator actually was.
        /// </summary>
        public static void RecordInitiative(GameState state)
        {
            state.initiativesThisYear++;
        }

        /// <summary>
        /// Repetition multiplier for one kind of action this year (GDD §25.1:
        /// XP comes from *meaningful* decisions).
        ///
        /// Actions differ enormously in how often they can be repeated — running
        /// a trade adjustment is a monthly click, prosecuting a war is not — so
        /// paying flat XP per action measured how repeatable a pillar's verbs
        /// were rather than how well the operator played. Measurement bore this
        /// out: an economy-focused decade earned roughly five times the XP of a
        /// military one at comparable competence.
        ///
        /// The first few of anything pay in full; past that the same lever pulled
        /// again teaches progressively less, floored so it never reaches zero —
        /// a repeated action should be worth less, not worthless.
        /// </summary>
        static float RepetitionFactor(GameState state, string reason)
        {
            int index = state.xpReasons.IndexOf(reason);
            if (index < 0)
            {
                state.xpReasons.Add(reason);
                state.xpReasonCounts.Add(1);
                return 1f;
            }

            int count = ++state.xpReasonCounts[index];
            if (count <= FreeRepetitions) return 1f;

            // 1 / (1 + excess/4): the 8th is ~0.85, the 20th ~0.5, the 50th ~0.25.
            float excess = count - FreeRepetitions;
            return Math.Max(MinimumRepetitionValue, 1f / (1f + excess / 4f));
        }

        /// <summary>Repeats per year that still pay full XP.</summary>
        public const int FreeRepetitions = 4;

        /// <summary>Floor on the repetition multiplier — grinding still pays a little.</summary>
        public const float MinimumRepetitionValue = 0.2f;

        /// <summary>
        /// XP for a deliberate decision. Subject to per-kind diminishing returns
        /// within the evaluation year — see <see cref="RepetitionFactor"/>.
        /// </summary>
        public static void AwardXP(GameState state, int amount, string reason)
        {
            if (amount <= 0) return;
            AddXP(state, Math.Max(1, (int)Math.Round(amount * RepetitionFactor(state, reason))), reason);
        }

        /// <summary>
        /// Credit XP without the repetition discount. Only for the passive
        /// monthly baseline: nothing was repeated, so nothing should decay.
        /// </summary>
        static void AddXP(GameState state, int amount, string reason)
        {
            if (amount <= 0) return;
            state.strategistXP += amount;

            int newLevel = state.strategistLevel;
            while (state.strategistXP >= XPForLevel(newLevel + 1)) newLevel++;

            if (newLevel > state.strategistLevel)
            {
                state.strategistLevel = newLevel;
                state.AddNotification(NotificationClass.Advisory, "OPERATOR LEVEL " + newLevel,
                    "Your operational experience has deepened. Your record and your skill "
                    + "trees are in OPERATOR.", state.playerCountryId);
                GameLog.Info("PROG", $"Strategist level {newLevel} reached.");
            }

            GameLog.Debug("PROG", $"+{amount} XP ({reason}). Total {state.strategistXP}.");
        }

        /// <summary>Baseline XP for simply running the government each month.</summary>
        public static void MonthlyXP(GameState state)
        {
            AddXP(state, 2, "Monthly administration");
        }

        // ---------- skill trees ----------

        public static bool CanUnlock(GameState state, string nodeId, out string reason)
        {
            var node = SkillCatalog.Find(nodeId);
            if (node == null) { reason = "No such skill."; return false; }
            if (state.HasSkill(nodeId)) { reason = "Already unlocked."; return false; }
            if (state.skillPoints < node.cost)
            {
                reason = $"Requires {node.cost} skill point(s); you have {state.skillPoints}.";
                return false;
            }
            foreach (var prerequisite in node.prerequisites)
            {
                if (state.HasSkill(prerequisite)) continue;
                reason = $"Requires {SkillCatalog.Find(prerequisite)?.name ?? prerequisite}.";
                return false;
            }
            reason = "";
            return true;
        }

        public static bool Unlock(GameState state, string nodeId)
        {
            if (!CanUnlock(state, nodeId, out string reason))
            {
                GameLog.Warn("PROG", $"Cannot unlock {nodeId}: {reason}");
                return false;
            }

            var node = SkillCatalog.Find(nodeId);
            state.skillPoints -= node.cost;
            state.unlockedSkills.Add(nodeId);

            state.AddNotification(NotificationClass.Advisory, "SKILL UNLOCKED: " + node.name.ToUpperInvariant(),
                node.description, state.playerCountryId);
            state.AddChronicle(ChronicleCategory.System, state.playerCountryId,
                $"Strategist capability unlocked: {node.name}.");
            GameLog.Info("PROG", $"Unlocked {node.name} for {node.cost} SP.");
            return true;
        }

        /// <summary>Summed magnitude of every unlocked node with this effect.</summary>
        public static float EffectValue(GameState state, SkillEffect effect)
        {
            float total = 0f;
            for (int i = 0; i < state.unlockedSkills.Count; i++)
            {
                var node = SkillCatalog.Find(state.unlockedSkills[i]);
                if (node != null && node.effect == effect) total += node.magnitude;
            }
            return total;
        }

        /// <summary>Reduce a CP cost by an efficiency effect, never below 1 (or 0 for free actions).</summary>
        public static int DiscountedCost(GameState state, int baseCost, SkillEffect effect, int minimum = 1)
        {
            int discount = (int)EffectValue(state, effect);
            return Math.Max(minimum, baseCost - discount);
        }

        // ---------- annual evaluation ----------

        /// <summary>
        /// Capture the baseline the coming year will be graded against.
        /// <paramref name="yearBeginning"/> defaults to the current year; the
        /// annual evaluation passes the year that is about to start, because it
        /// runs before the calendar advances.
        /// </summary>
        public static void CaptureYearSnapshot(GameState state, int yearBeginning = 0)
        {
            var player = state.PlayerCountry;
            if (player == null) return;

            state.yearSnapshot = new YearSnapshot
            {
                year = yearBeginning > 0 ? yearBeginning : state.date.year,
                pillarTotal = PillarTotal(player),
                gdp = player.economy.gdp,
                treasury = player.resources.treasury,
                approval = player.governmentApproval,
                stability = player.stability,
                unity = player.nationalUnity,
                marketIndex = player.economy.marketIndex,
                locationsHeld = LocationsHeld(state),
                relationsTotal = RelationsTotal(state),
                treatiesHeld = TreatiesHeld(state, out int pacts),
                defensePactsHeld = pacts,
                warsWon = player.warsWon,
                warsLost = player.warsLost
            };
            state.crisesFacedThisYear = 0;
            state.crisesResolvedThisYear = 0;
            state.initiativesThisYear = 0;
            state.directivesCompletedThisYear = 0;

            // Repetition is measured per year: a lever worn out last year is
            // worth learning from again after a year of doing something else.
            state.xpReasons.Clear();
            state.xpReasonCounts.Clear();
        }

        /// <summary>
        /// Grade the year (GDD §25.2). Considers national trajectory, economic
        /// management, domestic stability, strategic position and crisis handling
        /// — each judged relative to the conditions the year was actually run
        /// under, so holding a country together through a war can score well.
        /// </summary>
        public static EvaluationRecord EvaluateYear(GameState state, int year)
        {
            var player = state.PlayerCountry;
            var snapshot = state.yearSnapshot;

            bool wasAtWar = state.IsAtWar(player.id);
            float adversity = (wasAtWar ? 1f : 0f)
                              + EconomySystem.SanctionPressureOn(state, player.id) * 0.25f
                              + (player.warExhaustion > 40f ? 0.5f : 0f);

            // --- trajectory: did national capability advance? ---
            // Weighted so that a competently delegated year lands mid-scale:
            // autonomous officials alone should not produce a top grade.
            float pillarDelta = PillarTotal(player) - snapshot.pillarTotal;
            float trajectory = 50f + pillarDelta * 1.6f;

            // --- economy: growth and market confidence, penalized for inflation ---
            float gdpGrowth = snapshot.gdp > 0f ? (player.economy.gdp - snapshot.gdp) / snapshot.gdp * 100f : 0f;
            float marketMove = snapshot.marketIndex > 0f
                ? (player.economy.marketIndex - snapshot.marketIndex) / snapshot.marketIndex * 100f
                : 0f;
            float economy = 50f + gdpGrowth * 3f + marketMove * 0.6f
                            - Math.Max(0f, player.economy.inflation - 5f) * 2.5f
                            - SolvencyPenalty(state, player);

            // --- stability: the state held together ---
            float stability = 50f
                              + (player.stability - snapshot.stability) * 1.5f
                              + (player.governmentApproval - snapshot.approval) * 0.8f
                              + (player.nationalUnity - snapshot.unity) * 0.8f;

            // --- strategic position: territory, relationships and commitments ---
            // Standing agreements are strategic position too; an operator who
            // spends a year building an alliance has moved the country forward
            // even if no border changed.
            int treaties = TreatiesHeld(state, out int defensePacts);
            float position = 50f
                             // Conquest counts (2026-08-28, a design decision that
                             // supersedes GDD §25's "never a conquest checklist"):
                             // ground taken this year at 18, and ground held beyond
                             // the posting's opening holdings at 4 a year, every
                             // year — a position, not a one-off bonus.
                             + (LocationsHeld(state) - snapshot.locationsHeld) * 30f
                             + Math.Max(0, LocationsHeld(state) - OpeningHoldings(state)) * 8f
                             + (RelationsTotal(state) - snapshot.relationsTotal) * 0.35f
                             + (treaties - snapshot.treatiesHeld) * 9f
                             + (defensePacts - snapshot.defensePactsHeld) * 7f
                             + (Deterrent(player) - 0.5f) * 24f
                             // A war won this year is a fact about the nation's
                             // position; a war lost is too. Ground taken is
                             // already counted above, so this is the verdict
                             // itself — a deterrence or policy war won by
                             // settlement used to be worth exactly nothing here.
                             + (player.warsWon - snapshot.warsWon) * 14f
                             - (player.warsLost - snapshot.warsLost) * 10f;

            // --- crisis management ---
            // A year with no crises is a year of successful prevention, and must
            // score close to a year of competent response — not below it. Scoring
            // quiet years at the midpoint punished exactly the governments that
            // kept trouble from starting.
            float crisis = state.crisesFacedThisYear == 0
                ? 68f
                : 30f + (float)state.crisesResolvedThisYear / state.crisesFacedThisYear * 45f;

            // --- initiative: what the operator personally accomplished ---
            // Delegation remains legitimate — an idle year still passes — but the
            // top grades are reserved for operators who actually did something.
            //
            // This carries real weight deliberately. Acting costs treasury,
            // stability and capacity, all of which the other components penalize;
            // without a meaningful counterweight, measurement showed most active
            // playstyles grading *below* doing nothing.
            // Saturates around 17 decisions a year — roughly one a month plus
            // change. Beyond that, more clicking is not more statecraft.
            float initiative = 40f + Math.Min(38f, state.initiativesThisYear * 2.2f)
                               + Math.Min(18f, state.directivesCompletedThisYear * 6f);   // GDD §29, spec 24

            // --- efficiency: what the year's spending actually bought (GDD §25.2) ---
            //
            // The evaluation graded *what* was achieved and *how much* was done,
            // and never what either cost — so a year that burned the reserves to
            // stand still scored exactly like a lean one. This is the only
            // component that can be improved by doing less.
            //
            // A **ratio**, not a subtraction, and neutral when nothing was spent.
            //
            // The first version subtracted spending from delivery, which handed
            // a perfect efficiency score to an operator who did nothing at all.
            // Measurement was blunt about it: passive play rose from 2.50 to
            // 2.78 and the margin for engaged play collapsed — military from
            // +0.16 to +0.02. Inaction became the efficient strategy, which is
            // the exact opposite of the point.
            //
            // Now a quiet year scores exactly 50 — neither rewarded nor
            // punished, because there is nothing to judge — and a year that
            // spent is graded on what the spending bought. Treasury is already
            // in the year snapshot, so this needs no new tracking.
            float treasurySpent = Math.Max(0f, snapshot.treasury - player.resources.treasury);
            float delivered = pillarDelta * 2.5f + gdpGrowth * 1.5f;

            float efficiency = 50f;
            if (treasurySpent > 100f)
                efficiency = 50f + delivered / (treasurySpent / 1000f) * 6f;
            if (efficiency < 0f) efficiency = 0f;
            if (efficiency > 100f) efficiency = 100f;

            // Weights sum to 1.00. Efficiency is deliberately the smallest: it is
            // a check on the others, not a thing to optimise. Spending nothing
            // must never be a route to a good grade on its own.
            float score = trajectory * 0.18f + economy * 0.20f + stability * 0.14f
                          + position * 0.10f + crisis * 0.11f + initiative * 0.19f
                          + efficiency * 0.08f;

            // Circumstance and difficulty context: a hard year is graded gently,
            // and a sharper AI is a harder world to perform in (GDD §25.2).
            score += adversity * 6f;

            // Ground held beyond the opening holdings also pays directly (user
            // decision, 2026-08-28): position is a tenth of the score, so a
            // decade of conquest moved the grade by a tenth of a letter through
            // that channel alone. Capped, so a map painter still has to govern.
            score += Math.Min(12f, Math.Max(0, LocationsHeld(state) - OpeningHoldings(state)) * 1.5f);
            if (state.difficulty == Difficulty.Challenging) score += 3f;
            if (state.difficulty == Difficulty.Ruthless) score += 6f;

            var grade = GradeFor(score);
            int points = SkillPointsFor(grade);

            var record = new EvaluationRecord
            {
                year = year,
                grade = grade,
                score = score,
                skillPointsAwarded = points,
                trajectoryScore = trajectory,
                economyScore = economy,
                stabilityScore = stability,
                positionScore = position,
                crisisScore = crisis,
                initiativeScore = initiative,
                efficiencyScore = efficiency,
                summary = BuildSummary(grade, wasAtWar, pillarDelta, gdpGrowth, state)
            };

            // Say *why* (2026-08): the components were computed and shown as
            // numbers, and the summary said "Economy flat" and nothing else.
            // Name the strongest and weakest, in the operator's language.
            var named = new (string name, float score)[]
            {
                ("national trajectory", trajectory), ("the economy", economy), ("stability at home", stability),
                ("the country's position abroad", position), ("crisis handling", crisis),
                ("initiative shown", initiative), ("value for money spent", efficiency)
            };
            var best = named[0]; var worst = named[0];
            foreach (var component in named)
            {
                if (component.score > best.score) best = component;
                if (component.score < worst.score) worst = component;
            }
            record.summary += $" Carried by {best.name} ({best.score:F0}); held back by {worst.name} ({worst.score:F0}).";
            var fiscalCondition = FiscalSystem.ConditionOf(state, player);
            if (fiscalCondition >= FiscalCondition.DeficitFinanced)
                record.summary += $" The public finances read {FiscalSystem.ConditionText(fiscalCondition)}, and it shows.";

            state.evaluations.Add(record);
            CareerRecord.Record(state);   // spec 24 §2 — refreshed yearly, so an abandoned posting still shows what it was
            state.skillPoints += points;

            // Kept modest relative to decision XP, so a decade of engagement
            // outweighs a decade of simply letting the year pass.
            AwardXP(state, 25 + points * 15, "Annual evaluation");

            state.AddNotification(NotificationClass.Priority, $"ANNUAL EVALUATION {year} — GRADE {grade}",
                $"{record.summary} {points} skill point(s) awarded.", player.id);
            state.AddChronicle(ChronicleCategory.System, player.id,
                $"Annual evaluation {year}: grade {grade}.");
            FileYearInReview(state, year);
            GameLog.Info("PROG", $"Annual evaluation {year}: {grade} ({score:F1}), +{points} SP.");

            if (!state.tenureReviewed && state.date.MonthsSince(state.startDate) >= TenureMonths)
                DeliverTenureReview(state);

            CaptureYearSnapshot(state, year + 1);
            return record;
        }

        /// <summary>Months of service before the career review. Forty years.</summary>
        public const int TenureMonths = 480;

        /// <summary>
        /// What running the treasury into the red costs the economy component:
        /// up to 20 points, scaling with the deficit measured in years of income.
        ///
        /// The evaluation never looked at the balance. That is how a monthly
        /// cost 70× income (spec 19 §5) passed every balance measurement this
        /// project had taken: every posting was −30,000 to −170,000 by year ten
        /// and graded B. A government that has spent money it does not have is
        /// not running its economy well, whatever GDP did.
        /// </summary>
        public static float SolvencyPenalty(CountryState country) => SolvencyPenalty(null, country);

        /// <summary>
        /// Read from <see cref="FiscalSystem.ConditionOf"/>, never from the
        /// balance alone. Deficit financing zeroes the account every month, so a
        /// posting 30,000 in debt read as solvent to this penalty — the exact
        /// failure it was written to catch, restored by a different route. A
        /// government living on credit pays a little; one whose debt is heavy or
        /// whose creditors are wary pays more; one in arrears or fresh from a
        /// default pays the full twenty.
        /// </summary>
        public static float SolvencyPenalty(GameState state, CountryState country)
        {
            if (country == null) return 0f;
            float annualIncome = FiscalSystem.AnnualIncome(country);
            float yearsInTheRed = Math.Max(0f, -country.resources.treasury) / annualIncome;
            float arrears = Math.Min(20f, yearsInTheRed * 8f);

            switch (FiscalSystem.ConditionOf(state, country))
            {
                case FiscalCondition.CashNegativeButCreditworthy: return Math.Max(arrears, 2f);
                case FiscalCondition.DeficitFinanced:
                    return Math.Max(arrears, 6f + Math.Min(6f, country.fiscal.deficitFinancedMonths / 6f));
                case FiscalCondition.DebtStressed:
                    return Math.Max(arrears, 12f + Math.Min(6f, Math.Max(0f, FiscalSystem.DebtToGdp(country) - 120f) / 20f));
                case FiscalCondition.Crisis: return 20f;
                default: return arrears;
            }
        }

        /// <summary>
        /// The career arc (GDD §9 amendment, user decision). Reported from play:
        /// an operator who reaches the top of the world runs out of reasons to
        /// keep ending months — the game is open-ended by design, but open-ended
        /// and *shapeless* are different things. At forty years of service the
        /// record is formally closed and judged: every annual grade, every war
        /// verdict, every treaty and instrument, rolled into one career
        /// classification.
        ///
        /// **The world does not stop.** Nothing is disabled, no screen is forced,
        /// and the month after the review is a month like any other — the review
        /// is an arc, not an ending, per the same reasoning that keeps saves
        /// alive through coups and secessions. An operator who wants a fresh
        /// posting has FULL RESET; one who wants to see year sixty simply keeps
        /// playing, with the review standing in the record.
        /// </summary>
        public static void DeliverTenureReview(GameState state)
        {
            var player = state.PlayerCountry;
            if (player == null) return;
            state.tenureReviewed = true;
            CareerRecord.Record(state);   // spec 24 §2

            float gradeSum = 0f;
            foreach (var evaluation in state.evaluations) gradeSum += (int)evaluation.grade;
            float career = state.evaluations.Count > 0 ? gradeSum / state.evaluations.Count : 0f;

            // The same scale the annual grades use, applied to their average, so
            // the career classification cannot disagree with the record it sums.
            string classification =
                career >= 4.5f ? "EXCEPTIONAL — a tenure that redrew the world" :
                career >= 3.5f ? "DISTINGUISHED — consistently ahead of events" :
                career >= 2.5f ? "CREDITABLE — the state is stronger for these years" :
                career >= 1.5f ? "MIXED — years of drift among years of judgement" :
                                 "CENSURED — the record speaks against this office";

            int treaties = 0;
            foreach (var treaty in state.treaties)
                if (!treaty.broken && (treaty.countryA == player.id || treaty.countryB == player.id))
                    treaties++;

            string body =
                $"Forty years at this terminal. The record is closed and reads as follows.\n" +
                $"CAREER CLASSIFICATION: {classification}.\n" +
                $"ANNUAL EVALUATIONS: {state.evaluations.Count}, averaging {career:F1} on the grade scale.\n" +
                $"WARS: {player.warsWon} won, {player.warsLost} lost, {player.warsDrawn} drawn.\n" +
                $"TREATIES IN FORCE: {treaties}. ADMINISTRATIONS SERVED: {state.administrationsServed}.\n" +
                $"OPERATOR LEVEL {state.strategistLevel}, {state.strategistXP} XP.\n" +
                "The post remains yours. History does not stop being made because " +
                "it has been judged — and a new posting is always available through FULL RESET.";

            state.AddNotification(NotificationClass.Priority, "TENURE REVIEW — FORTY YEARS OF SERVICE",
                body, player.id, desk: ReportingDesk.Command);
            state.AddChronicle(ChronicleCategory.System, player.id,
                $"Tenure review at forty years: {classification}.");
            GameLog.Info("PROG", $"Tenure review delivered: {classification} ({career:F2}).");
        }

        /// <summary>
        /// File the year's record as ARCHIVE traffic (GDD §9.2).
        ///
        /// ARCHIVE is the bottom of the five-class notification hierarchy —
        /// reference material, filed rather than reported, nothing to act on.
        /// It existed in the enum, in the styling and in the briefing's filter
        /// with no producer anywhere in the simulation, so the class was a
        /// permanently empty drawer. A year-in-review is exactly what belongs in
        /// it: the operator does not need to read it this month, but should be
        /// able to find it later.
        /// </summary>
        static void FileYearInReview(GameState state, int year)
        {
            int military = 0, political = 0, diplomatic = 0, economic = 0, intelligence = 0;
            foreach (var entry in state.chronicle)
            {
                if (entry.date.year != year) continue;
                switch (entry.category)
                {
                    case ChronicleCategory.Military: military++; break;
                    case ChronicleCategory.Political: political++; break;
                    case ChronicleCategory.Diplomatic: diplomatic++; break;
                    case ChronicleCategory.Economic: economic++; break;
                    case ChronicleCategory.Intelligence: intelligence++; break;
                }
            }

            string body =
                $"WORLD RECORD {year}. Military {military}. Political {political}. " +
                $"Diplomatic {diplomatic}. Economic {economic}. Intelligence {intelligence}. " +
                $"Crises faced {state.crisesFacedThisYear}, resolved {state.crisesResolvedThisYear}. " +
                $"Operator decisions logged {state.initiativesThisYear}. " +
                "Full record available in CHRONICLE.";

            state.AddNotification(NotificationClass.Archive, $"YEAR IN REVIEW {year}",
                body, state.playerCountryId);
        }

        /// <summary>
        /// Grade bands.
        ///
        /// **These are calibrated. Do not move them without measuring first.**
        /// Across five seeds and ten years they put a passive, fully delegated
        /// decade at **2.46** (a high C) and the strongest active playstyle at
        /// **3.14** (a solid B). That is the shape the design wants: doing
        /// nothing is a passing grade because delegation is legitimate, and
        /// engaged play is visibly better without being a different league.
        ///
        /// They were once recentred upward on the theory that a competent decade
        /// "reading as a C" was the scale calling good statecraft mediocre. It
        /// was not. Measurement showed the C belonged to *passive* play, and
        /// lifting the bands moved doing nothing to 3.02 — a B for an operator
        /// who never touched anything — while compressing the reward for
        /// engagement from +0.36 to +0.16 over passive. It was reverted.
        ///
        /// If these ever do need to move, the thing to check is the passive
        /// baseline, not the top of the range: an uneventful year is the anchor
        /// the whole scale hangs from.
        ///
        /// **Grades are not cosmetic.** <see cref="SkillPointsFor"/> pays out by
        /// band, so moving a threshold changes progression pace too — two jobs,
        /// feedback and reward, welded to one number. Retune the payout curve
        /// alongside any band change, never one without the other.
        /// </summary>
        public static EvaluationGrade GradeFor(float score)
        {
            // S and A moved up (2026-08): a first year of answering two crises
            // and signing what was offered graded S. The top bands are for years
            // that were actually exceptional; B is still where a well-delegated
            // year lands (spec 12 §3).
            if (score >= 88f) return EvaluationGrade.S;
            if (score >= 76f) return EvaluationGrade.A;
            if (score >= 60f) return EvaluationGrade.B;
            if (score >= 48f) return EvaluationGrade.C;
            if (score >= 38f) return EvaluationGrade.D;
            return EvaluationGrade.F;
        }

        public static int SkillPointsFor(EvaluationGrade grade)
        {
            switch (grade)
            {
                case EvaluationGrade.S: return 5;
                case EvaluationGrade.A: return 4;
                case EvaluationGrade.B: return 3;
                case EvaluationGrade.C: return 2;
                case EvaluationGrade.D: return 1;
                default: return 0;
            }
        }

        static string BuildSummary(EvaluationGrade grade, bool wasAtWar, float pillarDelta, float gdpGrowth, GameState state)
        {
            string context = wasAtWar ? "Year conducted under active conflict."
                : state.crisesFacedThisYear > 0 ? "Year marked by crisis management."
                : "Year conducted under normal conditions.";

            string capability = pillarDelta > 3f ? "National capability advanced."
                : pillarDelta < -3f ? "National capability eroded."
                : "National capability held steady.";

            string economy = gdpGrowth > 3f ? "Economy expanded."
                : gdpGrowth < 0f ? "Economy contracted."
                : "Economy flat.";

            return $"{context} {capability} {economy}";
        }

        static float PillarTotal(CountryState country)
            => country.pillars.military + country.pillars.economy + country.pillars.intelligence
               + country.pillars.diplomacy + country.pillars.government;

        /// <summary>
        /// 0..1 read of the deterrent the country actually fields — force power
        /// and the sustainment behind it. Deterrence is the peacetime product of
        /// military power (GDD §19), so it belongs in strategic position: an
        /// operator who spent a year making the country genuinely hard to move
        /// on has advanced it, and one who let the force hollow out has not.
        /// Reads only our own forces, never a foreign true value.
        /// </summary>
        static float Deterrent(CountryState country)
        {
            var mil = country.military;
            // EffectivePower is already normalized to 0..1; scale it back to the
            // 0..100 range logistics uses before mixing the two.
            float forces = mil.TotalPower / 3f * 100f;
            return Math.Min(1f, (forces * 0.6f + mil.logistics * 0.4f) / 100f);
        }

        /// <summary>Locations the posting opened with (the mandate's base), or today's count on a save without one.</summary>
        static int OpeningHoldings(GameState state)
            => state.mandate != null && state.mandate.startLocationIds.Count > 0
                ? state.mandate.startLocationIds.Count
                : LocationsHeld(state);

        static int LocationsHeld(GameState state)
        {
            int held = 0;
            foreach (var location in state.locations)
                if (location.ownerId == state.playerCountryId) held++;
            return held;
        }

        /// <summary>Unbroken treaties the player holds, and how many are defense pacts.</summary>
        static int TreatiesHeld(GameState state, out int defensePacts)
        {
            int held = 0;
            defensePacts = 0;
            foreach (var treaty in state.treaties)
            {
                if (treaty.broken || !treaty.Involves(state.playerCountryId)) continue;
                held++;
                if (treaty.Has(TreatyCommitment.MutualDefense)) defensePacts++;
            }
            return held;
        }

        static float RelationsTotal(GameState state)
        {
            float total = 0f;
            foreach (var relationship in state.relationships)
                if (relationship.Involves(state.playerCountryId)) total += relationship.relations;
            return total;
        }
    }
}
