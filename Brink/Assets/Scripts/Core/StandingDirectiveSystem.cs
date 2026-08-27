using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Optional strategic directives (GDD §29): the government, the Cabinet or
    /// circumstances suggest something worth doing — with a deadline and a
    /// reward — and the operator may take it up or not.
    ///
    /// Rules:
    /// - **Suggested, never imposed.** A directive arrives as PRIORITY traffic
    ///   and sits on STRATEGIST. Ignoring it costs nothing; letting it lapse is
    ///   recorded, not punished. That is what "optional" means (§29).
    /// - **Circumstances pick them.** Each entry in the catalogue has an
    ///   eligibility test on the world, so a directive is always about
    ///   something true right now — energy below the comfort line, a rival at
    ///   alert, a treasury in the red — and its threshold is set from the
    ///   current value, so it is a reach rather than a box already ticked.
    /// - **The same evaluator as the mandate.** The condition is a
    ///   <see cref="MandateObjective"/> judged by <see cref="MandateSystem.IsMet"/>.
    /// - **At most two standing at once**, one new one every few months, so
    ///   they read as a government's agenda rather than a quest log.
    /// </summary>
    public static class StandingDirectiveSystem
    {
        public const int MaxStanding = 2;
        public const int MonthsBetweenOffers = 4;
        public const int DefaultMonths = 24;

        class Template
        {
            public string id, title, source;
            public Func<GameState, bool> eligible;
            public Func<GameState, MandateObjective> objective;
            public int months = DefaultMonths;
            public int reward = 120;
        }

        static MandateObjective O(MandateObjectiveKind kind, string text, float threshold = 0f, string param = "")
            => new MandateObjective { kind = kind, text = text, threshold = threshold, param = param };

        static readonly Template[] Catalogue =
        {
            new Template
            {
                id = "ENERGY_INDEPENDENCE", title = "Reduce energy dependence",
                source = "The Economy desk",
                eligible = s => s.PlayerCountry.resources.energy < 50f,
                objective = s => O(MandateObjectiveKind.EnergyAtLeast,
                    $"Energy security at {Reach(s.PlayerCountry.resources.energy, 12f)} or better.",
                    Reach(s.PlayerCountry.resources.energy, 12f)),
                months = 30
            },
            new Template
            {
                id = "RESTORE_READINESS", title = "Restore readiness",
                source = "The Military desk",
                eligible = s => s.PlayerCountry.military.ground.readiness < 60f && !s.IsAtWar(s.playerCountryId),
                objective = s => O(MandateObjectiveKind.PillarAtLeast,
                    $"Military pillar at {Reach(s.PlayerCountry.pillars.military, 6f)} or better.",
                    Reach(s.PlayerCountry.pillars.military, 6f), "Military"),
                months = 18
            },
            new Template
            {
                id = "SETTLE_THE_FRONTIER", title = "Settle the frontier",
                source = "The Diplomacy desk",
                eligible = s => ColdestRival(s) != null && s.FindRelationship(s.playerCountryId, ColdestRival(s)).relations < 35f
                                && s.ActiveConfrontation == null,
                objective = s => O(MandateObjectiveKind.RelationsAtLeast,
                    $"Relations with {s.FindCountry(ColdestRival(s))?.displayName} at 45 or better.", 45f, ColdestRival(s)),
                months = 24, reward = 160
            },
            new Template
            {
                id = "BALANCE_THE_BOOKS", title = "Balance the books",
                source = "The Treasury",
                eligible = s => s.PlayerCountry.resources.treasury < 0f,
                objective = s => O(MandateObjectiveKind.Solvent, "The treasury is not in the red."),
                months = 18
            },
            new Template
            {
                id = "STEADY_THE_COUNTRY", title = "Steady the country",
                source = "The Government desk",
                eligible = s => s.PlayerCountry.stability < 50f,
                objective = s => O(MandateObjectiveKind.StabilityAtLeast,
                    $"Stability at {Reach(s.PlayerCountry.stability, 12f)} or better.", Reach(s.PlayerCountry.stability, 12f)),
                months = 24
            },
            new Template
            {
                id = "FIND_A_PARTNER", title = "Find a partner",
                source = "The Diplomacy desk",
                eligible = s => TreatiesHeld(s) < 2,
                objective = s => O(MandateObjectiveKind.TreatiesAtLeast,
                    $"Hold unbroken treaties with at least {TreatiesHeld(s) + 2} states.", TreatiesHeld(s) + 2),
                months = 24
            },
            new Template
            {
                id = "BUILD_THE_BASE", title = "Build the industrial base",
                source = "The Economy desk",
                eligible = s => s.PlayerCountry.resources.industrialCapacity < 60f,
                objective = s => O(MandateObjectiveKind.IndustryAtLeast,
                    $"Industrial capacity at {Reach(s.PlayerCountry.resources.industrialCapacity, 8f)} or better.",
                    Reach(s.PlayerCountry.resources.industrialCapacity, 8f)),
                months = 30
            },
            new Template
            {
                id = "A_PROGRAMME", title = "Field a capability",
                source = "The Cabinet",
                eligible = s => s.PlayerCountry.technology.capabilities.Count < 3 && s.PlayerCountry.resources.treasury > 300f,
                objective = s => O(MandateObjectiveKind.CapabilitiesAtLeast,
                    s.PlayerCountry.technology.capabilities.Count == 0
                        ? "Hold at least one capability."
                        : $"Hold at least {s.PlayerCountry.technology.capabilities.Count + 1} capabilities.",
                    s.PlayerCountry.technology.capabilities.Count + 1),
                months = 36, reward = 140
            },
            new Template
            {
                id = "FEED_THE_COUNTRY", title = "Feed the country",
                source = "The Government desk",
                eligible = s => s.PlayerCountry.resources.foodSecurity < 45f,
                objective = s => O(MandateObjectiveKind.FoodAtLeast,
                    $"Food security at {Reach(s.PlayerCountry.resources.foodSecurity, 10f)} or better.",
                    Reach(s.PlayerCountry.resources.foodSecurity, 10f)),
                months = 30
            },
            new Template
            {
                id = "WIN_THE_PUBLIC", title = "Win the public back",
                source = "The Government desk",
                eligible = s => s.PlayerCountry.governmentApproval < 40f,
                objective = s => O(MandateObjectiveKind.ApprovalAtLeast,
                    $"Approval at {Reach(s.PlayerCountry.governmentApproval, 15f)} or better.",
                    Reach(s.PlayerCountry.governmentApproval, 15f)),
                months = 18
            }
        };

        static float Reach(float current, float step) => (float)Math.Min(95, Math.Round(current + step));

        static int TreatiesHeld(GameState state)
        {
            int held = 0;
            foreach (var treaty in state.treaties)
                if (!treaty.broken && treaty.Involves(state.playerCountryId)) held++;
            return held;
        }

        static string ColdestRival(GameState state)
        {
            string worst = null; float low = float.MaxValue;
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var relationship = state.FindRelationship(state.playerCountryId, country.id);
                if (relationship != null && relationship.relations < low) { low = relationship.relations; worst = country.id; }
            }
            return worst;
        }

        public static List<StandingDirective> Standing(GameState state)
        {
            var list = new List<StandingDirective>();
            foreach (var directive in state.standingDirectives)
                if (!directive.completed && !directive.expired) list.Add(directive);
            return list;
        }

        public static void MonthlyUpdate(GameState state)
        {
            var player = state.PlayerCountry;
            if (player == null) return;

            // Judge what stands.
            foreach (var directive in state.standingDirectives)
            {
                if (directive.completed || directive.expired) continue;
                if (MandateSystem.IsMet(state, directive.objective))
                {
                    directive.completed = true;
                    state.directivesCompletedThisYear++;
                    ProgressionSystem.AwardXP(state, directive.rewardXP, "Directive completed");
                    ProgressionSystem.RecordInitiative(state);
                    state.AddNotification(NotificationClass.Priority, "DIRECTIVE COMPLETED",
                        $"{directive.title}: {directive.objective.text} Done. {directive.source} notes it.", player.id);
                    state.AddChronicle(ChronicleCategory.System, player.id, $"Directive completed: {directive.title}.");
                    continue;
                }
                directive.monthsRemaining--;
                if (directive.monthsRemaining <= 0)
                {
                    directive.expired = true;
                    state.AddNotification(NotificationClass.Advisory, "DIRECTIVE LAPSED",
                        $"{directive.title} was not delivered in the time suggested. No cost; the moment has passed.", player.id);
                }
            }

            // Offer one, now and then.
            if (Standing(state).Count >= MaxStanding) return;
            int month = state.date.MonthsSince(state.startDate);
            if (month < 2 || (month - state.lastDirectiveOfferMonth) < MonthsBetweenOffers) return;

            var rng = new Random(unchecked(state.rngSeed * 977 + month * 131));
            var candidates = new List<Template>();
            foreach (var template in Catalogue)
            {
                if (!template.eligible(state)) continue;
                bool recent = false;
                foreach (var existing in state.standingDirectives)
                    if (existing.id == template.id && (!existing.expired && !existing.completed
                                                        || state.date.MonthsSince(existing.issued) < 36)) recent = true;
                if (!recent) candidates.Add(template);
            }
            if (candidates.Count == 0) return;

            var chosen = candidates[rng.Next(candidates.Count)];
            var offered = new StandingDirective
            {
                id = chosen.id,
                title = chosen.title,
                source = chosen.source,
                objective = chosen.objective(state),
                issued = state.date,
                monthsRemaining = chosen.months,
                rewardXP = chosen.reward
            };
            state.standingDirectives.Add(offered);
            state.lastDirectiveOfferMonth = month;

            state.AddNotification(NotificationClass.Priority, "STRATEGIC DIRECTIVE",
                $"{chosen.source} suggests: {offered.title}. {offered.objective.text} " +
                $"Within {offered.monthsRemaining} months, if the office chooses to. Optional; nothing is owed.",
                player.id);
        }

        /// <summary>Terminal-voice block for the STRATEGIST panel.</summary>
        public static string StatusText(GameState state)
        {
            var standing = Standing(state);
            if (standing.Count == 0) return "NO STANDING DIRECTIVES. The desks will suggest one when circumstances warrant.";
            var sb = new System.Text.StringBuilder();
            foreach (var directive in standing)
            {
                sb.AppendLine($"{directive.title.ToUpperInvariant()}  — {directive.source}, {directive.monthsRemaining} month(s) left, +{directive.rewardXP} XP");
                sb.AppendLine((MandateSystem.IsMet(state, directive.objective) ? "  [MET] " : "  [   ] ") + directive.objective.text);
            }
            int done = 0; foreach (var d in state.standingDirectives) if (d.completed) done++;
            sb.Append($"COMPLETED TO DATE: {done}");
            return sb.ToString();
        }
    }
}
