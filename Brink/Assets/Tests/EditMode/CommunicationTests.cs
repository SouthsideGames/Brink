using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Telling the player what is going on (GDD §28, §29).
    ///
    /// Three channels, and the whole design rests on them staying distinct:
    /// the **wire** says what happened, your **ministries** say what it means for
    /// us, and **intelligence** says what is underneath. A wire that reported
    /// everything would quietly destroy the third — collection would tell the
    /// player nothing the news had not already given them for free.
    /// </summary>
    public class CommunicationTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 5511);
            turns = new TurnManager(state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void EveryPersistedStateClassIsSerializable()
        {
            // Inserting a type between `[Serializable]` and the class it was
            // meant to decorate silently detaches the attribute, and JsonUtility
            // then writes nothing for it. The chronicle vanished from every save
            // that way — no error, no warning, just an empty list on load.
            //
            // Reflect over the real save graph rather than trusting a reading of
            // the file, because the whole failure mode is that it *looks* right.
            var missing = new System.Collections.Generic.List<string>();
            var assembly = typeof(GameState).Assembly;

            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsClass || type.Namespace != "Brink.Data") continue;
                if (type.IsAbstract || type.IsGenericType) continue;
                if (type.Name.EndsWith("Factory") || type.Name.EndsWith("Catalog")) continue;
                if (type.IsDefined(typeof(System.SerializableAttribute), false)) continue;

                // Only flag types the save graph actually reaches.
                foreach (var field in typeof(GameState).GetFields())
                {
                    if (field.FieldType == type
                        || (field.FieldType.IsGenericType
                            && field.FieldType.GetGenericArguments()[0] == type))
                    {
                        missing.Add(type.Name);
                        break;
                    }
                }
            }

            Assert.IsEmpty(missing,
                "Reachable from GameState but not [Serializable] — these would silently " +
                "vanish from every save: " + string.Join(", ", missing));
        }

        [Test]
        public void TheChronicleSurvivesASave()
        {
            state.AddChronicle(ChronicleCategory.Diplomatic, "CHN", "It happened.", Publicity.Public);
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.IsNotEmpty(loaded.chronicle, "The world's history must survive a save.");
            Assert.AreEqual(Publicity.Public, loaded.chronicle[loaded.chronicle.Count - 1].publicity,
                "Publicity has to persist, or the wire re-reads the world as secret on load.");
        }

        [Test]
        public void AVersionOneSaveWalksAllTheWayForward()
        {
            // The path a save made before any of this session's work actually
            // takes. Each step was covered on its own, but nothing asserted the
            // *combined* result — and a real save only ever runs the whole chain.
            var legacy = WorldFactory.CreateDebugWorld(seed: 6060);
            legacy.legacyCabinet.AddRange(legacy.PlayerCountry.cabinet);
            foreach (var country in legacy.countries)
            {
                country.cabinet.Clear();   // v1: cabinets lived on GameState
                country.vacancies.Clear();
            }
            legacy.saveVersion = 1;

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(legacy));

            Assert.AreEqual(SaveSystem.CurrentSaveVersion, loaded.saveVersion);
            Assert.IsEmpty(loaded.legacyCabinet);

            foreach (var country in loaded.countries)
            {
                Assert.AreEqual(5, country.cabinet.Count,
                    $"{country.id} came out of the chain without a cabinet.");
                foreach (var official in country.cabinet)
                    Assert.Greater(official.age, 0f,
                        $"{country.id} came out of the chain with un-aged officials — " +
                        "the second step did not run on the first step's output.");
            }

            Assert.DoesNotThrow(() => SaveMigration.Validate(loaded));
        }

        // ---------- the handover out of orientation ----------

        [Test]
        public void FinishingOrientationPointsAtTheTwoPanelsThatOrientYou()
        {
            TutorialSystem.Begin(state);

            // Sit on the last step and satisfy it. Setting the index past the end
            // instead makes `Evaluate` return early on a null step, so the
            // handover never fires and the test passes for the wrong reason.
            state.tutorial.stepIndex = TutorialSystem.Steps.Count - 1;
            TutorialSystem.Evaluate(state);

            Assert.IsTrue(state.tutorial.completed, "Orientation did not actually finish.");
            Assert.IsTrue(HandoverNames("ACTIONS") && HandoverNames("BRIEFING"),
                "Orientation used to end in silence, leaving the operator in front of " +
                "twelve panels with no answer to \"what now?\".");
        }

        [Test]
        public void SkippingOrientationStillHandsOver()
        {
            // Whoever skips is the *most* likely to be stranded, not the least.
            TutorialSystem.Begin(state);
            TutorialSystem.Skip(state);

            Assert.IsTrue(HandoverNames("ACTIONS"),
                "Skipping used to leave the operator with nothing at all.");
        }

        bool HandoverNames(string panel)
        {
            foreach (var notification in state.notifications)
                if (notification.title.Contains("POST") && notification.body.Contains(panel))
                    return true;
            return false;
        }

        // ---------- an offer the player can actually take ----------

        [Test]
        public void WhenTheyWouldSettleThereIsSomethingToAccept()
        {
            // The reported bug: the game said "they are prepared to negotiate"
            // and offered no way to agree. Telling the operator an opportunity
            // exists while the interface cannot act on it reads as a missing
            // button, not as a deep negotiation system.
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            // Grind them down until they want out.
            confrontation.defenderWarExhaustion = 95f;
            confrontation.momentum = 60f;
            var opponent = state.FindCountry("CHN");
            opponent.warSupport = 5f;

            Assume.That(ConfrontationSystem.OpponentWouldAccept(state, confrontation),
                Is.True, "Test needs an opponent who wants terms.");

            var deal = PeaceSystem.BestAcceptableProposal(state, confrontation, state.playerCountryId);

            Assert.IsNotNull(deal,
                "If they would accept terms, some concrete set of terms must exist to put to them.");
            Assert.IsNotEmpty(deal.terms);
        }

        [Test]
        public void TheOfferedDealIsOneTheyActuallyTake()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "IND",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);
            confrontation.defenderWarExhaustion = 95f;
            confrontation.momentum = 60f;
            state.FindCountry("IND").warSupport = 5f;

            var deal = PeaceSystem.BestAcceptableProposal(state, confrontation, state.playerCountryId);
            Assume.That(deal, Is.Not.Null);

            Assert.IsTrue(
                PeaceSystem.ProposeTerms(state, confrontation, state.playerCountryId, deal),
                "A button labelled ACCEPT that gets refused is worse than no button at all.");
            Assert.IsTrue(confrontation.resolved, "Accepting terms has to actually end the war.");
        }

        [Test]
        public void NoDealIsOfferedWhenThereIsNoneToBeHad()
        {
            // Fresh war, nobody hurting: honestly report that there is nothing
            // to sign rather than inventing terms that will be refused.
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "RUS",
                ConfrontationObjective.TerritorialConcession, "RUS_CAP", PrimaryStrategy.Military);

            var opponent = state.FindCountry("RUS");
            opponent.warSupport = 100f;
            confrontation.defenderWarExhaustion = 0f;
            confrontation.momentum = -60f;

            Assert.IsNull(PeaceSystem.BestAcceptableProposal(state, confrontation, state.playerCountryId),
                "With the other side unhurt and winning, there is no deal to offer.");
        }

        // ---------- the fog rule, at the point of use ----------

        [Test]
        public void ForeignSecretsAreNotReadableFromTheChronicle()
        {
            // The rule was tested on `WorldWire` and simply not applied by the
            // two screens that read the chronicle directly. A guard that exists
            // and is bypassed is worse than one that does not exist, because
            // everyone assumes it is working.
            state.AddChronicle(ChronicleCategory.Intelligence, "CHN",
                "Covert operation against a third party.", Publicity.Public);
            state.AddChronicle(ChronicleCategory.Military, "RUS",
                "A programme nobody has announced.");

            foreach (var entry in state.chronicle)
            {
                if (entry.countryId == state.playerCountryId) continue;
                if (!WorldWire.CanShow(state, entry)) continue;

                Assert.AreNotEqual(ChronicleCategory.Intelligence, entry.category,
                    "Another country's intelligence work must never be readable without collection.");
                Assert.AreEqual(Publicity.Public, entry.publicity,
                    "A secret foreign entry reached a surface the player can read.");
            }
        }

        [Test]
        public void OurOwnRecordIsAlwaysLegibleToUs()
        {
            state.AddChronicle(ChronicleCategory.Intelligence, state.playerCountryId,
                "Our own covert operation.");

            var ours = state.chronicle[state.chronicle.Count - 1];
            Assert.IsTrue(WorldWire.CanShow(state, ours),
                "A government knows what it did. The fog is about other people.");
        }

        [Test]
        public void ASecretForeignEntryIsHiddenEvenInItsOwnCategory()
        {
            state.AddChronicle(ChronicleCategory.Military, "IND", "A quiet mobilisation.");
            var entry = state.chronicle[state.chronicle.Count - 1];

            Assert.IsFalse(WorldWire.CanShow(state, entry),
                "Military is a carried category, but this entry was never made public.");
        }

        // ---------- the fog rule ----------

        [Test]
        public void TheWireNeverCarriesIntelligence()
        {
            // Covert work is exactly what the fog protects. If it reached the
            // news, running networks would buy the player nothing.
            state.AddChronicle(ChronicleCategory.Intelligence, "CHN",
                "A network was rolled up.", Publicity.Public);

            foreach (var item in WorldWire.ForMonth(state, state.date))
                Assert.AreNotEqual(ChronicleCategory.Intelligence, item.category,
                    "Intelligence must never reach the wire, even if something marks it public.");
        }

        [Test]
        public void SecretEventsStayOffTheWire()
        {
            state.AddChronicle(ChronicleCategory.Military, "RUS", "A quiet preparation.");
            Assert.IsEmpty(WorldWire.ForMonth(state, state.date),
                "An unclassified entry defaults to secret and must not leak.");
        }

        [Test]
        public void TheDefaultIsSecret()
        {
            // The safe default matters more than convenience here: a missing wire
            // item is a visible content gap someone fixes, a leak silently guts
            // the intelligence pillar and looks like the game working.
            state.AddChronicle(ChronicleCategory.Diplomatic, "IND", "Something happened.");
            var entry = state.chronicle[state.chronicle.Count - 1];

            Assert.AreEqual(Publicity.Secret, entry.publicity,
                "A chronicle entry nobody classified must fail closed.");
        }

        [Test]
        public void PublicEventsDoReachTheWire()
        {
            state.AddChronicle(ChronicleCategory.Diplomatic, "CHN",
                "A defence pact was signed.", Publicity.Public);

            var wire = WorldWire.ForMonth(state, state.date);
            Assert.AreEqual(1, wire.Count);
            StringAssert.Contains("defence pact", wire[0].headline);
        }

        [Test]
        public void TheWireNamesTheCountryItCameFrom()
        {
            state.AddChronicle(ChronicleCategory.Political, "JPN",
                "An election was held.", Publicity.Public);

            var wire = WorldWire.ForMonth(state, state.date);
            StringAssert.Contains("Japan".ToUpperInvariant(), WorldWire.Format(state, wire[0]));
        }

        [Test]
        public void TheWireIsNotFilteredByCabinetCompetence()
        {
            // This is public news, not our ministry's paperwork. A weak minister
            // costs us our own reporting, never the ability to hear that a war
            // has started.
            foreach (var official in state.cabinet)
            {
                official.competence = 0f;
                official.mode = ControlMode.Autonomous;
            }
            state.AddChronicle(ChronicleCategory.Military, "RUS",
                "War has broken out.", Publicity.Public);

            Assert.AreEqual(1, WorldWire.ForMonth(state, state.date).Count);
        }

        [Test]
        public void OurOwnAffairsAreMarkedAsSuch()
        {
            state.AddChronicle(ChronicleCategory.Military, state.playerCountryId,
                "We mobilized.", Publicity.Public);

            var wire = WorldWire.ForMonth(state, state.date);
            Assert.IsTrue(wire[0].involvesUs);
        }

        [Test]
        public void RealPlayProducesWorldNews()
        {
            // The point of all this: the simulation was already running sixteen
            // countries through elections, coups and wars, and almost none of it
            // reached the operator.
            SimulationPipeline.Wire(turns, state);
            int carried = 0;

            for (int i = 0; i < 120; i++)
            {
                turns.EndMonth();
                carried += WorldWire.LastMonth(state).Count;
            }

            Assert.Greater(carried, 0,
                "A decade of world history must produce something the player can read.");
        }

        // ---------- directives: what is worth doing ----------

        [Test]
        public void DirectivesAreDerivedFromRealConditions()
        {
            var player = state.PlayerCountry;
            player.resources.energy = 15f;

            bool raised = false;
            foreach (var directive in DirectiveSystem.Collect(state))
                if (directive.id == "ENERGY") raised = true;

            Assert.IsTrue(raised, "A real vulnerability should raise the matching directive.");
        }

        [Test]
        public void AHealthyCountryIsAdvisedOfNothingUrgent()
        {
            var player = state.PlayerCountry;
            player.resources.energy = 90f;
            player.resources.strategicMaterials = 90f;
            player.military.ground.readiness = 90f;
            player.stability = 80f;
            player.governmentApproval = 70f;
            player.technology.programs.Add(new ResearchProgram());
            state.treaties.Add(new Treaty { countryA = player.id, countryB = "DEU" });
            state.networks.Add(new IntelNetwork { ownerId = player.id, targetId = "RUS", penetration = 40f });

            Assert.IsEmpty(DirectiveSystem.Collect(state),
                "A directive the world did not earn is a chore. Nothing wrong, nothing advised.");
        }

        [Test]
        public void DirectivesAreNeverATaskQueue()
        {
            // Break everything at once; the Cabinet still advises on a few things.
            var player = state.PlayerCountry;
            player.resources.energy = 5f;
            player.resources.strategicMaterials = 5f;
            player.military.ground.readiness = 5f;
            player.stability = 5f;
            player.governmentApproval = 5f;

            Assert.LessOrEqual(DirectiveSystem.Collect(state).Count, DirectiveSystem.MaxShown,
                "A list of fifteen objectives is a task queue, not advice.");
        }

        [Test]
        public void EveryDirectiveNamesAPanelAndAConcreteAction()
        {
            var player = state.PlayerCountry;
            player.resources.energy = 10f;
            player.stability = 20f;

            foreach (var directive in DirectiveSystem.Collect(state))
            {
                Assert.IsNotEmpty(directive.viewId, "Advice with no panel sends the player hunting.");
                Assert.IsNotEmpty(directive.suggestion, "\"Improve the economy\" is not advice.");
                Assert.IsNotEmpty(directive.rationale);
            }
        }

        // ---------- the action index: what is possible ----------

        [Test]
        public void TheIndexCoversEveryPillar()
        {
            foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
                Assert.IsNotEmpty(ActionCatalog.ForPillar(state, pillar),
                    $"{pillar} has no listed actions — the operator would think it has none.");
        }

        [Test]
        public void UnavailableActionsAreShownWithAReason()
        {
            // Hiding a verb the operator cannot use today teaches them it does
            // not exist. Showing it with the rule teaches them the game.
            bool sawBlocked = false;
            foreach (var entry in ActionCatalog.All(state))
            {
                if (entry.available) continue;
                sawBlocked = true;
                Assert.IsNotEmpty(entry.blockedReason,
                    $"'{entry.label}' is unavailable with no explanation.");
            }

            Assert.IsTrue(sawBlocked,
                "At the start of a game some actions should be genuinely out of reach.");
        }

        [Test]
        public void EveryActionNamesItsPanelAndCost()
        {
            foreach (var entry in ActionCatalog.All(state))
            {
                Assert.IsNotEmpty(entry.viewId, $"'{entry.label}' does not say where to do it.");
                Assert.IsNotEmpty(entry.cost, $"'{entry.label}' does not say what it costs.");
                Assert.IsNotEmpty(entry.description);
            }
        }

        [Test]
        public void OperationsBecomeAvailableOnceAtWar()
        {
            bool BlockedNow()
            {
                foreach (var entry in ActionCatalog.All(state))
                    if (entry.label == "Launch an operation") return !entry.available;
                return false;
            }

            Assert.IsTrue(BlockedNow(), "With no war on, operations are out of reach.");

            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            Assert.IsFalse(BlockedNow(), "The index has to track the real world, not a static list.");
        }

        [Test]
        public void TheIndexIsSafeBeforeAGameStarts()
        {
            Assert.IsEmpty(ActionCatalog.All(null));
            Assert.IsEmpty(DirectiveSystem.Collect(null));
            Assert.IsEmpty(WorldWire.ForMonth(null, new GameDate(1984, 1)));
        }
    }
}
