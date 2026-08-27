using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// One country, everything we know about it (GDD §28.1's Deep Terminal tier).
    ///
    /// **Every fact was already in the game and nothing collected them by
    /// subject.** To answer "what is this state actually doing?" an operator
    /// visited INTELLIGENCE for the estimates, networks and armed movements,
    /// MILITARY for the balance of forces, DIPLOMACY for relations, treaties and
    /// the chamber, ECONOMY for trade and sanctions, and CHRONICLE for the
    /// history — and assembled the picture themselves, every time. The GDD
    /// promised three information layers; the shell was one flat rail of tabs,
    /// each organised by *our* pillar rather than by their country.
    ///
    /// The argument for building it is not tidiness. It is that **collection had
    /// no visible payoff**: buying intelligence improved numbers scattered across
    /// five screens, so the reward for a decade of patient network-building was
    /// diffuse and the pillar felt thinner than it measures. This is the one page
    /// where a well-collected state visibly reads differently from an uncollected
    /// one — same layout, same headings, and half of it saying NO REPORTING.
    ///
    /// **It prints no foreign true value.** Everything capability-shaped goes
    /// through `IntelReadout`, personnel through the shared
    /// `IntelReadout.PersonnelAccessOf` gate, and secret facts (who is arming an
    /// insurgency, what somebody is building) appear only where the game has
    /// already made them public. What is *observable* — a treaty signature, a
    /// war, a bloc, an occupation, a censure — is printed plainly, because those
    /// are things anyone with a radio knows.
    /// </summary>
    public class DossierView : TerminalView
    {
        public override string Id => "DOSSIER";
        public override string ShortCode => "DSR";

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        string subjectId;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;
            var player = state.PlayerCountry;
            if (player == null) return;

            Root.Clear();

            if (state.FindCountry(subjectId) == null)
            {
                foreach (var country in state.countries)
                    if (!country.isPlayer) { subjectId = country.id; break; }
            }
            if (subjectId == null) subjectId = player.id;

            BuildSelector(state);

            var subject = state.FindCountry(subjectId);
            if (subject == null) return;

            BuildIdentity(state, subject);
            BuildCapability(state, subject);
            BuildPersonnel(state, subject);
            BuildCommitments(state, subject);
            BuildConduct(state, subject);
            BuildCondition(state, subject);
            BuildBetweenUs(state, player, subject);
            BuildRecord(state, subject);
        }

        void BuildSelector(GameState state)
        {
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("COUNTRY DOSSIER", W);

            var row = MakeRow();
            foreach (var country in state.countries)
            {
                var captured = country.id;
                bool current = captured == subjectId;

                var button = new Button(() => { subjectId = captured; Refresh(); })
                {
                    text = (current ? "► " : "")
                           + (WorldFactory.FindProfile(country.id)?.mapCode ?? country.id)
                };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                row.Add(button);
            }
        }

        // ---------- who they are ----------

        void BuildIdentity(GameState state, CountryState subject)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($" {subject.displayName.ToUpperInvariant()}   "
                          + $"({subject.government.TypeText})");

            // Public by nature: who holds office and what the constitution is are
            // not secrets, whatever our collection looks like.
            sb.AppendLine($" HEAD OF GOVERNMENT: {subject.government.leader.name.ToUpperInvariant()}"
                          + $"   ({subject.government.leader.faction})");
            // A government's temperament is its public reputation — read from
            // how it has behaved, not from a hidden number, so it is not a leak.
            string temperament = AISystem.TemperamentOf(state, subject.id);
            if (!string.IsNullOrEmpty(temperament))
                sb.AppendLine($" TEMPERAMENT: {temperament}   PRIORITY: {subject.government.leader.priority.ToString().ToUpperInvariant()}");
            sb.AppendLine($" WAR RECORD: {subject.WarRecordText}"
                          + (subject.foundedDate.year > 0
                              ? $"   FOUNDED {subject.foundedDate.DisplayString}" : ""));

            if (subject.traits.Count > 0)
            {
                var traits = new StringBuilder();
                foreach (var trait in subject.traits)
                {
                    if (traits.Length > 0) traits.Append(", ");
                    traits.Append(trait.name.ToUpperInvariant());
                }
                sb.AppendLine($" CHARACTER: {traits}");
            }

            AddText().text = sb.ToString();
        }

        // ---------- what we believe they have ----------

        void BuildCapability(GameState state, CountryState subject)
        {
            AddText("terminal-text-bright").text = " WHAT WE BELIEVE THEY HAVE";

            if (subject.id == state.playerCountryId)
            {
                var own = new StringBuilder();
                foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
                    own.AppendLine($"   {pillar.ToString().ToUpperInvariant(),-13} "
                                   + $"{subject.pillars.Get(pillar):F0}");
                AddFigure().text = own.ToString().TrimEnd();
                return;
            }

            var sb = new StringBuilder();
            foreach (IntelDomain domain in System.Enum.GetValues(typeof(IntelDomain)))
                sb.AppendLine($"   {domain.ToString().ToUpperInvariant(),-13} "
                              + IntelReadout.ForDomain(state, subject.id, domain));
            AddText().text = sb.ToString().TrimEnd();

            AddText("terminal-text-dim").text = "   " + IntelReadout.AssessmentLegend;
        }

        // ---------- who runs it ----------

        void BuildPersonnel(GameState state, CountryState subject)
        {
            AddText("terminal-text-bright").text = "\n WHO RUNS IT";

            var access = IntelReadout.PersonnelAccessOf(state, subject.id);
            if (access == IntelReadout.PersonnelAccess.None)
            {
                AddText("terminal-text-dim").text =
                    $"   NO REPORTING. Identifying their ministers needs "
                    + $"{IntelReadout.NameThreshold:F0} penetration.";
                return;
            }

            bool assessed = access == IntelReadout.PersonnelAccess.Assessed;
            int nameWidth = AsciiChart.NameWidth(W - 6, 0.45f);

            var sb = new StringBuilder();
            foreach (var official in subject.cabinet)
                sb.AppendLine($"   {official.office.ToString().ToUpperInvariant(),-13} "
                              + $"{AsciiChart.Cell(official.displayName, nameWidth)}  "
                              + (assessed ? IntelReadout.CompetenceBand(official.competence)
                                          : "UNASSESSED")
                              + (official.recruitedById == state.playerCountryId ? "  ★ OURS" : ""));
            AddFigure().text = sb.ToString().TrimEnd();

            if (!assessed)
                AddText("terminal-text-dim").text =
                    $"   Identities only. Judging them needs {IntelReadout.AssessThreshold:F0} "
                    + "penetration.";
        }

        // ---------- what they have signed, and to whom ----------

        void BuildCommitments(GameState state, CountryState subject)
        {
            AddText("terminal-text-bright").text = "\n WHAT THEY ARE SIGNED TO";

            var sb = new StringBuilder();
            bool any = false;

            var bloc = BlocSystem.BlocOf(state, subject.id);
            if (bloc != null)
            {
                any = true;
                sb.AppendLine($"   {bloc.name}"
                              + (bloc.leaderId == subject.id ? "  (THEY LEAD IT)" : ""));
            }

            foreach (var treaty in state.treaties)
            {
                if (treaty.broken || !treaty.Involves(subject.id)) continue;
                var partner = state.FindCountry(treaty.PartnerOf(subject.id));
                if (partner == null) continue;

                var commitments = new StringBuilder();
                foreach (var commitment in treaty.commitments)
                {
                    if (commitments.Length > 0) commitments.Append(", ");
                    commitments.Append(commitment.ToString().ToUpperInvariant());
                }
                sb.AppendLine($"   {partner.displayName.ToUpperInvariant()}: {commitments}");
                any = true;
            }

            if (CouncilSystem.IsCensured(state, subject.id))
            {
                sb.AppendLine("   CENSURED BY THE CHAMBER");
                any = true;
            }
            if (CouncilSystem.SanctionsMandated(state, subject.id))
            {
                sb.AppendLine("   MEASURES AGAINST THEM ARE AUTHORISED");
                any = true;
            }

            sb.AppendLine(state.council != null && state.council.IsPermanent(subject.id)
                ? "   HOLDS A PERMANENT SEAT — they can block anything"
                : "");

            AddText().text = any ? sb.ToString().TrimEnd()
                : "   NOTHING. They have signed nothing and belong to nothing.";
        }

        // ---------- what they are doing ----------

        void BuildConduct(GameState state, CountryState subject)
        {
            AddText("terminal-text-bright").text = "\n WHAT THEY ARE DOING";

            var sb = new StringBuilder();
            bool any = false;

            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved || !confrontation.Involves(subject.id)) continue;
                var opponent = state.FindCountry(confrontation.OpponentOf(subject.id));
                if (opponent == null) continue;

                sb.AppendLine($"   {(confrontation.initiatorId == subject.id ? "AGAINST" : "DEFENDING AGAINST")}"
                              + $" {opponent.displayName.ToUpperInvariant()}"
                              + $"   {confrontation.escalation.ToString().ToUpperInvariant()}");
                any = true;
            }

            float occupied = TerritorySystem.OccupiedValue(state, subject.id);
            if (occupied > 0f)
            {
                sb.AppendLine($"   OCCUPYING FOREIGN GROUND (STRATEGIC VALUE {occupied:F0})");
                any = true;
            }

            // Only what has actually been attributed. Everything else about a
            // covert programme stays behind the fog where it belongs.
            foreach (var insurgency in state.insurgencies)
            {
                if (!InsurgencySystem.KnownSponsor(state, state.playerCountryId, insurgency)) continue;
                if (insurgency.sponsorId != subject.id) continue;

                var location = state.FindLocation(insurgency.locationId);
                sb.AppendLine($"   ARMING THE MOVEMENT AT "
                              + $"{(location != null ? location.displayName.ToUpperInvariant() : "A PROVINCE")}");
                any = true;
            }

            foreach (var campaign in state.accessions)
            {
                if (campaign.sponsorId != subject.id) continue;
                var target = state.FindCountry(campaign.targetId);
                if (target == null) continue;
                sb.AppendLine($"   COURTING {target.displayName.ToUpperInvariant()} TOWARD UNION");
                any = true;
            }

            AddText().text = any ? sb.ToString().TrimEnd()
                : "   NOTHING WE CAN SEE. That is not the same as nothing.";
        }

        // ---------- how they are holding up ----------

        void BuildCondition(GameState state, CountryState subject)
        {
            AddText("terminal-text-bright").text = "\n THEIR CONDITION";

            var sb = new StringBuilder();
            bool any = false;

            // An armed movement is people in the street with rifles. Everybody
            // knows it is there; who pays for it is the secret.
            foreach (var insurgency in state.insurgencies)
            {
                var location = state.FindLocation(insurgency.locationId);
                if (location == null || location.ownerId != subject.id) continue;

                sb.AppendLine($"   ARMED MOVEMENT AT {location.displayName.ToUpperInvariant()}"
                              + (InsurgencySystem.Denies(state, location)
                                  ? "  (THE GROUND IS PAYING NOBODY)" : ""));
                any = true;
            }

            int senders = 0;
            foreach (var sanction in state.sanctions)
                if (sanction.targetId == subject.id) senders++;
            if (senders > 0)
            {
                sb.AppendLine($"   UNDER MEASURES FROM {senders} STATE(S)");
                any = true;
            }

            if (subject.displacement.hosted >= 3f)
            {
                sb.AppendLine($"   CARRYING PEOPLE DISPLACED FROM ELSEWHERE ({subject.displacement.hosted:F0})");
                any = true;
            }
            if (subject.displacement.displaced >= 3f)
            {
                sb.AppendLine($"   THEIR OWN PEOPLE ARE LEAVING ({subject.displacement.displaced:F0})"
                              + (subject.displacement.bordersClosed ? "   BORDER CLOSED" : ""));
                any = true;
            }
            else if (subject.displacement.bordersClosed)
            {
                sb.AppendLine("   BORDER CLOSED TO ARRIVALS");
                any = true;
            }

            AddText().text = any ? sb.ToString().TrimEnd() : "   NOTHING VISIBLY WRONG.";
        }

        // ---------- what has passed between us ----------

        void BuildBetweenUs(GameState state, CountryState player, CountryState subject)
        {
            if (subject.id == player.id) return;

            AddText("terminal-text-bright").text = "\n WHAT HAS PASSED BETWEEN US";

            var relationship = state.FindRelationship(player.id, subject.id);
            if (relationship == null)
            {
                AddText("terminal-text-dim").text = "   No relationship exists.";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"   RELATIONS {relationship.relations:F0}   TRUST {relationship.trust:F0}"
                          + $"   ALIGNMENT {relationship.strategicAlignment:F0}");
            sb.AppendLine($"   THEY DEPEND ON US {relationship.DependenceOf(subject.id):F0}"
                          + $"   WE DEPEND ON THEM {relationship.DependenceOf(player.id):F0}");
            sb.AppendLine($"   THEY REGARD US AS A THREAT AT "
                          + $"{relationship.ThreatPerceivedBy(subject.id):F0}");
            AddText().text = sb.ToString().TrimEnd();

            if (relationship.memory.Count == 0) return;

            var memory = new StringBuilder();
            int shown = 0;
            for (int i = relationship.memory.Count - 1; i >= 0 && shown < 6; i--, shown++)
                memory.AppendLine("   " + relationship.memory[i]);
            AddText("terminal-text-dim").text = memory.ToString().TrimEnd();
        }

        // ---------- the record ----------

        void BuildRecord(GameState state, CountryState subject)
        {
            AddText("terminal-text-bright").text = "\n THE RECORD";

            var sb = new StringBuilder();
            int shown = 0;

            // Public entries always; our own secrets because they are ours. A
            // dossier must not become a way to read somebody else's covert file.
            for (int i = state.chronicle.Count - 1; i >= 0 && shown < 8; i--)
            {
                var entry = state.chronicle[i];
                if (entry.countryId != subject.id) continue;
                if (entry.publicity != Publicity.Public
                    && entry.countryId != state.playerCountryId) continue;

                sb.AppendLine($"   {entry.date.DisplayString}  {entry.text}");
                shown++;
            }

            AddText().text = shown > 0
                ? sb.ToString().TrimEnd()
                : "   NOTHING RECORDED. Either they have been quiet, or nobody was watching.";
        }
    }
}
