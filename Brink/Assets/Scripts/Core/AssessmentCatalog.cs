using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// The first-launch strategic assessment (GDD §5): ten in-universe scenarios
    /// whose scoring is hidden. The player is being evaluated for a post, not
    /// filling in a character sheet.
    /// </summary>
    public static class AssessmentCatalog
    {
        static List<AssessmentQuestion> questions;

        public static IReadOnlyList<AssessmentQuestion> Questions => questions ?? (questions = Build());

        static AssessmentOption Option(string text,
            float force = 0, float secrecy = 0, float coalition = 0, float order = 0, float horizon = 0,
            float mil = 0, float eco = 0, float intel = 0, float dip = 0, float gov = 0)
        {
            return new AssessmentOption
            {
                text = text,
                scores = new DoctrineProfile
                {
                    force = force, secrecy = secrecy, coalition = coalition, order = order, horizon = horizon,
                    militaryAffinity = mil, economyAffinity = eco, intelligenceAffinity = intel,
                    diplomacyAffinity = dip, governmentAffinity = gov
                }
            };
        }

        static List<AssessmentQuestion> Build()
        {
            return new List<AssessmentQuestion>
            {
                new AssessmentQuestion
                {
                    id = "Q1",
                    situation = "A neighboring state has massed forces near a shared frontier. " +
                                "Their public statements are routine. Your reporting is thin.",
                    prompt = "Your first instruction to the government:",
                    options = new[]
                    {
                        Option("Move our own forces to a matching posture.", force: 2, mil: 3),
                        Option("Task every collection asset we have at that frontier.", secrecy: 1, intel: 3, horizon: 1),
                        Option("Open a private channel and ask them directly.", coalition: 2, dip: 3),
                        Option("Say nothing publicly. Prepare the treasury for disruption.", eco: 3, horizon: 2)
                    }
                },
                new AssessmentQuestion
                {
                    id = "Q2",
                    situation = "A rival's economy is under strain. An opportunity exists to " +
                                "deepen that strain at moderate cost to our own exporters.",
                    prompt = "You advise:",
                    options = new[]
                    {
                        Option("Apply the pressure. Their weakness is our leverage.", force: 2, eco: 3),
                        Option("Do nothing overt. Position quietly to benefit either way.", secrecy: 2, eco: 2, horizon: 2),
                        Option("Offer relief in exchange for concessions.", coalition: 2, dip: 3),
                        Option("Protect our own exporters first. Domestic stability comes before advantage.", order: 2, gov: 3)
                    }
                },
                new AssessmentQuestion
                {
                    id = "Q3",
                    situation = "A trusted official has been briefing the press without authorization. " +
                                "Their work is otherwise excellent.",
                    prompt = "You recommend:",
                    options = new[]
                    {
                        Option("Remove them. Discipline is not negotiable.", order: 3, gov: 3),
                        Option("Use them. Feed the channel material we want carried.", secrecy: 3, intel: 3),
                        Option("Speak to them privately. Keep the talent, close the leak.", coalition: 1, dip: 2, gov: 1),
                        Option("Restructure the office so the leak cannot recur.", order: 2, gov: 2, horizon: 2)
                    }
                },
                new AssessmentQuestion
                {
                    id = "Q4",
                    situation = "An ally requests military support for an operation you consider " +
                                "unwise. Refusing will cost us standing with them.",
                    prompt = "Your position:",
                    options = new[]
                    {
                        Option("Support them fully. Alliances are worth more than any single judgment.", coalition: 3, dip: 2, mil: 1),
                        Option("Support them visibly, but limit our actual exposure.", secrecy: 2, coalition: 1, dip: 2),
                        Option("Refuse. We do not spend our forces on other people's mistakes.", force: 1, mil: 2, order: 1),
                        Option("Refuse the operation; offer economic support instead.", eco: 3, dip: 1)
                    }
                },
                new AssessmentQuestion
                {
                    id = "Q5",
                    situation = "Intelligence indicates a foreign service has penetrated one of our " +
                                "ministries. The extent is unclear.",
                    prompt = "You direct:",
                    options = new[]
                    {
                        Option("Roll it up immediately, whatever the disruption.", force: 2, order: 2, intel: 2),
                        Option("Leave it in place. Feed it. Learn who is asking what.", secrecy: 3, intel: 3, horizon: 2),
                        Option("Quietly restructure the ministry's access.", order: 2, gov: 2, intel: 1),
                        Option("Confront their ambassador privately.", coalition: 1, dip: 3)
                    }
                },
                new AssessmentQuestion
                {
                    id = "Q6",
                    situation = "Inflation is climbing. The politically easy response would " +
                                "postpone the pain by roughly two years.",
                    prompt = "You advise the leadership:",
                    options = new[]
                    {
                        Option("Take the pain now. The later reckoning is always worse.", eco: 3, horizon: 3, order: 1),
                        Option("Postpone. A government that falls fixes nothing.", gov: 3, horizon: -2),
                        Option("Split the difference and buy time to negotiate supply deals.", dip: 2, eco: 2),
                        Option("Use the moment. Restructure the sectors that failed us.", eco: 2, order: 2, horizon: 2)
                    }
                },
                new AssessmentQuestion
                {
                    id = "Q7",
                    situation = "A smaller state, strategically placed, is drifting toward a rival's " +
                                "orbit. They are not hostile to us — merely neglected.",
                    prompt = "Your approach:",
                    options = new[]
                    {
                        Option("Invest in them. Trade, access, visible partnership.", coalition: 3, dip: 3, eco: 1),
                        Option("Cultivate people inside their government quietly.", secrecy: 3, intel: 3),
                        Option("Make the cost of drifting clear to them.", force: 3, mil: 1, eco: 1),
                        Option("Let them go. We cannot hold everyone.", horizon: 1, order: 1)
                    }
                },
                new AssessmentQuestion
                {
                    id = "Q8",
                    situation = "An operation you authorized has gone wrong. Civilians were harmed. " +
                                "Attribution is possible but not yet certain.",
                    prompt = "You recommend:",
                    options = new[]
                    {
                        Option("Acknowledge it before someone else does. Control the account.", coalition: 2, dip: 2, gov: 2),
                        Option("Deny everything. Attribution is not proof.", secrecy: 3, order: 1),
                        Option("Say nothing. Fix the failure internally and move on.", secrecy: 2, order: 2, gov: 1),
                        Option("Accept responsibility and compensate. Reputation is a strategic asset.", coalition: 3, dip: 3, horizon: 2)
                    }
                },
                new AssessmentQuestion
                {
                    id = "Q9",
                    situation = "The armed forces request a substantial increase in readiness funding. " +
                                "The threat is real but not imminent.",
                    prompt = "You advise:",
                    options = new[]
                    {
                        Option("Fund it. Readiness cannot be improvised when it is needed.", force: 2, mil: 3, horizon: 2),
                        Option("Fund half. Build the industrial base that sustains it instead.", eco: 3, mil: 1, horizon: 3),
                        Option("Deny it. Buy the same security through diplomacy.", dip: 3, coalition: 2),
                        Option("Redirect it to intelligence. Warning time is cheaper than readiness.", intel: 3, secrecy: 1)
                    }
                },
                new AssessmentQuestion
                {
                    id = "Q10",
                    situation = "Domestic unrest is building over a policy the leadership considers " +
                                "essential. Emergency measures are available.",
                    prompt = "Your counsel:",
                    options = new[]
                    {
                        Option("Use the measures. Order first; explain afterward.", order: 3, force: 2, gov: 2),
                        Option("Negotiate with the organizers. Buy consent rather than compel it.", coalition: 3, dip: 2, gov: 1),
                        Option("Amend the policy. A policy that cannot be sustained is not essential.", gov: 2, horizon: 2),
                        Option("Find out who is organizing it, and why, before deciding anything.", secrecy: 2, intel: 3)
                    }
                }
            };
        }
    }
}
