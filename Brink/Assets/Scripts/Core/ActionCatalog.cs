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

        /// <summary>
        /// The `GameController` method(s) this entry documents, by name.
        ///
        /// **This is what stops the index going stale.** The index was written
        /// once and then ten verbs shipped past it — industrial programmes, agent
        /// operations, accession efforts, equipment orders, war footing, the
        /// strategic pivot, directives, direct action and securing the army —
        /// each wired into a view and each invisible to the one screen whose
        /// entire job is to answer "what can I do". A reference that is missing a
        /// third of its subject is worse than none, because it is trusted.
        ///
        /// Written with `nameof`, so renaming an operator verb breaks the build
        /// here rather than silently orphaning its entry, and
        /// `ActionIndexTests` fails when a new one appears with no entry at all.
        /// </summary>
        public string[] verbs = EmptyVerbs;

        static readonly string[] EmptyVerbs = new string[0];
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
        /// <summary>
        /// Any network at all, uncompromised. A commissioned assessment needs
        /// collection but not the deep access a personal approach does — the
        /// grade it comes back with is what thin reporting costs.
        /// </summary>
        static bool AnyNetwork(GameState state)
        {
            foreach (var network in state.networks)
                if (network.ownerId == state.playerCountryId && !network.compromised) return true;
            return false;
        }

        public static List<ActionEntry> All(GameState state)
        {
            var entries = new List<ActionEntry>();
            if (state?.PlayerCountry == null) return entries;

            var player = state.PlayerCountry;
            var confrontation = state.ActiveConfrontation;
            bool atWar = confrontation != null && !confrontation.resolved
                         && confrontation.escalation >= EscalationState.LimitedConflict;

            // A minister cannot be reached through a thin network, so the index
            // says whether *any* of ours is deep enough rather than advertising a
            // verb that would be refused wherever the operator pointed it.
            bool deepNetwork = false;
            foreach (var network in state.networks)
                if (network.ownerId == state.playerCountryId && !network.compromised
                    && network.penetration >= AgentSystem.MinimumPenetration)
                { deepNetwork = true; break; }

            void Add(Pillar pillar, string viewId, string label, string cost,
                string description, bool available = true, string blockedReason = "",
                string[] verbs = null)
                => entries.Add(new ActionEntry
                {
                    pillar = pillar, viewId = viewId, label = label, cost = cost,
                    description = description, available = available,
                    blockedReason = available ? "" : blockedReason,
                    verbs = verbs ?? new string[0]
                });

            // ---------- military ----------

            Add(Pillar.Military, "MILITARY", "Set posture", "1–2 CP",
                "Peacetime, Alert or Forward. Higher postures hold readiness up and cost treasury every month.",
                verbs: new[] { nameof(GameController.SetPosture) });
            Add(Pillar.Military, "MILITARY", "Adopt doctrine", "2 CP",
                "Maneuver, Attrition or Deterrence. Changes how operations resolve; grants no capability.",
                verbs: new[] { nameof(GameController.SetDoctrine) });
            Add(Pillar.Military, "MILITARY", "Begin procurement", "2–3 CP",
                "A multi-year programme. The only thing that writes force strength upward.",
                verbs: new[] { nameof(GameController.BeginProcurement) });
            Add(Pillar.Military, "MILITARY", "Invest in logistics", "1 CP + treasury",
                "Raises the sustainment ceiling and softens the drag of holding a posture.",
                verbs: new[] { nameof(GameController.InvestInLogistics) });
            Add(Pillar.Military, "MILITARY", "Conduct a joint exercise", "1–3 CP",
                "Readiness, interoperability and trust with a partner, bought with exposure.",
                verbs: new[] { nameof(GameController.ConductExercise) });
            // A second front is expensive, not forbidden. The gate is what the
            // force can actually sustain, so the index says "we are at our limit"
            // rather than "you already have one".
            bool canOpen = ConfrontationSystem.CanOpenAnother(
                state, state.playerCountryId, out string commitmentBlock);
            Add(Pillar.Military, "MILITARY", "Open a confrontation", "2 CP",
                "Commit against a state with a stated objective and primary strategy. " +
                "A second front drags every operation in the first.",
                canOpen, commitmentBlock,
                verbs: new[] { nameof(GameController.BeginConfrontation) });
            Add(Pillar.Military, "MILITARY", "Change escalation", "1 CP + premium",
                "Move up or down the ladder. Skipping levels costs a political premium.",
                confrontation != null, "Requires an active confrontation.",
                verbs: new[] { nameof(GameController.SetEscalation) });
            // Counted from the catalog rather than written out. This line used to
            // read "Assault, Raid, Siege or Withdraw" and stayed that way while
            // the list grew to twenty-three — the COMMAND INDEX exists precisely
            // because the operator cannot hold the verb list in their head, so an
            // index that advertises four of them is worse than none.
            Add(Pillar.Military, "MILITARY", "Launch an operation", "1–4 CP",
                $"{OperationCatalog.All.Count} operations across ground, naval, air and joint. " +
                "Selecting one is free; only EXECUTE spends capacity.",
                atWar, "Requires Limited Conflict or higher.",
                verbs: new[] { nameof(GameController.LaunchOperation) });
            string standingBlock = confrontation == null
                ? "Requires an active confrontation."
                : OperationPlanningSystem.StandingOrderIssueBlockReason(state, confrontation.id);
            bool canStand = string.IsNullOrEmpty(standingBlock);
            Add(Pillar.Military, "COMMAND CENTER", "Issue a standing order", "normal operation CP",
                "Preauthorize the next campaign-plan step after monthly command capacity refresh. " +
                "At most one step executes; every live gate and the ordinary price still apply.",
                canStand, standingBlock,
                verbs: new[] { nameof(GameController.SetStandingOrder) });
            Add(Pillar.Military, "MILITARY", "Defensive programme", "1–3 CP",
                "Fortify ground we hold, pacify occupied territory, escort our shipping or " +
                "build the shield. No confrontation required.",
                verbs: new[] { nameof(GameController.LaunchDefensiveProgramme) });
            // Ground taken in a war that has ended. The index states the reason
            // it is out of reach rather than hiding the verb, because "we hold
            // nothing that is not ours" and "the war is still on" are different
            // facts about the world and the operator should be able to tell
            // which one applies.
            string relinquishBlock = "We are not occupying anyone's ground.";
            bool canRelinquish = false;
            foreach (var location in state.locations)
            {
                if (location.ownerId != state.playerCountryId) continue;
                if (!location.IsOccupied) continue;
                if (TerritorySystem.CanRelinquish(
                        state, state.playerCountryId, location.id, out string why))
                { canRelinquish = true; break; }
                relinquishBlock = why;
            }
            Add(Pillar.Military, "MILITARY", "Relinquish occupied ground",
                $"{GameController.RelinquishCost} CP",
                "Hand ground back to the state it was taken from. Ends the upkeep and the "
                + "insurgency bill with it; costs war support at home. Not available while "
                + "the war with that state is still running.",
                canRelinquish, relinquishBlock,
                verbs: new[] { nameof(GameController.RelinquishLocation) });
            Add(Pillar.Military, "MILITARY", "Order equipment", "Treasury",
                $"{AssetCatalog.All.Count} counted classes across air, sea and ground. "
                + "Steel takes years, people take months; industry decides throughput.",
                verbs: new[] { nameof(GameController.OrderAssets) });
            Add(Pillar.Military, "MILITARY", "Set war footing", "5 PC + upkeep",
                "Roughly doubles delivery tempo. Gated on political backing rather than money — "
                + "moving the budget is something a chamber grants — and it lapses when it "
                + "cannot be justified.",
                player.government.legislativeSupport >= AcquisitionSystem.WarFootingSupport
                    || !player.government.IsElective,
                "The chamber will not carry it: support below "
                    + $"{AcquisitionSystem.WarFootingSupport:F0}.",
                verbs: new[] { nameof(GameController.SetWarFooting) });
            Add(Pillar.Military, "MILITARY", "Change primary strategy", "3 CP + momentum",
                "Pivot a running confrontation into another domain. Effort spent in the old one "
                + "does not transfer, and the world can see we could not make it work.",
                confrontation != null, "Requires an active confrontation.",
                verbs: new[] { nameof(GameController.Pivot) });
            Add(Pillar.Military, "MILITARY", "Propose terms", "0 CP",
                "Offer a settlement. Overreaching prolongs the war.",
                confrontation != null, "Requires an active confrontation.",
                verbs: new[] { nameof(GameController.ProposeTerms), nameof(GameController.ProposeSettlement) });

            // ---------- economy ----------

            Add(Pillar.Economy, "ECONOMY", "Impose sanctions", "2 CP",
                "Five severities. Coercion always blows back on the sender through inflation.",
                verbs: new[] { nameof(GameController.ImposeSanctions) });
            Add(Pillar.Economy, "ECONOMY", "Lift sanctions", "1 CP",
                "Ends a regime and begins repairing the relationship.",
                verbs: new[] { nameof(GameController.LiftSanctions) });
            Add(Pillar.Economy, "ECONOMY", "Set tariffs", "1 CP",
                "Adjust a trade link's terms. Protects an industry and costs the relationship.",
                verbs: new[] { nameof(GameController.SetTariff) });
            Add(Pillar.Economy, "ECONOMY", "Open a trade link", "1 CP",
                "New trade builds dependence — theirs on us, and ours on them.",
                verbs: new[] { nameof(GameController.ProposeTrade), nameof(GameController.WithdrawFromTrade) });

            // ---------- fiscal statecraft ----------

            Add(Pillar.Economy, "ECONOMY", "Set the tax rate", "2 PC",
                "What share of the economy the state takes. More revenue now against growth, "
                + "approval and what people can afford.",
                verbs: new[] { nameof(GameController.SetTaxRate) });
            Add(Pillar.Economy, "ECONOMY", "Set budget posture", "2 CP + monthly",
                "Balanced, Austerity or Expansionary. A standing choice, paid for every month "
                + "it is held: austerity buys solvency with living standards and unrest, "
                + "expansion the reverse.",
                verbs: new[] { nameof(GameController.SetBudgetPosture) });
            Add(Pillar.Economy, "ECONOMY", "Issue sovereign debt", "1 CP",
                "Money now, serviced every month forever, and the standing falls the moment "
                + "you ask. What it costs depends on what lenders already think of us.",
                FiscalSystem.CanIssueDebt(state, state.playerCountryId, out string debtBlock),
                debtBlock,
                verbs: new[] { nameof(GameController.IssueSovereignDebt) });
            Add(Pillar.Economy, "ECONOMY", "Restructure the debt", "4 PC",
                "Write half of it off. Effective, and every state holding our paper remembers "
                + "for five years.",
                player.fiscal.sovereignDebt > 0f, "We carry no debt to restructure.",
                verbs: new[] { nameof(GameController.RestructureDebt) });
            Add(Pillar.Economy, "ECONOMY", "Subsidise a sector", "1 CP + monthly treasury",
                "Hold one sector's functioning up for as long as it is paid for. It fades "
                + "without renewal.",
                player.resources.treasury >= FiscalSystem.SubsidyTreasury,
                "The treasury cannot cover a subsidy.",
                verbs: new[] { nameof(GameController.SubsidiseSector) });
            Add(Pillar.Economy, "ECONOMY", "Build strategic reserves", "1 CP + treasury",
                "Energy, materials or grain put by. Raises the floor a blockade or a sanctions "
                + "regime can grind us down to — and it depletes while doing it.",
                player.resources.treasury
                    >= FiscalSystem.ReserveOrderPoints * FiscalSystem.ReserveCostPerPoint,
                "The treasury cannot cover a reserve order.",
                verbs: new[] { nameof(GameController.BuildReserves),
                               nameof(GameController.ReleaseReserves) });

            Add(Pillar.Economy, "ECONOMY", "Invest in a sector", "2 CP + monthly treasury",
                "Repair, expand or modernise one of the seven sectors. Years of money now for "
                + "capacity later, and the treasury has to carry it every month or the work stops.",
                IndustrialSystem.CanBegin(state, state.playerCountryId, out string industrialBlock),
                industrialBlock,
                verbs: new[] { nameof(GameController.BeginIndustrialProgramme),
                               nameof(GameController.BeginEnergySiteProject) });

            // ---------- intelligence ----------

            Add(Pillar.Intelligence, "INTELLIGENCE", "Establish a network", "2 CP",
                "Collection against one state in one domain. Everything else here needs it first.",
                verbs: new[] { nameof(GameController.EstablishNetwork) });
            Add(Pillar.Intelligence, "INTELLIGENCE", "Expand a network", "1 CP",
                "Deeper penetration, better estimates, more exposure.",
                verbs: new[] { nameof(GameController.ExpandNetwork) });
            Add(Pillar.Intelligence, "INTELLIGENCE", "Set collection focus", "0 CP",
                "Which domain a network reports on.",
                verbs: new[] { nameof(GameController.SetIntelFocus) });
            Add(Pillar.Intelligence, "INTELLIGENCE", "Run a covert operation", "2 CP",
                "Sabotage, influence or theft. Exposure costs standing with everyone.",
                verbs: new[] { nameof(GameController.RunCovertOperation) });
            Add(Pillar.Intelligence, "INTELLIGENCE", "Commission an assessment",
                $"{IntelProductSystem.CommissionCost} CP",
                "Set the service one question about one state — what they are building toward, "
                + "whether they will honour a pact, how they read us. Months of work, and the "
                + "answer carries a confidence grade because it can be wrong.",
                deepNetwork || AnyNetwork(state),
                "No network anywhere. Analysis is a product of collection, not a substitute.",
                verbs: new[] { nameof(GameController.CommissionEstimate) });

            Add(Pillar.Intelligence, "INTELLIGENCE", "Mole hunt",
                $"{IntelligenceSystem.MoleHuntCost} CP",
                "Search our own service for a foreign one. A hunt that finds nothing still "
                + "investigated people: it costs elite cohesion and the standing of whoever it "
                + "fell on, so asking is never free.",
                verbs: new[] { nameof(GameController.MoleHunt) });

            Add(Pillar.Intelligence, "INTELLIGENCE", "Counterintelligence sweep", "1 CP",
                "Harden the state against penetration.",
                verbs: new[] { nameof(GameController.StrengthenCounterIntelligence) });

            Add(Pillar.Intelligence, "INTELLIGENCE", "Approach an official", "1–2 CP",
                "Cultivate, recruit or discredit a named foreign minister. Months of work, and "
                + "who you approach matters more than how hard you press.",
                deepNetwork,
                $"No network is {AgentSystem.MinimumPenetration:F0} deep anywhere — "
                + "a minister cannot be reached through a thin one.",
                verbs: new[] { nameof(GameController.RunAgentOperation) });

            Add(Pillar.Intelligence, "INTELLIGENCE", "Arm a movement", "2 CP + treasury + stocks",
                "Supply an existing rising in somebody else's country. Deniable until it is not, "
                + "and it never hands us the ground — only takes it from them.",
                state.insurgencies.Count > 0,
                "No armed movement is known anywhere. Risings come out of occupation, hardship "
                + "or separation — they cannot be commissioned.",
                verbs: new[] { nameof(GameController.SupportInsurgency),
                               nameof(GameController.WithdrawInsurgencySupport) });

            // ---------- diplomacy ----------

            Add(Pillar.Diplomacy, "DIPLOMACY", "Diplomatic outreach", "1 CP",
                "Improve standing with one state. The groundwork everything else rests on.",
                verbs: new[] { nameof(GameController.DiplomaticOutreach) });
            Add(Pillar.Diplomacy, "DIPLOMACY", "Propose a treaty", "2 CP",
                "Explicit commitments. They accept on their interests, not our wishes.",
                verbs: new[] { nameof(GameController.ProposeTreaty), nameof(GameController.ProposeNegotiatedTreaty) });
            Add(Pillar.Diplomacy, "DIPLOMACY", "Deepen a treaty", "2 CP",
                "Add commitments to a standing agreement. A partner with history signs "
                + "what a stranger would not.",
                verbs: new[] { nameof(GameController.DeepenTreaty) });
            Add(Pillar.Diplomacy, "DIPLOMACY", "Offer supply for a commitment", "2 CP",
                "Open an ordinary supply link on a commodity they are short of — energy, materials "
                + "or food we hold in surplus — in exchange for one commitment they carry for us. "
                + "What the link delivers follows the trade rules and our own stocks; it can be "
                + "changed or withdrawn later through TRADE, and their commitment stands regardless.",
                DiplomaticLeverage.Surpluses(state.PlayerCountry).Count > 0,
                "We hold no energy, materials or food surplus to offer anyone.",
                verbs: new[] { nameof(GameController.OfferSupplyForCommitment) });
            bool anyOwnSanction = false;
            foreach (var sanction in state.sanctions) if (sanction.senderId == state.playerCountryId) { anyOwnSanction = true; break; }
            Add(Pillar.Diplomacy, "DIPLOMACY", "Lift our sanctions for a commitment", "2 CP",
                "Lift the measures we imposed on a government in exchange for one commitment it "
                + "carries for us. The existing 24-month détente then bars new measures from either "
                + "side; a war voids it, and their commitment is a treaty term that outlives it.",
                anyOwnSanction,
                "We have no sanctions of our own in force against anyone.",
                verbs: new[] { nameof(GameController.OfferSanctionsReliefForCommitment) });
            Add(Pillar.Diplomacy, "DIPLOMACY", "Seek sanctions relief", "2 CP",
                "Ask a sender to lift its measures and hold a détente. Fatigue, their own "
                + "blowback and warmth persuade; the threat they still see does not.",
                verbs: new[] { nameof(GameController.SeekSanctionsRelief) });
            // Only offered where there is a state to recognise. A breakaway is
            // rare, and an index entry that is nearly always refused teaches the
            // operator to stop reading the index.
            bool anySuccessor = false;
            foreach (var country in state.countries)
                if (DiplomacySystem.IsSuccessor(state, country)
                    && DiplomacySystem.CanRecognise(state, state.playerCountryId, country.id, out _))
                { anySuccessor = true; break; }

            Add(Pillar.Diplomacy, "DIPLOMACY", "Recognise a state",
                $"{DiplomacySystem.RecogniseCost} CP",
                "Admit a breakaway exists. It buys a grateful new state and an angry old one — "
                + "and withholding is not neutrality, it is a position they notice for as long "
                + "as it lasts.",
                anySuccessor,
                "No state has declared itself that we have not already answered on.",
                verbs: new[] { nameof(GameController.RecogniseState) });
            Add(Pillar.Diplomacy, "DIPLOMACY", "Recognise a state for a commitment", $"{DiplomaticLeverage.OfferCost} CP",
                "Recognise a breakaway in exchange for one commitment it carries for us. Recognition "
                + "lands with every consequence RECOGNISE A STATE has and is never withdrawn; their "
                + "commitment is a treaty term.",
                anySuccessor,
                "No state has declared itself that we have not already answered on.",
                verbs: new[] { nameof(GameController.OfferRecognitionForCommitment) });

            bool anyToMediate = false;
            foreach (var other in state.confrontations)
                if (DiplomacySystem.CanMediate(state, state.playerCountryId, other, out _))
                { anyToMediate = true; break; }

            Add(Pillar.Diplomacy, "DIPLOMACY", "Offer to mediate",
                $"{DiplomacySystem.MediationCost} CP",
                "Bring two other states out of a war we are not in. Both sides have to be "
                + "willing to have us in the room, and being refused is public.",
                anyToMediate,
                "No war we could stand outside of, with both sides willing to have us.",
                verbs: new[] { nameof(GameController.OfferMediation) });

            bool anyTruce = false;
            foreach (var country in state.countries)
                if (DiplomacySystem.CanNormalise(state, state.playerCountryId, country.id, out _))
                { anyTruce = true; break; }

            Add(Pillar.Diplomacy, "DIPLOMACY", "Normalise relations",
                $"{DiplomacySystem.NormalisationCost} CP",
                "Put a war behind us. The only thing that reduces what two countries remember "
                + "about each other — and it is unpopular with the people who did the fighting.",
                anyTruce, "No recent war to put behind us.",
                verbs: new[] { nameof(GameController.BeginNormalisation) });

            Add(Pillar.Diplomacy, "DIPLOMACY", "Post an envoy", "1 INF",
                "Station the foreign minister in one capital. It holds that relationship warm "
                + "without a Command Point every month, and it is worth exactly what they are "
                + "worth — a weak appointment posted abroad is close to nobody being there.",
                player.FindOfficial(Pillar.Diplomacy) != null,
                "There is no foreign minister to post.",
                verbs: new[] { nameof(GameController.AssignEnvoy) });

            Add(Pillar.Diplomacy, "DIPLOMACY", "Convene a summit",
                $"{DiplomacySystem.SummitCost} CP + {DiplomacySystem.SummitPreparation} months",
                "Announce talks. The months are the point: it is judged on the relationship as "
                + "it stands when it meets, so a summit called in a warm month and met in a "
                + "cold one produces a communiqué and a public failure.",
                verbs: new[] { nameof(GameController.ConveneSummit) });

            Add(Pillar.Diplomacy, "DIPLOMACY", "Break a treaty", "1 CP",
                "Immediate freedom, lasting reputational damage with everyone watching.",
                verbs: new[] { nameof(GameController.BreakTreaty) });
            Add(Pillar.Diplomacy, "DIPLOMACY", "Assemble a coalition", "3 CP",
                "Recruit partners into a confrontation. They join on their own reasoning.",
                confrontation != null, "Requires an active confrontation.",
                verbs: new[] { nameof(GameController.RequestCoalition) });

            Add(Pillar.Diplomacy, "DIPLOMACY", "Open an accession effort", "3 CP",
                "Absorb a state that trusts us, by its establishment or by its people. Needs deep "
                + "trust, heavy dependence and something wrong with them — and it takes years.",
                verbs: new[] { nameof(GameController.BeginAccession),
                               nameof(GameController.AbandonAccession) });

            Add(Pillar.Diplomacy, "DIPLOMACY", "Found a bloc", "3 CP + monthly PC",
                "A standing side with a name, rather than a coalition raised for one war. "
                + "Members vote together in the chamber; holding it together is a monthly bill.",
                BlocSystem.CanFound(state, state.playerCountryId, out string blocBlock),
                blocBlock,
                verbs: new[] { nameof(GameController.FoundBloc) });
            Add(Pillar.Diplomacy, "DIPLOMACY", "Bring a state into the bloc", "2 CP",
                "They accept on their own interests — warmth, trust, dependence, and how "
                + "frightened of us they are.",
                BlocSystem.BlocOf(state, state.playerCountryId) != null,
                "We do not lead a bloc.",
                verbs: new[] { nameof(GameController.InviteToBloc),
                               nameof(GameController.LeaveBloc) });
            Add(Pillar.Diplomacy, "DIPLOMACY", "Put a motion to the chamber", "2 CP",
                "Condemnation, authorised measures or relief. States vote their own interests, "
                + "a permanent member can block anything, and losing a vote you called is public.",
                CouncilSystem.CanRaise(state, state.playerCountryId, out string chamberBlock),
                chamberBlock,
                verbs: new[] { nameof(GameController.RaiseCouncilMotion) });

            // ---------- government ----------

            Add(Pillar.Government, "GOVERNMENT", "Public messaging", "2 PC",
                "Approval and unity. The cheap instrument, and the one that fixes mood not machinery.",
                verbs: new[] { nameof(GameController.PublicMessaging) });
            Add(Pillar.Government, "GOVERNMENT", "Institutional reform", "6 PC",
                "Builds the state's capacity and costs the goodwill of whoever benefits from the status quo.",
                verbs: new[] { nameof(GameController.InstitutionalReform) });
            Add(Pillar.Government, "GOVERNMENT", "Declare emergency powers", "PC + approval",
                "Extra command capacity, bought with legitimacy. Cheaper in centralized systems.",
                verbs: new[] { nameof(GameController.DeclareEmergencyPowers) });
            Add(Pillar.Government, "GOVERNMENT", "Set national priority", "3 PC",
                "Redirects every delegated official. The broadest lever available.",
                verbs: new[] { nameof(GameController.SetNationalPriority) });
            Add(Pillar.Government, "GOVERNMENT",
                player.government.IsElective ? "Bargain with the chamber" : "Accommodate the elite", "2 PC",
                "Support bought rather than earned. It decays, so it has to be kept up.",
                verbs: new[] { nameof(GameController.BuildPoliticalSupport) });
            Add(Pillar.Government, "GOVERNMENT", "Court a bloc", "2 PC",
                "Bargain with one named part of the coalition rather than with the chamber in "
                + "general. Worth more than an undirected approach, because each bloc wants a "
                + "different thing and only one of them wants what you are offering.",
                verbs: new[] { nameof(GameController.CourtFaction), nameof(GameController.CourtFactionAtIndex) });

            Add(Pillar.Government, "GOVERNMENT", "Distribute patronage", "1 PC + treasury",
                "The same support, bought with money instead of standing — and it hollows the state.",
                player.resources.treasury >= GovernmentSystem.PatronageTreasury,
                "The treasury cannot cover it.",
                verbs: new[] { nameof(GameController.DistributePatronage) });
            Add(Pillar.Government, "GOVERNMENT", "Public inquiry", "4 PC",
                "Raises the weakest minister and the machinery around them. Nobody involved is grateful.",
                verbs: new[] { nameof(GameController.LaunchInquiry) });
            Add(Pillar.Government, "GOVERNMENT", "Prepare a successor", "3 PC",
                "A transition you saw coming. Raises who arrives next and keeps the handover orderly.",
                player.government.successorReadiness < 99f, "Continuity planning is already complete.",
                verbs: new[] { nameof(GameController.GroomSuccessor) });
            Add(Pillar.Government, "GOVERNMENT", "Set civic posture", "3 PC",
                "Open or restrictive. Order against legitimacy, and it decides how fast plots form.",
                verbs: new[] { nameof(GameController.SetCivicPosture) });
            Add(Pillar.Government, "GOVERNMENT", "Change the constitution",
                $"{GovernmentSystem.ConstitutionalOpeningCost:F0} PC + "
                + $"{GovernmentSystem.ConstitutionalUpkeep:F1} PC/mo for "
                + $"{GovernmentSystem.ConstitutionalMonths} months",
                "Become a different kind of state. It changes how power works here — succession, "
                + "term limits, what emergency powers cost — and it can fail, publicly, after "
                + "years of paying for it.",
                !player.government.ChangingConstitution,
                $"A constitutional process is already under way "
                + $"({player.government.constitutionalMonthsRemaining} month(s)).",
                verbs: new[] { nameof(GameController.BeginConstitutionalChange) });

            Add(Pillar.Government, "GOVERNMENT", "Consolidate authority", "12 PC",
                "Permanently make one pillar the operator's to command. The one large purchase.",
                verbs: new[] { nameof(GameController.ConsolidateAuthority) });
            Add(Pillar.Government, "GOVERNMENT", "Dismiss an official", "PC",
                "Replace a minister. Costs more in elective systems.",
                verbs: new[] { nameof(GameController.DismissOfficial) });
            Add(Pillar.Government, "GOVERNMENT", "Call an early election", "PC",
                "Parliamentary systems only. A gamble on current standing.",
                player.government.AllowsEarlyElection, "Only a parliamentary system can go to the country early.",
                verbs: new[] { nameof(GameController.CallEarlyElection) });
            Add(Pillar.Government, "GOVERNMENT",
                player.displacement.bordersClosed ? "Open the border" : "Close the border",
                "2 PC + standing",
                "Whether we take people displaced from elsewhere. Carrying them costs money "
                + "and is argued about; refusing them costs standing with every state that "
                + "is still carrying them, and leaves the pressure next door.",
                verbs: new[] { nameof(GameController.SetBorderPolicy) });
            Add(Pillar.Government, "GOVERNMENT", "Concede to the opposition", "2 PC + a real cost",
                "Move toward them on what they are campaigning about. Always works, and what "
                + "it costs depends on what you conceded — money, war support, allies or the "
                + "civic posture itself.",
                player.government.oppositionCase >= OppositionSystem.NoiseFloor,
                "Nobody is making a serious case against this government.",
                verbs: new[] { nameof(GameController.ConcedeToOpposition) });
            Add(Pillar.Government, "GOVERNMENT", "Confront the opposition", "3 PC",
                "Answer them in public. Effective where the case is mood; where it is hardship "
                + "or a war, denying it makes the argument stronger.",
                player.government.oppositionCase >= OppositionSystem.NoiseFloor,
                "Nobody is making a serious case against this government.",
                verbs: new[] { nameof(GameController.ConfrontOpposition) });
            Add(Pillar.Government, "GOVERNMENT", "Secure the army's loyalty", "5 PC",
                "Promotions, budgets and patronage where they will do the most good. Effective, "
                + "and quietly corrosive.",
                verbs: new[] { nameof(GameController.SecureMilitaryLoyalty) });
            Add(Pillar.Government, "CABINET", "Appoint to a vacancy", "0 CP",
                "Choose from the shortlist. The government appoints for you after three months.",
                player.vacancies.Count > 0, "No office is currently vacant.",
                verbs: new[] { nameof(GameController.AppointOfficial) });
            Add(Pillar.Government, "CABINET", "Issue a directive", "1 INF",
                "Tell a Directed official what to work on. Every directive changes something "
                + "an autonomous month would not have.",
                verbs: new[] { nameof(GameController.SetDirective) });
            Add(Pillar.Government, "CABINET", "Take direct action", "CP + their trust",
                "Run a pillar yourself for a month. It removes the intermediary — and the "
                + "filter, so you see what their desk would have buried.",
                verbs: new[] { nameof(GameController.ExecuteDirectAction) });
            Add(Pillar.Government, "CABINET", "Set a control mode", "0–1 INF",
                "Autonomous, Directed or Direct Control. Direct Control needs constitutional authority.",
                verbs: new[] { nameof(GameController.SetControlMode) });

            // ---------- long game ----------

            Add(Pillar.Economy, "RESEARCH", "Authorize research", "2 CP + treasury",
                "Multi-year programmes across all five pillars. Capabilities unlock ability, never force.",
                verbs: new[] { nameof(GameController.BeginResearch) });
            Add(Pillar.Military, "ENDGAME", "Prepare an instrument", "2 CP + treasury",
                "Months of preparation toward one decisive capability. Visible to anyone collecting on us.",
                verbs: new[] { nameof(GameController.PrepareEndgame) });
            Add(Pillar.Military, "ENDGAME", "Execute an instrument", "4 CP",
                "Only when prepared, and only against a state we are confronting.",
                verbs: new[] { nameof(GameController.ExecuteEndgame) });
            Add(Pillar.Government, "BRIEFING", "Hold for a stretch",
                $"Up to {HoldSystem.MaxMonths} months of capacity",
                "Stand back through a quiet run of months. It stops the moment anything needs "
                + "deciding, and the command capacity of the months it uses is forgone — a "
                + "held year grades like a passive one.",
                HoldSystem.CanHold(state, out string holdBlock), holdBlock,
                verbs: new[] { nameof(GameController.Hold) });
            Add(Pillar.Government, "OPERATOR", "Unlock a skill", "Skill points",
                "Operator capability only — command capacity, action costs, precision. Never national power.",
                state.skillPoints > 0, "No skill points available.",
                verbs: new[] { nameof(GameController.UnlockSkill) });

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
