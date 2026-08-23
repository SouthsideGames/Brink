using Brink.Core;
using Brink.Data;
using Brink.UI;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The terminal is a character grid under `white-space: pre`, so nothing
    /// wraps and anything wider than the panel simply runs off the screen. These
    /// assert that the grid is built to the measured width — the bug reported
    /// from a folding phone, where views hardcoded 64–78 columns.
    /// </summary>
    public class MapAndLayoutTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 3131);
        }

        [TearDown]
        public void TearDown()
        {
            TerminalMetrics.ResetForTests();
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static int WidestLine(string block)
        {
            int widest = 0;
            foreach (var line in block.Replace("\r", "").Split('\n'))
                if (line.Length > widest) widest = line.Length;
            return widest;
        }

        // ---------- panel scale ----------

        /// <summary>
        /// Every one of these is a resolution the shell must not render
        /// unreadably small at. The regression that prompted them: the panel used
        /// ConstantPhysicalSize with a 96 DPI fallback, so any platform that did
        /// not report an honest DPI got scale 1 — 13-pixel text on a 2300-pixel
        /// screen.
        /// </summary>
        static readonly (string name, int w, int h)[] Screens =
        {
            ("fold cover", 2316, 904),
            ("fold open", 2176, 1812),
            ("phone landscape", 2400, 1080),
            ("small phone", 1280, 720),
            ("tablet", 2560, 1600),
            ("desktop", 1920, 1080)
        };

        [Test]
        public void TextIsNeverRenderedAtOnePixelPerPoint()
        {
            foreach (var screen in Screens)
            {
                float scale = TerminalScale.ScaleFor(screen.w, screen.h);
                float fontPx = TerminalScale.BaseFontPx * scale;

                Assert.Greater(fontPx, 20f,
                    $"{screen.name} ({screen.w}x{screen.h}): {fontPx:F0}px text is unreadable. "
                    + "This is the DPI-fallback bug.");
            }
        }

        [Test]
        public void EveryScreen_GetsAReadableNumberOfColumns()
        {
            foreach (var screen in Screens)
            {
                float scale = TerminalScale.ScaleFor(screen.w, screen.h);
                float columns = screen.w / (TerminalScale.BaseCharPx * scale);

                Assert.GreaterOrEqual(columns, 40f,
                    $"{screen.name}: only {columns:F0} columns — too cramped to lay out a terminal.");
                Assert.LessOrEqual(columns, 110f,
                    $"{screen.name}: {columns:F0} columns means text is too small to read.");
            }
        }

        [Test]
        public void ABiggerScreen_BuysMoreColumnsNotBiggerText()
        {
            int small = TerminalScale.TargetColumns(1280, 720);
            int large = TerminalScale.TargetColumns(2560, 1600);

            Assert.Less(small, large,
                "GDD §4: a larger display shows more information, never larger UI.");
        }

        [Test]
        public void AWideShortPanel_TradesWidthForVerticalLines()
        {
            // Same width, different heights: the cover screen is much shorter.
            int tall = TerminalScale.TargetColumns(2316, 1600);
            int coverScreen = TerminalScale.TargetColumns(2316, 904);

            Assert.Greater(coverScreen, tall,
                "A short panel should ask for more columns — smaller text — so that "
                + "more lines fit in the height it does have.");
        }

        [Test]
        public void ScaleIsAlwaysSane_EvenForAbsurdInput()
        {
            foreach (var (w, h) in new[] { (0, 0), (1, 1), (100, 40), (20000, 8000) })
            {
                float scale = TerminalScale.ScaleFor(w, h);
                Assert.GreaterOrEqual(scale, TerminalScale.MinScale);
                Assert.LessOrEqual(scale, TerminalScale.MaxScale);
                Assert.IsFalse(float.IsNaN(scale));
            }
        }

        // ---------- metrics ----------

        [Test]
        public void ColumnsFollowTheMeasuredPanel_NotAFixedAssumption()
        {
            TerminalMetrics.Update(contentWidthPt: 300f, charWidthPt: 7f, heightPt: 400f, size: SizeClass.Compact);
            int narrow = TerminalMetrics.Columns;

            TerminalMetrics.Update(contentWidthPt: 1200f, charWidthPt: 7f, heightPt: 800f, size: SizeClass.Large);
            int wide = TerminalMetrics.Columns;

            Assert.Less(narrow, wide, "A bigger panel must yield more columns, not bigger text (GDD §4).");
            Assert.GreaterOrEqual(narrow, TerminalMetrics.MinColumns);
            Assert.LessOrEqual(wide, TerminalMetrics.MaxColumns);
        }

        [Test]
        public void AVeryNarrowPanel_StillGetsAUsableGrid()
        {
            TerminalMetrics.Update(60f, 7f, 400f, SizeClass.Compact);
            Assert.AreEqual(TerminalMetrics.MinColumns, TerminalMetrics.Columns,
                "Below the floor we clamp rather than emit a one-character terminal.");
        }

        [Test]
        public void AShortPanel_IsRecognisedAsShort()
        {
            TerminalMetrics.Update(900f, 7f, 700f, SizeClass.Medium);
            Assert.IsFalse(TerminalMetrics.ShortScreen);

            // A folding phone's cover display: wide, and very short.
            TerminalMetrics.Update(900f, 7f, 210f, SizeClass.Compact);
            Assert.IsTrue(TerminalMetrics.ShortScreen,
                "Height is the scarce resource on a cover screen and the shell must know it.");
            Assert.Less(TerminalMetrics.MapRows, 17,
                "A short screen cannot afford a full-height map.");
        }

        [Test]
        public void MetricsRaiseChanged_SoOpenViewsRebuild()
        {
            int raised = 0;
            void Handler() => raised++;

            TerminalMetrics.Changed += Handler;
            try
            {
                TerminalMetrics.Update(800f, 7f, 500f, SizeClass.Medium);
                TerminalMetrics.Update(800f, 7f, 500f, SizeClass.Medium); // identical: no event
                TerminalMetrics.Update(400f, 7f, 500f, SizeClass.Compact);
            }
            finally
            {
                TerminalMetrics.Changed -= Handler;
            }

            Assert.AreEqual(2, raised, "Only a real change should force a rebuild.");
        }

        // ---------- the wrapping policy ----------

        /// <summary>
        /// Every readout class the shell must hard-wrap.
        ///
        /// The policy tested only `terminal-text`, which silently excluded
        /// `terminal-text-dim` and `terminal-text-bright` — so a label built with
        /// only a dim class ran straight off the right edge of the device. The
        /// trap is that *dim* is exactly what secondary explanation uses, and
        /// secondary explanation is the longest prose in the game: the briefing's
        /// attention note and the Cabinet's rationale lines both overflowed.
        /// </summary>
        [Test]
        public void EveryReadoutClassIsWrapped()
        {
            foreach (var ussClass in new[] { "terminal-text", "terminal-text-dim", "terminal-text-bright" })
            {
                var label = new UnityEngine.UIElements.Label("x");
                label.AddToClassList(ussClass);

                Assert.IsTrue(TerminalShellController.IsReadout(label),
                    $"Prose carrying '{ussClass}' is never wrapped and will run off the screen.");
            }
        }

        [Test]
        public void NonProseIsLeftAlone()
        {
            var button = new UnityEngine.UIElements.Label("PRESS");
            button.AddToClassList("cmd-button");

            Assert.IsFalse(TerminalShellController.IsReadout(button),
                "Controls and figures are built to an exact grid; wrapping corrupts them.");
        }

        [Test]
        public void SecondaryExplanationActuallyWraps()
        {
            // End to end on the real text that overflowed on device.
            const string note =
                "The month can still be ended. Anything left unanswered is recorded as this " +
                "office having failed to decide.";

            foreach (int columns in new[] { 42, 50, 58, 66, 76 })
                Assert.LessOrEqual(WidestLine(AsciiChart.WrapBlock(note, columns)), columns,
                    $"Wrapped to {columns} columns and still overflowed.");
        }

        // ---------- the status bar fits ----------

        /// <summary>
        /// The bar carries the banner, a display toggle, the date, three resource
        /// readouts and END MONTH, all on one row. Reported from device: on a
        /// landscape phone it ran off the right edge, taking the turn control
        /// with it.
        ///
        /// The banner is the only flavour on that row, so it is the thing that
        /// gives way. This budgets it in characters against the columns each
        /// screen actually has.
        /// </summary>
        [Test]
        public void TheBannerLeavesRoomForTheInstrument()
        {
            // "SEP 1984" + "CP 6  INF 6  PC 20" + "END MONTH (1)" + DSP, with
            // separators. Measured from the real strings, rounded up.
            const int ReadoutColumns = 52;

            foreach (var screen in Screens)
            {
                int columns = TerminalScale.TargetColumns(screen.w, screen.h);
                var size = Breakpoints.FromColumns(columns);
                string banner = TerminalShellController.BannerFor(size, awaiting: false);

                Assert.LessOrEqual(banner.Length + 1 + ReadoutColumns, columns + 12,
                    $"{screen.name}: banner '{banner}' ({banner.Length} cols) plus the " +
                    $"readouts does not fit {columns} columns — the bar will overflow.");
            }
        }

        [Test]
        public void ANarrowBarDropsTheBannerEntirely()
        {
            Assert.IsEmpty(TerminalShellController.BannerFor(SizeClass.Compact, awaiting: false),
                "On a phone the readouts need every column; flavour text is not worth one.");
            Assert.IsNotEmpty(TerminalShellController.BannerFor(SizeClass.Large, awaiting: false),
                "A tablet has room for the full banner and should show it.");
        }

        [Test]
        public void TheBannerShortensRatherThanDisappearingInOneStep()
        {
            int large = TerminalShellController.BannerFor(SizeClass.Large, awaiting: false).Length;
            int medium = TerminalShellController.BannerFor(SizeClass.Medium, awaiting: false).Length;
            int compact = TerminalShellController.BannerFor(SizeClass.Compact, awaiting: false).Length;

            Assert.Greater(large, medium, "A foldable should get a shorter banner, not the full one.");
            Assert.Greater(medium, compact, "A phone should get less again.");
        }

        // ---------- enum names never reach the player ----------

        [Test]
        public void EnumNamesAreReadAsEnglish()
        {
            // Reported from device: "Confrontation active — TotalWar". Everything
            // around it is written English and one word is a variable name.
            Assert.AreEqual("Total War", Phrase.Of(EscalationState.TotalWar));
            Assert.AreEqual("Limited Conflict", Phrase.Of(EscalationState.LimitedConflict));
            Assert.AreEqual("Territorial Concession", Phrase.Of(ConfrontationObjective.TerritorialConcession));
            Assert.AreEqual("MUTUAL DEFENSE", Phrase.Caps(TreatyCommitment.MutualDefense));
        }

        [Test]
        public void SingleWordNamesAreLeftAlone()
        {
            Assert.AreEqual("Crisis", Phrase.Of(EscalationState.Crisis));
            Assert.AreEqual("Severe", Phrase.Of(SanctionSeverity.Severe));
        }

        [Test]
        public void AcronymsSurviveIntact()
        {
            // A naive split turns "ISRLift" into "I S R Lift".
            Assert.AreEqual("Intelligence Political", Phrase.Of(PrimaryStrategy.IntelligencePolitical));
            Assert.IsFalse(Phrase.Of(EndgameType.StateDestabilization).Contains("  "),
                "Splitting must never leave a double space.");
        }

        [Test]
        public void NoPlayerFacingTextInterpolatesARawMultiWordEnum()
        {
            // A guard on the class of bug rather than the one instance. Anything
            // player-facing that prints one of these values must run it through
            // Phrase, so the reading is English rather than code.
            foreach (EscalationState value in System.Enum.GetValues(typeof(EscalationState)))
            {
                string rendered = Phrase.Of(value);
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(rendered, "[a-z][A-Z]"),
                    $"'{rendered}' still reads as a variable name.");
            }
        }

        // ---------- affordability ----------

        /// <summary>
        /// Cost tags are read back off the label, so the parser has to be exact —
        /// a false positive disables a command the operator can afford.
        /// </summary>
        [Test]
        public void CostTags_AreParsedFromButtonLabels()
        {
            Assert.IsTrue(Brink.UI.Views.TerminalView.TryReadCost("PREPARE [2 CP]", out int cp, out string cpRes));
            Assert.AreEqual(2, cp);
            Assert.AreEqual("CP", cpRes);

            Assert.IsTrue(Brink.UI.Views.TerminalView.TryReadCost("DIRECTED [1 INF]", out int inf, out string infRes));
            Assert.AreEqual(1, inf);
            Assert.AreEqual("INF", infRes);

            Assert.IsTrue(Brink.UI.Views.TerminalView.TryReadCost("REFORM [6 PC]", out int pc, out string pcRes));
            Assert.AreEqual(6, pc);
            Assert.AreEqual("PC", pcRes);
        }

        [Test]
        public void LabelsWithoutACost_AreNeverGated()
        {
            foreach (string label in new[]
                     {
                         "AUTONOMOUS", "◄ WORLD MAP", "► UNITED STATES", "END MONTH",
                         "[X] MUTUALDEFENSE", "OPEN US ►", "BREAK TREATY"
                     })
                Assert.IsFalse(Brink.UI.Views.TerminalView.TryReadCost(label, out _, out _),
                    $"'{label}' has no cost and must never be disabled for affordability.");
        }

        // ---------- "the month is spent" ----------

        [Test]
        public void TheMonthReadsAsSpent_OnlyWhenCommandCapacityIsGone()
        {
            state.commandPoints.current = 3;
            Assert.IsFalse(state.CommandCapacitySpent, "There is still capacity to spend.");

            state.commandPoints.current = 0;
            Assert.IsTrue(state.CommandCapacitySpent,
                "With no Command Points left, every remaining decision is somebody "
                + "else's — the shell should say so rather than let the player hunt "
                + "for an action that no longer exists.");
        }

        [Test]
        public void ABlockingCrisis_SuppressesTheEndMonthSignal()
        {
            state.commandPoints.current = 0;
            state.activeCrises.Add(new ActiveCrisis
            {
                title = "TEST",
                body = ""
            });

            Assert.IsFalse(state.CommandCapacitySpent,
                "Pointing at END MONTH while a crisis refuses it is worse than "
                + "saying nothing; the crisis overlay is already the loudest thing "
                + "on screen.");
        }

        // ---------- size classes ----------

        /// <summary>
        /// Breakpoints are measured in characters across, not panel points. Once
        /// the scale is derived to hit a column target, panel width in points is
        /// roughly constant on every device — so a points-based `Large` threshold
        /// was unreachable and both wider classes were dead code.
        /// </summary>
        [Test]
        public void EverySizeClass_IsActuallyReachable()
        {
            var seen = new System.Collections.Generic.HashSet<SizeClass>();
            for (int columns = TerminalMetrics.MinColumns; columns <= TerminalMetrics.MaxColumns; columns++)
                seen.Add(Breakpoints.FromColumns(columns));

            foreach (SizeClass size in System.Enum.GetValues(typeof(SizeClass)))
                Assert.IsTrue(seen.Contains(size),
                    $"{size} cannot be reached at any column count the shell produces — "
                    + "its layout rules are dead.");
        }

        [Test]
        public void MoreColumns_NeverMeansASmallerSizeClass()
        {
            var previous = Breakpoints.FromColumns(TerminalMetrics.MinColumns);
            for (int columns = TerminalMetrics.MinColumns; columns <= TerminalMetrics.MaxColumns; columns++)
            {
                var current = Breakpoints.FromColumns(columns);
                Assert.GreaterOrEqual((int)current, (int)previous, $"Regressed at {columns} columns.");
                previous = current;
            }
        }

        // ---------- rich text must not distort the width ----------

        [Test]
        public void MarkupDoesNotCountTowardTheLineWidth()
        {
            const string plain = "[FLASH   ] CRISIS PENDING";
            const string tagged = "<color=#FF6E64>[FLASH   ] CRISIS PENDING</color>";

            Assert.AreEqual(plain.Length, AsciiChart.VisibleLength(tagged),
                "A colour tag is 24 invisible characters. Counting them wrapped a "
                + "FLASH line two dozen characters early.");
            Assert.AreEqual(0, AsciiChart.VisibleLength("<b></b>"));
        }

        [Test]
        public void AMarkedUpLineIsLeftIntact_RatherThanSplitMidTag()
        {
            string tagged = "<color=#FF6E64>" + new string('X', 200) + "</color>";
            string wrapped = AsciiChart.WrapBlock(tagged, 40);

            Assert.AreEqual(tagged, wrapped,
                "Better to leave markup alone than to cut a tag in half; views "
                + "should use one label per coloured line instead.");
        }

        // ---------- the map fits ----------

        [Test]
        public void TheWorldMap_NeverExceedsTheColumnsItWasGiven()
        {
            foreach (int columns in new[] { 34, 48, 64, 80, 100 })
            {
                string map = AsciiWorldMap.Render(state, "CHN", columns, 14);
                Assert.LessOrEqual(WidestLine(map), columns,
                    $"The map overflowed a {columns}-column panel — this is exactly what "
                    + "ran off the edge of a real phone.");
            }
        }

        [Test]
        public void TheWorldMap_HonoursItsRowBudget()
        {
            string map = AsciiWorldMap.Render(state, null, 60, 9);
            Assert.AreEqual(9, map.Split('\n').Length);
        }

        [Test]
        public void EveryCountryStillAppears_WhenTheMapIsScaledDown()
        {
            string map = AsciiWorldMap.Render(state, null, 40, 11);

            int found = 0;
            foreach (var profile in WorldFactory.Profiles)
            {
                string code = profile.mapCode;
                if (!string.IsNullOrEmpty(code) && map.Contains(code)) found++;
            }

            Assert.Greater(found, WorldFactory.Profiles.Length / 2,
                "Scaling the chart down must not drop most of the world off it.");
        }

        [Test]
        public void TheCountryChart_NeverExceedsItsColumns()
        {
            foreach (int columns in new[] { 34, 50, 72, 96 })
            {
                string chart = AsciiCountryMap.Render(state, "CHN", columns, 10);
                Assert.LessOrEqual(WidestLine(chart), columns);
            }
        }

        // ---------- the zoom respects the fog ----------

        [Test]
        public void WithNoCollection_ACountrysInteriorIsNotVisible()
        {
            Assert.AreEqual(AsciiCountryMap.DetailLevel.Public,
                AsciiCountryMap.LevelFor(state, "CHN"));

            string sites = AsciiCountryMap.DescribeSites(state, "CHN");
            StringAssert.Contains("does not extend inside", sites);

            // Their industry must not be named to someone who has never looked.
            foreach (var location in state.locations)
            {
                if (location.ownerId != "CHN") continue;
                if (location.type == LocationType.Capital) continue;
                StringAssert.DoesNotContain(location.displayName, sites);
            }
        }

        [Test]
        public void CollectionOpensTheInterior_ByDegrees()
        {
            var network = new IntelNetwork
            {
                ownerId = state.playerCountryId,
                targetId = "CHN",
                focus = IntelDomain.Military,
                penetration = 25f
            };
            state.networks.Add(network);
            Assert.AreEqual(AsciiCountryMap.DetailLevel.Sites,
                AsciiCountryMap.LevelFor(state, "CHN"),
                "A shallow network should name the sites and no more.");

            network.penetration = 80f;
            Assert.AreEqual(AsciiCountryMap.DetailLevel.Detailed,
                AsciiCountryMap.LevelFor(state, "CHN"),
                "Deep penetration should report condition too.");
        }

        [Test]
        public void OurOwnCountry_IsAlwaysComplete()
        {
            Assert.AreEqual(AsciiCountryMap.DetailLevel.Complete,
                AsciiCountryMap.LevelFor(state, state.playerCountryId));
        }

        [Test]
        public void AForeignBase_IsOnlyVisibleWithGoodCollection()
        {
            StrategicLocation host = null;
            foreach (var location in state.locations)
                if (location.ownerId == "CHN" && location.SupportsBasing) { host = location; break; }
            Assume.That(host, Is.Not.Null, "Precondition: China has a location that can host basing.");

            host.foreignOperatorId = "RUS";

            // No collection: we cannot see it.
            StringAssert.DoesNotContain("FOREIGN FORCE",
                AsciiCountryMap.DescribeSites(state, "CHN"));

            state.networks.Add(new IntelNetwork
            {
                ownerId = state.playerCountryId,
                targetId = "CHN",
                focus = IntelDomain.Military,
                penetration = 80f
            });

            StringAssert.Contains("FOREIGN FORCE",
                AsciiCountryMap.DescribeSites(state, "CHN"),
                "Knowing who operates from a rival's soil is what collection is for.");
        }

        // ---------- basing rights ----------

        [Test]
        public void ATransitTreaty_GrantsBasingAndLosingItTakesThemBack()
        {
            StrategicLocation host = null;
            foreach (var location in state.locations)
                if (location.ownerId == "IND" && location.SupportsBasing) { host = location; break; }
            Assume.That(host, Is.Not.Null);

            var relationship = state.FindRelationship("IND", state.playerCountryId);
            relationship.relations = 80f;
            relationship.SetThreatPerceivedBy("IND", 10f);

            state.treaties.Add(new Treaty
            {
                id = "T_IND_USA",
                countryA = "IND",
                countryB = state.playerCountryId,
                commitments = new System.Collections.Generic.List<TreatyCommitment>
                {
                    TreatyCommitment.Transit
                }
            });

            DiplomacySystem.MonthlyUpdate(state);
            Assert.AreEqual(state.playerCountryId, host.foreignOperatorId,
                "A transit commitment is permission to operate from a partner's soil.");

            // The relationship sours: nobody hosts a force they have come to fear.
            relationship.relations = 10f;
            DiplomacySystem.MonthlyUpdate(state);
            Assert.IsFalse(host.HasForeignBase,
                "The host keeps sovereignty and takes the rights back.");
        }

        [Test]
        public void OccupiedGround_IsHeldNotHosted()
        {
            StrategicLocation location = null;
            foreach (var candidate in state.locations)
                if (candidate.SupportsBasing) { location = candidate; break; }
            Assume.That(location, Is.Not.Null);

            location.foreignOperatorId = "RUS";
            location.ownerId = "CHN"; // taken from its original owner

            DiplomacySystem.MonthlyUpdate(state);
            Assert.IsFalse(location.HasForeignBase,
                "Basing is consensual; occupation is not. They are different facts.");
        }

        /// <summary>
        /// A partner's airbase is reach we did not have to conquer — that is the
        /// entire strategic point of a Transit commitment. Reading ownership
        /// alone meant basing rights bought nothing at all.
        /// </summary>
        [Test]
        public void OperatingFromAPartnersSoil_ActuallyExtendsOurReach()
        {
            StrategicLocation host = null;
            foreach (var location in state.locations)
                if (location.ownerId != state.playerCountryId && location.SupportsBasing)
                { host = location; break; }
            Assume.That(host, Is.Not.Null);

            float before = TerritorySystem.ProjectionSwing(state, state.playerCountryId);
            host.foreignOperatorId = state.playerCountryId;
            float after = TerritorySystem.ProjectionSwing(state, state.playerCountryId);

            Assert.Greater(after, before,
                "Basing rights must buy reach, or a Transit treaty is decoration.");
        }

        [Test]
        public void HostedReach_IsWorthLessThanGroundWeHold()
        {
            var location = state.FindLocation("CONTESTED_LANE");
            Assume.That(location, Is.Not.Null);

            // Measure what *this* hosting adds, not the running total. The world
            // now authors a hosting arrangement from month zero (Ramstein, spec
            // 08), so a player may already be a guest somewhere — comparing the
            // sum of every hosting against one location's value tested arithmetic
            // rather than the rule.
            float before = TerritorySystem.HostedProjection(state, state.playerCountryId);

            location.foreignOperatorId = state.playerCountryId;
            float added = TerritorySystem.HostedProjection(state, state.playerCountryId) - before;

            Assert.Greater(added, 0f);
            Assert.Less(added, location.strategicValue,
                "A host can withdraw the right, and does the moment relations sour — "
                + "so it cannot be worth as much as ground we hold outright.");
        }

        [Test]
        public void BasingRights_SurviveASave()
        {
            StrategicLocation host = null;
            foreach (var location in state.locations)
                if (location.SupportsBasing) { host = location; break; }
            Assume.That(host, Is.Not.Null);

            host.foreignOperatorId = "RUS";
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.AreEqual("RUS", loaded.FindLocation(host.id).foreignOperatorId);
        }
    }
}
