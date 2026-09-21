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

        [TestCase(4242)]
        [TestCase(9090)]
        [TestCase(8686)]
        [TestCase(5171)]
        [TestCase(6301)]
        public void EachNamedButtonCourtsItsOwnBlocEvenWhenConcernsAreIdentical(int seed)
        {
            state = WorldFactory.CreateDebugWorld(seed: seed);
            WithController(() =>
            {
                var gov = state.PlayerCountry.government;
                gov.factions.Clear();
                for (int i = 0; i < 3; i++)
                    gov.factions.Add(new Faction { name = "COALITION " + i,
                        theme = OppositionTheme.Hardship, share = i == 0 ? .7f : .15f, disposition = 50 });
                for (int selected = 0; selected < 3; selected++)
                {
                    var before = gov.factions.Select(f => f.disposition).ToArray();
                    float pc = state.politicalCapital;
                    int initiatives = state.initiativesThisYear;
                    float backing = gov.brokeredSupport;
                    var view = new GovernmentView();
                    view.Refresh();
                    var button = view.Root.Query<Button>().ToList().Single(b => b.text == $"COURT COALITION {selected} [2 PC]");
                    var clickable = typeof(Button).GetProperty("clickable")?.GetValue(button);
                    if (clickable != null)
                        clickable.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(clickable, new object[] { null });
                    else typeof(Button).GetMethod("SendClick").Invoke(button, null);
                    for (int i = 0; i < 3; i++)
                    {
                        Assert.AreEqual(before[i] + (i == selected ? 9 : 0), gov.factions[i].disposition);
                        Assert.AreEqual(i == 0 ? .7f : .15f, gov.factions[i].share);
                    }
                    Assert.AreEqual(pc - 2, state.politicalCapital);
                    Assert.AreEqual(initiatives + 1, state.initiativesThisYear);
                    Assert.AreEqual(backing + 3 + System.Math.Max(0, 60 - backing) * .14f, gov.brokeredSupport, .0001f);
                    StringAssert.Contains($"COALITION {selected} has been courted", state.notifications.Last().body);
                    CollectionAssert.AreEqual(gov.factions.Select(f => f.disposition),
                        SaveSystem.Load().PlayerCountry.government.factions.Select(f => f.disposition));
                }
            });
        }

        [TestCase(-1)]
        [TestCase(3)]
        [TestCase(int.MaxValue)]
        public void InvalidLedgerTargetRefusesBeforeAuthoritySpendOrSeeding(int index)
        {
            WithController(() =>
            {
                state.PlayerCountry.government.factions.Clear();
                state.PlayerCountry.government.type = GovernmentType.PresidentialRepublic;
                state.authorizedPillarMask = 0;
                string before = SaveSystem.ToJson(state);
                Assert.IsFalse(GameController.Instance.CourtFactionAtIndex(index));
                Assert.IsFalse(GovernmentSystem.BuildPoliticalSupportForFactionBy(state, state.playerCountryId, index));
                Assert.IsFalse(GovernmentSystem.BuildPoliticalSupportForFactionBy(state, "ABSENT", index));
                Assert.AreEqual(before, SaveSystem.ToJson(state));
            });
        }

        [Test]
        public void ExactCourtingKeepsAffordabilityAuthorityClampingAndLegacyThemeRules()
        {
            WithController(() =>
            {
                var gov = state.PlayerCountry.government;
                gov.factions.Clear();
                gov.factions.Add(new Faction { name = "MAJOR", theme = OppositionTheme.Liberty, share = .8f, disposition = 50 });
                gov.factions.Add(new Faction { name = "MINOR", theme = OppositionTheme.Liberty, share = .2f, disposition = 96 });
                state.politicalCapital = 1;
                string before = SaveSystem.ToJson(state);
                Assert.IsFalse(GameController.Instance.CourtFactionAtIndex(1));
                Assert.AreEqual(before, SaveSystem.ToJson(state));
                state.politicalCapital = 20;
                gov.type = GovernmentType.ParliamentaryRepublic;
                state.authorizedPillarMask = 0;
                Assert.IsFalse(GameController.Instance.CourtFactionAtIndex(1));
                Assert.AreEqual(20, state.politicalCapital);
                Assert.AreEqual(96, gov.factions[1].disposition);
                gov.type = GovernmentType.PresidentialRepublic;
                state.authorizedPillarMask = ~0;
                Assert.IsTrue(GameController.Instance.CourtFactionAtIndex(1));
                Assert.AreEqual(100, gov.factions[1].disposition);
                Assert.AreEqual(50, gov.factions[0].disposition);
                Assert.IsTrue(GameController.Instance.CourtFaction(OppositionTheme.Liberty));
                Assert.AreEqual(59, gov.factions[0].disposition, "legacy theme callers still court the largest constituency");
                Assert.IsTrue(GovernmentSystem.BuildPoliticalSupportBy(state, state.playerCountryId));
                Assert.AreEqual(61, gov.factions[0].disposition);
                Assert.AreEqual(100, gov.factions[1].disposition);
            });
        }

        [Test]
        public void ExactActorGenericBargainChargesOnlyTheForeignActor()
        {
            var country = state.FindCountry("CHN");
            var ai = state.FindAI(country.id);
            ai.politicalCapital = 20;
            country.government.factions.Clear();
            country.government.factions.Add(new Faction { name = "MAJOR", theme = OppositionTheme.Liberty, share = .8f, disposition = 50 });
            country.government.factions.Add(new Faction { name = "MINOR", theme = OppositionTheme.Liberty, share = .2f, disposition = 50 });
            float pc = state.politicalCapital;
            int initiatives = state.initiativesThisYear;
            Assert.IsTrue(GovernmentSystem.BuildPoliticalSupportForFactionBy(state, country.id, 1));
            Assert.AreEqual(18, ai.politicalCapital);
            Assert.AreEqual(pc, state.politicalCapital);
            Assert.AreEqual(initiatives, state.initiativesThisYear);
            Assert.AreEqual(50, country.government.factions[0].disposition);
            Assert.AreEqual(59, country.government.factions[1].disposition);
        }

        [Test]
        public void ExactTargetOnAnEmptyOldSaveMatchesTheDisplayedPreview()
        {
            WithController(() =>
            {
                var country = state.PlayerCountry;
                country.government.factions.Clear();
                var preview = GovernmentSystem.FactionsFor(state, country);
                Assert.AreEqual(0, country.government.factions.Count);
                Assert.IsTrue(GameController.Instance.CourtFactionAtIndex(2));
                for (int i = 0; i < preview.Count; i++)
                {
                    var actual = country.government.factions[i];
                    Assert.AreEqual(preview[i].name, actual.name);
                    Assert.AreEqual(preview[i].theme, actual.theme);
                    Assert.AreEqual(preview[i].share, actual.share);
                    Assert.AreEqual(preview[i].disposition + (i == 2 ? 9 : 0), actual.disposition);
                }
            });
        }

        [TestCase(GovernmentType.PresidentialRepublic, "THE CHAMBER MAJORITY")]
        [TestCase(GovernmentType.ParliamentaryRepublic, "THE CHAMBER MAJORITY")]
        [TestCase(GovernmentType.DominantPartyState, "THE PARTY APPARATUS")]
        [TestCase(GovernmentType.CentralizedRepublic, "THE STATE APPARATUS")]
        [TestCase(GovernmentType.Monarchy, "THE ROYAL COURT")]
        public void ConstitutionalSettlementRenamesButDoesNotReplaceBlocs(GovernmentType target, string institutionalName)
        {
            var country = state.PlayerCountry;
            var gov = country.government;
            gov.type = target == GovernmentType.PresidentialRepublic || target == GovernmentType.ParliamentaryRepublic
                ? GovernmentType.CentralizedRepublic : GovernmentType.PresidentialRepublic;
            gov.factions.Clear();
            GovernmentSystem.EnsureFactions(state, country);
            var blocs = gov.factions.ToArray();
            var themes = blocs.Select(f => f.theme).ToArray();
            var shares = blocs.Select(f => f.share).ToArray();
            var goodwill = blocs.Select(f => f.disposition).ToArray();
            float support = GovernmentSystem.FactionSupport(gov);
            string oldName = blocs[0].name;
            state.AddChronicle(ChronicleCategory.Political, country.id, oldName);
            gov.constitutionalTarget = target;
            typeof(GovernmentSystem).GetMethod("CompleteConstitutionalChange", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { state, country, gov });
            Assert.AreEqual(target, gov.type);
            Assert.AreEqual(institutionalName, blocs[0].name);
            Assert.AreEqual(gov.IsElective ? "THE OVERSIGHT BLOC" : "THE REFORM CIRCLE", blocs[1].name);
            CollectionAssert.AreEqual(blocs, gov.factions);
            CollectionAssert.AreEqual(themes, blocs.Select(f => f.theme));
            CollectionAssert.AreEqual(shares, blocs.Select(f => f.share));
            CollectionAssert.AreEqual(goodwill, blocs.Select(f => f.disposition));
            Assert.AreEqual(support, GovernmentSystem.FactionSupport(gov));
            Assert.IsTrue(state.chronicle.Any(e => e.text == oldName), "old records must not be rewritten");
            Assert.IsTrue(state.chronicle.Any(e => e.text.Contains(oldName + " → " + blocs[0].name)));
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            CollectionAssert.AreEqual(blocs.Select(f => f.name), loaded.PlayerCountry.government.factions.Select(f => f.name));
        }

        [Test]
        public void SuccessfulCoupRenamesImmediatelyWithoutErasingLiberty()
        {
            var country = state.PlayerCountry;
            country.government.type = GovernmentType.PresidentialRepublic;
            country.government.factions.Clear();
            GovernmentSystem.EnsureFactions(state, country);
            var reform = country.government.factions[1];
            reform.disposition = 74;
            float share = reform.share;
            typeof(RegimeSystem).GetMethod("SucceedingCoup", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { state, country, new System.Random(47) });
            Assert.AreEqual("THE REFORM CIRCLE", reform.name);
            Assert.AreEqual("THE STATE APPARATUS", country.government.factions[0].name);
            Assert.AreSame(reform, country.government.factions[1]);
            Assert.AreEqual(OppositionTheme.Liberty, reform.theme);
            Assert.AreEqual(74, reform.disposition);
            Assert.AreEqual(share, reform.share);
            Assert.AreEqual(35, GovernmentSystem.FactionDispositionTarget(reform.theme, CivicPosture.Restrictive));
        }

        [Test]
        public void LegacyLabelsRepairOnlyOnMutationAndOnlyOnce()
        {
            var country = state.PlayerCountry;
            var gov = country.government;
            gov.type = GovernmentType.PresidentialRepublic;
            gov.factions.Clear();
            GovernmentSystem.EnsureFactions(state, country);
            gov.type = GovernmentType.CentralizedRepublic;
            string before = SaveSystem.ToJson(state);
            GovernmentSystem.FactionsFor(state, country);
            Assert.AreEqual(before, SaveSystem.ToJson(state), "reading must not repair a save");
            int records = state.chronicle.Count;
            GovernmentSystem.EnsureFactions(state, country);
            Assert.AreEqual("THE REFORM CIRCLE", gov.factions[1].name);
            Assert.AreEqual(records + 1, state.chronicle.Count);
            string repaired = SaveSystem.ToJson(state);
            GovernmentSystem.EnsureFactions(state, country);
            Assert.AreEqual(repaired, SaveSystem.ToJson(state));
            gov.type = GovernmentType.PresidentialRepublic;
            GovernmentSystem.EnsureFactions(state, country);
            Assert.AreEqual("THE REFORM BENCH", gov.factions[1].name);
        }

        [Test]
        public void IdentityRepairLeavesCustomNamesAndUnmatchedConcernsAlone()
        {
            var country = state.PlayerCountry;
            var gov = country.government;
            gov.type = GovernmentType.CentralizedRepublic;
            gov.factions.Clear();
            gov.factions.Add(new Faction { name = "THE REFORM BENCH", theme = OppositionTheme.War });
            gov.factions.Add(new Faction { name = "A HISTORIC COALITION", theme = OppositionTheme.Liberty });
            string before = SaveSystem.ToJson(state);
            GovernmentSystem.RefreshFactionNames(state, country);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            gov.factions.Clear();
            before = SaveSystem.ToJson(state);
            GovernmentSystem.RefreshFactionNames(state, country);
            Assert.AreEqual(before, SaveSystem.ToJson(state), "a transition must not seed an empty ledger");
        }

        [Test]
        public void ForeignIdentityRepairUsesTheSameNamesWithoutAnOperatorNotice()
        {
            var country = state.FindCountry("CHN");
            country.government.type = GovernmentType.CentralizedRepublic;
            country.government.factions.Clear();
            GovernmentSystem.EnsureFactions(state, country);
            country.government.type = GovernmentType.ParliamentaryRepublic;
            int notices = state.notifications.Count;
            GovernmentSystem.RefreshFactionNames(state, country);
            Assert.AreEqual("THE OVERSIGHT BLOC", country.government.factions[1].name);
            Assert.AreEqual(notices, state.notifications.Count);
            Assert.AreEqual(Publicity.Secret, state.chronicle.Last().publicity);
            foreach (var bloc in country.government.factions)
                Assert.LessOrEqual($"COURT {bloc.name} [2 PC]".Length, 34);
        }

        [Test]
        public void FailedConstitutionalChangeKeepsTheOldIdentity()
        {
            var country = state.PlayerCountry;
            var gov = country.government;
            gov.type = GovernmentType.PresidentialRepublic;
            gov.factions.Clear();
            GovernmentSystem.EnsureFactions(state, country);
            gov.constitutionalTarget = GovernmentType.CentralizedRepublic;
            var names = gov.factions.Select(f => f.name).ToArray();
            typeof(GovernmentSystem).GetMethod("AbandonConstitutionalChange", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { state, country, gov, "test: insufficient support" });
            CollectionAssert.AreEqual(names, gov.factions.Select(f => f.name));
            Assert.AreEqual(GovernmentType.PresidentialRepublic, gov.type);
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void RenamedBlocsRenderAndCourtUnderTheirPersistedIdentity(int columns)
        {
            WithController(() =>
            {
                var country = state.PlayerCountry;
                country.government.type = GovernmentType.CentralizedRepublic;
                country.government.factions.Clear();
                GovernmentSystem.EnsureFactions(state, country);
                country.government.type = GovernmentType.ParliamentaryRepublic;
                GovernmentSystem.RefreshFactionNames(state, country);
                var view = new GovernmentView();
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                view.Refresh();
                string before = SaveSystem.ToJson(state);
                view.Refresh();
                Assert.AreEqual(before, SaveSystem.ToJson(state));
                foreach (var bloc in country.government.factions)
                    Assert.IsTrue(view.Root.Query<Button>().ToList().Any(b => b.text == $"COURT {AsciiChart.Cell(bloc.name, columns - 17).TrimEnd()} [2 PC]"));
                foreach (var label in view.Root.Query<Label>().ToList().Where(l => l.ClassListContains("terminal-text")))
                    foreach (string line in label.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
            });
        }

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
                    Assert.IsTrue(buttons.Any(b => b.text == $"COURT {AsciiChart.Cell(faction.name, columns - 17).TrimEnd()} [2 PC]" && b.enabledSelf));
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

        static void ResolveShares(GameState world, CountryState country)
            => typeof(GovernmentSystem).GetMethod("UpdateFactionShares", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { world, country });

        [TestCase(GovernmentType.ParliamentaryRepublic)]
        [TestCase(GovernmentType.DominantPartyState)]
        public void InfluenceTargetsFollowConditionsNotCourtingOrReadFrequency(GovernmentType type)
        {
            var c = state.PlayerCountry;
            c.government.type = type;
            c.government.factions.Clear();
            c.government.corruption = 0;
            c.government.civicPosture = CivicPosture.Standard;
            c.livingStandards = 50;
            string before = SaveSystem.ToJson(state);
            var calm = GovernmentSystem.FactionShareTargets(state, c);
            CollectionAssert.AreEqual(new float[] { .45f, .30f, .25f }, calm);
            for (int i = 0; i < 20; i++) CollectionAssert.AreEqual(calm, GovernmentSystem.FactionShareTargets(state, c));
            Assert.AreEqual(before, SaveSystem.ToJson(state), "preview must not establish an old save's ledger");
            GovernmentSystem.EnsureFactions(state, c);
            c.livingStandards = 0;
            var hardship = GovernmentSystem.FactionShareTargets(state, c);
            Assert.AreEqual(.36f, hardship[0], .00001);
            Assert.AreEqual(.24f, hardship[1], .00001);
            Assert.AreEqual(.40f, hardship[2], .00001);
            GovernmentSystem.CourtFaction(c.government, OppositionTheme.Hardship, 20);
            CollectionAssert.AreEqual(hardship, GovernmentSystem.FactionShareTargets(state, c), "goodwill cannot buy political weight");
            c.livingStandards = 80;
            c.government.corruption = 100;
            c.government.civicPosture = CivicPosture.Restrictive;
            var institutional = GovernmentSystem.FactionShareTargets(state, c);
            Assert.AreEqual(.45f / 1.3f, institutional[0], .00001);
            Assert.AreEqual(.60f / 1.3f, institutional[1], .00001);
            Assert.AreEqual(.25f / 1.3f, institutional[2], .00001);
        }

        [TestCase(-5f, 1f)]
        [TestCase(0f, 1f)]
        [TestCase(25f, .5f)]
        [TestCase(50f, 0f)]
        [TestCase(100f, 0f)]
        public void HardshipInfluencePressureHasBoundedEdges(float standards, float expected)
        {
            state.PlayerCountry.livingStandards = standards;
            Assert.AreEqual(expected, GovernmentSystem.FactionInfluencePressure(state.PlayerCountry, OppositionTheme.Hardship));
        }

        [Test]
        public void InfluenceConservesPowerSaturatesAndRecoversWithoutResettingGoodwill()
        {
            var c = state.PlayerCountry;
            var g = c.government;
            g.type = GovernmentType.DominantPartyState;
            g.factions.Clear();
            g.corruption = 0; c.livingStandards = 0;
            GovernmentSystem.EnsureFactions(state, c);
            var names = g.factions.Select(f => f.name).ToArray();
            var dispositions = g.factions.Select(f => f.disposition).ToArray();
            ResolveShares(state, c);
            Assert.AreEqual(.253f, g.factions[2].share, .00001, "one month closes only 2% of the gap, not an instant transfer");
            Assert.IsTrue(state.notifications.Any(n => n.title == "POLITICAL INFLUENCE SHIFTS" && n.body.Contains("25.00% -> 25.30%") && n.body.Contains("low living standards")));
            for (int i = 0; i < 600; i++)
            {
                ResolveShares(state, c);
                Assert.AreEqual(1, g.factions.Sum(f => f.share), .00001);
                Assert.That(g.factions[2].share, Is.InRange(.25f, .40001f));
                Assert.IsTrue(g.factions.All(f => f.share > 0));
            }
            Assert.AreEqual(.4f, g.factions[2].share, .00001);
            c.livingStandards = 80;
            for (int i = 0; i < 600; i++) ResolveShares(state, c);
            Assert.AreEqual(.25f, g.factions[2].share, .00001, "removing pressure must undo concentration without a new resource");
            CollectionAssert.AreEqual(names, g.factions.Select(f => f.name));
            CollectionAssert.AreEqual(dispositions, g.factions.Select(f => f.disposition));
        }

        [TestCase(GovernmentType.ParliamentaryRepublic)]
        [TestCase(GovernmentType.DominantPartyState)]
        public void RealGovernmentMonthUsesNewSharesInItsBackingTarget(GovernmentType type)
        {
            var c = state.PlayerCountry;
            var g = c.government;
            g.type = type; g.factions.Clear(); g.corruption = 80;
            g.civicPosture = CivicPosture.Restrictive;
            g.legislativeSupport = 65; g.eliteCohesion = 65;
            PushElectionsFarOut(g, state.date);
            GovernmentSystem.EnsureFactions(state, c);
            g.factions[0].disposition = 50; g.factions[1].disposition = 20; g.factions[2].disposition = 50;
            var original = g.factions.Select(f => f.share).ToArray();
            GovernmentSystem.MonthlyUpdate(state);
            var targets = GovernmentSystem.FactionShareTargets(state, c);
            for (int i = 0; i < original.Length; i++)
                Assert.AreEqual(original[i] + (targets[i] - original[i]) * .02f, g.factions[i].share, .00001);
            Assert.Greater(g.factions[1].share, .3f, "the monthly pipeline, not a test-only helper, must move power");
            float target = g.IsElective
                ? c.governmentApproval * .7f + c.pillars.government * .3f + g.brokeredSupport * .45f
                    + GovernmentSystem.FactionSupportShift(g) + GovernmentSystem.FactionSupport(g) - OppositionSystem.SupportDrag(g)
                : 40f + c.pillars.government * .35f + c.stability * .25f - c.warExhaustion * .15f
                    + g.brokeredSupport * .45f + GovernmentSystem.FactionCohesionShift(g) + GovernmentSystem.FactionSupport(g);
            target = System.Math.Max(0f, System.Math.Min(100f, target));
            Assert.AreEqual(65 + (target - 65) * (g.IsElective ? .08f : .06f),
                g.IsElective ? g.legislativeSupport : g.eliteCohesion, .0001);
        }

        [Test]
        public void InfluenceHandlesOtherLedgersAndSaveResumeDeterministically()
        {
            var foreign = state.countries.First(c => !c.isPlayer);
            foreign.government.factions.Clear();
            foreign.government.factions.Add(new Faction { name = "War", theme = OppositionTheme.War, share = .8f, disposition = 20 });
            foreign.government.factions.Add(new Faction { name = "Unknown", theme = (OppositionTheme)999, share = .2f, disposition = 80 });
            float capital = state.politicalCapital;
            int notices = state.notifications.Count, initiatives = state.initiativesThisYear;
            var targets = GovernmentSystem.FactionShareTargets(state, foreign);
            CollectionAssert.AreEqual(new float[] { .5f, .5f }, targets);
            ResolveShares(state, foreign);
            Assert.AreEqual(.794f, foreign.government.factions[0].share, .00001);
            Assert.AreEqual(capital, state.politicalCapital);
            Assert.AreEqual(notices, state.notifications.Count);
            Assert.AreEqual(initiatives, state.initiativesThisYear);
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            for (int i = 0; i < 120; i++)
            {
                ResolveShares(state, foreign);
                ResolveShares(loaded, loaded.FindCountry(foreign.id));
            }
            Assert.AreEqual(SaveSystem.ToJson(state), SaveSystem.ToJson(loaded));
            foreign.government.factions.Reverse();
            CollectionAssert.AreEqual(targets, GovernmentSystem.FactionShareTargets(state, foreign));
        }

        [Test]
        public void DuplicateConcernsAndReorderedOrZeroLedgersStillShareOnePool()
        {
            var c = state.PlayerCountry;
            c.government.factions.Clear();
            c.livingStandards = 0;
            c.government.factions.Add(new Faction { name = "West", theme = OppositionTheme.Hardship, share = 0 });
            c.government.factions.Add(new Faction { name = "East", theme = OppositionTheme.Hardship, share = 0 });
            c.government.factions.Add(new Faction { name = "Apparatus", theme = OppositionTheme.Drift, share = 0 });
            var targets = GovernmentSystem.FactionShareTargets(state, c);
            Assert.AreEqual(.5f / 1.45f, targets[0], .00001);
            Assert.AreEqual(targets[0], targets[1]);
            Assert.AreEqual(.45f / 1.45f, targets[2], .00001);
            ResolveShares(state, c);
            CollectionAssert.AreEqual(targets, c.government.factions.Select(f => f.share));
            c.government.factions.Reverse();
            CollectionAssert.AreEqual(targets.Reverse(), GovernmentSystem.FactionShareTargets(state, c));
            Assert.AreEqual(1f, c.government.factions.Sum(f => f.share), .00001);
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void InfluenceReadoutExplainsTheTendencyWithoutChangingTheLedger(int columns)
        {
            WithController(() =>
            {
                var view = new GovernmentView();
                view.Refresh();
                state.PlayerCountry.government.factions.Clear();
                state.PlayerCountry.livingStandards = 0;
                state.PlayerCountry.government.corruption = 0;
                state.PlayerCountry.government.civicPosture = CivicPosture.Standard;
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                string before = SaveSystem.ToJson(state);
                view.Refresh();
                Assert.AreEqual(before, SaveSystem.ToJson(state));
                var labels = view.Root.Query<Label>().ToList().Where(l => l.ClassListContains("terminal-text"));
                string text = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", labels.Select(l => l.text)), @"\s+", " ");
                StringAssert.Contains("conditions persist: 40.0%", text);
                StringAssert.Contains("Driver: low living standards; extra pressure present", text);
                StringAssert.Contains("not an instant transfer", text);
                Assert.AreEqual(3, view.Root.Query<Button>().ToList().Count(b => b.text.StartsWith("COURT ")));
                foreach (var label in labels)
                    foreach (var line in label.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
            });
        }

        static void ResolveGoodwill(GovernmentState gov)
            => typeof(GovernmentSystem).GetMethod("DriftFactions", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { gov });

        [TestCase(false)]
        [TestCase(true)]
        public void FactionReadsAreDetachedIncludingEveryEntry(bool seeded)
        {
            var c = state.PlayerCountry;
            c.government.factions.Clear();
            if (seeded) GovernmentSystem.EnsureFactions(state, c);
            string before = SaveSystem.ToJson(state);
            var first = GovernmentSystem.FactionsFor(state, c);
            var second = GovernmentSystem.FactionsFor(state, c);
            Assert.AreNotSame(first, second);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreNotSame(first[i], second[i]);
                Assert.AreEqual(first[i].name, second[i].name);
                Assert.AreEqual(first[i].theme, second[i].theme);
                Assert.AreEqual(first[i].share, second[i].share);
                Assert.AreEqual(first[i].disposition, second[i].disposition);
                first[i].name = "Overwritten"; first[i].theme = OppositionTheme.War;
                first[i].share = 0; first[i].disposition = 0;
            }
            first.Clear(); second.Reverse();
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            Assert.AreEqual(3, GovernmentSystem.FactionsFor(state, c).Count);
        }

        [TestCase(.000001f, "+<0.01")]
        [TestCase(-.000001f, "-<0.01")]
        [TestCase(.009f, "+<0.01")]
        [TestCase(-.009f, "-<0.01")]
        [TestCase(0f, "0")]
        [TestCase(2f, "+2")]
        public void TinyAppliedBlocChangesDoNotClaimSignedZero(float amount, string text)
            => Assert.AreEqual(text, GovernmentSystem.BlocDispositionChangeText(amount));

        [Test]
        public void TinyInquiryPreviewMatchesActualSignedReceiptWithoutChangingArithmetic()
        {
            WithController(() =>
            {
                var c = state.PlayerCountry;
                var g = c.government;
                g.corruption = .001f; g.factions.Clear();
                g.factions.Add(new Faction { name = "Reform", theme = OppositionTheme.Liberty, share = .5f, disposition = 50 });
                g.factions.Add(new Faction { name = "Recipients", theme = OppositionTheme.Hardship, share = .5f, disposition = 50 });
                var expected = g.factions.Select(f => System.Math.Max(0, System.Math.Min(100,
                    f.disposition + GovernmentSystem.InquiryReaction(f.theme, g.corruption)))).ToArray();
                var view = new GovernmentView(); view.Refresh();
                string text = string.Join(" ", view.Root.Query<Label>().ToList().Select(l => l.text));
                StringAssert.Contains("PUBLIC INQUIRY +<0.01", text);
                StringAssert.Contains("PUBLIC INQUIRY -<0.01", text);
                Assert.IsTrue(GameController.Instance.LaunchInquiry());
                CollectionAssert.AreEqual(expected, g.factions.Select(f => f.disposition));
                string receipt = state.notifications.Last(n => n.title == "INQUIRY CONCLUDED").body;
                StringAssert.Contains("Reform: disposition +<0.01", receipt);
                StringAssert.Contains("Recipients: disposition -<0.01", receipt);
                Assert.Greater(g.factions[0].disposition, 50);
                Assert.Less(g.factions[1].disposition, 50);
            });
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void LongNamedButtonsFitAndStillCourtTheirExactLedgerEntry(int columns)
        {
            WithController(() =>
            {
                var g = state.PlayerCountry.government;
                g.factions.Clear();
                string first = string.Join(" ", Enumerable.Repeat("REGIONAL COALITION", 12));
                string second = first + " TWO";
                g.factions.Add(new Faction { name = first, theme = OppositionTheme.Hardship, share = .7f, disposition = 50 });
                g.factions.Add(new Faction { name = second, theme = OppositionTheme.Hardship, share = .3f, disposition = 50 });
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                var view = new GovernmentView(); view.Refresh();
                var buttons = view.Root.Query<Button>().ToList().Where(b => b.text.StartsWith("COURT ")).ToList();
                Assert.AreEqual(2, buttons.Count);
                foreach (var b in buttons)
                {
                    Assert.LessOrEqual(b.text.Length, columns - 4, "leave margin for button padding");
                    StringAssert.EndsWith(" [2 PC]", b.text);
                    StringAssert.Contains("…", b.text);
                }
                Assert.IsTrue(view.Root.Query<Label>().ToList().Any(l =>
                    System.Text.RegularExpressions.Regex.Replace(l.text ?? "", @"\s+", " ").Trim() == second),
                    "the full name must remain visible above its abbreviated control");
                var clickable = typeof(Button).GetProperty("clickable")?.GetValue(buttons[1]);
                if (clickable != null) clickable.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(clickable, new object[] { null });
                else typeof(Button).GetMethod("SendClick").Invoke(buttons[1], null);
                Assert.AreEqual(50, g.factions[0].disposition);
                Assert.AreEqual(59, g.factions[1].disposition);
                StringAssert.Contains(second + " has been courted", state.notifications.Last().body);
            });
        }

        [Test]
        public void NeutralBlocPanelIsCompactAndExactApiIsUnambiguous()
        {
            Assert.IsNotNull(typeof(GameController).GetMethod("CourtFactionAtIndex", new[] { typeof(int) }));
            Assert.IsNull(typeof(GameController).GetMethod("CourtFaction", new[] { typeof(int) }));
            WithController(() =>
            {
                GovernmentSystem.EnsureFactions(state, state.PlayerCountry);
                var view = new GovernmentView(); view.Refresh();
                var labels = view.Root.Query<Label>().ToList();
                Assert.AreEqual(2, labels.Count(l => (l.text ?? "").Contains("Goodwill returns toward 50")));
                Assert.IsFalse(labels.Any(l => (l.text ?? "").Contains("OPEN 50, STANDARD 50")));
            });
        }

        [TestCase(CivicPosture.Open, 55f)]
        [TestCase(CivicPosture.Standard, 40f)]
        [TestCase(CivicPosture.Restrictive, 25f)]
        public void EmergencyGoodwillTargetIsPureAndLimitedToLiberty(CivicPosture posture, float target)
        {
            string before = SaveSystem.ToJson(state);
            Assert.AreEqual(target, GovernmentSystem.FactionDispositionTarget(OppositionTheme.Liberty, posture, true));
            Assert.AreEqual(target + 10, GovernmentSystem.FactionDispositionTarget(OppositionTheme.Liberty, posture, false));
            foreach (var theme in new[] { OppositionTheme.Drift, OppositionTheme.Hardship,
                OppositionTheme.Corruption, OppositionTheme.War, (OppositionTheme)999 })
                Assert.AreEqual(50, GovernmentSystem.FactionDispositionTarget(theme, posture, true));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [TestCase(GovernmentType.PresidentialRepublic)]
        [TestCase(GovernmentType.ParliamentaryRepublic)]
        [TestCase(GovernmentType.DominantPartyState)]
        [TestCase(GovernmentType.CentralizedRepublic)]
        [TestCase(GovernmentType.Monarchy)]
        public void EmergencyGoodwillUsesPersistedConcernsForEveryActor(GovernmentType type)
        {
            foreach (var c in state.countries)
            {
                var g = c.government;
                g.type = type; g.civicPosture = CivicPosture.Standard;
                g.leader.age = 40; g.legislativeSupport = g.eliteCohesion = 80;
                PushElectionsFarOut(g, state.date);
                g.emergencyPowers = true; g.emergencyPowersMonthsRemaining = 6;
                g.factions.Clear();
                g.factions.Add(new Faction { name = "First", theme = OppositionTheme.Liberty, share = .2f, disposition = 50 });
                g.factions.Add(new Faction { name = "Second", theme = OppositionTheme.Liberty, share = .3f, disposition = 50 });
                g.factions.Add(new Faction { name = "Not pro-emergency", theme = OppositionTheme.Corruption, share = .5f, disposition = 50 });
            }
            GovernmentSystem.MonthlyUpdate(state);
            foreach (var c in state.countries)
            {
                var g = c.government;
                Assert.AreEqual(type, g.type);
                Assert.AreEqual(49.8f, g.factions[0].disposition, .00001);
                Assert.AreEqual(49.8f, g.factions[1].disposition, .00001);
                Assert.AreEqual(50, g.factions[2].disposition);
                Assert.Less(GovernmentSystem.FactionSupport(g), 0);
                Assert.AreEqual(5, g.emergencyPowersMonthsRemaining);
            }
        }

        [Test]
        public void EmergencyExpiryRestoresTheTargetNotGoodwillAndSaveResumeMatches()
        {
            var g = state.PlayerCountry.government;
            g.type = GovernmentType.PresidentialRepublic;
            g.leader.age = 40; g.legislativeSupport = 80;
            PushElectionsFarOut(g, state.date);
            g.civicPosture = CivicPosture.Standard;
            g.emergencyPowers = true; g.emergencyPowersMonthsRemaining = 6;
            g.factions.Clear();
            g.factions.Add(new Faction { name = "Reformers", theme = OppositionTheme.Liberty, share = 1, disposition = 50 });
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(SaveSystem.ToJson(state), SaveSystem.ToJson(loaded));
            float expected = 50;
            for (int month = 1; month <= 12; month++)
            {
                // Drift precedes expiry: the sixth (last authorised) month still counts.
                expected += ((month <= 6 ? 40 : 50) - expected) * .02f;
                GovernmentSystem.MonthlyUpdate(state);
                GovernmentSystem.MonthlyUpdate(loaded);
                Assert.AreEqual(expected, g.factions[0].disposition, .00001, "month " + month);
                Assert.AreEqual(month < 6, g.emergencyPowers);
                if (month == 6)
                {
                    Assert.Less(g.factions[0].disposition, 50);
                    StringAssert.Contains("not instantly restored", state.notifications.Last(n => n.title == "EMERGENCY POWERS LAPSED").body);
                }
                state.date = state.date.NextMonth(); loaded.date = loaded.date.NextMonth();
                Assert.AreEqual(SaveSystem.ToJson(state), SaveSystem.ToJson(loaded));
            }
        }

        [Test]
        public void SustainedEmergencyGoodwillIsBoundedAndCannotWriteAnythingElse()
        {
            var g = state.PlayerCountry.government;
            g.factions.Clear();
            g.factions.Add(new Faction { name = "Custom", theme = OppositionTheme.Liberty, share = .4f, disposition = 100 });
            g.civicPosture = CivicPosture.Restrictive; g.emergencyPowers = true;
            string before = SaveSystem.ToJson(state);
            for (int i = 0; i < 600; i++) ResolveGoodwill(g);
            Assert.AreEqual(25, g.factions[0].disposition, .001);
            g.emergencyPowers = false;
            ResolveGoodwill(g);
            Assert.AreEqual(25.2f, g.factions[0].disposition, .001);
            for (int i = 0; i < 600; i++) ResolveGoodwill(g);
            Assert.AreEqual(35, g.factions[0].disposition, .001);
            g.emergencyPowers = true; g.factions[0].disposition = 100;
            Assert.AreEqual(before, SaveSystem.ToJson(state), "no shares, costs, rewards, identity or traffic changes in drift");
        }

        [Test]
        public void DeclaringEmergencyDoesNotSeedOrBuyGoodwillAndPostureNoticeUsesActualTarget()
        {
            WithController(() =>
            {
                var g = state.PlayerCountry.government;
                g.type = GovernmentType.PresidentialRepublic; g.civicPosture = CivicPosture.Standard;
                g.emergencyPowers = false; g.factions.Clear();
                state.politicalCapital = 0;
                string refused = SaveSystem.ToJson(state);
                Assert.IsFalse(GameController.Instance.DeclareEmergencyPowers());
                Assert.AreEqual(refused, SaveSystem.ToJson(state));
                state.politicalCapital = 20;
                float cost = GovernmentSystem.EmergencyPowersCost * 1.4f *
                    (1 - TechnologySystem.Effectiveness(state.PlayerCountry, "CAP_EMERGENCY") * .35f);
                int initiative = state.initiativesThisYear;
                Assert.IsTrue(GameController.Instance.DeclareEmergencyPowers());
                Assert.AreEqual(20 - cost, state.politicalCapital, .0001);
                Assert.AreEqual(initiative + 1, state.initiativesThisYear);
                Assert.AreEqual(0, g.factions.Count);
                StringAssert.Contains("40/100", state.notifications.Last(n => n.title == "EMERGENCY POWERS DECLARED").body);
                Assert.IsTrue(SaveSystem.Load().PlayerCountry.government.emergencyPowers);
                string active = SaveSystem.ToJson(state);
                Assert.IsFalse(GameController.Instance.DeclareEmergencyPowers());
                Assert.AreEqual(active, SaveSystem.ToJson(state));
                GovernmentSystem.EnsureFactions(state, state.PlayerCountry);
                var before = g.factions.Select(f => f.disposition).ToArray();
                Assert.IsTrue(GameController.Instance.SetCivicPosture(CivicPosture.Open));
                StringAssert.Contains("55/100", state.notifications.Last(n => n.title == "CIVIC POSTURE CHANGED").body);
                StringAssert.Contains("55/100. This includes", state.notifications.Last(n => n.title == "CIVIC POSTURE CHANGED").body);
                StringAssert.Contains("force. Other concerns", state.notifications.Last(n => n.title == "CIVIC POSTURE CHANGED").body);
                CollectionAssert.AreEqual(before, g.factions.Select(f => f.disposition));
            });
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void EmergencyBlocTargetsAreExplainedBeforeOrderingAndWhileActive(int columns)
        {
            WithController(() =>
            {
                var g = state.PlayerCountry.government;
                g.type = GovernmentType.PresidentialRepublic; g.civicPosture = CivicPosture.Standard; g.factions.Clear();
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                var view = new GovernmentView(); view.Refresh(); // existing chamber setup
                foreach (bool active in new[] { false, true })
                {
                    g.emergencyPowers = active;
                    string before = SaveSystem.ToJson(state);
                    view.Refresh(); view.Refresh();
                    Assert.AreEqual(before, SaveSystem.ToJson(state));
                    var labels = view.Root.Query<Label>().ToList().Where(l => l.ClassListContains("terminal-text"));
                    string text = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", labels.Select(l => l.text)), @"\s+", " ");
                    StringAssert.Contains("Current resting level " + (active ? "40" : "50"), text);
                    StringAssert.Contains("lower these ordinary resting levels by 10", text);
                    StringAssert.Contains("Expiry restores the target, not lost goodwill", text);
                    StringAssert.Contains("Other bloc concerns are unchanged", text);
                    foreach (var label in labels)
                        foreach (var line in label.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
                }
            });
        }

        [TestCase(CivicPosture.Open, 65f)]
        [TestCase(CivicPosture.Standard, 50f)]
        [TestCase(CivicPosture.Restrictive, 35f)]
        public void HeldCivicPolicyTargetsOnlyThePersistedLibertyConcern(CivicPosture posture, float expected)
        {
            string before = SaveSystem.ToJson(state);
            for (int i = 0; i < 20; i++)
            {
                Assert.AreEqual(expected, GovernmentSystem.FactionDispositionTarget(OppositionTheme.Liberty, posture));
                foreach (var theme in new[] { OppositionTheme.Drift, OppositionTheme.Hardship,
                    OppositionTheme.Corruption, OppositionTheme.War, (OppositionTheme)999 })
                    Assert.AreEqual(50f, GovernmentSystem.FactionDispositionTarget(theme, posture));
            }
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [TestCase(GovernmentType.PresidentialRepublic)]
        [TestCase(GovernmentType.ParliamentaryRepublic)]
        [TestCase(GovernmentType.DominantPartyState)]
        [TestCase(GovernmentType.CentralizedRepublic)]
        [TestCase(GovernmentType.Monarchy)]
        public void MonthlyPolicyGoodwillReachesBackingForEveryGovernmentAndActor(GovernmentType type)
        {
            foreach (var posture in new[] { CivicPosture.Open, CivicPosture.Standard, CivicPosture.Restrictive })
            {
                state = WorldFactory.CreateDebugWorld(seed: 6161);
                foreach (var c in state.countries)
                {
                    var g = c.government;
                    g.type = type; g.civicPosture = posture; g.factions.Clear();
                    g.leader.age = 40; g.leader.faction = "GOVERNING PARTY";
                    g.legislativeSupport = 65; g.eliteCohesion = 65;
                    g.emergencyPowers = false;
                    PushElectionsFarOut(g, state.date);
                    // Persisted concerns, arbitrary names, and duplicate themes:
                    // neither current regime nor largest-share selection may gate drift.
                    g.factions.Add(new Faction { name = "First", theme = OppositionTheme.Liberty, share = .2f, disposition = 50 });
                    g.factions.Add(new Faction { name = "Second", theme = OppositionTheme.Liberty, share = .3f, disposition = 50 });
                    g.factions.Add(new Faction { name = "Other", theme = OppositionTheme.Corruption, share = .5f, disposition = 20 });
                }
                GovernmentSystem.MonthlyUpdate(state);
                foreach (var c in state.countries)
                {
                    var g = c.government;
                    Assert.AreEqual(type, g.type);
                    float expected = posture == CivicPosture.Open ? 50.3f : posture == CivicPosture.Restrictive ? 49.7f : 50f;
                    Assert.AreEqual(expected, g.factions[0].disposition, .00001);
                    Assert.AreEqual(expected, g.factions[1].disposition, .00001);
                    Assert.AreEqual(20.6f, g.factions[2].disposition, .00001);
                    float target = g.IsElective
                        ? c.governmentApproval * .7f + c.pillars.government * .3f + g.brokeredSupport * .45f
                            + GovernmentSystem.FactionSupportShift(g) + GovernmentSystem.FactionSupport(g) - OppositionSystem.SupportDrag(g)
                        : 40f + c.pillars.government * .35f + c.stability * .25f - c.warExhaustion * .15f
                            + g.brokeredSupport * .45f + GovernmentSystem.FactionCohesionShift(g) + GovernmentSystem.FactionSupport(g);
                    target = System.Math.Max(0f, System.Math.Min(100f, target));
                    Assert.AreEqual(65 + (target - 65) * (g.IsElective ? .08f : .06f),
                        g.IsElective ? g.legislativeSupport : g.eliteCohesion, .0001,
                        "backing must read this month's goodwill, not last month's");
                }
            }
        }

        [TestCase(0f)]
        [TestCase(50f)]
        [TestCase(100f)]
        public void PolicyGoodwillSaturatesAndRecoversWithoutBuyingPower(float initial)
        {
            var c = state.PlayerCountry;
            var g = c.government;
            g.factions.Clear();
            g.factions.Add(new Faction { name = "Persistent reformers", theme = OppositionTheme.Liberty, share = .3f, disposition = initial });
            string before = SaveSystem.ToJson(state);
            foreach (var posture in new[] { CivicPosture.Restrictive, CivicPosture.Open, CivicPosture.Standard })
            {
                g.civicPosture = posture;
                float target = posture == CivicPosture.Open ? 65 : posture == CivicPosture.Restrictive ? 35 : 50;
                float distance = System.Math.Abs(g.factions[0].disposition - target);
                for (int i = 0; i < 600; i++)
                {
                    ResolveGoodwill(g);
                    float nextDistance = System.Math.Abs(g.factions[0].disposition - target);
                    Assert.LessOrEqual(nextDistance, distance);
                    distance = nextDistance;
                    Assert.That(g.factions[0].disposition, Is.InRange(0f, 100f));
                }
                Assert.AreEqual(target, g.factions[0].disposition, .001f);
            }
            Assert.AreEqual(.3f, g.factions[0].share);
            Assert.AreEqual("Persistent reformers", g.factions[0].name);
            // Restore only intended writes: no hidden rewards, costs, traffic or memory.
            var restored = SaveSystem.FromJson(before);
            g.civicPosture = restored.PlayerCountry.government.civicPosture;
            g.factions[0].disposition = initial;
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [Test]
        public void SwitchingPolicyCannotHarvestGoodwillAndCourtingStillFadesTowardPolicy()
        {
            WithController(() =>
            {
                var c = state.PlayerCountry;
                var g = c.government;
                g.type = GovernmentType.PresidentialRepublic;
                g.civicPosture = CivicPosture.Standard;
                g.factions.Clear();
                string empty = SaveSystem.ToJson(state);
                Assert.IsFalse(GameController.Instance.SetCivicPosture(CivicPosture.Standard));
                Assert.AreEqual(empty, SaveSystem.ToJson(state));
                Assert.IsTrue(GameController.Instance.SetCivicPosture(CivicPosture.Open));
                Assert.AreEqual(0, g.factions.Count, "changing policy must not seed an empty ledger");
                GovernmentSystem.EnsureFactions(state, c);
                var bloc = g.factions.Single(f => f.theme == OppositionTheme.Liberty);
                bloc.disposition = 65;
                var shares = g.factions.Select(f => f.share).ToArray();
                for (int i = 0; i < 4; i++)
                {
                    var posture = i % 2 == 0 ? CivicPosture.Restrictive : CivicPosture.Open;
                    float pc = state.politicalCapital;
                    int initiatives = state.initiativesThisYear;
                    Assert.IsTrue(GameController.Instance.SetCivicPosture(posture));
                    Assert.AreEqual(pc - GovernmentSystem.CivicPostureCost, state.politicalCapital);
                    Assert.AreEqual(initiatives + 1, state.initiativesThisYear, "only the ordinary action reward");
                    Assert.AreEqual(65, bloc.disposition, "switching without resolving time cannot accumulate goodwill");
                    CollectionAssert.AreEqual(shares, g.factions.Select(f => f.share));
                    var saved = SaveSystem.Load().PlayerCountry.government;
                    Assert.AreEqual(posture, saved.civicPosture);
                    Assert.AreEqual(65, saved.factions.Single(f => f.theme == OppositionTheme.Liberty).disposition);
                }
                state.politicalCapital = 0;
                string noFunds = SaveSystem.ToJson(state);
                Assert.IsFalse(GameController.Instance.SetCivicPosture(CivicPosture.Restrictive));
                Assert.AreEqual(noFunds, SaveSystem.ToJson(state));
                state.politicalCapital = 20;
                Assert.IsTrue(GameController.Instance.CourtFaction(OppositionTheme.Liberty));
                Assert.AreEqual(74, bloc.disposition);
                ResolveGoodwill(g);
                Assert.AreEqual(73.82f, bloc.disposition, .00001, "Open is a resting target, not another monthly bonus");
                Assert.IsTrue(state.notifications.Any(n => n.title == "CIVIC POSTURE CHANGED"
                    && n.body.Contains("35/100") && n.body.Contains("No bloc goodwill or influence is transferred")));
            });
        }

        [Test]
        public void PolicyGoodwillSurvivesSaveResumeAndDoesNotRewriteConstituencies()
        {
            var g = state.PlayerCountry.government;
            g.type = GovernmentType.PresidentialRepublic; g.factions.Clear();
            GovernmentSystem.EnsureFactions(state, state.PlayerCountry);
            g.civicPosture = CivicPosture.Restrictive;
            for (int i = 0; i < 120; i++) ResolveGoodwill(g);
            g.type = GovernmentType.Monarchy;
            var names = g.factions.Select(f => f.name).ToArray();
            var themes = g.factions.Select(f => f.theme).ToArray();
            string json = SaveSystem.ToJson(state);
            var loaded = SaveSystem.FromJson(json);
            Assert.AreEqual(json, SaveSystem.ToJson(loaded));
            for (int i = 0; i < 120; i++)
            {
                if (i == 60) g.civicPosture = loaded.PlayerCountry.government.civicPosture = CivicPosture.Open;
                ResolveGoodwill(g);
                ResolveGoodwill(loaded.PlayerCountry.government);
            }
            Assert.AreEqual(SaveSystem.ToJson(state), SaveSystem.ToJson(loaded));
            CollectionAssert.AreEqual(names, g.factions.Select(f => f.name));
            CollectionAssert.AreEqual(themes, g.factions.Select(f => f.theme));
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void PolicyTargetsAndSharedPoolDilutionAreExplainedWithoutWritingState(int columns)
        {
            WithController(() =>
            {
                var view = new GovernmentView();
                view.Refresh();
                var c = state.PlayerCountry;
                var g = c.government;
                g.type = GovernmentType.PresidentialRepublic; g.factions.Clear();
                g.civicPosture = CivicPosture.Standard; g.corruption = 0; c.livingStandards = 0;
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                string before = SaveSystem.ToJson(state);
                view.Refresh(); view.Refresh();
                Assert.AreEqual(before, SaveSystem.ToJson(state));
                var labels = view.Root.Query<Label>().ToList().Where(l => l.ClassListContains("terminal-text"));
                string text = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", labels.Select(l => l.text)), @"\s+", " ");
                StringAssert.Contains("OPEN 65, STANDARD 50, RESTRICTIVE 35", text);
                StringAssert.Contains("Current resting level 50", text);
                StringAssert.Contains("Switching policy grants no immediate goodwill", text);
                StringAssert.Contains("a bloc can lose share because others gain", text);
                foreach (var label in labels)
                    foreach (var line in label.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
                GovernmentSystem.EnsureFactions(state, c);
                ResolveShares(state, c);
                var notice = state.notifications.Last(n => n.title == "POLITICAL INFLUENCE SHIFTS");
                StringAssert.Contains("A bloc can lose share because others gain", notice.body);
                StringAssert.Contains("own driver: restrictive civic policy; extra pressure absent", notice.body);
                Assert.Less(g.factions.Single(f => f.theme == OppositionTheme.Liberty).share, .3f);
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
