using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Government, political capital and succession (GDD Phase 8, §13, §7.3).
    ///
    /// Government type changes how power works rather than applying modifiers:
    /// elective systems face the voters and need legislative backing, while
    /// non-elective systems answer to elite cohesion and broker succession
    /// internally. The player is the persistent strategic operator — leaders
    /// and administrations come and go while the save continues.
    /// </summary>
    public static class GovernmentSystem
    {
        public const float PublicMessagingCost = 2f;
        public const float DismissOfficialCost = 4f;
        public const float InstitutionalReformCost = 6f;
        public const float EmergencyPowersCost = 5f;
        public const float EarlyElectionCost = 3f;

        public const int EmergencyPowersDuration = 6;

        public const float NationalPriorityCost = 3f;
        public const float CivicPostureCost = 3f;
        public const float BuildSupportCost = 2f;
        public const float PatronageCost = 1f;
        public const float PatronageTreasury = 200f;
        public const float InquiryCost = 4f;
        public const float GroomSuccessorCost = 3f;

        /// <summary>
        /// The one large sink in the Political Capital economy.
        ///
        /// Income runs 1.5–3 a month against a cap of 20 and nothing cost more
        /// than 7, so an operator who was not spending simply sat at the cap with
        /// nothing worth buying — which is most of why the Government pillar was
        /// the least active in the game. A purchase at roughly five months of
        /// income gives the currency somewhere to go and banking it a purpose.
        /// </summary>
        public const float ConsolidateAuthorityCost = 12f;

        /// <summary>How much bought support survives into next month.</summary>
        public const float BrokeredSupportDecay = 0.94f;

        /// <summary>
        /// How much of a messaging campaign survives into next month. Deliberately
        /// quicker than <see cref="BrokeredSupportDecay"/> — about a nine-month
        /// half-life against fourteen — because a favour owed outlasts a speech.
        /// </summary>
        public const float MessagingDecay = 0.92f;

        /// <summary>
        /// Where the messaging reservoir saturates. Below `brokeredSupport`'s 60:
        /// there is a limit to what a government can talk its way into, and past
        /// it further campaigning is a government that has run out of other ideas.
        /// At the ceiling this is worth ~+19 approval on the target, which is real
        /// without being the whole pillar.
        /// </summary>
        public const float MessagingCeiling = 45f;

        /// <summary>Monthly political bill for holding a restrictive civic posture.</summary>
        public const float RestrictiveUpkeep = 0.45f;

        // ---------- monthly resolution ----------

        public static void MonthlyUpdate(GameState state)
        {
            int monthIndex = state.date.MonthsSince(state.startDate);

            foreach (var country in state.countries)
            {
                var gov = country.government;
                gov.leader.monthsInOffice++;
                gov.leader.age += 1f / 12f;

                var rng = new Random(unchecked(state.rngSeed * 3010349 + monthIndex * 271 + Hash.Of(country.id)));

                UpdatePoliticalCondition(state, country, gov);
                UpdateEmergencyPowers(state, country, gov);

                if (gov.IsElective)
                {
                    if (state.date.CompareTo(gov.nextElectionDate) >= 0)
                        HoldElection(state, country, gov, rng);
                    else
                        CheckConfidence(state, country, gov, rng);
                }
                else
                {
                    CheckSuccession(state, country, gov, rng);
                }
            }

            AccruePoliticalCapital(state);
        }

        /// <summary>Approval, legislative support and elite cohesion respond to conditions.</summary>
        // ---------- civic posture (GDD §12, §13) ----------

        /// <summary>
        /// What an open or restrictive civic posture does to approval.
        ///
        /// Government type is the interesting part. A system that faces the
        /// voters pays far more for governing restrictively than one that does
        /// not — which is §13's requirement that the type change *how power
        /// works* rather than hand out a modifier, applied to the one standing
        /// choice the pillar has.
        /// </summary>
        public static float ApprovalShiftFor(GovernmentState gov)
        {
            switch (gov.civicPosture)
            {
                case CivicPosture.Open:
                    return 4f;
                case CivicPosture.Restrictive:
                    return gov.IsElective ? -9f : -4f;
                default:
                    return 0f;
            }
        }

        // ---------- faction arithmetic (GDD §13, spec 05 §2b) ----------

        /// <summary>
        /// Months a new governing faction needs before the chamber is genuinely
        /// theirs, and a provisional authority needs before it is a government
        /// rather than a committee.
        /// </summary>
        public const int FactionConsolidationMonths = 30;

        /// <summary>
        /// What the leader's faction does to the legislative-support target
        /// (elective systems).
        ///
        /// `Leader.faction` was a display string no rule read — a government of
        /// continuity and one that had just thrown the old party out faced
        /// identical chambers. A leader who arrived by turnover ("OPPOSITION",
        /// "REFORM BLOC") governs against a chamber still partly held by the
        /// people they defeated; the penalty decays with months in office, so it
        /// is a term in a target rather than a ratchet, and `brokeredSupport`
        /// remains the way to buy past it — which finally gives that verb its
        /// most natural customer.
        /// </summary>
        public static float FactionSupportShift(GovernmentState gov)
        {
            if (!gov.IsElective) return 0f;
            if (gov.leader.faction != "OPPOSITION" && gov.leader.faction != "REFORM BLOC") return 0f;

            float remaining = Math.Max(0f,
                1f - (float)gov.leader.monthsInOffice / FactionConsolidationMonths);
            return -12f * remaining;
        }

        /// <summary>
        /// What the leader's faction does to the elite-cohesion target
        /// (non-elective systems).
        ///
        /// A military council stands on the officer corps — its cohesion tracks
        /// `militaryLoyalty`, so undermining the army *is* undermining the junta,
        /// which gives a coup-born government the specific vulnerability GDD §22
        /// promises. A provisional authority is a state still deciding whether it
        /// is one; the discount decays as it consolidates.
        /// </summary>
        public static float FactionCohesionShift(GovernmentState gov)
        {
            if (gov.IsElective) return 0f;

            if (gov.leader.faction == "MILITARY COUNCIL")
                return (gov.militaryLoyalty - 65f) * 0.25f;

            if (gov.leader.faction == "PROVISIONAL AUTHORITY")
            {
                float remaining = Math.Max(0f,
                    1f - (float)gov.leader.monthsInOffice / (FactionConsolidationMonths + 6));
                return -10f * remaining;
            }

            return 0f;
        }

        /// <summary>Order is what a restrictive posture buys, and what an open one spends.</summary>
        public static float StabilityShiftFor(GovernmentState gov)
        {
            switch (gov.civicPosture)
            {
                case CivicPosture.Open: return -4f;
                case CivicPosture.Restrictive: return 9f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Unity runs the other way from order. A society held down coheres
        /// less, not more — the appearance of unanimity is not the thing.
        /// </summary>
        public static float UnityShiftFor(GovernmentState gov)
        {
            switch (gov.civicPosture)
            {
                case CivicPosture.Open:
                    return gov.IsElective ? 9f : 4f;
                case CivicPosture.Restrictive:
                    return -10f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Multiplier on how fast plots organise (read by <see cref="RegimeSystem"/>).
        /// The whole case for governing restrictively, and the whole risk of not.
        /// </summary>
        public static float ConspiracyRateFor(GovernmentState gov)
        {
            switch (gov.civicPosture)
            {
                case CivicPosture.Open: return 1.35f;
                case CivicPosture.Restrictive: return 0.6f;
                default: return 1f;
            }
        }

        /// <summary>Plain-language name for the order screen and the readouts.</summary>
        public static string PostureText(CivicPosture posture)
        {
            switch (posture)
            {
                case CivicPosture.Open: return "OPEN";
                case CivicPosture.Restrictive: return "RESTRICTIVE";
                default: return "STANDARD";
            }
        }

        /// <summary>
        /// The social layer beneath the politics (GDD §12).
        ///
        /// Three quantities on three different timescales, which is the whole
        /// point of separating them:
        ///
        /// - **Living standards** move over *years*. They are how a decade of
        ///   prosperity becomes political capital and a decade of decline becomes
        ///   a government nobody can save. Before this, approval read the current
        ///   quarter's growth figure directly, so history had no weight at all.
        /// - **Social unrest** moves over *months*. It is not the same thing as
        ///   low approval: approval is an opinion, unrest is people in the street.
        ///   A state can be disliked and calm, or quietly approved of and coming
        ///   apart in three cities.
        /// - **Public grievance** moves over *decades* and never quite clears. It
        ///   is what makes the second war harder to sell than the first.
        ///
        /// All three drift toward a target rather than accumulating a rate. Two
        /// of the three are things a government can make worse far faster than it
        /// can make them better, which is modelled by asymmetric rates rather
        /// than by letting anything ratchet.
        /// </summary>
        static void UpdateSocialCondition(GameState state, CountryState country, GovernmentState gov)
        {
            var eco = country.economy;

            // ---- living standards: the slow accumulation ----
            // Anchored on the market index because it is already fundamentals-
            // anchored and reads 100 at world creation, so "how much better off
            // is this country than when the save began" needs no new field.
            // The market-index term is deliberately steeper below the line than
            // above it. At 0.15/point a *total* economic collapse — index 7.6 —
            // subtracted only 14, so the worst living standards this model could
            // describe were about 32 out of 100: an unpleasant decade, not a
            // catastrophe. Prosperity accumulating slowly is right; deprivation
            // being capped is not.
            float indexGap = eco.marketIndex - 100f;
            float indexTerm = indexGap >= 0f ? indexGap * 0.15f : indexGap * 0.42f;

            float standardsTarget = Clamp(
                52f
                + indexTerm
                + eco.growthRate * 1.6f
                - Math.Max(0f, eco.inflation - 4f) * 1.5f
                - Math.Max(0f, eco.unemployment - 6f) * 1.2f
                - country.warExhaustion * 0.20f
                - country.publicGrievance * 0.12f
                // Hunger — measured against the country's *own normal*, not an
                // absolute line. An absolute threshold (the first version used
                // 50) reads an authored dependency as a standing humanitarian
                // crisis: Saudi Arabia is authored at food 18 and would carry
                // permanent phantom deprivation from month one, which rippled
                // through fifteen AI states and moved measured balance. A state
                // authored food-poor has adapted; what starves people is food
                // *falling below its own endowment* — which war, siege and
                // sanctions now genuinely cause. Zero by construction at every
                // authored baseline — the `distress` idiom, kept honest.
                - Math.Max(0f, country.resources.foodEndowment
                               - country.resources.foodSecurity - 5f) * 0.5f
                // Carrying a crisis for somebody else is a strain on services.
                // Mild per point, and through the target like everything else
                // here — a host that is otherwise well run absorbs it.
                - DisplacementSystem.StandardsDrag(country));

            // Deliberately slower to rise than to fall. Prosperity is felt as it
            // accumulates; a collapse is felt immediately.
            float standardsRate = standardsTarget > country.livingStandards ? 0.020f : 0.045f;
            country.livingStandards = Approach(country.livingStandards, standardsTarget, standardsRate);

            // ---- social unrest: organised anger ----
            //
            // Hardship the government has not addressed. Note what is *not* here:
            // simply being unpopular. A government can be disliked without
            // anybody organising, and that distinction is what stops this from
            // being a second approval score.
            float unrestPressure =
                Math.Max(0f, 45f - country.livingStandards) * 0.55f
                + Math.Max(0f, eco.inflation - 8f) * 1.6f
                + Math.Max(0f, eco.unemployment - 10f) * 1.4f
                + country.warExhaustion * 0.22f
                + country.publicGrievance * 0.18f
                // Hunger organises faster than the living-standards average it
                // is part of — standards move over years, an empty shelf moves
                // people this month. Gap against the country's own endowment
                // (see the standards term above for why not an absolute line),
                // with a deeper grace: organisation needs a real drop, not a
                // lean month.
                + Math.Max(0f, country.resources.foodEndowment
                               - country.resources.foodSecurity - 10f) * 0.45f;

            // National unity *damps* hardship; it does not cancel it.
            //
            // This was `- nationalUnity * 0.25f` on the pressure sum, which gave
            // every country an absolute immunity budget — at an ordinary unity of
            // 60 the first 15 points of hardship registered as nothing at all, and
            // four years of severe deprivation produced literally zero organised
            // anger. A subtraction is the wrong shape for a resilience term: a
            // cohesive society under real hardship still ends up in the street, it
            // simply takes more to put it there. 0 -> x1.18, 50 -> x0.88,
            // 100 -> x0.58.
            //
            // The range is deliberately narrower than the first version's
            // (1.25 → 0.45). At an ordinary unity of 64 that took 26% off, which
            // in a *total* economic collapse is not resilience but near-immunity —
            // it held the ceiling on unrest around 45 and so made GENERAL_STRIKE,
            // gated at 58, unreachable for any country in any playthrough.
            // In normal times the pressure sum is near zero, so widening the
            // multiplier here changes nothing outside a genuine crisis.
            unrestPressure *= 1.18f - country.nationalUnity / 165f;

            // An organised campaign against the government is people already
            // meeting about it. Added to the pressure rather than to the value,
            // so it raises where unrest settles instead of being erased by the
            // next month's drift.
            unrestPressure += OppositionSystem.UnrestPressure(gov);

            // People shooting at the government somewhere in the country is not a
            // mood, and it does not stay local.
            unrestPressure += InsurgencySystem.UnrestPressure(state, country.id);

            // Arrivals are an argument in the host, and people who wanted out and
            // could not get out are an argument at home. Different countries,
            // different terms.
            unrestPressure += DisplacementSystem.UnrestPressure(country);
            unrestPressure += DisplacementSystem.PressureAtSource(state, country);

            // Some states argue about everything. Hardship organises faster there.
            float unrestTarget = Clamp(unrestPressure * NationalTraitCatalog.UnrestVolatility(country));

            // A restrictive posture suppresses the *expression* without touching
            // the cause — the grievance keeps accruing underneath, which is the
            // trade the posture is meant to represent.
            if (gov.civicPosture == CivicPosture.Restrictive) unrestTarget *= 0.45f;
            else if (gov.civicPosture == CivicPosture.Open) unrestTarget *= 1.15f;

            // Asymmetric, like living standards above and for the same reason:
            // anger organises faster than it disperses. A single rate meant that
            // four years into a total economic collapse unrest had reached only
            // three quarters of the level that collapse justified, because the
            // target kept moving while the value crawled after it.
            //
            // The slow fall matters as much as the quick rise — a government that
            // repairs the economy does not get its streets back the same quarter,
            // which is what stops unrest from being a number you buy off.
            float unrestRate = unrestTarget > country.socialUnrest ? 0.11f : 0.05f;
            country.socialUnrest = Approach(country.socialUnrest, unrestTarget, unrestRate);

            // ---- public grievance: what is not forgotten ----
            //
            // Accrues only from real hardship, and decays on a decade scale. The
            // floor is deliberate: a country that has been through something does
            // not return to the condition of one that has not.
            // The deprivation threshold is 40, not 35, and it has to be above the
            // decay rate to accumulate at all. At 35 the gain from living
            // standards of 31.7 was 0.026/month against a decay of 0.045, so
            // grievance sat pinned at exactly zero — and the only other source
            // needed unrest above 45, which unrest could not reach without the
            // grievance it was gated behind. A circular deadlock: the memory of
            // hardship required hardship the country was not allowed to suffer.
            float grievanceGain =
                Math.Max(0f, country.socialUnrest - 45f) * 0.010f
                + Math.Max(0f, 40f - country.livingStandards) * 0.011f
                + (state.IsAtWar(country.id) ? country.warExhaustion * 0.004f : 0f);

            // Decay is **proportional to what has accumulated**, not a flat
            // subtraction. A constant 0.045/month has no equilibrium: any
            // hardship producing more than that ratchets grievance to 100 and
            // pins it there forever, which is this codebase's most-repeated bug
            // and which the first version of this line reintroduced.
            //
            // Proportional decay gives every level of hardship its own resting
            // point instead — sustained living standards of 25 settle near 30,
            // and only permanent total deprivation approaches the ceiling. The
            // floor still does its job: a country that has been through something
            // does not return to the condition of one that has not, it just no
            // longer does so irreversibly.
            float grievanceDecay = 0.045f + country.publicGrievance * 0.004f;
            country.publicGrievance = Clamp(country.publicGrievance + grievanceGain - grievanceDecay);

            // ---- what the layer does to the rest of the state ----
            //
            // Unrest is not a readout. It costs order, and it feeds the
            // conspiracy that ends governments — which is the point of tracking
            // it separately from approval, since a coup follows organisation
            // rather than unpopularity.
            if (country.socialUnrest > 55f)
            {
                country.stability = Clamp(country.stability - (country.socialUnrest - 55f) * 0.020f);
                gov.conspiracyLevel = Clamp(
                    gov.conspiracyLevel + (country.socialUnrest - 55f) * 0.012f);
            }
        }

        static void UpdatePoliticalCondition(GameState state, CountryState country, GovernmentState gov)
        {
            var eco = country.economy;

            UpdateSocialCondition(state, country, gov);

            // Living standards drive approval more than anything else — and now
            // they are a real accumulated quantity rather than a phrase in a
            // comment describing this month's growth figure.
            float approvalPull = eco.growthRate * 0.35f
                                 - Math.Max(0f, eco.inflation - 4f) * 0.30f
                                 - Math.Max(0f, eco.unemployment - 7f) * 0.18f
                                 - country.warExhaustion * 0.03f
                                 + (country.livingStandards - 55f) * 0.09f;

            // Approach a level rather than integrating a rate. Accumulating the
            // pull each month meant a healthy economy drove approval to 100 in
            // about five years and pinned it there for the rest of the save,
            // which made elections a formality, and — because the AI scores
            // ConsolidateHome on (50 − approval) — stopped every AI government
            // from ever attending to its own domestic condition again.
            float approvalTarget = Clamp(50f + approvalPull * 6f + ApprovalShiftFor(gov)
                                         - country.socialUnrest * 0.25f
                                         + gov.publicMessaging * 0.42f);
            country.governmentApproval = Approach(country.governmentApproval, approvalTarget, 0.06f);

            // Stability and unity had **no restoring force at all** — the only
            // two political stats in the simulation without one. They ratcheted
            // down from a dozen flat drains (war, occupation, inflation, civil
            // conflict, emergency powers) and recovered only through discrete
            // events, so a country that had a bad decade could never be governed
            // back to health. National unity had exactly one repeatable player
            // source in the entire game.
            //
            // This is the bug class this project has now hit five times: a value
            // that is decremented every month by conditions that recur, with a
            // recovery path that does not. Both now sit at a level the state's
            // own institutions and standing can hold.
            float stabilityTarget = Clamp(
                38f
                + country.pillars.government * 0.30f
                + country.governmentApproval * 0.20f
                - country.warExhaustion * 0.25f
                - (gov.inCivilConflict ? 22f : 0f)
                - InsurgencySystem.StabilityDrag(state, country.id)
                + StabilityShiftFor(gov));
            country.stability = Approach(country.stability, stabilityTarget, 0.05f);

            float unityTarget = Clamp(
                42f
                + country.pillars.government * 0.20f
                + country.stability * 0.25f
                - country.warExhaustion * 0.20f
                + UnityShiftFor(gov)
                // Weighted well below approval on purpose. Talking to the country
                // can make a government liked; it cannot by itself make a divided
                // country whole, and unity has the slowest drift in the file, so a
                // large term here would be the strongest lever in the pillar.
                + gov.publicMessaging * 0.16f);
            country.nationalUnity = Approach(country.nationalUnity, unityTarget, 0.04f);

            // Bought support fades. It moves the *target* below rather than the
            // value, because both of those drift, and it decays so that support
            // is something a government maintains rather than something it
            // purchases once and keeps forever.
            gov.brokeredSupport = Math.Max(0f, gov.brokeredSupport * BrokeredSupportDecay);

            // So does a message. Faster than patronage: a favour owed outlasts a
            // speech given, and this is the difference between a campaign a
            // government sustains and one it can stop paying for.
            gov.publicMessaging = Math.Max(0f, gov.publicMessaging * MessagingDecay);

            // A restrictive apparatus is not free to hold. Charged against the
            // same pool everything else is bought from, so it competes with
            // governing rather than sitting outside the economy.
            if (gov.civicPosture == CivicPosture.Restrictive)
                SpendPoliticalCapitalBy(state, country.id, RestrictiveUpkeep, "Civic apparatus");

            // A country at peace recovers from the last war. War exhaustion had
            // only one decrement in the whole simulation (−10 when a
            // confrontation closed), so it ratcheted up permanently — and since
            // it feeds RegimeSystem's coup pressure, any state that fought two
            // wars was locked into a coup cycle for the rest of the game.
            if (!state.IsAtWar(country.id))
            {
                country.warExhaustion = Math.Max(0f, country.warExhaustion - 0.7f);

                // Public willingness to fight recovers toward normal in peacetime
                // too. It was drained by every war month and never restored, so
                // after roughly a war and a half an AI government could no longer
                // escalate (gate: > 35) and sued for terms the moment it opened
                // anything (gate: < 20). The world stopped being dangerous.
                // A martial tradition holds its public through a long war —
                // which is also why a settlement is harder to sell there.
                country.warSupport = Approach(country.warSupport, 50f,
                    0.02f * NationalTraitCatalog.WarSupportResilience(country));
            }

            // Leader competence slowly steadies the institutions — through the
            // shared growth curve, like every other capability gain.
            //
            // This was a raw monthly addition, the only un-damped capability
            // ratchet left in the simulation: a leader of competence 85 added
            // roughly 17 points a decade with nothing tapering it. It stayed
            // under the long-run saturation invariant only because AI states had
            // little else raising the pillar, and crossed it the moment foreign
            // cabinets started contributing too. `Growth.Apply` leaves losses
            // fully felt, so a poor leader still erodes institutions at the
            // undamped rate.
            country.pillars.government =
                Growth.Apply(country.pillars.government, (gov.leader.competence - 50f) * 0.004f);

            if (gov.IsElective)
            {
                float supportTarget = country.governmentApproval * 0.7f + country.pillars.government * 0.3f
                                      + gov.brokeredSupport * 0.45f
                                      + FactionSupportShift(gov)
                                      // A campaign against the government is
                                      // weight in the chamber. It moves the
                                      // target, like everything else here —
                                      // subtracting from the value would be
                                      // erased by this same tick's drift.
                                      - OppositionSystem.SupportDrag(gov);
                gov.legislativeSupport = Approach(gov.legislativeSupport, Clamp(supportTarget), 0.08f);
            }
            else
            {
                float cohesionTarget = 40f + country.pillars.government * 0.35f + country.stability * 0.25f
                                       - country.warExhaustion * 0.15f
                                       + gov.brokeredSupport * 0.45f
                                       + FactionCohesionShift(gov);
                if (gov.emergencyPowers) cohesionTarget -= 8f;
                gov.eliteCohesion = Approach(gov.eliteCohesion, Clamp(cohesionTarget), 0.06f);
            }
        }

        static void UpdateEmergencyPowers(GameState state, CountryState country, GovernmentState gov)
        {
            if (!gov.emergencyPowers) return;

            gov.emergencyPowersMonthsRemaining--;

            // Extraordinary authority is politically corrosive while it lasts.
            country.governmentApproval = Clamp(country.governmentApproval - 0.8f);
            country.nationalUnity = Clamp(country.nationalUnity - 0.4f);
            if (gov.IsElective)
                gov.legislativeSupport = Clamp(gov.legislativeSupport - 1.2f);

            if (gov.emergencyPowersMonthsRemaining <= 0)
            {
                gov.emergencyPowers = false;
                if (country.isPlayer)
                {
                    state.AddNotification(NotificationClass.Advisory, "EMERGENCY POWERS LAPSED",
                        "Extraordinary authority has expired. Normal command capacity restored.", country.id,
                        desk: ReportingDesk.Government);
                }
                state.AddChronicle(ChronicleCategory.Political, country.id,
                    "Emergency powers lapsed.", Publicity.Public);
            }
        }

        /// <summary>
        /// Political Capital a government earns in a month, from its own standing.
        ///
        /// Shared by the player and every AI government (GDD §13). Keeping one
        /// formula is the point: a popular government with a cohesive elite is
        /// well funded politically whoever is running it, and a government that
        /// has spent its authority has to rebuild it the same way.
        ///
        /// Operator skill is **not** included here — it is added only to the
        /// player's pool by the caller, because skills grant operator capability
        /// and never national power (GDD §25.3, test-enforced).
        /// </summary>
        public static float PoliticalCapitalIncomeFor(CountryState country)
        {
            var gov = country.government;

            // **Governing well has to buy room to govern.**
            //
            // This used to run 1.8–3.6 a month against verbs costing 1 to 6, so
            // an operator could afford roughly *one* government action a month
            // whatever they did. The pillar looked well stocked — thirteen
            // controls — and played as a single forced move, which is why adding
            // more verbs to it would have changed nothing.
            //
            // The narrowness was the real fault rather than the level. A range of
            // barely 2× means the difference between a popular, cohesive
            // government and a despised, fractured one is a third of an action a
            // month: nothing to build toward, and no reason to spend on the
            // pillar's own condition. Now roughly 2.2 when failing and 4.4 when
            // governing well, so a good administration gets two or three moves and
            // a bad one is genuinely constrained.
            //
            // The floor is deliberate. A failing government must not be *unable*
            // to act — that is a death spiral with no recovery path, the bug class
            // this project has shipped more than any other. It is slow, not stuck.
            float backing = gov.IsElective ? gov.legislativeSupport : gov.eliteCohesion;

            float income = 0.8f
                           + country.governmentApproval / 50f
                           + country.pillars.government / 80f
                           + backing / 100f;

            if (gov.emergencyPowers) income += 0.5f;

            // A state apparatus that executes (GDD §11).
            income += TechnologySystem.Effectiveness(country, "CAP_CIVADMIN") * 0.6f;

            return income;
        }

        static void AccruePoliticalCapital(GameState state)
        {
            foreach (var country in state.countries)
            {
                float income = PoliticalCapitalIncomeFor(country);

                if (country.isPlayer)
                {
                    income += ProgressionSystem.EffectValue(state, SkillEffect.PoliticalOperator);
                    state.politicalCapital =
                        Math.Min(GameState.PoliticalCapitalCap, state.politicalCapital + income);
                    continue;
                }

                var ai = state.FindAI(country.id);
                if (ai == null) continue;
                ai.politicalCapital =
                    Math.Min(GameState.PoliticalCapitalCap, ai.politicalCapital + income);
            }
        }

        /// <summary>
        /// Political Capital available to a government, whoever runs it.
        /// </summary>
        public static float PoliticalCapitalOf(GameState state, string countryId)
        {
            if (countryId == state.playerCountryId) return state.politicalCapital;
            var ai = state.FindAI(countryId);
            return ai?.politicalCapital ?? 0f;
        }

        /// <summary>
        /// Actor-generic Political Capital spend. Returns false when the
        /// government cannot afford the act — which must bite for AI states
        /// exactly as it bites for the player, or the budget is decoration.
        /// </summary>
        public static bool SpendPoliticalCapitalBy(GameState state, string countryId, float amount, string reason)
        {
            if (countryId == state.playerCountryId)
                return SpendPoliticalCapital(state, amount, reason);

            var ai = state.FindAI(countryId);
            if (ai == null || ai.politicalCapital < amount) return false;

            ai.politicalCapital -= amount;
            GameLog.Debug("GOV", $"{countryId} spent {amount:F1} PC: {reason}.");
            return true;
        }

        // ---------- elections ----------

        static void HoldElection(GameState state, CountryState country, GovernmentState gov, Random rng)
        {
            var eco = country.economy;

            // Centered on 50: an average government in average conditions is a
            // coin flip. Elections are genuinely uncertain, and long incumbency
            // accumulates anti-incumbent sentiment regardless of performance.
            float incumbentScore = 50f
                                   + (country.governmentApproval - 50f) * 0.8f
                                   + eco.growthRate * 3.0f
                                   + (country.nationalUnity - 50f) * 0.15f
                                   + (gov.legislativeSupport - 50f) * 0.2f
                                   - Math.Max(0f, eco.inflation - 4f) * 1.8f
                                   - country.warExhaustion * 0.3f
                                   - gov.leader.termsServed * 6f
                                   // What the opposition has actually been
                                   // campaigning on, and for how long. Without
                                   // this an election was a roll against
                                   // incumbency fatigue and the campaign that
                                   // preceded it counted for nothing.
                                   - OppositionSystem.ElectionDrag(gov)
                                   + (float)(rng.NextDouble() * 28.0 - 14.0);

            bool incumbentHolds = incumbentScore >= 50f;
            gov.nextElectionDate = AddMonths(state.date, gov.termLengthMonths);

            // Term limits end an administration on schedule regardless of support.
            // The governing faction may hold power; the leader still changes.
            if (gov.consecutiveTermLimit > 0 && gov.leader.termsServed >= gov.consecutiveTermLimit)
            {
                bool factionRetainsPower = incumbentHolds;
                InstallNewLeadership(state, country, gov, rng, "term limit");
                gov.leader.faction = factionRetainsPower ? "GOVERNING PARTY" : "OPPOSITION";

                // `InstallNewLeadership` already filed the operator's own copy,
                // and it is the one that explains what the change means for this
                // office. A second item on the same event reads as two things
                // having happened.
                if (!country.isPlayer)
                    state.AddNotification(NotificationClass.Wire,
                        "ELECTION — TERM LIMIT REACHED",
                        $"{gov.leader.name} ({gov.leader.faction}) succeeds a term-limited leader in {country.displayName}.",
                        country.id, desk: ReportingDesk.Government);
                return;
            }

            if (incumbentHolds)
            {
                gov.leader.monthsInOffice = 0;
                gov.leader.termsServed++;
                gov.legislativeSupport = Clamp(gov.legislativeSupport + 6f);
                country.governmentApproval = Clamp(country.governmentApproval + 4f);

                // Our own election result is command traffic, not something the
                // government of the day forwards to us at its discretion — the
                // operator's writ is defined by who is in office.
                state.AddNotification(country.isPlayer ? NotificationClass.Priority : NotificationClass.Wire,
                    "ELECTION — INCUMBENT RETAINED",
                    country.isPlayer
                        ? $"{gov.leader.name} ({gov.leader.faction}) returned to office. "
                          + "The administration you serve continues, and so does your authority "
                          + "over each pillar as it stands."
                        : $"{gov.leader.name} ({gov.leader.faction}) returned to office in {country.displayName}.",
                    country.id,
                    desk: country.isPlayer ? ReportingDesk.Command : ReportingDesk.Government);
                state.AddChronicle(ChronicleCategory.Political, country.id,
                    $"Election: {gov.leader.name} retained office.", Publicity.Public);
            }
            else
            {
                InstallNewLeadership(state, country, gov, rng, "election defeat");
            }
        }

        // ---------- confidence (GDD §13, spec 05 §2b) ----------

        /// <summary>Support below which a parliamentary government can fall.</summary>
        public const float ConfidenceThreshold = 30f;

        /// <summary>Monthly chance the chamber brings a failing government down.</summary>
        public const double ConfidenceCollapseChance = 0.15;

        /// <summary>
        /// A parliamentary government that has lost its chamber can lose office
        /// between elections. `GovernmentType.ParliamentaryRepublic`'s own
        /// declaration promised this — "government falls with confidence, early
        /// elections possible" — and nothing implemented the first half:
        /// legislative support could sit at zero for a decade with no
        /// consequence beyond a thinner PC income. This is where the number
        /// finally bites, and what makes the two elective types play
        /// differently: a presidential system rides out a hostile chamber to
        /// the scheduled date; a parliamentary one lives month to month.
        ///
        /// Probabilistic rather than a hard threshold-plus-timer so the fall
        /// arrives with the unpredictability of an ambushed division vote, and
        /// deterministic per save for the same reason everything else is.
        /// Recovery stays reachable the whole way down: brokered support,
        /// patronage and messaging all move the target this reads.
        /// </summary>
        static void CheckConfidence(GameState state, CountryState country, GovernmentState gov, Random rng)
        {
            if (!gov.AllowsEarlyElection) return;
            if (gov.legislativeSupport >= ConfidenceThreshold) return;
            if (rng.NextDouble() >= ConfidenceCollapseChance) return;

            state.AddChronicle(ChronicleCategory.Political, country.id,
                $"{country.displayName}: the government of {gov.leader.name} falls on a confidence vote.",
                Publicity.Public);
            if (!country.isPlayer)
                state.AddNotification(NotificationClass.Wire, "GOVERNMENT FALLS",
                    $"{gov.leader.name}'s government has lost the confidence of the chamber in " +
                    $"{country.displayName}. A new administration forms from the snap election.",
                    country.id, desk: ReportingDesk.Government);

            // The snap election is not a coin flip — a government that fell has
            // already lost the argument. `InstallNewLeadership` files the
            // player's own NEW ADMINISTRATION item; one item per handover.
            InstallNewLeadership(state, country, gov, rng, "lost the confidence of the chamber");
            gov.nextElectionDate = AddMonths(state.date, gov.termLengthMonths);
        }

        // ---------- succession ----------

        static void CheckSuccession(GameState state, CountryState country, GovernmentState gov, Random rng)
        {
            // Age and elite fracture are the two routes to a leadership change.
            float ageRisk = Math.Max(0f, gov.leader.age - 68f) * 0.004f;
            float cohesionRisk = gov.eliteCohesion < 35f ? (35f - gov.eliteCohesion) * 0.003f : 0f;
            float risk = ageRisk + cohesionRisk;

            if (risk <= 0f || rng.NextDouble() >= risk) return;

            // A prepared succession is not a fought one. This is the other half
            // of what grooming buys, and the half the player actually feels:
            // a contested handover costs 12 stability and 8 unity.
            bool contested = gov.eliteCohesion < 45f && gov.successorReadiness < 50f;
            InstallNewLeadership(state, country, gov, rng, contested ? "contested succession" : "orderly succession");

            if (contested)
            {
                // A brokered fight leaves the state weaker for a while.
                country.stability = Clamp(country.stability - 12f);
                country.nationalUnity = Clamp(country.nationalUnity - 8f);
                gov.eliteCohesion = Clamp(gov.eliteCohesion + 15f); // resolved, for now
                // Not player-gated before this: a contested succession anywhere
                // in the world raised FLASH on the operator's own briefing.
                state.AddNotification(
                    country.isPlayer ? NotificationClass.Priority : NotificationClass.Wire,
                    "CONTESTED SUCCESSION",
                    $"Leadership struggle in {country.displayName} has unsettled the state.", country.id,
                    desk: ReportingDesk.Government);
            }
        }

        /// <summary>
        /// Install a new leadership. Priorities and personnel change; inherited
        /// national capabilities do not (GDD §13) — a new administration does not
        /// magically re-arm or re-industrialize the country.
        /// </summary>
        static void InstallNewLeadership(GameState state, CountryState country, GovernmentState gov, Random rng, string cause)
        {
            var profile = WorldFactory.FindProfile(country.id);
            var previousName = gov.leader.name;
            var previousPriority = gov.leader.priority;

            var priorities = (NationalPriority[])Enum.GetValues(typeof(NationalPriority));
            var newPriority = priorities[rng.Next(priorities.Length)];

            gov.leader = new Leader
            {
                name = profile != null
                    ? $"{profile.firstNames[rng.Next(profile.firstNames.Length)]} {profile.lastNames[rng.Next(profile.lastNames.Length)]}"
                    : "NEW LEADERSHIP",
                faction = gov.IsElective
                    ? (previousName != null && rng.NextDouble() < 0.5 ? "OPPOSITION" : "REFORM BLOC")
                    : "PARTY LEADERSHIP",
                priority = newPriority,
                competence = 40f + (float)rng.NextDouble() * 45f,
                age = 48f + (float)rng.NextDouble() * 22f,
                monthsInOffice = 0
            };

            // A prepared successor arrives knowing the file. Grooming raises the
            // floor rather than the ceiling — it cannot manufacture a great
            // leader, it only stops the state being handed to someone who has
            // never seen the papers. Consumed by the transition it was built for.
            if (gov.successorReadiness > 0f)
            {
                float floor = 40f + gov.successorReadiness * 0.35f;
                if (gov.leader.competence < floor) gov.leader.competence = floor;
                gov.successorReadiness = 0f;
            }

            // Honeymoon: new leadership starts with public goodwill, not capability.
            country.governmentApproval = Clamp(55f + (float)rng.NextDouble() * 10f);
            if (gov.IsElective) gov.legislativeSupport = Clamp(58f + (float)rng.NextDouble() * 12f);

            // Emergency authority does not survive a change of leadership.
            gov.emergencyPowers = false;
            gov.emergencyPowersMonthsRemaining = 0;

            // No priority in the text. A leadership change is unmissable news, but
            // `NationalPriority` is an internal strategic orientation set
            // privately elsewhere — announcing it here would have handed every
            // observer, for free, something they should have to collect for.
            state.AddChronicle(ChronicleCategory.Political, country.id,
                $"{gov.leader.name} takes office in {country.displayName} ({cause}).",
                Publicity.Public);

            // Every new administration brings its own people, ours included.
            ReshuffleCabinetOf(state, country, rng);

            if (country.isPlayer)
            {
                state.administrationsServed++;

                // Delegated authority was personal to the government that granted
                // it. A new administration decides for itself what the operator
                // may command (spec 05 §1a).
                AuthoritySystem.ClearGrantedAuthority(state);
                MandateSystem.Reissue(state, cause);   // spec 24 §3

                // **The one item that must never be buried.**
                //
                // Reported from play as a straight objection: "someone else was
                // elected, and I still control the country — this makes no
                // sense." The premise is GDD §13's, and the game had been
                // stating it in a single trailing clause on an item the
                // Government desk was free to strip of urgency or lose
                // entirely — so the operator could meet a change of
                // administration as an unexplained change of name on a readout,
                // plus a set of authorities that had quietly reverted.
                //
                // `ReportingDesk.Command` is what the operator sees directly,
                // and this qualifies by the same rule that exempts their own
                // orders: it is news about *this office*, not about the world.
                // Still PRIORITY rather than FLASH — §28.2 reserves FLASH for a
                // turn that cannot be taken without deciding, and nothing here
                // needs answering.
                state.AddNotification(NotificationClass.Priority, "NEW ADMINISTRATION",
                    $"{gov.leader.name} ({gov.leader.faction}) takes office in "
                    + $"{country.displayName} ({cause}).\n"
                    + "You are not the head of government and never were. This office is a "
                    + "permanent post: administrations are elected, appointed and removed, and "
                    + "the operator at this terminal remains.\n"
                    + $"WHAT CHANGES: national priority shifts from {previousPriority} to "
                    + $"{newPriority}, which redirects every delegated official; the cabinet has "
                    + "been reshuffled; and any pillar this office had been GRANTED authority "
                    + "over must be granted again by the incoming administration.\n"
                    + "WHAT DOES NOT: your post, your record, your skills, and every "
                    + "constitutional authority the office holds in its own right.",
                    country.id, desk: ReportingDesk.Command);
                GameLog.Info("GOV", $"New administration: {gov.leader.name}. Priority {newPriority}.");
            }
            else
            {
                state.AddNotification(NotificationClass.Wire, "FOREIGN LEADERSHIP CHANGE",
                    $"{gov.leader.name} takes office in {country.displayName}.", country.id,
                    desk: ReportingDesk.Government);
            }
        }

        /// <summary>
        /// A new administration brings its own people. Replaced officials arrive
        /// with fresh trust and revert to autonomous operation.
        /// </summary>
        /// <summary>
        /// Actor-generic reshuffle. Every government's new administration brings
        /// its own people — without this, a foreign minister appointed at world
        /// creation would still be in office fifty years and six elections later,
        /// which is the sort of thing that reads as a frozen world.
        /// </summary>
        static void ReshuffleCabinetOf(GameState state, CountryState country, Random rng)
        {
            if (country == null) return;
            var profile = WorldFactory.FindProfile(country.id);
            if (profile == null) return;

            int replaced = 0;
            for (int i = 0; i < country.cabinet.Count; i++)
            {
                if (rng.NextDouble() >= 0.5) continue;

                var official = country.cabinet[i];
                official.displayName = $"{profile.firstNames[rng.Next(profile.firstNames.Length)]} " +
                                       $"{profile.lastNames[rng.Next(profile.lastNames.Length)]}";
                official.competence = 40f + (float)rng.NextDouble() * 38f;
                official.loyalty = 35f + (float)rng.NextDouble() * 45f;
                official.riskTolerance = 20f + (float)rng.NextDouble() * 60f;
                official.trust = 50f + (float)rng.NextDouble() * 12f;
                official.monthsInOffice = 0;
                official.mode = ControlMode.Autonomous;
                official.directiveId = "";
                replaced++;
            }

            // Against the country it happened in, not the player's. Harmless
            // while this only ever ran for the player; the moment it went
            // actor-generic it started filing every foreign reshuffle in the
            // world into our own national record.
            if (replaced > 0)
                state.AddChronicle(ChronicleCategory.Political, country.id,
                    $"Cabinet reshuffle in {country.displayName}: {replaced} office(s) changed hands.",
                    Publicity.Public);
        }

        // ---------- player commands ----------

        public static bool PublicMessaging(GameState state)
        {
            if (!PublicMessagingBy(state, state.playerCountryId)) return false;

            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 8, "Public messaging");
            return true;
        }

        /// <summary>
        /// Actor-generic public messaging. Any government can address its people;
        /// all of them pay the same Political Capital for it.
        /// </summary>
        public static bool PublicMessagingBy(GameState state, string countryId)
        {
            if (!SpendPoliticalCapitalBy(state, countryId, PublicMessagingCost, "Public messaging campaign"))
                return false;

            var country = state.FindCountry(countryId);
            if (country == null) return false;

            // **Moves the target, not the value.** See Government.publicMessaging:
            // writing approval and unity directly meant the monthly drift erased
            // the campaign, so the most repeatable verb in the pillar could not
            // hold either stat anywhere the model did not already want it.
            //
            // Headroom shape borrowed from BuildPoliticalSupport: the first
            // campaign lands hard and a government already saturating the airwaves
            // gets progressively less for the same 2 PC, so messaging is worth
            // starting and not worth spamming.
            var gov = country.government;
            float effectiveness = 1f + country.pillars.government / 120f;
            float headroom = Math.Max(0f, MessagingCeiling - gov.publicMessaging);
            gov.publicMessaging = Clamp(
                gov.publicMessaging + (2.5f + headroom * 0.13f) * effectiveness);

            state.AddChronicle(ChronicleCategory.Political, country.id,
                "Public messaging campaign conducted.", Publicity.Public);
            return true;
        }

        /// <summary>
        /// Dismiss and replace a pillar official. Costs Political Capital, and in
        /// elective systems also burns legislative goodwill (GDD §7.3, §13).
        /// </summary>
        public static bool DismissOfficial(GameState state, Pillar office)
        {
            var official = state.FindOfficial(office);
            if (official == null) return false;

            var player = state.PlayerCountry;
            var gov = player.government;
            float cost = DismissOfficialCost * (gov.IsElective ? 1.25f : 0.85f);
            if (!SpendPoliticalCapital(state, cost, $"Dismiss {official.title}")) return false;

            int monthIndex = state.date.MonthsSince(state.startDate);
            // Per-invocation, or dismissing the same office twice in a month
            // returns a bit-identical replacement and the second spend buys
            // nothing.
            var rng = new Random(unchecked(
                state.rngSeed * 7211 + monthIndex * 89 + (int)office
                + state.NextActionSequence() * 104729));
            var profile = WorldFactory.FindProfile(player.id);

            string dismissedName = official.displayName;
            official.displayName = profile != null
                ? $"{profile.firstNames[rng.Next(profile.firstNames.Length)]} {profile.lastNames[rng.Next(profile.lastNames.Length)]}"
                : "APPOINTEE";
            official.competence = 42f + (float)rng.NextDouble() * 40f;
            official.loyalty = 45f + (float)rng.NextDouble() * 40f; // hand-picked: loyal
            official.riskTolerance = 20f + (float)rng.NextDouble() * 60f;
            official.trust = 55f + (float)rng.NextDouble() * 10f;
            official.monthsInOffice = 0;
            official.mode = ControlMode.Autonomous;
            official.directiveId = "";

            if (gov.IsElective)
                gov.legislativeSupport = Clamp(gov.legislativeSupport - 4f);

            state.AddNotification(NotificationClass.Priority, "CABINET CHANGE",
                $"{dismissedName} dismissed. {official.displayName} appointed {official.title}.", player.id);
            state.AddChronicle(ChronicleCategory.Political, player.id,
                $"{dismissedName} dismissed as {official.title}.", Publicity.Public);
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 14, "Cabinet appointment");
            return true;
        }

        /// <summary>Institutional reform: durable improvement to how the state functions.</summary>
        public static bool InstitutionalReform(GameState state)
        {
            if (!InstitutionalReformBy(state, state.playerCountryId)) return false;

            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 20, "Institutional reform");
            return true;
        }

        /// <summary>
        /// Actor-generic institutional reform. Builds the state's capacity and
        /// costs it the goodwill of whoever benefits from the present
        /// arrangement — a bill every government pays, not only the player's.
        /// </summary>
        public static bool InstitutionalReformBy(GameState state, string countryId)
        {
            if (!SpendPoliticalCapitalBy(state, countryId, InstitutionalReformCost, "Institutional reform"))
                return false;

            var country = state.FindCountry(countryId);
            if (country == null) return false;

            // Diminishing returns (`Core/Growth.cs`), not a flat +4. As a raw
            // addition this was survivable while the player was the only one
            // reforming; opening it to fifteen AI governments saturated the
            // Government pillar at its ceiling within a decade, which the
            // long-run invariant tests correctly refused. Reform gets harder as
            // institutions get better, which is also simply true.
            country.pillars.government = Growth.Apply(country.pillars.government, 4f);
            country.stability = Clamp(country.stability + 3f);
            country.counterIntel.counterIntelligence = Clamp(country.counterIntel.counterIntelligence + 2f);

            // Reform disturbs entrenched interests.
            if (country.government.IsElective)
                country.government.legislativeSupport = Clamp(country.government.legislativeSupport - 3f);
            else
                country.government.eliteCohesion = Clamp(country.government.eliteCohesion - 5f);

            state.AddNotification(
                country.isPlayer ? NotificationClass.Advisory : NotificationClass.Wire,
                "INSTITUTIONAL REFORM",
                country.isPlayer
                    ? "Structural reform enacted. Entrenched interests are displeased."
                    : $"{country.displayName} has enacted structural reform.",
                country.id, desk: ReportingDesk.Government);
            state.AddChronicle(ChronicleCategory.Political, country.id,
                "Institutional reform enacted.", Publicity.Public);
            return true;
        }

        /// <summary>
        /// Emergency powers grant extra command capacity at real political cost.
        /// Centralized systems obtain them more cheaply; elective systems pay
        /// heavily in approval and legislative standing (GDD §13).
        /// </summary>
        public static bool DeclareEmergencyPowers(GameState state)
        {
            var player = state.PlayerCountry;
            var gov = player.government;
            if (gov.emergencyPowers)
            {
                GameLog.Warn("GOV", "Emergency powers are already in force.");
                return false;
            }

            float cost = EmergencyPowersCost * (gov.IsElective ? 1.4f : 0.7f);
            if (!SpendPoliticalCapital(state, cost, "Declare emergency powers")) return false;

            gov.emergencyPowers = true;
            gov.emergencyPowersMonthsRemaining = EmergencyPowersDuration;

            // Immediate political price, on top of the monthly drain.
            player.governmentApproval = Clamp(player.governmentApproval - (gov.IsElective ? 6f : 3f));
            player.stability = Clamp(player.stability - 2f);

            // The bonus is derived from the emergency state each month (see
            // TurnManager), so it lapses automatically with the authority.
            state.commandPoints.current += 2;

            state.AddNotification(NotificationClass.Priority, "EMERGENCY POWERS DECLARED",
                $"Extraordinary authority in force for {EmergencyPowersDuration} months. +2 CP per month.", player.id);
            state.AddChronicle(ChronicleCategory.Political, player.id,
                "Emergency powers declared.", Publicity.Public);
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 16, "Emergency powers");
            return true;
        }

        // ---------- political bargaining (GDD §13) ----------

        /// <summary>
        /// Bargain for support — with the legislature where there is one, with
        /// the ruling elite where there is not (GDD §13 "political bargaining").
        ///
        /// The pillar's workhorse: cheap, repeatable, and it decays, so it is
        /// something a government keeps doing rather than something it finishes.
        /// What it buys is concrete — below 30 legislative support the
        /// legislature refuses the operator direct authority over a pillar
        /// outright, and the political capital is spent asking either way.
        /// </summary>
        public static bool BuildPoliticalSupport(GameState state)
        {
            if (!BuildPoliticalSupportBy(state, state.playerCountryId)) return false;
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 9, "Political bargaining");
            return true;
        }

        /// <summary>Actor-generic. Every government bargains for its own support.</summary>
        public static bool BuildPoliticalSupportBy(GameState state, string countryId)
        {
            var country = state.FindCountry(countryId);
            if (country == null) return false;
            if (!SpendPoliticalCapitalBy(state, countryId, BuildSupportCost, "Political bargaining"))
                return false;

            var gov = country.government;

            // Diminishing: the first concessions are cheap and the last are not,
            // so a government cannot simply buy its way to a compliant chamber.
            float headroom = Math.Max(0f, 60f - gov.brokeredSupport);
            gov.brokeredSupport = Clamp(gov.brokeredSupport + 3f + headroom * 0.14f);

            if (country.isPlayer)
                state.AddNotification(NotificationClass.Advisory,
                    gov.IsElective ? "LEGISLATIVE BARGAIN STRUCK" : "ELITE ACCOMMODATION REACHED",
                    gov.IsElective
                        ? "Concessions traded for votes. The chamber will carry us further than it did."
                        : "Portfolios and guarantees exchanged. The inner circle is holding.",
                    countryId, desk: ReportingDesk.Government);
            return true;
        }

        /// <summary>
        /// Buy support with money instead of standing (GDD §13).
        ///
        /// The same destination as bargaining by a different road, and the choice
        /// between them is the interesting part: a rich government spends
        /// treasury, a well-regarded one spends authority. Patronage also buys
        /// the cabinet's loyalty — and corrodes the institutions doing it, which
        /// is the honest price of governing this way.
        /// </summary>
        public static bool DistributePatronage(GameState state)
        {
            if (!DistributePatronageBy(state, state.playerCountryId)) return false;
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 8, "Patronage distributed");
            return true;
        }

        /// <summary>Actor-generic.</summary>
        public static bool DistributePatronageBy(GameState state, string countryId)
        {
            var country = state.FindCountry(countryId);
            if (country == null) return false;
            if (country.resources.treasury < PatronageTreasury) return false;
            if (!SpendPoliticalCapitalBy(state, countryId, PatronageCost, "Patronage")) return false;

            country.resources.treasury -= PatronageTreasury;

            var gov = country.government;
            float headroom = Math.Max(0f, 60f - gov.brokeredSupport);
            gov.brokeredSupport = Clamp(gov.brokeredSupport + 2f + headroom * 0.10f);

            foreach (var official in country.cabinet)
                official.loyalty = Clamp(official.loyalty + 5f);

            // Losses are felt in full through Growth.Apply, so this genuinely
            // hollows the state out if it becomes the habitual instrument.
            country.pillars.government = Growth.Apply(country.pillars.government, -1.4f);

            if (country.isPlayer)
                state.AddNotification(NotificationClass.Advisory, "PATRONAGE DISTRIBUTED",
                    "Appointments, contracts and quiet favours. Everyone is content, and the " +
                    "machinery is a little worse than it was.", countryId, desk: ReportingDesk.Government);
            return true;
        }

        /// <summary>
        /// Turn the state's scrutiny on itself (GDD §13 "institutional reform").
        ///
        /// The inverse of patronage, and priced against the same pool: it makes
        /// the government work better and costs the goodwill of everyone whose
        /// arrangements it disturbs. It raises the *weakest* minister rather than
        /// the strongest, so it also improves what the operator is told —
        /// competence is what decides which traffic reaches the terminal at all.
        /// </summary>
        public static bool LaunchInquiry(GameState state)
        {
            if (!LaunchInquiryBy(state, state.playerCountryId)) return false;
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 14, "Institutional inquiry");
            return true;
        }

        /// <summary>Actor-generic.</summary>
        public static bool LaunchInquiryBy(GameState state, string countryId)
        {
            var country = state.FindCountry(countryId);
            if (country == null) return false;
            if (!SpendPoliticalCapitalBy(state, countryId, InquiryCost, "Institutional inquiry"))
                return false;

            Official weakest = null;
            foreach (var official in country.cabinet)
                if (weakest == null || official.competence < weakest.competence) weakest = official;

            if (weakest != null)
            {
                weakest.competence = Clamp(weakest.competence + 7f);
                weakest.loyalty = Clamp(weakest.loyalty - 4f); // nobody enjoys being audited
            }

            country.pillars.government = Growth.Apply(country.pillars.government, 2.5f);
            country.counterIntel.counterIntelligence =
                Clamp(country.counterIntel.counterIntelligence + 1.5f);

            // Whoever benefited from the arrangements being disturbed.
            var gov = country.government;
            gov.brokeredSupport = Math.Max(0f, gov.brokeredSupport - 9f);
            if (gov.IsElective) gov.legislativeSupport = Clamp(gov.legislativeSupport - 2f);
            else gov.eliteCohesion = Clamp(gov.eliteCohesion - 4f);

            if (country.isPlayer)
                state.AddNotification(NotificationClass.Advisory, "INQUIRY CONCLUDED",
                    weakest != null
                        ? $"Findings delivered. {weakest.displayName} has been put on notice and the " +
                          "department is sharper for it. Nobody involved is grateful."
                        : "Findings delivered. The machinery is sharper and nobody is grateful.",
                    countryId, desk: ReportingDesk.Government);
            state.AddChronicle(ChronicleCategory.Political, countryId,
                "Public inquiry into the conduct of government.", Publicity.Public);
            return true;
        }

        /// <summary>
        /// Prepare the ground for the next transition (GDD §13).
        ///
        /// Administrations come and go while the operator's tenure continues, and
        /// until now a change of leadership was purely something that happened
        /// *to* the player: a reshuffled cabinet, a dip in everything, no way to
        /// have seen it coming. Consumed by the transition it was built for.
        /// </summary>
        public static bool GroomSuccessor(GameState state)
        {
            if (!GroomSuccessorBy(state, state.playerCountryId)) return false;
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 10, "Succession prepared");
            return true;
        }

        /// <summary>Actor-generic.</summary>
        public static bool GroomSuccessorBy(GameState state, string countryId)
        {
            var country = state.FindCountry(countryId);
            if (country == null) return false;

            var gov = country.government;
            if (gov.successorReadiness >= 99f) return false;
            if (!SpendPoliticalCapitalBy(state, countryId, GroomSuccessorCost, "Prepare a successor"))
                return false;

            gov.successorReadiness = Clamp(gov.successorReadiness + 22f);

            if (country.isPlayer)
                state.AddNotification(NotificationClass.Advisory, "SUCCESSION PREPARED",
                    $"Continuity planning advanced. Readiness {gov.successorReadiness:F0}%. " +
                    "Whoever comes next will arrive knowing the file.",
                    countryId, desk: ReportingDesk.Government);
            return true;
        }

        /// <summary>
        /// Change how the state holds its own society (GDD §12, §13).
        ///
        /// A standing position rather than an action — the pillar had none, while
        /// the military had two. Restrictive suppresses plots and buys order at a
        /// standing cost in legitimacy and a monthly bill in political capital;
        /// open buys unity and legitimacy and lets conspiracy organise faster.
        /// </summary>
        public static bool SetCivicPosture(GameState state, CivicPosture posture)
        {
            if (!SetCivicPostureBy(state, state.playerCountryId, posture)) return false;
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 12, "Civic posture set");
            return true;
        }

        /// <summary>Actor-generic.</summary>
        public static bool SetCivicPostureBy(GameState state, string countryId, CivicPosture posture)
        {
            var country = state.FindCountry(countryId);
            if (country == null) return false;

            var gov = country.government;
            if (gov.civicPosture == posture) return false;
            if (!SpendPoliticalCapitalBy(state, countryId, CivicPostureCost,
                    $"Civic posture: {posture}")) return false;

            gov.civicPosture = posture;

            if (country.isPlayer)
                state.AddNotification(NotificationClass.Priority, "CIVIC POSTURE CHANGED",
                    $"The state now holds its society on {PostureText(posture)} terms.",
                    countryId, desk: ReportingDesk.Government);
            state.AddChronicle(ChronicleCategory.Political, countryId,
                $"Civic posture set to {PostureText(posture)}.", Publicity.Public);
            return true;
        }

        /// <summary>
        /// Permanently raise what the operator may personally command over one
        /// pillar (GDD §3, §13).
        ///
        /// The large purchase the Political Capital economy did not have. Income
        /// runs 1.5–3 a month against a cap of 20 and nothing cost more than 7,
        /// so an operator who was not spending simply sat at the cap — which is
        /// most of why this pillar was the least active in the game.
        ///
        /// Deliberately **player-only, and deliberately not national power**.
        /// Authority is an operator interface: it describes what this particular
        /// advisor may order without asking, not what the state is capable of. A
        /// foreign government has no operator standing outside it to be granted
        /// anything, which is the same reasoning that settled Influence and
        /// Crisis Turns.
        /// </summary>
        public static bool ConsolidateAuthority(GameState state, Pillar pillar)
        {
            var player = state.PlayerCountry;
            var gov = player.government;

            if (AuthoritySystem.AuthorityOver(state, pillar) == AuthoritySystem.AuthorityLevel.Direct)
            {
                GameLog.Warn("GOV", $"{pillar} is already the operator's to command directly.");
                return false;
            }

            // A legislature that will not back us will not amend our powers either.
            if (gov.IsElective && gov.legislativeSupport < 45f)
            {
                GameLog.Warn("GOV", "The chamber will not consider it while our support is this thin.");
                state.AddNotification(NotificationClass.Priority, "AMENDMENT REFUSED",
                    "Our standing in the chamber is too thin to carry a change of this kind. " +
                    "Build support first.", player.id, desk: ReportingDesk.Government);
                return false;
            }

            if (!SpendPoliticalCapital(state, ConsolidateAuthorityCost,
                    $"Consolidate authority over {pillar}")) return false;

            gov.authorityUpgradeMask |= 1 << (int)pillar;

            // Reaching for permanent power costs standing with whoever gave it up.
            if (gov.IsElective) gov.legislativeSupport = Clamp(gov.legislativeSupport - 6f);
            else gov.eliteCohesion = Clamp(gov.eliteCohesion - 5f);
            player.governmentApproval = Clamp(player.governmentApproval - 2f);

            state.AddNotification(NotificationClass.Priority, "AUTHORITY CONSOLIDATED",
                $"{pillar.ToString().ToUpperInvariant()} is now ours to direct. The institutions " +
                "that gave it up have not forgotten.", player.id, desk: ReportingDesk.Government);
            state.AddChronicle(ChronicleCategory.Political, player.id,
                $"Standing authority over {pillar} transferred to the strategic directorate.",
                Publicity.Public);

            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 26, "Authority consolidated");
            return true;
        }

        /// <summary>Parliamentary systems can go to the country early (GDD §13).</summary>
        public static bool CallEarlyElection(GameState state)
        {
            var player = state.PlayerCountry;
            var gov = player.government;
            if (!gov.AllowsEarlyElection)
            {
                GameLog.Warn("GOV", "This system does not permit an early election.");
                return false;
            }
            if (!SpendPoliticalCapital(state, EarlyElectionCost, "Call early election")) return false;

            gov.nextElectionDate = state.date;
            state.AddNotification(NotificationClass.Priority, "EARLY ELECTION CALLED",
                "The government has gone to the country.", player.id);
            ProgressionSystem.RecordInitiative(state);
            return true;
        }

        /// <summary>
        /// Set the national priority directly — a costly political intervention
        /// that redirects every autonomous official (GDD §13).
        /// </summary>
        public static bool SetNationalPriority(GameState state, NationalPriority priority)
        {
            var player = state.PlayerCountry;
            if (player.government.leader.priority == priority) return false;
            if (!SpendPoliticalCapital(state, NationalPriorityCost, $"Set national priority: {priority}"))
                return false;

            player.government.leader.priority = priority;
            ProgressionSystem.RecordInitiative(state);
            state.AddChronicle(ChronicleCategory.Political, player.id, $"National priority set to {priority}.");
            return true;
        }

        // ---------- queries ----------

        /// <summary>
        /// How strongly the national priority boosts a pillar's autonomous work.
        /// The matching pillar is favored; the others are quietly starved.
        /// </summary>
        public static float PriorityMultiplierFor(NationalPriority priority, Pillar pillar)
        {
            bool favored;
            switch (priority)
            {
                case NationalPriority.Security: favored = pillar == Pillar.Military || pillar == Pillar.Intelligence; break;
                case NationalPriority.Prosperity: favored = pillar == Pillar.Economy; break;
                case NationalPriority.Influence: favored = pillar == Pillar.Diplomacy; break;
                default: favored = pillar == Pillar.Government; break;
            }
            return favored ? 1.35f : 0.85f;
        }

        public static bool SpendPoliticalCapital(GameState state, float amount, string reason)
        {
            if (state.politicalCapital < amount)
            {
                GameLog.Warn("GOV", $"Insufficient Political Capital for: {reason} " +
                                    $"(need {amount:F1}, have {state.politicalCapital:F1}).");
                Telemetry.Record(state, TelemetryKind.PlayerAction, state.playerCountryId,
                    Telemetry.Bucket(reason), reason, amount, success: false);
                return false;
            }
            state.politicalCapital -= amount;
            GameLog.Info("GOV", $"{amount:F1} PC spent: {reason}. Remaining: {state.politicalCapital:F1}.");
            Telemetry.Record(state, TelemetryKind.PlayerAction, state.playerCountryId,
                Telemetry.Bucket(reason), reason, amount);
            return true;
        }

        static GameDate AddMonths(GameDate date, int months)
        {
            var result = date;
            for (int i = 0; i < months; i++) result = result.NextMonth();
            return result;
        }

        static float Approach(float current, float target, float rate) => current + (target - current) * rate;
        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
