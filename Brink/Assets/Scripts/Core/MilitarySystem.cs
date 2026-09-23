using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Objective-based operation types (GDD §19).
    ///
    /// Each draws on a different mix of branches, so **what you built decides
    /// what you can do**. A power with no navy cannot blockade; one whose air
    /// force has been ground down cannot strike or suppress. That is the point:
    /// three interchangeable ground verbs made force composition irrelevant and
    /// every campaign the same campaign.
    ///
    /// They are also meant to be *sequenced*. Defences suppressed this month
    /// make next month's assault land; a blockade run for a year makes the
    /// garrison you eventually attack a weaker one. Appended in order — never
    /// reordered, since JsonUtility persists enums by ordinal and every archived
    /// operation record carries one.
    /// </summary>
    public enum OperationType
    {
        Assault,   // take the location by force
        Raid,      // degrade garrison/supply without holding ground
        Siege,     // isolate and grind down; slow, cheaper in lives
        Withdraw,  // abandon the effort, preserve the force

        /// <summary>Air power against the position. Cannot take ground.</summary>
        AirStrike,

        /// <summary>
        /// Break the air defences and fortifications themselves. Takes nothing
        /// and kills few, and permanently lowers what the next operation has to
        /// fight through — the setup move the verb list had no room for.
        /// </summary>
        SuppressDefenses,

        /// <summary>
        /// Close the sea lanes to the whole country. Strategic rather than
        /// tactical: it starves their economy and their sustainment instead of
        /// touching the objective.
        /// </summary>
        NavalBlockade,

        /// <summary>
        /// A small, precise, deniable action. Degrades the garrison with almost
        /// no civilian harm and almost no losses, and is far more exposed to a
        /// well-run counterintelligence service than to a large army.
        /// </summary>
        SpecialOperation,

        // ---- ground ----

        /// <summary>Dig in on ground we hold. Raises its defence value permanently.</summary>
        PreparedDefense,

        /// <summary>Pacify occupied ground so that holding it costs us less at home.</summary>
        CounterInsurgency,

        // ---- naval ----

        /// <summary>Fight their fleet for the water itself; every later naval operation is easier.</summary>
        SeaControl,

        /// <summary>Hunt their merchant shipping. Slower than a blockade, and it takes treasury too.</summary>
        CommerceRaiding,

        /// <summary>Deny a port or a strait, and keep denying it after the ships have gone.</summary>
        MineWarfare,

        /// <summary>Protect our own shipping. The only naval operation that defends.</summary>
        ConvoyEscort,

        // ---- air ----

        /// <summary>Fight their air force for the sky. The only way to destroy enemy air power.</summary>
        CounterAirCampaign,

        /// <summary>Ground their air force without destroying it. Costly to hold.</summary>
        NoFlyZone,

        /// <summary>Cut the supply lines behind the front.</summary>
        AirInterdiction,

        /// <summary>Attack the country's capacity to make war. The costliest in civilians.</summary>
        StrategicBombing,

        /// <summary>
        /// Strike the government itself. Legitimacy is the price — partners recoil
        /// and the target closes ranks behind whoever survives.
        /// </summary>
        LeadershipStrike,

        // ---- joint ----

        /// <summary>Take a coastal objective from the sea. The other verb that holds ground.</summary>
        AmphibiousAssault,

        /// <summary>Degrade their command and coordination. Runs on the intelligence service.</summary>
        CyberOperation,

        /// <summary>Build the shield against strikes and decisive instruments.</summary>
        MissileDefense,

        /// <summary>Get our nationals out before it starts.</summary>
        NoncombatantEvacuation
    }

    /// <summary>
    /// Military resolution (GDD Phase 4, §19). Readiness and supply decay in the
    /// field and recover in garrison; operations are objective-based and resolve
    /// against defense value, garrison and the delegated directive's risk
    /// settings. Deterministic per seed+month so long runs are reproducible.
    /// </summary>
    public static class MilitarySystem
    {
        public const int MineHazardMonths = 6;
        public const int MineEscortMonths = 3;

        /// <summary>Calendar-based routine clearance, including after peace and without a fleet.</summary>
        public static int MineMonthsRemaining(GameState state, StrategicLocation site)
            => Math.Max(0, site.mineHazardUntilMonth - (state.date.year * 12 + state.date.month));

        /// <summary>
        /// Converts branch power (0..1 each) onto the same scale as garrison and
        /// defence value (0..100), so an army and a fortification can be compared.
        /// </summary>
        public const float PowerScale = 30f;

        /// <summary>
        /// Which branches actually carry out this operation (GDD §19).
        ///
        /// This is what makes force composition a decision. An assault is
        /// overwhelmingly a ground affair with air and naval support; a strike is
        /// air alone; a blockade is the fleet and nothing else. Build a lopsided
        /// military and whole categories of operation stop being available to you
        /// in practice, without anything having to forbid them.
        /// </summary>
        public static float BranchPowerFor(MilitaryState mil, OperationType operationType)
        {
            var profile = OperationCatalog.For(operationType);
            if (profile == null) return mil.TotalPower;

            return mil.ground.EffectivePower * profile.ground
                   + mil.air.EffectivePower * profile.air
                   + mil.naval.EffectivePower * profile.naval;
        }

        /// <summary>
        /// The weight a country can actually bring to this operation.
        ///
        /// Almost always the branch mix above. Cyber is the exception: it is run
        /// by the intelligence service, so a country with a small army and a good
        /// one is dangerous in a way the branch weights cannot express.
        /// </summary>
        public static float OperationPower(CountryState country, OperationType operationType)
        {
            if (country == null) return 0f;
            var profile = OperationCatalog.For(operationType);
            if (profile != null && profile.usesIntelligence)
                return country.pillars.intelligence / 100f;

            return BranchPowerFor(country.military, operationType);
        }

        /// <summary>Whether this kind of operation can end with us holding the ground.</summary>
        public static bool CanTakeGround(OperationType operationType)
            => OperationCatalog.For(operationType)?.canTakeGround ?? false;

        /// <summary>
        /// What an order costs the operator's month, before escalation premium
        /// and skill discounts (GDD §12).
        ///
        /// A flat price across every verb would leave one of them strictly best,
        /// since they no longer resolve the same way. Priced apart, sequencing
        /// becomes a real decision: suppression then assault costs more command
        /// capacity than an assault alone and buys much better odds, and a
        /// special operation is the cheap probe you can afford in a thin month.
        /// </summary>
        public static int OperationCost(OperationType operationType)
            => OperationCatalog.For(operationType)?.cpCost ?? 2;

        /// <summary>One line on what an operation is for, for the order screen.</summary>
        public static string DescribeOperation(OperationType operationType)
            => OperationCatalog.For(operationType)?.description ?? "";

        /// <summary>The order screen's label for an operation.</summary>
        public static string NameOf(OperationType operationType)
            => OperationCatalog.For(operationType)?.displayName ?? operationType.ToString();

        // ---------- posture ----------

        public static int PostureCost(MilitaryPosture posture)
        {
            switch (posture)
            {
                case MilitaryPosture.Forward: return 2;
                case MilitaryPosture.Alert: return 1;
                default: return 0;
            }
        }

        /// <summary>Readiness the force is held at under a given posture.</summary>
        public static float ReadinessTargetFor(CountryState country, MilitaryPosture posture, bool atWar)
        {
            if (atWar) return 90f;
            switch (posture)
            {
                case MilitaryPosture.Forward: return 92f;
                case MilitaryPosture.Alert: return 85f;
                default: return 55f + country.pillars.economy * 0.15f;
            }
        }

        /// <summary>
        /// Extra readiness the force is held at while the operator has directed
        /// the defence ministry to prioritize readiness over budget (GDD §7.2).
        ///
        /// This has to move the target rather than add to the value — the drift
        /// in <see cref="MonthlyUpkeep"/> pulls readiness back every month, so a
        /// one-shot addition would be erased before it could be felt. Same trap
        /// that made occupation's readiness cost dead code.
        ///
        /// Player-only: the cabinet is the operator's. AI states reach the same
        /// place through posture, which costs them treasury the same way.
        /// </summary>
        public static float ReadinessDirectiveBonus(GameState state, CountryState country)
        {
            if (!country.isPlayer) return 0f;
            var official = state.FindOfficial(Pillar.Military);
            if (official == null) return 0f;
            if (official.mode != ControlMode.Directed) return 0f;
            return official.directiveId == "MIL_READINESS" ? 8f : 0f;
        }

        /// <summary>Monthly treasury cost of holding a posture.</summary>
        public static float PostureUpkeep(MilitaryPosture posture, bool atWar)
        {
            if (posture == MilitaryPosture.Forward) return 34f;
            if (posture == MilitaryPosture.Alert || atWar) return 12f;
            return 0f;
        }

        /// <summary>Monthly upkeep: readiness/supply drift toward posture targets.</summary>
        public static void MonthlyUpkeep(GameState state)
        {
            foreach (var country in state.countries)
            {
                var mil = country.military;
                bool atWar = state.IsAtWar(country.id);
                bool holding = mil.posture != MilitaryPosture.Peacetime || atWar;

                AdvanceProcurement(state, country);

                // Garrison duty ties down the force that is doing it. This has to
                // move the *target*, not subtract from readiness directly: the
                // drift below pulls readiness back to target every month, so a
                // flat monthly subtraction elsewhere was erased before it could
                // ever be felt — occupation looked costly in the code and was free
                // in play. Capped so a large occupier is degraded, not disarmed.
                float garrisonDrag = Math.Min(25f,
                    TerritorySystem.OccupiedValue(state, country.id) * 0.12f);

                // An armed movement on our own ground ties down the same force,
                // and by the same mechanism: it moves the target rather than
                // subtracting from the value.
                garrisonDrag += InsurgencySystem.ForceDrag(state, country.id);

                foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
                {
                    var force = mil.Get(branch);

                    // Basing is reach: held airbases raise the readiness a force
                    // can be sustained at, and losing them lowers it (GDD §19).
                    float target = Clamp(ReadinessTargetFor(country, mil.posture, atWar)
                                         + TerritorySystem.ProjectionSwing(state, country.id)
                                         + ReadinessDirectiveBonus(state, country)
                                         - garrisonDrag);
                    force.readiness = MoveToward(force.readiness, target, holding ? 4f : 2.5f);

                    // Logistics investment raises the sustainment ceiling and
                    // softens the drag of holding a posture (GDD §19).
                    float supplyTarget = 55f + country.resources.industrialCapacity * 0.30f
                                         + mil.logistics * 0.20f
                                         + TechnologySystem.Effectiveness(country, "CAP_LIFT") * 12f;

                    // Holding a posture consumes sustainment faster than it can
                    // be replaced, so it lowers the level the force settles at.
                    // It must not be an unbounded drain: an earlier build applied
                    // a monthly subtraction with no resupply path above Peacetime,
                    // so any state that stayed alert bled to zero supply and
                    // hollowed out its own army by standing still.
                    float drag = mil.posture == MilitaryPosture.Forward ? 30f : holding ? 18f : 0f;
                    if (atWar) drag += 12f;
                    drag *= Math.Max(0.35f, 1f - mil.logistics / 160f)
                            * (1f - TechnologySystem.Effectiveness(country, "CAP_LIFT") * 0.35f);

                    force.supply = MoveToward(force.supply, Clamp(supplyTarget - drag), 3f);

                    // Institutional memory fades. A force that is not fighting
                    // settles toward what its peacetime training sustains, which
                    // its own readiness and doctrine investment decide — so a
                    // well-drilled army forgets slowly and a neglected one
                    // forgets fast.
                    //
                    // This is the restoring force that stops veterancy being a
                    // ratchet. Nothing here can push experience *up*: peacetime
                    // improvement has to be bought through exercises, which is
                    // what finally gives war games a reason to exist beyond
                    // readiness and trust. Only wartime pulls above the ceiling.
                    if (!atWar)
                    {
                        float trainingCeiling = 18f + force.readiness * 0.22f + mil.logistics * 0.08f;
                        if (force.experience > trainingCeiling)
                            force.experience = MoveToward(force.experience, trainingCeiling, 0.45f);
                    }
                }

                country.resources.treasury -= PostureUpkeep(mil.posture, atWar);

                // Logistics decays without maintenance.
                mil.logistics = Clamp(mil.logistics - 0.15f);

                // A forward posture is read abroad as a threat (GDD §15.1).
                if (mil.posture == MilitaryPosture.Forward)
                {
                    foreach (var relationship in state.relationships)
                    {
                        if (!relationship.Involves(country.id)) continue;
                        string other = relationship.PartnerOf(country.id);
                        float current = relationship.ThreatPerceivedBy(other);
                        relationship.SetThreatPerceivedBy(other, Clamp(current + 0.6f));
                    }
                }
            }
        }

        /// <summary>Advance running procurement programs and pay for them.</summary>
        static void AdvanceProcurement(GameState state, CountryState country)
        {
            var mil = country.military;
            for (int i = mil.programs.Count - 1; i >= 0; i--)
            {
                var program = mil.programs[i];

                // A program the treasury cannot fund is cancelled, not free.
                if (country.resources.treasury < program.costPerMonth)
                {
                    mil.programs.RemoveAt(i);
                    if (country.isPlayer)
                        state.AddNotification(NotificationClass.Priority, "PROGRAM CANCELLED",
                            $"{program.label} cannot be funded and has been terminated.", country.id,
                            desk: ReportingDesk.Military);
                    state.AddChronicle(ChronicleCategory.Military, country.id,
                        $"PROCUREMENT TERMINATED: {program.label} cancelled for lack of funds.");
                    continue;
                }

                country.resources.treasury -= program.costPerMonth;
                var force = mil.Get(program.branch);
                force.SetStrength(Growth.Apply(force.strength, program.strengthPerMonth));

                // Real force structure is national military capability, so it
                // shows up in the headline pillar too (GDD §10). Without this,
                // defense investment is pure cost and never visible.
                country.pillars.military = Growth.Apply(country.pillars.military, program.strengthPerMonth * 0.5f);

                // Sustained procurement builds the industry that produces it.
                // This does not repay the programme — the treasury cost is far
                // larger — but defense spending is not purely extractive, and
                // modelling it as such made armament strictly self-defeating.
                EconomySystem.BuildIndustry(country, program.strengthPerMonth * 0.25f);

                program.monthsRemaining--;

                if (program.monthsRemaining > 0) continue;

                mil.programs.RemoveAt(i);
                if (country.isPlayer)
                    state.AddNotification(NotificationClass.Advisory, "PROGRAM DELIVERED",
                        $"{program.label} has completed. The force structure reflects it.", country.id,
                        desk: ReportingDesk.Military);
                state.AddChronicle(ChronicleCategory.Military, country.id,
                    $"PROCUREMENT COMPLETED: {program.label} delivered. Benefits accrued during funded months.", Publicity.Public);
            }
        }

        // ---------- player commands ----------

        /// <summary>
        /// Set the standing posture. Higher postures hold readiness up, cost
        /// treasury every month, and make neighbors nervous.
        /// </summary>
        /// <summary>
        /// Whether the player may assume this posture, and why not if they may
        /// not. The view must call this: a button that silently does nothing
        /// reads as a broken game rather than a locked capability.
        /// </summary>
        public static bool CanSetPosture(GameState state, MilitaryPosture posture, out string reason)
        {
            if (state.PlayerCountry.military.posture == posture)
            {
                reason = "Already at this posture.";
                return false;
            }

            // A forward posture needs access agreements and prepositioned stocks
            // we do not have by default (strategic verb, GDD §25.3).
            if (posture == MilitaryPosture.Forward
                && ProgressionSystem.EffectValue(state, SkillEffect.ForwardBasing) <= 0f)
            {
                reason = "No basing arrangements to stand forward from — requires Forward Basing.";
                return false;
            }

            reason = "";
            return true;
        }

        public static bool SetPosture(GameState state, TurnManager turns, MilitaryPosture posture)
        {
            var player = state.PlayerCountry;
            if (!CanSetPosture(state, posture, out string blocked))
            {
                if (player.military.posture != posture) GameLog.Warn("MILITARY", blocked);
                return false;
            }

            // Standing down is free; standing up costs command attention.
            int cost = posture > player.military.posture ? PostureCost(posture) : 0;
            if (cost > 0 && !turns.SpendCommandPoints(cost, $"Assume {posture} posture")) return false;

            player.military.posture = posture;
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 10, "Posture change");

            state.AddNotification(NotificationClass.Advisory, "POSTURE CHANGED",
                $"The force now stands at {posture.ToString().ToUpperInvariant()} posture.", player.id);
            state.AddChronicle(ChronicleCategory.Military, player.id,
                $"Posture set to {Phrase.Of(posture)}.", Publicity.Public);
            return true;
        }

        /// <summary>Program scales: bigger programs build more, over longer, for more.</summary>
        public enum ProgramScale { Modest, Major, Transformative }

        public static int ProgramCpCost(ProgramScale scale) => scale == ProgramScale.Transformative ? 3 : 2;

        public static ProcurementProgram BuildProgram(ForceBranch branch, ProgramScale scale)
        {
            switch (scale)
            {
                case ProgramScale.Transformative:
                    return new ProcurementProgram
                    {
                        branch = branch, monthsRemaining = 36,
                        strengthPerMonth = 0.55f, costPerMonth = 55f,
                        label = $"Transformative {branch} program"
                    };
                case ProgramScale.Major:
                    return new ProcurementProgram
                    {
                        branch = branch, monthsRemaining = 24,
                        strengthPerMonth = 0.40f, costPerMonth = 34f,
                        label = $"Major {branch} program"
                    };
                default:
                    return new ProcurementProgram
                    {
                        branch = branch, monthsRemaining = 12,
                        strengthPerMonth = 0.30f, costPerMonth = 18f,
                        label = $"Modest {branch} program"
                    };
            }
        }

        /// <summary>Maximum concurrent procurement programs.</summary>
        public const int MaxPrograms = 2;

        /// <summary>A read of saved terms, not a reserved budget or an invented start date.</summary>
        public static string ProcurementReadout(ProcurementProgram program)
        {
            int months = Math.Max(0, program.monthsRemaining);
            float cost = Math.Max(0f, program.costPerMonth);
            return $"{program.label ?? "Unnamed procurement programme"} — {program.branch}\n"
                + $"{months} funded months remaining at {cost:F0}/MO; "
                + $"remaining treasury commitment {months * (double)cost:F0} at saved terms (not reserved).\n"
                + "Capability builds during funded months, not as a final equipment shipment. "
                + "If a monthly payment cannot be met, the programme terminates; prior spending is not refunded.";
        }

        /// <summary>
        /// Authorize a procurement program for any state.
        ///
        /// Force <c>strength</c> is written upward in exactly one place — running
        /// a program — and this was previously reachable only by the player. Every
        /// AI state's army could therefore be ground down by combat and never
        /// rebuilt, so a player could permanently disarm a rival by fighting them
        /// once, and by the late game the world fielded paper armies whose
        /// intelligence estimates (which read the *pillar*, not the force) still
        /// reported them strong.
        /// </summary>
        public static bool BeginProcurementBy(GameState state, string actorId,
            ForceBranch branch, ProgramScale scale)
        {
            var actor = state.FindCountry(actorId);
            if (actor == null) return false;
            if (actor.military.programs.Count >= MaxPrograms) return false;

            var program = BuildProgram(branch, scale);
            if (actor.resources.treasury < program.costPerMonth * 3f) return false;

            actor.military.programs.Add(program);
            state.AddChronicle(ChronicleCategory.Military, actorId, $"PROCUREMENT AUTHORIZED: {program.label} authorized.");
            return true;
        }

        /// <summary>Sustainment investment by any state.</summary>
        public static bool InvestInLogisticsBy(GameState state, string actorId)
        {
            var actor = state.FindCountry(actorId);
            if (actor == null) return false;
            if (actor.resources.treasury < LogisticsTreasuryCost) return false;

            actor.resources.treasury -= LogisticsTreasuryCost;
            actor.military.logistics = Growth.Apply(actor.military.logistics, 9f);
            return true;
        }

        /// <summary>
        /// Whether a procurement programme can be authorized, and what stops it.
        ///
        /// **One gate, shared by the order screen and the order** — the same rule
        /// `OperationCatalog.CanOrder` follows, and for the same reason. These
        /// three refusals used to live only inside `BeginProcurement`, where the
        /// operator met them as a bright button that spent no Command Points and
        /// reported nothing. Money and industrial slots are not things a player
        /// can be expected to infer from a screen that does not mention them.
        /// </summary>
        public static bool CanBeginProcurement(GameState state, ProgramScale scale, out string reason)
        {
            reason = null;
            var player = state.PlayerCountry;
            if (player == null) { reason = "NO STATE"; return false; }

            if (player.military.programs.Count >= MaxPrograms)
            {
                reason = $"THE INDUSTRIAL BASE IS CARRYING {MaxPrograms} PROGRAMMES ALREADY.";
                return false;
            }

            if (scale == ProgramScale.Transformative
                && ProgressionSystem.EffectValue(state, SkillEffect.StrategicIndustry) <= 0f)
            {
                reason = "NO YARD OR LINE CAN ABSORB A PROGRAMME THAT SIZE — REQUIRES STRATEGIC INDUSTRY.";
                return false;
            }

            float needed = BuildProgram(ForceBranch.Ground, scale).costPerMonth * 3f;
            if (player.resources.treasury < needed)
            {
                reason = $"TREASURY {player.resources.treasury:F0} — A {scale.ToString().ToUpperInvariant()} "
                         + $"PROGRAMME NEEDS {needed:F0} IN HAND.";
                return false;
            }

            return true;
        }

        /// <summary>Whether sustainment investment can be funded, and what stops it.</summary>
        public static bool CanInvestInLogistics(GameState state, out string reason)
        {
            reason = null;
            var player = state.PlayerCountry;
            if (player == null) { reason = "NO STATE"; return false; }

            if (player.resources.treasury < LogisticsTreasuryCost)
            {
                reason = $"TREASURY {player.resources.treasury:F0} — A LOGISTICS EXPANSION COSTS "
                         + $"{LogisticsTreasuryCost:F0}.";
                return false;
            }

            return true;
        }

        public static bool BeginProcurement(GameState state, TurnManager turns,
            ForceBranch branch, ProgramScale scale)
        {
            var player = state.PlayerCountry;
            if (!CanBeginProcurement(state, scale, out string blocked))
            {
                GameLog.Warn("MILITARY", $"Programme refused: {blocked}");
                return false;
            }

            var program = BuildProgram(branch, scale);
            if (!turns.SpendCommandPoints(ProgramCpCost(scale), program.label)) return false;

            player.military.programs.Add(program);
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 18, "Procurement program launched");

            state.AddNotification(NotificationClass.Advisory, "PROGRAM AUTHORIZED",
                $"{program.label} authorized: {program.monthsRemaining} months at " +
                $"{program.costPerMonth:F0} per month.", player.id);
            state.AddChronicle(ChronicleCategory.Military, player.id, $"PROCUREMENT AUTHORIZED: {program.label} authorized.");
            return true;
        }

        public const int LogisticsInvestmentCost = 1;
        public const float LogisticsTreasuryCost = 70f;

        /// <summary>Invest in sustainment: depots, transport, stockpiles.</summary>
        public static bool InvestInLogistics(GameState state, TurnManager turns)
        {
            var player = state.PlayerCountry;

            // Check the money before spending the attention. A negative treasury
            // cancels running procurement and suspends research, so letting this
            // through unchecked let one click strand the player.
            if (!CanInvestInLogistics(state, out string blocked))
            {
                GameLog.Warn("MILITARY", $"Logistics investment refused: {blocked}");
                return false;
            }
            if (!turns.SpendCommandPoints(LogisticsInvestmentCost, "Logistics investment")) return false;

            player.resources.treasury -= LogisticsTreasuryCost;
            player.military.logistics = Growth.Apply(player.military.logistics, 9f);
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 10, "Logistics investment");

            state.AddChronicle(ChronicleCategory.Military, player.id, "Sustainment capacity expanded.");
            return true;
        }

        public const int DoctrineCost = 2;

        /// <summary>
        /// Adopt a force employment doctrine. Changes how operations resolve —
        /// it does not grant capability.
        /// </summary>
        public static bool SetDoctrine(GameState state, TurnManager turns, MilitaryDoctrine doctrine)
        {
            var player = state.PlayerCountry;
            if (player.military.doctrine == doctrine) return false;
            if (!turns.SpendCommandPoints(DoctrineCost, $"Adopt {doctrine} doctrine")) return false;

            player.military.doctrine = doctrine;
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 16, "Doctrine adopted");

            state.AddNotification(NotificationClass.Advisory, "DOCTRINE ADOPTED",
                $"The force will fight on {doctrine.ToString().ToUpperInvariant()} principles.", player.id);
            state.AddChronicle(ChronicleCategory.Military, player.id, $"{Phrase.Of(doctrine)} doctrine adopted.");
            return true;
        }

        /// <summary>
        /// Resolve one operation. Returns the record (also appended to the
        /// confrontation). A player can win battles and still lose the
        /// confrontation through casualties, treasury and political cost.
        /// </summary>
        public static OperationRecord ResolveOperation(
            GameState state,
            Confrontation confrontation,
            string attackerId,
            StrategicLocation target,
            OperationType operationType,
            OperationDirective directive,
            Random rng,
            float coalitionSupport = 0f)
        {
            var attacker = state.FindCountry(attackerId);
            var defender = state.FindCountry(target.ownerId);
            var record = new OperationRecord
            {
                date = state.date,
                locationId = target.id,
                operationType = operationType.ToString().ToUpperInvariant(),
                attackerId = attackerId ?? ""
            };

            if (operationType == OperationType.Withdraw)
            {
                record.success = true;

                // Abandoning a captured position hands it back to whoever it
                // belonged to. Without this, withdrawing changed nothing but the
                // momentum — the occupier kept the ground it had just given up.
                if (target.IsOccupied && target.ownerId == attackerId)
                {
                    target.ownerId = target.originalOwnerId;
                    target.garrison = Math.Max(20f, target.garrison);
                    record.summary = $"Forces withdrawn from {target.displayName}. " +
                                     "The position reverts to its original holder.";
                    state.AddChronicle(ChronicleCategory.Military, attackerId,
                        $"{target.displayName} relinquished.", Publicity.Public);
                }
                else
                {
                    record.summary = $"Forces withdrawn from {target.displayName}. Position abandoned.";
                }

                if (confrontation != null)
                {
                    confrontation.operations.Add(record);
                    confrontation.momentum -= attackerId == confrontation.initiatorId ? 8f : -8f;
                }
                return record;
            }

            // Distance is a tax on the attacker, never on the defender: fighting
            // near home is the one advantage a smaller power reliably has
            // (GDD §16). A state that has taken ground nearby, or holds basing
            // rights, is measured from there instead of from its capital.
            float reach = GeographySystem.ReachFactorFor(state, attackerId, target);
            record.reachFactor = reach;

            // `TotalPower` is 0..3; `garrison` is 0..100. Adding them raw made the
            // entire national army about four percent of the defence figure and
            // put a typical assault at roughly 8% odds regardless of what the
            // attacker had built — so procurement, readiness, doctrine, ISR,
            // reach and coalition support were all multipliers on a term too
            // small to matter. Everything below assumes one common scale.
            var profile = OperationCatalog.For(operationType);

            // What we are doing everywhere else (GDD §16). Distance already
            // priced *how far* the force has to go; this prices how much of the
            // force is already busy. Two wars on opposite sides of the world are
            // not two wars — the second is fought with what the first left over.
            float focus = TheatreSystem.FocusFactorFor(state, attackerId, target);
            record.theatre = TheatreSystem.Of(target);

            // Every term below records itself, so the after-action report can say
            // *why* rather than only what. Without this the only strategy
            // available to a player who failed was to try again unchanged.
            //
            // Computed by the same shared function the *preview* uses, so an
            // assessment offered before the order and the resolution that follows
            // it can never disagree. Two implementations of the same odds would
            // make a minister's advice quietly wrong.
            float attackPower = ComputePowers(state, attackerId, target, operationType,
                directive, coalitionSupport, out float defensePower, out var analysis);

            float speed = directive.speedPriority / 100f;

            // Doctrine shapes how the force fights (GDD §19). The loss and
            // civilian modifiers stay here because they affect the *aftermath*
            // rather than the odds, and the preview has no aftermath.
            float ownLossModifier = 1f;
            float enemyLossModifier = 1f;
            float civilianModifier = 1f - TechnologySystem.Effectiveness(attacker, "CAP_PRECISION") * 0.45f;
            switch (attacker.military.doctrine)
            {
                case MilitaryDoctrine.Maneuver:
                    ownLossModifier = 0.75f;
                    civilianModifier *= 1.35f;   // tempo over care
                    break;
                case MilitaryDoctrine.Attrition:
                    ownLossModifier = 1.30f;
                    enemyLossModifier = 1.45f;   // grind them down, and ourselves
                    break;
                case MilitaryDoctrine.Deterrence:
                    ownLossModifier = 0.85f;
                    civilianModifier *= 0.7f;
                    break;
            }

            float odds = analysis.odds;
            float roll = (float)rng.NextDouble();
            record.success = roll < odds;

            record.oddsAtOrder = odds;
            record.explanation = analysis.Explain(
                record.success, attackerId == state.playerCountryId,
                target.displayName, operationType);

            // How much force is actually committed, which sets both sides' losses
            // and the civilian toll. A special operation risks a handful of
            // people; an assault risks an army.
            float intensity = profile?.intensity ?? 1f;
            float casualtyAppetite = 0.6f + directive.casualtyTolerance / 100f * 0.8f;

            // Machines where people used to be: our own casualties fall for the
            // same effect (`CAP_AUTONOMY`, spec 13 §6). It buys nothing on the
            // other side of the ledger — the enemy's losses and the civilian
            // harm below are untouched, which is what makes it a capability
            // about *us* rather than a general force multiplier.
            record.attackerLosses = intensity * casualtyAppetite * ownLossModifier
                                    * (1f - TechnologySystem.Effectiveness(attacker, "CAP_AUTONOMY") * 0.30f)
                                    * (record.success ? 3.5f : 6.5f) * (0.7f + (float)rng.NextDouble() * 0.6f);
            record.defenderLosses = intensity * enemyLossModifier
                                    * (record.success ? 7f : 3.5f) * (0.7f + (float)rng.NextDouble() * 0.6f);

            // Civilian harm scales with speed, permissiveness and doctrine (GDD §27),
            // but not with committed weight alone — how the force is applied
            // matters more than how much of it there is. Standoff fires against
            // a populated position kill civilians out of all proportion to the
            // troops committed; a small precise action does the opposite. Without
            // this the order screen's warning about an air strike was untrue,
            // because its lower intensity made it gentler than an assault.
            civilianModifier *= profile?.civilianFactor ?? 1f;

            record.civilianHarm = intensity * (directive.civilianRiskLimit / 100f) * (0.4f + speed * 0.8f) * civilianModifier *
                                  (target.type == LocationType.Capital || target.type == LocationType.IndustrialCenter ? 1.6f : 1f) *
                                  (float)(1.0 + rng.NextDouble());

            ApplyOperationCosts(state, confrontation, attacker, defender, target, record, operationType, directive);

            // A wartime operation belongs to its confrontation's after-action
            // record. A peacetime programme on our own ground belongs to the
            // national record instead — it is not part of anybody's war, and
            // filing it under one would be the wrong history.
            if (confrontation != null) confrontation.operations.Add(record);
            else if (record.success)
                state.AddChronicle(ChronicleCategory.Military, attackerId, record.summary);

            return record;
        }

        /// <summary>
        /// Everything that decides the odds, with no side effects (GDD §19, §28.1).
        ///
        /// Shared by `ResolveOperation` and by the preview a minister uses to
        /// advise. **One implementation, deliberately**: an assessment offered
        /// before the order and the resolution that follows it must agree, and
        /// two copies of a formula this long would drift within a month — the
        /// same lesson as the monthly system list and the text-wrapping rule.
        /// </summary>
        public static float ComputePowers(GameState state, string attackerId,
            StrategicLocation target, OperationType operationType, OperationDirective directive,
            float coalitionSupport, out float defensePower, out OperationAnalysis analysis)
        {
            analysis = new OperationAnalysis();
            defensePower = 0f;

            var attacker = state.FindCountry(attackerId);
            if (attacker == null || target == null) return 0f;

            var defender = state.FindCountry(target.ownerId);
            var profile = OperationCatalog.For(operationType);
            var theatre = TheatreSystem.Of(target);

            float reach = GeographySystem.ReachFactorFor(state, attackerId, target);
            float focus = TheatreSystem.FocusFactorFor(state, attackerId, target);

            float basePower = OperationPower(attacker, operationType) * PowerScale;
            float attackPower = basePower * reach * focus
                                + Math.Max(0f, coalitionSupport) * PowerScale;

            // Experience is already inside EffectivePower, so it is already
            // deciding the outcome. Recording it is what lets the after-action
            // report *name* it — otherwise a green force loses and the report
            // ranks everything except the reason.
            float experienceFactor = WeightedExperienceFactor(attacker.military, operationType);
            analysis.Record(OperationAnalysis.Labels.Experience, experienceFactor, false,
                $"{DescribeForceExperience(attacker.military, operationType)}");

            analysis.Record(OperationAnalysis.Labels.Reach, reach, false,
                $"force arrives at {reach * 100f:F0}% weight");
            analysis.Record(OperationAnalysis.Labels.Commitment, focus, false,
                $"{TheatreSystem.CommitmentElsewhere(state, attackerId, theatre):F1} " +
                "committed in other theatres");
            if (coalitionSupport > 0.01f && basePower > 0.01f)
                analysis.Record(OperationAnalysis.Labels.Coalition,
                    1f + coalitionSupport * PowerScale / basePower, false,
                    $"+{coalitionSupport * PowerScale:F0} weight");

            defensePower = DefensePowerFor(profile, attacker, defender, target);

            // A position with nothing left in it should fall, not grind (GDD §19).
            //
            // **Only where the position is what we are fighting.** On our own
            // ground the opposition is the insurgency, or nobody — and reading
            // *our* thin garrison as the defence collapsing had it exactly
            // backwards: a lightly held occupation is harder to pacify, not
            // easier, and a fortification programme is not opposed by the works
            // it is building.
            float depletion = profile?.targeting == OperationTargeting.OwnGround
                ? 1f
                : DepletionFactor(defender, target);
            if (depletion < 0.999f)
            {
                defensePower *= depletion;
                analysis.Record(OperationAnalysis.Labels.Depleted, 1f / Math.Max(0.05f, depletion),
                    false, $"garrison {target.garrison:F0}, works {target.defenseValue:F0}");
            }

            RecordDefence(analysis, profile, defender, target, defensePower, attackPower);

            // Knowing how the other side fights outlives the friendship that
            // produced it (GDD §15.3). There is no other side on our own ground.
            if (defender != null && defender.id != attackerId)
            {
                var relationship = state.FindRelationship(attacker.id, defender.id);
                if (relationship != null && relationship.doctrineFamiliarity > 0f)
                {
                    attackPower *= 1f + relationship.doctrineFamiliarity / 320f;
                    defensePower *= 1f + relationship.doctrineFamiliarity / 400f;
                    analysis.Record(OperationAnalysis.Labels.Familiarity,
                        1f + relationship.doctrineFamiliarity / 320f, false,
                        $"familiarity {relationship.doctrineFamiliarity:F0}");
                }
            }

            // A shield stops the things it was built to stop.
            if (defender != null && IsAerialDelivery(operationType))
            {
                float shield = 1f - defender.military.missileDefense / 220f;
                attackPower *= shield;
                analysis.Record(OperationAnalysis.Labels.MissileDefence, shield, false,
                    $"shield {defender.military.missileDefense:F0}");
            }

            // Speed buys tempo at the cost of preparation; sieges invert this.
            float speed = directive.speedPriority / 100f;
            switch (operationType)
            {
                case OperationType.Assault:
                case OperationType.AmphibiousAssault:
                    attackPower *= 0.85f + speed * 0.35f;
                    analysis.Record(OperationAnalysis.Labels.Speed, 0.85f + speed * 0.35f, false,
                        $"speed priority {directive.speedPriority:F0}");
                    break;
                case OperationType.Siege:
                    attackPower *= 0.75f; defensePower *= 0.7f;
                    break;
                case OperationType.Raid:
                    attackPower *= 0.6f; defensePower *= 0.8f;
                    break;
            }

            // Capability changes what the force can do with what it has (GDD §11).
            float isr = TechnologySystem.Effectiveness(attacker, "CAP_ISR");
            if (isr > 0f)
            {
                attackPower *= 1f + isr * 0.15f;
                analysis.Record(OperationAnalysis.Labels.Isr, 1f + isr * 0.15f, false,
                    $"ISR capability {isr * 100f:F0}%");
            }

            switch (attacker.military.doctrine)
            {
                case MilitaryDoctrine.Maneuver:
                    attackPower *= 1.12f;
                    analysis.Record(OperationAnalysis.Labels.Doctrine, 1.12f, false, "manoeuvre");
                    break;
                case MilitaryDoctrine.Attrition:
                    attackPower *= 1.05f;
                    analysis.Record(OperationAnalysis.Labels.Doctrine, 1.05f, false, "attrition");
                    break;
                case MilitaryDoctrine.Deterrence:
                    attackPower *= 0.88f;
                    analysis.Record(OperationAnalysis.Labels.Doctrine, 0.88f, false,
                        "deterrence — built to threaten, not to take");
                    break;
            }

            analysis.attackPower = attackPower;
            analysis.defensePower = defensePower;
            analysis.odds = attackPower / Math.Max(0.01f, attackPower + defensePower);
            return attackPower;
        }

        /// <summary>
        /// The odds an order would face, without ordering it. Used by the cabinet
        /// to advise and by the order screen to say what it is asking for.
        /// </summary>
        public static float EstimateOdds(GameState state, string attackerId,
            StrategicLocation target, OperationType operationType,
            OperationDirective directive, float coalitionSupport)
        {
            ComputePowers(state, attackerId, target, operationType, directive,
                coalitionSupport, out _, out var analysis);
            return analysis.odds;
        }

        /// <summary>
        /// How much a stripped position still resists (GDD §19).
        ///
        /// Below `DepletedGarrison` the defence collapses toward nothing rather
        /// than scaling smoothly, because a position with no troops and no works
        /// is not lightly held — it is open. This is what makes a siege, a
        /// bombardment or a long interdiction campaign pay off with something the
        /// operator can see: the assault that follows walks in.
        ///
        /// A country whose whole branch inventory is spent cannot reinforce it
        /// either, which is the other half of counting equipment.
        /// </summary>
        public static float DepletionFactor(CountryState defender, StrategicLocation target)
        {
            const float DepletedGarrison = 22f;
            const float DepletedWorks = 20f;

            float garrisonShare = Math.Min(1f, target.garrison / DepletedGarrison);
            float worksShare = Math.Min(1f, target.defenseValue / DepletedWorks);

            // Either one being intact is enough to hold; both being gone is what
            // opens the position.
            float holding = Math.Max(garrisonShare, worksShare * 0.7f);

            // Nothing left to reinforce with anywhere in the army.
            if (defender != null && defender.military.ground.inventory.IsSpent)
                holding *= 0.5f;

            // Floored rather than zeroed: somebody is always in the way, and a
            // guaranteed capture would make the last step of a campaign a
            // formality rather than a decision.
            return Math.Max(0.12f, holding);
        }

        /// <summary>
        /// Record the defence as a multiplier on the attacker's chances, so the
        /// report can rank "their fortifications" against "distance" on one
        /// scale. Expressed relative to the attack power it is opposing, because
        /// a garrison of 60 means something different to a large army than to a
        /// small one.
        /// </summary>
        static void RecordDefence(OperationAnalysis analysis, OperationProfile profile,
            CountryState defender, StrategicLocation target, float defensePower, float attackPower)
        {
            if (defensePower <= 0.01f || attackPower <= 0.01f) return;

            // How much the defence divides our chances by.
            float multiplier = 1f + defensePower / attackPower;
            var model = profile?.defense ?? DefenseModel.Garrison;

            switch (model)
            {
                case DefenseModel.Fortifications:
                    analysis.Record(OperationAnalysis.Labels.Fortifications, multiplier, true,
                        $"defence value {target.defenseValue:F0}");
                    break;
                case DefenseModel.AirDefenses:
                    analysis.Record(OperationAnalysis.Labels.Fortifications, multiplier, true,
                        $"air defences over a garrison of {target.garrison:F0}");
                    break;
                case DefenseModel.EnemyAir:
                    analysis.Record(OperationAnalysis.Labels.EnemyAir, multiplier, true,
                        $"air strength {defender?.military.air.strength:F0}");
                    break;
                case DefenseModel.EnemyNavy:
                    analysis.Record(OperationAnalysis.Labels.EnemyNavy, multiplier, true,
                        $"naval strength {defender?.military.naval.strength:F0}");
                    break;
                case DefenseModel.CounterIntel:
                    analysis.Record(OperationAnalysis.Labels.CounterIntelligence, multiplier, true,
                        $"counterintelligence {defender?.counterIntel.counterIntelligence:F0}");
                    break;
                case DefenseModel.Insurgency:
                    analysis.Record(OperationAnalysis.Labels.Insurgency, multiplier, true,
                        $"pacification {target.pacification:F0}");
                    break;
                case DefenseModel.Unopposed:
                    // Recorded, not skipped. Nobody is holding the ground, but the
                    // work still has a size, and a report that names no factor at
                    // all cannot say what would change the outcome.
                    analysis.Record(OperationAnalysis.Labels.Undertaking, multiplier, true,
                        $"unopposed work at {target.displayName}");
                    break;
                default:
                    analysis.Record(OperationAnalysis.Labels.Garrison, multiplier, true,
                        $"garrison {target.garrison:F0}, works {target.defenseValue:F0}");
                    break;
            }
        }

        /// <summary>
        /// What this operation is actually fought against (GDD §19).
        ///
        /// The single most important idea in the verb list. If everything
        /// resolves against the garrison then every verb is an assault with a
        /// different name, and the whole list collapses back into one button.
        /// </summary>
        static float DefensePowerFor(OperationProfile profile, CountryState attacker,
            CountryState defender, StrategicLocation target)
        {
            var model = profile?.defense ?? DefenseModel.Garrison;
            float scale = profile?.defenseScale ?? 1f;
            float power;

            switch (model)
            {
                case DefenseModel.Fortifications:
                    // Contested by the works it exists to break, and nothing else.
                    power = target.defenseValue * 0.55f;
                    break;

                case DefenseModel.AirDefenses:
                    // Air power is not stopped by infantry, only by air defence.
                    // Layered interception is the capability that makes the
                    // difference (`CAP_AIRDEFENSE`) — and hypersonics are what
                    // nothing currently fielded can intercept, so they read
                    // straight through it.
                    power = target.garrison * 0.12f * (1f + target.defenseValue / 90f)
                            * (1f + TechnologySystem.Effectiveness(defender, "CAP_AIRDEFENSE") * 0.7f);
                    if (TechnologySystem.Has(attacker, "CAP_HYPERSONIC")) power *= 0.55f;
                    break;

                case DefenseModel.EnemyAir:
                    power = defender != null
                        ? defender.military.air.EffectivePower * PowerScale * 1.1f
                        : target.garrison * 0.2f;
                    break;

                case DefenseModel.EnemyNavy:
                    // Quiet hulls and the sensors to hunt them: naval fighting
                    // resolves on capability rather than on tonnage
                    // (`CAP_UNDERSEA`).
                    power = defender != null
                        ? defender.military.naval.EffectivePower * PowerScale * 1.1f
                          * (1f + TechnologySystem.Effectiveness(defender, "CAP_UNDERSEA") * 0.45f)
                        : target.garrison * 0.2f;
                    break;

                case DefenseModel.CounterIntel:
                    // A large army is nearly irrelevant; a good security service
                    // is the whole defence.
                    power = target.garrison * 0.15f
                            + (defender?.counterIntel.counterIntelligence ?? 0f) * 0.55f;
                    break;

                case DefenseModel.Insurgency:
                    // Resistance scales with how much of their country we hold and
                    // how little of it we have pacified.
                    power = target.strategicValue * 0.30f
                            + Math.Max(0f, 60f - target.pacification) * 0.45f;
                    break;

                case DefenseModel.Unopposed:
                    // Construction, escort, withdrawal. Something can still go
                    // wrong, but nobody is holding the ground against us.
                    power = 12f + target.defenseValue * 0.05f;
                    break;

                default: // Garrison — the standard land fight
                    power = defender != null
                        ? defender.military.TotalPower * PowerScale * 0.55f + target.garrison * 0.35f
                        : target.garrison * 0.5f;
                    power *= 1f + target.defenseValue / 140f;
                    break;
            }

            return power * scale;
        }

        /// <summary>
        /// Operations that arrive through the air, and so can be met by a shield.
        /// Missile defence would be worthless if it stopped infantry.
        /// </summary>
        public static bool IsAerialDelivery(OperationType operationType)
        {
            switch (operationType)
            {
                case OperationType.AirStrike:
                case OperationType.StrategicBombing:
                case OperationType.LeadershipStrike:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// What a successful operation that does not take ground actually does.
        ///
        /// The point of the wider verb list: each of these leaves the map
        /// unchanged and the *situation* different. Suppression is the clearest
        /// case — it takes nothing, kills almost nobody, and makes every later
        /// operation against that position easier, which is a strategic move the
        /// three-verb list had no way to express.
        /// </summary>
        static string ApplyNonCapturingSuccess(GameState state, CountryState attacker,
            CountryState defender, StrategicLocation target, OperationType operationType)
        {
            switch (operationType)
            {
                case OperationType.SuppressDefenses:
                    // Permanent, and the whole reason to do it.
                    target.defenseValue = Clamp(target.defenseValue - 18f);
                    return $"{target.displayName}'s defences degraded. Fortifications and air " +
                           "cover will not be what they were when we come back.";

                case OperationType.AirStrike:
                    target.garrison = Clamp(target.garrison - 14f);
                    StrategicConnections.Disrupt(state, target);
                    if (defender != null)
                    {
                        defender.resources.industrialCapacity =
                            Clamp(defender.resources.industrialCapacity - target.strategicValue * 0.03f);
                        defender.stability = Clamp(defender.stability - 1.5f);
                    }
                    return $"Strikes on {target.displayName} landed. Garrison and works damaged."
                        + (StrategicConnections.IsEndpoint(target.id) ? " Modelled connections at this endpoint are disrupted for at least 6 months; the holder can repair them." : "");

                case OperationType.NavalBlockade:
                    if (defender != null)
                    {
                        // Strategic, not tactical: it is the country that suffers.
                        foreach (var link in state.trade)
                            if (link.Involves(defender.id)) link.volume = Clamp(link.volume - 8f);

                        defender.military.ground.supply = Clamp(defender.military.ground.supply - 6f);
                        defender.military.naval.supply = Clamp(defender.military.naval.supply - 8f);
                        defender.economy.confidence = Clamp(defender.economy.confidence - 4f);
                    }
                    return $"The sea lanes to {defender?.displayName} are closed. Their trade " +
                           "and sustainment are being strangled.";

                case OperationType.SpecialOperation:
                    target.garrison = Clamp(target.garrison - 10f);
                    if (defender != null)
                        defender.government.militaryLoyalty =
                            Clamp(defender.government.militaryLoyalty - 2f);
                    return $"The action at {target.displayName} succeeded and was over before " +
                           "anyone could respond.";

                case OperationType.Siege:
                    target.garrison = Clamp(target.garrison - 12f);
                    return $"{target.displayName} isolated; garrison degraded.";

                // ---------------- ground ----------------

                case OperationType.PreparedDefense:
                    // Permanent, on ground we already hold. The cheapest way to
                    // make a position expensive, and the only verb that raises a
                    // defence value rather than lowering one.
                    target.defenseValue = Clamp(target.defenseValue + 9f);
                    target.garrison = Clamp(target.garrison + 4f);
                    return $"{target.displayName} fortified. Anyone coming for it will pay more.";

                case OperationType.CounterInsurgency:
                    target.pacification = Clamp(target.pacification + 12f);
                    if (attacker != null)
                        attacker.stability = Clamp(attacker.stability + 0.8f);

                    // If there is an actual movement there, this is what it is
                    // for. Raising `pacification` alone would leave the fighters
                    // untouched while the readout said the operation worked —
                    // an operation that appears to succeed and changes nothing
                    // the operator was aiming at.
                    InsurgencySystem.OnCounterInsurgency(state, target, 1f);

                    return $"Security operations across {target.displayName}. The population is "
                           + "quieter, and not necessarily more willing.";

                // ---------------- naval ----------------

                case OperationType.SeaControl:
                    if (defender != null)
                    {
                        // The fleet itself is the objective. Everything naval we
                        // try afterwards resolves against what is left of it.
                        defender.military.naval.SetStrength(defender.military.naval.strength - 7f);
                        defender.military.naval.readiness = Clamp(defender.military.naval.readiness - 9f);
                    }
                    return $"The waters off {target.displayName} are ours. Their fleet will not "
                           + "contest them again soon.";

                case OperationType.CommerceRaiding:
                    if (defender != null)
                    {
                        foreach (var link in state.trade)
                            if (link.Involves(defender.id)) link.volume = Clamp(link.volume - 4f);
                        defender.resources.treasury = Math.Max(0f, defender.resources.treasury - 70f);
                        defender.economy.confidence = Clamp(defender.economy.confidence - 2.5f);
                    }
                    return $"Their merchant shipping is being hunted. Insurance and absence are "
                           + "doing what a blockade would do, more slowly and for less.";

                case OperationType.MineWarfare:
                    // A temporary hazard, not permanent damage to trade agreements.
                    target.mineHazardUntilMonth = Math.Max(target.mineHazardUntilMonth,
                        state.date.year * 12 + state.date.month + MineHazardMonths);
                    target.defenseValue = Clamp(target.defenseValue - 6f);
                    if (defender != null)
                    {
                        defender.military.naval.supply = Clamp(defender.military.naval.supply - 10f);
                    }
                    return $"{target.displayName} is mined: {MineMonthsRemaining(state, target)} months of disruption remain. "
                           + "Mapped links dependent on this port or named passage lose up to 6 effective volume; unmodelled links use the national-holder rule. Trade is not closed. "
                           + "Hazards do not stack. Routine clearance ends the disruption; successful Convoy Escort here removes 3 months.";

                case OperationType.ConvoyEscort:
                    // The defensive answer to a blockade or a raiding campaign.
                    if (attacker != null)
                    {
                        foreach (var link in state.trade)
                            if (link.Involves(attacker.id)) link.volume = Clamp(link.volume + 5f);
                        attacker.economy.confidence = Clamp(attacker.economy.confidence + 3f);
                        attacker.military.naval.supply = Clamp(attacker.military.naval.supply + 4f);
                    }
                    int cleared = Math.Min(MineEscortMonths, MineMonthsRemaining(state, target));
                    if (cleared > 0) target.mineHazardUntilMonth -= cleared;
                    return "Our shipping is running under escort. Trade and sustainment improved."
                        + (cleared > 0 ? $" Clearance at {target.displayName} removed {cleared} months of mine disruption; "
                            + $"{MineMonthsRemaining(state, target)} remain. Other mined sites are unaffected." : "");

                // ---------------- air ----------------

                case OperationType.CounterAirCampaign:
                    if (defender != null)
                    {
                        // The only thing in the list that destroys enemy air power.
                        defender.military.air.SetStrength(defender.military.air.strength - 8f);
                        defender.military.air.readiness = Clamp(defender.military.air.readiness - 10f);
                    }
                    return "Their air force was met and beaten. The sky over this theatre is ours.";

                case OperationType.NoFlyZone:
                    if (defender != null)
                    {
                        // Grounds them rather than destroying them — which is why
                        // it must be held, and why it is priced as it is.
                        defender.military.air.readiness = Clamp(defender.military.air.readiness - 16f);
                        defender.military.air.supply = Clamp(defender.military.air.supply - 10f);
                        defender.stability = Clamp(defender.stability - 1f);
                    }
                    return $"A no-fly zone is in force over {target.displayName}. Their aircraft "
                           + "are intact and on the ground.";

                case OperationType.AirInterdiction:
                    target.garrison = Clamp(target.garrison - 6f);
                    if (defender != null)
                    {
                        defender.military.ground.supply = Clamp(defender.military.ground.supply - 11f);
                        defender.military.naval.supply = Clamp(defender.military.naval.supply - 4f);
                    }
                    return $"The roads and rail behind {target.displayName} are cut. Whatever we "
                           + "attack next month will be hungrier than it is today.";

                case OperationType.StrategicBombing:
                    if (defender != null)
                    {
                        defender.resources.industrialCapacity =
                            Clamp(defender.resources.industrialCapacity - 5f);
                        defender.resources.energy = Clamp(defender.resources.energy - 4f);
                        defender.economy.confidence = Clamp(defender.economy.confidence - 7f);
                        // A bombed population does not sue for peace. It hardens.
                        defender.warSupport = Clamp(defender.warSupport + 5f);
                    }
                    return $"The war-making capacity around {target.displayName} is burning. "
                           + "So is their willingness to stop.";

                case OperationType.LeadershipStrike:
                    if (defender != null)
                    {
                        defender.government.militaryLoyalty =
                            Clamp(defender.government.militaryLoyalty - 9f);
                        defender.stability = Clamp(defender.stability - 7f);
                        // They close ranks behind whoever survives.
                        defender.warSupport = Clamp(defender.warSupport + 14f);
                        defender.nationalUnity = Clamp(defender.nationalUnity + 6f);
                    }
                    if (attacker != null)
                    {
                        // Legitimacy is the price, and it is charged whether or
                        // not the strike achieved anything.
                        attacker.pillars.diplomacy = Clamp(attacker.pillars.diplomacy - 7f);
                        foreach (var relationship in state.relationships)
                        {
                            if (!relationship.Involves(attacker.id)) continue;
                            relationship.trust = Clamp(relationship.trust - 6f);
                        }
                    }
                    state.AddChronicle(ChronicleCategory.Military, attacker?.id,
                        $"Strike on the seat of government at {target.displayName}.", Publicity.Public);
                    return $"The strike on {target.displayName} landed. Their government is "
                           + "damaged, their country is united, and ours has explaining to do.";

                // ---------------- joint ----------------

                case OperationType.AmphibiousAssault:
                    // Capture is handled by the seizure path; this is the case
                    // where the landing succeeded but the order was not to hold.
                    target.garrison = Clamp(target.garrison - 16f);
                    return $"The landing at {target.displayName} went in and the beachhead held.";

                case OperationType.CyberOperation:
                    StrategicConnections.Disrupt(state, target);
                    if (defender != null)
                    {
                        // Command and coordination, not steel.
                        defender.military.ground.readiness = Clamp(defender.military.ground.readiness - 5f);
                        defender.military.air.readiness = Clamp(defender.military.air.readiness - 5f);
                        defender.military.naval.readiness = Clamp(defender.military.naval.readiness - 5f);
                        defender.economy.confidence = Clamp(defender.economy.confidence - 3f);
                    }
                    return "Their networks are degraded. Nothing is broken that anyone can "
                           + "photograph, and nothing they do this month will go smoothly."
                           + (StrategicConnections.IsEndpoint(target.id) ? " Modelled connections at this endpoint are disrupted for at least 6 months; the holder can repair them." : "");

                case OperationType.MissileDefense:
                    if (attacker != null)
                        attacker.military.missileDefense =
                            Clamp(attacker.military.missileDefense + 10f);
                    return "Another layer of the shield is live. What arrives through the air "
                           + "now has to get through it first.";

                case OperationType.NoncombatantEvacuation:
                    if (attacker != null)
                    {
                        attacker.stability = Clamp(attacker.stability + 1.5f);
                        attacker.warSupport = Clamp(attacker.warSupport + 3f);
                    }
                    return "Our nationals are out. Whatever happens here next, they are not "
                           + "part of the bargaining.";

                default:
                    target.garrison = Clamp(target.garrison - 8f);
                    return $"Raid on {target.displayName} succeeded; enemy sustainment damaged.";
            }
        }

        /// <summary>
        /// Where our own losses come off. A strike costs aircraft, a blockade
        /// costs ships, and only the operations that put people on the ground
        /// cost the army.
        /// </summary>
        /// <summary>
        /// What the branches that actually fought learn from an operation.
        ///
        /// Distributed by the same weights that decided the outcome and took the
        /// losses, so a blockade teaches the fleet and nothing else.
        ///
        /// Two deliberate shapes:
        /// - **A hard fight teaches more than a walkover.** Gain scales with the
        ///   losses the branch took, so grinding down a defended position is worth
        ///   more than a dozen unopposed sorties. Otherwise the optimal way to
        ///   build a veteran army is to attack the weakest thing on the map
        ///   repeatedly, which is not a lesson about war.
        /// - **Losing teaches too, slightly less.** A failed assault is where an
        ///   army learns what it cannot do, and `OperationAnalysis` already exists
        ///   to tell the operator the same thing. Zero on defeat would make the
        ///   whole mechanic a reward for already being strong.
        ///
        /// Diminishing: the closer to 100, the less there is left to learn, so a
        /// long war produces a seasoned force rather than an invincible one.
        /// </summary>
        /// <summary>
        /// The experience multiplier the branches doing this operation contribute,
        /// weighted the same way their power is. Reported, not applied — the
        /// factor is already inside <see cref="BranchForce.EffectivePower"/>.
        /// </summary>
        public static float WeightedExperienceFactor(MilitaryState mil, OperationType operationType)
        {
            var profile = OperationCatalog.For(operationType);
            if (mil == null || profile == null || profile.usesIntelligence) return 1f;

            float weight = profile.ground + profile.air + profile.naval;
            if (weight <= 0.01f) return 1f;

            return (mil.ground.ExperienceFactor * profile.ground
                    + mil.air.ExperienceFactor * profile.air
                    + mil.naval.ExperienceFactor * profile.naval) / weight;
        }

        /// <summary>Plain-language band for the branch that carries this operation.</summary>
        public static string DescribeForceExperience(MilitaryState mil, OperationType operationType)
        {
            var profile = OperationCatalog.For(operationType);
            if (mil == null || profile == null) return "";

            var lead = mil.ground;
            if (profile.air >= profile.ground && profile.air >= profile.naval) lead = mil.air;
            else if (profile.naval >= profile.ground && profile.naval >= profile.air) lead = mil.naval;

            return $"{lead.ExperienceBand.ToLowerInvariant()} ({lead.experience:F0})";
        }

        /// <param name="odds">
        /// The chance this side was given before the order — low odds mean a hard
        /// fight, and a hard fight is what teaches. Pass the defender's view
        /// (1 − attacker odds) when awarding to the defender.
        ///
        /// **Measured backwards on the first attempt.** This scaled with the
        /// losses the branch took, on the reasoning that a bloody fight teaches
        /// more. It does not work: losses are driven by the verb's intensity and
        /// our own casualty appetite far more than by what we were up against, and
        /// an easy operation succeeds every time while a hard one takes the
        /// reduced defeat share. Walking into undefended ground five times taught
        /// 10.7; grinding down a fortified position taught 6.6 — the exact
        /// inversion of the intent. The odds are the only term in the resolution
        /// that actually knows how hard the fight was.
        /// </param>
        static void AwardExperience(MilitaryState mil, OperationType operationType,
            float odds, bool success)
        {
            if (mil == null) return;

            var profile = OperationCatalog.For(operationType);
            if (profile == null || profile.usesIntelligence) return;

            float difficulty = 1f - (odds < 0f ? 0f : odds > 1f ? 1f : odds);
            float scale = (success ? 1f : 0.75f) * (0.45f + 1.75f * difficulty);

            void Learn(BranchForce force, float weight)
            {
                if (force == null || weight <= 0.01f) return;
                float room = (100f - force.experience) / 100f;   // less left to learn
                force.experience = Clamp(force.experience + scale * weight * room * 2.2f);
            }

            Learn(mil.ground, profile.ground);
            Learn(mil.air, profile.air);
            Learn(mil.naval, profile.naval);
        }

        static void ApplyAttackerAttrition(MilitaryState mil, OperationType operationType, float losses)
        {
            var profile = OperationCatalog.For(operationType);
            if (profile == null)
            {
                mil.ground.SetStrength(mil.ground.strength - losses * 0.6f);
                return;
            }

            // Cyber costs the intelligence service, not the armed forces.
            if (profile.usesIntelligence) return;

            // Distributed by the same weights that decided the outcome, so this
            // can never fall out of step as verbs are added. Weights need not sum
            // to one — a special operation's do not — and normalising is what
            // makes its losses small in the right way.
            float total = profile.ground + profile.air + profile.naval;
            if (total <= 0.001f) return;

            float bill = losses * 0.6f;
            mil.ground.SetStrength(mil.ground.strength - bill * (profile.ground / total));
            mil.air.SetStrength(mil.air.strength - bill * (profile.air / total));
            mil.naval.SetStrength(mil.naval.strength - bill * (profile.naval / total));
        }

        /// <summary>
        /// Where the enemy's losses come off — the force the operation actually
        /// engaged, not the garrison by default.
        ///
        /// This is what stops a wider verb list from collapsing back into one
        /// verb: if every operation grinds the garrison, then blockade, strike
        /// and suppression are all just slower assaults and there is no reason
        /// to think about which to use.
        /// </summary>
        static void ApplyDefenderAttrition(CountryState defender, StrategicLocation target,
            OperationType operationType, float losses)
        {
            // Keyed by what defended, mirroring DefensePowerFor exactly. The two
            // must agree: computing the odds against their fleet and then billing
            // the losses to a garrison is how repeated blockades used to empty a
            // position the navy never went near.
            var model = OperationCatalog.For(operationType)?.defense ?? DefenseModel.Garrison;

            switch (model)
            {
                case DefenseModel.EnemyNavy:
                    if (defender != null)
                        defender.military.naval.strength =
                            Clamp(defender.military.naval.strength - losses * 0.5f);
                    break;

                case DefenseModel.EnemyAir:
                    if (defender != null)
                        defender.military.air.strength =
                            Clamp(defender.military.air.strength - losses * 0.5f);
                    break;

                case DefenseModel.Fortifications:
                    target.defenseValue = Clamp(target.defenseValue - losses * 0.5f);
                    if (defender != null)
                        defender.military.air.strength =
                            Clamp(defender.military.air.strength - losses * 0.4f);
                    break;

                case DefenseModel.AirDefenses:
                    target.garrison = Clamp(target.garrison - losses * 0.7f);
                    if (defender != null)
                        defender.military.air.strength =
                            Clamp(defender.military.air.strength - losses * 0.25f);
                    break;

                case DefenseModel.CounterIntel:
                    target.garrison = Clamp(target.garrison - losses * 0.4f);
                    break;

                case DefenseModel.Insurgency:
                    // What is worn down is the resistance itself.
                    target.pacification = Clamp(target.pacification + losses * 0.3f);
                    break;

                case DefenseModel.Unopposed:
                    // Nobody was holding it against us.
                    break;

                default:
                    target.garrison = Clamp(target.garrison - losses);
                    if (defender != null)
                        defender.military.ground.strength =
                            Clamp(defender.military.ground.strength - losses * 0.3f);
                    break;
            }
        }

        static void ApplyOperationCosts(
            GameState state, Confrontation confrontation,
            CountryState attacker, CountryState defender,
            StrategicLocation target, OperationRecord record, OperationType operationType,
            OperationDirective directive)
        {
            // Force degradation. Losses land on whatever actually fought — the
            // same branch weighting that decided the outcome. Charging every
            // operation to the garrison and the army made the verb list a lie:
            // repeated blockades emptied a garrison the fleet never engaged, and
            // the ground force paid for sorties it never flew.
            ApplyAttackerAttrition(attacker.military, operationType, record.attackerLosses);
            attacker.military.ground.supply = Clamp(attacker.military.ground.supply - 6f);
            attacker.military.air.supply = Clamp(attacker.military.air.supply - 4f);

            // **On our own ground there is no opponent.** `defender` is whoever
            // owns the target, which for a defensive programme is us — so every
            // "and now bill the other side" line below was billing us a second
            // time: manpower twice over, war exhaustion twice over, and the
            // experience of both winning and losing the same engagement.
            // Fortifying a position we hold cost more than attacking one we did
            // not, which is not a difficulty setting, it is an accounting error.
            bool opposed = defender != null && defender.id != attacker.id;
            if (!opposed) defender = null;

            ApplyDefenderAttrition(defender, target, operationType, record.defenderLosses);

            // What the branches that fought take away from it. Both sides learn —
            // the defender arguably more, since surviving an attack teaches you
            // where you were weak.
            AwardExperience(attacker.military, operationType, record.oddsAtOrder, record.success);
            if (defender != null)
                AwardExperience(defender.military, operationType, 1f - record.oddsAtOrder, !record.success);

            attacker.resources.treasury -= 45f;
            // A country cannot spend more people than it has.
            attacker.resources.manpower =
                Math.Max(0f, attacker.resources.manpower - record.attackerLosses * 4f);
            if (defender != null)
                defender.resources.manpower =
                    Math.Max(0f, defender.resources.manpower - record.defenderLosses * 4f);

            // Exhaustion and public cost accumulate on both sides — when there
            // are two sides. A programme conducted on our own ground in
            // peacetime has no war to exhaust, no momentum to swing and no
            // opponent to bill, which is why every one of these is guarded
            // rather than the whole path being duplicated.
            float attackerExhaustion = record.attackerLosses * 0.5f;
            float defenderExhaustion = record.defenderLosses * 0.4f;

            if (confrontation != null)
            {
                bool attackerIsInitiator = attacker.id == confrontation.initiatorId;
                if (attackerIsInitiator)
                {
                    confrontation.initiatorWarExhaustion += attackerExhaustion;
                    confrontation.defenderWarExhaustion += defenderExhaustion;
                    confrontation.initiatorCasualties += record.attackerLosses;
                    confrontation.defenderCasualties += record.defenderLosses;
                }
                else
                {
                    confrontation.defenderWarExhaustion += attackerExhaustion;
                    confrontation.initiatorWarExhaustion += defenderExhaustion;
                    confrontation.defenderCasualties += record.attackerLosses;
                    confrontation.initiatorCasualties += record.defenderLosses;
                }

                confrontation.civilianHarmTotal += record.civilianHarm;

                float momentumSwing = (record.success ? 9f : -6f)
                                      * (operationType == OperationType.Raid ? 0.5f : 1f);
                confrontation.momentum += attackerIsInitiator ? momentumSwing : -momentumSwing;
            }

            Causal.Apply(state, attacker.id, CausalMetric.WarExhaustion,
                CausalReason.MilitaryOperations, ref attacker.warExhaustion,
                Clamp(attacker.warExhaustion + attackerExhaustion * 0.5f), CausalCategory.Military,
                CausalKind.Direct, CausalVisibility.Known, defender?.id);
            if (defender != null)
                Causal.Apply(state, defender.id, CausalMetric.WarExhaustion,
                    CausalReason.MilitaryOperations, ref defender.warExhaustion,
                    Clamp(defender.warExhaustion + defenderExhaustion * 0.5f), CausalCategory.Military,
                    CausalKind.Direct, CausalVisibility.Known, attacker.id);

            // Civilian harm hardens enemy resistance and costs international standing (GDD §27).
            if (record.civilianHarm > 1.5f)
            {
                attacker.pillars.diplomacy = Clamp(attacker.pillars.diplomacy - record.civilianHarm * 0.25f);
                if (defender != null) defender.warSupport = Clamp(defender.warSupport + record.civilianHarm * 0.8f);
            }

            bool seizing = directive.territorialIntent == TerritorialIntent.Seize;

            if (record.success && CanTakeGround(operationType) && seizing)
            {
                TerritorySystem.RecordSeizure(state, target, attacker.id);
                target.ownerId = attacker.id;
                target.garrison = Math.Max(15f, target.garrison);
                attacker.warSupport = Clamp(attacker.warSupport + 6f);
                if (defender != null)
                {
                    defender.warSupport = Clamp(defender.warSupport + 4f); // rally under attack
                    defender.stability = Clamp(defender.stability - target.strategicValue * 0.06f);
                }
                record.summary = $"{target.displayName} captured. {attacker.displayName} forces hold the objective.";
                state.AddChronicle(ChronicleCategory.Military, attacker.id,
                    $"{target.displayName} captured by {attacker.displayName}.", Publicity.Public);
            }
            else if (record.success && CanTakeGround(operationType))
            {
                // Ordered to break the position, not to hold it (GDD §19).
                // Without this every successful assault annexed, so there was no
                // way to hurt a rival without also taking their land — and no way
                // to fight a limited war at all.
                target.garrison = Clamp(target.garrison - 25f);
                if (defender != null)
                    defender.stability = Clamp(defender.stability - target.strategicValue * 0.03f);
                record.summary = $"{target.displayName} overrun and abandoned. " +
                                 "The position was broken, not held.";
                state.AddChronicle(ChronicleCategory.Military, attacker.id,
                    $"{target.displayName} struck and left standing.", Publicity.Public);
            }
            else if (record.success)
            {
                record.summary = ApplyNonCapturingSuccess(
                    state, attacker, defender, target, operationType);
            }
            else if (OperationCatalog.For(operationType)?.targeting == OperationTargeting.OwnGround)
            {
                // **A programme on ground we hold is not a failed attack.**
                // This branch used to be shared with offensive operations, so
                // digging in at one of our own positions and not finishing the
                // work cost war support, was reported as an "operation against"
                // a place we own, and was announced to the world wire as a
                // public failure. Reported from play as exactly that: trying to
                // improve the defence of captured ground, failing repeatedly,
                // and being told nothing that could be acted on.
                record.summary = $"{OperationCatalog.For(operationType).displayName} at "
                                 + $"{target.displayName} did not achieve what was intended. "
                                 + "The works are unfinished and the effort is spent.";

                // Not on the wire. `Publicity.Secret` here means "ours to know" —
                // `WorldWire.CanShow` always shows us our own record, and
                // `ForMonth` keeps anything not Public off the wire. What our own
                // engineers did not manage is nobody else's news, and unlike a
                // battle the other side has no reason to have seen it.
                state.AddChronicle(ChronicleCategory.Military, attacker.id,
                    $"{OperationCatalog.For(operationType).displayName} at {target.displayName} "
                    + "fell short.", Publicity.Secret);
            }
            else
            {
                attacker.warSupport = Clamp(attacker.warSupport - 5f);
                record.summary = $"Operation against {target.displayName} failed. Losses absorbed without gain.";

                // Say why, when distance was the reason. A player who cannot see
                // that the force never arrived at full weight will read a run of
                // failures as the dice being unfair rather than as the map.
                if (record.reachFactor < 0.8f)
                    record.summary += $" Force projected at {record.reachFactor * 100f:F0}% "
                                      + $"of full weight ({GeographySystem.ReachText(record.reachFactor)}).";

                state.AddChronicle(ChronicleCategory.Military, attacker.id,
                    $"Failed operation at {target.displayName}.", Publicity.Public);
            }
        }

        static float MoveToward(float current, float target, float rate)
        {
            if (current < target) return Clamp(Math.Min(target, current + rate));
            return Clamp(Math.Max(target, current - rate));
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
