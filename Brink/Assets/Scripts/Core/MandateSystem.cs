using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// The posting's mandate and its ten-year review (GDD §25 amendment, 2026-08).
    ///
    /// **Why it exists.** A sixteen-posting playtest had to invent its own
    /// definition of winning — won a war, fired an instrument, graded B — because
    /// the game had none. Annual grades say how a year went; the tenure review
    /// arrives after forty years; the instruments are means. Nothing said what
    /// the operator was *for*. A mandate is the answer a real posting comes with:
    /// three or four claims about the world the government expects to be true in
    /// ten years, written from the country's authored character and vulnerability
    /// (spec 08), and a verdict on them.
    ///
    /// Rules:
    /// - **Never a conquest checklist.** No mandate asks for foreign ground. A
    ///   military posting is asked to hold what it has, win the wars it fights,
    ///   or keep a rival at arm's length — outcomes, not annexations.
    /// - **Measured from the world, not from the path.** Every objective is a
    ///   test against state at review time; how the operator got there is the
    ///   annual evaluation's business.
    /// - **The save always continues.** A verdict is a record and a moment, not
    ///   an ending; the tenure review still comes at forty years.
    /// - **Every posting has one.** Sixteen are authored; the expansion roster
    ///   derives one from its traits so no posting opens without a purpose.
    /// </summary>
    public static class MandateSystem
    {
        public const int ReviewMonths = 120;

        /// <summary>XP for the verdict, by outcome.</summary>
        public const int FulfilledXP = 400, HeldXP = 150;

        /// <summary>Give the posting its brief. Idempotent; safe on an old save.</summary>
        public static void Assign(GameState state)
        {
            if (state.mandate != null) return;
            var player = state.PlayerCountry;
            if (player == null) return;

            var mandate = MandateCatalog.For(state, player);
            mandate.startGdp = player.economy.gdp;
            foreach (var location in state.locations)
                if (location.ownerId == player.id) mandate.startLocationIds.Add(location.id);
            mandate.startGovernmentType = player.government.type.ToString();
            state.mandate = mandate;

            state.AddNotification(NotificationClass.Priority, "MANDATE",
                $"{mandate.title}. {mandate.brief} Review in ten years.", player.id);
            state.AddChronicle(ChronicleCategory.System, player.id,
                $"Posting opened under the mandate: {mandate.title}.");
        }

        public static void MonthlyUpdate(GameState state)
        {
            if (state.mandate == null) Assign(state);
            if (state.mandate == null || state.mandateRecord != null) return;
            if (state.date.MonthsSince(state.startDate) < state.mandate.reviewMonths) return;
            DeliverVerdict(state);
        }

        /// <summary>Whether an objective holds right now.</summary>
        public static bool IsMet(GameState state, MandateObjective objective)
        {
            var player = state.PlayerCountry;
            var mandate = state.mandate;
            switch (objective.kind)
            {
                case MandateObjectiveKind.PillarAtLeast:
                    return player.pillars.Get(ParsePillar(objective.param)) >= objective.threshold;
                case MandateObjectiveKind.TreatiesAtLeast:
                {
                    int held = 0;
                    foreach (var treaty in state.treaties)
                        if (!treaty.broken && treaty.Involves(player.id)) held++;
                    return held >= objective.threshold;
                }
                case MandateObjectiveKind.HoldOriginalGround:
                    foreach (var id in mandate.startLocationIds)
                        if (state.FindLocation(id)?.ownerId != player.id) return false;
                    return true;
                case MandateObjectiveKind.GdpGrowthAtLeast:
                    return mandate.startGdp > 0f
                           && (player.economy.gdp / mandate.startGdp - 1f) * 100f >= objective.threshold;
                case MandateObjectiveKind.StabilityAtLeast: return player.stability >= objective.threshold;
                case MandateObjectiveKind.ApprovalAtLeast: return player.governmentApproval >= objective.threshold;
                case MandateObjectiveKind.UnityAtLeast: return player.nationalUnity >= objective.threshold;
                case MandateObjectiveKind.EnergyAtLeast: return player.resources.energy >= objective.threshold;
                case MandateObjectiveKind.FoodAtLeast: return player.resources.foodSecurity >= objective.threshold;
                case MandateObjectiveKind.MaterialsAtLeast: return player.resources.strategicMaterials >= objective.threshold;
                case MandateObjectiveKind.IndustryAtLeast: return player.resources.industrialCapacity >= objective.threshold;
                case MandateObjectiveKind.WarsWonWithoutLoss:
                    return player.warsWon >= objective.threshold && player.warsLost == 0;
                case MandateObjectiveKind.NoWarLost: return player.warsLost == 0;
                case MandateObjectiveKind.InstrumentUsed:
                    foreach (var record in state.endgameRecords)
                        if (record.actorId == player.id) return true;
                    return false;
                case MandateObjectiveKind.RelationsAtLeast:
                    return (state.FindRelationship(player.id, objective.param)?.relations ?? 0f) >= objective.threshold;
                case MandateObjectiveKind.RelationsAtMost:
                    return (state.FindRelationship(player.id, objective.param)?.relations ?? 100f) <= objective.threshold;
                case MandateObjectiveKind.CapabilitiesAtLeast:
                    return player.technology.capabilities.Count >= objective.threshold;
                // Read through the one fiscal condition, not the balance: with
                // deficit financing the balance is zero every month and this
                // objective was a free tick in five authored mandates.
                case MandateObjectiveKind.Solvent:
                    return FiscalSystem.ConditionOf(state, player) <= FiscalCondition.CashNegativeButCreditworthy;
                case MandateObjectiveKind.ConstitutionalOrderKept:
                    return player.government.type.ToString() == mandate.startGovernmentType;
                default: return false;
            }
        }

        public static int MetCount(GameState state)
        {
            if (state.mandate == null) return 0;
            int met = 0;
            foreach (var objective in state.mandate.objectives)
                if (IsMet(state, objective)) met++;
            return met;
        }

        public static MandateVerdict VerdictFor(int met, int total)
        {
            if (total <= 0) return MandateVerdict.Pending;
            if (met >= total) return MandateVerdict.Fulfilled;
            return met * 2 >= total ? MandateVerdict.Held : MandateVerdict.Failed;
        }

        static void DeliverVerdict(GameState state)
        {
            var player = state.PlayerCountry;
            var mandate = state.mandate;
            int total = mandate.objectives.Count;
            int met = MetCount(state);
            var verdict = VerdictFor(met, total);

            var lines = new List<string>();
            foreach (var objective in mandate.objectives)
                lines.Add((IsMet(state, objective) ? "MET     " : "NOT MET ") + objective.text);

            string headline = verdict == MandateVerdict.Fulfilled
                ? "MANDATE FULFILLED — every undertaking delivered."
                : verdict == MandateVerdict.Held
                    ? "MANDATE HELD — the posting kept its ground."
                    : "MANDATE FAILED — the undertaking was not delivered.";

            state.mandateRecord = new MandateRecord
            {
                date = state.date,
                verdict = verdict,
                met = met,
                total = total,
                summary = $"{headline} {met} of {total} objectives met.\n{string.Join("\n", lines)}"
            };

            int xp = verdict == MandateVerdict.Fulfilled ? FulfilledXP : verdict == MandateVerdict.Held ? HeldXP : 0;
            if (xp > 0) ProgressionSystem.AwardXP(state, xp, "Mandate review");

            // A verdict is read at home. Fulfilment steadies the government;
            // failure is the kind of thing that ends one.
            if (verdict == MandateVerdict.Fulfilled)
            {
                player.governmentApproval = Clamp(player.governmentApproval + 8f);
                player.stability = Clamp(player.stability + 4f);
            }
            else if (verdict == MandateVerdict.Failed)
            {
                player.governmentApproval = Clamp(player.governmentApproval - 8f);
            }

            state.AddNotification(NotificationClass.Flash, "MANDATE REVIEW",
                state.mandateRecord.summary + "\nThe posting continues. The record is closed.", player.id);
            state.AddChronicle(ChronicleCategory.System, player.id,
                $"Ten-year mandate review: {verdict} ({met}/{total}).", Publicity.Public);
            GameLog.Info("MANDATE", $"Verdict {verdict}: {met}/{total}.");
            CareerRecord.Record(state);   // spec 24 §2
        }

        /// <summary>
        /// A new administration may reissue the brief (spec 24 §3): if more than
        /// five years remain to the review, the objectives are replaced by a
        /// fresh derived set weighted to the incoming leader's national priority.
        /// The review date and the original bases are kept — the clock does not
        /// restart because the government changed. Within five years the brief
        /// stands: a government that arrives in year eight inherits it.
        /// </summary>
        public const int ReissueWindowMonths = 60;

        public static bool Reissue(GameState state, string cause)
        {
            var player = state.PlayerCountry;
            var mandate = state.mandate;
            if (player == null || mandate == null || state.mandateRecord != null) return false;
            int elapsed = state.date.MonthsSince(state.startDate);
            if (mandate.reviewMonths - elapsed < ReissueWindowMonths) return false;

            var fresh = MandateCatalog.For(state, player);
            var priority = player.government.leader.priority;
            var lead = priority switch
            {
                NationalPriority.Security => new MandateObjective
                {
                    kind = MandateObjectiveKind.PillarAtLeast, param = "Military",
                    threshold = (float)Math.Min(95, Math.Round(player.pillars.military + 6)),
                    text = $"Military pillar at {Math.Min(95, Math.Round(player.pillars.military + 6))} or better."
                },
                NationalPriority.Prosperity => new MandateObjective
                {
                    kind = MandateObjectiveKind.GdpGrowthAtLeast, threshold = 20f,
                    text = "GDP at least 20 percent larger than when the posting began."
                },
                NationalPriority.Influence => new MandateObjective
                {
                    kind = MandateObjectiveKind.TreatiesAtLeast, threshold = 4f,
                    text = "Hold unbroken treaties with at least four states."
                },
                _ => new MandateObjective
                {
                    kind = MandateObjectiveKind.StabilityAtLeast, threshold = 65f,
                    text = "Stability at 65 or better."
                }
            };

            var objectives = new List<MandateObjective> { lead };
            foreach (var objective in fresh.objectives)
                if (objectives.Count < 4 && objective.kind != lead.kind) objectives.Add(objective);

            mandate.title = fresh.title;
            mandate.brief = fresh.brief + $" Reissued by the new administration ({cause}); the review date stands.";
            mandate.objectives = objectives;

            state.AddNotification(NotificationClass.Priority, "MANDATE REVISED",
                $"The incoming administration has reissued the brief under {priority}: " +
                $"{string.Join(" ", objectives.ConvertAll(o => o.text))} The review date is unchanged.", player.id);
            state.AddChronicle(ChronicleCategory.System, player.id, "Mandate reissued by a new administration.");
            return true;
        }

        /// <summary>Terminal-voice status block for the views.</summary>
        public static string StatusText(GameState state)
        {
            var mandate = state.mandate;
            if (mandate == null) return "NO MANDATE ON FILE.";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(mandate.title.ToUpperInvariant());
            int months = state.mandate.reviewMonths - state.date.MonthsSince(state.startDate);
            if (state.mandateRecord != null)
                sb.AppendLine($"VERDICT: {state.mandateRecord.verdict.ToString().ToUpperInvariant()} ({state.mandateRecord.met}/{state.mandateRecord.total})");
            else
                sb.AppendLine($"REVIEW IN {Math.Max(0, months)} MONTH(S) — {MetCount(state)}/{mandate.objectives.Count} CURRENTLY MET");
            foreach (var objective in mandate.objectives)
                sb.AppendLine((IsMet(state, objective) ? "[MET] " : "[   ] ") + objective.text);
            return sb.ToString().TrimEnd();
        }

        static Pillar ParsePillar(string name)
            => (Pillar)Enum.Parse(typeof(Pillar), string.IsNullOrEmpty(name) ? "Government" : name);

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }

    /// <summary>
    /// The authored mandates, one per Standard-roster posting, each written from
    /// spec 08's character and vulnerability line for that country. The
    /// expansion roster gets one derived from its traits, so every posting opens
    /// with a purpose.
    /// </summary>
    public static class MandateCatalog
    {
        static MandateObjective O(MandateObjectiveKind kind, string text, float threshold = 0f, string param = "")
            => new MandateObjective { kind = kind, text = text, threshold = threshold, param = param };

        public static Mandate For(GameState state, CountryState country)
        {
            switch (country.id)
            {
                case "USA": return new Mandate
                {
                    title = "Keep the order we built",
                    brief = "The system of alliances is the instrument. Hold it together, keep the minerals flowing, and do not let the country come apart at home.",
                    objectives =
                    {
                        O(MandateObjectiveKind.TreatiesAtLeast, "Hold unbroken treaties with at least six states.", 6),
                        O(MandateObjectiveKind.MaterialsAtLeast, "Strategic materials at 50 or better.", 50),
                        O(MandateObjectiveKind.UnityAtLeast, "National unity at 50 or better.", 50),
                        O(MandateObjectiveKind.HoldOriginalGround, "Every location we hold today, we still hold.")
                    }
                };
                case "CHN": return new Mandate
                {
                    title = "Secure the energy the factories run on",
                    brief = "Industrial weight resting on imported fuel is a hostage. Close the gap, keep the economy growing, and let nobody take the lanes.",
                    objectives =
                    {
                        O(MandateObjectiveKind.EnergyAtLeast, "Energy security at 46 or better.", 46),
                        O(MandateObjectiveKind.GdpGrowthAtLeast, "GDP at least 25 percent larger than today.", 25),
                        O(MandateObjectiveKind.HoldOriginalGround, "Every location we hold today, we still hold."),
                        O(MandateObjectiveKind.CapabilitiesAtLeast, "Hold at least three capabilities.", 3)
                    }
                };
                case "RUS": return new Mandate
                {
                    title = "End the isolation without losing the ground",
                    brief = "Energy and materials are plentiful; friends and money are not. Come out of the cold, put the treasury in order, and lose nothing.",
                    objectives =
                    {
                        O(MandateObjectiveKind.TreatiesAtLeast, "Hold unbroken treaties with at least three states.", 3),
                        O(MandateObjectiveKind.PillarAtLeast, "Economy pillar at 55 or better.", 55, "Economy"),
                        O(MandateObjectiveKind.Solvent, "The treasury is not in the red."),
                        O(MandateObjectiveKind.NoWarLost, "No war lost.")
                    }
                };
                case "IND": return new Mandate
                {
                    title = "Grow, and see clearly",
                    brief = "A populous, well-placed state with an energy gap and shallow reporting. Fix the reporting, close the gap, keep growing.",
                    objectives =
                    {
                        O(MandateObjectiveKind.PillarAtLeast, "Intelligence pillar at 65 or better.", 65, "Intelligence"),
                        O(MandateObjectiveKind.EnergyAtLeast, "Energy security at 50 or better.", 50),
                        O(MandateObjectiveKind.GdpGrowthAtLeast, "GDP at least 25 percent larger than today.", 25),
                        O(MandateObjectiveKind.StabilityAtLeast, "Stability at 60 or better.", 60)
                    }
                };
                case "DEU": return new Mandate
                {
                    title = "Power the workshop",
                    brief = "An industrial heavyweight importing its energy and materials. Secure both, keep the industrial base, and stay solvent.",
                    objectives =
                    {
                        O(MandateObjectiveKind.EnergyAtLeast, "Energy security at 45 or better.", 45),
                        O(MandateObjectiveKind.Solvent, "The treasury is not in the red."),
                        O(MandateObjectiveKind.IndustryAtLeast, "Industrial capacity at 75 or better.", 75),
                        O(MandateObjectiveKind.TreatiesAtLeast, "Hold unbroken treaties with at least five states.", 5)
                    }
                };
                case "JPN": return new Mandate
                {
                    title = "An island that cannot feed or fuel itself",
                    brief = "Short of energy, materials, food and people. Buy security with partners and technology, and keep the sea lanes open.",
                    objectives =
                    {
                        O(MandateObjectiveKind.TreatiesAtLeast, "Hold unbroken treaties with at least five states.", 5),
                        O(MandateObjectiveKind.FoodAtLeast, "Food security at 55 or better.", 55),
                        O(MandateObjectiveKind.CapabilitiesAtLeast, "Hold at least five capabilities.", 5),
                        O(MandateObjectiveKind.HoldOriginalGround, "Every location we hold today, we still hold.")
                    }
                };
                case "BRA": return new Mandate
                {
                    title = "Institutions to match the land",
                    brief = "Agricultural and resource depth on thin institutions and a thin treasury. Build the state to the size of the country.",
                    objectives =
                    {
                        O(MandateObjectiveKind.PillarAtLeast, "Government pillar at 65 or better.", 65, "Government"),
                        O(MandateObjectiveKind.PillarAtLeast, "Intelligence pillar at 55 or better.", 55, "Intelligence"),
                        O(MandateObjectiveKind.StabilityAtLeast, "Stability at 65 or better.", 65),
                        O(MandateObjectiveKind.Solvent, "The treasury is not in the red.")
                    }
                };
                case "TUR": return new Mandate
                {
                    title = "Leverage from position",
                    brief = "No energy of its own and a treasury to match; everything comes from where it sits. Keep the straits, keep the partners, keep the peace at home.",
                    objectives =
                    {
                        O(MandateObjectiveKind.HoldOriginalGround, "Every location we hold today, we still hold."),
                        O(MandateObjectiveKind.EnergyAtLeast, "Energy security at 45 or better.", 45),
                        O(MandateObjectiveKind.TreatiesAtLeast, "Hold unbroken treaties with at least four states.", 4),
                        O(MandateObjectiveKind.UnityAtLeast, "National unity at 55 or better.", 55)
                    }
                };
                case "NGA": return new Mandate
                {
                    title = "Hold the country together",
                    brief = "An energy exporter carrying real internal fragility. Stability and unity first; everything else follows or fails with them.",
                    objectives =
                    {
                        O(MandateObjectiveKind.StabilityAtLeast, "Stability at 62 or better.", 62),
                        O(MandateObjectiveKind.UnityAtLeast, "National unity at 58 or better.", 58),
                        O(MandateObjectiveKind.IndustryAtLeast, "Industrial capacity at 52 or better.", 52),
                        O(MandateObjectiveKind.ConstitutionalOrderKept, "The constitutional order stands.")
                    }
                };
                case "SAU": return new Mandate
                {
                    title = "Feed the kingdom",
                    brief = "An energy superpower that cannot feed itself. Turn the energy into food security and industry, and keep the neighbourhood quiet.",
                    objectives =
                    {
                        O(MandateObjectiveKind.FoodAtLeast, "Food security at 25 or better.", 25),
                        O(MandateObjectiveKind.IndustryAtLeast, "Industrial capacity at 45 or better.", 45),
                        O(MandateObjectiveKind.NoWarLost, "No war lost."),
                        O(MandateObjectiveKind.TreatiesAtLeast, "Hold unbroken treaties with at least three states.", 3)
                    }
                };
                case "AUS": return new Mandate
                {
                    title = "A small population on long sea lines",
                    brief = "Surplus materials and food, few people, a thin military. Make the surplus buy partners, and make the lanes somebody else's problem too.",
                    objectives =
                    {
                        O(MandateObjectiveKind.TreatiesAtLeast, "Hold unbroken treaties with at least five states.", 5),
                        O(MandateObjectiveKind.PillarAtLeast, "Military pillar at 55 or better.", 55, "Military"),
                        O(MandateObjectiveKind.IndustryAtLeast, "Industrial capacity at 55 or better.", 55),
                        O(MandateObjectiveKind.HoldOriginalGround, "Every location we hold today, we still hold.")
                    }
                };
                case "KOR": return new Mandate
                {
                    title = "Advanced industry in a dangerous neighbourhood",
                    brief = "No energy, no materials, little food, and a border that has never been quiet. Secure the inputs, lose nothing, and keep the alliances that keep you.",
                    objectives =
                    {
                        O(MandateObjectiveKind.EnergyAtLeast, "Energy security at 40 or better.", 40),
                        O(MandateObjectiveKind.HoldOriginalGround, "Every location we hold today, we still hold."),
                        O(MandateObjectiveKind.NoWarLost, "No war lost."),
                        O(MandateObjectiveKind.TreatiesAtLeast, "Hold unbroken treaties with at least four states.", 4)
                    }
                };
                case "MEX": return new Mandate
                {
                    title = "More than one customer",
                    brief = "Manufacturing tied to a single neighbour's market, and a state that struggles to keep order. Widen the trade, steady the country.",
                    objectives =
                    {
                        O(MandateObjectiveKind.TreatiesAtLeast, "Hold unbroken treaties with at least four states.", 4),
                        O(MandateObjectiveKind.StabilityAtLeast, "Stability at 60 or better.", 60),
                        O(MandateObjectiveKind.PillarAtLeast, "Military pillar at 45 or better.", 45, "Military"),
                        O(MandateObjectiveKind.GdpGrowthAtLeast, "GDP at least 20 percent larger than today.", 20)
                    }
                };
                case "IDN": return new Mandate
                {
                    title = "Astride the lanes",
                    brief = "Everyone's shipping passes through, and the state is fragmented and lightly armed. Unite the archipelago and make it able to say no.",
                    objectives =
                    {
                        O(MandateObjectiveKind.UnityAtLeast, "National unity at 60 or better.", 60),
                        O(MandateObjectiveKind.PillarAtLeast, "Military pillar at 55 or better.", 55, "Military"),
                        O(MandateObjectiveKind.PillarAtLeast, "Intelligence pillar at 55 or better.", 55, "Intelligence"),
                        O(MandateObjectiveKind.HoldOriginalGround, "Every location we hold today, we still hold.")
                    }
                };
                case "POL": return new Mandate
                {
                    title = "The frontline holds",
                    brief = "An exposed industrial buffer that knows it. Deter the neighbour, bind the partners, and never let the treasury decide the war for you.",
                    objectives =
                    {
                        O(MandateObjectiveKind.HoldOriginalGround, "Every location we hold today, we still hold."),
                        O(MandateObjectiveKind.PillarAtLeast, "Military pillar at 60 or better.", 60, "Military"),
                        O(MandateObjectiveKind.TreatiesAtLeast, "Hold unbroken treaties with at least four states.", 4),
                        O(MandateObjectiveKind.Solvent, "The treasury is not in the red.")
                    }
                };
                case "KAZ": return new Mandate
                {
                    title = "A buffer that is nobody's",
                    brief = "Landlocked between two larger neighbours, rich in what they want. Stay on terms with both, stay intact, and build an industry that is your own.",
                    objectives =
                    {
                        O(MandateObjectiveKind.RelationsAtLeast, "Relations with Russia at 55 or better.", 55, "RUS"),
                        O(MandateObjectiveKind.RelationsAtLeast, "Relations with China at 55 or better.", 55, "CHN"),
                        O(MandateObjectiveKind.HoldOriginalGround, "Every location we hold today, we still hold."),
                        O(MandateObjectiveKind.IndustryAtLeast, "Industrial capacity at 60 or better.", 60)
                    }
                };
            }
            return Derived(country);
        }

        /// <summary>
        /// A mandate for a posting nobody has authored one for, built from its
        /// traits (spec 08 §4): what a state of this kind is always asked for.
        /// </summary>
        static Mandate Derived(CountryState country)
        {
            var mandate = new Mandate
            {
                title = $"Serve {country.displayName}",
                brief = "No standing brief was issued for this posting. The government expects what any government expects: keep what we have, stay solvent, and leave the state stronger.",
                objectives =
                {
                    O(MandateObjectiveKind.HoldOriginalGround, "Every location we hold today, we still hold."),
                    O(MandateObjectiveKind.Solvent, "The treasury is not in the red."),
                    O(MandateObjectiveKind.StabilityAtLeast, "Stability at 55 or better.", 55)
                }
            };
            if (NationalTraitCatalog.Has(country, NationalTraitCatalog.Mercantile) || NationalTraitCatalog.Has(country, NationalTraitCatalog.Convening))
                mandate.objectives.Add(O(MandateObjectiveKind.TreatiesAtLeast, "Hold unbroken treaties with at least four states.", 4));
            else if (NationalTraitCatalog.Has(country, NationalTraitCatalog.Industrial) || NationalTraitCatalog.Has(country, NationalTraitCatalog.Technocratic))
                mandate.objectives.Add(O(MandateObjectiveKind.CapabilitiesAtLeast, "Hold at least four capabilities.", 4));
            else if (NationalTraitCatalog.Has(country, NationalTraitCatalog.Martial) || NationalTraitCatalog.Has(country, NationalTraitCatalog.Besieged))
                mandate.objectives.Add(O(MandateObjectiveKind.NoWarLost, "No war lost."));
            else
                mandate.objectives.Add(O(MandateObjectiveKind.GdpGrowthAtLeast, "GDP at least 20 percent larger than today.", 20));
            return mandate;
        }
    }
}
