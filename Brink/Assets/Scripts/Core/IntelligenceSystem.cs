using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>Covert operation types (GDD §14).</summary>
    public enum CovertOperation
    {
        Sabotage,          // damage industry/sector health
        PoliticalInfluence,// destabilize the target government
        TheftOfPlans,      // deepen penetration, sharpen estimates
        Deception          // shape what others believe about us
    }

    /// <summary>
    /// Intelligence, estimates and deception (GDD Phase 6, §14).
    ///
    /// The central rule: nobody reads true foreign state. Observers hold
    /// estimates with a margin and a confidence grade, produced from network
    /// penetration net of the target's counterintelligence, degraded by
    /// staleness, and bent by the target's deception programs. The same
    /// machinery serves AI observers, so the AI is under the same fog.
    /// </summary>
    public static class IntelligenceSystem
    {
        public const int EstablishNetworkCost = 2;
        public const int ExpandNetworkCost = 1;
        public const int CovertOperationCost = 2;

        // ---------- truth access (internal only) ----------

        /// <summary>The real value an estimate is trying to measure.</summary>
        public static float TrueValue(CountryState country, IntelDomain domain)
        {
            switch (domain)
            {
                case IntelDomain.Military: return country.pillars.military;
                case IntelDomain.Economic: return country.pillars.economy;
                case IntelDomain.Political: return country.stability;
                default: return country.pillars.diplomacy;
            }
        }

        // ---------- collection ----------

        /// <summary>Monthly collection cycle for every network in the world.</summary>
        public static void MonthlyCollection(GameState state)
        {
            int monthIndex = state.date.MonthsSince(state.startDate);

            foreach (var network in state.networks)
            {
                network.monthsActive++;
                var target = state.FindCountry(network.targetId);
                if (target == null) continue;

                var rng = new Random(unchecked(
                    state.rngSeed * 1500450271 + monthIndex * 137 +
                    Hash.Of(network.ownerId) * 31 + Hash.Of(network.targetId)));

                // Counterintelligence may roll up the network entirely.
                //
                // A deeper network is a *larger footprint* — more officers, more
                // communications, more chances to be caught — so depth never buys
                // immunity. Treating penetration as pure protection meant any
                // network that outgrew twice the target's counterintelligence
                // became permanently un-rollable, which it reliably did within
                // about a decade: fog of war simply ended in the late game.
                float footprint = 0.30f + network.penetration / 160f;
                float exposure = target.counterIntel.counterIntelligence * footprint / 100f;
                if (!network.compromised && exposure > 0f && rng.NextDouble() < exposure * 0.06)
                {
                    network.compromised = true;
                    network.penetration *= 0.35f;

                    // Rolling up a foreign network teaches the service that did
                    // it — the same +6 lesson the covert-op exposure path has
                    // always taught, and this path silently lacked. It matters
                    // because this is the *event-driven* half of counter-play:
                    // the AI's deliberate HardenSecurity policy competes for an
                    // action budget that a hot world fills with wars, so once
                    // the world started fighting its own wars, a decade of
                    // caught networks measurably taught nobody anything until
                    // the catch itself carried the lesson.
                    target.counterIntel.counterIntelligence =
                        Clamp(target.counterIntel.counterIntelligence + 6f);
                    target.counterIntel.institutionalHardening =
                        Math.Min(20f, target.counterIntel.institutionalHardening + 4f);

                    // Being caught goes on the record, for everyone, publicly.
                    //
                    // Two reasons this cannot stay a player-only line. First, only
                    // the player could ever be seen to be caught, so no AI state
                    // ever built a reputation for subversion. Second, the
                    // `compromised` flag is cleared again within a month or two by
                    // the rebuild below — it answers "is this network blown right
                    // now", not "has this government been caught before", and the
                    // second question is the one other governments reason from.
                    state.AddChronicle(ChronicleCategory.Intelligence, network.ownerId,
                        $"Network in {target.displayName} compromised.", Publicity.Public);

                    if (network.ownerId == state.playerCountryId)
                        state.AddNotification(NotificationClass.Priority, "NETWORK COMPROMISED",
                            $"Collection network in {target.displayName} has been rolled up.", target.id,
                            desk: ReportingDesk.Intelligence);
                    continue;
                }

                if (network.compromised)
                {
                    // A blown network slowly rebuilds but reports poorly.
                    network.penetration = Clamp(network.penetration + 0.4f);
                    if (network.penetration > 25f) network.compromised = false;
                }
                else
                {
                    float growth = 0.6f;
                    if (network.ownerId == state.playerCountryId)
                        growth += ProgressionSystem.EffectValue(state, SkillEffect.CollectionTradecraft);

                    // National collection infrastructure (GDD §11).
                    var owner = state.FindCountry(network.ownerId);
                    if (owner != null)
                        growth += TechnologySystem.Effectiveness(owner, "CAP_SIGINT") * 1.4f;

                    network.penetration = Clamp(network.penetration + growth);
                }

                // Focus domain collected well; others collected incidentally.
                foreach (IntelDomain domain in Enum.GetValues(typeof(IntelDomain)))
                {
                    float focusFactor = domain == network.focus ? 1f : 0.45f;
                    UpdateEstimate(state, network, target, domain, focusFactor, rng);
                }
            }

            DecayStaleEstimates(state);
        }

        static void UpdateEstimate(GameState state, IntelNetwork network, CountryState target,
            IntelDomain domain, float focusFactor, Random rng)
        {
            var estimate = state.FindEstimate(network.ownerId, target.id, domain);
            if (estimate == null)
            {
                estimate = new IntelEstimate
                {
                    observerId = network.ownerId,
                    targetId = target.id,
                    domain = domain
                };
                state.estimates.Add(estimate);
            }

            // Effective access: penetration net of counterintelligence, and of
            // whatever hardening the target has fielded (GDD §11).
            float hardening = 1f + TechnologySystem.Effectiveness(target, "CAP_SECCOMMS") * 0.5f;
            float access = network.penetration * focusFactor
                           - target.counterIntel.counterIntelligence * 0.55f * hardening;
            if (network.compromised) access *= 0.3f;
            access = Math.Max(0f, access);

            float truth = TrueValue(target, domain);

            // Deception bends the reported value when it targets this domain and
            // beats the collector's access (GDD §14: intelligence can be wrong).
            var deception = target.counterIntel;
            float deceptionEffect = 0f;
            bool deceived = false;
            if (deception.deceptionStrength > 0f && deception.deceptionDomain == domain)
            {
                float penetrationOfDeception = access - deception.deceptionStrength * 0.6f;
                if (penetrationOfDeception < 0f)
                {
                    deceptionEffect = deception.deceptionBias * (deception.deceptionStrength / 100f) * 18f;
                    deceived = true;
                }
            }

            // Analytical noise shrinks as access grows but never reaches zero.
            float noiseScale = Math.Max(1.5f, 26f - access * 0.35f);

            // Operator analytical training tightens our own reporting only.
            if (network.ownerId == state.playerCountryId)
                noiseScale *= Math.Max(0.3f, 1f - ProgressionSystem.EffectValue(state, SkillEffect.AnalyticalPrecision));

            // Analytic computing tightens it for whoever fielded it (GDD §11).
            var analyst = state.FindCountry(network.ownerId);
            if (analyst != null)
                noiseScale *= 1f - TechnologySystem.Effectiveness(analyst, "CAP_ANALYTICS") * 0.3f;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * noiseScale * 0.5f;

            estimate.reportedValue = Clamp(truth + deceptionEffect + noise);
            estimate.margin = noiseScale;
            estimate.confidence = GradeFor(access);
            estimate.asOf = state.date;
            estimate.everCollected = true;
            estimate.deceived = deceived;
        }

        /// <summary>Estimates age: confidence decays when collection stops.</summary>
        static void DecayStaleEstimates(GameState state)
        {
            foreach (var estimate in state.estimates)
            {
                int monthsStale = state.date.MonthsSince(estimate.asOf);
                if (monthsStale <= 1) continue;

                estimate.margin = Math.Min(45f, estimate.margin + 0.4f);
                if (monthsStale > 6 && estimate.confidence > ConfidenceGrade.Low)
                    estimate.confidence = (ConfidenceGrade)((int)estimate.confidence - 1);
            }
        }

        static ConfidenceGrade GradeFor(float access)
        {
            if (access >= 60f) return ConfidenceGrade.Confirmed;
            if (access >= 40f) return ConfidenceGrade.High;
            if (access >= 22f) return ConfidenceGrade.Moderate;
            if (access >= 8f) return ConfidenceGrade.Low;
            return ConfidenceGrade.None;
        }

        /// <summary>
        /// What an observer believes about a target. Returns null when nothing has
        /// ever been collected — the caller must render "NO ASSESSMENT" rather
        /// than reaching for the truth.
        /// </summary>
        public static IntelEstimate GetEstimate(GameState state, string observerId, string targetId, IntelDomain domain)
        {
            if (observerId == targetId) return null; // you know yourself
            var estimate = state.FindEstimate(observerId, targetId, domain);
            return estimate != null && estimate.everCollected ? estimate : null;
        }

        /// <summary>
        /// What our reporting says a foreign garrison is, as a band. Lives here
        /// rather than in the view because it needs the true value to distort,
        /// and truth must not leave the intelligence boundary.
        ///
        /// The band is centred on the *distorted* figure, not the real one: the
        /// same bias our headline estimate of that state carries is applied to
        /// the garrison, so a rival running a deception program bends the numbers
        /// the player actually plans an assault from. Centring on truth — as an
        /// earlier build did — meant deception changed the INTELLIGENCE screen
        /// and nothing the player acted on.
        /// </summary>
        public static bool TryEstimateGarrison(GameState state, string observerId,
            StrategicLocation location, out float low, out float high, out ConfidenceGrade confidence)
        {
            low = high = 0f;
            confidence = ConfidenceGrade.None;
            if (location == null) return false;

            // Our own ground, and ground we hold, we simply know.
            if (location.ownerId == observerId)
            {
                low = high = location.garrison;
                confidence = ConfidenceGrade.Confirmed;
                return true;
            }

            var estimate = GetEstimate(state, observerId, location.ownerId, IntelDomain.Military);
            if (estimate == null || estimate.confidence == ConfidenceGrade.None) return false;

            var owner = state.FindCountry(location.ownerId);
            float truth = owner != null ? TrueValue(owner, IntelDomain.Military) : 0f;

            // Carry the estimate's distortion across proportionally.
            float bias = truth > 1f ? estimate.reportedValue / truth : 1f;
            bias = Math.Min(2f, Math.Max(0.5f, bias));
            float perceived = Clamp(location.garrison * bias);

            float band = estimate.margin * 0.6f;
            low = Math.Max(0f, perceived - band);
            high = Math.Min(100f, perceived + band);
            confidence = estimate.confidence;
            return true;
        }

        // ---------- player commands ----------

        public static bool EstablishNetwork(GameState state, TurnManager turns, string targetId, IntelDomain focus)
        {
            if (state.FindNetwork(state.playerCountryId, targetId) != null)
            {
                GameLog.Warn("INTEL", "A network already exists against that state.");
                return false;
            }
            var target = state.FindCountry(targetId);
            if (target == null || targetId == state.playerCountryId) return false;
            if (!turns.SpendCommandPoints(EstablishNetworkCost, $"Establish network in {target.displayName}"))
                return false;

            state.networks.Add(new IntelNetwork
            {
                ownerId = state.playerCountryId,
                targetId = targetId,
                focus = focus,
                penetration = 18f
            });

            state.AddNotification(NotificationClass.Advisory, "NETWORK ESTABLISHED",
                $"Collection network active in {target.displayName}. Focus: {Phrase.Of(focus)}.", targetId);
            state.AddChronicle(ChronicleCategory.Intelligence, state.playerCountryId,
                $"Collection network established in {target.displayName}.");
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 8, "Network established");
            return true;
        }

        public static bool ExpandNetwork(GameState state, TurnManager turns, string targetId)
        {
            var network = state.FindNetwork(state.playerCountryId, targetId);
            if (network == null) return false;
            if (!turns.SpendCommandPoints(ExpandNetworkCost, "Expand collection")) return false;

            network.penetration = Clamp(network.penetration + 12f);
            ProgressionSystem.RecordInitiative(state);
            return true;
        }

        public static bool SetFocus(GameState state, string targetId, IntelDomain focus)
        {
            var network = state.FindNetwork(state.playerCountryId, targetId);
            if (network == null || network.focus == focus) return false;
            network.focus = focus;
            GameLog.Info("INTEL", $"Collection focus against {targetId} set to {focus}.");
            return true;
        }

        /// <summary>
        /// Run a covert operation. Success depends on penetration net of the
        /// target's counterintelligence; exposure carries diplomatic and
        /// operational consequences.
        /// </summary>
        /// <summary>
        /// Whether this covert operation is available to us, and why not if it is
        /// not. Views must call this rather than presenting a dead button.
        /// </summary>
        public static bool CanRunCovertOperation(GameState state, string targetId,
            CovertOperation operation, out string reason)
        {
            if (operation == CovertOperation.Deception)
            {
                if (ProgressionSystem.EffectValue(state, SkillEffect.DeepCoverProgram) <= 0f)
                {
                    reason = "No deep cover apparatus — requires Deep Cover Program.";
                    return false;
                }
                reason = "";
                return true;
            }

            if (state.FindNetwork(state.playerCountryId, targetId) == null)
            {
                reason = "No collection network in place to work through.";
                return false;
            }

            reason = "";
            return true;
        }

        public static bool RunCovertOperation(GameState state, TurnManager turns, string targetId,
            CovertOperation operation, float deceptionBias = 1f,
            IntelDomain deceptionDomain = IntelDomain.Military)
        {
            var player = state.PlayerCountry;

            if (operation == CovertOperation.Deception)
            {
                // Deception requires legends and cover built over years
                // (strategic verb, GDD §25.3).
                if (!CanRunCovertOperation(state, targetId, operation, out string blocked))
                {
                    GameLog.Warn("INTEL", blocked);
                    return false;
                }
                if (!turns.SpendCommandPoints(CovertOperationCost, "Deception program")) return false;
                ProgressionSystem.RecordInitiative(state);
                player.counterIntel.deceptionStrength = Clamp(player.counterIntel.deceptionStrength + 30f);
                player.counterIntel.deceptionBias = deceptionBias;

                // Set the domain the programme actually bends. Without this the
                // field kept its default and every deception the player ever ran
                // distorted the Military estimate — so a programme intended to
                // conceal economic weakness silently did nothing at all.
                player.counterIntel.deceptionDomain = deceptionDomain;
                state.AddNotification(NotificationClass.Advisory, "DECEPTION PROGRAM",
                    "Defensive deception program expanded.", player.id);
                state.AddChronicle(ChronicleCategory.Intelligence, player.id, "Deception program expanded.");
                return true;
            }

            var network = state.FindNetwork(state.playerCountryId, targetId);
            var target = state.FindCountry(targetId);
            if (network == null || target == null)
            {
                GameLog.Warn("INTEL", "No network in place for that operation.");
                return false;
            }
            int covertCost = ProgressionSystem.DiscountedCost(state, CovertOperationCost, SkillEffect.CovertEfficiency);
            if (!turns.SpendCommandPoints(covertCost, $"{operation} against {target.displayName}"))
                return false;

            ProgressionSystem.RecordInitiative(state);

            // Covert action against a state you are already confronting winds the
            // situation up whether or not it is ever traced back (GDD §18.1) —
            // the target's services know something is being done to them.
            ConfrontationSystem.AddPressure(state, state.playerCountryId, targetId, 8f);

            int monthIndex = state.date.MonthsSince(state.startDate);
            // Mix in the target and a per-action counter: two operations in one
            // month must be two independent gambles. Sharing a stream made a
            // successful theft repeatable at will, because the second run drew
            // the same roll against a threshold its own success had raised.
            var rng = new Random(unchecked(
                state.rngSeed * 40503
                + monthIndex * 7919
                + (int)operation * 131
                + Hash.Of(targetId) * 17
                + state.NextActionSequence() * 104729));

            float access = network.penetration - target.counterIntel.counterIntelligence * 0.6f;
            float successChance = Clamp01(0.2f + access / 90f);
            bool success = rng.NextDouble() < successChance;

            float exposureChance = Clamp01(0.18f + target.counterIntel.counterIntelligence / 260f)
                                   * Math.Max(0.1f, 1f - ProgressionSystem.EffectValue(state, SkillEffect.Compartmentation));
            bool exposed = rng.NextDouble() < exposureChance;

            if (success)
            {
                switch (operation)
                {
                    case CovertOperation.Sabotage:
                    {
                        var industry = target.economy.GetSector(EconomicSector.Industry);
                        if (industry != null) industry.health = Clamp(industry.health - 14f);
                        target.resources.industrialCapacity = Clamp(target.resources.industrialCapacity - 7f);
                        target.economy.confidence = Clamp(target.economy.confidence - 5f);
                        break;
                    }
                    case CovertOperation.PoliticalInfluence:
                        target.stability = Clamp(target.stability - 7f);
                        target.governmentApproval = Clamp(target.governmentApproval - 5f);
                        target.nationalUnity = Clamp(target.nationalUnity - 4f);

                        // Support to opposition accelerates an existing plot; it
                        // cannot conjure one where there is no grievance (GDD §22).
                        float receptiveness = Clamp01((60f - target.stability) / 60f);
                        target.government.conspiracyLevel =
                            Clamp(target.government.conspiracyLevel + 6f * receptiveness);
                        if (target.government.conspiracyLevel > 20f
                            && string.IsNullOrEmpty(target.government.conspiracyBackerId))
                            target.government.conspiracyBackerId = player.id;
                        break;
                    case CovertOperation.TheftOfPlans:
                        network.penetration = Clamp(network.penetration + 20f);
                        // Through Growth.Apply, like every other capability gain.
                        // A raw +2 on a monthly-cadence action is the un-damped
                        // ratchet this codebase has now fixed six times — and here
                        // it was quietly *masking* the exposure penalty below,
                        // since the pillar it inflated is one of the five that
                        // trajectory sums.
                        player.pillars.intelligence =
                            Growth.Apply(player.pillars.intelligence, 2f);
                        break;
                }

                state.AddNotification(NotificationClass.Priority, $"{operation.ToString().ToUpperInvariant()} — SUCCESS",
                    $"Operation against {target.displayName} achieved its objective.", targetId,
                    desk: ReportingDesk.Intelligence);
                state.AddChronicle(ChronicleCategory.Intelligence, player.id,
                    $"Covert {Phrase.Of(operation)} against {target.displayName} succeeded.");
                ProgressionSystem.AwardXP(state, 12, "Covert operation succeeded");
            }
            else
            {
                state.AddNotification(NotificationClass.Advisory, $"{operation.ToString().ToUpperInvariant()} — FAILED",
                    $"Operation against {target.displayName} did not achieve its objective.", targetId,
                    desk: ReportingDesk.Intelligence);
            }

            if (exposed)
            {
                network.compromised = true;
                network.penetration = Clamp(network.penetration - 25f);
                target.counterIntel.counterIntelligence = Clamp(target.counterIntel.counterIntelligence + 6f);
                target.counterIntel.institutionalHardening =
                    Math.Min(20f, target.counterIntel.institutionalHardening + 4f);

                // Being caught costs you **how you are regarded**, not your
                // government's capacity to conduct diplomacy.
                //
                // This was `pillars.diplomacy -= 4f` — raw, undamped, and paired
                // with a notification reading "Diplomatic damage taken" while
                // nothing in the block touched a single relationship. Three things
                // were wrong with it. It charged the wrong quantity: an expelled
                // station chief changes what other states think of you, not how
                // capable your foreign ministry is. It had no recovery path for
                // the operator incurring it, since only the diplomacy playstyle
                // regrows that pillar (~0.16/month against a −4 hit — one exposure
                // erasing two years of a ministry's work). And because it moved a
                // pillar, the entire cost landed in the annual evaluation's
                // *trajectory* component rather than *position*, where standing
                // damage belongs.
                //
                // Measured effect: the intelligence playstyle took ~65 exposures a
                // decade, demanding ~260 points from a pillar with a range of 70.
                // Its diplomacy pillar floored at zero around month 15 and stayed
                // there, which is why intelligence graded below doing nothing.
                var relationship = state.FindRelationship(player.id, targetId);
                if (relationship != null)
                {
                    relationship.relations = Clamp(relationship.relations - 9f);
                    relationship.trust = Clamp(relationship.trust - 12f);
                    relationship.AddMemory(state.date,
                        $"Caught running a covert operation against us.", 1.2f);
                }

                // Everyone else hears about it too, and thinks a little less of a
                // government that gets caught — smaller, because it is somebody
                // else's embassy that was burgled.
                foreach (var other in state.relationships)
                {
                    if (other == relationship) continue;
                    if (!other.Involves(player.id)) continue;
                    other.trust = Clamp(other.trust - 1.5f);
                }

                // **No pillar cost at all.** The first pass at this cut the old −4
                // to −0.8, on the reasoning that a service whose officers keep
                // being expelled genuinely gets less done. Measurement said that
                // was still wrong: at ~0.6 exposures a month it is −5.8 a year
                // against a diplomacy ministry that regrows +1.9, so an 80%
                // reduction changed the *rate* of an unrecoverable decline and
                // nothing else. The intelligence playstyle's trajectory did not
                // move (43.4 -> 43.3).
                //
                // The rule this violates is the project's own: does every value
                // this decrements have a **reachable recovery path under the same
                // conditions**? For an operator running intelligence, the
                // diplomacy pillar does not — repairing it means abandoning the
                // playstyle that damaged it. Any cost with that shape is a slow
                // disqualification rather than a price.
                //
                // The cost of being caught is the standing damage above plus the
                // target's counterintelligence hardening below, and both of those
                // *are* recoverable: relations can be rebuilt, and a burned
                // network can be rebuilt somewhere else.

                state.AddNotification(NotificationClass.Priority, "OPERATION EXPOSED",
                    $"{target.displayName} has attributed the operation. Our standing with them " +
                    "has suffered, and others have noticed.", targetId,
                    desk: ReportingDesk.Intelligence);
                state.AddChronicle(ChronicleCategory.Intelligence, player.id,
                    $"Covert operation against {target.displayName} exposed.");
            }

            return success;
        }

        /// <summary>Invest in counterintelligence — a first-class defensive system (GDD §14).</summary>
        public static bool StrengthenCounterIntelligence(GameState state, TurnManager turns)
        {
            if (!turns.SpendCommandPoints(1, "Counterintelligence sweep")) return false;
            var player = state.PlayerCountry;
            player.counterIntel.counterIntelligence = Clamp(player.counterIntel.counterIntelligence + 8f);
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 7, "Counterintelligence sweep");

            // Sweeps can uncover foreign networks operating against us.
            int monthIndex = state.date.MonthsSince(state.startDate);
            // Per-invocation, or a second sweep in the same month replays the
            // same draws against the networks that survived the first one.
            var rng = new Random(unchecked(
                state.rngSeed * 22801 + monthIndex * 613 + state.NextActionSequence() * 104729));
            foreach (var network in state.networks)
            {
                if (network.targetId != player.id || network.compromised) continue;
                if (rng.NextDouble() < 0.4)
                {
                    network.compromised = true;
                    network.penetration *= 0.4f;
                    var owner = state.FindCountry(network.ownerId);
                    state.AddNotification(NotificationClass.Priority, "FOREIGN NETWORK DISRUPTED",
                        $"A {owner?.displayName} collection network has been rolled up.", network.ownerId,
                        desk: ReportingDesk.Intelligence);
                }
            }
            return true;
        }

        /// <summary>Deception and counterintelligence decay without maintenance.</summary>
        public static void MonthlyDecay(GameState state)
        {
            foreach (var country in state.countries)
            {
                var ci = country.counterIntel;
                ci.deceptionStrength = Math.Max(0f, ci.deceptionStrength - 1.2f);

                // What has been learned from catching people is part of what the
                // service reverts *to* — the reversion below erased every bump
                // the catches wrote, which is how a decade of exposures taught
                // the world 0.15 points. Procedures fade on a decade scale, not
                // a quarterly one.
                ci.institutionalHardening *= 0.995f;

                float baseline = 25f + country.pillars.intelligence * 0.45f + ci.institutionalHardening;
                ci.counterIntelligence = ci.counterIntelligence > baseline
                    ? Math.Max(baseline, ci.counterIntelligence - 0.8f)
                    : Math.Min(baseline, ci.counterIntelligence + 0.4f);
            }
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
