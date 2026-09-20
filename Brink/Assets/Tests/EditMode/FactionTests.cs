using Brink.Core;
using Brink.Data;
using NUnit.Framework;
using Brink.UI;
using Brink.UI.Views;
using UnityEngine.UIElements;
using System.Reflection;
using System.IO;
using System.Linq;

namespace Brink.Tests
{
    /// <summary>
    /// Faction arithmetic (GDD §13, spec 05 §2b).
    ///
    /// `Leader.faction` was a display string no rule read: a government of
    /// continuity and one that had just thrown the old party out faced identical
    /// chambers, a junta's grip had nothing to do with the army it stood on, and
    /// `GovernmentType.ParliamentaryRepublic`'s own declaration — "government
    /// falls with confidence" — was implemented by nothing.
    ///
    /// NARROW PIPELINE: these tests run `GovernmentSystem.MonthlyUpdate` alone.
    /// Each pins its own preconditions and asserts this system's arithmetic;
    /// nothing here claims anything about the emergent world, which the
    /// full-pipeline suites cover.
    /// </summary>
    public class FactionTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 6161);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        void RunGovernmentMonths(int months, System.Action beforeEachMonth = null)
        {
            for (int i = 0; i < months; i++)
            {
                beforeEachMonth?.Invoke();
                GovernmentSystem.MonthlyUpdate(state);
                state.date = state.date.NextMonth();
            }
        }

        static void PushElectionsFarOut(GovernmentState gov, GameDate now)
            => gov.nextElectionDate = new GameDate(now.year + 30, now.month);

        // ---------- the chamber ----------

        [Test]
        public void PreviewingAnUnseededLedgerIsPureAndMatchesItsEventualSeed()
        {
            var country = state.PlayerCountry;
            country.government.factions.Clear();
            string before = SaveSystem.ToJson(state);
            var preview = GovernmentSystem.FactionsFor(state, country);
            Assert.AreEqual(3, preview.Count);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            GovernmentSystem.EnsureFactions(state, country);
            Assert.AreNotSame(preview, country.government.factions);
            for (int i = 0; i < preview.Count; i++)
            {
                Assert.AreEqual(preview[i].name, country.government.factions[i].name);
                Assert.AreEqual(preview[i].disposition, country.government.factions[i].disposition);
                Assert.AreEqual(preview[i].share, country.government.factions[i].share);
                Assert.AreEqual(preview[i].theme, country.government.factions[i].theme);
            }
        }

        [Test]
        public void AnAbsentBlocCannotConsumePoliticalCapitalOrSeedTheLedger()
        {
            state.PlayerCountry.government.factions.Clear();
            state.politicalCapital = 20;
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(GovernmentSystem.BuildPoliticalSupportBy(state, state.playerCountryId, OppositionTheme.War));
            Assert.IsFalse(GovernmentSystem.BuildPoliticalSupportBy(state, state.playerCountryId, (OppositionTheme)999));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            WithController(() =>
            {
                state.authorizedPillarMask = 0;
                state.PlayerCountry.government.type = GovernmentType.PresidentialRepublic;
                string untouched = SaveSystem.ToJson(state);
                Assert.IsFalse(GameController.Instance.CourtFaction(OppositionTheme.War));
                Assert.AreEqual(untouched, SaveSystem.ToJson(state), "invalid names must not even buy constitutional approval");
            });
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void GovernmentActuallyOffersEveryBlocWithoutWritingTheWorld(int columns)
        {
            WithController(() =>
            {
                // Let the pre-existing foreign-government readout initialize its
                // own read models before measuring this panel's purity.
                var view = new GovernmentView();
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                Assert.AreEqual(columns, TerminalMetrics.Columns);
                view.Refresh();
                state.PlayerCountry.government.factions.Clear();
                string before = SaveSystem.ToJson(state);
                view.Refresh();
                var buttons = view.Root.Query<Button>().ToList().Where(b => b.text.StartsWith("COURT ")).ToList();
                var factions = GovernmentSystem.FactionsFor(state, state.PlayerCountry);
                Assert.AreEqual(factions.Count, buttons.Count);
                foreach (var faction in factions)
                    Assert.IsTrue(buttons.Any(b => b.text == $"COURT {faction.name} [2 PC]" && b.enabledSelf));
                foreach (var label in view.Root.Query<Label>().ToList())
                    if (label.ClassListContains("terminal-text"))
                        foreach (string line in label.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
                Assert.AreEqual(before, SaveSystem.ToJson(state));
            });
        }

        [Test]
        public void PressingTheNamedBlocButtonCourtsThatBlocExactlyOnceAndSavesIt()
        {
            WithController(() =>
            {
                GovernmentSystem.EnsureFactions(state, state.PlayerCountry);
                var gov = state.PlayerCountry.government;
                foreach (var f in gov.factions) f.disposition = 50;
                var selected = gov.factions[2];
                int initiatives = state.initiativesThisYear;
                float capital = state.politicalCapital;
                float backing = GovernmentSystem.FactionSupport(gov);
                var view = new GovernmentView();
                view.Refresh();
                var button = view.Root.Query<Button>().ToList().Single(b => b.text == $"COURT {selected.name} [2 PC]");
                // Unity's manipulator dispatches the callback; the optional
                // dotnet UI shim exposes SendClick instead (no layout engine).
                var clickable = typeof(Button).GetProperty("clickable")?.GetValue(button);
                if (clickable != null)
                {
                    var invoke = clickable.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(invoke, "the test must invoke the real button callback");
                    invoke.Invoke(clickable, new object[] { null });
                }
                else
                {
                    var click = typeof(Button).GetMethod("SendClick");
                    Assert.IsNotNull(click, "neither Unity nor the harness can click this control");
                    click.Invoke(button, null);
                }
                Assert.AreEqual(capital - GovernmentSystem.BuildSupportCost, state.politicalCapital);
                Assert.AreEqual(initiatives + 1, state.initiativesThisYear);
                Assert.AreEqual(59, selected.disposition);
                foreach (var f in gov.factions.Where(f => f != selected)) Assert.AreEqual(50, f.disposition);
                Assert.Greater(gov.brokeredSupport, 0);
                Assert.Greater(GovernmentSystem.FactionSupport(gov), backing);
                var saved = SaveSystem.Load();
                Assert.AreEqual(59, saved.PlayerCountry.government.factions[2].disposition);
                Assert.IsTrue(state.notifications.Any(n => n.body.Contains(selected.name)));
            });
        }

        [Test]
        public void BlocButtonsExplainBothAffordabilityAndConstitutionalRefusal()
        {
            WithController(() =>
            {
                var view = new GovernmentView();
                state.politicalCapital = 0;
                view.Refresh();
                var buttons = view.Root.Query<Button>().ToList().Where(b => b.text.StartsWith("COURT ")).ToList();
                Assert.AreEqual(3, buttons.Count);
                foreach (var b in buttons)
                {
                    Assert.IsFalse(b.enabledSelf);
                    Assert.IsNotEmpty(TerminalView.BlockedReason(b));
                }
                state.politicalCapital = 20;
                state.authorizedPillarMask = 0;
                state.PlayerCountry.government.type = GovernmentType.ParliamentaryRepublic;
                view.Refresh();
                foreach (var b in view.Root.Query<Button>().ToList().Where(b => b.text.StartsWith("COURT ")))
                {
                    Assert.IsFalse(b.enabledSelf);
                    StringAssert.Contains("constitution", TerminalView.BlockedReason(b));
                }
                float before = state.politicalCapital;
                Assert.IsFalse(GameController.Instance.CourtFaction(OppositionTheme.Hardship));
                Assert.AreEqual(before, state.politicalCapital);
            });
        }

        [TestCase(GovernmentType.ParliamentaryRepublic)]
        [TestCase(GovernmentType.DominantPartyState)]
        public void PatronageAndInquiryCreateOpposedBlocReactions(GovernmentType type)
        {
            var country = state.PlayerCountry;
            var gov = country.government;
            gov.type = type;
            gov.factions.Clear();
            GovernmentSystem.EnsureFactions(state, country);
            foreach (var f in gov.factions) f.disposition = 50;
            var names = gov.factions.Select(f => f.name).ToArray();
            var shares = gov.factions.Select(f => f.share).ToArray();
            state.politicalCapital = 20;
            country.resources.treasury = 1000;
            gov.corruption = 20;
            Assert.IsTrue(GovernmentSystem.DistributePatronageBy(state, country.id));
            CollectionAssert.AreEqual(new float[] { 50, 45, 57 }, gov.factions.Select(f => f.disposition));
            Assert.AreEqual(19, state.politicalCapital);
            Assert.AreEqual(800, country.resources.treasury);
            Assert.AreEqual(27, gov.corruption);
            Assert.AreEqual(0.1f, GovernmentSystem.FactionSupport(gov), 0.0001);
            Assert.IsTrue(GovernmentSystem.LaunchInquiryBy(state, country.id));
            CollectionAssert.AreEqual(new float[] { 50, 49, 53 }, gov.factions.Select(f => f.disposition));
            Assert.AreEqual(15, state.politicalCapital);
            Assert.AreEqual(16, gov.corruption);
            CollectionAssert.AreEqual(names, gov.factions.Select(f => f.name));
            CollectionAssert.AreEqual(shares, gov.factions.Select(f => f.share));
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            CollectionAssert.AreEqual(gov.factions.Select(f => f.disposition), loaded.PlayerCountry.government.factions.Select(f => f.disposition));
            StringAssert.Contains(gov.factions[1].name + ": disposition +4", state.notifications.Last(n => n.title == "INQUIRY CONCLUDED").body);
        }

        [TestCase(0f, 0f)]
        [TestCase(5.5f, 2f)]
        [TestCase(11f, 4f)]
        [TestCase(100f, 4f)]
        public void InquiryGoodwillIsLimitedToCorruptionActuallyRemoved(float corruption, float reaction)
        {
            var country = state.PlayerCountry;
            GovernmentSystem.EnsureFactions(state, country);
            var gov = country.government;
            gov.factions.Clear();
            foreach (OppositionTheme theme in System.Enum.GetValues(typeof(OppositionTheme)))
                gov.factions.Add(new Faction { name = theme.ToString(), theme = theme, share = 0.2f, disposition = 50 });
            gov.corruption = corruption;
            state.politicalCapital = 20;
            Assert.IsTrue(GovernmentSystem.LaunchInquiryBy(state, country.id));
            foreach (var f in gov.factions)
            {
                float expected = f.theme == OppositionTheme.Hardship ? -reaction
                    : f.theme == OppositionTheme.Liberty || f.theme == OppositionTheme.Corruption ? reaction : 0;
                Assert.AreEqual(50 + expected, f.disposition, 0.0001, f.name);
            }
        }

        [Test]
        public void AbsentPolicyConstituenciesDoNotSpreadReactionsAndRefusalsDoNotSeed()
        {
            var country = state.PlayerCountry;
            country.government.factions.Clear();
            state.politicalCapital = 0;
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(GovernmentSystem.LaunchInquiryBy(state, country.id));
            Assert.IsFalse(GovernmentSystem.DistributePatronageBy(state, country.id));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            state.politicalCapital = 20;
            country.resources.treasury = 0;
            before = SaveSystem.ToJson(state);
            Assert.IsFalse(GovernmentSystem.DistributePatronageBy(state, country.id));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            country.government.factions.Add(new Faction { name = "Only war", theme = OppositionTheme.War, share = 1, disposition = 50 });
            country.resources.treasury = 1000;
            Assert.IsTrue(GovernmentSystem.DistributePatronageBy(state, country.id));
            Assert.IsTrue(GovernmentSystem.LaunchInquiryBy(state, country.id));
            Assert.AreEqual(50, country.government.factions.Single().disposition);
        }

        [Test]
        public void PolicyReactionsClampReportAppliedMovementAndCanRecover()
        {
            WithController(() =>
            {
                var country = state.PlayerCountry;
                var gov = country.government;
                gov.type = GovernmentType.DominantPartyState;
                gov.factions.Clear();
                GovernmentSystem.EnsureFactions(state, country);
                gov.factions[1].disposition = 2;
                gov.factions[2].disposition = 98;
                country.resources.treasury = 1000;
                int initiatives = state.initiativesThisYear;
                Assert.IsTrue(GameController.Instance.DistributePatronage());
                Assert.AreEqual(initiatives + 1, state.initiativesThisYear);
                Assert.AreEqual(0, gov.factions[1].disposition);
                Assert.AreEqual(100, gov.factions[2].disposition);
                string notice = state.notifications.Last(n => n.title == "PATRONAGE DISTRIBUTED").body;
                StringAssert.Contains(gov.factions[1].name + ": disposition -2", notice);
                StringAssert.Contains(gov.factions[2].name + ": disposition +2", notice);
                Assert.AreEqual(0, SaveSystem.Load().PlayerCountry.government.factions[1].disposition);
                Assert.IsTrue(GameController.Instance.CourtFaction(gov.factions[1].theme));
                Assert.AreEqual(9, gov.factions[1].disposition);
                RunGovernmentMonths(1);
                Assert.Greater(gov.factions[1].disposition, 9);
                Assert.Less(gov.factions[2].disposition, 100);
            });
        }

        [Test]
        public void ForeignPoliciesUseTheirOwnLedgerAndInquiryClampsItsReport()
        {
            var country = state.countries.First(c => !c.isPlayer);
            country.government.type = GovernmentType.DominantPartyState;
            country.government.factions.Clear();
            GovernmentSystem.EnsureFactions(state, country);
            foreach (var f in country.government.factions) f.disposition = 50;
            state.FindAI(country.id).politicalCapital = 20;
            country.resources.treasury = 1000;
            country.government.corruption = 20;
            float pc = state.politicalCapital;
            int initiatives = state.initiativesThisYear, notices = state.notifications.Count;
            Assert.IsTrue(GovernmentSystem.DistributePatronageBy(state, country.id));
            CollectionAssert.AreEqual(new float[] { 50, 45, 57 }, country.government.factions.Select(f => f.disposition));
            Assert.IsTrue(GovernmentSystem.LaunchInquiryBy(state, country.id));
            CollectionAssert.AreEqual(new float[] { 50, 49, 53 }, country.government.factions.Select(f => f.disposition));
            Assert.AreEqual(15, state.FindAI(country.id).politicalCapital);
            Assert.AreEqual(pc, state.politicalCapital);
            Assert.AreEqual(initiatives, state.initiativesThisYear);
            Assert.AreEqual(notices, state.notifications.Count);

            var player = state.PlayerCountry;
            player.government.factions.Clear();
            player.government.factions.Add(new Faction { name = "Reform", theme = OppositionTheme.Liberty, share = 0.5f, disposition = 98 });
            player.government.factions.Add(new Faction { name = "Recipients", theme = OppositionTheme.Hardship, share = 0.5f, disposition = 2 });
            player.government.corruption = 11;
            state.politicalCapital = 20;
            Assert.IsTrue(GovernmentSystem.LaunchInquiryBy(state, player.id));
            CollectionAssert.AreEqual(new float[] { 100, 0 }, player.government.factions.Select(f => f.disposition));
            string notice = state.notifications.Last(n => n.title == "INQUIRY CONCLUDED").body;
            StringAssert.Contains("Reform: disposition +2", notice);
            StringAssert.Contains("Recipients: disposition -2", notice);
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void BlocPolicyPreviewIsPureClampedAndMatchesTheAction(int columns)
        {
            WithController(() =>
            {
                var view = new GovernmentView();
                view.Refresh(); // existing chamber initialisation
                var country = state.PlayerCountry;
                var gov = country.government;
                gov.factions.Clear();
                gov.corruption = 5.5f;
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                string before = SaveSystem.ToJson(state);
                view.Refresh();
                Assert.AreEqual(before, SaveSystem.ToJson(state));
                var labels = view.Root.Query<Label>().ToList().Where(l => l.ClassListContains("terminal-text"));
                string text = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", labels.Select(l => l.text)), @"\s+", " ");
                StringAssert.Contains("PUBLIC INQUIRY +2", text);
                StringAssert.Contains("PUBLIC INQUIRY -2", text);
                foreach (var label in labels)
                    foreach (var line in (label.text ?? "").Split('\n')) Assert.LessOrEqual(line.Length, columns);
                GovernmentSystem.EnsureFactions(state, country);
                var expected = gov.factions.Select(f => f.disposition + GovernmentSystem.InquiryReaction(f.theme, gov.corruption)).ToArray();
                Assert.IsTrue(GameController.Instance.LaunchInquiry());
                CollectionAssert.AreEqual(expected, gov.factions.Select(f => f.disposition));
                CollectionAssert.AreEqual(expected, SaveSystem.Load().PlayerCountry.government.factions.Select(f => f.disposition));
                foreach (var f in gov.factions)
                    f.disposition = f.theme == OppositionTheme.Hardship ? 98f : 2f;
                view.Refresh();
                string clamped = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", view.Root.Query<Label>().ToList().Select(l => l.text)), @"\s+", " ");
                StringAssert.Contains("PATRONAGE +2", clamped);
                StringAssert.Contains("PATRONAGE -2", clamped);
            });
        }

        void WithController(System.Action check)
        {
            var gc = GameController.Instance;
            var previous = gc.State;
            var previousTurns = gc.Turns;
            string previousDirectory = SaveSystem.SaveDirectoryOverride;
            string directory = Path.Combine(Path.GetTempPath(), "brink-faction-tests-" + System.Guid.NewGuid());
            try
            {
                SaveSystem.SaveDirectoryOverride = directory;
                typeof(GameController).GetProperty("State").SetValue(gc, state);
                state.politicalCapital = 20;
                state.authorizedPillarMask = ~0;
                check();
            }
            finally
            {
                typeof(GameController).GetProperty("State").SetValue(gc, previous);
                typeof(GameController).GetProperty("Turns").SetValue(gc, previousTurns);
                SaveSystem.SaveDirectoryOverride = previousDirectory;
                TerminalMetrics.ResetForTests();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void ANewGoverningFactionInheritsAThinnerChamber()
        {
            float SupportAfterSixMonths(string faction)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 6161);
                var gov = world.PlayerCountry.government;
                string leaderBefore = gov.leader.name;

                gov.leader.faction = faction;
                gov.leader.monthsInOffice = 0;
                PushElectionsFarOut(gov, world.date);

                for (int i = 0; i < 6; i++)
                {
                    GovernmentSystem.MonthlyUpdate(world);
                    world.date = world.date.NextMonth();
                }

                Assert.AreEqual(leaderBefore, gov.leader.name,
                    "the fixture did not hold — a leadership change reset the chamber " +
                    "and the comparison measures the honeymoon instead");
                return gov.legislativeSupport;
            }

            float turnover = SupportAfterSixMonths("OPPOSITION");
            float continuity = SupportAfterSixMonths("GOVERNING PARTY");

            Assert.Less(turnover, continuity - 2f,
                $"a government that just threw the old party out holds the chamber " +
                $"({turnover:F1}) as firmly as one of continuity ({continuity:F1}) — " +
                "the faction string is still decoration.");
        }

        [Test]
        public void TheChamberComesAroundWithTime()
        {
            var gov = state.PlayerCountry.government;
            gov.leader.faction = "OPPOSITION";

            gov.leader.monthsInOffice = 0;
            float fresh = GovernmentSystem.FactionSupportShift(gov);

            gov.leader.monthsInOffice = GovernmentSystem.FactionConsolidationMonths;
            float consolidated = GovernmentSystem.FactionSupportShift(gov);

            Assert.Less(fresh, -8f, "a fresh turnover government faces no real penalty");
            Assert.AreEqual(0f, consolidated, 0.01f,
                "the penalty never expires — a term in office and the chamber is still " +
                "somebody else's, which turns a transition into a permanent tax.");
        }

        [Test]
        public void BrokeredSupportRemainsTheWayThrough()
        {
            // The penalty must be answerable with the pillar's own verbs, or a
            // turnover government is a death spiral rather than a hard start.
            var gov = state.PlayerCountry.government;
            gov.leader.faction = "OPPOSITION";
            gov.leader.monthsInOffice = 0;

            float unaided = GovernmentSystem.FactionSupportShift(gov);
            gov.brokeredSupport = 30f;

            // brokeredSupport enters the same target at 0.45 — more than the
            // whole penalty at full strength.
            Assert.Greater(gov.brokeredSupport * 0.45f, -unaided,
                "the workhorse verb cannot outbid the faction penalty at full strength.");
        }

        // ---------- the men with guns, and the committee ----------

        [Test]
        public void AJuntaStandsOnTheOfficerCorps()
        {
            var gov = state.FindCountry("CHN").government;
            gov.leader.faction = "MILITARY COUNCIL";

            gov.militaryLoyalty = 90f;
            float loyal = GovernmentSystem.FactionCohesionShift(gov);
            gov.militaryLoyalty = 30f;
            float mutinous = GovernmentSystem.FactionCohesionShift(gov);

            Assert.Greater(loyal, 0f, "a loyal officer corps lends the junta nothing");
            Assert.Less(mutinous, -5f,
                "the army has turned and the council's grip is unaffected — undermining " +
                "the military is supposed to be the specific way at a coup-born government.");
        }

        [Test]
        public void AProvisionalAuthorityConsolidates()
        {
            var gov = state.FindCountry("CHN").government;
            gov.leader.faction = "PROVISIONAL AUTHORITY";

            gov.leader.monthsInOffice = 0;
            float fresh = GovernmentSystem.FactionCohesionShift(gov);
            gov.leader.monthsInOffice = GovernmentSystem.FactionConsolidationMonths + 6;
            float settled = GovernmentSystem.FactionCohesionShift(gov);

            Assert.Less(fresh, -5f, "a state still deciding whether it is one pays nothing for it");
            Assert.AreEqual(0f, settled, 0.01f, "the provisional discount never expires");
        }

        [Test]
        public void OrdinaryFactionsCarryNoArithmetic()
        {
            // Every string the game writes must either have a rule or cost
            // nothing — a label that penalises by accident of wording is worse
            // than a label that does nothing.
            var electiveGov = state.PlayerCountry.government;
            electiveGov.leader.faction = "GOVERNING PARTY";
            electiveGov.leader.monthsInOffice = 0;
            Assert.AreEqual(0f, GovernmentSystem.FactionSupportShift(electiveGov), 0.01f);

            var internalGov = state.FindCountry("CHN").government;
            internalGov.leader.faction = "PARTY LEADERSHIP";
            internalGov.leader.monthsInOffice = 0;
            Assert.AreEqual(0f, GovernmentSystem.FactionCohesionShift(internalGov), 0.01f);
        }

        // ---------- confidence (the parliamentary promise) ----------

        [Test]
        public void AParliamentaryGovernmentCanFall()
        {
            var germany = state.FindCountry("DEU");
            var gov = germany.government;
            Assert.IsTrue(gov.AllowsEarlyElection, "DEU is expected to be parliamentary");

            string leaderBefore = gov.leader.name;
            PushElectionsFarOut(gov, state.date);

            RunGovernmentMonths(60, () =>
            {
                gov.legislativeSupport = 10f;
                if (gov.leader.name == leaderBefore) PushElectionsFarOut(gov, state.date);
            });

            Assert.AreNotEqual(leaderBefore, gov.leader.name,
                "five years at legislative support 10 and the government stands (seed 6161) — " +
                "the type's own declaration, \"government falls with confidence\", is still " +
                "implemented by nothing.");

            bool chronicled = false;
            foreach (var entry in state.chronicle)
                if (entry.countryId == "DEU" && entry.text.Contains("confidence"))
                    chronicled = true;
            Assert.IsTrue(chronicled, "the fall left no public record");
        }

        [Test]
        public void APresidentialSystemRidesItOut()
        {
            var usa = state.FindCountry("USA");
            var gov = usa.government;
            Assert.IsFalse(gov.AllowsEarlyElection, "USA is expected to be presidential");
            PushElectionsFarOut(gov, state.date);

            RunGovernmentMonths(60, () =>
            {
                gov.legislativeSupport = 10f;
                PushElectionsFarOut(gov, state.date);
            });

            foreach (var entry in state.chronicle)
                if (entry.countryId == "USA")
                    Assert.IsFalse(entry.text.Contains("confidence vote"),
                        "a presidential government fell to a confidence vote — the fixed term " +
                        "is the thing that makes the two elective types different, and it is gone.");
        }

        [Test]
        public void AHealthyChamberNeverBringsTheGovernmentDown()
        {
            var germany = state.FindCountry("DEU");
            var gov = germany.government;
            PushElectionsFarOut(gov, state.date);

            RunGovernmentMonths(60, () =>
            {
                gov.legislativeSupport = 60f;
                PushElectionsFarOut(gov, state.date);
            });

            foreach (var entry in state.chronicle)
                if (entry.countryId == "DEU")
                    Assert.IsFalse(entry.text.Contains("confidence"),
                        "a government with a working majority fell anyway — the mechanic is " +
                        "random rather than conditional.");
        }
    }
}
