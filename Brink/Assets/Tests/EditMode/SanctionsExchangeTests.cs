using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Brink.Core;
using Brink.Data;
using Brink.UI;
using Brink.UI.Views;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Brink.Tests
{
    /// <summary>
    /// Specific diplomatic leverage, slice 2 (spec 04 §5i): lift OUR sanctions
    /// on a government for one commitment it carries, through the real command
    /// path. Ours only; the concession is priced by what the regime costs them
    /// today; the existing 24-month détente is the only durability the game
    /// promises; declined and invalid offers change nothing.
    /// </summary>
    public class SanctionsExchangeTests
    {
        GameState state;
        GameController gc;
        string target, third;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            gc = GameController.Instance;
            gc.NewGame(4747);
            state = gc.State;
            state.commandPoints.current = 40;
            state.politicalCapital = 20f;
            target = null; third = null;
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (state.FindTreaty(state.playerCountryId, country.id) != null) continue;
                if (state.FindSanction(state.playerCountryId, country.id) != null) continue;
                if (state.FindSanction(country.id, state.playerCountryId) != null) continue;
                if (state.IsAtWar(country.id) && state.ActiveConfrontationFor(country.id)?.Involves(state.playerCountryId) == true) continue;
                if (target == null) target = country.id; else if (third == null) third = country.id;
            }
            Assert.IsNotNull(target); Assert.IsNotNull(third);
            state.trade.RemoveAll(l => l.Involves(state.playerCountryId) && l.Involves(target));
            state.sanctions.RemoveAll(s => s.Involves2(target, third));
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
            TerminalMetrics.ResetForTests();
        }

        static List<TreatyClause> TheyProvide(TreatyCommitment c)
            => new List<TreatyClause> { new TreatyClause { commitment = c, side = ClauseSide.TheyProvide } };

        Sanction Impose(SanctionSeverity severity, int monthsActive = 0)
        {
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, target, severity, "PLAYER"), "fixture: could not impose");
            var s = state.FindSanction(state.playerCountryId, target); s.monthsActive = monthsActive; return s;
        }

        /// <summary>Warm the pair to where the bare ask fails and the ask-plus-relief passes; assert both took.</summary>
        void WarmToTheMargin(TreatyCommitment commitment)
        {
            var r = state.FindRelationship(state.playerCountryId, target);
            r.trust = 55f; r.strategicAlignment = 55f; r.SetThreatPerceivedBy(target, 0f);
            for (float relations = 10f; relations <= 95f; relations += 1f)
            {
                r.relations = relations;
                float plain = DiplomacySystem.TreatyWillingness(state, state.playerCountryId, target, TheyProvide(commitment));
                float lever = DiplomaticLeverage.ReliefOfferWillingness(state, state.playerCountryId, target, commitment);
                if (plain < 50f && lever >= 50f) return;
            }
            Assert.Inconclusive("no relations level separates the plain ask from the relief-backed one");
        }

        [Test]
        public void ReliefIsWhatCarriesTheAsk_AndSeverityAndAdaptationSetItsWorth()
        {
            Impose(SanctionSeverity.Coercive);
            var r = state.FindRelationship(state.playerCountryId, target);
            r.relations = 60f; r.trust = 55f; r.strategicAlignment = 55f; r.SetThreatPerceivedBy(target, 0f);
            float bare = DiplomacySystem.TreatyWillingness(state, state.playerCountryId, target, TheyProvide(TreatyCommitment.Transit));
            float priced = DiplomaticLeverage.ReliefOfferWillingness(state, state.playerCountryId, target, TreatyCommitment.Transit);
            float value = DiplomaticLeverage.SanctionsReliefValue(state, state.playerCountryId, target);
            Assert.AreEqual(1.5f, value, 0.001f, "a fresh Coercive regime is worth its full weight");
            Assert.AreEqual(bare + value * DiplomaticLeverage.WillingnessPerPressurePoint, priced, 0.001f, "priced by treaty willingness plus the live pressure lifted, nothing else");

            // Severity orders the concession; adaptation halves it.
            var s = state.FindSanction(state.playerCountryId, target);
            s.severity = SanctionSeverity.Routine; float routine = DiplomaticLeverage.SanctionsReliefValue(state, state.playerCountryId, target);
            s.severity = SanctionSeverity.Severe; float severe = DiplomaticLeverage.SanctionsReliefValue(state, state.playerCountryId, target);
            s.monthsActive = EconomySystem.SanctionAdaptationMonths; float adapted = DiplomaticLeverage.SanctionsReliefValue(state, state.playerCountryId, target);
            Assert.Less(routine, 1.5f); Assert.Greater(severe, 1.5f); Assert.AreEqual(severe * (1f - EconomySystem.SanctionAdaptationFloor), adapted, 0.001f);
            s.severity = SanctionSeverity.Coercive; s.monthsActive = 0;

            WarmToTheMargin(TreatyCommitment.Transit);
            int cp = state.commandPoints.current;
            Assert.IsFalse(gc.ProposeNegotiatedTreaty(target, TheyProvide(TreatyCommitment.Transit)), "the bare ask must be refused");
            Assert.IsNull(state.FindTreaty(state.playerCountryId, target));
            Assert.AreEqual(cp - DiplomacySystem.TreatyProposalCost, state.commandPoints.current);
            Assert.IsTrue(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.Transit), "with relief on the table the same ask is signed");
        }

        [Test]
        public void AnAcceptedOfferLiftsExactlyOurRegimeAndSignsExactlyTheClause()
        {
            var ours = Impose(SanctionSeverity.Severe);
            // unrelated regimes: theirs on a third state, a third state's on them, ours on a third state
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, third, target, SanctionSeverity.Pressure, "RIVALRY"));
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, third, SanctionSeverity.Routine, "PLAYER"));
            int sanctions = state.sanctions.Count;
            var link = new TradeRelation { countryA = state.playerCountryId, countryB = target, focus = TradeFocus.Energy, volume = 40f, tariff = 20f, embargoed = true }; state.trade.Add(link);
            state.PlayerCountry.resources.energy = 90f;
            WarmToTheMargin(TreatyCommitment.Transit);
            var r = state.FindRelationship(state.playerCountryId, target);
            int cp = state.commandPoints.current, initiative = state.initiativesThisYear, xp = state.strategistXP; int treaties = state.treaties.Count;
            float pressureBefore = EconomySystem.SanctionPressureOn(state, target);

            Assert.IsTrue(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.Transit));

            Assert.IsNull(state.FindSanction(state.playerCountryId, target), "our regime is gone");
            Assert.IsNotNull(state.FindSanction(third, target), "a third state's regime is untouched");
            Assert.IsNotNull(state.FindSanction(state.playerCountryId, third), "our regime on somebody else is untouched");
            Assert.AreEqual(sanctions - 1, state.sanctions.Count);
            Assert.IsFalse(link.embargoed, "the lift clears the embargo, exactly as LIFT SANCTIONS does");
            Assert.AreEqual(pressureBefore - ours.Weight, EconomySystem.SanctionPressureOn(state, target), 0.001f);
            Assert.AreEqual(EconomySystem.DetenteTruceMonths, r.sanctionsTruceMonths, "the existing détente is set");

            var treaty = state.FindTreaty(state.playerCountryId, target);
            Assert.IsNotNull(treaty); Assert.AreEqual(treaties + 1, state.treaties.Count);
            CollectionAssert.AreEqual(new[] { TreatyCommitment.Transit }, treaty.commitments);
            Assert.IsTrue(treaty.Carries(state, target, TreatyCommitment.Transit)); Assert.IsFalse(treaty.Carries(state, state.playerCountryId, TreatyCommitment.Transit));

            Assert.AreEqual(cp - DiplomaticLeverage.OfferCost, state.commandPoints.current);
            Assert.AreEqual(initiative + 1, state.initiativesThisYear, "exactly one Diplomacy initiative");
            Assert.AreEqual(xp + 42, state.strategistXP, "30 for the treaty concluded + 12 for the exchange; the LIFT SANCTIONS verb's 10 is not added");
            Assert.IsTrue(state.notifications.Exists(n => n.title == "SANCTIONS LIFTED FOR A COMMITMENT"));
        }

        [Test]
        public void ADeclinedOfferLeavesTheRegimeAndEveryClauseAlone()
        {
            var ours = Impose(SanctionSeverity.Routine);
            var r = state.FindRelationship(state.playerCountryId, target); r.relations = 5f; r.trust = 5f; r.SetThreatPerceivedBy(target, 90f);
            Assert.IsTrue(DiplomaticLeverage.CanOfferRelief(state, state.playerCountryId, target, TreatyCommitment.MutualDefense, out _));
            int cp = state.commandPoints.current, xp = state.strategistXP, initiative = state.initiativesThisYear, sanctions = state.sanctions.Count, treaties = state.treaties.Count; int truce = r.sanctionsTruceMonths;
            Assert.IsFalse(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.MutualDefense));
            Assert.AreSame(ours, state.FindSanction(state.playerCountryId, target)); Assert.AreEqual(sanctions, state.sanctions.Count); Assert.AreEqual(treaties, state.treaties.Count);
            Assert.AreEqual(truce, r.sanctionsTruceMonths); Assert.AreEqual(cp - DiplomaticLeverage.OfferCost, state.commandPoints.current);
            Assert.AreEqual(xp, state.strategistXP); Assert.AreEqual(initiative, state.initiativesThisYear);
            StringAssert.Contains("Rejected a sanctions-for-commitment offer", r.memory[r.memory.Count - 1]);
            Assert.IsTrue(state.notifications.Exists(n => n.title == "OFFER DECLINED"));
        }

        [Test]
        public void OnlyOurOwnRegimeCanBeTraded()
        {
            void Invalid(string label, string t, TreatyCommitment c, string expectReason)
            {
                Assert.IsFalse(DiplomaticLeverage.CanOfferRelief(state, state.playerCountryId, t, c, out string reason), label);
                StringAssert.Contains(expectReason, reason, label);
                string before = SaveSystem.ToJson(state); int cp = state.commandPoints.current, xp = state.strategistXP, ini = state.initiativesThisYear;
                Assert.IsFalse(gc.OfferSanctionsReliefForCommitment(t, c), label);
                Assert.AreEqual(cp, state.commandPoints.current, label); Assert.AreEqual(xp, state.strategistXP); Assert.AreEqual(ini, state.initiativesThisYear);
                Assert.AreEqual(before, SaveSystem.ToJson(state), label + ": state changed");
            }
            Invalid("no regime", target, TreatyCommitment.Transit, "NOTHING TO LIFT");
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, target, state.playerCountryId, SanctionSeverity.Coercive, "RIVALRY"));
            Invalid("their regime on us", target, TreatyCommitment.Transit, "THEIRS TO LIFT");
            state.sanctions.RemoveAll(s => s.senderId == target);
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, third, target, SanctionSeverity.Severe, "RIVALRY"));
            Invalid("a third state's regime on them", target, TreatyCommitment.Transit, "NOTHING TO LIFT");
            Invalid("self", state.playerCountryId, TreatyCommitment.Transit, "NO SUCH PARTNER");
            Invalid("unknown", "NOWHERE", TreatyCommitment.Transit, "NO SUCH PARTNER");
            Impose(SanctionSeverity.Coercive);
            Invalid("arms control without a regime", target, TreatyCommitment.ArmsControl, "VERIFICATION REGIME");
            // unaffordable
            WarmToTheMargin(TreatyCommitment.Transit);
            state.commandPoints.current = 1; string snap = SaveSystem.ToJson(state);
            Assert.IsFalse(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.Transit)); Assert.AreEqual(snap, SaveSystem.ToJson(state));
        }

        [Test]
        public void AChamberMandatedRegimeIsNotOursToTradeAway()
        {
            Impose(SanctionSeverity.Coercive); WarmToTheMargin(TreatyCommitment.Transit);
            if (state.council == null) state.council = new CouncilState();
            state.council.mandates.Add(new CouncilMandate { subjectId = target, monthsRemaining = 12 });
            Assert.IsTrue(CouncilSystem.SanctionsMandated(state, target), "fixture: the mandate did not take");
            Assert.IsFalse(DiplomaticLeverage.CanOfferRelief(state, state.playerCountryId, target, TreatyCommitment.Transit, out string why));
            StringAssert.Contains("CHAMBER", why);
            string before = SaveSystem.ToJson(state); int cp = state.commandPoints.current;
            Assert.IsFalse(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.Transit));
            Assert.AreEqual(cp, state.commandPoints.current); Assert.AreEqual(before, SaveSystem.ToJson(state));
            Assert.IsNotNull(state.FindSanction(state.playerCountryId, target), "the mandated regime stands");
            state.council.mandates.Clear();
            Assert.IsTrue(DiplomaticLeverage.CanOfferRelief(state, state.playerCountryId, target, TreatyCommitment.Transit, out _), "with the mandate gone the same offer is available");
        }

        [Test]
        public void ExtendingAStandingTreaty_TheyAreCountryA_PreservesUnrelatedClauses()
        {
            Impose(SanctionSeverity.Coercive);
            var mutual = new List<TreatyClause> { new TreatyClause { commitment = TreatyCommitment.NonAggression, side = ClauseSide.Mutual, trigger = TreatyClauseTrigger.ConflictWithCountry, triggerCountryId = third, durationMonths = 36 } };
            Assert.IsTrue(DiplomacySystem.ConcludeNegotiatedTreaty(state, target, state.playerCountryId, mutual));
            var treaty = state.FindTreaty(state.playerCountryId, target); Assert.AreEqual(target, treaty.countryA);
            var na = treaty.clauses[0]; var snap = (na.side, na.trigger, na.triggerCountryId, na.durationMonths, na.effectiveDate.SortKey);
            WarmToTheMargin(TreatyCommitment.Transit);
            int initiative = state.initiativesThisYear, xp = state.strategistXP;
            Assert.IsTrue(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.Transit));
            Assert.AreSame(treaty, state.FindTreaty(state.playerCountryId, target));
            Assert.AreEqual(snap, (na.side, na.trigger, na.triggerCountryId, na.durationMonths, na.effectiveDate.SortKey), "unrelated conditional clause altered");
            var transit = treaty.clauses.Find(c => c.commitment == TreatyCommitment.Transit);
            Assert.AreEqual(ClauseSide.WeProvide, transit.side, "stored relative to countryA, which is them");
            Assert.IsTrue(treaty.Carries(state, target, TreatyCommitment.Transit)); Assert.IsFalse(treaty.Carries(state, state.playerCountryId, TreatyCommitment.Transit));
            Assert.AreEqual(initiative + 1, state.initiativesThisYear); Assert.AreEqual(xp + 32, state.strategistXP, "20 deepening + 12 exchange");
            Assert.IsNull(state.FindSanction(state.playerCountryId, target));
        }

        [Test]
        public void TheDetenteBarsImmediateReimposition_AndAWarVoidsIt()
        {
            Impose(SanctionSeverity.Coercive);
            WarmToTheMargin(TreatyCommitment.Transit);
            Assert.IsTrue(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.Transit));
            var r = state.FindRelationship(state.playerCountryId, target);
            Assert.AreEqual(EconomySystem.DetenteTruceMonths, r.sanctionsTruceMonths);

            // Neither side may reimpose while the détente runs (existing rule in ImposeSanctionsBy).
            Assert.IsFalse(EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, target, SanctionSeverity.Coercive, "PLAYER"));
            Assert.IsFalse(EconomySystem.ImposeSanctionsBy(state, target, state.playerCountryId, SanctionSeverity.Coercive, "RIVALRY"));
            Assert.IsNull(state.FindSanction(state.playerCountryId, target)); Assert.IsNull(state.FindSanction(target, state.playerCountryId));
            // The player's ordinary command also fails (its CP spend is the pre-existing IMPOSE behaviour, not this feature's).
            Assert.IsFalse(gc.ImposeSanctions(target, SanctionSeverity.Coercive));
            Assert.IsNull(state.FindSanction(state.playerCountryId, target));

            // The clause outlives the détente; the détente does not outlive a war.
            var treaty = state.FindTreaty(state.playerCountryId, target);
            var war = ConfrontationSystem.BeginBy(state, target, state.playerCountryId, ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(war, "fixture: could not open the war");
            Assert.AreEqual(0, r.sanctionsTruceMonths, "a declaration of war voids the détente");
            Assert.IsTrue(treaty.Carries(state, target, TreatyCommitment.Transit), "their commitment is a treaty term and stands until the treaty is broken");
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, target, SanctionSeverity.Coercive, "PLAYER"), "after the war voids the détente, measures can return");
        }

        [Test]
        public void ARemovedRegimeCannotBeOfferedAgain_EvenAfterALoad()
        {
            Impose(SanctionSeverity.Coercive);
            WarmToTheMargin(TreatyCommitment.Transit);
            Assert.IsTrue(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.Transit));
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(state.saveVersion, loaded.saveVersion);
            Assert.IsNull(loaded.FindSanction(loaded.playerCountryId, target));
            Assert.AreEqual(EconomySystem.DetenteTruceMonths, loaded.FindRelationship(loaded.playerCountryId, target).sanctionsTruceMonths, "the détente survives a save");
            Assert.IsTrue(loaded.FindTreaty(loaded.playerCountryId, target).Carries(loaded, target, TreatyCommitment.Transit));
            Assert.IsFalse(DiplomaticLeverage.CanOfferRelief(loaded, loaded.playerCountryId, target, TreatyCommitment.IntelligenceSharing, out string why));
            StringAssert.Contains("NOTHING TO LIFT", why);
            int cp = state.commandPoints.current; Assert.IsFalse(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.IntelligenceSharing)); Assert.AreEqual(cp, state.commandPoints.current);
        }

        [Test]
        public void PreviewIsGradedByCollectionAndTheScreenReadsNoForeignFigure()
        {
            Impose(SanctionSeverity.Coercive); WarmToTheMargin(TreatyCommitment.Transit);
            state.estimates.RemoveAll(e => e.observerId == state.playerCountryId && e.targetId == target);
            Assert.AreEqual(TradeOutlook.Uncertain, DiplomaticLeverage.AssessReliefOffer(state, state.playerCountryId, target, TreatyCommitment.Transit));
            state.estimates.Add(new IntelEstimate { observerId = state.playerCountryId, targetId = target, domain = IntelDomain.Political, reportedValue = 50f, margin = 3f, confidence = ConfidenceGrade.Confirmed, asOf = state.date, everCollected = true });
            Assert.AreEqual(TradeOutlook.Likely, DiplomaticLeverage.AssessReliefOffer(state, state.playerCountryId, target, TreatyCommitment.Transit));
            string source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/UI/Views/DiplomacyView.cs"));
            int start = source.IndexOf("void BuildSanctionsExchangeControls", StringComparison.Ordinal);
            int end = source.IndexOf("TreatyClause FindClause", start, StringComparison.Ordinal);
            string body = source.Substring(start, end - start);
            Assert.IsFalse(body.Contains("target.resources"), "the screen reads a foreign country's true resources");
            Assert.IsFalse(body.Contains("ReliefOfferWillingness("), "the screen prints the true reception");
        }

        [Test]
        public void TheScreenNamesWhoseSanctionsDirectionCostAndTruce_AndFitsANarrowPhone()
        {
            Impose(SanctionSeverity.Coercive, 12); WarmToTheMargin(TreatyCommitment.Transit);
            foreach (var (cols, size) in new[] { (34, SizeClass.Compact), (49, SizeClass.Compact), (64, SizeClass.Medium), (104, SizeClass.Large) })
            {
                TerminalMetrics.Update(cols * 8 + 8, 8, 640, size);
                var view = new DiplomacyView();
                typeof(DiplomacyView).GetField("selectedTargetId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, target);
                view.Refresh(); TerminalShellController.ApplyTextPolicy(view.Root, DisplaySettings.ParagraphSpacing, TerminalMetrics.Columns);
                int max = 0; string worst = "", all = "";
                view.Root.Query<Label>().ForEach(l => { if (!TerminalShellController.IsReadout(l) || l.ClassListContains("terminal-figure")) return; all += l.text + "\n"; foreach (var line in (l.text ?? "").Replace("\r", "").Split('\n')) { int len = AsciiChart.VisibleLength(line); if (len > max) { max = len; worst = line; } } });
                Assert.LessOrEqual(max, cols, $"DIPLOMACY at {cols} columns overflows: \"{worst}\"");
                string flat = System.Text.RegularExpressions.Regex.Replace(all, @"\s+", " ");
                StringAssert.Contains("LIFT OUR SANCTIONS FOR A COMMITMENT", flat);
                StringAssert.Contains("OUR COERCIVE MEASURES AGAINST", flat);
                StringAssert.Contains("IN FORCE 12 MONTH(S)", flat);
                StringAssert.Contains("a clause they carry", flat);
                StringAssert.Contains($"A détente then holds for {EconomySystem.DetenteTruceMonths} months", flat);
                StringAssert.Contains("a war between us voids it", flat);
                StringAssert.Contains("their commitment stands regardless", flat);
                var lifts = new List<Button>(); view.Root.Query<Button>().ForEach(b => { if (b.text.StartsWith("LIFT FOR ")) lifts.Add(b); });
                Assert.AreEqual(Enum.GetValues(typeof(TreatyCommitment)).Length, lifts.Count);
                Assert.IsTrue(lifts.Exists(b => b.text == "LIFT FOR TRANSIT [2 CP]" && b.enabledSelf));
                var arms = lifts.Find(b => b.text == "LIFT FOR ARMS CONTROL [2 CP]"); Assert.IsFalse(arms.enabledSelf); StringAssert.Contains("VERIFICATION REGIME", TerminalView.BlockedReason(arms));
                StringAssert.Contains("UNAVAILABLE", all);
                // the supply exchange stays on the same screen (refused under sanctions, and says so)
                StringAssert.Contains("LEVERAGE", flat); StringAssert.Contains("NO COMMERCE UNDER SANCTIONS", flat);
            }
            // their sanctions on us: the block points at SEEK SANCTIONS RELIEF instead
            state.sanctions.RemoveAll(s => s.senderId == state.playerCountryId && s.targetId == target);
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, target, state.playerCountryId, SanctionSeverity.Pressure, "RIVALRY"));
            TerminalMetrics.Update(64 * 8 + 8, 8, 640, SizeClass.Medium);
            var v2 = new DiplomacyView(); typeof(DiplomacyView).GetField("selectedTargetId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(v2, target); v2.Refresh();
            string all2 = ""; v2.Root.Query<Label>().ForEach(l => all2 += l.text + "\n");
            StringAssert.Contains("theirs to lift", all2); var lifts2 = new List<Button>(); v2.Root.Query<Button>().ForEach(b => { if (b.text.StartsWith("LIFT FOR ")) lifts2.Add(b); }); Assert.AreEqual(0, lifts2.Count);
            Assert.IsTrue(ActionCatalog.All(state).Exists(e => e.label == "Lift our sanctions for a commitment"));
        }

        [Test]
        public void TheOfferIsDeterministicAndReadsArePure()
        {
            Impose(SanctionSeverity.Coercive); WarmToTheMargin(TreatyCommitment.Transit);
            string before = SaveSystem.ToJson(state); int seq = state.actionSequence;
            foreach (TreatyCommitment c in Enum.GetValues(typeof(TreatyCommitment))) { DiplomaticLeverage.CanOfferRelief(state, state.playerCountryId, target, c, out _); DiplomaticLeverage.AssessReliefOffer(state, state.playerCountryId, target, c); DiplomaticLeverage.ReliefOfferWillingness(state, state.playerCountryId, target, c); DiplomaticLeverage.SanctionsReliefValue(state, state.playerCountryId, target); DiplomaticLeverage.EmbargoReliefGain(state, state.playerCountryId, target); }
            Assert.AreEqual(before, SaveSystem.ToJson(state)); Assert.AreEqual(seq, state.actionSequence);
            var a = SaveSystem.FromJson(before); var b = SaveSystem.FromJson(before);
            Assert.IsTrue(DiplomaticLeverage.OfferReliefBy(a, a.playerCountryId, target, TreatyCommitment.Transit)); Assert.IsTrue(DiplomaticLeverage.OfferReliefBy(b, b.playerCountryId, target, TreatyCommitment.Transit));
            Assert.AreEqual(SaveSystem.ToJson(a), SaveSystem.ToJson(b));
        }
    }

    static class SanctionTestExtensions
    {
        public static bool Involves2(this Sanction s, string a, string b)
            => (s.senderId == a && s.targetId == b) || (s.senderId == b && s.targetId == a);
    }
}
