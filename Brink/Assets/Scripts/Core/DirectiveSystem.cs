using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>One standing objective the government believes is worth pursuing.</summary>
    public class StrategicDirective
    {
        public string id;
        public string title;

        /// <summary>Why this is being raised, in the Cabinet's voice.</summary>
        public string rationale;

        /// <summary>Where the operator would act on it.</summary>
        public string viewId;

        /// <summary>Concretely, what to do — named verbs, not vague encouragement.</summary>
        public string suggestion;

        /// <summary>Higher sorts first. Not a score shown to the player.</summary>
        public float urgency;
    }

    /// <summary>
    /// Optional strategic directives (GDD §29).
    ///
    /// The problem this solves is the one the game's own author hit: after the
    /// tutorial ends there are twelve panels and several dozen verbs, and no
    /// answer to "what should I do first?". The attention markers say what is
    /// *waiting*; the action index says what is *possible*; neither says what is
    /// **worth doing**, and that is the question a new operator actually has.
    ///
    /// The GDD is emphatic that these must never become generic daily-task
    /// chores, so three rules hold:
    ///
    /// 1. **They are derived, never dealt.** Every directive comes from a real
    ///    condition in this world — an energy dependency, a decayed army, an
    ///    isolated diplomatic position. If nothing is wrong, nothing is offered.
    ///    A directive the world did not earn is a chore.
    /// 2. **Few at a time.** At most <see cref="MaxShown"/>. A list of fifteen
    ///    objectives is a task queue, not advice.
    /// 3. **They are advice, not obligations.** Nothing tracks completion,
    ///    nothing scolds, nothing rewards clearing them. They disappear when the
    ///    condition that raised them goes away, which is the only completion
    ///    that means anything.
    ///
    /// Nothing here is stored. Directives are recomputed from the live world, so
    /// they cannot go stale and cannot disagree with the panel they point at.
    /// </summary>
    public static class DirectiveSystem
    {
        /// <summary>Never advise on more than this at once.</summary>
        public const int MaxShown = 3;

        public static List<StrategicDirective> Collect(GameState state)
        {
            var found = new List<StrategicDirective>();
            if (state?.PlayerCountry == null) return found;

            var player = state.PlayerCountry;
            var resources = player.resources;

            void Add(string id, float urgency, string title, string viewId,
                string rationale, string suggestion)
                => found.Add(new StrategicDirective
                {
                    id = id, urgency = urgency, title = title, viewId = viewId,
                    rationale = rationale, suggestion = suggestion
                });

            // ---- the country's own vulnerabilities ----

            if (resources.energy < 45f)
                Add("ENERGY", 100f - resources.energy,
                    "Reduce energy dependence", "ECONOMY",
                    $"Energy security stands at {resources.energy:F0}. A shortfall this deep " +
                    "drags growth every month and hands leverage to whoever supplies us.",
                    "Build trade links with an energy exporter, fund an energy research " +
                    "programme, or take and hold an energy region.");

            if (resources.strategicMaterials < 40f)
                Add("MATERIALS", 90f - resources.strategicMaterials,
                    "Secure critical materials", "ECONOMY",
                    $"Strategic materials at {resources.strategicMaterials:F0}. Industry and " +
                    "procurement both draw on this, and we cannot buy our way past the shortfall.",
                    "Open trade with a resource power, or contest a materials-bearing region.");

            if (player.military.ground.readiness < 45f)
                Add("READINESS", 85f - player.military.ground.readiness,
                    "Restore the armed forces", "MILITARY",
                    $"Ground readiness at {player.military.ground.readiness:F0}. A force at this " +
                    "level cannot be committed to anything and does not deter.",
                    "Raise posture to Alert, invest in logistics, or direct the defence " +
                    "ministry to prioritise readiness.");

            if (player.stability < 45f)
                Add("STABILITY", 95f - player.stability,
                    "Steady the state", "GOVERNMENT",
                    $"Stability at {player.stability:F0}. Below this, coup risk rises and " +
                    "every other instrument gets harder to use.",
                    "Institutional reform, or a public messaging campaign if the problem " +
                    "is mood rather than machinery.");

            if (player.governmentApproval < 40f)
                Add("APPROVAL", 80f - player.governmentApproval,
                    "Recover public support", "GOVERNMENT",
                    $"Approval at {player.governmentApproval:F0}. An unpopular government has " +
                    "less political capital to spend and loses elections.",
                    "Public messaging, or address whatever the economy is doing to living standards.");

            // ---- position in the world ----

            int treaties = 0;
            foreach (var treaty in state.treaties)
                if (treaty.Involves(player.id)) treaties++;
            if (treaties == 0)
                Add("ISOLATED", 70f,
                    "End our diplomatic isolation", "DIPLOMACY",
                    "We hold no treaties. An isolated state fights alone, trades on worse " +
                    "terms, and has nobody to call.",
                    "Open diplomatic outreach, then propose a treaty to a state whose " +
                    "interests already align with ours.");

            int networks = 0;
            foreach (var network in state.networks)
                if (network.ownerId == player.id && !network.compromised) networks++;
            if (networks == 0)
                Add("BLIND", 65f,
                    "Establish collection", "INTELLIGENCE",
                    "We run no collection networks. Every estimate we hold about a foreign " +
                    "power is guesswork, and our own decisions rest on it.",
                    "Establish a network against the state that most concerns us.");

            if (player.technology.programs.Count == 0)
                Add("RESEARCH", 45f,
                    "Fund a research programme", "RESEARCH",
                    "No research is under way. Capabilities take years, so the cost of not " +
                    "starting is paid much later than it is incurred.",
                    "Authorize a programme in whichever pillar we intend to lead.");

            // ---- the thing in front of us ----

            var confrontation = state.ActiveConfrontation;
            if (confrontation != null && !confrontation.resolved)
                Add("CONFRONTATION", 120f,
                    "Resolve the confrontation", "MILITARY",
                    $"We are committed against " +
                    $"{state.FindCountry(confrontation.OpponentOf(player.id))?.displayName}. " +
                    "A war that is neither prosecuted nor settled costs us every month it runs.",
                    "Press it with operations, apply pressure in our chosen domain, or " +
                    "propose terms.");

            found.Sort((a, b) => b.urgency.CompareTo(a.urgency));
            if (found.Count > MaxShown) found.RemoveRange(MaxShown, found.Count - MaxShown);
            return found;
        }
    }
}
