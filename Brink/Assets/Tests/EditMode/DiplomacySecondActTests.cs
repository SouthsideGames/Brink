using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Diplomacy's second act (spec 04 §5a, spec 02 §4a): treaties can deepen,
    /// and sanctions can end at a table.
    ///
    /// Both were found by playing. A treaty could never be amended, so the
    /// first signature per pair was the last — a campaign ended with fourteen
    /// treaties and one defence pact, the pact possible only where signing had
    /// been deliberately refused for years. And sanctions suppress the very
    /// relations their automatic lapse requires, a self-locking cycle with no
    /// verb to break it — measured: a pariah great power sanctioned 237 of 240
    /// months, and 40–60 standing AI-AI regimes grinding the world.
    /// </summary>
    public class DiplomacySecondActTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 8642);
            turns = new TurnManager(state);
            state.commandPoints.current = 40;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        Treaty SignSoftTreatyWith(string partnerId)
        {
            var treaty = new Treaty
            {
                id = $"T_{partnerId}",
                countryA = state.playerCountryId,
                countryB = partnerId,
                signedDate = state.startDate
            };
            treaty.commitments.Add(TreatyCommitment.NonAggression);
            treaty.commitments.Add(TreatyCommitment.TradePreference);
            state.treaties.Add(treaty);
            return treaty;
        }

        Relationship Warm(string partnerId, float relations = 78f, float trust = 65f)
        {
            var relationship = state.FindRelationship(state.playerCountryId, partnerId);
            relationship.relations = relations;
            relationship.trust = trust;
            relationship.strategicAlignment = 70f;
            return relationship;
        }

        // ---------- treaties deepen ----------

        [Test]
        public void AFriendshipCanGrowIntoAnAlliance()
        {
            var treaty = SignSoftTreatyWith("AUS");
            Warm("AUS");
            state.date = new GameDate(state.startDate.year + 3, 1); // history counts

            bool deepened = DiplomacySystem.DeepenTreatyBy(state, state.playerCountryId, "AUS",
                new List<TreatyCommitment> { TreatyCommitment.MutualDefense });

            Assert.IsTrue(deepened,
                "A warm, trusted, three-year partner refused a defence pact. If a treaty can " +
                "never be amended, the first signature per pair is the last — and a friendship " +
                "can never become an alliance, which a campaign proved by ending with fourteen " +
                "treaties and one pact.");
            Assert.IsTrue(treaty.Has(TreatyCommitment.MutualDefense));

            int treatiesWithPartner = 0;
            foreach (var t in state.treaties)
                if (t.Involves("AUS") && t.Involves(state.playerCountryId)) treatiesWithPartner++;
            Assert.AreEqual(1, treatiesWithPartner, "Deepening created a second treaty instead of growing the first.");
        }

        [Test]
        public void AColdPartnerDeclinesToDeepen()
        {
            SignSoftTreatyWith("AUS");
            Warm("AUS", relations: 35f, trust: 25f);

            Assert.IsFalse(DiplomacySystem.DeepenTreatyBy(state, state.playerCountryId, "AUS",
                    new List<TreatyCommitment> { TreatyCommitment.MutualDefense }),
                "A cold, distrustful partner signed a defence pact because the paper already " +
                "had a signature on it. History is a bonus, not a bypass.");
        }

        [Test]
        public void DeepeningIntoAPactAnswersToBlocPolitics()
        {
            SignSoftTreatyWith("AUS");
            Warm("AUS");
            state.date = new GameDate(state.startDate.year + 3, 1);

            // We are deeply aligned with Australia's genuine enemy.
            var withRival = state.FindRelationship(state.playerCountryId, "RUS");
            withRival.strategicAlignment = 95f;
            var theirEnmity = state.FindRelationship("AUS", "RUS");
            theirEnmity.relations = 5f;

            Assert.IsFalse(DiplomacySystem.DeepenTreatyBy(state, state.playerCountryId, "AUS",
                    new List<TreatyCommitment> { TreatyCommitment.MutualDefense }),
                "A state signed a defence pact with its enemy's committed ally through the " +
                "deepening door. The rival-tie rule must bind every route to a pact, or the " +
                "bloc politics it enforces has a hole in the fence.");
        }

        [Test]
        public void DeepeningAddsNothingAlreadyHeld()
        {
            var treaty = SignSoftTreatyWith("AUS");
            Warm("AUS");

            Assert.IsFalse(DiplomacySystem.DeepenTreatyBy(state, state.playerCountryId, "AUS",
                    new List<TreatyCommitment> { TreatyCommitment.NonAggression }),
                "Re-adding a commitment the treaty already carries should be a refusal, not " +
                "a duplicate line in the document.");
            int count = 0;
            foreach (var c in treaty.commitments) if (c == TreatyCommitment.NonAggression) count++;
            Assert.AreEqual(1, count);
        }

        // ---------- sanctions end at a table ----------

        Sanction SanctionUsBy(string senderId, int monthsActive = 40)
        {
            var sanction = new Sanction
            {
                senderId = senderId,
                targetId = state.playerCountryId,
                severity = SanctionSeverity.Coercive,
                imposedDate = state.startDate,
                monthsActive = monthsActive
            };
            state.sanctions.Add(sanction);
            return sanction;
        }

        [Test]
        public void ASanctionedStateCanNegotiateItsWayOut()
        {
            SanctionUsBy("CHN");
            var relationship = Warm("CHN", relations: 55f, trust: 45f);
            relationship.SetThreatPerceivedBy("CHN", 20f);

            bool lifted = EconomySystem.SeekSanctionsReliefBy(state, state.playerCountryId, "CHN");

            Assert.IsTrue(lifted,
                "A stale, expensive sanctions regime against a state the sender no longer " +
                "fears could not be negotiated away. Sanctions suppress the relations their " +
                "automatic lapse requires — without this verb the cycle is unbreakable, " +
                "measured at 237 sanctioned months out of 240.");
            Assert.IsNull(state.FindSanction("CHN", state.playerCountryId));
            Assert.Greater(relationship.sanctionsTruceMonths, 0, "no détente was agreed");
        }

        [Test]
        public void AStateStillFearedIsRefused()
        {
            SanctionUsBy("CHN");
            var relationship = Warm("CHN", relations: 55f, trust: 45f);
            relationship.SetThreatPerceivedBy("CHN", 85f);

            Assert.IsFalse(EconomySystem.SeekSanctionsReliefBy(state, state.playerCountryId, "CHN"),
                "A sender that still regards the target as a major threat lifted its measures " +
                "anyway — relief must be earned by conduct, not requested into existence.");
        }

        [Test]
        public void TheDetenteHoldsAndThenExpires()
        {
            SanctionUsBy("CHN");
            var relationship = Warm("CHN", relations: 55f, trust: 45f);
            relationship.SetThreatPerceivedBy("CHN", 20f);
            Assert.IsTrue(EconomySystem.SeekSanctionsReliefBy(state, state.playerCountryId, "CHN"));

            Assert.IsFalse(EconomySystem.ImposeSanctionsBy(state, "CHN", state.playerCountryId,
                    SanctionSeverity.Pressure),
                "New measures were imposed straight through a standing détente — the truce " +
                "is decoration and negotiating one bought nothing.");

            // NARROW PIPELINE: DiplomacySystem.MonthlyUpdate alone, to walk the
            // truce down without the rest of the world re-arranging the fixture.
            for (int month = 0; month < EconomySystem.DetenteTruceMonths + 1; month++)
                DiplomacySystem.MonthlyUpdate(state);

            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, "CHN", state.playerCountryId,
                    SanctionSeverity.Pressure),
                "The détente never expires — a 24-month truce became permanent immunity.");
        }

        [Test]
        public void WarVoidsTheDetente()
        {
            SanctionUsBy("CHN");
            var relationship = Warm("CHN", relations: 55f, trust: 45f);
            relationship.SetThreatPerceivedBy("CHN", 20f);
            Assert.IsTrue(EconomySystem.SeekSanctionsReliefBy(state, state.playerCountryId, "CHN"));

            ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            Assert.AreEqual(0, relationship.sanctionsTruceMonths,
                "A détente survived a declaration of war between the same pair.");
        }

        // ---------- the world uses both doors ----------

        [Test]
        public void TheWorldNegotiatesItsOwnDetentes()
        {
            // The census this feature answers: 40–60 standing AI-AI sanction
            // regimes, none of which any AI had a verb to end.
            var world = WorldFactory.CreateDebugWorld(seed: 9753);
            var manager = new TurnManager(world);
            SimulationPipeline.Wire(manager, world);
            for (int month = 0; month < 360; month++) manager.EndMonth();

            int negotiated = 0, deepened = 0;
            foreach (var entry in world.chronicle)
            {
                if (entry.text == null) continue;
                if (entry.text.Contains("Negotiated an end to")) negotiated++;
                if (entry.text.Contains("deepened")) deepened++;
            }

            Assert.Greater(negotiated, 0,
                "Thirty years, a world of standing sanction regimes, and no AI government " +
                "ever negotiated one away — the pariah trap is still unbreakable for " +
                "everyone the player is not.");
            Assert.Greater(deepened, 0,
                "Thirty years and no AI pair ever deepened a treaty — the blocs cannot " +
                "solidify and alliance obligations have no signatories.");
        }
    }
}
