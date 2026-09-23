using Brink.Core;
using Brink.Data;
using NUnit.Framework;
using System;
using System.Linq;
using Brink.UI;
using Brink.UI.Views;
using UnityEngine.UIElements;

namespace Brink.Tests
{
    public class HistoricalIdentityTests
    {
        static GameState EmptyRecord()
        {
            var s = WorldFactory.CreateDebugWorld(971);
            s.chronicle.Clear(); s.sanctions.Clear(); s.trade.Clear();
            s.treaties.Clear(); s.confrontations.Clear();
            return s;
        }

        static void SanctionRecord(GameState s, string actor, string target, Publicity publicity = Publicity.Public)
            => s.AddChronicle(ChronicleCategory.Economic, actor, "A public measure.", publicity,
                HistoricalEvent.SanctionsImposed, target);

        [Test]
        public void RealSanctionProducerWritesOneDatedPublicActAndRefusalWritesNone()
        {
            var s = EmptyRecord();
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(s, "USA", "CHN", SanctionSeverity.Coercive));
            var records = s.chronicle.Where(e => e.historicalEvent == HistoricalEvent.SanctionsImposed).ToList();
            Assert.AreEqual(1, records.Count); Assert.AreEqual("CHN", records[0].counterpartyId);
            Assert.AreEqual(s.date, records[0].date); Assert.AreEqual(Publicity.Public, records[0].publicity);
            string before = SaveSystem.ToJson(s);
            Assert.IsFalse(EconomySystem.ImposeSanctionsBy(s, "USA", "CHN", SanctionSeverity.Coercive));
            Assert.AreEqual(before, SaveSystem.ToJson(s));
        }

