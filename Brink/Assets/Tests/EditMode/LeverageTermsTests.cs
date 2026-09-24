using System;
using System.Collections.Generic;
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
    /// Roadmap #21, final core slice (spec 04 §5k): every leverage exchange can
    /// ask for a commitment bounded by the existing conditional-agreement
    /// model — a named-conflict trigger, a supported term, or both. One
    /// requested clause is carried through gate, pricing and record; the
    /// concession follows its own rules and is never reversed by the
    /// commitment lapsing; an expired promise renews on its recorded terms.
    /// </summary>
    public class LeverageTermsTests
    {
        GameState state;
        GameController gc;
        string target, third, parent, successor;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            gc = GameController.Instance;
            gc.NewGame(4747);
            state = gc.State;
            state.commandPoints.current = 40;
            state.politicalCapital = 20f;
            target = null; third = null; parent = null;
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (state.FindTreaty(state.playerCountryId, country.id) != null) continue;
                if (state.FindSanction(state.playerCountryId, country.id) != null || state.FindSanction(country.id, state.playerCountryId) != null) continue;
                if (state.IsAtWar(country.id)) continue;
                if (target == null) target = country.id; else if (third == null) third = country.id; else if (parent == null) parent = country.id;
            }
            Assert.IsNotNull(target); Assert.IsNotNull(third); Assert.IsNotNull(parent);
            state.trade.RemoveAll(l => l.Involves(state.playerCountryId) && l.Involves(target));
            state.trade.RemoveAll(l => l.Involves(target) && l.focus == TradeFocus.Energy);
            state.PlayerCountry.resources.energy = 90f;
            state.FindCountry(target).resources.energyEndowment = 20f;
            var net = state.FindNetwork(state.playerCountryId, target); if (net != null) net.penetration = 0f;
            successor = PlantABreakaway(parent).id;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
            TerminalMetrics.ResetForTests();
        }

        CountryState PlantABreakaway(string parentId)
        {
            var p = state.FindCountry(parentId);
            var s = new CountryState { id = parentId + "_S", displayName = p.displayName + " (Breakaway)", foundedDate = state.date.NextMonth(), stability = 38f, nationalUnity = 66f };
            state.countries.Add(s);
            foreach (var other in state.countries)
            {
                if (other.id == s.id) continue;
                state.relationships.Add(new Relationship { countryA = s.id, countryB = other.id, relations = 45f, trust = 40f });
            }
            return s;
        }

        static TreatyClause Terms(string triggerCountry, int months)
            => new TreatyClause { trigger = triggerCountry == null ? TreatyClauseTrigger.Always : TreatyClauseTrigger.ConflictWithCountry, triggerCountryId = triggerCountry ?? "", durationMonths = months };
        static List<TreatyClause> They(TreatyCommitment c, TreatyClause terms) => new List<TreatyClause> { DiplomaticLeverage.RequestedClause(c, terms) };
        void Warm(string id, float w) { var r = state.FindRelationship(state.playerCountryId, id); r.relations = w; r.trust = w; r.strategicAlignment = w; r.SetThreatPerceivedBy(id, 0f); }
        void Advance(int months) { for (int i = 0; i < months; i++) state.date = state.date.NextMonth(); }
        Sanction Impose(SanctionSeverity severity, string on = null) { on = on ?? target; Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, on, severity, "PLAYER"), "fixture: could not impose"); return state.FindSanction(state.playerCountryId, on); }
        static TreatyClause ClauseOf(Treaty t, TreatyCommitment c) => t.clauses.Find(x => x.commitment == c);

        [TestCase(TreatyClauseTrigger.RelationsAtLeast60, 0)]
        [TestCase(TreatyClauseTrigger.RelationsAtLeast60, 1)]
        [TestCase(TreatyClauseTrigger.RelationsAtLeast60, 2)]
        [TestCase(TreatyClauseTrigger.NoMutualOccupation, 0)]
        [TestCase(TreatyClauseTrigger.NoMutualOccupation, 1)]
        [TestCase(TreatyClauseTrigger.NoMutualOccupation, 2)]
        public void BilateralConditionsSurviveEachRealExchange(TreatyClauseTrigger trigger, int exchange)
        {
            string partner = exchange == 2 ? successor : target;
            Warm(partner, 95);
            var terms = new TreatyClause { trigger = trigger, durationMonths = 12 };
            if (exchange == 1) Impose(SanctionSeverity.Coercive);
            int cp = state.commandPoints.current;
            bool accepted = exchange == 0
                ? gc.OfferSupplyForCommitment(partner, TradeFocus.Energy, TreatyCommitment.Transit, terms)
                : exchange == 1 ? gc.OfferSanctionsReliefForCommitment(partner, TreatyCommitment.Transit, terms)
                : gc.OfferRecognitionForCommitment(partner, TreatyCommitment.Transit, terms);
            Assert.IsTrue(accepted, "Fixture must reach accepted real exchange.");
            Assert.AreEqual(cp - 2, state.commandPoints.current);
            var treaty = state.FindTreaty(state.playerCountryId, partner);
            Assert.AreEqual(trigger, ClauseOf(treaty, TreatyCommitment.Transit).trigger);
            Assert.IsTrue(treaty.Carries(state, partner, TreatyCommitment.Transit));
            Advance(12);
            Assert.IsFalse(treaty.ClauseIsActive(state, TreatyCommitment.Transit));
            var renewal = DiplomaticLeverage.RenewableClause(state, state.playerCountryId, partner, TreatyCommitment.Transit);
            Assert.IsNotNull(renewal); Assert.AreEqual(trigger, renewal.trigger);
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(trigger, ClauseOf(loaded.FindTreaty(state.playerCountryId, partner), TreatyCommitment.Transit).trigger);
        }

        [TestCase(TreatyClauseTrigger.RelationsAtLeast60)]
        [TestCase(TreatyClauseTrigger.NoMutualOccupation)]
        public void BilateralConditionControlsTheWireAndRenewsThroughThePaidCommand(TreatyClauseTrigger trigger)
        {
            Warm(target, 95);
            Assert.IsFalse(WorldWire.Watches(state, target), "fixture: no other wire access");
            var terms = new TreatyClause { trigger = trigger, durationMonths = 12 };
            Assert.IsTrue(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.IntelligenceSharing, terms));
            Assert.IsTrue(WorldWire.Watches(state, target));
            var relation = state.FindRelationship(state.playerCountryId, target);
            var site = state.locations.Find(x => x.originalOwnerId == target && x.ownerId == target);
            Assert.IsNotNull(site);
            if (trigger == TreatyClauseTrigger.RelationsAtLeast60) relation.relations = 59.99f;
            else site.ownerId = state.playerCountryId;
            Assert.IsFalse(WorldWire.Watches(state, target), "signed but dormant must not reveal the wire");
            if (trigger == TreatyClauseTrigger.RelationsAtLeast60) relation.relations = 60f;
            else site.ownerId = target;
            Assert.IsTrue(WorldWire.Watches(state, target), "live compliance restores the actual consumer");
            Advance(12);
            Assert.IsFalse(WorldWire.Watches(state, target), "expiry still wins");
            Impose(SanctionSeverity.Coercive); Warm(target, 95);
            int cp = state.commandPoints.current;
            Assert.IsFalse(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.IntelligenceSharing, Terms(null, 12)));
            Assert.AreEqual(cp, state.commandPoints.current, "different condition cannot silently amend the promise");
            Assert.IsTrue(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.IntelligenceSharing, terms));
            Assert.AreEqual(cp - 2, state.commandPoints.current);
            var treaty = state.FindTreaty(state.playerCountryId, target);
            Assert.AreEqual(1, treaty.clauses.Count);
            Assert.AreEqual(trigger, ClauseOf(treaty, TreatyCommitment.IntelligenceSharing).trigger);
            Assert.IsTrue(WorldWire.Watches(state, target));
        }

        [Test]
        public void BilateralConditionsAreReachableInTheDraftCycleAndRenderWithoutWritingState()
        {
            Warm(target, 95);
            // Initial chamber seating belongs to the existing view, not clause selection.
            new DiplomacyView().Refresh();
            string before = SaveSystem.ToJson(state);
            foreach (var (cols, size) in new[] { (34, SizeClass.Compact), (49, SizeClass.Compact), (64, SizeClass.Medium), (104, SizeClass.Large) })
            {
                TerminalMetrics.Update(cols * 8 + 8, 8, 640, size);
                var view = new DiplomacyView();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(DiplomacyView).GetField("selectedTargetId", flags).SetValue(view, target);
                var cycle = typeof(DiplomacyView).GetMethod("CycleTrigger", flags);
                var local = typeof(DiplomacyView).GetField("draftLocalTrigger", flags);
                for (int i = 0; i < state.countries.Count + 1 && (TreatyClauseTrigger)local.GetValue(view) == TreatyClauseTrigger.Always; i++)
                    cycle.Invoke(view, new object[] { state });
                foreach (var trigger in new[] { TreatyClauseTrigger.RelationsAtLeast60, TreatyClauseTrigger.NoMutualOccupation })
                {
                    Assert.AreEqual(trigger, local.GetValue(view));
                    view.Refresh(); TerminalShellController.ApplyTextPolicy(view.Root, DisplaySettings.ParagraphSpacing, cols);
                    string expected = trigger == TreatyClauseTrigger.RelationsAtLeast60 ? "TRIGGER: RELATIONS >= 60" : "TRIGGER: NO MUTUAL OCCUPATION";
                    bool found = false;
                    view.Root.Query<Button>().ForEach(b => { if (b.text == expected) found = true; });
                    Assert.IsTrue(found, expected);
                    view.Root.Query<Label>().ForEach(l => {
                        if (!TerminalShellController.IsReadout(l) || l.ClassListContains("terminal-figure")) return;
                        foreach (string line in (l.text ?? "").Split('\n')) Assert.LessOrEqual(AsciiChart.VisibleLength(line), cols, line);
                    });
                    var terms = (TreatyClause)typeof(DiplomacyView).GetMethod("LeverageTerms", flags).Invoke(view, null);
                    Assert.AreEqual(trigger, terms.trigger);
                    cycle.Invoke(view, new object[] { state });
                }
                Assert.AreEqual(TreatyClauseTrigger.Always, local.GetValue(view));
            }
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [Test]
        public void Supply_CarriesAFiniteTerm_AndPricesItsScopeOnce()
        {
            var terms = Terms(null, 36); Warm(target, 70f);
            Assert.IsTrue(DiplomaticLeverage.CanOffer(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit, terms, out string why), why);
            float gain = DiplomaticLeverage.SupplyGain(state, state.playerCountryId, target, TradeFocus.Energy);
            float priced = DiplomaticLeverage.Willingness(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit, terms);
            Assert.AreEqual(DiplomacySystem.TreatyWillingness(state, state.playerCountryId, target, They(TreatyCommitment.Transit, terms)) + gain * DiplomaticLeverage.WillingnessPerCeilingPoint, priced, 0.001f, "the requested clause is priced by the ordinary clause test, once, plus the supply term");
            Assert.Greater(priced, DiplomaticLeverage.Willingness(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit), "a bounded promise is easier to accept than a permanent one");
            string when = DiplomacySystem.TermsText(state, DiplomaticLeverage.RequestedClause(TreatyCommitment.Transit, terms), state.date);
            StringAssert.StartsWith("EXPIRES BEFORE ", when);
            int cp = state.commandPoints.current, xp = state.strategistXP, ini = state.initiativesThisYear; var signed = state.date;
            Assert.IsTrue(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.Transit, terms));
            var treaty = state.FindTreaty(state.playerCountryId, target); var clause = ClauseOf(treaty, TreatyCommitment.Transit);
            Assert.AreEqual(state.playerCountryId, treaty.countryA); Assert.AreEqual(ClauseSide.TheyProvide, clause.side);
            Assert.AreEqual(TreatyClauseTrigger.Always, clause.trigger); Assert.AreEqual(36, clause.durationMonths); Assert.AreEqual(signed.SortKey, clause.effectiveDate.SortKey, "effective from the day it was signed");
            Assert.IsTrue(treaty.Carries(state, target, TreatyCommitment.Transit));
            var notice = state.notifications.Find(n => n.title == "LEVERAGE ACCEPTED"); StringAssert.Contains(when.ToLowerInvariant(), notice.body); StringAssert.Contains("does not reverse what we gave", notice.body);
            Assert.IsTrue(state.chronicle.Exists(c => c.text.Contains("in exchange for transit (" + when.ToLowerInvariant() + ")")), "the record names the term");
            Assert.AreEqual(cp - DiplomaticLeverage.OfferCost, state.commandPoints.current); Assert.AreEqual(xp + 42, state.strategistXP); Assert.AreEqual(ini + 1, state.initiativesThisYear);
            Advance(36);
            Assert.IsTrue(treaty.ClauseIsExpired(state, TreatyCommitment.Transit)); Assert.IsFalse(treaty.Carries(state, target, TreatyCommitment.Transit), "expired: no longer active");
            var link = state.FindTrade(state.playerCountryId, target); Assert.IsNotNull(link); Assert.IsFalse(link.embargoed); Assert.AreEqual(TradeFocus.Energy, link.focus, "the supply link is an ordinary link and outlives their promise");
        }

        [Test]
        public void Sanctions_CarriesACondition_DormantUntilTheNamedConflict_ThroughARealConsumer()
        {
            Impose(SanctionSeverity.Coercive); Warm(target, 70f);
            var terms = Terms(third, 0);
            Assert.IsFalse(WorldWire.Watches(state, target), "fixture: the wire must not already watch them");
            Assert.IsTrue(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.IntelligenceSharing, terms));
            var treaty = state.FindTreaty(state.playerCountryId, target); var clause = ClauseOf(treaty, TreatyCommitment.IntelligenceSharing);
            Assert.AreEqual(TreatyClauseTrigger.ConflictWithCountry, clause.trigger); Assert.AreEqual(third, clause.triggerCountryId); Assert.AreEqual(0, clause.durationMonths);
            Assert.IsTrue(treaty.Carries(target, TreatyCommitment.IntelligenceSharing), "signed: they carry it"); Assert.IsFalse(treaty.Carries(state, target, TreatyCommitment.IntelligenceSharing), "dormant until the named conflict");
            Assert.IsFalse(WorldWire.Watches(state, target), "a dormant intelligence-sharing clause opens nothing");
            Assert.IsNull(state.FindSanction(state.playerCountryId, target), "the concession applied at once"); Assert.AreEqual(EconomySystem.DetenteTruceMonths, state.FindRelationship(state.playerCountryId, target).sanctionsTruceMonths);
            var notice = state.notifications.Find(n => n.title == "SANCTIONS LIFTED FOR A COMMITMENT"); StringAssert.Contains("if conflict with", notice.body);
            var war = ConfrontationSystem.BeginBy(state, third, target, ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(war, "fixture: could not open the named conflict"); war.escalation = EscalationState.LimitedConflict;
            Assert.IsTrue(treaty.Carries(state, target, TreatyCommitment.IntelligenceSharing), "active during the named conflict");
            Assert.IsTrue(WorldWire.Watches(state, target), "the real consumer wakes with the clause");
        }

        [Test]
        public void Recognition_CarriesConditionAndTerm_AndTheCounterfactualReadsThatClause()
        {
            var terms = Terms(third, 60); Warm(successor, 60f);
            float once = DiplomaticLeverage.WillingnessOnceRecognised(state, state.playerCountryId, successor, TreatyCommitment.Transit, terms);
            var world = SaveSystem.FromJson(SaveSystem.ToJson(state)); Assert.IsTrue(DiplomacySystem.RecogniseBy(world, world.playerCountryId, successor));
            Assert.AreEqual(DiplomacySystem.TreatyWillingness(world, world.playerCountryId, successor, They(TreatyCommitment.Transit, terms)), once, 0.001f, "the counterfactual reads the requested clause, scope included, once");
            float share = DiplomaticLeverage.LegitimacyGain(state, state.playerCountryId, successor) * DiplomacySystem.LegitimacyWillingnessWeight;
            Assert.AreEqual(once + share, DiplomaticLeverage.RecognitionOfferWillingness(state, state.playerCountryId, successor, TreatyCommitment.Transit, terms), 0.001f);
            Assert.Greater(once, DiplomaticLeverage.WillingnessOnceRecognised(state, state.playerCountryId, successor, TreatyCommitment.Transit), "bounded terms are easier");
            Assert.IsTrue(gc.OfferRecognitionForCommitment(successor, TreatyCommitment.Transit, terms));
            var treaty = state.FindTreaty(state.playerCountryId, successor); var clause = ClauseOf(treaty, TreatyCommitment.Transit);
            Assert.AreEqual(TreatyClauseTrigger.ConflictWithCountry, clause.trigger); Assert.AreEqual(third, clause.triggerCountryId); Assert.AreEqual(60, clause.durationMonths);
            Assert.IsTrue(state.FindRelationship(state.playerCountryId, successor).recognised); Assert.IsFalse(treaty.Carries(state, successor, TreatyCommitment.Transit), "dormant now");
            Advance(60); Assert.IsTrue(treaty.ClauseIsExpired(state, TreatyCommitment.Transit)); Assert.IsTrue(state.FindRelationship(state.playerCountryId, successor).recognised, "recognition outlives their promise");
        }

        [Test]
        public void DefaultTermsAreUnchanged()
        {
            Warm(target, 70f); Impose(SanctionSeverity.Coercive); Warm(successor, 60f);
            var permanent = Terms(null, 0);
            Assert.AreEqual(DiplomaticLeverage.Willingness(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit), DiplomaticLeverage.Willingness(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit, permanent));
            Assert.AreEqual(DiplomaticLeverage.ReliefOfferWillingness(state, state.playerCountryId, target, TreatyCommitment.Transit), DiplomaticLeverage.ReliefOfferWillingness(state, state.playerCountryId, target, TreatyCommitment.Transit, permanent));
            Assert.AreEqual(DiplomaticLeverage.RecognitionOfferWillingness(state, state.playerCountryId, successor, TreatyCommitment.Transit), DiplomaticLeverage.RecognitionOfferWillingness(state, state.playerCountryId, successor, TreatyCommitment.Transit, permanent));
            Assert.IsTrue(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.Transit));
            var clause = ClauseOf(state.FindTreaty(state.playerCountryId, target), TreatyCommitment.Transit);
            Assert.AreEqual(TreatyClauseTrigger.Always, clause.trigger); Assert.AreEqual("", clause.triggerCountryId ?? ""); Assert.AreEqual(0, clause.durationMonths);
            Assert.IsFalse(state.notifications.Find(n => n.title == "SANCTIONS LIFTED FOR A COMMITMENT").body.Contains("Their commitment applies"), "no terms sentence for an unconditional, permanent promise");
        }

        [Test]
        public void InvalidTermsAreRefusedBeforeSpending()
        {
            Warm(target, 70f); Impose(SanctionSeverity.Coercive, parent); Warm(parent, 70f); Warm(successor, 60f);
            Assert.IsTrue(DiplomaticLeverage.CanOffer(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit, null, out string s0), s0);
            Assert.IsTrue(DiplomaticLeverage.CanOfferRelief(state, state.playerCountryId, parent, TreatyCommitment.Transit, null, out string r0), r0);
            Assert.IsTrue(DiplomaticLeverage.CanOfferRecognition(state, state.playerCountryId, successor, TreatyCommitment.Transit, null, out string g0), g0);
            void Invalid(string label, TreatyClause terms, string expect)
            {
                string before = SaveSystem.ToJson(state); int cp = state.commandPoints.current;
                Assert.IsFalse(DiplomaticLeverage.CanOffer(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit, terms, out string a), label); StringAssert.Contains(expect, a, label);
                Assert.IsFalse(DiplomaticLeverage.CanOfferRelief(state, state.playerCountryId, parent, TreatyCommitment.Transit, terms, out string b), label); StringAssert.Contains(expect, b, label);
                Assert.IsFalse(DiplomaticLeverage.CanOfferRecognition(state, state.playerCountryId, successor, TreatyCommitment.Transit, terms, out string c), label); StringAssert.Contains(expect, c, label);
                Assert.IsFalse(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.Transit, terms), label);
                Assert.IsFalse(gc.OfferSanctionsReliefForCommitment(parent, TreatyCommitment.Transit, terms), label);
                Assert.IsFalse(gc.OfferRecognitionForCommitment(successor, TreatyCommitment.Transit, terms), label);
                Assert.AreEqual(cp, state.commandPoints.current, label + ": spent"); Assert.AreEqual(before, SaveSystem.ToJson(state), label + ": state changed");
            }
            Invalid("unsupported term", Terms(null, 24), "UNSUPPORTED TERM");
            Invalid("negative term", Terms(null, -12), "NEGATIVE");
            Invalid("trigger names ourselves", Terms(state.playerCountryId, 0), "NOT A SIGNATORY");
            Invalid("unknown third state", Terms("NOWHERE", 12), "DOES NOT EXIST");
            Invalid("conflict trigger with no state", new TreatyClause { trigger = TreatyClauseTrigger.ConflictWithCountry, triggerCountryId = "" }, "MUST NAME A STATE");
            // the trigger naming the counterparty is refused for that counterparty and allowed elsewhere
            Assert.IsFalse(DiplomaticLeverage.CanOffer(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit, Terms(target, 0), out string d)); StringAssert.Contains("NOT A SIGNATORY", d);
            Assert.IsTrue(DiplomaticLeverage.CanOfferRecognition(state, state.playerCountryId, successor, TreatyCommitment.Transit, Terms(target, 0), out _));
        }

        [Test]
        public void TheyAreCountryA_TermsSurviveTheFlip_AndUnrelatedClausesKeepTheirClocks()
        {
            Warm(target, 70f);
            var mutual = new List<TreatyClause> { new TreatyClause { commitment = TreatyCommitment.NonAggression, side = ClauseSide.Mutual, durationMonths = 60 } };
            Assert.IsTrue(DiplomacySystem.ConcludeNegotiatedTreaty(state, target, state.playerCountryId, mutual));
            var treaty = state.FindTreaty(state.playerCountryId, target); Assert.AreEqual(target, treaty.countryA);
            Advance(6); var naExpiry = treaty.ClauseExpiry(TreatyCommitment.NonAggression);
            int xp = state.strategistXP, ini = state.initiativesThisYear;
            Assert.IsTrue(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.Transit, Terms(third, 12)));
            var clause = ClauseOf(treaty, TreatyCommitment.Transit);
            Assert.AreEqual(ClauseSide.WeProvide, clause.side, "stored relative to countryA, which is them"); Assert.IsTrue(treaty.Carries(target, TreatyCommitment.Transit)); Assert.IsFalse(treaty.Carries(state.playerCountryId, TreatyCommitment.Transit));
            Assert.AreEqual(TreatyClauseTrigger.ConflictWithCountry, clause.trigger); Assert.AreEqual(third, clause.triggerCountryId); Assert.AreEqual(12, clause.durationMonths); Assert.AreEqual(state.date.SortKey, clause.effectiveDate.SortKey);
            Assert.AreEqual(naExpiry, treaty.ClauseExpiry(TreatyCommitment.NonAggression), "an unrelated clause's clock was reset");
            Assert.AreEqual(xp + 32, state.strategistXP); Assert.AreEqual(ini + 1, state.initiativesThisYear);
        }

        [Test]
        public void AnExpiredPromiseRenewsOnItsRecordedTerms_NotOnFreshOnes()
        {
            Warm(target, 70f);
            Assert.IsTrue(DiplomacySystem.ConcludeNegotiatedTreaty(state, state.playerCountryId, target, new List<TreatyClause> { DiplomaticLeverage.RequestedClause(TreatyCommitment.Transit, Terms(null, 12)), DiplomaticLeverage.RequestedClause(TreatyCommitment.IntelligenceSharing, Terms(null, 60)) }));
            var treaty = state.FindTreaty(state.playerCountryId, target); Advance(12);
            Assert.IsTrue(treaty.ClauseIsExpired(state, TreatyCommitment.Transit), "fixture: expired"); var isExpiry = treaty.ClauseExpiry(TreatyCommitment.IntelligenceSharing);
            Impose(SanctionSeverity.Coercive);
            var recorded = DiplomaticLeverage.RenewableClause(state, state.playerCountryId, target, TreatyCommitment.Transit); Assert.IsNotNull(recorded); Assert.AreEqual(12, recorded.durationMonths);
            Assert.IsFalse(DiplomaticLeverage.CanOfferRelief(state, state.playerCountryId, target, TreatyCommitment.Transit, Terms(null, 36), out string why)); StringAssert.Contains("RECORDED TERMS", why);
            Assert.IsFalse(DiplomaticLeverage.CanOfferRelief(state, state.playerCountryId, target, TreatyCommitment.Transit, null, out string why2)); StringAssert.Contains("RECORDED TERMS", why2);
            int cp = state.commandPoints.current; Assert.IsFalse(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.Transit, Terms(null, 36))); Assert.AreEqual(cp, state.commandPoints.current, "a mismatched renewal spends nothing");
            Assert.IsTrue(DiplomaticLeverage.CanOfferRelief(state, state.playerCountryId, target, TreatyCommitment.Transit, Terms(null, 12), out _));
            int clauses = treaty.clauses.Count, xp = state.strategistXP, ini = state.initiativesThisYear;
            Assert.IsTrue(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.Transit, Terms(null, 12)));
            Assert.AreEqual(clauses, treaty.clauses.Count, "renewal adds no clause"); Assert.IsTrue(treaty.Carries(state, target, TreatyCommitment.Transit), "renewed: active again");
            Assert.AreEqual(state.date.SortKey, ClauseOf(treaty, TreatyCommitment.Transit).effectiveDate.SortKey, "only this clause's clock restarts"); Assert.AreEqual(isExpiry, treaty.ClauseExpiry(TreatyCommitment.IntelligenceSharing));
            Assert.AreEqual("TREATY RENEWED", state.notifications.Find(n => n.title == "TREATY RENEWED")?.title);
            Assert.IsNull(state.FindSanction(state.playerCountryId, target)); Assert.AreEqual(xp + 32, state.strategistXP); Assert.AreEqual(ini + 1, state.initiativesThisYear);
        }

        [Test]
        public void ADormantOrOppositePromiseIsNotOverwritten()
        {
            Warm(target, 70f);
            Assert.IsTrue(DiplomacySystem.ConcludeNegotiatedTreaty(state, state.playerCountryId, target, new List<TreatyClause> { DiplomaticLeverage.RequestedClause(TreatyCommitment.Transit, Terms(third, 0)), new TreatyClause { commitment = TreatyCommitment.IntelligenceSharing, side = ClauseSide.WeProvide } }));
            var treaty = state.FindTreaty(state.playerCountryId, target);
            Assert.IsFalse(treaty.Carries(state, target, TreatyCommitment.Transit), "fixture: dormant");
            Assert.IsFalse(DiplomaticLeverage.CanOffer(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit, Terms(null, 12), out string a)); StringAssert.Contains("ALREADY CARRY", a);
            Assert.IsFalse(DiplomaticLeverage.CanOffer(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.IntelligenceSharing, null, out string b)); StringAssert.Contains("THE OTHER WAY", b);
            string before = SaveSystem.ToJson(state); int cp = state.commandPoints.current;
            Assert.IsFalse(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.Transit, Terms(null, 12))); Assert.IsFalse(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.IntelligenceSharing));
            Assert.AreEqual(cp, state.commandPoints.current); Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [Test]
        public void RejectedOffersLeaveTermsAndConcessionsAlone_AndTermsSurviveASave()
        {
            Impose(SanctionSeverity.Routine); var r = state.FindRelationship(state.playerCountryId, target); r.relations = 5f; r.trust = 5f; r.strategicAlignment = 5f; r.SetThreatPerceivedBy(target, 90f);
            int cp = state.commandPoints.current;
            Assert.IsFalse(gc.OfferSanctionsReliefForCommitment(target, TreatyCommitment.MutualDefense, Terms(third, 36)));
            Assert.IsNotNull(state.FindSanction(state.playerCountryId, target)); Assert.IsNull(state.FindTreaty(state.playerCountryId, target)); Assert.AreEqual(cp - DiplomaticLeverage.OfferCost, state.commandPoints.current);
            Warm(target, 70f); state.sanctions.RemoveAll(s => s.senderId == state.playerCountryId && s.targetId == target);
            Assert.IsTrue(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.Transit, Terms(third, 36)));
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state)); var t = loaded.FindTreaty(loaded.playerCountryId, target); var c = ClauseOf(t, TreatyCommitment.Transit);
            Assert.AreEqual(TreatyClauseTrigger.ConflictWithCountry, c.trigger); Assert.AreEqual(third, c.triggerCountryId); Assert.AreEqual(36, c.durationMonths); Assert.AreEqual(state.date.SortKey, c.effectiveDate.SortKey);
            Assert.AreEqual(state.FindTreaty(state.playerCountryId, target).ClauseExpiry(TreatyCommitment.Transit), t.ClauseExpiry(TreatyCommitment.Transit));
        }

        [Test]
        public void TheScreenShowsTheTermsBeforeConfirmation_BlocksAnInvalidTrigger_AndFitsANarrowPhone()
        {
            Warm(target, 70f);
            DiplomacyView View(string selected, string trigger, int months)
            {
                var v = new DiplomacyView();
                typeof(DiplomacyView).GetField("selectedTargetId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(v, selected);
                typeof(DiplomacyView).GetField("draftTriggerCountryId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(v, trigger);
                typeof(DiplomacyView).GetField("draftDurationMonths", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(v, months);
                v.Refresh(); TerminalShellController.ApplyTextPolicy(v.Root, DisplaySettings.ParagraphSpacing, TerminalMetrics.Columns); return v;
            }
            string thirdName = state.FindCountry(third).displayName.ToUpperInvariant();
            foreach (var (cols, size) in new[] { (34, SizeClass.Compact), (49, SizeClass.Compact), (64, SizeClass.Medium), (104, SizeClass.Large) })
            {
                TerminalMetrics.Update(cols * 8 + 8, 8, 640, size);
                var view = View(target, third, 36);
                int max = 0; string worst = "", all = "";
                view.Root.Query<Label>().ForEach(l => { if (!TerminalShellController.IsReadout(l) || l.ClassListContains("terminal-figure")) return; all += l.text + "\n"; foreach (var line in (l.text ?? "").Replace("\r", "").Split('\n')) { int len = AsciiChart.VisibleLength(line); if (len > max) { max = len; worst = line; } } });
                Assert.LessOrEqual(max, cols, $"DIPLOMACY at {cols} columns overflows: \"{worst}\"");
                string flat = System.Text.RegularExpressions.Regex.Replace(all, @"\s+", " ");
                StringAssert.Contains("TERMS FOR WHAT THEY WOULD CARRY", flat); StringAssert.Contains($"if conflict with {thirdName.ToLowerInvariant()}", flat.ToLowerInvariant()); StringAssert.Contains("expires before", flat.ToLowerInvariant());
                StringAssert.Contains("does not reverse any of it", flat); StringAssert.Contains($"at least {EconomySystem.DetenteTruceMonths} months", flat); StringAssert.Contains("recognition is permanent", flat);
                var buttons = new List<Button>(); view.Root.Query<Button>().ForEach(b => buttons.Add(b));
                Assert.IsTrue(buttons.Exists(b => b.text == $"TRIGGER: CONFLICT WITH {thirdName}")); Assert.IsTrue(buttons.Exists(b => b.text == "TERM: 3 YEAR(S)"));
                Assert.IsTrue(buttons.Exists(b => b.text == "FOR TRANSIT [2 CP]" && b.enabledSelf));
            }
            // a trigger naming the counterparty cannot spend: the exchange refuses it and says why
            TerminalMetrics.Update(64 * 8 + 8, 8, 640, SizeClass.Medium);
            var bad = View(target, target, 0); var refused = new List<Button>(); bad.Root.Query<Button>().ForEach(b => { if (b.text == "FOR TRANSIT [2 CP]") refused.Add(b); });
            Assert.AreEqual(1, refused.Count); Assert.IsFalse(refused[0].enabledSelf); StringAssert.Contains("NOT A SIGNATORY", TerminalView.BlockedReason(refused[0]));
            // with our measures on them the sanctions box carries the same terms, and the same refusal
            Impose(SanctionSeverity.Coercive);
            var lift = View(target, third, 36); var lifts = new List<Button>(); lift.Root.Query<Button>().ForEach(b => { if (b.text == "LIFT FOR TRANSIT [2 CP]") lifts.Add(b); });
            Assert.AreEqual(1, lifts.Count); Assert.IsTrue(lifts[0].enabledSelf);
            var liftBad = View(target, target, 0); var liftsBad = new List<Button>(); liftBad.Root.Query<Button>().ForEach(b => { if (b.text == "LIFT FOR TRANSIT [2 CP]") liftsBad.Add(b); });
            Assert.AreEqual(1, liftsBad.Count); Assert.IsFalse(liftsBad[0].enabledSelf); StringAssert.Contains("NOT A SIGNATORY", TerminalView.BlockedReason(liftsBad[0]));
            // the recognition box carries the same terms
            var rec = View(successor, third, 12); string recAll = ""; rec.Root.Query<Label>().ForEach(l => recAll += l.text + "\n"); var recButtons = new List<Button>(); rec.Root.Query<Button>().ForEach(b => recButtons.Add(b));
            StringAssert.Contains("TERM: 1 YEAR(S)", string.Join("|", recButtons.ConvertAll(b => b.text))); Assert.IsTrue(recButtons.Exists(b => b.text == "RECOGNISE FOR TRANSIT [2 CP]" && b.enabledSelf));
        }
    }
}
