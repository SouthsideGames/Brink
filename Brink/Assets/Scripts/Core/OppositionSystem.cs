using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// The people who want this government out (GDD §13, §27).
    ///
    /// **The government pillar had an election and no politics.** Faction became
    /// arithmetic, a chamber could withdraw confidence, and an election resolved
    /// as a roll against incumbency fatigue — but nobody was ever *campaigning*.
    /// Nothing accumulated a case, nothing chose an issue, and the operator had
    /// no way to answer one. The result was a pillar where the only domestic
    /// antagonist was a coup: either nothing was happening or the army was in
    /// the building.
    ///
    /// The design turns on one idea: **the theme decides which answer works.**
    ///
    /// - A case built on hardship or a war cannot be shouted down. Confronting it
    ///   makes it worse, because the thing being denied is visible from every
    ///   kitchen in the country.
    /// - A case built on drift — that this government has run out of ideas — is
    ///   almost entirely mood, and mood is what a communications operation is
    ///   for.
    /// - Conceding always works and always costs something real, chosen to match
    ///   what was conceded: money for hardship, war support for a war, elite
    ///   goodwill for corruption, the civic posture itself for liberty.
    ///
    /// So the operator is not choosing a difficulty setting on a slider. They are
    /// reading what the country is actually angry about and deciding whether to
    /// pay for it or fight it, and the wrong instrument is worse than nothing.
    ///
    /// Everything here obeys the rules this codebase keeps re-learning:
    /// `oppositionCase` drifts toward a target computed from the record, so it
    /// falls when the record improves and never ratchets; the verbs are
    /// actor-generic and foreign governments answer their own oppositions with
    /// them; and the case is *read* in three places — the support target, the
    /// election, and organised unrest — rather than being a number on a screen.
    /// </summary>
    public static class OppositionSystem
    {
        /// <summary>Political Capital to give ground.</summary>
        public const float ConcedeCost = 2f;

        /// <summary>Political Capital to take them on.</summary>
        public const float ConfrontCost = 3f;

        /// <summary>Treasury a hardship concession costs.</summary>
        public const float HardshipConcessionTreasury = 900f;

        /// <summary>Below this the opposition is background noise rather than a campaign.</summary>
        public const float NoiseFloor = 25f;

        // ---------- what they have on us ----------

        /// <summary>
        /// The strength of case the government's own record can sustain, and what
        /// it is about.
        ///
        /// Every term is something the simulation already computes about this
        /// country. An opposition cannot invent a grievance any more than the
        /// chamber can invent an agenda — which is what makes governing well an
        /// actual defence rather than a modifier.
        /// </summary>
        public static float CaseTargetFor(GameState state, CountryState country,
            out OppositionTheme theme)
        {
            var gov = country.government;
            var eco = country.economy;

            float hardship = Math.Max(0f, 48f - country.livingStandards) * 0.85f
                           + Math.Max(0f, eco.inflation - 5f) * 1.6f
                           + Math.Max(0f, eco.unemployment - 7f) * 1.4f
                           + country.publicGrievance * 0.20f;

            // A war is only a case against you while it is costing more than it
            // is winning. War support is exactly that judgement, already made.
            float war = state.IsAtWar(country.id)
                ? country.warExhaustion * 0.75f + Math.Max(0f, 50f - country.warSupport) * 0.55f
                : country.warExhaustion * 0.30f;

            // **The recorded thing first** (spec 05 §2e). The three terms below
            // measure the state being *feeble* — a weak pillar, plots, a divided
            // elite — which is not the same as the state being bought, and was
            // all this had to go on before `gov.corruption` existed.
            float corruption = gov.corruption * 0.75f
                             + Math.Max(0f, 55f - country.pillars.government) * 0.60f
                             + gov.conspiracyLevel * 0.25f
                             + Math.Max(0f, 50f - gov.eliteCohesion) * 0.20f;

            float liberty = gov.civicPosture == CivicPosture.Restrictive
                ? 18f + country.publicGrievance * 0.35f + country.socialUnrest * 0.20f
                : Math.Max(0f, country.socialUnrest - 55f) * 0.30f;

            // The residual. Always there, never large on its own, and it is what
            // a long and uneventful tenure eventually produces.
            float drift = gov.leader.monthsInOffice * 0.055f
                        + Math.Max(0f, 45f - country.governmentApproval) * 0.35f;

            theme = OppositionTheme.Drift;
            float best = drift;
            if (hardship > best) { best = hardship; theme = OppositionTheme.Hardship; }
            if (war > best) { best = war; theme = OppositionTheme.War; }
            if (corruption > best) { best = corruption; theme = OppositionTheme.Corruption; }
            if (liberty > best) { best = liberty; theme = OppositionTheme.Liberty; }

            // An organised opposition needs somewhere to organise. A restrictive
            // state suppresses the *campaign* while every underlying cause keeps
            // accruing — the same bargain `CivicPosture` already makes with
            // unrest, and the same bill comes due when the posture relaxes.
            if (gov.civicPosture == CivicPosture.Restrictive && theme != OppositionTheme.Liberty)
                best *= 0.65f;

            return Clamp(best);
        }

        /// <summary>
        /// How much of the government's support the campaign is holding down.
        ///
        /// Read by `GovernmentSystem`'s monthly support target, so it moves the
        /// **target** rather than the value — a subtraction from the value would
        /// be erased by the same tick's drift, which is the trap that made
        /// occupation's readiness cost dead code for a year.
        /// </summary>
        public static float SupportDrag(GovernmentState gov)
            => Math.Max(0f, gov.oppositionCase - NoiseFloor) * 0.42f;

        /// <summary>What the campaign is worth against the incumbent at the ballot.</summary>
        public static float ElectionDrag(GovernmentState gov)
            => Math.Max(0f, gov.oppositionCase - NoiseFloor) * 0.55f;

        /// <summary>
        /// What an organised campaign adds to the pressure behind unrest.
        ///
        /// Fed into `GovernmentSystem`'s unrest **target**, never added to the
        /// value: unrest drifts to a computed level every month, so a direct
        /// addition would be gone by the next tick. Deliberately small — an
        /// opposition is legitimate politics, not a plot, and it must not become
        /// a back door into the conspiracy model. What it does is make a country
        /// that is already angry a little quicker to be in the street about it.
        /// </summary>
        public static float UnrestPressure(GovernmentState gov)
            => Math.Max(0f, gov.oppositionCase - 55f) * 0.12f;

        /// <summary>Plain description, for the government screen and the briefing.</summary>
        public static string Describe(OppositionTheme theme)
        {
            switch (theme)
            {
                case OppositionTheme.Hardship:
                    return "that people cannot afford to live";
                case OppositionTheme.War:
                    return "that the war is costing more than it can win";
                case OppositionTheme.Corruption:
                    return "that this government has hollowed out the state";
                case OppositionTheme.Liberty:
                    return "that this government rules by fiat";
                default:
                    return "that this government has run out of ideas";
            }
        }

        /// <summary>
        /// How well shouting at them works, 0..1.
        ///
        /// **This is the whole system in one function.** Material grievances are
        /// visible from every kitchen in the country and denying one insults the
        /// people living it; mood is a different matter, and mood is what a
        /// communications operation is actually for.
        /// </summary>
        public static float ConfrontationEffectiveness(OppositionTheme theme)
        {
            switch (theme)
            {
                case OppositionTheme.Hardship: return 0.15f;
                case OppositionTheme.War: return 0.20f;
                case OppositionTheme.Corruption: return 0.45f;
                case OppositionTheme.Liberty: return 0.35f;
                default: return 0.95f;
            }
        }

        // ---------- answering them ----------

        /// <summary>
        /// Give ground. Always works, always costs something real, and what it
        /// costs is chosen to match what was conceded.
        /// </summary>
        public static bool ConcedeBy(GameState state, string countryId)
        {
            var country = state.FindCountry(countryId);
            if (country == null) return false;

            var gov = country.government;
            if (gov.oppositionCase < NoiseFloor) return false;

            if (!GovernmentSystem.SpendPoliticalCapitalBy(
                    state, countryId, ConcedeCost, "Concede to the opposition")) return false;

            gov.oppositionCase = Clamp(gov.oppositionCase - 26f);
            country.governmentApproval = Clamp(country.governmentApproval + 3f);

            switch (gov.oppositionTheme)
            {
                case OppositionTheme.Hardship:
                    // A programme, not a speech. If the money is not there the
                    // concession is thinner, which is its own kind of answer.
                    float spend = Math.Min(HardshipConcessionTreasury,
                        Math.Max(0f, country.resources.treasury));
                    country.resources.treasury -= spend;
                    country.livingStandards = Clamp(
                        country.livingStandards + spend / HardshipConcessionTreasury * 3.5f);
                    country.publicGrievance = Clamp(country.publicGrievance - 4f);
                    break;

                case OppositionTheme.War:
                    // Saying you will wind it down is heard by the people you are
                    // asking to keep fighting it.
                    country.warSupport = Clamp(country.warSupport - 10f);
                    country.warExhaustion = Clamp(country.warExhaustion - 4f);
                    break;

                case OppositionTheme.Corruption:
                    // Somebody has to go, and their friends were your friends.
                    gov.eliteCohesion = Clamp(gov.eliteCohesion - 7f);
                    country.pillars.government = Growth.Apply(country.pillars.government, 1.5f);
                    break;

                case OppositionTheme.Liberty:
                    // The posture itself is the concession. There is no way to
                    // grant this one and keep the instrument.
                    if (gov.civicPosture == CivicPosture.Restrictive)
                        gov.civicPosture = CivicPosture.Standard;
                    country.publicGrievance = Clamp(country.publicGrievance - 6f);
                    break;

                default:
                    country.nationalUnity = Clamp(country.nationalUnity + 2f);
                    break;
            }

            if (country.isPlayer)
                state.AddNotification(NotificationClass.Advisory, "GROUND CONCEDED",
                    $"The government has moved toward the opposition on the argument "
                    + $"{Describe(gov.oppositionTheme)}. It has cost something to do it.",
                    countryId, desk: ReportingDesk.Government);

            state.AddChronicle(ChronicleCategory.Political, countryId,
                $"{country.displayName}'s government concedes ground to its opposition.",
                Publicity.Public);
            return true;
        }

        /// <summary>Player order: XP and initiative on top of the spend.</summary>
        public static bool Concede(GameState state)
        {
            if (!AuthoritySystem.EnsureAuthority(state, Pillar.Government)) return false;
            if (!ConcedeBy(state, state.playerCountryId)) return false;

            ProgressionSystem.AwardXP(state, 16, "Answered the opposition");
            ProgressionSystem.RecordInitiative(state);
            return true;
        }

        /// <summary>
        /// Take them on publicly. Cheap against a case made of mood and actively
        /// counter-productive against one made of facts.
        /// </summary>
        public static bool ConfrontBy(GameState state, string countryId)
        {
            var country = state.FindCountry(countryId);
            if (country == null) return false;

            var gov = country.government;
            if (gov.oppositionCase < NoiseFloor) return false;

            if (!GovernmentSystem.SpendPoliticalCapitalBy(
                    state, countryId, ConfrontCost, "Confront the opposition")) return false;

            float effectiveness = ConfrontationEffectiveness(gov.oppositionTheme);
            gov.oppositionCase = Clamp(gov.oppositionCase - 24f * effectiveness);

            // Whatever the argument, going after the people making it hardens the
            // people who agree with them.
            //
            // **Grievance, not unrest.** `socialUnrest` drifts to a target
            // computed every month, so adding to the value here would be erased
            // by the next tick — the trap that made occupation's readiness cost
            // dead code. Grievance is a genuine store with its own proportional
            // decay, and it is the right currency anyway: what a country
            // remembers about being told it was wrong.
            country.publicGrievance = Clamp(country.publicGrievance + 3f * (1f - effectiveness) * 2f);

            // **Denying something everybody can see makes it worse.** Below a
            // third effective, the attempt itself becomes part of the case.
            bool backfired = effectiveness < 0.33f;
            if (backfired)
            {
                gov.oppositionCase = Clamp(gov.oppositionCase + 9f);
                country.governmentApproval = Clamp(country.governmentApproval - 3f);
            }

            if (country.isPlayer)
                state.AddNotification(
                    backfired ? NotificationClass.Priority : NotificationClass.Advisory,
                    backfired ? "THE DENIAL DID NOT LAND" : "OPPOSITION ANSWERED",
                    backfired
                        ? $"The government has told the country {Describe(gov.oppositionTheme)} is "
                          + "not so. The country can see otherwise, and the argument is stronger "
                          + "for having been denied."
                        : "The opposition's argument has been answered in public and has lost "
                          + "some of its force.",
                    countryId, desk: ReportingDesk.Government);

            // One campaign, one line: a government answering its critics every
            // month was 1,279 identical entries in a thirty-year world.
            string line = $"{country.displayName}'s government moves against its critics.";
            if (!state.ChronicledWithin(countryId, line, 12))
                state.AddChronicle(ChronicleCategory.Political, countryId, line, Publicity.Public);
            return true;
        }

        /// <summary>Player order: XP and initiative on top of the spend.</summary>
        public static bool Confront(GameState state)
        {
            if (!AuthoritySystem.EnsureAuthority(state, Pillar.Government)) return false;
            if (!ConfrontBy(state, state.playerCountryId)) return false;

            ProgressionSystem.AwardXP(state, 14, "Answered the opposition");
            ProgressionSystem.RecordInitiative(state);
            return true;
        }

        // ---------- the monthly tick ----------

        public static void MonthlyUpdate(GameState state)
        {
            foreach (var country in state.countries)
            {
                var gov = country.government;

                float target = CaseTargetFor(state, country, out OppositionTheme theme);

                // The theme only changes when the new grievance is clearly the
                // bigger one. Otherwise a campaign would re-brand itself every
                // month on noise, and no operator could read what the country was
                // angry about for long enough to answer it.
                if (theme != gov.oppositionTheme && target > gov.oppositionCase + 8f)
                    gov.oppositionTheme = theme;

                gov.oppositionCase = Approach(gov.oppositionCase, target,
                    gov.oppositionCase < target ? 0.09f : 0.06f);

                if (gov.oppositionCase >= NoiseFloor) gov.oppositionMonths++;
                else gov.oppositionMonths = 0;

                if (!country.isPlayer) ConsiderAnswer(state, country);
                else Warn(state, country);
            }
        }

        /// <summary>
        /// Tell the operator once, when it becomes a campaign rather than
        /// grumbling — and again if it gets serious. PRIORITY, never FLASH: §28.2
        /// reserves FLASH for a turn that cannot be taken without deciding, and
        /// an opposition can be ignored. Expensively, but it can.
        /// </summary>
        static void Warn(GameState state, CountryState country)
        {
            var gov = country.government;
            if (gov.oppositionMonths == 3)
                state.AddNotification(NotificationClass.Priority, "AN OPPOSITION FORMS",
                    $"A serious argument is being made against this government: "
                    + $"{Describe(gov.oppositionTheme)}. Conceding ground costs something real; "
                    + "denying it works only where the case is mood rather than fact.",
                    country.id, desk: ReportingDesk.Government);
            else if (gov.oppositionMonths == 18 && gov.oppositionCase > 55f)
                state.AddNotification(NotificationClass.Priority, "THE CASE HAS HARDENED",
                    $"Eighteen months of {Describe(gov.oppositionTheme)}, unanswered. It is "
                    + "holding down support in the chamber and it will be waiting at the "
                    + "next election.", country.id, desk: ReportingDesk.Government);
        }

        /// <summary>
        /// How a foreign government answers its own opposition.
        ///
        /// Actor-generic in the way that matters: the same two verbs at the same
        /// prices, chosen by what kind of state it is. A government that already
        /// governs restrictively reaches for the confrontation even where it will
        /// not work — which is not a cheat, it is the characteristic mistake.
        /// </summary>
        static void ConsiderAnswer(GameState state, CountryState country)
        {
            var gov = country.government;
            if (gov.oppositionCase < 45f) return;

            bool coercive = gov.civicPosture == CivicPosture.Restrictive
                            || !gov.IsElective;

            // A government answers with what it can afford (2026-08). A
            // coercive regime whose Political Capital could not cover a
            // confrontation (3) never tried the concession (2) it *could* pay
            // for — so a destitute non-elective state, the case most likely to
            // face an opposition, never answered one at all.
            if (coercive)
            {
                if (!ConfrontBy(state, country.id)) ConcedeBy(state, country.id);
            }
            else if (!ConcedeBy(state, country.id)) ConfrontBy(state, country.id);
        }

        static float Approach(float current, float target, float rate)
            => Clamp(current + (target - current) * rate);

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
