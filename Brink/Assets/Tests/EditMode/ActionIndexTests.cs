using System.Collections.Generic;
using System.Reflection;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The COMMAND INDEX has to stay complete (GDD §28.1).
    ///
    /// `ActionCatalog` exists because the game's own author, who designed every
    /// verb in it, reported forgetting what was possible. It was written once and
    /// then **ten operator verbs shipped past it** — industrial programmes, agent
    /// operations, accession efforts, equipment orders, war footing, the strategic
    /// pivot, directives, direct action and securing the army — every one wired
    /// into a view, every one invisible on the single screen whose entire job is
    /// to answer "what can I do".
    ///
    /// That is this codebase's "written but never read" bug wearing the opposite
    /// costume: read constantly, and quietly incomplete. A reference missing a
    /// third of its subject is worse than no reference, because it is trusted.
    ///
    /// `GameController` is the right thing to measure against: it is the boundary
    /// every view crosses to take an action, so it is the enumerable list of what
    /// the operator can actually do. Anything on it that is not an operator verb
    /// is named in `NotOperatorActions` below **with a reason** — that list is
    /// written-down debt in the `PipelineWiringTests.Grandfathered` sense and may
    /// only shrink.
    /// </summary>
    public class ActionIndexTests
    {
        GameState state;

        /// <summary>
        /// `GameController` members that are deliberately not in the index, and
        /// why. Session lifecycle, the turn itself, and the tutorial are not
        /// things an operator goes looking for in a reference — they are the
        /// frame the reference sits inside.
        /// </summary>
        static readonly Dictionary<string, string> NotOperatorActions = new Dictionary<string, string>
        {
            { "EnsureStarted", "Session lifecycle, not a decision." },
            { "NewGame", "Session lifecycle." },
            { "NewGameFromAssessment", "Session lifecycle." },
            { "ResetGame", "Session lifecycle; reached from Settings, deliberately." },
            { "EndMonth", "The turn itself. It is the one control that is never hidden." },
            { "SaveToSlot", "Autosave plumbing; there is no save UI by decision (GDD §30)." },
            { "RefreshTutorial", "Orientation, not an action." },
            { "SkipTutorial", "Orientation, not an action." },
            { "AcknowledgeTutorialStep", "Orientation, not an action." },
            { "ResolveCrisis", "A crisis arrives in front of the operator. Nobody needs an "
                             + "index entry to find a modal that is already open." },
        };

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 8801);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static List<MethodInfo> OperatorMethods()
        {
            var methods = new List<MethodInfo>();
            foreach (var method in typeof(GameController).GetMethods(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName) continue;           // property accessors
                if (method.DeclaringType != typeof(GameController)) continue;
                methods.Add(method);
            }
            return methods;
        }

        static HashSet<string> IndexedVerbs(GameState state)
        {
            var named = new HashSet<string>();
            foreach (var entry in ActionCatalog.All(state))
                foreach (var verb in entry.verbs)
                    named.Add(verb);
            return named;
        }

        [Test]
        public void EveryOperatorVerbAppearsInTheIndex()
        {
            var indexed = IndexedVerbs(state);
            var missing = new List<string>();

            foreach (var method in OperatorMethods())
            {
                if (NotOperatorActions.ContainsKey(method.Name)) continue;
                if (indexed.Contains(method.Name)) continue;
                if (missing.Contains(method.Name)) continue;
                missing.Add(method.Name);
            }

            Assert.IsEmpty(missing,
                "These operator verbs are reachable in the game and absent from the COMMAND "
                + "INDEX: " + string.Join(", ", missing) + ". Give each one an entry, or name it "
                + "in NotOperatorActions with a reason. A reference the player trusts and that "
                + "is missing a verb teaches them the verb does not exist.");
        }

        [Test]
        public void EveryIndexEntryNamesTheVerbItDocuments()
        {
            var unattributed = new List<string>();
            foreach (var entry in ActionCatalog.All(state))
                if (entry.verbs == null || entry.verbs.Length == 0)
                    unattributed.Add($"{entry.viewId}/{entry.label}");

            Assert.IsEmpty(unattributed,
                "Index entries with no verb behind them: " + string.Join(", ", unattributed)
                + ". Without one the completeness check cannot see them, which is how the "
                + "index went stale the first time.");
        }

        [Test]
        public void EveryNamedVerbIsARealControllerMethod()
        {
            var real = new HashSet<string>();
            foreach (var method in OperatorMethods()) real.Add(method.Name);

            var phantom = new List<string>();
            foreach (var entry in ActionCatalog.All(state))
                foreach (var verb in entry.verbs)
                    if (!real.Contains(verb) && !phantom.Contains(verb))
                        phantom.Add(verb);

            Assert.IsEmpty(phantom,
                "The index documents verbs that no longer exist: " + string.Join(", ", phantom));
        }

        [Test]
        public void TheExemptionListNamesOnlyThingsThatExist()
        {
            var real = new HashSet<string>();
            foreach (var method in OperatorMethods()) real.Add(method.Name);

            var stale = new List<string>();
            foreach (var name in NotOperatorActions.Keys)
                if (!real.Contains(name)) stale.Add(name);

            Assert.IsEmpty(stale,
                "NotOperatorActions still excuses methods that are gone: "
                + string.Join(", ", stale) + ". A debt list that outlives its debt stops "
                + "recording what needs fixing and becomes a list of permitted exceptions.");
        }

        [Test]
        public void EveryEntryPointsAtAPanelThatExists()
        {
            var panels = new HashSet<string>();
            foreach (var panel in Brink.UI.TerminalShellController.BuildPanels(true))
                panels.Add(panel.Id);

            var orphans = new List<string>();
            foreach (var entry in ActionCatalog.All(state))
                if (!panels.Contains(entry.viewId) && !orphans.Contains(entry.viewId))
                    orphans.Add(entry.viewId);

            Assert.IsEmpty(orphans,
                "Index entries name panels that are not in the rail: " + string.Join(", ", orphans)
                + ". Knowing a verb exists is useless if the screen it names does not.");
        }
    }
}
