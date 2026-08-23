using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// What kind of situation an event is. Not flavour: it decides what a calm
    /// world is allowed to produce.
    ///
    /// A stable, prosperous, scientifically capable country should still make a
    /// discovery or find a mineral deposit — those arrive *because* things are
    /// going well, not despite it. Adversity is the thing a settled world must
    /// not manufacture, and conflating the two meant the invariant guarding
    /// against invented crises could only be satisfied by a world where nothing
    /// good ever happened either.
    /// </summary>
    public enum EventNature
    {
        /// <summary>Something has gone wrong. Requires a troubled world.</summary>
        Adversity = 0,

        /// <summary>Something has gone right. A calm world may produce these.</summary>
        Opportunity = 1
    }

    /// <summary>
    /// One authored situation with systemic eligibility (GDD §23).
    /// Simulation conditions decide *whether* it can happen and how likely;
    /// the authored content decides how good it is when it does.
    /// </summary>
    public class EventDefinition
    {
        public string id;
        public string title;

        /// <summary>Adversity unless stated otherwise — the safe default.</summary>
        public EventNature nature = EventNature.Adversity;

        /// <summary>Situation text. Receives the state so it can name real countries.</summary>
        public Func<GameState, string> body;

        /// <summary>False when the world cannot currently produce this situation.</summary>
        public Func<GameState, bool> isEligible;

        /// <summary>Relative likelihood among eligible events. Higher = more likely.</summary>
        public Func<GameState, float> weight;

        /// <summary>Months before this definition can fire again.</summary>
        public int cooldownMonths = 24;

        /// <summary>
        /// The state this situation is about, resolved once when it fires. The
        /// options close over the same value, so the crisis names one country
        /// and acts on that same country however long the operator deliberates.
        /// </summary>
        public Func<GameState, string> subject;

        /// <summary>
        /// What the world does if nobody decides (GDD §23). Drifting cost only
        /// standing before this, which made ignoring a crisis the cheapest way
        /// to dodge its consequences.
        /// </summary>
        public string lapseEffectId = "";

        /// <summary>Whether the lapse effect acts on <see cref="subject"/>.</summary>
        public bool lapseTargetUsesSubject;

        public float lapseMagnitude;

        public Func<GameState, List<CrisisOption>> options;
    }

    /// <summary>
    /// The authored event catalog. Every entry states the conditions under which
    /// it becomes possible, so events arise from the world state rather than
    /// feeling like disconnected random cards (GDD §23).
    /// </summary>
    public static class EventCatalog
    {
        static List<EventDefinition> definitions;

        public static IReadOnlyList<EventDefinition> Definitions => definitions ?? (definitions = Build());

        public static EventDefinition Find(string id)
        {
            foreach (var definition in Definitions)
                if (definition.id == id) return definition;
            return null;
        }

        // ---------- helpers ----------

        static CrisisOption Option(string label, string description, string resultText,
            float treasury = 0, float stability = 0, float approval = 0, float unity = 0,
            string effect = "", string target = "", float magnitude = 0)
        {
            return new CrisisOption
            {
                label = label,
                description = description,
                resultText = resultText,
                treasuryDelta = treasury,
                stabilityDelta = stability,
                approvalDelta = approval,
                unityDelta = unity,
                effectId = effect,
                effectTargetId = target ?? "",
                effectMagnitude = magnitude
            };
        }

        /// <summary>Id of the coldest rival, or empty. Captured at fire time.</summary>
        static string ColdestRivalId(GameState state) => ColdestRival(state)?.id ?? "";

        /// <summary>The partner we trade most heavily with.</summary>
        static string LargestTradePartnerId(GameState state)
        {
            string best = "";
            float most = 0f;
            foreach (var link in state.trade)
            {
                if (!link.Involves(state.playerCountryId)) continue;
                if (link.volume <= most) continue;
                most = link.volume;
                best = link.PartnerOf(state.playerCountryId);
            }
            return best;
        }

        /// <summary>
        /// Whether our coldest relationship is genuinely cold, rather than merely
        /// the coolest among warm ones.
        ///
        /// <see cref="ColdestRival"/> returns the minimum of a list, so it is
        /// non-null in every world that contains another country. Any eligibility
        /// written as `ColdestRival(s) != null` is therefore a constant `true`
        /// wearing the costume of a condition, and the situation it guards fires
        /// in a world at peace with everyone.
        /// </summary>
        static bool IsActuallyCold(GameState state, float threshold)
        {
            var rival = ColdestRival(state);
            if (rival == null) return false;

            var relationship = state.FindRelationship(state.playerCountryId, rival.id);
            return relationship != null && relationship.relations < threshold;
        }

        /// <summary>The non-player country with the coldest relations toward us.</summary>
        static CountryState ColdestRival(GameState state)
        {
            CountryState coldest = null;
            float lowest = float.MaxValue;
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var relationship = state.FindRelationship(state.playerCountryId, country.id);
                if (relationship == null) continue;
                if (relationship.relations < lowest) { lowest = relationship.relations; coldest = country; }
            }
            return coldest;
        }

        static bool AnyNeighborAtAlert(GameState state)
        {
            foreach (var country in state.countries)
                if (!country.isPlayer && country.military.alertPosture) return true;
            return false;
        }

        static bool AnyTradePartnerDisrupted(GameState state)
        {
            foreach (var link in state.trade)
            {
                if (!link.Involves(state.playerCountryId)) continue;
                string partnerId = link.PartnerOf(state.playerCountryId);
                if (link.embargoed || state.IsAtWar(partnerId)) return true;
            }
            return false;
        }

        static bool AnyNetworkCompromised(GameState state)
        {
            foreach (var network in state.networks)
                if (network.ownerId == state.playerCountryId && network.compromised) return true;
            return false;
        }

        static Official LeastTrustingOfficial(GameState state)
        {
            Official worst = null;
            foreach (var official in state.cabinet)
                if (worst == null || official.trust < worst.trust) worst = official;
            return worst;
        }

        // ---------- catalog ----------

        static List<EventDefinition> Build()
        {
            var list = new List<EventDefinition>();

            list.Add(new EventDefinition
            {
                id = "BORDER_INCIDENT",
                title = "BORDER INCIDENT",
                cooldownMonths = 18,
                isEligible = s => AnyNeighborAtAlert(s) ||
                                  (ColdestRival(s) != null &&
                                   s.FindRelationship(s.playerCountryId, ColdestRival(s).id).relations < 40f),
                weight = s => AnyNeighborAtAlert(s) ? 2.5f : 1f,
                body = s => $"Exchange of fire reported at a contested frontier post with " +
                            $"{ColdestRival(s)?.displayName}. Casualties unconfirmed. Foreign observers " +
                            "are watching the response.",
                subject = ColdestRivalId,
                // Nobody answers a frontier incident, so the frontier answers
                // for us: they read hesitation and come to the same conclusion
                // they were already leaning toward.
                lapseEffectId = CrisisEffects.SufferConfrontation,
                lapseTargetUsesSubject = true,
                options = s => new List<CrisisOption>
                {
                    Option("MOBILIZE RESPONSE FORCE", "Show of strength. Costly; steadies the public — " +
                        "and they will read it as one.",
                        "Response force deployed to the frontier. Position held.",
                        treasury: -60, stability: 2, approval: 3,
                        effect: CrisisEffects.Threat, target: ColdestRivalId(s), magnitude: 12),
                    Option("FILE DIPLOMATIC PROTEST", "Measured. Cheap, reads as hesitation at home, " +
                        "and keeps the channel open.",
                        "Formal protest delivered. Frontier remains tense.", approval: -2,
                        effect: CrisisEffects.Relations, target: ColdestRivalId(s), magnitude: -4),
                    Option("SUPPRESS THE REPORT", "No public reaction. Risky if the story leaks.",
                        "Incident buried. Rumors circulate in border districts.",
                        approval: -4, stability: -3, unity: -2,
                        effect: CrisisEffects.Conspiracy, magnitude: 6)
                }
            });

            list.Add(new EventDefinition
            {
                id = "MARKET_PANIC",
                title = "MARKET PANIC",
                cooldownMonths = 20,
                isEligible = s => s.PlayerCountry.economy.confidence < 58f
                                  || s.PlayerCountry.economy.debtToGdp > 85f,
                weight = s => s.PlayerCountry.economy.confidence < 45f ? 2.5f : 1.2f,
                body = s => "Sharp sell-off on the national exchange after rumors of bank exposure. " +
                            "Confidence is deteriorating by the hour.",
                // A panic nobody answers is a panic that runs.
                lapseEffectId = CrisisEffects.MarketShock,
                lapseMagnitude = -14,
                options = s => new List<CrisisOption>
                {
                    Option("EMERGENCY LIQUIDITY INJECTION", "Expensive intervention; halts the slide " +
                        "and restores confidence.",
                        "Liquidity injected. Markets stabilize at a cost.", treasury: -120, approval: 2,
                        effect: CrisisEffects.MarketShock, magnitude: 8),
                    Option("LET MARKETS CORRECT", "Preserve the treasury; absorb the political damage " +
                        "and the hit to confidence.",
                        "No intervention. Savers take losses; anger builds.", approval: -4, stability: -2,
                        effect: CrisisEffects.MarketShock, magnitude: -10)
                }
            });

            list.Add(new EventDefinition
            {
                id = "FOOD_SHORTAGE",
                title = "REGIONAL FOOD SHORTAGE",
                cooldownMonths = 24,
                isEligible = s => s.PlayerCountry.resources.foodSecurity < 65f,
                weight = s => (70f - s.PlayerCountry.resources.foodSecurity) / 20f,
                body = s => "Distribution failure has emptied shelves in two provinces. " +
                            "Queues are forming; local officials request direction.",
                options = s => new List<CrisisOption>
                {
                    Option("EMERGENCY IMPORTS", "Buy relief abroad. Drains treasury.",
                        "Grain purchased on the spot market. Shelves refill.", treasury: -80, approval: 1),
                    Option("REQUISITION RESERVES", "Redirect strategic stockpiles. Free, but unpopular in the capital.",
                        "Reserves released. Stockpile levels criticized in the assembly.",
                        approval: -1, stability: 1),
                    Option("LOCAL AUTHORITIES HANDLE IT", "Delegate downward. Unrest likely.",
                        "Provinces left to cope. Scattered protests reported.",
                        approval: -3, stability: -2, unity: -1)
                }
            });

            list.Add(new EventDefinition
            {
                id = "INFLATION_PROTESTS",
                title = "PROTESTS OVER LIVING COSTS",
                cooldownMonths = 15,
                isEligible = s => s.PlayerCountry.economy.inflation > 7f
                                  && s.PlayerCountry.governmentApproval < 60f,
                weight = s => s.PlayerCountry.economy.inflation / 5f,
                body = s => $"Crowds have gathered in the major cities over prices. Inflation stands at " +
                            $"{s.PlayerCountry.economy.inflation:F1}%. Organizers are not yet coordinated.",
                options = s => new List<CrisisOption>
                {
                    Option("SUBSIDIZE ESSENTIALS", "Direct relief. Expensive and inflationary in itself.",
                        "Subsidies announced. The streets quiet for now.",
                        treasury: -100, approval: 5, stability: 2),
                    Option("MEET THE ORGANIZERS", "Negotiate rather than compel.",
                        "Talks opened. The government looks responsive, if weak.",
                        approval: 2, unity: 2, stability: -1),
                    Option("ENFORCE PUBLIC ORDER", "Clear the squares. Order restored, resentment retained.",
                        "Order restored. The grievance has not gone anywhere.",
                        stability: 3, approval: -6, unity: -4)
                }
            });

            list.Add(new EventDefinition
            {
                id = "ENERGY_CRISIS",
                title = "ENERGY SUPPLY CRISIS",
                cooldownMonths = 24,
                isEligible = s => s.PlayerCountry.resources.energy < 50f,
                weight = s => (55f - s.PlayerCountry.resources.energy) / 15f,
                body = s => "Reserves have fallen below the winter planning threshold. Industry is " +
                            "already rationing; households will notice within weeks.",
                subject = LargestTradePartnerId,
                lapseEffectId = CrisisEffects.MarketShock,
                lapseMagnitude = -9,
                options = s => new List<CrisisOption>
                {
                    Option("PURCHASE AT MARKET PRICE", "Pay whatever it costs. The lights stay on.",
                        "Emergency cargoes secured at punishing prices.", treasury: -140, approval: 2),
                    Option("RATION INDUSTRIAL SUPPLY", "Protect households; the economy takes the hit.",
                        "Industrial allocation cut. Output will suffer.", approval: -2, stability: -1,
                        effect: CrisisEffects.MarketShock, magnitude: -6),
                    Option("APPROACH A SUPPLIER STATE", "Ask for terms. They will remember the asking, " +
                        "and the asking is worth something to them.",
                        "Supply agreement reached. We are more dependent than we were.",
                        treasury: -40, approval: 1,
                        effect: CrisisEffects.TradeShock, target: LargestTradePartnerId(s), magnitude: 9)
                }
            });

            list.Add(new EventDefinition
            {
                id = "INTELLIGENCE_SCANDAL",
                title = "SERVICE EXPOSED ABROAD",
                cooldownMonths = 20,
                isEligible = AnyNetworkCompromised,
                weight = s => 1.8f,
                body = s => "A rolled-up network has become public. The foreign press has names. " +
                            "Our own legislature is asking what was authorized, and by whom.",
                subject = ColdestRivalId,
                // Left unanswered, the story writes itself abroad.
                lapseEffectId = CrisisEffects.Relations,
                lapseTargetUsesSubject = true,
                lapseMagnitude = -8,
                options = s => new List<CrisisOption>
                {
                    Option("ACCEPT RESPONSIBILITY", "Take the hit publicly; protect the service. " +
                        "Costly abroad as well as at home.",
                        "Responsibility accepted at the top. The service survives intact.",
                        approval: -5, stability: 1,
                        effect: CrisisEffects.Relations, target: ColdestRivalId(s), magnitude: -6),
                    Option("DISAVOW THE OPERATION", "Deny authorization. The officers carry it, and " +
                        "the network with them.",
                        "Operation disavowed. Morale in the service is badly damaged.",
                        approval: -1, unity: -3,
                        effect: CrisisEffects.ExposeNetwork),
                    Option("ORDER AN INQUIRY", "Buy time and look accountable.",
                        "Inquiry announced. The story will run for months.",
                        approval: -2, stability: -1)
                }
            });

            list.Add(new EventDefinition
            {
                id = "SUPPLY_DISRUPTION",
                title = "SUPPLY CHAIN DISRUPTION",
                cooldownMonths = 18,
                isEligible = AnyTradePartnerDisrupted,
                weight = s => 1.6f,
                body = s => "A disrupted trading partner has stopped shipments. Manufacturers report " +
                            "they can maintain output for six weeks, then not.",
                lapseEffectId = CrisisEffects.TradeShock,
                lapseMagnitude = -8,
                options = s => new List<CrisisOption>
                {
                    Option("SUBSIDIZE ALTERNATIVE SOURCING", "Pay to re-route. Fast, expensive, and it " +
                        "rebuilds the volumes we lost.",
                        "Alternative suppliers contracted at a premium.", treasury: -90, stability: 1,
                        effect: CrisisEffects.TradeShock, magnitude: 6),
                    Option("DRAW DOWN STRATEGIC STOCKS", "Use the reserve we built for exactly this.",
                        "Stockpiles released. The buffer is thinner now.", approval: 1),
                    Option("LET INDUSTRY ABSORB IT", "No intervention. Some firms will not survive, and " +
                        "the trade they carried goes with them.",
                        "No support offered. Layoffs announced in affected sectors.",
                        approval: -4, unity: -2,
                        effect: CrisisEffects.TradeShock, magnitude: -6)
                }
            });

            list.Add(new EventDefinition
            {
                id = "CABINET_DISSENT",
                title = "CABINET DISSENT",
                cooldownMonths = 18,
                isEligible = s =>
                {
                    var official = LeastTrustingOfficial(s);
                    return official != null && official.trust < 42f;
                },
                weight = s => 1.4f,
                body = s =>
                {
                    var official = LeastTrustingOfficial(s);
                    return $"{official?.displayName}, {official?.title}, has briefed against the current " +
                           "direction in private and is being quoted anonymously in the press.";
                },
                options = s => new List<CrisisOption>
                {
                    Option("BRING THEM BACK IN", "Concede something. Keep the Cabinet whole.",
                        "A private accommodation is reached. The leaks stop.",
                        approval: -1, unity: 2, stability: 1),
                    Option("ISOLATE THEM", "Freeze them out of the decisions that matter. A resentful " +
                        "minister with nothing to lose is how plots start.",
                        "Their access is quietly reduced. The resentment is not.",
                        stability: -2, unity: -2,
                        effect: CrisisEffects.Conspiracy, magnitude: 9),
                    Option("PUBLIC REBUKE", "Assert authority. Everyone else is watching.",
                        "The rebuke lands. The Cabinet is disciplined and unhappy.",
                        approval: 2, unity: -3, stability: 1)
                }
            });

            list.Add(new EventDefinition
            {
                id = "WAR_WEARINESS",
                title = "WAR WEARINESS",
                cooldownMonths = 12,
                isEligible = s => s.PlayerCountry.warExhaustion > 40f && s.IsAtWar(s.playerCountryId),
                weight = s => s.PlayerCountry.warExhaustion / 25f,
                body = s => "Casualty notices, rationing and no visible end. Veterans' associations have " +
                            "joined the demonstrations. The question being asked is what this is for.",
                // Nobody speaks for the war, so the country stops believing in it.
                lapseEffectId = CrisisEffects.WarSupport,
                lapseMagnitude = -12,
                options = s => new List<CrisisOption>
                {
                    Option("ADDRESS THE NATION", "Make the case again, honestly. It restores the will " +
                        "to keep going.",
                        "The address is well received. It buys time, not consent.",
                        approval: 4, unity: 3,
                        effect: CrisisEffects.WarSupport, magnitude: 9),
                    Option("EXPAND VETERANS' SUPPORT", "Spend on those who carried it.",
                        "Support package announced. Genuinely welcomed.",
                        treasury: -110, approval: 3, unity: 4,
                        effect: CrisisEffects.WarSupport, magnitude: 5),
                    Option("RESTRICT COVERAGE", "Control the images. Control the mood — and hollow out " +
                        "whatever conviction is left underneath it.",
                        "Coverage restricted. The mood is managed, not improved.",
                        stability: 2, approval: -5, unity: -4,
                        effect: CrisisEffects.WarSupport, magnitude: -7)
                }
            });

            // ---------- events that change the world, not just the numbers ----------

            list.Add(new EventDefinition
            {
                id = "DEFECTION",
                title = "DEFECTION",
                cooldownMonths = 30,
                isEligible = s => HasNetworkAgainstUs(s),
                weight = s => 1.3f,
                body = s => "A serving officer of a foreign service has walked in and offered to " +
                            "co-operate. The material is genuine. Their motives are not entirely clear.",
                subject = ColdestRivalId,
                options = s => new List<CrisisOption>
                {
                    Option("ACCEPT AND EXPLOIT", "Take the material and use it. It will be noticed, and " +
                        "they will conclude we are worth watching.",
                        "The defector is resettled. Their former service is already hunting the leak.",
                        approval: 1, stability: -1,
                        effect: CrisisEffects.Threat, target: ColdestRivalId(s), magnitude: 10),
                    Option("ACCEPT QUIETLY", "Take them in without acknowledgement.",
                        "Handled discreetly. The gain is smaller and so is the exposure."),
                    Option("REFUSE", "Not worth the diplomatic damage — and worth something to the " +
                        "service we hand them back to.",
                        "The offer was declined. Somebody else will take it.",
                        approval: -1,
                        effect: CrisisEffects.Relations, target: ColdestRivalId(s), magnitude: 6)
                }
            });

            list.Add(new EventDefinition
            {
                id = "CHOKEPOINT_INCIDENT",
                title = "CHOKEPOINT INCIDENT",
                cooldownMonths = 24,
                isEligible = s => AnyForeignChokepoint(s),
                weight = s => 1.5f,
                body = s =>
                {
                    var choke = FirstForeignChokepoint(s);
                    return $"Shipping has been detained at {choke?.displayName}. The stated reason is " +
                           "paperwork. Our carriers are re-routing at cost, and everyone is watching " +
                           "how we respond.";
                },
                subject = s => FirstForeignChokepoint(s)?.ownerId ?? "",
                // The one authored path from a crisis straight into a war. Left
                // alone, a state that can close a strait to us and meet no answer
                // concludes it can do rather more than that.
                lapseEffectId = CrisisEffects.SufferConfrontation,
                lapseTargetUsesSubject = true,
                options = s => new List<CrisisOption>
                {
                    Option("SEND AN ESCORT", "A naval presence. Unambiguous, expensive, and they will " +
                        "treat it as what it is.",
                        "Escorts dispatched. The detentions stopped; the resentment did not.",
                        treasury: -110, approval: 3, stability: 1,
                        effect: CrisisEffects.Threat,
                        target: FirstForeignChokepoint(s)?.ownerId ?? "", magnitude: 16),
                    Option("NEGOTIATE TRANSIT TERMS", "Buy passage. Cheaper than a warship, and it " +
                        "keeps the relationship.",
                        "Transit arrangements agreed. We are paying for a right we used to assume.",
                        treasury: -60, approval: -1,
                        effect: CrisisEffects.Relations,
                        target: FirstForeignChokepoint(s)?.ownerId ?? "", magnitude: 5),
                    Option("FORCE THE STRAIT", "Sail through regardless. This becomes a confrontation " +
                        "the moment we do it, and everyone knows that before we start.",
                        "The passage was forced. We are now in an open confrontation over it.",
                        approval: 5, unity: 3,
                        effect: CrisisEffects.OpenConfrontation,
                        target: FirstForeignChokepoint(s)?.ownerId ?? ""),
                    Option("ACCEPT THE DELAYS", "Absorb it. This is not the fight to pick — and the " +
                        "trade it costs us is real.",
                        "Shipping re-routed at commercial cost. The precedent is set.",
                        approval: -3, unity: -1,
                        effect: CrisisEffects.TradeShock, magnitude: -7)
                }
            });

            list.Add(new EventDefinition
            {
                id = "FOREIGN_COUP_FALLOUT",
                title = "GOVERNMENT FALLS ABROAD",
                cooldownMonths = 18,
                isEligible = s => AnyForeignCoupRecently(s),
                weight = s => 2f,
                body = s => "A government we dealt with has been removed by force. Agreements signed " +
                            "with the old order are now worth exactly what the new one says they are.",
                subject = s => RecentlyCouped(s)?.id ?? "",
                options = s => new List<CrisisOption>
                {
                    Option("RECOGNIZE IMMEDIATELY", "Deal with whoever holds the country. They will " +
                        "remember who was first.",
                        "Recognition extended. Pragmatic, and noted by everyone who values legitimacy.",
                        approval: -2, stability: 1,
                        effect: CrisisEffects.Relations, target: RecentlyCouped(s)?.id ?? "", magnitude: 14),
                    Option("WITHHOLD RECOGNITION", "Make them earn it. They will remember that too.",
                        "Recognition withheld. Our access is reduced; our position is consistent.",
                        approval: 3, unity: 2,
                        effect: CrisisEffects.Relations, target: RecentlyCouped(s)?.id ?? "", magnitude: -12),
                    Option("QUIETLY OPEN A CHANNEL", "No public position; keep the line open.",
                        "A private channel is established. Nobody has to say anything out loud.",
                        effect: CrisisEffects.Trust, target: RecentlyCouped(s)?.id ?? "", magnitude: 6)
                }
            });

            list.Add(new EventDefinition
            {
                id = "TREATY_PRESSURE",
                title = "PARTNER SEEKS ASSURANCES",
                cooldownMonths = 20,
                isEligible = s => HasTreatyPartner(s),
                weight = s => 1.2f,
                body = s =>
                {
                    var partner = FirstTreatyPartner(s);
                    return $"{partner?.displayName} has asked, privately, whether our commitments still " +
                           "mean what they meant when they were signed. They would like something " +
                           "public.";
                },
                subject = s => FirstTreatyPartner(s)?.id ?? "",
                // A partner who asked and got nothing draws the obvious conclusion.
                lapseEffectId = CrisisEffects.Trust,
                lapseTargetUsesSubject = true,
                lapseMagnitude = -12,
                options = s => new List<CrisisOption>
                {
                    Option("REAFFIRM PUBLICLY", "Say it out loud. Binding, visible to rivals, and worth " +
                        "a great deal to the partner who asked.",
                        "The commitment was restated in public. Our partner is reassured; others took note.",
                        approval: -1, stability: 1,
                        effect: CrisisEffects.Trust, target: FirstTreatyPartner(s)?.id ?? "", magnitude: 12),
                    Option("REASSURE PRIVATELY", "Warm words, no new obligation.",
                        "Private assurances given. Adequate for now.",
                        effect: CrisisEffects.Trust, target: FirstTreatyPartner(s)?.id ?? "", magnitude: 4),
                    Option("DECLINE TO ELABORATE", "Strategic ambiguity has its uses. Being trusted is " +
                        "not one of them.",
                        "No further assurance offered. The doubt remains, and grows.",
                        approval: 1, unity: -1,
                        effect: CrisisEffects.Trust, target: FirstTreatyPartner(s)?.id ?? "", magnitude: -9)
                }
            });

            list.Add(new EventDefinition
            {
                id = "INDUSTRIAL_ACCIDENT",
                title = "MAJOR INDUSTRIAL ACCIDENT",
                cooldownMonths = 30,
                // Accidents follow strain and neglect, not the mere existence of
                // industry — a well-maintained sector is not a crisis waiting.
                isEligible = s =>
                {
                    if (s.PlayerCountry.resources.industrialCapacity <= 50f) return false;
                    var industry = s.PlayerCountry.economy.GetSector(EconomicSector.Industry);
                    return industry != null && industry.health < 82f;
                },
                weight = s => 0.9f,
                body = s => "A failure at a major installation has killed workers and stopped output. " +
                            "The inspection regime is being questioned in public.",
                options = s => new List<CrisisOption>
                {
                    Option("FULL PUBLIC INQUIRY", "Transparency. Slow, expensive, and correct.",
                        "An inquiry is opened. Production stays down longer; trust is preserved.",
                        treasury: -70, approval: 2, unity: 2),
                    Option("RESTART UNDER REVIEW", "Get output back and investigate alongside.",
                        "Production resumed. The families are not satisfied.",
                        approval: -3, unity: -2),
                    Option("BLAME THE OPERATOR", "Contain it to one company.",
                        "Liability assigned. The regulator's credibility is not improved.",
                        approval: -1, stability: -2)
                }
            });

            list.Add(new EventDefinition
            {
                id = "REFUGEE_PRESSURE",
                title = "DISPLACEMENT AT THE BORDER",
                cooldownMonths = 24,
                isEligible = s => AnyForeignInstability(s),
                weight = s => 1.4f,
                body = s => "Conflict and collapse elsewhere have pushed people to our frontier in " +
                            "numbers the border authorities cannot process. The cameras have arrived.",
                subject = s => MostUnstableForeignState(s)?.id ?? "",
                options = s => new List<CrisisOption>
                {
                    Option("RECEIVE AND PROCESS", "Meet the obligation. It will cost, and divide, and " +
                        "the country people are fleeing will not forget who took them.",
                        "Reception centres opened. Internationally well received; domestically contested.",
                        treasury: -120, unity: -3, stability: -1,
                        effect: CrisisEffects.Relations,
                        target: MostUnstableForeignState(s)?.id ?? "", magnitude: 10),
                    Option("FUND REGIONAL CONTAINMENT", "Pay others to hold the line. It steadies them.",
                        "Support extended to frontline states. The problem is further away, not smaller.",
                        treasury: -90,
                        effect: CrisisEffects.ForeignUnrest,
                        target: MostUnstableForeignState(s)?.id ?? "", magnitude: 5),
                    Option("CLOSE THE FRONTIER", "Order first. Everything else after.",
                        "The frontier is closed. Order holds; our standing abroad does not.",
                        approval: 3, unity: -4,
                        effect: CrisisEffects.Relations,
                        target: MostUnstableForeignState(s)?.id ?? "", magnitude: -12)
                }
            });

            // ================= the social layer (GDD §12) =================
            //
            // Living standards, unrest and public memory are now tracked, and a
            // tracked pressure with no situation attached to it is a number on a
            // panel. These are where the operator meets it.

            list.Add(new EventDefinition
            {
                id = "COST_OF_LIVING",
                title = "STANDARDS OF LIVING FALLING",
                cooldownMonths = 20,
                isEligible = s => s.PlayerCountry.livingStandards < 42f,
                weight = s => (48f - s.PlayerCountry.livingStandards) / 12f,
                body = s => "Households are measurably worse off than they were three years ago. " +
                            "The figures are public and nobody is disputing them.",
                lapseEffectId = CrisisEffects.Conspiracy,
                lapseMagnitude = 8,
                options = s => new List<CrisisOption>
                {
                    Option("DIRECT HOUSEHOLD RELIEF", "Money to people, now. Expensive and immediate.",
                        "Payments announced. It will be felt this quarter.",
                        treasury: -180, approval: 6, stability: 2),
                    Option("STRUCTURAL REFORM PACKAGE", "Fix the cause. It will take years to land.",
                        "Reform announced. Nobody is better off yet.",
                        treasury: -90, approval: -3,
                        effect: CrisisEffects.MarketShock, magnitude: 6),
                    Option("HOLD THE LINE", "Neither. The books matter more than the mood.",
                        "No package. The government has decided this is weather, not climate.",
                        approval: -6, unity: -3)
                }
            });

            list.Add(new EventDefinition
            {
                id = "GENERAL_STRIKE",
                title = "GENERAL STRIKE",
                cooldownMonths = 18,
                isEligible = s => s.PlayerCountry.socialUnrest > 58f,
                weight = s => s.PlayerCountry.socialUnrest / 30f,
                body = s => "Coordinated stoppages across transport, ports and heavy industry. " +
                            "This is organised, and the organisers are not asking for a meeting.",
                lapseEffectId = CrisisEffects.MarketShock,
                lapseMagnitude = -12,
                options = s => new List<CrisisOption>
                {
                    Option("NEGOTIATE WITH THE UNIONS", "Concede something real. It ends this week.",
                        "Terms agreed. Work resumes; the precedent is set.",
                        treasury: -120, approval: 3, unity: 4),
                    Option("DECLARE ESSENTIAL SERVICES", "Compel a return. Legal, and remembered.",
                        "Order given. The strike breaks and something else does with it.",
                        stability: 4, approval: -5, unity: -6),
                    Option("WAIT IT OUT", "Neither side moves. The economy pays for the standoff.",
                        "The strike runs. Output is lost that will not be recovered.",
                        effect: CrisisEffects.MarketShock, magnitude: -9)
                }
            });

            list.Add(new EventDefinition
            {
                id = "OLD_WOUND",
                title = "AN OLD GRIEVANCE RESURFACES",
                cooldownMonths = 30,
                isEligible = s => s.PlayerCountry.publicGrievance > 45f,
                weight = s => s.PlayerCountry.publicGrievance / 40f,
                body = s => "An anniversary, a documentary, a released file — the specific trigger " +
                            "hardly matters. What the country went through is being argued about " +
                            "again, and the argument is not really about the past.",
                options = s => new List<CrisisOption>
                {
                    Option("FORMAL ACKNOWLEDGEMENT", "Say it plainly. It costs, and it closes something.",
                        "The statement was made. It was not universally welcomed, and it helped.",
                        approval: -3, unity: 6),
                    Option("COMMISSION A HISTORY", "Hand it to scholars and to time.",
                        "An official history is commissioned. The argument continues at lower volume.",
                        treasury: -40, unity: 2),
                    Option("REFUSE TO REVISIT IT", "The past is not this government's business.",
                        "No comment offered. The grievance is exactly where it was.",
                        approval: 2, unity: -5)
                }
            });

            // ================= theatres and commitment (GDD §16) =================

            list.Add(new EventDefinition
            {
                id = "OVERSTRETCH",
                title = "THE FORCE IS STRETCHED THIN",
                cooldownMonths = 14,
                isEligible = s => TheatreSystem.IsOverstretched(s, s.playerCountryId),
                weight = s => TheatreSystem.TotalCommitment(s, s.playerCountryId),
                body = s =>
                {
                    var theatres = TheatreSystem.ActiveTheatresFor(s, s.playerCountryId);
                    var names = new List<string>();
                    foreach (var theatre in theatres) names.Add(TheatreSystem.Name(theatre));
                    return $"The staff have delivered an assessment nobody asked for. We are " +
                           $"committed in {string.Join(" and ", names.ToArray())}, and they judge " +
                           "that we cannot sustain both at present intensity.";
                },
                lapseEffectId = CrisisEffects.Readiness,
                lapseMagnitude = -10,
                options = s => new List<CrisisOption>
                {
                    Option("SURGE SUSTAINMENT", "Pay for the second front properly.",
                        "Emergency sustainment funded. The force holds, at a price.",
                        treasury: -200, effect: CrisisEffects.Readiness, magnitude: 8),
                    Option("PRIORITIZE ONE THEATRE", "Accept that the other will go quiet.",
                        "Resources concentrated. One front is being fought; the other is being held.",
                        approval: -2, effect: CrisisEffects.Readiness, magnitude: 3),
                    Option("MAINTAIN BOTH AS THEY ARE", "Refuse the choice. The staff have noted it.",
                        "No change ordered. The assessment is filed and remains true.",
                        stability: -2, effect: CrisisEffects.Readiness, magnitude: -7)
                }
            });

            list.Add(new EventDefinition
            {
                id = "SECOND_FRONT_OPPORTUNITY",
                title = "A RIVAL IS COMMITTED ELSEWHERE",
                cooldownMonths = 16,
                isEligible = s => OverstretchedRival(s) != null,
                weight = s => 1.7f,
                body = s => $"{OverstretchedRival(s)?.displayName} is committed on more than one " +
                            "front and visibly cannot sustain it. The staff observe, without " +
                            "recommending, that this will not last.",
                subject = s => OverstretchedRival(s)?.id ?? "",
                options = s => new List<CrisisOption>
                {
                    Option("PRESS THE ADVANTAGE", "Move while they cannot answer.",
                        "We have moved against them while they were looking the other way.",
                        approval: 2,
                        effect: CrisisEffects.OpenConfrontation, target: OverstretchedRival(s)?.id ?? ""),
                    Option("EXTRACT CONCESSIONS", "Ask for something. They are in no position to refuse.",
                        "Terms extracted quietly. They will remember being leaned on.",
                        treasury: 140,
                        effect: CrisisEffects.Relations,
                        target: OverstretchedRival(s)?.id ?? "", magnitude: -10),
                    Option("DO NOTHING", "Not every opening is worth walking through.",
                        "No action taken. The opportunity passes, and is noted as having passed.",
                        effect: CrisisEffects.Trust,
                        target: OverstretchedRival(s)?.id ?? "", magnitude: 8)
                }
            });

            // ================= fragmentation (GDD §17.1) =================

            list.Add(new EventDefinition
            {
                id = "SEPARATIST_MOVEMENT",
                title = "SEPARATIST MOVEMENT",
                cooldownMonths = 26,
                isEligible = s => s.PlayerCountry.nationalUnity < 38f
                                  && s.PlayerCountry.socialUnrest > 42f,
                weight = s => (45f - s.PlayerCountry.nationalUnity) / 12f,
                body = s => "A regional movement has moved from complaint to organisation. They " +
                            "are talking about a referendum. Nobody has said the other word yet.",
                lapseEffectId = CrisisEffects.Conspiracy,
                lapseMagnitude = 14,
                options = s => new List<CrisisOption>
                {
                    Option("DEVOLVE AUTHORITY", "Give them something real to hold.",
                        "Powers devolved. The movement has what it asked for and less reason to ask again.",
                        unity: 8, stability: -2, approval: -4),
                    Option("INVEST IN THE REGION", "Buy the argument out.",
                        "Investment announced. It will take years, and it may work.",
                        treasury: -160, unity: 4),
                    Option("RULE IT OUT ENTIRELY", "There will be no referendum.",
                        "The position is stated and final. So is theirs.",
                        stability: 3, unity: -9,
                        effect: CrisisEffects.Conspiracy, magnitude: 12)
                }
            });

            list.Add(new EventDefinition
            {
                id = "BREAKAWAY_RECOGNITION",
                title = "A BREAKAWAY SEEKS RECOGNITION",
                cooldownMonths = 20,
                isEligible = s => AnyBreakawayState(s) != null,
                weight = s => 2f,
                body = s => $"{AnyBreakawayState(s)?.displayName} has asked us to recognise it. " +
                            "The state it left has asked us not to. Both are watching what we do.",
                subject = s => AnyBreakawayState(s)?.id ?? "",
                options = s => new List<CrisisOption>
                {
                    Option("RECOGNIZE", "Deal with the country that exists.",
                        "Recognition extended. One government is grateful and another is not.",
                        effect: CrisisEffects.Relations,
                        target: AnyBreakawayState(s)?.id ?? "", magnitude: 20),
                    Option("WITHHOLD RECOGNITION", "Sovereignty is not a thing we hand out.",
                        "Recognition withheld. Our position is consistent and our access is not.",
                        effect: CrisisEffects.Relations,
                        target: AnyBreakawayState(s)?.id ?? "", magnitude: -14),
                    Option("DEFER THE QUESTION", "Say nothing that can be quoted.",
                        "No position taken. Both sides read it as leaning the other way.",
                        approval: -1, unity: -1)
                }
            });

            // ================= the maritime world (GDD §16, §19) =================

            list.Add(new EventDefinition
            {
                id = "PORT_STRIKE",
                title = "PORT WORKERS WALK OUT",
                cooldownMonths = 18,
                isEligible = s => HasPort(s, s.playerCountryId) && s.PlayerCountry.socialUnrest > 40f,
                weight = s => 1.3f,
                body = s => "Our container terminals have stopped. Ships are anchored offshore and " +
                            "the queue is being photographed from the air.",
                lapseEffectId = CrisisEffects.TradeShock,
                lapseMagnitude = -10,
                options = s => new List<CrisisOption>
                {
                    Option("SETTLE THE DISPUTE", "Pay what it takes to reopen the docks.",
                        "Terms agreed. The queue clears within the fortnight.",
                        treasury: -100, unity: 2,
                        effect: CrisisEffects.TradeShock, magnitude: 5),
                    Option("USE NAVAL LOGISTICS", "Move the cargo ourselves. Slow and visible.",
                        "Naval transport pressed into commercial service. It is not enough, but it is something.",
                        treasury: -60,
                        effect: CrisisEffects.Readiness, magnitude: -5),
                    Option("LET IT RUN", "The docks will reopen when the money runs out.",
                        "No intervention. The lost trade is real and the resentment is durable.",
                        approval: -4,
                        effect: CrisisEffects.TradeShock, magnitude: -8)
                }
            });

            list.Add(new EventDefinition
            {
                id = "SEA_LANE_PIRACY",
                title = "SHIPPING ATTACKED",
                cooldownMonths = 16,
                isEligible = s => s.PlayerCountry.military.naval.EffectivePower > 0.05f
                                  && AnyForeignInstability(s),
                weight = s => 1.4f,
                body = s => "Armed attacks on commercial shipping near a failing state. Our " +
                            "carriers are demanding escorts and our insurers are repricing.",
                lapseEffectId = CrisisEffects.TradeShock,
                lapseMagnitude = -8,
                options = s => new List<CrisisOption>
                {
                    Option("DEPLOY ESCORTS", "Warships on the route. Expensive and effective.",
                        "Escorts on station. The attacks stop; the deployment does not.",
                        treasury: -130,
                        effect: CrisisEffects.TradeShock, magnitude: 6),
                    Option("FUND A REGIONAL PATROL", "Pay somebody closer to do it.",
                        "A regional force is funded. Slower, cheaper, and not ours to command.",
                        treasury: -70,
                        effect: CrisisEffects.ForeignUnrest,
                        target: MostUnstableForeignState(s)?.id ?? "", magnitude: 6),
                    Option("REROUTE COMMERCIAL TRAFFIC", "Go the long way and absorb the cost.",
                        "Traffic rerouted. Everything arrives later and costs more.",
                        effect: CrisisEffects.TradeShock, magnitude: -6)
                }
            });

            // ================= identity and character (GDD §10) =================

            list.Add(new EventDefinition
            {
                id = "RESOURCE_WINDFALL",
                title = "MAJOR RESOURCE FIND",
                nature = EventNature.Opportunity,
                cooldownMonths = 34,
                isEligible = s => HasLocationType(s, s.playerCountryId, LocationType.MaterialsRegion)
                                  || HasLocationType(s, s.playerCountryId, LocationType.EnergyRegion),
                weight = s => 1.1f,
                body = s => "Survey results confirm a deposit substantially larger than modelled. " +
                            "Three ministries have already written the revenue into their plans.",
                options = s => new List<CrisisOption>
                {
                    Option("DEVELOP IT IMMEDIATELY", "Extract now, at whatever it costs to move fast.",
                        "Development accelerated. Revenue arrives early and so does the disruption.",
                        treasury: 260, stability: -2, approval: 3),
                    Option("DEVELOP IT SLOWLY", "Do it properly. The deposit is not going anywhere.",
                        "A measured programme is approved. The money comes later and cleaner.",
                        treasury: 90, unity: 2,
                        effect: CrisisEffects.MarketShock, magnitude: 5),
                    Option("LEAVE IT IN THE GROUND", "A reserve is worth more unextracted.",
                        "The deposit stays where it is, and everybody knows it is there.",
                        approval: -2)
                }
            });

            list.Add(new EventDefinition
            {
                id = "TECHNICAL_BREAKTHROUGH",
                title = "LABORATORY BREAKTHROUGH",
                nature = EventNature.Opportunity,
                cooldownMonths = 28,
                isEligible = s => s.PlayerCountry.pillars.intelligence > 55f
                                  || s.PlayerCountry.pillars.economy > 65f,
                weight = s => 1.2f,
                body = s => "A programme nobody was watching has produced something. The people " +
                            "who did it would like to know what happens next.",
                options = s => new List<CrisisOption>
                {
                    Option("PUBLISH OPENLY", "Standing, partners, and no monopoly.",
                        "Published. Our standing rises and so does everyone else's capability.",
                        approval: 3,
                        effect: CrisisEffects.Trust, target: LargestTradePartnerId(s), magnitude: 8),
                    Option("CLASSIFY IT", "Keep the advantage as long as it lasts.",
                        "Classified. The advantage is real and finite.",
                        effect: CrisisEffects.ExposeNetwork),
                    Option("LICENCE IT ABROAD", "Sell it. Advantage converted into money.",
                        "Licensing agreed. The treasury is better off and the edge is shared.",
                        treasury: 200,
                        effect: CrisisEffects.Relations, target: LargestTradePartnerId(s), magnitude: 6)
                }
            });

            list.Add(new EventDefinition
            {
                id = "DIPLOMATIC_INSULT",
                title = "A REMARK BECOMES AN INCIDENT",
                cooldownMonths = 14,
                // "Somebody is coldest" is always true — there is a minimum in
                // every list — so this fired in worlds where every relationship
                // stood at 85 and nobody was a rival at all. An eligibility test
                // has to name a condition the world can fail.
                isEligible = s => IsActuallyCold(s, 55f),
                weight = s => 1.1f,
                body = s => $"A senior figure in {ColdestRival(s)?.displayName} has said something " +
                            "about this country that was probably meant for their own audience. " +
                            "Ours has heard it anyway.",
                subject = ColdestRivalId,
                options = s => new List<CrisisOption>
                {
                    Option("DEMAND AN APOLOGY", "Make it their problem. Publicly.",
                        "A demand was issued. They will either apologise or not, and both are answers.",
                        approval: 4,
                        effect: CrisisEffects.Relations, target: ColdestRivalId(s), magnitude: -8),
                    Option("RESPOND IN KIND", "Say something back. It will feel good.",
                        "The reply landed. Nobody is better off and the public enjoyed it.",
                        approval: 5, unity: 2,
                        effect: CrisisEffects.Threat, target: ColdestRivalId(s), magnitude: 8),
                    Option("IGNORE IT ENTIRELY", "It was a remark. We have a country to run.",
                        "No response. The story lasted two days.",
                        approval: -3,
                        effect: CrisisEffects.Relations, target: ColdestRivalId(s), magnitude: 3)
                }
            });

            list.Add(new EventDefinition
            {
                id = "SUCCESSION_QUESTION",
                title = "THE SUCCESSION QUESTION",
                cooldownMonths = 24,
                isEligible = s => s.PlayerCountry.government.leader.age > 66f
                                  && s.PlayerCountry.government.successorReadiness < 40f,
                weight = s => (s.PlayerCountry.government.leader.age - 62f) / 5f,
                body = s => "The leadership is visibly ageing and there is no settled answer to " +
                            "what comes after. The question is being asked in print now.",
                lapseEffectId = CrisisEffects.Conspiracy,
                lapseMagnitude = 10,
                options = s => new List<CrisisOption>
                {
                    Option("NAME A SUCCESSOR", "Settle it. Everyone who is not chosen will know.",
                        "A successor is named. The question is answered and the factions are not.",
                        stability: 5, unity: -3),
                    Option("ESTABLISH A PROCESS", "Not a person — a procedure.",
                        "A succession process is codified. Slower, and it will outlast this leadership.",
                        approval: 2, stability: 3),
                    Option("REFUSE TO DISCUSS IT", "The leadership is not going anywhere.",
                        "No comment. The question does not go away because it was not answered.",
                        stability: -3,
                        effect: CrisisEffects.Conspiracy, magnitude: 8)
                }
            });

            return list;
        }

        // ---------- eligibility helpers for world-changing events ----------

        /// <summary>A rival visibly committed on more than one front (GDD §16).</summary>
        static CountryState OverstretchedRival(GameState state)
        {
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (!TheatreSystem.IsOverstretched(state, country.id)) continue;

                var relationship = state.FindRelationship(state.playerCountryId, country.id);
                if (relationship != null && relationship.relations < 55f) return country;
            }
            return null;
        }

        /// <summary>Any state that came into existence by secession (GDD §17.1).</summary>
        static CountryState AnyBreakawayState(GameState state)
        {
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (country.id.EndsWith("_S", StringComparison.Ordinal)) return country;
            }
            return null;
        }

        static bool HasPort(GameState state, string countryId)
            => HasLocationType(state, countryId, LocationType.Port);

        static bool HasLocationType(GameState state, string countryId, LocationType type)
        {
            foreach (var location in state.locations)
                if (location.ownerId == countryId && location.type == type) return true;
            return false;
        }

        static bool HasNetworkAgainstUs(GameState state)
        {
            foreach (var network in state.networks)
                if (network.targetId == state.playerCountryId && !network.compromised) return true;
            return false;
        }

        static bool AnyForeignChokepoint(GameState state) => FirstForeignChokepoint(state) != null;

        /// <summary>
        /// A chokepoint only becomes a problem when the state holding it has some
        /// reason to squeeze us. Merely existing is not a crisis.
        /// </summary>
        static StrategicLocation FirstForeignChokepoint(GameState state)
        {
            foreach (var location in state.locations)
            {
                if (location.type != LocationType.Chokepoint) continue;
                if (location.ownerId == state.playerCountryId) continue;

                var relationship = state.FindRelationship(state.playerCountryId, location.ownerId);
                if (relationship == null || relationship.relations >= 55f) continue;
                return location;
            }
            return null;
        }

        static bool AnyForeignCoupRecently(GameState state)
        {
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (country.government.coupsExperienced > 0 && country.government.leader.monthsInOffice < 18)
                    return true;
            }
            return false;
        }

        static bool HasTreatyPartner(GameState state) => FirstTreatyPartner(state) != null;

        static CountryState FirstTreatyPartner(GameState state)
        {
            foreach (var treaty in state.treaties)
            {
                if (treaty.broken || !treaty.Involves(state.playerCountryId)) continue;
                return state.FindCountry(treaty.PartnerOf(state.playerCountryId));
            }
            return null;
        }

        static bool AnyForeignInstability(GameState state) => MostUnstableForeignState(state) != null;

        static CountryState MostUnstableForeignState(GameState state)
        {
            CountryState worst = null;
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                bool troubled = country.stability < 35f
                                || country.government.inCivilConflict
                                || state.IsAtWar(country.id);
                if (!troubled) continue;
                if (worst == null || country.stability < worst.stability) worst = country;
            }
            return worst;
        }

        static CountryState RecentlyCouped(GameState state)
        {
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (country.government.coupsExperienced > 0 && country.government.leader.monthsInOffice < 18)
                    return country;
            }
            return null;
        }
    }
}
