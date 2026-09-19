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
    /// Specific diplomatic leverage, slice 3 (spec 04 §5j): recognise a
    /// breakaway in exchange for one commitment it carries, through the real
    /// command path. The concession is our actual recognition with every
    /// consequence RECOGNISE A STATE has; it is priced by what recognition is
    /// already worth in the game; it cannot be sold twice; declined and invalid
    /// offers change nothing.
    /// </summary>
    public class RecognitionExchangeTests
    {
        GameState state;
        GameController gc;
        string successor, parent, third;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            gc = GameController.Instance;
            gc.NewGame(4747);
            state = gc.State;
            state.commandPoints.current = 40;
            state.politicalCapital = 20f;
            parent = null; third = null;
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (state.FindTreaty(state.playerCountryId, country.id) != null) continue;
                if (state.IsAtWar(country.id)) continue;
                if (parent == null) parent = country.id; else if (third == null) third = country.id;
            }
            Assert.IsNotNull(parent); Assert.IsNotNull(third);
            successor = PlantABreakaway(parent).id;

            // fixture preconditions, asserted rather than assumed
            var s = state.FindCountry(successor);
            Assert.IsTrue(DiplomacySystem.IsSuccessor(state, s), "fixture: not a successor");
            Assert.AreEqual(parent, DiplomacySystem.ParentOf(state, s), "fixture: parent id convention");
            Assert.AreEqual(0, DiplomacySystem.RecognitionCount(state, successor), "fixture: nobody recognises them yet");
            Assert.AreEqual(0f, DiplomacySystem.Legitimacy(state, s), 0.0001f, "fixture: no legitimacy yet");
            Assert.IsTrue(DiplomacySystem.CanRecognise(state, state.playerCountryId, successor, out _), "fixture: recognisable");
            var r = state.FindRelationship(state.playerCountryId, successor);
            Assert.IsNotNull(r); Assert.IsFalse(r.recognised);
            Assert.IsNotNull(state.FindRelationship(state.playerCountryId, parent), "fixture: standing with the parent");
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
            TerminalMetrics.ResetForTests();
        }

        /// <summary>A breakaway, made the way `SecessionSystem` makes one (the established `RecognitionAndMediationTests` helper).</summary>
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

        static List<TreatyClause> TheyProvide(TreatyCommitment c)
            => new List<TreatyClause> { new TreatyClause { commitment = c, side = ClauseSide.TheyProvide } };

        /// <summary>Warm the pair to where the bare ask fails and the recognition-backed one passes; assert both took.</summary>
        void WarmToTheMargin(TreatyCommitment commitment)
        {
            var r = state.FindRelationship(state.playerCountryId, successor);
            r.SetThreatPerceivedBy(successor, 0f);
            for (float w = 0f; w <= 95f; w += 0.5f)
            {
                r.relations = w; r.trust = w; r.strategicAlignment = w;
                float plain = DiplomacySystem.TreatyWillingness(state, state.playerCountryId, successor, TheyProvide(commitment));
                float backed = DiplomaticLeverage.RecognitionOfferWillingness(state, state.playerCountryId, successor, commitment);
                if (plain < 50f && backed >= 50f) return;
            }
            Assert.Inconclusive("no warmth separates the plain ask from the recognition-backed one");
        }

        [Test]
        public void RecognitionIsWhatCarriesTheAsk_AndItsWorthIsTheWarmthAndTheShare()
        {
            var r = state.FindRelationship(state.playerCountryId, successor);
            r.relations = 50f; r.trust = 50f; r.strategicAlignment = 50f; r.SetThreatPerceivedBy(successor, 0f);
            float plain = DiplomacySystem.TreatyWillingness(state, state.playerCountryId, successor, TheyProvide(TreatyCommitment.Transit));
            float once = DiplomaticLeverage.WillingnessOnceRecognised(state, state.playerCountryId, successor, TreatyCommitment.Transit);
            float gain = DiplomaticLeverage.LegitimacyGain(state, state.playerCountryId, successor);
            float value = DiplomaticLeverage.RecognitionValue(state, state.playerCountryId, successor, TreatyCommitment.Transit);
            float backed = DiplomaticLeverage.RecognitionOfferWillingness(state, state.playerCountryId, successor, TreatyCommitment.Transit);
            Assert.Greater(once, plain, "the warmth recognition writes must make the same ask easier");
            Assert.AreEqual(1f / (state.countries.Count - 1), gain, 0.0001f, "one share of the world's acceptance");
            Assert.AreEqual((once - plain) + gain * DiplomacySystem.LegitimacyWillingnessWeight, value, 0.001f, "recognition is worth its warmth plus its share, nothing else");
            Assert.AreEqual(once + gain * DiplomacySystem.LegitimacyWillingnessWeight, backed, 0.001f, "the package is the ask read once recognised plus the share");
            Assert.IsFalse(r.recognised, "reading the price granted nothing");

            WarmToTheMargin(TreatyCommitment.Transit);
            // The bare ask, put through the real proposal path on a detached copy so
            // its rejection memory does not change the relationship the exchange then reads.
            var bare = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.IsFalse(DiplomacySystem.ProposeNegotiatedTreatyBy(bare, bare.playerCountryId, successor, TheyProvide(TreatyCommitment.Transit)), "the bare ask must be refused");
            Assert.IsNull(bare.FindTreaty(bare.playerCountryId, successor));
            Assert.IsTrue(gc.OfferRecognitionForCommitment(successor, TreatyCommitment.Transit), "with recognition on the table the same ask is signed");
        }

        [Test]
        public void AnAcceptedOfferRecognisesAndSignsExactlyTheClause_AndCostsTheParent()
        {
            Assert.IsTrue(DiplomacySystem.RecogniseBy(state, third, successor), "fixture: a third state's recognition");
            var withSuccessor = state.FindRelationship(state.playerCountryId, successor);
            var withParent = state.FindRelationship(state.playerCountryId, parent);
            var thirdWithSuccessor = state.FindRelationship(third, successor);
            WarmToTheMargin(TreatyCommitment.Transit);
            float toSuccessor = withSuccessor.relations, toParent = withParent.relations, parentTrust = withParent.trust;
            int parentMemories = withParent.memory.Count, count = DiplomacySystem.RecognitionCount(state, successor);
            int cp = state.commandPoints.current, initiative = state.initiativesThisYear, xp = state.strategistXP, treaties = state.treaties.Count, sanctions = state.sanctions.Count;

            Assert.IsTrue(gc.OfferRecognitionForCommitment(successor, TreatyCommitment.Transit));

            Assert.IsTrue(withSuccessor.recognised, "recognition granted");
            Assert.AreEqual(count + 1, DiplomacySystem.RecognitionCount(state, successor));
            Assert.GreaterOrEqual(withSuccessor.relations, Math.Min(100f, toSuccessor + 16f) - 0.001f, "the grateful new state — at least what RECOGNISE A STATE writes (signing warms them further)");
            Assert.AreEqual(Math.Max(0f, toParent - 14f), withParent.relations, 0.001f, "the parent takes it as a hostile act — exactly what RECOGNISE A STATE writes");
            Assert.AreEqual(Math.Max(0f, parentTrust - 10f), withParent.trust, 0.001f);
            Assert.AreEqual(parentMemories + 1, withParent.memory.Count); StringAssert.Contains("Recognised", withParent.memory[withParent.memory.Count - 1]);
            Assert.IsTrue(thirdWithSuccessor.recognised, "an unrelated recognition is untouched");

            var treaty = state.FindTreaty(state.playerCountryId, successor);
            Assert.IsNotNull(treaty); Assert.AreEqual(treaties + 1, state.treaties.Count);
            CollectionAssert.AreEqual(new[] { TreatyCommitment.Transit }, treaty.commitments);
            Assert.IsTrue(treaty.Carries(state, successor, TreatyCommitment.Transit)); Assert.IsFalse(treaty.Carries(state, state.playerCountryId, TreatyCommitment.Transit));
            Assert.AreEqual(sanctions, state.sanctions.Count);

            Assert.AreEqual(cp - DiplomaticLeverage.OfferCost, state.commandPoints.current);
            Assert.AreEqual(initiative + 1, state.initiativesThisYear, "exactly one Diplomacy initiative");
            Assert.AreEqual(xp + 42, state.strategistXP, "30 for the treaty concluded + 12 for the exchange; RECOGNISE A STATE's own 14 is not added");
            var notice = state.notifications.Find(n => n.title == "RECOGNITION FOR A COMMITMENT");
            Assert.IsNotNull(notice); StringAssert.Contains("not withdrawn", notice.body);
            Assert.IsTrue(state.chronicle.Exists(c => c.text.Contains("Recognises ") && c.countryId == state.playerCountryId), "the ordinary recognition record is written");
        }

        [Test]
        public void ADeclinedOfferLeavesRecognitionAndEveryClauseAlone()
        {
            var r = state.FindRelationship(state.playerCountryId, successor); r.relations = 5f; r.trust = 5f; r.strategicAlignment = 5f; r.SetThreatPerceivedBy(successor, 90f);
            var withParent = state.FindRelationship(state.playerCountryId, parent); float toParent = withParent.relations; int parentMemories = withParent.memory.Count;
            Assert.IsTrue(DiplomaticLeverage.CanOfferRecognition(state, state.playerCountryId, successor, TreatyCommitment.MutualDefense, out _));
            Assert.Less(DiplomaticLeverage.RecognitionOfferWillingness(state, state.playerCountryId, successor, TreatyCommitment.MutualDefense), 50f, "fixture: the offer must be refusable");
            int cp = state.commandPoints.current, xp = state.strategistXP, initiative = state.initiativesThisYear, treaties = state.treaties.Count;
            Assert.IsFalse(gc.OfferRecognitionForCommitment(successor, TreatyCommitment.MutualDefense));
            Assert.IsFalse(r.recognised, "declined: no recognition"); Assert.AreEqual(0, DiplomacySystem.RecognitionCount(state, successor));
            Assert.IsNull(state.FindTreaty(state.playerCountryId, successor)); Assert.AreEqual(treaties, state.treaties.Count);
            Assert.AreEqual(toParent, withParent.relations); Assert.AreEqual(parentMemories, withParent.memory.Count, "the parent saw nothing");
            Assert.AreEqual(cp - DiplomaticLeverage.OfferCost, state.commandPoints.current);
            Assert.AreEqual(xp, state.strategistXP); Assert.AreEqual(initiative, state.initiativesThisYear);
            StringAssert.Contains("Rejected a recognition-for-commitment offer", r.memory[r.memory.Count - 1]);
            Assert.IsTrue(state.notifications.Exists(n => n.title == "OFFER DECLINED"));
        }

        [Test]
        public void OnlyAnUnrecognisedBreakawayCanBeOffered()
        {
            void Invalid(string label, string t, TreatyCommitment c, string expectReason)
            {
                Assert.IsFalse(DiplomaticLeverage.CanOfferRecognition(state, state.playerCountryId, t, c, out string reason), label);
                StringAssert.Contains(expectReason, reason, label);
                string before = SaveSystem.ToJson(state); int cp = state.commandPoints.current, xp = state.strategistXP, ini = state.initiativesThisYear;
                Assert.IsFalse(gc.OfferRecognitionForCommitment(t, c), label);
                Assert.AreEqual(cp, state.commandPoints.current, label); Assert.AreEqual(xp, state.strategistXP); Assert.AreEqual(ini, state.initiativesThisYear);
                Assert.AreEqual(before, SaveSystem.ToJson(state), label + ": state changed");
            }
            Invalid("self", state.playerCountryId, TreatyCommitment.Transit, "NO SUCH PARTNER");
            Invalid("unknown", "NOWHERE", TreatyCommitment.Transit, "NO SUCH PARTNER");
            Invalid("a state that has always been there", third, TreatyCommitment.Transit, "ALWAYS BEEN THERE");
            Invalid("arms control without a regime", successor, TreatyCommitment.ArmsControl, "VERIFICATION REGIME");
            // unaffordable
            WarmToTheMargin(TreatyCommitment.Transit);
            state.commandPoints.current = 1; string snap = SaveSystem.ToJson(state);
            Assert.IsFalse(gc.OfferRecognitionForCommitment(successor, TreatyCommitment.Transit)); Assert.AreEqual(snap, SaveSystem.ToJson(state));
            state.commandPoints.current = 40;
            // already recognised the ordinary way: not ours to sell again
            Assert.IsTrue(gc.RecogniseState(successor), "fixture: ordinary recognition");
            Invalid("already recognised", successor, TreatyCommitment.Transit, "ALREADY RECOGNISE");
            Assert.AreEqual(0f, DiplomaticLeverage.LegitimacyGain(state, state.playerCountryId, successor), "nothing left to add");
            Assert.AreEqual(0f, DiplomaticLeverage.RecognitionValue(state, state.playerCountryId, successor, TreatyCommitment.Transit));
        }

        [Test]
        public void ExtendingAStandingTreaty_TheyAreCountryA_PreservesUnrelatedClauses()
        {
            var mutual = new List<TreatyClause> { new TreatyClause { commitment = TreatyCommitment.NonAggression, side = ClauseSide.Mutual, trigger = TreatyClauseTrigger.ConflictWithCountry, triggerCountryId = third, durationMonths = 36 } };
            Assert.IsTrue(DiplomacySystem.ConcludeNegotiatedTreaty(state, successor, state.playerCountryId, mutual));
            var treaty = state.FindTreaty(state.playerCountryId, successor); Assert.AreEqual(successor, treaty.countryA);
            var na = treaty.clauses[0]; var snap = (na.side, na.trigger, na.triggerCountryId, na.durationMonths, na.effectiveDate.SortKey);
            WarmToTheMargin(TreatyCommitment.Transit);
            int initiative = state.initiativesThisYear, xp = state.strategistXP;
            Assert.IsTrue(gc.OfferRecognitionForCommitment(successor, TreatyCommitment.Transit));
            Assert.AreSame(treaty, state.FindTreaty(state.playerCountryId, successor));
            Assert.AreEqual(snap, (na.side, na.trigger, na.triggerCountryId, na.durationMonths, na.effectiveDate.SortKey), "unrelated conditional clause altered");
            var transit = treaty.clauses.Find(c => c.commitment == TreatyCommitment.Transit);
            Assert.AreEqual(ClauseSide.WeProvide, transit.side, "stored relative to countryA, which is them");
            Assert.IsTrue(treaty.Carries(state, successor, TreatyCommitment.Transit)); Assert.IsFalse(treaty.Carries(state, state.playerCountryId, TreatyCommitment.Transit));
            Assert.AreEqual(initiative + 1, state.initiativesThisYear); Assert.AreEqual(xp + 32, state.strategistXP, "20 deepening + 12 exchange");
            Assert.IsTrue(state.FindRelationship(state.playerCountryId, successor).recognised);
        }

        [Test]
        public void RecognitionCannotBeSoldAgain_EvenAfterALoad()
        {
            WarmToTheMargin(TreatyCommitment.Transit);
            Assert.IsTrue(gc.OfferRecognitionForCommitment(successor, TreatyCommitment.Transit));
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(state.saveVersion, loaded.saveVersion);
            Assert.IsTrue(loaded.FindRelationship(loaded.playerCountryId, successor).recognised, "recognition survives a save");
            Assert.AreEqual(1, DiplomacySystem.RecognitionCount(loaded, successor));
            Assert.IsTrue(loaded.FindTreaty(loaded.playerCountryId, successor).Carries(loaded, successor, TreatyCommitment.Transit));
            Assert.IsFalse(DiplomaticLeverage.CanOfferRecognition(loaded, loaded.playerCountryId, successor, TreatyCommitment.IntelligenceSharing, out string why));
            StringAssert.Contains("ALREADY RECOGNISE", why);
            Assert.AreEqual(0f, DiplomaticLeverage.LegitimacyGain(loaded, loaded.playerCountryId, successor));
            int cp = state.commandPoints.current; Assert.IsFalse(gc.OfferRecognitionForCommitment(successor, TreatyCommitment.IntelligenceSharing)); Assert.AreEqual(cp, state.commandPoints.current);
        }

        [Test]
        public void PreviewsArePureAndTheScreenReadsNoForeignFigure()
        {
            WarmToTheMargin(TreatyCommitment.Transit);
            var r = state.FindRelationship(state.playerCountryId, successor);
            string before = SaveSystem.ToJson(state); int seq = state.actionSequence, memories = r.memory.Count; float weight = r.memoryWeight;
            foreach (TreatyCommitment c in Enum.GetValues(typeof(TreatyCommitment)))
            {
                DiplomaticLeverage.CanOfferRecognition(state, state.playerCountryId, successor, c, out _);
                DiplomaticLeverage.AssessRecognitionOffer(state, state.playerCountryId, successor, c);
                DiplomaticLeverage.RecognitionOfferWillingness(state, state.playerCountryId, successor, c);
                DiplomaticLeverage.RecognitionValue(state, state.playerCountryId, successor, c);
                DiplomaticLeverage.WillingnessOnceRecognised(state, state.playerCountryId, successor, c);
                DiplomaticLeverage.LegitimacyGain(state, state.playerCountryId, successor);
            }
            Assert.IsFalse(r.recognised, "a preview recognised them"); Assert.AreEqual(memories, r.memory.Count, "a preview wrote a memory"); Assert.AreEqual(weight, r.memoryWeight);
            Assert.AreEqual(before, SaveSystem.ToJson(state), "a preview changed the live world"); Assert.AreEqual(seq, state.actionSequence);

            state.estimates.RemoveAll(e => e.observerId == state.playerCountryId && e.targetId == successor);
            Assert.AreEqual(TradeOutlook.Uncertain, DiplomaticLeverage.AssessRecognitionOffer(state, state.playerCountryId, successor, TreatyCommitment.Transit));
            state.estimates.Add(new IntelEstimate { observerId = state.playerCountryId, targetId = successor, domain = IntelDomain.Political, reportedValue = 50f, margin = 3f, confidence = ConfidenceGrade.Confirmed, asOf = state.date, everCollected = true });
            Assert.AreEqual(TradeOutlook.Likely, DiplomaticLeverage.AssessRecognitionOffer(state, state.playerCountryId, successor, TreatyCommitment.Transit));

            string source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/UI/Views/DiplomacyView.cs"));
            int start = source.IndexOf("void BuildRecognitionExchangeControls", StringComparison.Ordinal);
            int end = source.IndexOf("TreatyClause FindClause", start, StringComparison.Ordinal);
            string body = source.Substring(start, end - start);
            foreach (var forbidden in new[] { "target.resources", "Legitimacy(", "RecognitionOfferWillingness(", "WillingnessOnceRecognised(", "TreatyWillingness(", "RecognitionValue(" })
                Assert.IsFalse(body.Contains(forbidden), $"the screen reads {forbidden}");
        }

        [Test]
        public void TheScreenNamesTheStateParentCommitmentDirectionCostAndConsequences_AndFitsANarrowPhone()
        {
            WarmToTheMargin(TreatyCommitment.Transit);
            string name = state.FindCountry(successor).displayName.ToUpperInvariant(), parentName = state.FindCountry(parent).displayName.ToUpperInvariant();
            foreach (var (cols, size) in new[] { (34, SizeClass.Compact), (49, SizeClass.Compact), (64, SizeClass.Medium), (104, SizeClass.Large) })
            {
                TerminalMetrics.Update(cols * 8 + 8, 8, 640, size);
                var view = new DiplomacyView();
                typeof(DiplomacyView).GetField("selectedTargetId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, successor);
                view.Refresh(); TerminalShellController.ApplyTextPolicy(view.Root, DisplaySettings.ParagraphSpacing, TerminalMetrics.Columns);
                int max = 0; string worst = "", all = "";
                view.Root.Query<Label>().ForEach(l => { if (!TerminalShellController.IsReadout(l) || l.ClassListContains("terminal-figure")) return; all += l.text + "\n"; foreach (var line in (l.text ?? "").Replace("\r", "").Split('\n')) { int len = AsciiChart.VisibleLength(line); if (len > max) { max = len; worst = line; } } });
                Assert.LessOrEqual(max, cols, $"DIPLOMACY at {cols} columns overflows: \"{worst}\"");
                string flat = System.Text.RegularExpressions.Regex.Replace(all, @"\s+", " ");
                StringAssert.Contains("RECOGNITION FOR A COMMITMENT", flat);
                StringAssert.Contains($"RECOGNISING {name}", flat);
                StringAssert.Contains($"BROKE AWAY FROM {parentName}", flat);
                StringAssert.Contains("RECOGNISED BY 0 OF", flat);
                StringAssert.Contains("a clause they carry", flat);
                StringAssert.Contains("not withdrawn", flat);
                StringAssert.Contains("hostile act", flat);
                StringAssert.Contains("Declined, nothing applies", flat);
                var offers = new List<Button>(); view.Root.Query<Button>().ForEach(b => { if (b.text.StartsWith("RECOGNISE FOR ")) offers.Add(b); });
                Assert.AreEqual(Enum.GetValues(typeof(TreatyCommitment)).Length, offers.Count);
                Assert.IsTrue(offers.Exists(b => b.text == "RECOGNISE FOR TRANSIT [2 CP]" && b.enabledSelf));
                var arms = offers.Find(b => b.text == "RECOGNISE FOR ARMS CONTROL [2 CP]"); Assert.IsFalse(arms.enabledSelf); StringAssert.Contains("VERIFICATION REGIME", TerminalView.BlockedReason(arms));
                StringAssert.Contains("UNAVAILABLE", all);
                // all three exchanges share the screen
                StringAssert.Contains("LEVERAGE", flat); StringAssert.Contains("LIFT OUR SANCTIONS FOR A COMMITMENT", flat);
            }
            // a state that has always been there: the block says so and offers nothing
            TerminalMetrics.Update(64 * 8 + 8, 8, 640, SizeClass.Medium);
            var v2 = new DiplomacyView(); typeof(DiplomacyView).GetField("selectedTargetId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(v2, third); v2.Refresh();
            string all2 = ""; v2.Root.Query<Label>().ForEach(l => all2 += l.text + "\n");
            StringAssert.Contains("has always been there", all2);
            var offers2 = new List<Button>(); v2.Root.Query<Button>().ForEach(b => { if (b.text.StartsWith("RECOGNISE FOR ")) offers2.Add(b); }); Assert.AreEqual(0, offers2.Count);
            // once recognised: not ours to sell again
            Assert.IsTrue(gc.OfferRecognitionForCommitment(successor, TreatyCommitment.Transit));
            var v3 = new DiplomacyView(); typeof(DiplomacyView).GetField("selectedTargetId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(v3, successor); v3.Refresh();
            string all3 = ""; v3.Root.Query<Label>().ForEach(l => all3 += l.text + "\n");
            StringAssert.Contains("We already recognise", all3);
            var offers3 = new List<Button>(); v3.Root.Query<Button>().ForEach(b => { if (b.text.StartsWith("RECOGNISE FOR ")) offers3.Add(b); }); Assert.AreEqual(0, offers3.Count);
            Assert.IsTrue(ActionCatalog.All(state).Exists(e => e.label == "Recognise a state for a commitment"));
        }

        /// <summary>
        /// The parent is a third state rival gravity reads: recognition cools
        /// us with the parent, and a breakaway committed to its parent then
        /// pulls against us. The preview must read gravity from the world
        /// recognition would leave, not the one before it.
        /// </summary>
        [TestCase(90f, 25f, 80f)]   // alignment past the 68 line; our −14 crosses the 22 line
        [TestCase(90f, 10f, 60f)]   // already cold with the parent; recognition deepens it and flips the outcome
        public void TheCounterfactualCarriesTheParentConsequenceIntoGravity(float successorParentAlignment, float usParent, float usSuccessor)
        {
            var sp = state.FindRelationship(successor, parent); sp.strategicAlignment = successorParentAlignment;
            var up = state.FindRelationship(state.playerCountryId, parent); up.relations = usParent; up.trust = usParent;
            var us = state.FindRelationship(state.playerCountryId, successor); us.relations = usSuccessor; us.trust = usSuccessor; us.strategicAlignment = usSuccessor; us.SetThreatPerceivedBy(successor, 0f);
            Assert.Greater(sp.strategicAlignment, 68f, "fixture: the breakaway must be committed to its parent for the parent to radiate");
            Assert.Less(usParent - 14f, 22f, "fixture: recognition's cost must leave us in real enmity with the parent");
            var after = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.IsTrue(DiplomacySystem.RecogniseBy(after, after.playerCountryId, successor));
            float gravityBefore = DiplomacySystem.RivalGravity(state, state.playerCountryId, successor), gravityAfter = DiplomacySystem.RivalGravity(after, after.playerCountryId, successor);
            Assert.Greater(gravityAfter, gravityBefore + 0.1f, "fixture: recognition must raise gravity on the pair through the parent");

            float once = DiplomaticLeverage.WillingnessOnceRecognised(state, state.playerCountryId, successor, TreatyCommitment.Transit);
            float actual = DiplomacySystem.TreatyWillingness(after, after.playerCountryId, successor, TheyProvide(TreatyCommitment.Transit));
            Assert.AreEqual(actual, once, 0.001f, "the counterfactual must equal the ordinary reading after recognition actually lands, parent cost included");
            Assert.IsFalse(us.recognised); Assert.AreEqual(usParent, up.relations, "the preview cooled the live parent relationship");

            float share = DiplomaticLeverage.LegitimacyGain(state, state.playerCountryId, successor) * DiplomacySystem.LegitimacyWillingnessWeight;
            bool predicted = actual + share >= 50f;
            int cp = state.commandPoints.current;
            Assert.AreEqual(predicted, gc.OfferRecognitionForCommitment(successor, TreatyCommitment.Transit), "acceptance must follow the world recognition leaves");
            Assert.AreEqual(cp - DiplomaticLeverage.OfferCost, state.commandPoints.current);
            Assert.AreEqual(predicted, us.recognised);
        }

        [Test]
        public void TheOfferIsDeterministic()
        {
            WarmToTheMargin(TreatyCommitment.Transit);
            string before = SaveSystem.ToJson(state);
            var a = SaveSystem.FromJson(before); var b = SaveSystem.FromJson(before);
            Assert.IsTrue(DiplomaticLeverage.OfferRecognitionBy(a, a.playerCountryId, successor, TreatyCommitment.Transit)); Assert.IsTrue(DiplomaticLeverage.OfferRecognitionBy(b, b.playerCountryId, successor, TreatyCommitment.Transit));
            Assert.AreEqual(SaveSystem.ToJson(a), SaveSystem.ToJson(b));
            Assert.IsTrue(a.FindRelationship(a.playerCountryId, successor).recognised);
        }
    }
}
