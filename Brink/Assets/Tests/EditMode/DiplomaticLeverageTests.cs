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
    /// Specific diplomatic leverage, first slice (roadmap #21, spec 04 §5h):
    /// a supply guarantee they need, offered through the real command path for
    /// one commitment they carry. The concession must be what carries the ask,
    /// a declined or invalid offer must deliver nothing, an accepted one must
    /// apply exactly the exchange, and the same guarantee must not buy twice.
    /// </summary>
    public class DiplomaticLeverageTests
    {
        GameState state;
        GameController gc;
        string target;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            gc = GameController.Instance;
            gc.NewGame(3131);
            state = gc.State;
            state.commandPoints.current = 40;
            state.politicalCapital = 20f;

            // We are rich in energy; they were written short of it (spec 08).
            state.PlayerCountry.resources.energy = 90f;
            state.PlayerCountry.resources.strategicMaterials = 90f;
            target = null;
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var profile = WorldFactory.FindProfile(country.id);
                if (profile == null || profile.energy >= 45f) continue;
                if (state.FindSanction(state.playerCountryId, country.id) != null) continue;
                if (state.FindSanction(country.id, state.playerCountryId) != null) continue;
                if (state.FindTreaty(state.playerCountryId, country.id) != null) continue;
                target = country.id; break;
            }
            Assert.IsNotNull(target, "no authored energy-poor state without existing ties — fixture cannot take");
            // The authored world already links us to some of these states; the
            // fixture starts from no commercial tie so the guarantee is the
            // whole concession.
            state.trade.RemoveAll(l => l.Involves(state.playerCountryId) && l.Involves(target));
            state.FindCountry(target).resources.energy = 30f;
            state.FindCountry(target).resources.energyEndowment = 30f;
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

        /// <summary>
        /// Warm the pair to exactly the point where the commitment alone is
        /// refused and the commitment plus the guarantee is accepted — and
        /// assert both, so a fixture that did not take reports itself.
        /// </summary>
        void WarmToTheMargin(TreatyCommitment commitment, TradeFocus focus = TradeFocus.Energy)
        {
            var r = state.FindRelationship(state.playerCountryId, target);
            r.trust = 55f; r.strategicAlignment = 55f; r.SetThreatPerceivedBy(target, 0f);
            for (float relations = 20f; relations <= 95f; relations += 1f)
            {
                r.relations = relations;
                float plain = DiplomacySystem.TreatyWillingness(state, state.playerCountryId, target, TheyProvide(commitment));
                float lever = DiplomaticLeverage.Willingness(state, state.playerCountryId, target, focus, commitment);
                if (plain < 50f && lever >= 50f) return;
            }
            Assert.Inconclusive("no relations level separates the plain ask from the leveraged one on this seed");
        }

        [Test]
        public void TheConcessionIsWhatCarriesTheAsk()
        {
            // First, at any warmth: the guarantee is worth exactly its real
            // supply, and nothing else moves the reception. Asserted directly
            // rather than through the margin search, so an acceptance rule that
            // quietly ignores the concession fails here instead of leaving the
            // margin search with nothing to find.
            var r0 = state.FindRelationship(state.playerCountryId, target);
            r0.relations = 60f; r0.trust = 55f; r0.strategicAlignment = 55f; r0.SetThreatPerceivedBy(target, 0f);
            float bare = DiplomacySystem.TreatyWillingness(state, state.playerCountryId, target, TheyProvide(TreatyCommitment.Transit));
            float priced = DiplomaticLeverage.Willingness(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit);
            float worth = DiplomaticLeverage.SupplyGain(state, state.playerCountryId, target, TradeFocus.Energy);
            Assert.Greater(worth, 0f, "fixture: the guarantee must be worth something to a state short of energy");
            Assert.Greater(priced, bare, "the concession does not move the reception at all");
            Assert.AreEqual(bare + worth * DiplomaticLeverage.WillingnessPerCeilingPoint, priced, 0.001f);

            WarmToTheMargin(TreatyCommitment.Transit);
            float plain = DiplomacySystem.TreatyWillingness(state, state.playerCountryId, target, TheyProvide(TreatyCommitment.Transit));
            float lever = DiplomaticLeverage.Willingness(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit);
            float gain = DiplomaticLeverage.SupplyGain(state, state.playerCountryId, target, TradeFocus.Energy);
            Assert.Less(plain, 50f, "fixture: the bare ask must fail");
            Assert.GreaterOrEqual(lever, 50f, "fixture: the leveraged ask must pass");
            Assert.Greater(gain, 0f);
            Assert.AreEqual(plain + gain * DiplomaticLeverage.WillingnessPerCeilingPoint, lever, 0.001f,
                "the offer is priced by treaty willingness plus the guarantee's real worth, nothing else");

            int cp = state.commandPoints.current;
            Assert.IsFalse(gc.ProposeNegotiatedTreaty(target, TheyProvide(TreatyCommitment.Transit)),
                "the same commitment asked for on its own must be refused");
            Assert.IsNull(state.FindTreaty(state.playerCountryId, target));
            Assert.AreEqual(cp - DiplomacySystem.TreatyProposalCost, state.commandPoints.current);

            Assert.IsTrue(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.Transit),
                "with the guarantee on the table the same commitment is signed");
            Assert.IsNotNull(state.FindTreaty(state.playerCountryId, target));
        }

        [Test]
        public void NeedIsWhatMakesTheGuaranteeWorthSomething()
        {
            WarmToTheMargin(TreatyCommitment.Transit);
            // A state with no room to use the supply gains nothing from it.
            var them = state.FindCountry(target);
            them.resources.energyEndowment = 100f; them.resources.energy = 100f;
            Assert.AreEqual(0f, DiplomaticLeverage.SupplyGain(state, state.playerCountryId, target, TradeFocus.Energy), 0.001f);
            Assert.AreEqual(
                DiplomacySystem.TreatyWillingness(state, state.playerCountryId, target, TheyProvide(TreatyCommitment.Transit)),
                DiplomaticLeverage.Willingness(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit), 0.001f);
            Assert.IsTrue(DiplomaticLeverage.CanOffer(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit, out _),
                "the validity gate reads only public facts, so a valid offer to a rich state is still an offer");
            Assert.IsFalse(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.Transit),
                "our surplus alone buys nothing from a state that does not need it");
            Assert.IsNull(state.FindTreaty(state.playerCountryId, target));
            Assert.IsNull(state.FindTrade(state.playerCountryId, target));
        }

        [Test]
        public void AnAcceptedOfferAppliesExactlyTheExchange()
        {
            WarmToTheMargin(TreatyCommitment.Transit);
            var them = state.FindCountry(target);
            var r = state.FindRelationship(state.playerCountryId, target);
            float ceilingBefore = EconomySystem.EnergyCeilingFor(state, them);
            float gain = DiplomaticLeverage.SupplyGain(state, state.playerCountryId, target, TradeFocus.Energy);
            float dependenceBefore = r.DependenceOf(target);
            int cp = state.commandPoints.current, initiative = state.initiativesThisYear, xp = state.strategistXP;
            int treaties = state.treaties.Count, links = state.trade.Count;

            Assert.IsTrue(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.Transit));

            // The concession, exactly.
            var link = state.FindTrade(state.playerCountryId, target);
            Assert.IsNotNull(link); Assert.AreEqual(links + 1, state.trade.Count);
            Assert.AreEqual(TradeFocus.Energy, link.focus);
            Assert.AreEqual(DiplomaticLeverage.OfferVolume, link.volume, 0.001f);
            Assert.AreEqual(DiplomaticLeverage.OfferTariff, link.tariff, 0.001f);
            Assert.IsFalse(link.embargoed); Assert.AreEqual(state.playerCountryId, link.initiatedBy);
            Assert.AreEqual(ceilingBefore + gain, EconomySystem.EnergyCeilingFor(state, them), 0.01f,
                "the guarantee lifts their ceiling by exactly what it was priced at");
            Assert.AreEqual(dependenceBefore + DiplomaticLeverage.OfferVolume * 0.25f, r.DependenceOf(target), 0.001f,
                "the importer becomes dependent on the supplier");

            // The commitment, exactly, in the direction agreed.
            var treaty = state.FindTreaty(state.playerCountryId, target);
            Assert.IsNotNull(treaty); Assert.AreEqual(treaties + 1, state.treaties.Count);
            CollectionAssert.AreEqual(new[] { TreatyCommitment.Transit }, treaty.commitments);
            Assert.AreEqual(1, treaty.clauses.Count);
            Assert.IsTrue(treaty.Carries(state, target, TreatyCommitment.Transit), "they carry it");
            Assert.IsFalse(treaty.Carries(state, state.playerCountryId, TreatyCommitment.Transit), "we do not");
            Assert.IsTrue(treaty.Receives(state, state.playerCountryId, TreatyCommitment.Transit));
            Assert.AreEqual(TreatyClauseTrigger.Always, treaty.clauses[0].trigger);
            Assert.AreEqual(0, treaty.clauses[0].durationMonths);
            Assert.AreEqual(state.date.SortKey, treaty.clauses[0].effectiveDate.SortKey);

            // Paid through the existing command path, once.
            Assert.AreEqual(cp - DiplomaticLeverage.OfferCost, state.commandPoints.current);
            Assert.Greater(state.initiativesThisYear, initiative);
            Assert.Greater(state.strategistXP, xp);
            Assert.IsTrue(state.notifications.Exists(n => n.title == "LEVERAGE ACCEPTED"));
            Assert.IsTrue(state.notifications.Exists(n => n.title == "TREATY SIGNED"));
        }

        [Test]
        public void ADeclinedOfferDeliversNothing()
        {
            var r = state.FindRelationship(state.playerCountryId, target);
            r.relations = 8f; r.trust = 10f; r.strategicAlignment = 20f; r.SetThreatPerceivedBy(target, 85f);
            Assert.Less(DiplomaticLeverage.Willingness(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.MutualDefense), 50f,
                "fixture: the offer must be one they refuse");
            Assert.IsTrue(DiplomaticLeverage.CanOffer(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.MutualDefense, out _));

            int cp = state.commandPoints.current; float dependence = r.DependenceOf(target);
            float ceiling = EconomySystem.EnergyCeilingFor(state, state.FindCountry(target));
            int treaties = state.treaties.Count, links = state.trade.Count, memories = r.memory.Count;

            Assert.IsFalse(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.MutualDefense));

            Assert.AreEqual(cp - DiplomaticLeverage.OfferCost, state.commandPoints.current, "a declined attempt is a spent attempt, like a treaty proposal");
            Assert.AreEqual(treaties, state.treaties.Count, "no treaty");
            Assert.AreEqual(links, state.trade.Count, "no link — the concession was not delivered");
            Assert.AreEqual(dependence, r.DependenceOf(target), 0.001f);
            Assert.AreEqual(ceiling, EconomySystem.EnergyCeilingFor(state, state.FindCountry(target)), 0.001f);
            Assert.AreEqual(memories + 1, r.memory.Count);
            StringAssert.Contains("Rejected a supply-for-commitment offer", r.memory[r.memory.Count - 1]);
            Assert.IsTrue(state.notifications.Exists(n => n.title == "OFFER DECLINED"));
            Assert.IsFalse(state.notifications.Exists(n => n.title == "LEVERAGE ACCEPTED"));
        }

        [Test]
        public void InvalidOffersSpendNothingAndChangeNothing()
        {
            WarmToTheMargin(TreatyCommitment.Transit);
            void Invalid(string label, string t, TradeFocus focus, TreatyCommitment c, Action arrange = null, Action restore = null)
            {
                arrange?.Invoke();
                Assert.IsFalse(DiplomaticLeverage.CanOffer(state, state.playerCountryId, t, focus, c, out string reason), label);
                Assert.IsFalse(string.IsNullOrEmpty(reason), label + ": every refusal says why");
                string before = SaveSystem.ToJson(state); int cp = state.commandPoints.current;
                Assert.IsFalse(gc.OfferSupplyForCommitment(t, focus, c), label);
                Assert.AreEqual(cp, state.commandPoints.current, label + ": CP");
                Assert.AreEqual(before, SaveSystem.ToJson(state), label + ": state");
                restore?.Invoke();
            }
            Invalid("self", state.playerCountryId, TradeFocus.Energy, TreatyCommitment.Transit);
            Invalid("unknown", "NOWHERE", TradeFocus.Energy, TreatyCommitment.Transit);
            Invalid("no commodity", target, TradeFocus.General, TreatyCommitment.Transit);
            Invalid("no surplus", target, TradeFocus.Food, TreatyCommitment.Transit,
                () => state.PlayerCountry.resources.foodSecurity = 30f);
            Invalid("sanctions", target, TradeFocus.Energy, TreatyCommitment.Transit,
                () => state.sanctions.Add(new Sanction { senderId = state.playerCountryId, targetId = target, severity = SanctionSeverity.Routine, imposedDate = state.date }),
                () => state.sanctions.RemoveAll(s => s.senderId == state.playerCountryId && s.targetId == target));
            Invalid("arms control without a regime", target, TradeFocus.Energy, TreatyCommitment.ArmsControl);

            // After a signed exchange: the commitment is carried and the guarantee is spent.
            Assert.IsTrue(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.Transit));
            Invalid("already carried", target, TradeFocus.Energy, TreatyCommitment.Transit);
            Invalid("nothing new to offer", target, TradeFocus.Energy, TreatyCommitment.IntelligenceSharing);
        }

        [Test]
        public void TheSameGuaranteeCannotBuyASecondCommitment()
        {
            WarmToTheMargin(TreatyCommitment.Transit);
            Assert.IsTrue(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.Transit));

            Assert.AreEqual(0f, DiplomaticLeverage.SupplyGain(state, state.playerCountryId, target, TradeFocus.Energy), 0.001f,
                "once they draw our energy on these terms the guarantee is worth nothing more");
            Assert.AreEqual(
                DiplomacySystem.TreatyWillingness(state, state.playerCountryId, target, TheyProvide(TreatyCommitment.IntelligenceSharing)),
                DiplomaticLeverage.Willingness(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.IntelligenceSharing), 0.001f,
                "a second ask on the same guarantee is priced as a bare ask");
            Assert.IsFalse(DiplomaticLeverage.CanOffer(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.IntelligenceSharing, out string reason));
            StringAssert.Contains("NOTHING NEW", reason);

            // A different commodity would be a different concession — but the
            // trade model carries one commodity per pair, so it cannot be put
            // on the same link without taking their energy away. The refusal
            // says so, and sends the operator to the trade screen.
            Assert.IsFalse(DiplomaticLeverage.CanOffer(state, state.playerCountryId, target, TradeFocus.Materials, TreatyCommitment.IntelligenceSharing, out string why));
            StringAssert.Contains("ALREADY CARRIES ENERGY", why);
        }

        [Test]
        public void DirectionIsCorrectWhenTheyAreCountryA()
        {
            WarmToTheMargin(TreatyCommitment.Transit);
            // A treaty they proposed: they are countryA, so a clause they carry
            // must be stored as WeProvide from countryA's side.
            var mutual = new List<TreatyClause> { new TreatyClause { commitment = TreatyCommitment.NonAggression, side = ClauseSide.Mutual } };
            Assert.IsTrue(DiplomacySystem.ConcludeNegotiatedTreaty(state, target, state.playerCountryId, mutual));
            var treaty = state.FindTreaty(state.playerCountryId, target);
            Assert.AreEqual(target, treaty.countryA);
            int clausesBefore = treaty.clauses.Count;

            Assert.IsTrue(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.Transit));
            Assert.AreSame(treaty, state.FindTreaty(state.playerCountryId, target), "deepened, never a second treaty");
            CollectionAssert.AreEquivalent(new[] { TreatyCommitment.NonAggression, TreatyCommitment.Transit }, treaty.commitments);
            Assert.AreEqual(clausesBefore + 1, treaty.clauses.Count);
            var clause = treaty.clauses.Find(c => c.commitment == TreatyCommitment.Transit);
            Assert.AreEqual(ClauseSide.WeProvide, clause.side, "stored relative to countryA, which is them");
            Assert.AreEqual(ClauseSide.TheyProvide, treaty.SideFor(state.playerCountryId, TreatyCommitment.Transit));
            Assert.IsTrue(treaty.Carries(state, target, TreatyCommitment.Transit));
            Assert.IsFalse(treaty.Carries(state, state.playerCountryId, TreatyCommitment.Transit));
            Assert.AreEqual(ClauseSide.Mutual, treaty.SideFor(state.playerCountryId, TreatyCommitment.NonAggression), "the existing clause is untouched");
            Assert.IsTrue(state.notifications.Exists(n => n.title == "TREATY DEEPENED"));
        }

        [Test]
        public void PlainDeepeningStillRecordsNoClause()
        {
            // The extracted application path must not change what deepening
            // writes: a plain ADD reads as mutual by the absence of a clause.
            var r = state.FindRelationship(state.playerCountryId, target);
            r.relations = 90f; r.trust = 85f; r.strategicAlignment = 80f; r.SetThreatPerceivedBy(target, 0f);
            Assert.IsTrue(gc.ProposeTreaty(target, new List<TreatyCommitment> { TreatyCommitment.NonAggression }));
            var treaty = state.FindTreaty(state.playerCountryId, target);
            Assert.AreEqual(0, treaty.clauses.Count);
            Assert.IsTrue(gc.DeepenTreaty(target, TreatyCommitment.TradePreference));
            Assert.IsTrue(treaty.Has(TreatyCommitment.TradePreference));
            Assert.AreEqual(0, treaty.clauses.Count);
        }

        [Test]
        public void PreviewIsGradedByCollectionAndTheScreenReadsNoForeignFigure()
        {
            WarmToTheMargin(TreatyCommitment.Transit);
            state.estimates.RemoveAll(e => e.observerId == state.playerCountryId && e.targetId == target);
            Assert.AreEqual(TradeOutlook.Uncertain,
                DiplomaticLeverage.Assess(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit),
                "with no collection the outlook is uncertain whatever the truth");
            state.estimates.Add(new IntelEstimate { observerId = state.playerCountryId, targetId = target, domain = IntelDomain.Political,
                reportedValue = 50f, margin = 3f, confidence = ConfidenceGrade.Confirmed, asOf = state.date, everCollected = true });
            Assert.AreEqual(TradeOutlook.Likely,
                DiplomaticLeverage.Assess(state, state.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit));
            Assert.AreEqual(TradeOutlook.NoTerms,
                DiplomaticLeverage.Assess(state, state.playerCountryId, target, TradeFocus.General, TreatyCommitment.Transit));

            // The offer screen may read our stocks and the authored profile, never their live resources.
            string source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/UI/Views/DiplomacyView.cs"));
            int start = source.IndexOf("void BuildLeverageControls", StringComparison.Ordinal);
            int end = source.IndexOf("TreatyClause FindClause", start, StringComparison.Ordinal);
            Assert.Greater(start, 0); Assert.Greater(end, start);
            string body = source.Substring(start, end - start);
            Assert.IsFalse(body.Contains("target.resources"), "the leverage screen reads a foreign country's true resources");
            Assert.IsFalse(body.Contains("DiplomaticLeverage.Willingness("), "the leverage screen prints the true reception");
            Assert.IsFalse(body.Contains("SupplyGain("), "the leverage screen prints what the guarantee is truly worth to them");
        }

        [Test]
        public void TheExchangeSurvivesASaveAndOldSavesNeedNothing()
        {
            WarmToTheMargin(TreatyCommitment.Transit);
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(before.Contains("leverage"), "no new serialized state");
            Assert.IsTrue(gc.OfferSupplyForCommitment(target, TradeFocus.Energy, TreatyCommitment.Transit));
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(state.saveVersion, loaded.saveVersion);
            var link = loaded.FindTrade(loaded.playerCountryId, target);
            Assert.AreEqual(TradeFocus.Energy, link.focus); Assert.AreEqual(DiplomaticLeverage.OfferVolume, link.volume, 0.001f);
            var treaty = loaded.FindTreaty(loaded.playerCountryId, target);
            Assert.IsTrue(treaty.Carries(loaded, target, TreatyCommitment.Transit));
            Assert.IsFalse(treaty.Carries(loaded, loaded.playerCountryId, TreatyCommitment.Transit));
            Assert.AreEqual(EconomySystem.EnergyCeilingFor(state, state.FindCountry(target)),
                EconomySystem.EnergyCeilingFor(loaded, loaded.FindCountry(target)), 0.001f);
        }

        [Test]
        public void TheOfferIsDeterministic()
        {
            WarmToTheMargin(TreatyCommitment.Transit);
            var a = SaveSystem.FromJson(SaveSystem.ToJson(state)); var b = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.IsTrue(DiplomaticLeverage.OfferBy(a, a.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit));
            Assert.IsTrue(DiplomaticLeverage.OfferBy(b, b.playerCountryId, target, TradeFocus.Energy, TreatyCommitment.Transit));
            Assert.AreEqual(SaveSystem.ToJson(a), SaveSystem.ToJson(b));
        }

        [Test]
        public void TheLeverageScreenFitsANarrowPhoneAndExplainsRefusals()
        {
            WarmToTheMargin(TreatyCommitment.Transit);
            foreach (var (cols, size) in new[] { (34, SizeClass.Compact), (49, SizeClass.Compact), (64, SizeClass.Medium) })
            {
                TerminalMetrics.Update(cols * 8 + 8, 8, 640, size);
                var view = new DiplomacyView();
                typeof(DiplomacyView).GetField("selectedTargetId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, target);
                view.Refresh();
                TerminalShellController.ApplyTextPolicy(view.Root, DisplaySettings.ParagraphSpacing, TerminalMetrics.Columns);

                int max = 0; string worst = ""; string all = "";
                view.Root.Query<Label>().ForEach(l =>
                {
                    if (!TerminalShellController.IsReadout(l) || l.ClassListContains("terminal-figure")) return;
                    all += l.text + "\n";
                    foreach (var line in (l.text ?? "").Replace("\r", "").Split('\n'))
                    { int len = AsciiChart.VisibleLength(line); if (len > max) { max = len; worst = line; } }
                });
                Assert.LessOrEqual(max, cols, $"DIPLOMACY at {cols} columns overflows: \"{worst}\"");
                StringAssert.Contains("LEVERAGE", all);
                StringAssert.Contains("OUTLOOK", all);

                var offers = new List<Button>(); view.Root.Query<Button>().ForEach(b => { if (b.text.StartsWith("FOR ")) offers.Add(b); });
                Assert.AreEqual(Enum.GetValues(typeof(TreatyCommitment)).Length, offers.Count, "one control per commitment");
                var arms = offers.Find(b => b.text.Contains("ARMS CONTROL"));
                Assert.IsFalse(arms.enabledSelf, "arms control without a verification regime must be refused");
                StringAssert.Contains("VERIFICATION REGIME", TerminalView.BlockedReason(arms));
                StringAssert.Contains("UNAVAILABLE", all, "a refused control must say so on screen, not in a tooltip");
            }
        }
    }
}