        [Test]
        public void LiftedMeasuresRemainEvidenceWithoutCountingActiveOrRepeatedTargetsTwice()
        {
            var s = EmptyRecord();
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(s, "USA", "CHN", SanctionSeverity.Routine));
            SanctionRecord(s, "USA", "CHN");
            Assert.AreEqual(1, HistoricalIdentitySystem.CoercionTargets(s, "USA").Count);
            s.commandPoints.current = 5;
            Assert.IsTrue(EconomySystem.LiftSanctions(s, new TurnManager(s), "CHN"));
            Assert.IsNull(s.FindSanction("USA", "CHN"));
            Assert.AreEqual(1, HistoricalIdentitySystem.CoercionTargets(s, "USA").Count);
            StringAssert.Contains("China", HistoricalIdentitySystem.CoercionPrecedent(s, "USA"));
            StringAssert.Contains("including measures since lifted", string.Join(" ", HistoricalIdentitySystem.KnownFor(s, "USA")));
        }

        [TestCase(119, 1)] [TestCase(120, 1)] [TestCase(121, 0)]
        public void AiWindowExpiresButArchiveDoesNot(int months, int expected)
        {
            var s = EmptyRecord(); SanctionRecord(s,"USA","CHN");
            for (int i = 0; i < months; i++) s.date = s.date.NextMonth();
            Assert.AreEqual(expected, HistoricalIdentitySystem.CoercionTargets(s,"USA").Count);
            Assert.AreEqual(1, HistoricalIdentitySystem.TurningPoints(s,"USA").Count);
            StringAssert.Contains("Economic coercion recorded", string.Join(" ", HistoricalIdentitySystem.KnownFor(s,"USA")));
        }

        [Test]
        public void SecretLegacyFutureAndOtherActorsAreNotPublicCoercionEvidence()
        {
            var s = EmptyRecord();
            SanctionRecord(s,"USA","CHN",Publicity.Secret);
            SanctionRecord(s,"CHN","USA");
            s.AddChronicle(ChronicleCategory.Economic,"USA","Sanctions imposed on Russia.",Publicity.Public);
            SanctionRecord(s,"USA","RUS"); s.chronicle.Last().date = s.date.NextMonth();
            Assert.AreEqual(0,HistoricalIdentitySystem.CoercionTargets(s,"USA").Count);
            var points=HistoricalIdentitySystem.TurningPoints(s,"USA");
            Assert.AreEqual(1,points.Count,"The public measure against us is our history, not our coercive act.");
            StringAssert.Contains("China imposed sanctions against United States",points[0].text);
        }

        [Test]
        public void RealAiObservationCarriesDatedMemoryIntoItsCoercionExpectation()
        {
            var s = EmptyRecord();
            var ai = s.aiStates.First(a => a.countryId == "CHN");
            s.FindRelationship("USA","CHN").relations = 20;
            foreach (string target in new[] {"CHN","RUS","IND"}) SanctionRecord(s,"USA",target);
            var control = SaveSystem.FromJson(SaveSystem.ToJson(s)); control.chronicle.Clear();
            var controlAi = control.aiStates.First(a => a.countryId == "CHN");
            for (int i = 0; i < 60; i++) { AIPrediction.Observe(s,ai); AIPrediction.Observe(control,controlAi); }
            Assert.Greater(ai.ModelOf("USA").economicCoercion, 50);
            Assert.AreEqual(0,controlAi.ModelOf("USA").economicCoercion);
            Assert.AreEqual(PredictedMove.Coerce,ai.ModelOf("USA").predictedMove);
            Assert.Greater(AIPrediction.Expectation(ai,"USA",PredictedMove.Coerce), 0);
            StringAssert.Contains("JAN 1984",ai.ModelOf("USA").coercionPrecedent);
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(s));
            Assert.AreEqual(ai.ModelOf("USA").coercionPrecedent,loaded.aiStates.First(a => a.countryId == "CHN").ModelOf("USA").coercionPrecedent);
        }

        [Test]
        public void AQuietDecadeMustBeObservedBeforeNonActionBecomesAClaim()
        {
            var s = EmptyRecord(); s.date = new GameDate(1993,12);
            Assert.IsFalse(string.Join(" ", HistoricalIdentitySystem.KnownFor(s,"USA")).Contains("No initiated confrontation"));
            s.date = new GameDate(1994,1);
            StringAssert.Contains("No initiated confrontation in 10 years",string.Join(" ",HistoricalIdentitySystem.KnownFor(s,"USA")));
            s.confrontations.Add(new Confrontation { initiatorId="CHN", defenderId="USA", startDate=s.date });
            StringAssert.Contains("No initiated confrontation",string.Join(" ",HistoricalIdentitySystem.KnownFor(s,"USA")));
            s.confrontations.Add(new Confrontation { initiatorId="USA", defenderId="CHN", startDate=s.date });
            Assert.IsFalse(string.Join(" ",HistoricalIdentitySystem.KnownFor(s,"USA")).Contains("No initiated confrontation"));
        }

        [Test]
        public void SuccessorDoesNotInheritItsParentsYearsOrConduct()
        {
            var s = EmptyRecord(); s.date = new GameDate(2034,1);
            s.countries.Add(new CountryState { id="NEW",displayName="Successor",foundedDate=s.date });
            SanctionRecord(s,"USA","CHN");
            string text = HistoricalIdentitySystem.Render(s,"NEW");
            Assert.IsFalse(text.Contains("No initiated confrontation in 50"));
            Assert.IsFalse(text.Contains("Economic coercion recorded against"));
            Assert.AreEqual(0,HistoricalIdentitySystem.TurningPoints(s,"NEW").Count);
        }

        [Test]
        public void SigningHistorySurvivesExpiryAndDoesNotPromiseCurrentAccess()
        {
            var s = EmptyRecord();
            s.treaties.Add(new Treaty {countryA="USA",countryB="CHN",signedDate=s.date,broken=true,brokenBy="USA",
                commitments=new System.Collections.Generic.List<TreatyCommitment>{TreatyCommitment.IntelligenceSharing}});
            string text=string.Join(" ",HistoricalIdentitySystem.KnownFor(s,"USA"));
            StringAssert.Contains("Intelligence-sharing agreements with 1",text);
            StringAssert.Contains("not proof of current access",text); StringAssert.Contains("broken by this state",text);
            Assert.AreEqual(1,HistoricalIdentitySystem.TurningPoints(s,"CHN").Count);
        }

        [Test]
        public void HiddenForeignStateCannotChangeItsHistoricalPortrait()
        {
            var s=EmptyRecord(); string before=HistoricalIdentitySystem.Render(s,"CHN");
            var foreign=s.FindCountry("CHN");foreign.pillars.military=99;foreign.resources.industrialCapacity=99;foreign.socialUnrest=99;
            s.AddChronicle(ChronicleCategory.Military,"CHN","SECRET MUST NOT LEAK",Publicity.Secret,HistoricalEvent.TurningPoint);
            s.AddChronicle(ChronicleCategory.Military,"CHN","SECOND SECRET MUST NOT CREATE A LABEL",Publicity.Secret);
            Assert.AreEqual(before,HistoricalIdentitySystem.Render(s,"CHN"));
        }

        [Test]
        public void PreStartAndFutureEntriesDoNotManufactureAnEarnedIdentity()
        {
            var s=EmptyRecord();
            for(int i=0;i<5;i++) { s.AddChronicle(ChronicleCategory.Economic,"USA","backstory",Publicity.Public);s.chronicle.Last().date=new GameDate(1975,1); }
            Assert.IsEmpty(HistoricalIdentitySystem.Build(s));
            foreach(var e in s.chronicle)e.date=s.date.NextMonth();
            Assert.IsEmpty(HistoricalIdentitySystem.Build(s));
        }

        [Test]
        public void TurningPointsAreDatedStableBoundedDetachedAndAvailableToBothParticipants()
        {
            var s=EmptyRecord();s.date=new GameDate(2034,1);
            for(int i=0;i<12;i++)s.chronicle.Add(new ChronicleEntry {date=new GameDate(2000+i,1),countryId="USA",counterpartyId="CHN",
                category=ChronicleCategory.Political,publicity=Publicity.Public,historicalEvent=HistoricalEvent.TurningPoint,text="Turn "+i});
            var points=HistoricalIdentitySystem.TurningPoints(s,"CHN");
            Assert.AreEqual(8,points.Count);Assert.AreEqual(2011,points[0].date.year);Assert.AreEqual(2004,points[7].date.year);
            points[0].text="changed";Assert.AreEqual("Turn 11",HistoricalIdentitySystem.TurningPoints(s,"CHN")[0].text);
            Assert.AreEqual(12,HistoricalIdentitySystem.TurningPoints(s,"USA",20).Count);
            Assert.IsEmpty(HistoricalIdentitySystem.TurningPoints(s,"USA",0));
        }

        [Test]
        public void MissingLegacyDatesAreNotInventedOrRenderedAsJanuaryZero()
        {
            var s=EmptyRecord();s.treaties.Add(new Treaty {countryA="USA",countryB="CHN"});
            Assert.DoesNotThrow(()=>HistoricalIdentitySystem.Render(s));
            Assert.IsEmpty(HistoricalIdentitySystem.TurningPoints(s,"USA"));
        }

        [Test]
        public void ReadersArePureAndSurviveSaveRoundTrip()
        {
            var s=EmptyRecord();SanctionRecord(s,"USA","CHN");
            string json=SaveSystem.ToJson(s),text=HistoricalIdentitySystem.Render(s);
            for(int i=0;i<10;i++){HistoricalIdentitySystem.Render(s);HistoricalIdentitySystem.Render(s,"CHN");}
            Assert.AreEqual(json,SaveSystem.ToJson(s));
            Assert.AreEqual(text,HistoricalIdentitySystem.Render(SaveSystem.FromJson(json)));
        }

        [Test]
        public void OldSavesDoNotInferTypedEventsFromTheirProse()
        {
            var s=EmptyRecord();s.AddChronicle(ChronicleCategory.Economic,"USA","Sanctions imposed on China.",Publicity.Public);
            string json=SaveSystem.ToJson(s);
            foreach(string field in new[]{"historicalEvent","counterpartyId","coercionPrecedent"})
                json=System.Text.RegularExpressions.Regex.Replace(json, @",?\s*"""+field+@"""\s*:\s*(0|null|"""")", "");
            var loaded=SaveSystem.FromJson(json);
            Assert.AreEqual(HistoricalEvent.None,loaded.chronicle.Single().historicalEvent);
            Assert.IsEmpty(HistoricalIdentitySystem.CoercionTargets(loaded,"USA"));
            Assert.AreEqual(7,loaded.saveVersion);
        }

        [Test]
        public void ProjectCompletionIsANamedTurningPointWithoutAnotherEventOrGrant()
        {
            var s=EmptyRecord();s.PlayerCountry.resources.treasury=100000;
            Assert.IsTrue(IndustrialSystem.BeginBy(s,"USA",EconomicSector.Energy,IndustrialScale.Maintenance));
            for(int i=0;i<12;i++)IndustrialSystem.MonthlyUpdate(s);
            var points=HistoricalIdentitySystem.TurningPoints(s,"USA");
            Assert.AreEqual(1,points.Count);StringAssert.Contains("PROJECT COMPLETE",points[0].text);
            Assert.AreEqual(1,s.chronicle.Count(e=>e.historicalEvent==HistoricalEvent.TurningPoint));
        }

        [Test]
        public void HistoricalCoercionRaisesTheExistingCounterObjectiveNotANewBudgetSlot()
        {
            var s=EmptyRecord();var ai=s.aiStates.First(a=>a.countryId=="CHN");
            ai.profile.caution=0;s.FindRelationship("USA","CHN").relations=20;
            foreach(string target in new[]{"CHN","RUS","IND"})SanctionRecord(s,"USA",target);
            for(int i=0;i<60;i++)AIPrediction.Observe(s,ai);
            ai.ModelOf("USA").confidence=1;
            var candidates=new System.Collections.Generic.List<AIObjective>();
            typeof(AISystem).GetMethod("AddCounterObjectives",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)
                .Invoke(null,new object[]{s,ai,s.FindCountry("CHN"),candidates});
            Assert.IsTrue(candidates.Any(o=>o.type==AIObjectiveType.InsulateEconomy && o.targetId=="USA"));
            Assert.AreEqual(0,ai.actionsThisMonth);
        }

        [Test]
        public void LivePipelineAndResumedHistoryRemainDeterministic()
        {
            var a=EmptyRecord();SanctionRecord(a,"USA","CHN");
            var b=SaveSystem.FromJson(SaveSystem.ToJson(a));
            var ta=new TurnManager(a);SimulationPipeline.Wire(ta,a);
            for(int i=0;i<24;i++)ta.EndMonth();
            var tb=new TurnManager(b);SimulationPipeline.Wire(tb,b);
            for(int i=0;i<12;i++)tb.EndMonth();
            b=SaveSystem.FromJson(SaveSystem.ToJson(b));tb=new TurnManager(b);SimulationPipeline.Wire(tb,b);
            for(int i=0;i<12;i++)tb.EndMonth();
            Assert.AreEqual(SaveSystem.ToJson(a),SaveSystem.ToJson(b));
            Assert.AreEqual(HistoricalIdentitySystem.Render(a),HistoricalIdentitySystem.Render(b));
        }

        [TestCase(34)] [TestCase(49)] [TestCase(64)] [TestCase(104)]
        public void ChronicleAndDossierExposeWrappedIdentityWithoutMutatingTheWorld(int columns)
        {
            var s=EmptyRecord();SanctionRecord(s,"USA","CHN");
            var gc=GameController.Instance;var prior=gc.State;
            try
            {
                typeof(GameController).GetProperty("State").SetValue(gc,s);
                TerminalMetrics.Update((columns+1)*8f,8f,500f,Breakpoints.FromColumns(columns));
                string json=SaveSystem.ToJson(s);
                foreach(TerminalView view in new TerminalView[]{new ChronicleView(),new DossierView()})
                {
                    view.Refresh();
                    var label=view.Root.Query<Label>().ToList().Single(l=>(l.text??"").Contains("HISTORICAL IDENTITY"));
                    foreach(string line in label.text.Split('\n'))Assert.LessOrEqual(line.Length,columns);
                    StringAssert.Contains("HISTORICAL",label.text);
                }
                Assert.AreEqual(json,SaveSystem.ToJson(s));
            }
            finally {typeof(GameController).GetProperty("State").SetValue(gc,prior);TerminalMetrics.ResetForTests();}
        }
        [Test]
        public void RepeatedPublicRecordCreatesIdentityWithoutChangingState()
        {
            var state = WorldFactory.CreateDebugWorld(971);
            state.chronicle.Add(new ChronicleEntry { date = state.date, category = ChronicleCategory.Diplomatic, countryId = state.playerCountryId, text = "Brokered settlement.", publicity = Publicity.Public });
            state.chronicle.Add(new ChronicleEntry { date = state.date, category = ChronicleCategory.Diplomatic, countryId = state.playerCountryId, text = "Built coalition.", publicity = Publicity.Public });
            int sequence = state.actionSequence;

            var identities = HistoricalIdentitySystem.Build(state);

            Assert.IsTrue(identities.Exists(i => i.name == "BROKER STATE"));
            Assert.AreEqual(sequence, state.actionSequence);
        }

        [Test]
        public void IdentityDoesNotCountOtherCountriesRecord()
        {
            var state = WorldFactory.CreateDebugWorld(972);
            var other = state.countries.Find(c => c.id != state.playerCountryId);
            Assert.IsNotNull(other);
            state.chronicle.Add(new ChronicleEntry { date = state.date, category = ChronicleCategory.Military, countryId = other.id, text = "Foreign war.", publicity = Publicity.Public });
            state.chronicle.Add(new ChronicleEntry { date = state.date, category = ChronicleCategory.Military, countryId = other.id, text = "Foreign war again.", publicity = Publicity.Public });

            var identities = HistoricalIdentitySystem.Build(state);
            Assert.IsFalse(identities.Exists(i => i.name == "SECURITY STATE" && i.evidence == 2));
        }
    }
}
