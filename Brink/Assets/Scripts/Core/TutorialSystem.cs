using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// First-posting orientation (GDD §5). Teaches the five things a new operator
    /// cannot play without: the monthly loop, the scarcity of Command Points,
    /// delegation, the intelligence fog, and the crisis interrupt.
    ///
    /// It teaches by making the operator *do* each one in the live simulation —
    /// there is no sandbox and no scripted world. It can be skipped at any point,
    /// and it never blocks an action.
    /// </summary>
    public static class TutorialSystem
    {
        static List<TutorialStep> steps;

        public static IReadOnlyList<TutorialStep> Steps => steps ?? (steps = Build());

        public static TutorialStep CurrentStep(GameState state)
        {
            var tutorial = state.tutorial;
            if (tutorial == null || !tutorial.active || tutorial.completed) return null;
            if (tutorial.stepIndex < 0 || tutorial.stepIndex >= Steps.Count) return null;
            return Steps[tutorial.stepIndex];
        }

        public static void Begin(GameState state)
        {
            state.tutorial = new TutorialState { active = true, stepIndex = 0 };
            CaptureBaseline(state);

            state.AddNotification(NotificationClass.Priority, "INDUCTION BRIEFING",
                "You have the terminal. Orientation is on the left of your screen.",
                state.playerCountryId);
        }

        public static void Skip(GameState state)
        {
            if (state.tutorial == null) return;
            state.tutorial.active = false;
            state.tutorial.completed = true;

            // Someone who skips the orientation is the *most* likely to be
            // stranded, not the least — they have opted out of the teaching and
            // still face twelve panels. Skipping used to leave them with
            // nothing at all.
            HandOver(state);
            GameLog.Info("TUTORIAL", "Orientation dismissed.");
        }

        /// <summary>
        /// The handover from orientation to the game.
        ///
        /// The tutorial teaches six things and then stops, and what followed was
        /// silence — the operator was left in front of twelve panels and several
        /// dozen verbs with no answer to "what now?". This names the two places
        /// that answer it, because a system nobody can find is a system that does
        /// not exist.
        ///
        /// Deliberately a pointer rather than more teaching: the answer to "there
        /// is too much here" is not another tutorial step.
        /// </summary>
        static void HandOver(GameState state)
        {
            state.AddNotification(NotificationClass.Priority, "YOU HAVE THE POST",
                "The world does not wait for you.\n\n" +
                "Two things will keep you oriented. ACTIONS lists every command " +
                "available to you right now, with its cost, the panel to issue it " +
                "from, and — for anything currently out of reach — the reason. " +
                "The BRIEFING carries what your Cabinet recommends: a short " +
                "standing view of what is worth doing, drawn from the state of " +
                "this country rather than a checklist.\n\n" +
                "Neither is an instruction. Nothing here is scored on obedience.",
                state.playerCountryId);
        }

        /// <summary>Snapshot the values a step measures progress against.</summary>
        static void CaptureBaseline(GameState state)
        {
            var tutorial = state.tutorial;
            tutorial.commandPointsAtStep = state.commandPoints.current;
            tutorial.initiativesAtStep = state.initiativesThisYear;
            tutorial.monthsAtStep = state.date.MonthsSince(state.startDate);
        }

        /// <summary>
        /// Advance the orientation if the current step's condition is met. Safe to
        /// call every frame and after every action.
        /// </summary>
        public static void Evaluate(GameState state)
        {
            var step = CurrentStep(state);
            if (step == null) return;
            if (step.isSatisfied != null && !step.isSatisfied(state)) return;

            var tutorial = state.tutorial;
            tutorial.completedStepIds.Add(step.id);
            tutorial.stepIndex++;

            if (tutorial.stepIndex >= Steps.Count)
            {
                tutorial.active = false;
                tutorial.completed = true;
                HandOver(state);
                state.AddChronicle(ChronicleCategory.System, state.playerCountryId,
                    "Operator orientation complete.");
                ProgressionSystem.AwardXP(state, 30, "Orientation complete");
                GameLog.Info("TUTORIAL", "Orientation complete.");
                return;
            }

            CaptureBaseline(state);
            GameLog.Info("TUTORIAL", $"Orientation step: {CurrentStep(state)?.title}");
        }

        static List<TutorialStep> Build()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    id = "BRIEFING",
                    title = "THE BRIEFING",
                    targetViewId = "BRIEFING",
                    body =
                        "This terminal is your only window on the world. It does not show you the " +
                        "world — it shows you what has been reported to you, which is not the same " +
                        "thing.\n\n" +
                        "The BRIEFING is where each month begins: priority traffic first, then the " +
                        "state of the nation, then the wire. Read it before you decide anything.",
                    instruction = "Open BRIEFING.",
                    isSatisfied = s => true // satisfied by acknowledgement
                },

                new TutorialStep
                {
                    id = "COMMAND_POINTS",
                    title = "COMMAND POINTS",
                    targetViewId = "CABINET",
                    body =
                        "You are one person. Command Points are how much of the government's work " +
                        "you can personally direct in a month — roughly five, and they do not " +
                        "accumulate freely.\n\n" +
                        "Everything you do personally costs them. Reading costs nothing. The " +
                        "constraint is not the government's capacity; it is yours.",
                    instruction = "Spend Command Points on any action.",
                    isSatisfied = s => s.commandPoints.current < s.tutorial.commandPointsAtStep
                },

                new TutorialStep
                {
                    id = "DELEGATION",
                    title = "DELEGATION",
                    targetViewId = "CABINET",
                    body =
                        "Five officials run the pillars. Left alone they act on their own judgment, " +
                        "and it costs you nothing — a competent minister is better than your " +
                        "attention spent elsewhere.\n\n" +
                        "DIRECTED sets their priority for Influence. DIRECT CONTROL sidelines them " +
                        "and puts the work on your desk, at Command Point cost, and they will " +
                        "remember being bypassed.\n\n" +
                        "Depth is optional. Command the pillar you care about; delegate the rest.",
                    instruction = "Change any official's control mode in CABINET.",
                    isSatisfied = s =>
                    {
                        foreach (var official in s.cabinet)
                            if (official.mode != ControlMode.Autonomous) return true;
                        return false;
                    }
                },

                new TutorialStep
                {
                    id = "INTELLIGENCE",
                    title = "WHAT YOU DO NOT KNOW",
                    targetViewId = "INTELLIGENCE",
                    body =
                        "You will notice foreign strength is shown as a range with a confidence " +
                        "grade, or as UNTASKED when nothing is collecting on them. That is not a " +
                        "placeholder. You never see " +
                        "another country's true figures — only what your services have managed to " +
                        "learn.\n\n" +
                        "Estimates can be wrong: through poor access, through their " +
                        "counterintelligence, or because they are deliberately deceiving you. " +
                        "Establish a network before you act on a number.",
                    instruction = "Establish a collection network against any state.",
                    isSatisfied = s =>
                    {
                        foreach (var network in s.networks)
                            if (network.ownerId == s.playerCountryId) return true;
                        return false;
                    }
                },

                new TutorialStep
                {
                    id = "THE_MONTH",
                    title = "ENDING THE MONTH",
                    targetViewId = "BRIEFING",
                    body =
                        "When you have spent what you intend to spend, end the month. Officials " +
                        "act, markets move, foreign governments pursue their own objectives, and " +
                        "the consequences of your decisions arrive.\n\n" +
                        "The world does not wait for you, and it does not scale itself to you.",
                    instruction = "End the month.",
                    isSatisfied = s => s.date.MonthsSince(s.startDate) > s.tutorial.monthsAtStep
                },

                new TutorialStep
                {
                    id = "CRISIS",
                    title = "WHEN THE MONTH WILL NOT END",
                    targetViewId = "BRIEFING",
                    body =
                        "Some situations will not wait for your convenience. A Crisis Turn " +
                        "interrupts the month and asks you for an answer.\n\n" +
                        "You are never forced to give one. The month will end whether or not you " +
                        "decide, and this office is judged to have drifted — which costs more than " +
                        "any option on the table would have, because every option is somebody " +
                        "taking responsibility and drifting is nobody doing so.\n\n" +
                        "Crisis decisions never cost Command Points — you will not be left helpless " +
                        "because you spent your capacity earlier. But every option has a price, and " +
                        "the cheapest one is rarely free.\n\n" +
                        "That is the whole job: limited attention, imperfect information, " +
                        "consequences that outlast you. Your performance is assessed at the end of " +
                        "each year. Good luck, operator.",
                    instruction = "Continue.",
                    isSatisfied = s => true
                }
            };
        }
    }
}
