using System.Collections.Generic;
using System.IO;
using Brink.Core;
using Brink.Data;
using Brink.UI;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The negotiating table respects the fog (GDD §26, spec 01 §5a).
    ///
    /// `PeaceSystem.WouldAccept` and `ConfrontationSystem.OpponentWouldAccept`
    /// are ground truth about a foreign government's decision. The audit found
    /// the settlement console printing both — the exact maximal term list under
    /// "THEY WOULD SIGN THIS TODAY", the true willingness bit as "OPEN TO
    /// TERMS", the opponent's exact exhaustion, and exact per-operation enemy
    /// losses — with no collection at all. This fixture holds the player-facing
    /// surface to the assessment layer and pins the two properties that make
    /// an assessment honest: identical reporting gives an identical read, and
    /// better reporting gives a sharper one.
    /// </summary>
    public class SettlementFogTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 4747);
        }

        // ---------- the rule, enforced at the source ----------

        static readonly string[] OracleIdentifiers =
        {
            "BestAcceptableProposal",
            "OpponentWouldAccept",
            "WouldAcceptTermsFrom",
            "SettlementWillingnessFor",
            "PeaceSystem.WouldAccept",
            "defenderWarExhaustion",
            "initiatorWarExhaustion",
            ".defenderLosses",
        };

        /// <summary>
        /// Files that are allowed to read the truth because they are the fog
        /// boundary itself, not a reader of it. Each one turns a true value
        /// into a band or a grade before anything player-facing sees it.
        /// </summary>
        static readonly string[] FogBoundaryFiles = { "IntelReadout.cs" };

        [Test]
        public void NoPlayerFacingCodeReadsTheAcceptanceOracle()
        {
            // Unity runs with the project folder as the working directory (the
            // PipelineWiringTests convention); the dotnet harness runs from
            // Tools/dotnet-harness/tests, so walk up until the project is found.
            string root = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Scripts");
            for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
                 !Directory.Exists(root) && dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "Brink", "Assets", "Scripts");
                if (Directory.Exists(candidate)) root = candidate;
            }
            Assert.IsTrue(Directory.Exists(root), $"cannot find the runtime sources from {Directory.GetCurrentDirectory()}");

            var files = new List<string>(Directory.GetFiles(Path.Combine(root, "UI"), "*.cs", SearchOption.AllDirectories));
            files.Add(Path.Combine(root, "Core", "AttentionSystem.cs"));

            var offences = new List<string>();
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (System.Array.IndexOf(FogBoundaryFiles, name) >= 0) continue;

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimStart();
                    if (line.StartsWith("//") || line.StartsWith("///")) continue;
                    foreach (string identifier in OracleIdentifiers)
                        if (line.Contains(identifier))
                            offences.Add($"{name}:{i + 1} reads {identifier}");
                }
            }

            Assert.IsEmpty(offences,
                "Player-facing code reads the settlement oracle directly. Route it through " +
                "PeaceSystem.Assess / AssessDisposition / RecommendedProposal or IntelReadout:\n"
                + string.Join("\n", offences));
        }

        // ---------- fixtures ----------

        Confrontation WarWith(string opponentId)
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, opponentId,
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);
            return confrontation;
        }

        void Reporting(string targetId, ConfidenceGrade grade)
        {
            var existing = state.FindEstimate(state.playerCountryId, targetId, IntelDomain.Political);
            if (existing != null) state.estimates.Remove(existing);
            if (grade == ConfidenceGrade.None) return;
            state.estimates.Add(new IntelEstimate
            {
                observerId = state.playerCountryId,
                targetId = targetId,
                domain = IntelDomain.Political,
                confidence = grade,
                everCollected = true,
                reportedValue = 50f,
            });
        }

        /// <summary>Drive the hidden willingness to roughly a chosen level.</summary>
        void SetWillingness(Confrontation confrontation, float exhaustion, float warSupport)
        {
            confrontation.defenderWarExhaustion = exhaustion;
            confrontation.momentum = 0f;
            confrontation.escalationPressure = 0f;
            state.FindCountry(confrontation.defenderId).warSupport = warSupport;
        }

        // ---------- identical reporting, identical read ----------

        [Test]
        public void IdenticalReportingGivesAnIdenticalRead_WhateverTheHiddenWillingness()
        {
            var confrontation = WarWith("CHN");
            Reporting("CHN", ConfidenceGrade.Low);

            SetWillingness(confrontation, 40f, 60f);
            float w1 = ConfrontationSystem.SettlementWillingnessFor(state, confrontation, state.playerCountryId);
            var read1 = PeaceSystem.AssessDisposition(state, confrontation, state.playerCountryId);
            var rec1 = PeaceSystem.RecommendedProposal(state, confrontation, state.playerCountryId, out var out1);

            SetWillingness(confrontation, 52f, 60f);
            float w2 = ConfrontationSystem.SettlementWillingnessFor(state, confrontation, state.playerCountryId);
            var read2 = PeaceSystem.AssessDisposition(state, confrontation, state.playerCountryId);
            var rec2 = PeaceSystem.RecommendedProposal(state, confrontation, state.playerCountryId, out var out2);

            Assert.AreNotEqual(w1, w2, "the hidden willingness did not move, so this proves nothing");
            Assert.AreEqual(read1, read2,
                "two hidden willingness values our reporting cannot separate produced different reads " +
                "— the read is a side-channel onto the hidden figure");
            Assert.AreEqual(out1, out2);
            Assert.AreEqual(rec1 == null, rec2 == null);
            if (rec1 != null) CollectionAssert.AreEqual(rec1.terms, rec2.terms);
        }

        [Test]
        public void NoCollectionMeansNoRead_HoweverWillingTheyAre()
        {
            var confrontation = WarWith("CHN");
            Reporting("CHN", ConfidenceGrade.None);

            SetWillingness(confrontation, 95f, 5f);
            Assume.That(ConfrontationSystem.OpponentWouldAccept(state, confrontation), Is.True,
                "the fixture needs an opponent who truly wants out");
            Assume.That(PeaceSystem.BestAcceptableProposal(state, confrontation, state.playerCountryId),
                Is.Not.Null, "the oracle must have an answer for this test to mean anything");

            Assert.AreEqual(SettlementDisposition.Unknown,
                PeaceSystem.AssessDisposition(state, confrontation, state.playerCountryId));
            Assert.IsNull(PeaceSystem.RecommendedProposal(state, confrontation, state.playerCountryId, out var outlook),
                "a recommendation was drafted with no reporting on their politics — that is the oracle");
            Assert.AreEqual(SettlementOutlook.Unknown, outlook);
            Assert.AreEqual("NO READ", IntelReadout.ForeignExhaustion(state, confrontation));

            SetWillingness(confrontation, 0f, 100f);
            Assert.AreEqual(SettlementDisposition.Unknown,
                PeaceSystem.AssessDisposition(state, confrontation, state.playerCountryId),
                "with no collection the read must not change when the truth does");
        }

        // ---------- better reporting, sharper read ----------

        [Test]
        public void BetterCollectionSharpensTheRead_WithoutRevealingTheFigure()
        {
            var confrontation = WarWith("CHN");

            // Two situations a coarse read cannot tell apart: one squarely
            // receptive, one desperate. Preconditions asserted, so a retune of
            // the willingness formula fails loudly here rather than passing on
            // a fixture that no longer straddles the fine boundary.
            SetWillingness(confrontation, 75f, 25f);
            float wA = ConfrontationSystem.SettlementWillingnessFor(state, confrontation, state.playerCountryId);
            Assume.That(wA, Is.GreaterThanOrEqualTo(35f).And.LessThan(60f), $"fixture A willingness {wA}");
            Reporting("CHN", ConfidenceGrade.Low);
            var coarseA = PeaceSystem.AssessDisposition(state, confrontation, state.playerCountryId);
            Reporting("CHN", ConfidenceGrade.Confirmed);
            var fineA = PeaceSystem.AssessDisposition(state, confrontation, state.playerCountryId);

            SetWillingness(confrontation, 95f, 10f);
            float wB = ConfrontationSystem.SettlementWillingnessFor(state, confrontation, state.playerCountryId);
            Assume.That(wB, Is.GreaterThanOrEqualTo(60f), $"fixture B willingness {wB}");
            Reporting("CHN", ConfidenceGrade.Low);
            var coarseB = PeaceSystem.AssessDisposition(state, confrontation, state.playerCountryId);
            Reporting("CHN", ConfidenceGrade.Confirmed);
            var fineB = PeaceSystem.AssessDisposition(state, confrontation, state.playerCountryId);

            Assert.AreEqual(coarseA, coarseB, "the coarse read should not separate these");
            Assert.AreNotEqual(fineA, fineB, "confirmed reporting should separate them");
            Assert.Greater((int)fineB, (int)fineA, "the more willing opponent reads as more receptive");

            // ...and the read is never the number. Confirmed narrows the
            // exhaustion bin; it does not print the value.
            string band = IntelReadout.ForeignExhaustion(state, confrontation);
            StringAssert.DoesNotContain("95.0", band);
            StringAssert.Contains("–", band);
            Assert.Less(IntelReadout.ExhaustionBinFor(ConfidenceGrade.Confirmed),
                IntelReadout.ExhaustionBinFor(ConfidenceGrade.Low));
            Assert.AreEqual(0, IntelReadout.ExhaustionBinFor(ConfidenceGrade.None));
        }

        [Test]
        public void TheRecommendationIsHonest_AtEveryGrade()
        {
            var confrontation = WarWith("IND");
            SetWillingness(confrontation, 95f, 5f);

            // With confirmed reporting the recommendation is one they take.
            Reporting("IND", ConfidenceGrade.Confirmed);
            var deal = PeaceSystem.RecommendedProposal(state, confrontation, state.playerCountryId, out var outlook);
            Assert.IsNotNull(deal);
            Assert.AreEqual(SettlementOutlook.Likely, outlook);
            Assert.IsTrue(PeaceSystem.WouldAccept(state, confrontation, state.playerCountryId, deal),
                "a recommendation labelled LIKELY on confirmed reporting should be one they sign");

            // With poor reporting the staff gives more ground than necessary
            // (or declines to promise) — never more certainty than they have.
            Reporting("IND", ConfidenceGrade.Low);
            var cautious = PeaceSystem.RecommendedProposal(state, confrontation, state.playerCountryId, out var lowOutlook);
            Assert.AreNotEqual(SettlementOutlook.Unknown, lowOutlook);
            if (cautious != null)
                Assert.LessOrEqual(
                    PeaceSystem.ProposalCost(state, confrontation, state.playerCountryId, cautious),
                    PeaceSystem.ProposalCost(state, confrontation, state.playerCountryId, deal) + 0.001f,
                    "poor reporting recommended a harder bargain than confirmed reporting did");
        }

        [Test]
        public void TheAssessmentNeverAnswersTheExactSign()
        {
            // At every grade there is a band of margin the read cannot resolve,
            // so a well-collected operator cannot walk the term list to the
            // exact acceptance boundary.
            foreach (ConfidenceGrade grade in System.Enum.GetValues(typeof(ConfidenceGrade)))
            {
                if (grade == ConfidenceGrade.None) continue;
                Assert.Greater(PeaceSystem.DeadBandFor(grade), 0f,
                    $"{grade} reporting resolves the exact acceptance boundary");
            }
            Assert.Less(PeaceSystem.DeadBandFor(ConfidenceGrade.Confirmed),
                PeaceSystem.DeadBandFor(ConfidenceGrade.Low),
                "better reporting should narrow the band of doubt");
        }

        // ---------- the after-action log ----------

        [Test]
        public void PerOperationEnemyLossesAreBandedAndSidedCorrectly()
        {
            var confrontation = WarWith("RUS");
            var ours = new OperationRecord { attackerId = state.playerCountryId, attackerLosses = 3f, defenderLosses = 7f };
            var theirs = new OperationRecord { attackerId = "RUS", attackerLosses = 3f, defenderLosses = 7f };

            string ourLine = IntelReadout.OperationLosses(state, confrontation, ours);
            string theirLine = IntelReadout.OperationLosses(state, confrontation, theirs);

            StringAssert.Contains("OWN " + IntelReadout.OwnCasualties(3f), ourLine);
            StringAssert.Contains("OWN " + IntelReadout.OwnCasualties(7f), theirLine,
                "when they attacked us, the defender's losses are ours");
            StringAssert.DoesNotContain("7.0", ourLine);
            StringAssert.Contains("ENEMY (EST)", ourLine);
            StringAssert.Contains("–", ourLine, "enemy losses must be a band, not a figure");
        }
    }
}
