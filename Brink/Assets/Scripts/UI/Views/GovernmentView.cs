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
            sb.AppendLine($" SYSTEM:    {gov.TypeText}");
            sb.AppendLine($" LEADER:    {gov.leader.name}  ({gov.leader.faction})");
            sb.AppendLine($" IN OFFICE: {gov.leader.monthsInOffice} MO");
            sb.AppendLine($" PRIORITY:  {gov.leader.priority.ToString().ToUpperInvariant()}");

            if (gov.IsElective)
                sb.AppendLine($" NEXT ELECTION: {gov.nextElectionDate.DisplayString} " +
                              $"({gov.nextElectionDate.MonthsSince(state.date)} MO)");
            else
                sb.AppendLine(" SUCCESSION: INTERNAL — NO SCHEDULED CONTEST");

            if (gov.emergencyPowers)
                sb.AppendLine($" ** EMERGENCY POWERS IN FORCE — {gov.emergencyPowersMonthsRemaining} MO REMAINING **");

            sb.AppendLine($" ADMINISTRATIONS SERVED: {state.administrationsServed}");
            sb.AppendLine(" You are the persistent strategic operator. Administrations change; you remain.");
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
            emergency.SetEnabled(!gov.emergencyPowers);
            row1.Add(emergency);

            if (gov.AllowsEarlyElection)
                AddButton(row1, $"CALL EARLY ELECTION [{GovernmentSystem.EarlyElectionCost:F0} PC]", "danger",
                    () => { GameController.Instance.CallEarlyElection(); Refresh(); });

            AddButton(row1, $"SECURE COMMAND LOYALTY [{RegimeSystem.SecureLoyaltyCost:F0} PC]", null,
                () => { GameController.Instance.SecureMilitaryLoyalty(); Refresh(); });

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
            patronage.SetEnabled(player.resources.treasury >= GovernmentSystem.PatronageTreasury);
            bargainRow.Add(patronage);

            AddButton(bargainRow, $"PUBLIC INQUIRY [{GovernmentSystem.InquiryCost:F0} PC]", null,
                () => { GameController.Instance.LaunchInquiry(); Refresh(); });

            var groom = new Button(() => { GameController.Instance.GroomSuccessor(); Refresh(); })
            { text = $"PREPARE SUCCESSOR [{GovernmentSystem.GroomSuccessorCost:F0} PC]" };
            groom.AddToClassList("cmd-button");
            groom.SetEnabled(gov.successorReadiness < 99f);
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
                button.SetEnabled(!thin);
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

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);
            return row;
        }

        void AddButton(VisualElement row, string text, string extraClass, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("cmd-button");
            if (extraClass != null) button.AddToClassList(extraClass);
            row.Add(button);
        }
    }
}
