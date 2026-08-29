using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Corruption (spec 05 §2e).
    ///
    /// `DistributePatronage` has claimed in its own comment since it shipped
    /// that it "hollows the state out if it becomes the habitual instrument",
    /// and nothing in the simulation recorded that it had — so the habitual
    /// instrument cost a slowly-regrowing pillar and nothing else.
    /// `OppositionTheme.Corruption` existed too, proxied by a weak government
    /// pillar plus conspiracy plus poor cohesion: a measure of the state being
    /// *feeble*, which is not the same as the state being bought.
    /// </summary>
    public class CorruptionTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 3141);
            state.politicalCapital = GameState.PoliticalCapitalCap;
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

        GovernmentState Gov => state.PlayerCountry.government;

        // ---------- the faction ledger (spec 05 §2g) ----------

        [Test]
        public void EveryGovernmentRestsOnNamedBlocsThatWantDifferentThings()
        {
            // Seeded lazily, so an old save gains them on load without a
            // migration — and deterministically, so it gains the same ones every
            // time it is loaded.
            GovernmentSystem.EnsureFactions(state, state.PlayerCountry);

            Assert.Greater(Gov.factions.Count, 1,
                "The government rests on a single undifferentiated bloc, which is the scalar "
                + "this was meant to replace wearing a list.");

            var themes = new System.Collections.Generic.HashSet<OppositionTheme>();
            foreach (var faction in Gov.factions)
            {
                Assert.IsNotEmpty(faction.name);
                themes.Add(faction.theme);
            }
            Assert.Greater(themes.Count, 1,
                "Every bloc wants the same thing, so which one you court cannot matter.");
        }

        [Test]
        public void SeedingIsDeterministic()
        {
            GovernmentSystem.EnsureFactions(state, state.PlayerCountry);

            var again = WorldFactory.CreateDebugWorld(seed: 3141);
            GovernmentSystem.EnsureFactions(again, again.PlayerCountry);

            Assert.AreEqual(Gov.factions.Count, again.PlayerCountry.government.factions.Count);
            for (int i = 0; i < Gov.factions.Count; i++)
                Assert.AreEqual(Gov.factions[i].disposition,
                    again.PlayerCountry.government.factions[i].disposition, 0.001f,
                    "The same world seeded a different coalition twice, so a save reloaded "
                    + "would find different people in the chamber.");
        }

        [Test]
        public void AnIndifferentCoalitionIsWorthNothingAndASeededOneIsWorthLittle()
        {
            GovernmentSystem.EnsureFactions(state, state.PlayerCountry);

            // What a freshly seeded coalition contributes before anybody has been
            // courted. It is deliberately *not* zero — a real coalition has its
            // own majority, its sceptics and its provinces from day one — but it
            // has to be small enough not to retune every government in the world,
            // which is the trap the fiscal multipliers were built to avoid.
            float seeded = Math.Abs(GovernmentSystem.FactionSupport(Gov));
            Assert.Less(seeded, 4f,
                $"A coalition nobody has spoken to yet is worth {seeded:F1} support. Anything "
                + "larger is a silent rebalance of every elective government at world creation.");

            foreach (var faction in Gov.factions) faction.disposition = 50f;
            Assert.AreEqual(0f, GovernmentSystem.FactionSupport(Gov), 0.001f,
                "A coalition sitting at exact indifference still contributed support, so the "
                + "term has an offset nobody chose.");
        }

        [Test]
        public void CourtingABlocMovesThatBlocAndNotTheOthers()
        {
            GovernmentSystem.EnsureFactions(state, state.PlayerCountry);
            var courted = GovernmentSystem.FactionFor(Gov, OppositionTheme.Hardship);
            Assert.IsNotNull(courted, "the fixture's coalition has nobody who cares about hardship");

            float before = courted.disposition;
            var others = new System.Collections.Generic.Dictionary<string, float>();
            foreach (var faction in Gov.factions)
                if (faction != courted) others[faction.name] = faction.disposition;

            state.politicalCapital = GameState.PoliticalCapitalCap;
            GovernmentSystem.BuildPoliticalSupportBy(state, state.playerCountryId,
                OppositionTheme.Hardship);

            Assert.Greater(courted.disposition, before, "Courting a bloc did not move it.");
            foreach (var faction in Gov.factions)
                if (faction != courted)
                    Assert.AreEqual(others[faction.name], faction.disposition, 0.001f,
                        $"Courting the hardship bloc also moved {faction.name}, so the ledger is "
                        + "one number in three costumes.");
        }

        [Test]
        public void ACourtedCoalitionIsWorthMoreSupport()
        {
            GovernmentSystem.EnsureFactions(state, state.PlayerCountry);
            foreach (var faction in Gov.factions) faction.disposition = 50f;
            float indifferent = GovernmentSystem.FactionSupport(Gov);

            foreach (var faction in Gov.factions) faction.disposition = 85f;
            float courted = GovernmentSystem.FactionSupport(Gov);

            Assert.Greater(courted, indifferent + 5f,
                "A coalition that is thoroughly behind the government was worth no more than "
                + "one that is indifferent to it.");
        }

        [Test]
        public void SupportHasToBeMaintained()
        {
            // Blocs drift back to indifference: a coalition is maintained rather
            // than bought once, the `brokeredSupport` rule applied to people.
            GovernmentSystem.EnsureFactions(state, state.PlayerCountry);
            foreach (var faction in Gov.factions) faction.disposition = 90f;

            for (int month = 0; month < 36; month++) turns.EndMonth();

            bool anyDrifted = false;
            foreach (var faction in Gov.factions)
                if (faction.disposition < 88f) anyDrifted = true;

            Assert.IsTrue(anyDrifted,
                "Three years on, a coalition bought once was still exactly as enthusiastic. "
                + "Support that never fades is support bought once and kept forever.");
        }

        // ---------- constitutional change (spec 05 §2f) ----------

        [Test]
        public void AGovernmentNobodyIsListeningToCannotRewriteTheRules()
        {
            var gov = Gov;
            if (gov.IsElective) gov.legislativeSupport = 20f;
            else gov.eliteCohesion = 20f;

            Assert.IsFalse(GovernmentSystem.CanChangeConstitution(state, state.playerCountryId,
                    GovernmentType.Monarchy, out string reason),
                "A government with nobody behind it rewrote the constitution anyway.");
            Assert.IsNotEmpty(reason, "A refusal the operator cannot see is a broken button.");
        }

        [Test]
        public void ChangingWhatTheStateIsTakesYearsAndCanBeLost()
        {
            var gov = Gov;
            if (gov.IsElective) gov.legislativeSupport = 60f;
            else gov.eliteCohesion = 60f;
            state.politicalCapital = GameState.PoliticalCapitalCap;

            var was = gov.type;
            var target = gov.type == GovernmentType.Monarchy
                ? GovernmentType.CentralizedRepublic : GovernmentType.Monarchy;

            Assert.IsTrue(GovernmentSystem.BeginConstitutionalChangeBy(
                state, state.playerCountryId, target));
            Assert.AreEqual(was, gov.type,
                "The constitution changed the instant it was proposed, so the thirty months are "
                + "decoration and this is a button rather than a process.");

            // Make it fail: nobody behind it, and a country coming apart.
            for (int month = 0; month < GovernmentSystem.ConstitutionalMonths + 2; month++)
            {
                state.politicalCapital = GameState.PoliticalCapitalCap;
                if (gov.IsElective) gov.legislativeSupport = 12f;
                else gov.eliteCohesion = 12f;
                state.PlayerCountry.socialUnrest = 90f;
                turns.EndMonth();
            }

            Assert.IsFalse(gov.ChangingConstitution, "The process never resolved.");
            Assert.AreEqual(was, gov.type,
                "A constitutional process with no support behind it and a country in the "
                + "street succeeded anyway, so it cannot be lost and the years carry no risk.");
        }

        [Test]
        public void AProcessWithRealBackingArrives()
        {
            var gov = Gov;
            var was = gov.type;
            var target = gov.type == GovernmentType.Monarchy
                ? GovernmentType.CentralizedRepublic : GovernmentType.Monarchy;

            if (gov.IsElective) gov.legislativeSupport = 90f;
            else gov.eliteCohesion = 90f;
            state.politicalCapital = GameState.PoliticalCapitalCap;

            Assert.IsTrue(GovernmentSystem.BeginConstitutionalChangeBy(
                state, state.playerCountryId, target));

            for (int month = 0; month < GovernmentSystem.ConstitutionalMonths + 2; month++)
            {
                state.politicalCapital = GameState.PoliticalCapitalCap;
                if (gov.IsElective) gov.legislativeSupport = 90f;
                else gov.eliteCohesion = 90f;
                state.PlayerCountry.socialUnrest = 5f;
                gov.corruption = 0f;
                turns.EndMonth();
            }

            Assert.AreEqual(target, gov.type,
                $"Thirty months of a united, popular, uncorrupt state pushing for a new "
                + $"constitution left it a {was}. If it cannot be won it is not a verb.");
        }

        [Test]
        public void AProcessNobodyCanAffordCollapses()
        {
            // It is paid for every month it runs. A government that runs out of
            // standing cannot hold the process together — which is what makes
            // the upkeep a real constraint rather than an opening fee.
            var gov = Gov;
            if (gov.IsElective) gov.legislativeSupport = 70f;
            else gov.eliteCohesion = 70f;
            state.politicalCapital = GameState.PoliticalCapitalCap;

            var target = gov.type == GovernmentType.Monarchy
                ? GovernmentType.CentralizedRepublic : GovernmentType.Monarchy;
            GovernmentSystem.BeginConstitutionalChangeBy(state, state.playerCountryId, target);

            state.politicalCapital = 0f;
            for (int month = 0; month < 4; month++)
            {
                state.politicalCapital = 0f;
                turns.EndMonth();
            }

            Assert.IsFalse(gov.ChangingConstitution,
                "A government with no Political Capital at all kept a constitutional process "
                + "running for free.");
        }

        [Test]
        public void AGovernmentThatBuysSupportIsRecordedDoingIt()
        {
            state.PlayerCountry.resources.treasury = 50000f;
            Assert.AreEqual(0f, Gov.corruption, 0.001f, "It started corrupt.");

            GovernmentSystem.DistributePatronageBy(state, state.playerCountryId);

            Assert.Greater(Gov.corruption, 0f,
                "Handing out appointments, contracts and quiet favours left no trace, so the "
                + "line in patronage's own comment about hollowing the state remains a claim "
                + "nothing backs.");
        }

        [Test]
        public void FavoursFadeWhenTheyStopBeingHandedOut()
        {
            // The trap this codebase has shipped more than any other: a store fed
            // by a recurring act with no reachable resting point ratchets to the
            // cap and pins there. Proportional decay gives every level one.
            Gov.corruption = 60f;
            float before = Gov.corruption;

            for (int month = 0; month < 24; month++) turns.EndMonth();

            Assert.Less(Gov.corruption, before - 10f,
                $"Two years without a single favour left corruption at {Gov.corruption:F1} "
                + "against 60. A government that stops buying support has to be able to "
                + "recover, or the first patronage of a save is permanent.");
        }

        [Test]
        public void EveryLevelHasARestingPointRatherThanRunningToTheCap()
        {
            // Proportional decay means a *sustained* habit settles somewhere
            // instead of pinning at 100 — the `publicGrievance` lesson, applied
            // before it could become the same bug.
            state.PlayerCountry.resources.treasury = 500000f;

            for (int month = 0; month < 60; month++)
            {
                state.politicalCapital = GameState.PoliticalCapitalCap;
                GovernmentSystem.DistributePatronageBy(state, state.playerCountryId);
                turns.EndMonth();
            }

            Assert.Less(Gov.corruption, 99f,
                $"Five years of buying support every single month pinned corruption at "
                + $"{Gov.corruption:F1}. A value that reaches its ceiling under sustained "
                + "pressure and cannot come down is a ratchet.");
            Assert.Greater(Gov.corruption, 15f,
                "Five years of monthly patronage barely registered, so the instrument is "
                + "effectively free after all.");
        }

        [Test]
        public void AnInquiryTakesFavoursBackOutOfTheSystem()
        {
            Gov.corruption = 50f;
            state.politicalCapital = GameState.PoliticalCapitalCap;

            Assert.IsTrue(GovernmentSystem.LaunchInquiryBy(state, state.playerCountryId));

            Assert.Less(Gov.corruption, 50f,
                "A public inquiry into how the state is run changed nothing about how the "
                + "state is run.");
        }

        [Test]
        public void AuditingWhatYouJustBoughtIsANetLoss()
        {
            // Relief is deliberately smaller than what patronage puts in, so
            // buy-then-audit is a losing cycle rather than a way to launder
            // support into permanence.
            Assert.Less(GovernmentSystem.InquiryCorruptionRelief,
                GovernmentSystem.PatronageCorruption * 2f,
                "An inquiry cleans up more than two rounds of patronage create, which makes "
                + "buying support and auditing it a free way to keep the support.");
            Assert.Greater(GovernmentSystem.InquiryCorruptionRelief,
                GovernmentSystem.PatronageCorruption,
                "An inquiry cannot even undo a single round of patronage, so there is no way "
                + "back from a corrupt state at all.");
        }

        [Test]
        public void MoneyLeaksOutOfACorruptTreasury()
        {
            // The most concrete thing corruption does, and the one an operator
            // feels without being told.
            Assert.AreEqual(0f, GovernmentSystem.RevenueLeakage(state.PlayerCountry), 0.0001f,
                "A clean government was already losing revenue.");

            Gov.corruption = 90f;
            Assert.Greater(GovernmentSystem.RevenueLeakage(state.PlayerCountry), 0.2f,
                "A thoroughly corrupt state collected its taxes in full, so the money side of "
                + "corruption is invisible.");
        }

        [Test]
        public void ACorruptStateIsHarderToGovern()
        {
            var control = WorldFactory.CreateDebugWorld(seed: 3141);
            var controlTurns = new TurnManager(control);
            SimulationPipeline.Wire(controlTurns, control);

            Gov.corruption = 85f;

            for (int month = 0; month < 18; month++)
            {
                Gov.corruption = 85f;              // hold the condition
                turns.EndMonth();
                controlTurns.EndMonth();
            }

            Assert.Less(state.PlayerCountry.stability, control.PlayerCountry.stability,
                "Eighteen months of a state running on favours left it exactly as stable as "
                + "one running on rules.");
        }

        [Test]
        public void TheOppositionCampaignsOnWhatWasRecordedRatherThanOnAProxy()
        {
            // Before `gov.corruption` existed the theme could only be reached
            // through a weak pillar, conspiracy or a divided elite — none of
            // which is the government being bought.
            var country = state.PlayerCountry;
            country.pillars.government = 75f;      // a capable state
            Gov.eliteCohesion = 80f;               // a united elite
            Gov.conspiracyLevel = 0f;              // nobody plotting
            Gov.corruption = 95f;                  // and thoroughly bought

            for (int month = 0; month < 12; month++)
            {
                Gov.corruption = 95f;
                turns.EndMonth();
            }

            Assert.AreEqual(OppositionTheme.Corruption, Gov.oppositionTheme,
                $"A capable, united, unplotted-against government that runs entirely on "
                + $"favours drew an opposition campaigning about {Gov.oppositionTheme} "
                + "instead. Corruption is the one thing it is actually guilty of.");
        }
    }
}
