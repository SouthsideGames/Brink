using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>One thing the operator can do, and whether they can do it now.</summary>
    public class ActionEntry
    {
        public Pillar pillar;
        public string viewId;
        public string label;
        public string cost;
        public string description;

        /// <summary>False when something currently prevents it.</summary>
        public bool available;

        /// <summary>Why not, when unavailable. Never empty if `available` is false.</summary>
        public string blockedReason;
    }

    /// <summary>
    /// Everything the operator can do, in one place (GDD §28.1).
    ///
    /// This exists because the game's own author, who designed every verb in it,
    /// reported forgetting what was possible. That is not an onboarding problem —
    /// onboarding fades and this does not. It is a **reference** problem, and a
    /// reference has to be permanent, complete and honest about what is currently
    /// out of reach.
    ///
    /// Three rules make it useful rather than a wall of text:
    ///
    /// 1. **Unavailable entries still appear**, with the reason. Hiding a verb
    ///    the operator cannot use today teaches them it does not exist; showing
    ///    it greyed with "requires Limited Conflict" teaches them the game.
    /// 2. **Every entry names its panel**, because knowing a thing exists is
    ///    useless if you cannot find where to do it.
    /// 3. **Costs are stated**, so the list doubles as the answer to "what can I
    ///    afford this month".
    ///
    /// This is a *catalog*, not an executor — it never performs anything. Views
    /// remain the only place actions are taken, so this cannot drift into being
    /// a second, competing interface.
    /// </summary>
    public static class ActionCatalog
    {
        public static List<ActionEntry> All(GameState state)
        {
            var entries = new List<ActionEntry>();
            if (state?.PlayerCountry == null) return entries;

            var player = state.PlayerCountry;
            var confrontation = state.ActiveConfrontation;
            bool atWar = confrontation != null && !confrontation.resolved
                         && confrontation.escalation >= EscalationState.LimitedConflict;

            void Add(Pillar pillar, string viewId, string label, string cost,
                string description, bool available = true, string blockedReason = "")
                => entries.Add(new ActionEntry
                {
                    pillar = pillar, viewId = viewId, label = label, cost = cost,
                    description = description, available = available,
                    blockedReason = available ? "" : blockedReason
                });

            // ---------- military ----------

            Add(Pillar.Military, "MILITARY", "Set posture", "1–2 CP",
                "Peacetime, Alert or Forward. Higher postures hold readiness up and cost treasury every month.");
            Add(Pillar.Military, "MILITARY", "Adopt doctrine", "2 CP",
                "Maneuver, Attrition or Deterrence. Changes how operations resolve; grants no capability.");
            Add(Pillar.Military, "MILITARY", "Begin procurement", "2–3 CP",
                "A multi-year programme. The only thing that writes force strength upward.");
            Add(Pillar.Military, "MILITARY", "Invest in logistics", "1 CP + treasury",
                "Raises the sustainment ceiling and softens the drag of holding a posture.");
            Add(Pillar.Military, "MILITARY", "Conduct a joint exercise", "1–3 CP",
                "Readiness, interoperability and trust with a partner, bought with exposure.");
            // A second front is expensive, not forbidden. The gate is what the
            // force can actually sustain, so the index says "we are at our limit"
            // rather than "you already have one".
            bool canOpen = ConfrontationSystem.CanOpenAnother(
                state, state.playerCountryId, out string commitmentBlock);
            Add(Pillar.Military, "MILITARY", "Open a confrontation", "2 CP",
                "Commit against a state with a stated objective and primary strategy. " +
                "A second front drags every operation in the first.",
                canOpen, commitmentBlock);
            Add(Pillar.Military, "MILITARY", "Change escalation", "1 CP + premium",
                "Move up or down the ladder. Skipping levels costs a political premium.",
                confrontation != null, "Requires an active confrontation.");
            // Counted from the catalog rather than written out. This line used to
            // read "Assault, Raid, Siege or Withdraw" and stayed that way while
            // the list grew to twenty-three — the COMMAND INDEX exists precisely
            // because the operator cannot hold the verb list in their head, so an
            // index that advertises four of them is worse than none.
            Add(Pillar.Military, "MILITARY", "Launch an operation", "1–4 CP",
                $"{OperationCatalog.All.Count} operations across ground, naval, air and joint. " +
                "Selecting one is free; only EXECUTE spends capacity.",
                atWar, "Requires Limited Conflict or higher.");
            Add(Pillar.Military, "MILITARY", "Defensive programme", "1–3 CP",
                "Fortify ground we hold, pacify occupied territory, escort our shipping or " +
                "build the shield. No confrontation required.");
            Add(Pillar.Military, "MILITARY", "Propose terms", "0 CP",
                "Offer a settlement. Overreaching prolongs the war.",
                confrontation != null, "Requires an active confrontation.");

            // ---------- economy ----------

            Add(Pillar.Economy, "ECONOMY", "Impose sanctions", "2 CP",
                "Five severities. Coercion always blows back on the sender through inflation.");
            Add(Pillar.Economy, "ECONOMY", "Lift sanctions", "1 CP",
                "Ends a regime and begins repairing the relationship.");
            Add(Pillar.Economy, "ECONOMY", "Set tariffs", "1 CP",
                "Adjust a trade link's terms. Protects an industry and costs the relationship.");
            Add(Pillar.Economy, "ECONOMY", "Open a trade link", "1 CP",
                "New trade builds dependence — theirs on us, and ours on them.");

            // ---------- intelligence ----------

            Add(Pillar.Intelligence, "INTELLIGENCE", "Establish a network", "2 CP",
                "Collection against one state in one domain. Everything else here needs it first.");
            Add(Pillar.Intelligence, "INTELLIGENCE", "Expand a network", "1 CP",
                "Deeper penetration, better estimates, more exposure.");
            Add(Pillar.Intelligence, "INTELLIGENCE", "Set collection focus", "0 CP",
                "Which domain a network reports on.");
            Add(Pillar.Intelligence, "INTELLIGENCE", "Run a covert operation", "2 CP",
                "Sabotage, influence or theft. Exposure costs standing with everyone.");
            Add(Pillar.Intelligence, "INTELLIGENCE", "Counterintelligence sweep", "1 CP",
                "Harden the state against penetration.");

            // ---------- diplomacy ----------

            Add(Pillar.Diplomacy, "DIPLOMACY", "Diplomatic outreach", "1 CP",
                "Improve standing with one state. The groundwork everything else rests on.");
            Add(Pillar.Diplomacy, "DIPLOMACY", "Propose a treaty", "2 CP",
                "Explicit commitments. They accept on their interests, not our wishes.");
            Add(Pillar.Diplomacy, "DIPLOMACY", "Break a treaty", "1 CP",
                "Immediate freedom, lasting reputational damage with everyone watching.");
            Add(Pillar.Diplomacy, "DIPLOMACY", "Assemble a coalition", "3 CP",
                "Recruit partners into a confrontation. They join on their own reasoning.",
                confrontation != null, "Requires an active confrontation.");

            // ---------- government ----------

            Add(Pillar.Government, "GOVERNMENT", "Public messaging", "2 PC",
                "Approval and unity. The cheap instrument, and the one that fixes mood not machinery.");
            Add(Pillar.Government, "GOVERNMENT", "Institutional reform", "6 PC",
                "Builds the state's capacity and costs the goodwill of whoever benefits from the status quo.");
            Add(Pillar.Government, "GOVERNMENT", "Declare emergency powers", "PC + approval",
                "Extra command capacity, bought with legitimacy. Cheaper in centralized systems.");
            Add(Pillar.Government, "GOVERNMENT", "Set national priority", "3 PC",
                "Redirects every delegated official. The broadest lever available.");
            Add(Pillar.Government, "GOVERNMENT",
                player.government.IsElective ? "Bargain with the chamber" : "Accommodate the elite", "2 PC",
                "Support bought rather than earned. It decays, so it has to be kept up.");
            Add(Pillar.Government, "GOVERNMENT", "Distribute patronage", "1 PC + treasury",
                "The same support, bought with money instead of standing — and it hollows the state.",
                player.resources.treasury >= GovernmentSystem.PatronageTreasury,
                "The treasury cannot cover it.");
            Add(Pillar.Government, "GOVERNMENT", "Public inquiry", "4 PC",
                "Raises the weakest minister and the machinery around them. Nobody involved is grateful.");
            Add(Pillar.Government, "GOVERNMENT", "Prepare a successor", "3 PC",
                "A transition you saw coming. Raises who arrives next and keeps the handover orderly.",
                player.government.successorReadiness < 99f, "Continuity planning is already complete.");
            Add(Pillar.Government, "GOVERNMENT", "Set civic posture", "3 PC",
                "Open or restrictive. Order against legitimacy, and it decides how fast plots form.");
            Add(Pillar.Government, "GOVERNMENT", "Consolidate authority", "12 PC",
                "Permanently make one pillar the operator's to command. The one large purchase.");
            Add(Pillar.Government, "GOVERNMENT", "Dismiss an official", "PC",
                "Replace a minister. Costs more in elective systems.");
            Add(Pillar.Government, "GOVERNMENT", "Call an early election", "PC",
                "Parliamentary systems only. A gamble on current standing.",
                player.government.AllowsEarlyElection, "Only a parliamentary system can go to the country early.");
            Add(Pillar.Government, "CABINET", "Appoint to a vacancy", "0 CP",
                "Choose from the shortlist. The government appoints for you after three months.",
                player.vacancies.Count > 0, "No office is currently vacant.");
            Add(Pillar.Government, "CABINET", "Set a control mode", "0–1 INF",
                "Autonomous, Directed or Direct Control. Direct Control needs constitutional authority.");

            // ---------- long game ----------

            Add(Pillar.Economy, "RESEARCH", "Authorize research", "2 CP + treasury",
                "Multi-year programmes across all five pillars. Capabilities unlock ability, never force.");
            Add(Pillar.Military, "ENDGAME", "Prepare an instrument", "2 CP + treasury",
                "Months of preparation toward one decisive capability. Visible to anyone collecting on us.");
            Add(Pillar.Military, "ENDGAME", "Execute an instrument", "4 CP",
                "Only when prepared, and only against a state we are confronting.");
            Add(Pillar.Government, "OPERATOR", "Unlock a skill", "Skill points",
                "Operator capability only — command capacity, action costs, precision. Never national power.",
                state.skillPoints > 0, "No skill points available.");

            return entries;
        }

        /// <summary>Just the entries for one pillar, for a per-pillar readout.</summary>
        public static List<ActionEntry> ForPillar(GameState state, Pillar pillar)
        {
            var subset = new List<ActionEntry>();
            foreach (var entry in All(state))
                if (entry.pillar == pillar) subset.Add(entry);
            return subset;
        }

        /// <summary>How many actions are currently open to the operator.</summary>
        public static int AvailableCount(GameState state)
        {
            int count = 0;
            foreach (var entry in All(state))
                if (entry.available) count++;
            return count;
        }
    }
}
