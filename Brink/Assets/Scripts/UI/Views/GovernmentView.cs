using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Government command (GDD Phase 8, §13): the political condition of the
    /// state, the administration you currently serve, and the Political Capital
    /// instruments — messaging, appointments, reform and emergency authority.
    /// </summary>
    public class GovernmentView : TerminalView
    {
        public override string Id => "GOVERNMENT";
        public override string ShortCode => "GOV";

        /// <summary>Orders here are Government pillar orders — see TerminalView.GateOnAuthority.</summary>
        protected override Pillar? CommandPillar => Pillar.Government;

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;
            var player = state.PlayerCountry;
            if (player == null) return;

            Root.Clear();
            AddAuthorityBadge(state, Pillar.Government);
            AddPillarArt(state, Pillar.Government);
            AddCabinetAdvice(state, Pillar.Government);
            BuildAdministration(state, player);
            BuildPoliticalCondition(state, player);
            BuildInstruments(state, player);
            BuildForeignGovernments(state);
        }

        void BuildAdministration(GameState state, CountryState player)
        {
            var gov = player.government;
            var text = AddText("terminal-text-bright");
            var sb = new StringBuilder();

            sb.AppendLine(AsciiChart.BoxHeader("ADMINISTRATION", W));

            // Who is who, stated before anything else on the screen.
            //
            // The operator is not the head of government (GDD §13, §203) and the
            // panel used to say so in one soft sentence twelve lines down, under
            // the election date. Read from play as a contradiction — "someone
            // else was elected but I still run the country" — which is the
            // premise of the whole game being mistaken for a bug.
            sb.AppendLine(" THIS OFFICE:   PERMANENT STRATEGIC OPERATOR — not elected, not replaced");
            sb.AppendLine($"                {state.administrationsServed} administration(s) served "
                          + "under this post");
            sb.AppendLine();
            sb.AppendLine($" SYSTEM:    {gov.TypeText}");
            sb.AppendLine($" HEAD OF GOVERNMENT: {gov.leader.name}  ({gov.leader.faction})");
            sb.AppendLine($" IN OFFICE: {gov.leader.monthsInOffice} MO");
            sb.AppendLine($" THEIR PRIORITY: {gov.leader.priority.ToString().ToUpperInvariant()}"
                          + "  — redirects every official you have delegated to");

            if (gov.IsElective)
                sb.AppendLine($" NEXT ELECTION: {gov.nextElectionDate.DisplayString} " +
                              $"({gov.nextElectionDate.MonthsSince(state.date)} MO)");
            else
                sb.AppendLine(" SUCCESSION: INTERNAL — NO SCHEDULED CONTEST");

            // Faction arithmetic (spec 05 §2b) — say it in words where it bites.
            // A penalty the operator cannot see is indistinguishable from a
            // broken readout.
            float factionShift = GovernmentSystem.FactionSupportShift(gov)
                                 + GovernmentSystem.FactionCohesionShift(gov);
            if (factionShift < -1f)
                sb.AppendLine(gov.IsElective
                    ? $" CHAMBER: NOT YET THEIRS — new governing faction, support runs {factionShift:F0} until it consolidates"
                    : $" STANDING: UNCONSOLIDATED — this leadership's backing runs {factionShift:F0} while it settles");
            else if (factionShift > 1f)
                sb.AppendLine($" STANDING: THE ARRANGEMENT HOLDS — backing runs +{factionShift:F0} on loyalty");

            if (gov.AllowsEarlyElection && gov.legislativeSupport < GovernmentSystem.ConfidenceThreshold)
                sb.AppendLine(" ** CONFIDENCE AT RISK — a chamber this hostile can bring the government down **");

            if (gov.emergencyPowers)
                sb.AppendLine($" ** EMERGENCY POWERS IN FORCE — {gov.emergencyPowersMonthsRemaining} MO REMAINING **");

            sb.AppendLine();
            sb.AppendLine(" A change of administration reshuffles the cabinet, resets the national");
            sb.AppendLine(" priority and withdraws any authority this office was granted rather than");
            sb.AppendLine(" holds in its own right. It does not end your post.");
            text.text = sb.ToString();
        }

        void BuildPoliticalCondition(GameState state, CountryState player)
        {
            var gov = player.government;
            var text = AddText();
            var sb = new StringBuilder();

            sb.AppendLine(AsciiChart.BoxHeader("POLITICAL CONDITION", W));
            sb.AppendLine("  " + AsciiChart.LabeledBar("APPROVAL", player.governmentApproval, 100, 14, 20));
            sb.AppendLine("  " + AsciiChart.LabeledBar("STABILITY", player.stability, 100, 14, 20));
            sb.AppendLine("  " + AsciiChart.LabeledBar("UNITY", player.nationalUnity, 100, 14, 20));
            sb.AppendLine("  " + AsciiChart.LabeledBar("WAR SUPPORT", player.warSupport, 100, 14, 20));
            sb.AppendLine("  " + AsciiChart.LabeledBar("WAR EXHAUSTION", player.warExhaustion, 100, 14, 20));
            sb.AppendLine();

            // The social layer (GDD §12). Shown together and above the
            // institutions, because this is what the institutions are reacting
            // to — approval is downstream of how people actually live.
            sb.AppendLine("  " + AsciiChart.LabeledBar("LIVING STANDARDS", player.livingStandards, 100, 14, 20));
            sb.AppendLine("  " + AsciiChart.LabeledBar("SOCIAL UNREST", player.socialUnrest, 100, 14, 20));
            sb.AppendLine("  " + AsciiChart.LabeledBar("PUBLIC MEMORY", player.publicGrievance, 100, 14, 20));
            sb.AppendLine();
            sb.AppendLine(gov.IsElective
                ? "  " + AsciiChart.LabeledBar("LEGISLATURE", gov.legislativeSupport, 100, 14, 20)
                : "  " + AsciiChart.LabeledBar("ELITE COHESION", gov.eliteCohesion, 100, 14, 20));
            sb.AppendLine();
            sb.AppendLine("  " + AsciiChart.LabeledBar("CMD LOYALTY", gov.militaryLoyalty, 100, 14, 20));
            sb.AppendLine();

            // **Which lever moves which bar.** Reported from play as "is there any
            // way I can boost these things?" — asked in front of a screen that had
            // nine verbs on it. The bars and the buttons were both present and
            // nothing connected them, so the pillar read as a readout.
            //
            // The last two lines are the honest part: living standards and public
            // memory are consequences, not dials, and saying so is better than
            // leaving the operator hunting for a button that does not exist.
            sb.AppendLine(AsciiChart.BoxHeader("WHAT MOVES THESE", W));
            Lever(sb, "APPROVAL", "messaging, posture, standards", "public messaging, civic posture, living standards");
            Lever(sb, "STABILITY", "posture, reform, approval", "civic posture, institutional reform, approval");
            Lever(sb, "UNITY", "posture, stability, messaging", "civic posture, stability, public messaging");
            Lever(sb, gov.IsElective ? "LEGISLATURE" : "ELITE COHESION",
                "support, patronage — fade", "build support, patronage — both fade if not renewed");
            Lever(sb, "SOCIAL UNREST", "civic posture", "civic posture; falls as unity and standards rise");
            Lever(sb, "CMD LOYALTY", "secure loyalty, spending", "secure command loyalty, military spending");
            sb.AppendLine();
            Lever(sb, "LIVING STANDARDS", "no lever — ECONOMY", "no direct lever — earned in the ECONOMY pillar");
            Lever(sb, "PUBLIC MEMORY", "no lever — conditions", "no direct lever — decays as conditions improve");

            // Our own services report on domestic plots — if they are competent.
            bool detected = player.counterIntel.counterIntelligence >= 35f && gov.conspiracyLevel >= 55f;
            sb.AppendLine(detected
                ? "  " + AsciiChart.LabeledBar("CONSPIRACY", gov.conspiracyLevel, 100, 14, 20) + "  ** DETECTED **"
                : "  CONSPIRACY ..... NO INDICATIONS REPORTED");

            if (gov.inCivilConflict)
                sb.AppendLine($"  ** CIVIL CONFLICT ACTIVE — {gov.civilConflictMonthsRemaining} MO REMAINING **");
            if (gov.coupsExperienced > 0)
                sb.AppendLine($"  POWER SEIZED BY FORCE: {gov.coupsExperienced} TIME(S) IN THIS SAVE");

            sb.AppendLine();
            // **Show the income, not just the balance.**
            //
            // Political Capital is earned by approval, institutional quality and
            // the backing of whichever body actually sustains this government —
            // so how much room the operator has to act next month is a direct
            // consequence of how they have governed. That loop is the whole
            // argument for spending on the pillar's own condition, and it was
            // invisible: the screen showed a number that went up by an unexplained
            // amount.
            float income = GovernmentSystem.PoliticalCapitalIncomeFor(player)
                           + ProgressionSystem.EffectValue(state, SkillEffect.PoliticalOperator);

            sb.AppendLine($"  POLITICAL CAPITAL: {state.politicalCapital:F1} / {GameState.PoliticalCapitalCap:F0}"
                          + $"   (+{income:F1} A MONTH)");
            sb.AppendLine($"    Earned by approval, institutions and "
                          + $"{(player.government.IsElective ? "the chamber" : "elite")} backing. "
                          + "Govern well and there is more of it.");
            text.text = sb.ToString();

            // On demand, never permanent (spec 26 §6). Every bar above this row
            // can be asked about; nothing is explained until it is.
            AddWhyPanel(state,
                CausalMetric.GovernmentApproval,
                CausalMetric.SocialUnrest,
                CausalMetric.LivingStandards,
                CausalMetric.PublicGrievance,
                CausalMetric.WarExhaustion);
        }

        void BuildInstruments(GameState state, CountryState player)
        {
            var gov = player.government;
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("POLITICAL INSTRUMENTS", W);

            var row1 = MakeRow();
            AddButton(row1, $"PUBLIC MESSAGING [{GovernmentSystem.PublicMessagingCost:F0} PC]", "primary",
                () => { GameController.Instance.PublicMessaging(); Refresh(); });
            AddButton(row1, $"INSTITUTIONAL REFORM [{GovernmentSystem.InstitutionalReformCost:F0} PC]", null,
                () => { GameController.Instance.InstitutionalReform(); Refresh(); });

            float emergencyCost = GovernmentSystem.EmergencyPowersCost * (gov.IsElective ? 1.4f : 0.7f);
            var emergency = new Button(() => { GameController.Instance.DeclareEmergencyPowers(); Refresh(); })
            { text = $"EMERGENCY POWERS [{emergencyCost:F0} PC]" };
            emergency.AddToClassList("cmd-button");
            emergency.AddToClassList("danger");
            if (gov.emergencyPowers)
                Block(emergency, "EMERGENCY AUTHORITY IS ALREADY IN FORCE.");
            row1.Add(emergency);

            if (gov.AllowsEarlyElection)
                AddButton(row1, $"CALL EARLY ELECTION [{GovernmentSystem.EarlyElectionCost:F0} PC]", "danger",
                    () => { GameController.Instance.CallEarlyElection(); Refresh(); });

            AddButton(row1, $"SECURE COMMAND LOYALTY [{RegimeSystem.SecureLoyaltyCost:F0} PC]", null,
                () => { GameController.Instance.SecureMilitaryLoyalty(); Refresh(); });

            BuildOppositionControls(state, player, gov);
            BuildDisplacementControls(state, player);

            // The bargaining instruments: repeatable, cheap, and the pillar's
            // day-to-day work. Two roads to the same destination — one spends
            // the operator's standing, the other spends the treasury.
            AddText().text = gov.IsElective
                ? "\n POLITICAL BARGAINING — support in the chamber, and what it costs to hold"
                : "\n POLITICAL BARGAINING — accommodation with the elite, and what it costs to hold";
            var bargainRow = MakeRow();
            AddButton(bargainRow,
                $"{(gov.IsElective ? "BARGAIN WITH CHAMBER" : "ACCOMMODATE ELITE")} " +
                $"[{GovernmentSystem.BuildSupportCost:F0} PC]", "primary",
                () => { GameController.Instance.BuildPoliticalSupport(); Refresh(); });

            var patronage = new Button(() => { GameController.Instance.DistributePatronage(); Refresh(); })
            { text = $"DISTRIBUTE PATRONAGE [{GovernmentSystem.PatronageCost:F0} PC]" };
            patronage.AddToClassList("cmd-button");
            if (player.resources.treasury < GovernmentSystem.PatronageTreasury)
                Block(patronage, $"TREASURY {player.resources.treasury:F0} — PATRONAGE COSTS "
                                 + $"{GovernmentSystem.PatronageTreasury:F0}.");
            bargainRow.Add(patronage);

            AddButton(bargainRow, $"PUBLIC INQUIRY [{GovernmentSystem.InquiryCost:F0} PC]", null,
                () => { GameController.Instance.LaunchInquiry(); Refresh(); });

            var groom = new Button(() => { GameController.Instance.GroomSuccessor(); Refresh(); })
            { text = $"PREPARE SUCCESSOR [{GovernmentSystem.GroomSuccessorCost:F0} PC]" };
            groom.AddToClassList("cmd-button");
            if (gov.successorReadiness >= 99f)
                Block(groom, "THE SUCCESSOR IS AS PREPARED AS WE CAN MAKE THEM.");
            bargainRow.Add(groom);

            AddText("terminal-text-dim").text =
                $"   BOUGHT SUPPORT {gov.brokeredSupport:F0}  (decays; must be maintained)" +
                $"   SUCCESSION READY {gov.successorReadiness:F0}%" +
                $"   {(player.resources.treasury >= GovernmentSystem.PatronageTreasury ? "" : "TREASURY TOO THIN FOR PATRONAGE")}";

            // The pillar's standing choice. Costs are stated because the
            // trade-off *is* the decision, and it differs by government type.
            AddText().text = "\n CIVIC POSTURE — how the state holds its own society";
            var postureRow = MakeRow();
            foreach (CivicPosture posture in System.Enum.GetValues(typeof(CivicPosture)))
            {
                var captured = posture;
                bool current = gov.civicPosture == posture;
                var button = new Button(() => { GameController.Instance.SetCivicPosture(captured); Refresh(); })
                {
                    text = (current ? "► " : "") + GovernmentSystem.PostureText(posture)
                           + (current ? "" : $" [{GovernmentSystem.CivicPostureCost:F0} PC]")
                };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                button.SetEnabled(!current);
                postureRow.Add(button);
            }

            var postureBrief = AddText("terminal-text-dim");
            postureBrief.text =
                $"   ORDER {GovernmentSystem.StabilityShiftFor(gov):+0;-0;0}" +
                $"   UNITY {GovernmentSystem.UnityShiftFor(gov):+0;-0;0}" +
                $"   APPROVAL {GovernmentSystem.ApprovalShiftFor(gov):+0;-0;0}" +
                $"   PLOTS ×{GovernmentSystem.ConspiracyRateFor(gov):F2}" +
                (gov.civicPosture == CivicPosture.Restrictive
                    ? $"\n   Holding this costs {GovernmentSystem.RestrictiveUpkeep:F2} PC every month."
                    : "");

            AddText().text = "\n NATIONAL PRIORITY — redirects every autonomous official";
            var priorityRow = MakeRow();
            foreach (NationalPriority priority in System.Enum.GetValues(typeof(NationalPriority)))
            {
                var captured = priority;
                bool current = gov.leader.priority == priority;
                var button = new Button(() => { GameController.Instance.SetNationalPriority(captured); Refresh(); })
                {
                    text = (current ? "► " : "") + priority.ToString().ToUpperInvariant()
                           + (current ? "" : $" [{GovernmentSystem.NationalPriorityCost:F0} PC]")
                };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                button.SetEnabled(!current);
                priorityRow.Add(button);
            }

            BuildAuthorityPurchase(state, player, gov);

            float dismissCost = GovernmentSystem.DismissOfficialCost * (gov.IsElective ? 1.25f : 0.85f);
            AddText().text = $"\n DISMISS AND REPLACE OFFICIAL [{dismissCost:F0} PC]" +
                             (gov.IsElective ? " — also costs legislative goodwill" : "");
            var dismissRow = MakeRow();
            foreach (var official in state.cabinet)
            {
                var captured = official;
                AddButton(dismissRow, captured.office.ToString().ToUpperInvariant(), "danger",
                    () => { GameController.Instance.DismissOfficial(captured.office); Refresh(); });
            }
        }

        /// <summary>
        /// The one large purchase in the Political Capital economy.
        ///
        /// Only pillars the operator cannot already command directly are offered
        /// — buying authority you already hold would be a button that takes 12 PC
        /// and does nothing, which is worse than no button.
        /// </summary>
        void BuildAuthorityPurchase(GameState state, CountryState player, GovernmentState gov)
        {
            var buyable = new System.Collections.Generic.List<Pillar>();
            foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
            {
                // Read the structural table, not the current level: emergency
                // powers make everything Direct for six months and would
                // otherwise hide this entire control while they are in force.
                if ((gov.authorityUpgradeMask & (1 << (int)pillar)) != 0) continue;
                if (!gov.emergencyPowers
                    && AuthoritySystem.AuthorityOver(state, pillar) == AuthoritySystem.AuthorityLevel.Direct)
                    continue;
                buyable.Add(pillar);
            }

            if (buyable.Count == 0)
            {
                AddText("terminal-text-dim").text =
                    "\n CONSTITUTIONAL AUTHORITY — every pillar is already ours to direct.";
                return;
            }

            AddText().text =
                $"\n CONSOLIDATE AUTHORITY [{GovernmentSystem.ConsolidateAuthorityCost:F0} PC] — permanent, " +
                "one pillar at a time";
            AddText("terminal-text-dim").text =
                "   Unlike emergency powers this does not lapse and costs nothing to hold. " +
                "The institutions that gave it up do not forget.";

            bool thin = gov.IsElective && gov.legislativeSupport < 45f;
            if (thin)
                AddText("sig-hostile").text =
                    "   THE CHAMBER WILL NOT CONSIDER IT — legislative support below 45. Bargain first.";

            var row = MakeRow();
            foreach (var pillar in buyable)
            {
                var captured = pillar;
                var button = new Button(() => { GameController.Instance.ConsolidateAuthority(captured); Refresh(); })
                { text = captured.ToString().ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (thin) Block(button, "LEGISLATIVE SUPPORT BELOW 45 — BARGAIN WITH THE CHAMBER FIRST.");
                row.Add(button);
            }
        }

        void BuildForeignGovernments(GameState state)
        {
            var text = AddText("terminal-text-dim");
            var sb = new StringBuilder();
            sb.AppendLine("\n" + AsciiChart.BoxHeader("FOREIGN GOVERNMENTS (PUBLIC RECORD)", W));
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var gov = country.government;
                sb.AppendLine($" {country.displayName.ToUpperInvariant(),-16} {gov.TypeText}");
                sb.AppendLine($"   LEADER {gov.leader.name}  ({gov.leader.monthsInOffice} MO)   " +
                              $"PRIORITY {gov.leader.priority.ToString().ToUpperInvariant()}");
                sb.AppendLine($"   STABILITY (EST) {IntelReadout.ForDomain(state, country.id, IntelDomain.Political)}");
            }
            text.text = sb.ToString();
        }

        /// <summary>
        /// One "stat — what moves it" row, sized to the panel.
        ///
        /// Written as a helper rather than as literal strings because a fixed
        /// 76-column line is the exact thing that runs off a phone: the shell
        /// hard-wraps `terminal-text`, and a wrapped line in a two-column block
        /// puts the continuation under the label and the alignment is gone.
        /// The short form is chosen to fit the narrowest panel the game supports.
        /// </summary>
        static void Lever(StringBuilder sb, string stat, string shortForm, string longForm)
        {
            int labelWidth = AsciiChart.NameWidth(W, 0.30f);
            string detail = W >= Breakpoints.MediumMinColumns ? longForm : shortForm;

            sb.Append("  ").Append(AsciiChart.Cell(stat, labelWidth)).Append("  ");
            sb.AppendLine(AsciiChart.Cell(detail, System.Math.Max(8, W - labelWidth - 5)));
        }

        /// <summary>
        /// The people who want this government out (GDD §13).
        ///
        /// The theme is printed in words rather than as a number because the
        /// theme is the decision: it decides which of the two answers works, and
        /// an operator who reaches for the wrong one makes the case stronger.
        /// The panel says so outright rather than making that a thing you learn
        /// by losing an election.
        /// </summary>
        void BuildOppositionControls(GameState state, CountryState player, GovernmentState gov)
        {
            AddText().text = "\n THE OPPOSITION";

            if (gov.oppositionCase < OppositionSystem.NoiseFloor)
            {
                AddText("terminal-text-dim").text =
                    "   Nobody is making a serious case against this government. That is a "
                    + "condition, not a fact — it is built out of the record, and the record "
                    + "is still being written.";
                return;
            }

            float effectiveness = OppositionSystem.ConfrontationEffectiveness(gov.oppositionTheme);

            var readout = AddText("sig-advice");
            readout.text =
                $"   THEY ARE CAMPAIGNING {OppositionSystem.Describe(gov.oppositionTheme).ToUpperInvariant()}"
                + $"\n   CASE {gov.oppositionCase:F0}   STANDING {gov.oppositionMonths} MO"
                + $"   HOLDING DOWN SUPPORT BY {OppositionSystem.SupportDrag(gov):F0}"
                + "\n   " + (effectiveness < 0.33f
                    ? "This is not an argument that can be denied. The country can see it."
                    : effectiveness < 0.6f
                        ? "Denying it works, partly. Conceding works, and costs."
                        : "This is mood rather than fact, and mood can be answered in public.");

            var row = MakeRow();
            AddButton(row, $"CONCEDE GROUND [{OppositionSystem.ConcedeCost:F0} PC]", "primary",
                () => { GameController.Instance.ConcedeToOpposition(); Refresh(); });
            AddButton(row, $"CONFRONT THEM [{OppositionSystem.ConfrontCost:F0} PC]",
                effectiveness < 0.33f ? "danger" : null,
                () => { GameController.Instance.ConfrontOpposition(); Refresh(); });
        }

        /// <summary>
        /// People arriving, and people leaving (GDD §12, §27).
        ///
        /// On the GOVERNMENT screen rather than DIPLOMACY because the decision it
        /// presents is a domestic one — it is paid for in money and in argument at
        /// home — even though everything that produced it happened somewhere else.
        /// </summary>
        void BuildDisplacementControls(GameState state, CountryState player)
        {
            var displacement = player.displacement;
            if (displacement.hosted < 0.5f && displacement.displaced < 0.5f
                && !displacement.bordersClosed)
                return;

            AddText().text = "\n DISPLACEMENT";

            var readout = AddText("terminal-text-dim");
            readout.text =
                $"   HOSTING {displacement.hosted:F0}   OUR OWN DISPLACED {displacement.displaced:F0}"
                + $"   BORDER {(displacement.bordersClosed ? "CLOSED" : "OPEN")}"
                + (displacement.hosted >= 0.5f
                    ? $"\n   COSTING {displacement.hosted * DisplacementSystem.HostingCostPerPoint:F0} A MONTH"
                      + $", AND {DisplacementSystem.StandardsDrag(player):F0} OFF LIVING STANDARDS"
                    : "")
                + (displacement.bordersClosed
                    ? $"\n   SHUT {displacement.monthsClosed} MO. The pressure has not gone away; "
                      + "it is on the other side of the line."
                    : "");

            var row = MakeRow();
            bool closed = displacement.bordersClosed;
            AddButton(row,
                (closed ? "OPEN THE BORDER" : "CLOSE THE BORDER")
                + $" [{DisplacementSystem.BorderPolicyCost:F0} PC]",
                closed ? "primary" : "danger",
                () => { GameController.Instance.SetBorderPolicy(!closed); Refresh(); });
        }

    }
}
