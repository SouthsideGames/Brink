using System;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Brink.Tests
{
    /// <summary>
    /// Finished intelligence (spec 03 §10, spec 25 §5.3).
    ///
    /// Collection used to buy sharper numbers about foreign *capability* and
    /// nothing else, while `AIStrategy.StrategicPath`,
    /// `AIPrediction.OpponentModel` and `EndgameSystem.KnownPreparation` were
    /// computed every month for every government and read by almost nothing.
    /// The claims here are the three the system rests on: it needs collection,
    /// the answer is fixed once given, and a poor service is *plausibly* wrong
    /// rather than noisy.
    /// </summary>
    public class IntelProductTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 7714);
            state.commandPoints.current = 60;
            state.authorizedPillarMask = ~0;

            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        CountryState SomeoneElse()
        {
            foreach (var country in state.countries)
                if (!country.isPlayer) return country;
            return null;
        }

        IntelNetwork GiveUsANetwork(string targetId, float penetration)
        {
            var network = new IntelNetwork
            {
                ownerId = state.playerCountryId,
                targetId = targetId,
                focus = IntelDomain.Political,
                penetration = penetration
            };
            state.networks.Add(network);
            return network;
        }

        // ---------- analysis is a product of collection ----------

        [Test]
        public void FindingControllerChargesOnceAndAutosavesOnlyInIsolatedDirectory()
        {
            var gc = GameController.Instance;
            var previous = gc.State; var previousTurns = gc.Turns;
            string oldDirectory = SaveSystem.SaveDirectoryOverride;
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "brink-finding-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            try
            {
                SaveSystem.SaveDirectoryOverride = directory;
                typeof(GameController).GetProperty("State").SetValue(gc, state);
                typeof(GameController).GetProperty("Turns").SetValue(gc, turns);
                var movement = PlantFindingEvidence();
                SponsorshipFindings.Discover(state);
                var finding = SponsorshipFindings.For(state, state.playerCountryId)[0];
                state.commandPoints.current = 1;
                string before = SaveSystem.ToJson(state);
                Assert.IsFalse(gc.ExposeSponsorshipFinding(finding.Key));
                Assert.AreEqual(before, SaveSystem.ToJson(state));
                state.commandPoints.current = 10;
                Assert.IsTrue(gc.ExposeSponsorshipFinding(finding.Key));
                Assert.AreEqual(8, state.commandPoints.current);
                Assert.IsTrue(SaveSystem.Load(0).sponsorshipFindings[0].exposed);
                before = SaveSystem.ToJson(state);
                Assert.IsFalse(gc.ExposeSponsorshipFinding(finding.Key));
                Assert.AreEqual(before, SaveSystem.ToJson(state));
                Assert.IsTrue(gc.FileSponsorshipFinding(finding.Key));
                Assert.AreEqual(8, state.commandPoints.current);
                Assert.IsTrue(SaveSystem.Load(0).sponsorshipFindings[0].filed);
            }
            finally
            {
                SaveSystem.SaveDirectoryOverride = oldDirectory;
                typeof(GameController).GetProperty("State").SetValue(gc, previous);
                typeof(GameController).GetProperty("Turns").SetValue(gc, previousTurns);
                System.IO.Directory.Delete(directory, true);
            }
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void FindingPanelIsOwnOnlyWrappedAndPure(int columns)
        {
            PlantFindingEvidence();
            SponsorshipFindings.Discover(state);
            state.sponsorshipFindings.Add(new SponsorshipFinding { observerId = SomeoneElse().id,
                sponsorId = "SECRET FOREIGN MARKER", locationId = "FOREIGN PLACE", discovered = state.date });
            string before = SaveSystem.ToJson(state);
            Brink.UI.TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Brink.UI.Breakpoints.FromColumns(columns));
            try
            {
                var view = new Brink.UI.Views.IntelligenceView();
                typeof(Brink.UI.Views.IntelligenceView).GetMethod("BuildFindings",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(view, new object[] { state });
                string output = "";
                view.Root.Query<UnityEngine.UIElements.Label>().ForEach(label => {
                    output += label.text;
                    foreach (string line in label.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
                });
                StringAssert.Contains("CLASSIFIED FINDING", output);
                StringAssert.DoesNotContain("SECRET FOREIGN MARKER", output);
                Assert.AreEqual(before, SaveSystem.ToJson(state));
            }
            finally { Brink.UI.TerminalMetrics.ResetForTests(); }
        }

        Insurgency PlantFindingEvidence()
        {
            var sponsor = SomeoneElse();
            var place = state.locations.Find(l => l.ownerId == state.playerCountryId);
            Assert.IsNotNull(place);
            state.PlayerCountry.technology.capabilities.Add(new HeldCapability { capabilityId = "CAP_FORENSICS", maturity = 100f });
            GiveUsANetwork(sponsor.id, 65f);
            var movement = new Insurgency { id = "FINDING_TEST", locationId = place.id,
                sponsorId = sponsor.id, strength = 40f, support = 40f, armsSupplied = 20f };
            state.insurgencies.Add(movement);
            return movement;
        }

        void FindingDiplomaticClimate(float warmth)
        {
            state.treaties.Clear(); state.confrontations.Clear();
            foreach (var relation in state.relationships)
            {
                relation.relations = relation.trust = relation.strategicAlignment = warmth;
                relation.memoryWeight = 0f;
                relation.threatPerceptionOfA = relation.threatPerceptionOfB = 0f;
            }
        }

        [TestCase("share", 1, true)]
        [TestCase("confront", 2, true)]
        [TestCase("confront-decline", 2, false)]
        [TestCase("bargain", 2, true)]
        [TestCase("bargain-decline", 2, false)]
        public void FindingResponsesUsePaidControllersAndPersistAttempts(string action, int cost, bool accepts)
        {
            var gc = GameController.Instance; var previous = gc.State; var previousTurns = gc.Turns;
            string oldDirectory = SaveSystem.SaveDirectoryOverride;
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "brink-finding-response-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            try
            {
                SaveSystem.SaveDirectoryOverride = directory;
                typeof(GameController).GetProperty("State").SetValue(gc, state);
                typeof(GameController).GetProperty("Turns").SetValue(gc, turns);
                var movement = PlantFindingEvidence(); SponsorshipFindings.Discover(state);
                var finding = state.sponsorshipFindings[0];
                FindingDiplomaticClimate(accepts ? 100f : 0f);
                string ally = state.countries.Find(c => c.id != state.playerCountryId && c.id != finding.sponsorId).id;
                if (action == "share")
                {
                    var pact = new Treaty { countryA = state.playerCountryId, countryB = ally, signedDate = state.date };
                    pact.commitments.Add(TreatyCommitment.IntelligenceSharing); state.treaties.Add(pact);
                }
                Func<bool> send = () => action == "share" ? gc.ShareSponsorshipFinding(finding.Key, ally)
                    : action.StartsWith("confront") ? gc.ConfrontSponsorshipFinding(finding.Key)
                    : gc.BargainSponsorshipFinding(finding.Key, TreatyCommitment.Transit);
                state.commandPoints.current = 0;
                string before = SaveSystem.ToJson(state);
                Assert.IsFalse(send()); Assert.AreEqual(before, SaveSystem.ToJson(state));
                state.commandPoints.current = 10;
                int initiatives = state.initiativesThisYear;
                Assert.AreEqual(accepts, send());
                Assert.AreEqual(10 - cost, state.commandPoints.current);
                Assert.AreEqual(initiatives + (accepts ? 1 : 0), state.initiativesThisYear);
                var loaded = SaveSystem.Load(0);
                Assert.AreEqual(SaveSystem.ToJson(state), SaveSystem.ToJson(loaded));
                before = SaveSystem.ToJson(state);
                Assert.IsFalse(send()); Assert.AreEqual(before, SaveSystem.ToJson(state));
            }
            finally
            {
                SaveSystem.SaveDirectoryOverride = oldDirectory;
                typeof(GameController).GetProperty("State").SetValue(gc, previous);
                typeof(GameController).GetProperty("Turns").SetValue(gc, previousTurns);
                System.IO.Directory.Delete(directory, true);
            }
        }

        [Test]
        public void SilenceValueCanCarryARealOfferAtTheMarginWithoutPublishing()
        {
            var movement = PlantFindingEvidence(); SponsorshipFindings.Discover(state);
            var finding = state.sponsorshipFindings[0];
            FindingDiplomaticClimate(50f);
            var relationship = state.FindRelationship(state.playerCountryId, finding.sponsorId);
            var clauses = new List<TreatyClause> { new TreatyClause { commitment = TreatyCommitment.Transit, side = ClauseSide.TheyProvide } };
            bool found = false;
            for (int warmth = 0; warmth <= 100; warmth++)
            {
                relationship.relations = warmth;
                float bare = DiplomacySystem.TreatyWillingness(state, state.playerCountryId, finding.sponsorId, clauses);
                if (bare < 50f && bare + SponsorshipFindings.SilenceValue(state, finding.Key) >= 50f) { found = true; break; }
            }
            Assert.IsTrue(found, "Fixture has no margin where the finding matters.");
            Assert.IsTrue(SponsorshipFindings.Bargain(state, finding.Key, TreatyCommitment.Transit));
            Assert.IsFalse(movement.sponsorExposed);
            Assert.IsTrue(finding.silencePromised);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PrivateConfrontationClosesOnlyAnAcceptedChannelAndCannotBeReplayed(bool accepted)
        {
            var movement = PlantFindingEvidence();
            SponsorshipFindings.Discover(state);
            var finding = state.sponsorshipFindings[0];
            FindingDiplomaticClimate(accepted ? 100f : 0f);
            Assert.AreEqual(accepted, DiplomacySystem.TreatyWillingness(state, state.playerCountryId,
                movement.sponsorId, new List<TreatyCommitment>()) >= 50f, "Fixture must separate willingness.");
            Assert.AreEqual(accepted, SponsorshipFindings.Confront(state, finding.Key));
            Assert.AreEqual(accepted ? "" : finding.sponsorId, movement.sponsorId);
            Assert.IsTrue(finding.confronted);
            Assert.IsFalse(movement.sponsorExposed);
            Assert.AreEqual(40f, movement.strength); Assert.AreEqual(20f, movement.armsSupplied);
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(SponsorshipFindings.Confront(state, finding.Key));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [Test]
        public void SharingRequiresAnActiveOutboundClauseAndGivesOnlyTheChosenPartnerKnowledge()
        {
            var movement = PlantFindingEvidence();
            SponsorshipFindings.Discover(state);
            var finding = state.sponsorshipFindings[0];
            var ally = state.countries.Find(c => c.id != state.playerCountryId && c.id != finding.sponsorId);
            FindingDiplomaticClimate(100f);
            Assert.IsFalse(SponsorshipFindings.CanShare(state, finding.Key, ally.id, out _));
            var treaty = new Treaty { countryA = state.playerCountryId, countryB = ally.id, signedDate = state.date };
            treaty.commitments.Add(TreatyCommitment.IntelligenceSharing);
            treaty.clauses.Add(new TreatyClause { commitment = TreatyCommitment.IntelligenceSharing, side = ClauseSide.TheyProvide });
            state.treaties.Add(treaty);
            Assert.IsFalse(SponsorshipFindings.CanShare(state, finding.Key, ally.id, out _));
            treaty.clauses[0].side = ClauseSide.WeProvide;
            Assert.IsTrue(SponsorshipFindings.Share(state, finding.Key, ally.id));
            Assert.IsTrue(InsurgencySystem.KnownSponsor(state, ally.id, movement));
            Assert.IsFalse(movement.sponsorExposed);
            Assert.AreEqual(2, state.sponsorshipFindings.Count);
            Assert.AreEqual(finding.discovered, SponsorshipFindings.Find(state, ally.id, finding.Key).discovered);
            Assert.IsFalse(SponsorshipFindings.CanBargain(state, finding.Key, TreatyCommitment.Transit, out _));
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(SponsorshipFindings.Share(state, finding.Key, ally.id));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            treaty.broken = true;
            Assert.IsFalse(SponsorshipFindings.CanShare(state, finding.Key, ally.id, out _));
        }

        [Test]
        public void SilenceValuationUsesTheClampedOrdinaryPublicationTrustLoss()
        {
            var movement = PlantFindingEvidence();
            SponsorshipFindings.Discover(state);
            var finding = state.sponsorshipFindings[0];
            foreach (var r in state.relationships) r.trust = r.Involves(state.playerCountryId) ? 2f : 1f;
            var copy = SaveSystem.FromJson(SaveSystem.ToJson(state));
            InsurgencySystem.Attribute(copy, copy.insurgencies.Find(i => i.id == movement.id),
                copy.FindLocation(movement.locationId), copy.PlayerCountry, copy.FindCountry(movement.sponsorId));
            float sum = 0f; int count = 0;
            foreach (var r in state.relationships)
                if (r.Involves(movement.sponsorId))
                { sum += r.trust - copy.FindRelationship(r.countryA, r.countryB).trust; count++; }
            string before = SaveSystem.ToJson(state);
            Assert.AreEqual(Math.Min(12f, sum / count), SponsorshipFindings.SilenceValue(state, finding.Key), 0.00001f);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SilenceBargainWritesTheirClauseOnceAndBlocksOurPublication(bool extending)
        {
            var movement = PlantFindingEvidence();
            SponsorshipFindings.Discover(state);
            var finding = state.sponsorshipFindings[0];
            FindingDiplomaticClimate(100f);
            if (extending)
            {
                var old = new Treaty { countryA = finding.sponsorId, countryB = state.playerCountryId, signedDate = state.date };
                old.commitments.Add(TreatyCommitment.NonAggression);
                state.treaties.Add(old);
            }
            int initiatives = state.initiativesThisYear;
            Assert.IsTrue(SponsorshipFindings.Bargain(state, finding.Key, TreatyCommitment.Transit));
            Assert.IsTrue(finding.silencePromised);
            var treaty = state.FindTreaty(state.playerCountryId, finding.sponsorId);
            Assert.IsTrue(treaty.Carries(finding.sponsorId, TreatyCommitment.Transit));
            Assert.IsFalse(treaty.Carries(state.playerCountryId, TreatyCommitment.Transit));
            Assert.AreEqual(initiatives + 1, state.initiativesThisYear);
            Assert.AreEqual(finding.sponsorId, movement.sponsorId);
            Assert.IsFalse(movement.sponsorExposed);
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            string before = SaveSystem.ToJson(loaded);
            Assert.IsFalse(SponsorshipFindings.Expose(loaded, finding.Key));
            Assert.IsFalse(SponsorshipFindings.Bargain(loaded, finding.Key, TreatyCommitment.IntelligenceSharing));
            Assert.AreEqual(before, SaveSystem.ToJson(loaded));
        }

        [Test]
        public void RejectedSilenceOfferKeepsEvidenceAndMovementButCannotFarmRepeatedOffers()
        {
            var movement = PlantFindingEvidence();
            SponsorshipFindings.Discover(state);
            var finding = state.sponsorshipFindings[0];
            FindingDiplomaticClimate(0f);
            int initiatives = state.initiativesThisYear;
            Assert.IsFalse(SponsorshipFindings.Bargain(state, finding.Key, TreatyCommitment.Transit));
            Assert.IsTrue(finding.bargainAttempted); Assert.IsFalse(finding.silencePromised);
            Assert.AreEqual(0, state.treaties.Count); Assert.AreEqual(initiatives, state.initiativesThisYear);
            Assert.IsTrue(SponsorshipFindings.CanExpose(state, finding.Key, out _));
            Assert.IsFalse(SponsorshipFindings.CanBargain(state, finding.Key, TreatyCommitment.NonAggression, out _));
            Assert.IsTrue(SponsorshipFindings.File(state, finding.Key));
            Assert.IsTrue(SponsorshipFindings.Reopen(state, finding.Key));
            Assert.IsFalse(finding.filed);
            Assert.IsFalse(SponsorshipFindings.CanBargain(state, finding.Key, TreatyCommitment.Transit, out _));
        }

        [Test]
        public void SoldSilenceBlocksOurSharingButDoesNotDisableOrdinaryAttribution()
        {
            var movement = PlantFindingEvidence(); SponsorshipFindings.Discover(state);
            var finding = state.sponsorshipFindings[0];
            FindingDiplomaticClimate(100f);
            var ally = state.countries.Find(c => c.id != state.playerCountryId && c.id != finding.sponsorId);
            var pact = new Treaty { countryA = state.playerCountryId, countryB = ally.id, signedDate = state.date };
            pact.commitments.Add(TreatyCommitment.IntelligenceSharing); state.treaties.Add(pact);
            Assert.IsTrue(SponsorshipFindings.CanShare(state, finding.Key, ally.id, out _));
            Assert.IsTrue(SponsorshipFindings.Bargain(state, finding.Key, TreatyCommitment.Transit));
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(SponsorshipFindings.Share(state, finding.Key, ally.id));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            InsurgencySystem.Attribute(state, movement, state.FindLocation(movement.locationId),
                state.PlayerCountry, state.FindCountry(finding.sponsorId));
            Assert.IsTrue(movement.sponsorExposed);
            Assert.IsTrue(state.FindTreaty(state.playerCountryId, finding.sponsorId)
                .Carries(finding.sponsorId, TreatyCommitment.Transit));
        }

        [TestCase("thin")]
        [TestCase("compromised")]
        [TestCase("no-forensics")]
        [TestCase("foreign-ground")]
        [TestCase("public")]
        public void PrivateFindingRequiresForensicCollectionAndOurGround(string gate)
        {
            var movement = PlantFindingEvidence();
            if (gate == "thin") state.FindNetwork(state.playerCountryId, movement.sponsorId).penetration = 64.99f;
            if (gate == "compromised") state.FindNetwork(state.playerCountryId, movement.sponsorId).compromised = true;
            if (gate == "no-forensics") state.PlayerCountry.technology.capabilities.RemoveAll(c => c.capabilityId == "CAP_FORENSICS");
            if (gate == "foreign-ground") state.FindLocation(movement.locationId).ownerId = movement.sponsorId;
            if (gate == "public") movement.sponsorExposed = true;
            string before = SaveSystem.ToJson(state);
            SponsorshipFindings.Discover(state);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [Test]
        public void MonthlyDiscoveryIsDatedPrivateDurableAndDoesNotPromoteAnalystAccuracy()
        {
            var movement = PlantFindingEvidence();
            var misleading = new IntelProduct { observerId = state.playerCountryId, targetId = movement.sponsorId,
                delivered = true, accurate = false, question = EstimateQuestion.SubversionSponsorship,
                commissioned = state.date, answer = "No evidence." };
            state.intelProducts.Add(misleading);
            Assert.IsFalse(InsurgencySystem.KnownSponsor(state, state.playerCountryId, movement));
            IntelProductSystem.MonthlyUpdate(state);
            var findings = SponsorshipFindings.For(state, state.playerCountryId);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(state.date, findings[0].discovered);
            Assert.IsFalse(movement.sponsorExposed);
            Assert.IsTrue(InsurgencySystem.KnownSponsor(state, state.playerCountryId, movement));
            Assert.IsFalse(misleading.accurate);
            Assert.AreEqual("No evidence.", misleading.answer);
            string before = SaveSystem.ToJson(state);
            SponsorshipFindings.Discover(state);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            var loaded = SaveSystem.FromJson(before);
            Assert.AreEqual(1, SponsorshipFindings.For(loaded, loaded.playerCountryId).Count);
            var legacy = SaveSystem.FromJson(before.Replace("\"sponsorshipFindings\"", "\"omittedFindings\""));
            Assert.AreEqual(0, SponsorshipFindings.For(legacy, legacy.playerCountryId).Count);
        }

        [Test]
        public void ExposureAppliesExactlyOrdinaryAttributionOnce()
        {
            var movement = PlantFindingEvidence();
            SponsorshipFindings.Discover(state);
            var finding = SponsorshipFindings.For(state, state.playerCountryId)[0];
            var control = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var controlMovement = control.insurgencies.Find(i => i.id == movement.id);
            InsurgencySystem.Attribute(control, controlMovement, control.FindLocation(movement.locationId),
                control.PlayerCountry, control.FindCountry(movement.sponsorId));
            Assert.IsTrue(SponsorshipFindings.Expose(state, finding.Key));
            control.sponsorshipFindings[0].exposed = true;
            Assert.AreEqual(SaveSystem.ToJson(control), SaveSystem.ToJson(state));
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(SponsorshipFindings.Expose(state, finding.Key));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            Assert.AreEqual(40f, movement.strength);
            Assert.AreEqual(20f, movement.armsSupplied);
        }

        [Test]
        public void FiledEvidenceStaysKnownButStaleEvidenceCannotExposeANewSponsor()
        {
            var movement = PlantFindingEvidence();
            SponsorshipFindings.Discover(state);
            var finding = SponsorshipFindings.For(state, state.playerCountryId)[0];
            Assert.IsTrue(SponsorshipFindings.File(state, finding.Key));
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(SponsorshipFindings.File(state, finding.Key));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            Assert.IsTrue(InsurgencySystem.KnownSponsor(state, state.playerCountryId, movement));
            movement.sponsorId = state.countries.Find(c => c.id != state.playerCountryId && c.id != finding.sponsorId).id;
            Assert.IsFalse(InsurgencySystem.KnownSponsor(state, state.playerCountryId, movement));
            before = SaveSystem.ToJson(state);
            Assert.IsFalse(SponsorshipFindings.Expose(state, finding.Key));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            Assert.AreEqual("", SponsorshipFindings.Describe(state, new SponsorshipFinding { observerId = movement.sponsorId }));
        }

        [Test]
        public void NothingCanBeAskedWithoutANetwork()
        {
            var target = SomeoneElse();
            Assert.IsFalse(IntelProductSystem.CanCommission(state, state.playerCountryId,
                    target.id, EstimateQuestion.StrategicIntent, out string reason),
                "A service with no collection anywhere answered a question about a foreign "
                + "state. That is a free oracle, not intelligence.");
            Assert.IsNotEmpty(reason, "A refusal the operator cannot see is a broken button.");

            Assert.IsNull(IntelProductSystem.CommissionBy(state, state.playerCountryId,
                    target.id, EstimateQuestion.StrategicIntent),
                "The gate and the verb disagreed — they must be the same function.");
        }

        [Test]
        public void TheShopHasACapacity()
        {
            var target = SomeoneElse();
            GiveUsANetwork(target.id, 50f);

            Assert.IsNotNull(IntelProductSystem.CommissionBy(state, state.playerCountryId,
                target.id, EstimateQuestion.StrategicIntent));
            Assert.IsNotNull(IntelProductSystem.CommissionBy(state, state.playerCountryId,
                target.id, EstimateQuestion.TreatyReliability));

            Assert.IsFalse(IntelProductSystem.CanCommission(state, state.playerCountryId,
                    target.id, EstimateQuestion.TheirReadOfUs, out _),
                $"More than {IntelProductSystem.MaxOutstanding} assessments ran at once. An "
                + "analytical shop with no capacity limit makes the CP cost the only "
                + "constraint, and CP is not what is scarce here.");
        }

        [Test]
        public void TheSameQuestionIsNotAskedTwiceAtOnce()
        {
            var target = SomeoneElse();
            GiveUsANetwork(target.id, 50f);

            IntelProductSystem.CommissionBy(state, state.playerCountryId,
                target.id, EstimateQuestion.StrategicIntent);

            Assert.IsFalse(IntelProductSystem.CanCommission(state, state.playerCountryId,
                    target.id, EstimateQuestion.StrategicIntent, out _),
                "The same question was accepted twice while the first was still running, which "
                + "is a way to buy two rolls at one answer.");
        }

        // ---------- it takes time, and it arrives ----------

        [Test]
        public void AnAssessmentTakesMonthsAndThenArrives()
        {
            var target = SomeoneElse();
            GiveUsANetwork(target.id, 60f);

            var product = IntelProductSystem.CommissionBy(state, state.playerCountryId,
                target.id, EstimateQuestion.StrategicIntent);

            Assert.IsFalse(product.delivered, "It answered instantly.");

            for (int month = 0; month < IntelProductSystem.BaseMonths; month++) turns.EndMonth();

            Assert.IsTrue(product.delivered,
                $"{IntelProductSystem.BaseMonths} months on, the assessment had still not "
                + "landed — so nothing the operator commissions ever comes back.");
            Assert.IsNotEmpty(product.answer, "It arrived with nothing written on it.");
        }

        // ---------- the answer is fixed once given ----------

        [Test]
        public void TheJudgementDoesNotChangeOnceDelivered()
        {
            // A number that flickers on refresh is unusable, and averaging
            // repeated reads would leak the true value. The `MilitaryAdvice`
            // precedent, applied to a written judgement.
            var target = SomeoneElse();
            GiveUsANetwork(target.id, 60f);

            var product = IntelProductSystem.CommissionBy(state, state.playerCountryId,
                target.id, EstimateQuestion.StrategicIntent);
            for (int month = 0; month < IntelProductSystem.BaseMonths; month++) turns.EndMonth();

            string first = product.answer;
            var grade = product.confidence;

            for (int month = 0; month < 6; month++) turns.EndMonth();

            Assert.AreEqual(first, product.answer,
                "The delivered judgement rewrote itself months later.");
            Assert.AreEqual(grade, product.confidence, "The confidence grade drifted after delivery.");
        }

        [Test]
        public void TwoIdenticalWorldsProduceTheIdenticalJudgement()
        {
            // Deterministic per (observer, target, question, month), so reloading
            // cannot shake a different answer loose.
            string Run()
            {
                var world = WorldFactory.CreateDebugWorld(seed: 7714);
                var runTurns = new TurnManager(world);
                SimulationPipeline.Wire(runTurns, world);

                var subject = world.countries.Find(c => !c.isPlayer);
                world.networks.Add(new IntelNetwork
                {
                    ownerId = world.playerCountryId,
                    targetId = subject.id,
                    focus = IntelDomain.Political,
                    penetration = 55f
                });

                var product = IntelProductSystem.CommissionBy(world, world.playerCountryId,
                    subject.id, EstimateQuestion.StrategicIntent);
                for (int month = 0; month < IntelProductSystem.BaseMonths; month++)
                    runTurns.EndMonth();
                return product.answer;
            }

            string a = Run();
            string b = Run();

            Assert.IsNotEmpty(a, "the fixture delivered nothing, so this compared two blanks");
            Assert.AreEqual(a, b, "The same commission in the same world gave two answers.");
        }

        // ---------- a poor service is plausibly wrong ----------

        [Test]
        public void GoodCollectionIsRightFarMoreOftenThanNone()
        {
            // The property that makes buying intelligence worth doing. Measured
            // across many commissions rather than one, because a single draw
            // says nothing about a probability.
            int SharpHits(float penetration, ConfidenceGrade grade)
            {
                int correct = 0;
                for (int i = 0; i < 40; i++)
                {
                    var world = WorldFactory.CreateDebugWorld(seed: 9000 + i);
                    var runTurns = new TurnManager(world);
                    SimulationPipeline.Wire(runTurns, world);

                    var subject = world.countries.Find(c => !c.isPlayer);
                    world.networks.Add(new IntelNetwork
                    {
                        ownerId = world.playerCountryId,
                        targetId = subject.id,
                        focus = IntelDomain.Political,
                        penetration = penetration
                    });

                    // Pin the grade the analysis is scored against, so this
                    // measures the accuracy curve rather than how fast a network
                    // happens to deepen.
                    var estimate = new IntelEstimate
                    {
                        observerId = world.playerCountryId,
                        targetId = subject.id,
                        domain = IntelDomain.Political,
                        confidence = grade,
                        everCollected = true,
                        asOf = world.date
                    };
                    world.estimates.Add(estimate);

                    var product = IntelProductSystem.CommissionBy(world, world.playerCountryId,
                        subject.id, EstimateQuestion.StrategicIntent);

                    for (int month = 0; month < IntelProductSystem.BaseMonths; month++)
                    {
                        estimate.confidence = grade;   // hold it against collection drift
                        runTurns.EndMonth();
                    }

                    if (product.accurate) correct++;
                }
                return correct;
            }

            int blind = SharpHits(4f, ConfidenceGrade.None);
            int sharp = SharpHits(85f, ConfidenceGrade.Confirmed);

            Assert.Greater(sharp, blind,
                $"A service with deep, confident access was right {sharp}/40 against {blind}/40 "
                + "for one with almost nothing. If collection does not sharpen the judgement, "
                + "there is no reason to buy any.");
        }

        [Test]
        public void AWrongAnswerIsPlausibleRatherThanNoise()
        {
            // **The load-bearing property.** A poor service must return a
            // different defensible conclusion, stated exactly like a good one —
            // noise would be obviously worthless and therefore free to ignore,
            // and an operator who can spot the bad assessments is not being
            // asked to trust anybody.
            var descriptions = new HashSet<string>();
            foreach (StrategicPath path in Enum.GetValues(typeof(StrategicPath)))
                descriptions.Add(AIStrategy.Describe(path));

            int checkedAnswers = 0;
            for (int i = 0; i < 30; i++)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 4400 + i);
                var runTurns = new TurnManager(world);
                SimulationPipeline.Wire(runTurns, world);

                var subject = world.countries.Find(c => !c.isPlayer);
                world.networks.Add(new IntelNetwork
                {
                    ownerId = world.playerCountryId,
                    targetId = subject.id,
                    focus = IntelDomain.Political,
                    penetration = 3f
                });

                var product = IntelProductSystem.CommissionBy(world, world.playerCountryId,
                    subject.id, EstimateQuestion.StrategicIntent);
                for (int month = 0; month < IntelProductSystem.BaseMonths; month++)
                    runTurns.EndMonth();

                if (!product.delivered) continue;
                if (product.answer.StartsWith("No coherent")) continue;

                checkedAnswers++;
                bool namesARealPath = false;
                foreach (string description in descriptions)
                    if (product.answer.Contains(description)) namesARealPath = true;

                Assert.IsTrue(namesARealPath,
                    $"A wrong assessment did not name a real strategy: \"{product.answer}\". "
                    + "It has to be a conclusion the operator could act on and be wrong about.");
            }

            Assert.Greater(checkedAnswers, 10,
                "the fixture delivered almost nothing, so this asserted nothing");
        }

        // ---------- it survives a save ----------

        [Test]
        public void AnAssessmentSurvivesASaveRoundTrip()
        {
            var target = SomeoneElse();
            GiveUsANetwork(target.id, 60f);

            var product = IntelProductSystem.CommissionBy(state, state.playerCountryId,
                target.id, EstimateQuestion.TreatyReliability);
            for (int month = 0; month < IntelProductSystem.BaseMonths; month++) turns.EndMonth();

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var restored = loaded.intelProducts.Find(p =>
                p.IsFor(loaded.playerCountryId, target.id, EstimateQuestion.TreatyReliability));

            Assert.IsNotNull(restored, "The assessment did not survive the save at all.");
            Assert.AreEqual(product.answer, restored.answer,
                "The judgement changed across a save — which is a reload that shakes a "
                + "different answer loose, the thing this game refuses.");
            Assert.AreEqual(product.confidence, restored.confidence);
        }
    }
}
