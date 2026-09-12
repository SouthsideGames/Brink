using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Strategic endgames (GDD §21).
    ///
    /// Every pillar can reach an effect capable of breaking an opponent. None of
    /// them is a button. Each requires:
    ///
    /// - a **capability** the state has actually built,
    /// - **preparation** measured in months of dedicated effort,
    /// - **conditions** that make it possible at all,
    ///
    /// and each produces systemic consequences — contagion, retaliation, and a
    /// permanent mark on how the world regards you. Intelligence destabilization
    /// does not guarantee a friendly successor. Economic collapse spreads.
    /// </summary>
    public static class EndgameSystem
    {
        public const int PreparationCost = 2;
        public const int ExecutionCost = 4;

        /// <summary>Monthly progress from one funded preparation effort.</summary>
        public const float PreparationRate = 4.5f;

        /// <summary>Treasury each authorization of preparation consumes.</summary>
        public const float PreparationTreasuryCost = 120f;

        // ---------- requirements ----------

        /// <summary>The capability without which an instrument cannot exist.</summary>
        public static string RequiredCapability(EndgameType type)
        {
            switch (type)
            {
                case EndgameType.StrategicDestruction: return "CAP_ISR";
                case EndgameType.SystemicCollapse: return "CAP_FINANCE";
                case EndgameType.StateDestabilization: return "CAP_ANALYTICS";
                case EndgameType.StrategicIsolation: return "CAP_CONVENING";
                default: return "CAP_CIVADMIN";
            }
        }

        /// <summary>
        /// Which part of the historical record an instrument's use belongs in.
        /// Mirrors <see cref="PillarOf"/>: the chronicle should file a financial
        /// collapse under Economic and a destabilization under Political, so the
        /// CHRONICLE filters and the world wire both find them where a reader
        /// would look.
        /// </summary>
        public static ChronicleCategory CategoryFor(EndgameType type)
        {
            switch (type)
            {
                case EndgameType.SystemicCollapse: return ChronicleCategory.Economic;
                case EndgameType.StrategicIsolation: return ChronicleCategory.Diplomatic;
                case EndgameType.StateDestabilization: return ChronicleCategory.Political;
                case EndgameType.TotalMobilization: return ChronicleCategory.Political;
                default: return ChronicleCategory.Military;
            }
        }

        public static Pillar PillarOf(EndgameType type)
        {
            switch (type)
            {
                case EndgameType.StrategicDestruction: return Pillar.Military;
                case EndgameType.SystemicCollapse: return Pillar.Economy;
                case EndgameType.StateDestabilization: return Pillar.Intelligence;
                case EndgameType.StrategicIsolation: return Pillar.Diplomacy;
                default: return Pillar.Government;
            }
        }

        public static string NameOf(EndgameType type)
        {
            switch (type)
            {
                case EndgameType.StrategicDestruction: return "Strategic Destruction";
                case EndgameType.SystemicCollapse: return "Systemic Financial Collapse";
                case EndgameType.StateDestabilization: return "State Destabilization";
                case EndgameType.StrategicIsolation: return "Strategic Isolation";
                default: return "Total National Mobilization";
            }
        }

        /// <summary>Whether a state could even begin preparing this instrument.</summary>
        public static bool CanPrepare(GameState state, CountryState country, EndgameType type, out string reason)
        {
            string capability = RequiredCapability(type);
            if (!TechnologySystem.Has(country, capability))
            {
                reason = $"Requires {CapabilityCatalog.Find(capability)?.name}.";
                return false;
            }
            if (TechnologySystem.Effectiveness(country, capability) < 0.6f)
            {
                reason = "We hold the capability but do not yet command it.";
                return false;
            }
            if (country.pillars.Get(PillarOf(type)) < 65f)
            {
                reason = $"Our {PillarOf(type)} pillar is not equal to it.";
                return false;
            }
            if (country.resources.treasury < PreparationTreasuryCost)
            {
                reason = $"Preparation costs {PreparationTreasuryCost:F0} and the treasury cannot fund it.";
                return false;
            }
            reason = "";
            return true;
        }

        /// <summary>Whether the instrument is prepared and conditions allow its use.</summary>
        public static bool CanExecute(GameState state, EndgameType type, string targetId, out string reason)
            => CanExecuteBy(state, state.playerCountryId, type, targetId, out reason);

        /// <summary>
        /// Actor-generic form of <see cref="CanExecute"/>. Every state on the map
        /// answers to the same conditions — the player holds no class of option
        /// that cannot be turned back on them.
        /// </summary>
        public static bool CanExecuteBy(GameState state, string actorId, EndgameType type,
            string targetId, out string reason)
        {
            var actor = state.FindCountry(actorId);
            if (actor == null) { reason = "No such state."; return false; }

            if (actor.endgames.ProgressFor(type) < 100f)
            {
                reason = "Preparation is incomplete.";
                return false;
            }

            if (type == EndgameType.TotalMobilization)
            {
                if (actor.endgames.totalMobilization) { reason = "Already mobilized."; return false; }
                if (actor.nationalUnity < 40f)
                {
                    reason = "The country is too divided to be mobilized.";
                    return false;
                }
                reason = "";
                return true;
            }

            var target = state.FindCountry(targetId);
            if (target == null) { reason = "No such state."; return false; }

            // These are instruments of a confrontation, not of routine diplomacy.
            var confrontation = state.ActiveConfrontationFor(actorId);
            if (confrontation == null || !confrontation.Involves(targetId))
            {
                reason = "Only usable against a state we are in confrontation with.";
                return false;
            }

            if (type == EndgameType.StrategicDestruction
                && confrontation.escalation < EscalationState.TotalWar)
            {
                reason = "Requires total war.";
                return false;
            }

            reason = "";
            return true;
        }

        // ---------- preparation ----------

        public static bool Prepare(GameState state, TurnManager turns, EndgameType type)
        {
            var player = state.PlayerCountry;
            if (!CanPrepare(state, player, type, out string reason))
            {
                GameLog.Warn("ENDGAME", $"Cannot prepare {NameOf(type)}: {reason}");
                return false;
            }
            if (player.endgames.ProgressFor(type) >= 100f)
            {
                GameLog.Warn("ENDGAME", "Preparation is already complete.");
                return false;
            }
            if (!turns.SpendCommandPoints(PreparationCost, $"Prepare {NameOf(type)}")) return false;

            // Credit nothing if the work did not actually happen.
            if (!PrepareBy(state, player.id, type)) return false;

            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 20, "Strategic preparation");
            return true;
        }

        /// <summary>
        /// Actor-generic preparation. Costs treasury and time but no command
        /// points — CP is the player's attention budget, not a national resource.
        /// </summary>
        public static bool PrepareBy(GameState state, string actorId, EndgameType type)
        {
            var actor = state.FindCountry(actorId);
            if (actor == null) return false;
            if (!CanPrepare(state, actor, type, out _)) return false;
            if (actor.endgames.ProgressFor(type) >= 100f) return false;
            if (actor.resources.treasury < 120f) return false;

            var preparation = actor.endgames.Find(type);
            if (preparation == null)
            {
                preparation = new EndgamePreparation { type = type };
                actor.endgames.preparations.Add(preparation);
                state.AddChronicle(ChronicleCategory.System, actorId,
                    $"Preparation begun: {NameOf(type)}.");
            }

            preparation.progress = Math.Min(100f, preparation.progress + PreparationRate * 3f);
            actor.resources.treasury -= 120f;

            if (preparation.progress >= 100f && actor.isPlayer)
                state.AddNotification(NotificationClass.Priority, "INSTRUMENT READY",
                    $"{NameOf(type)} is prepared. Using it will change how the world regards us.",
                    actorId);
            return true;
        }

        /// <summary>
        /// What an observer can learn about a foreign state's preparations. This
        /// is not public information: it requires collection against them, and
        /// the closer an instrument is to readiness the harder it is to conceal.
        /// Returns -1 when the observer has no basis to know anything.
        /// </summary>
        public static float KnownPreparation(GameState state, string observerId,
            string targetId, EndgameType type)
        {
            var target = state.FindCountry(targetId);
            if (target == null) return -1f;

            float progress = target.endgames.ProgressFor(type);
            if (progress <= 0f) return -1f;

            var network = state.FindNetwork(observerId, targetId);
            float penetration = network?.penetration ?? 0f;

            // A programme close to completion leaks: movement, spending, people.
            // A finished one crosses the threshold on its own signature alone —
            // the world gets exactly one warning, and it is that it is ready.
            float visibility = penetration + progress * 0.45f;
            if (visibility < 45f) return -1f;
            return progress;
        }

        // ---------- execution ----------

        public static bool Execute(GameState state, TurnManager turns, EndgameType type, string targetId)
        {
            if (!CanExecute(state, type, targetId, out string reason))
            {
                GameLog.Warn("ENDGAME", $"Cannot execute {NameOf(type)}: {reason}");
                return false;
            }
            if (!turns.SpendCommandPoints(ExecutionCost, NameOf(type))) return false;
            if (!ExecuteBy(state, state.playerCountryId, type, targetId)) return false;

            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 60, "Strategic instrument employed");
            return true;
        }

        /// <summary>
        /// Actor-generic execution. The same instruments, the same consequences,
        /// whoever reaches for them.
        /// </summary>
        public static bool ExecuteBy(GameState state, string actorId, EndgameType type, string targetId)
        {
            if (!CanExecuteBy(state, actorId, type, targetId, out string reason))
            {
                GameLog.Warn("ENDGAME", $"Cannot execute {NameOf(type)}: {reason}");
                return false;
            }

            var actor = state.FindCountry(actorId);
            var preparation = actor.endgames.Find(type);
            preparation.everUsed = true;
            preparation.progress = 0f; // spent

            string summary;
            switch (type)
            {
                case EndgameType.StrategicDestruction: summary = StrategicDestruction(state, actor, targetId); break;
                case EndgameType.SystemicCollapse: summary = SystemicCollapse(state, actor, targetId); break;
                case EndgameType.StateDestabilization: summary = StateDestabilization(state, actor, targetId); break;
                case EndgameType.StrategicIsolation: summary = StrategicIsolation(state, actor, targetId); break;
                default: summary = TotalMobilization(state, actor); break;
            }

            state.endgameRecords.Add(new EndgameRecord
            {
                date = state.date,
                type = type,
                actorId = actorId,
                targetId = targetId ?? "",
                severity = SeverityOf(type),
                summary = summary
            });

            // Filed under the instrument's own pillar, not System.
            //
            // `System` is the operator's private record, and `WorldWire` excludes
            // it outright — so the single most dramatic event in the game could
            // never reach the news no matter what publicity it carried. The
            // *effects* of a strategic instrument are unmissable by definition;
            // this is the one thing the world is guaranteed to see.
            state.AddChronicle(CategoryFor(type), actorId, summary, Publicity.Public);
            ProvokeResponse(state, actor, targetId, type);
            GameLog.Warn("ENDGAME", summary);
            return true;
        }

        /// <summary>
        /// An existential instrument entitles its target to answer militarily
        /// (GDD §21). A state that has just had its financial system destroyed
        /// does not treat that as an economic disagreement.
        /// </summary>
        static void ProvokeResponse(GameState state, CountryState actor, string targetId, EndgameType type)
        {
            if (!JustifiesMilitaryResponse(type)) return;
            var target = state.FindCountry(targetId);
            if (target == null || target.id == actor.id) return;

            var confrontation = state.ActiveConfrontationFor(target.id);
            if (confrontation == null || !confrontation.Involves(actor.id)) return;

            target.warSupport = Clamp(target.warSupport + 20f);

            if (confrontation.escalation >= EscalationState.TotalWar) return;

            // The target's own strategic logic now permits the highest response.
            ConfrontationSystem.SetEscalationBy(state, confrontation, EscalationState.TotalWar, target.id);

            if (target.isPlayer)
                state.AddNotification(NotificationClass.Flash, "ESCALATION JUSTIFIED",
                    $"{actor.displayName} has employed an existential instrument against us. " +
                    "Nothing now restrains our response.", actor.id);
        }

        public static StrategicSeverity SeverityOf(EndgameType type)
        {
            switch (type)
            {
                case EndgameType.StrategicDestruction: return StrategicSeverity.Existential;
                case EndgameType.SystemicCollapse: return StrategicSeverity.Existential;
                case EndgameType.StateDestabilization: return StrategicSeverity.Severe;
                case EndgameType.StrategicIsolation: return StrategicSeverity.Severe;
                default: return StrategicSeverity.Coercive;
            }
        }

        // ---------- the instruments ----------

        static string StrategicDestruction(GameState state, CountryState player, string targetId)
        {
            var target = state.FindCountry(targetId);

            target.pillars.military = Clamp(target.pillars.military - 40f);
            target.military.ground.SetStrength(target.military.ground.strength - 45f);
            target.military.air.SetStrength(target.military.air.strength - 45f);
            // The one industrial loss that is permanent: a strategic instrument
            // takes the endowment with it, so the plant does not regrow toward
            // what it was (`EconomySystem.BuildIndustry` for the reasoning).
            EconomySystem.EnsureIndustrialEndowment(target);
            target.resources.industrialCapacity = Clamp(target.resources.industrialCapacity - 30f);
            target.resources.industrialEndowment = Clamp(target.resources.industrialEndowment - 30f);
            target.resources.manpower = Math.Max(0f, target.resources.manpower - 300f);
            target.stability = Clamp(target.stability - 30f);
            target.economy.confidence = Clamp(target.economy.confidence - 40f);
            target.warSupport = Clamp(target.warSupport + 25f); // they will not fold, they will harden

            // The world does not forget this, and does not forgive it quickly.
            // A second use is not judged as a second incident but as a pattern.
            float recidivism = Recidivism(state, player.id);
            player.pillars.diplomacy = Clamp(player.pillars.diplomacy - 25f * recidivism);
            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(player.id)) continue;
                string other = relationship.PartnerOf(player.id);
                relationship.trust = Clamp(relationship.trust - 35f * recidivism);
                relationship.relations = Clamp(relationship.relations - 25f * recidivism);
                relationship.SetThreatPerceivedBy(other,
                    Clamp(relationship.ThreatPerceivedBy(other) + 40f * recidivism));
                relationship.AddMemory(state.date, "Employed strategic destruction", -12f * recidivism);
            }
            player.governmentApproval = Clamp(player.governmentApproval - 10f);
            player.nationalUnity = Clamp(player.nationalUnity - 8f);

            state.AddNotification(Reach(player, target), "STRATEGIC DESTRUCTION",
                $"{player.displayName} employed strategic forces against {target.displayName}. " +
                "Every state on earth has revised its assessment of them.", targetId,
                desk: ReportingDesk.Military);

            return $"{player.displayName} employed strategic destruction against {target.displayName}.";
        }

        static string SystemicCollapse(GameState state, CountryState player, string targetId)
        {
            var target = state.FindCountry(targetId);
            float recidivism = Recidivism(state, player.id);

            target.economy.confidence = Clamp(target.economy.confidence - 45f);
            target.economy.growthRate -= 6f;
            target.economy.inflation += 12f;
            target.economy.debtToGdp = Math.Min(250f, target.economy.debtToGdp + 40f);
            target.economy.marketIndex = Math.Max(5f, target.economy.marketIndex * 0.45f);
            target.stability = Clamp(target.stability - 15f);
            foreach (var sector in target.economy.sectors)
                sector.health = Clamp(sector.health - 25f);

            // Contagion: a financial system is not a closed box (GDD §21).
            foreach (var link in state.trade)
            {
                if (!link.Involves(targetId)) continue;
                var partner = state.FindCountry(link.PartnerOf(targetId));
                if (partner == null) continue;

                float exposure = link.volume / 100f;
                partner.economy.confidence = Clamp(partner.economy.confidence - 18f * exposure);
                partner.economy.growthRate -= 1.8f * exposure;
                partner.economy.marketIndex = Math.Max(5f, partner.economy.marketIndex * (1f - 0.15f * exposure));

                if (partner.id != player.id)
                {
                    var relationship = state.FindRelationship(player.id, partner.id);
                    if (relationship != null)
                    {
                        relationship.AddMemory(state.date,
                            "Collapsed a financial system we were exposed to", -4f * recidivism);
                        relationship.relations = Clamp(relationship.relations - 12f * exposure * recidivism);
                    }
                }
            }

            player.pillars.diplomacy = Clamp(player.pillars.diplomacy - 10f * recidivism);
            state.AddNotification(Reach(player, target), "SYSTEMIC FINANCIAL COLLAPSE",
                $"{target.displayName}'s financial system has failed. The contagion is spreading " +
                "through everyone exposed to it, including us.", targetId,
                desk: ReportingDesk.Economy);

            return $"{player.displayName} triggered a systemic financial collapse in {target.displayName}.";
        }

        static string StateDestabilization(GameState state, CountryState player, string targetId)
        {
            var target = state.FindCountry(targetId);
            float recidivism = Recidivism(state, player.id);

            target.stability = Clamp(target.stability - 30f);
            target.nationalUnity = Clamp(target.nationalUnity - 25f);
            target.governmentApproval = Clamp(target.governmentApproval - 20f);
            target.pillars.government = Clamp(target.pillars.government - 15f);
            target.government.militaryLoyalty = Clamp(target.government.militaryLoyalty - 25f);

            // We can fracture a state. We cannot choose what comes next (GDD §22).
            target.government.conspiracyLevel = Clamp(target.government.conspiracyLevel + 35f);
            target.government.conspiracyBackerId = player.id;

            player.pillars.diplomacy = Clamp(player.pillars.diplomacy - 12f * recidivism);
            var toTarget = state.FindRelationship(player.id, targetId);
            if (toTarget != null)
            {
                toTarget.relations = Clamp(toTarget.relations - 30f);
                toTarget.AddMemory(state.date, "Attacked the foundations of our state", -10f * recidivism);
            }

            state.AddNotification(Reach(player, target), "STATE DESTABILIZATION",
                $"Institutional penetration of {target.displayName} has been activated. " +
                "What replaces the current order is not ours to decide.", targetId,
                desk: ReportingDesk.Intelligence);

            return $"{player.displayName} moved to fracture the institutions of {target.displayName}.";
        }

        static string StrategicIsolation(GameState state, CountryState player, string targetId)
        {
            var target = state.FindCountry(targetId);

            // Strip them of partners, access and standing.
            foreach (var treaty in state.treaties)
            {
                if (treaty.broken || !treaty.Involves(targetId)) continue;
                if (treaty.PartnerOf(targetId) == player.id) continue;
                treaty.broken = true;
                treaty.brokenBy = treaty.PartnerOf(targetId);
            }

            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(targetId)) continue;
                string other = relationship.PartnerOf(targetId);
                if (other == player.id) continue;

                relationship.relations = Clamp(relationship.relations - 25f);
                relationship.trust = Clamp(relationship.trust - 20f);
                relationship.strategicAlignment = Clamp(relationship.strategicAlignment - 20f);
            }

            target.pillars.diplomacy = Clamp(target.pillars.diplomacy - 30f);
            target.economy.confidence = Clamp(target.economy.confidence - 15f);

            foreach (var link in state.trade)
            {
                if (!link.Involves(targetId)) continue;
                link.volume = Math.Max(0f, link.volume * 0.6f);
            }

            // Isolation campaigns spend our own standing to run, and a state
            // known for running them finds fewer partners willing to be used.
            player.pillars.diplomacy = Clamp(player.pillars.diplomacy - 6f * Recidivism(state, player.id));
            state.AddNotification(Reach(player, target), "STRATEGIC ISOLATION",
                $"{target.displayName} has been stripped of partners, access and standing.", targetId,
                desk: ReportingDesk.Diplomacy);

            return $"{player.displayName} isolated {target.displayName} from the international system.";
        }

        static string TotalMobilization(GameState state, CountryState player)
        {
            player.endgames.totalMobilization = true;
            player.endgames.mobilizationMonthsRemaining = 18;

            player.military.posture = MilitaryPosture.Forward;
            player.military.ground.readiness = Clamp(player.military.ground.readiness + 20f);
            player.military.air.readiness = Clamp(player.military.air.readiness + 20f);
            player.military.naval.readiness = Clamp(player.military.naval.readiness + 20f);
            player.warSupport = Clamp(player.warSupport + 20f);

            state.AddNotification(
                player.isPlayer ? NotificationClass.Priority : NotificationClass.Wire,
                "TOTAL NATIONAL MOBILIZATION",
                $"{player.displayName} has been placed on a war footing. Everything is available, " +
                "and everything is being spent.", player.id, desk: ReportingDesk.Government);

            return $"{player.displayName} declared total national mobilization.";
        }

        // ---------- monthly ----------

        public static void MonthlyUpdate(GameState state)
        {
            foreach (var country in state.countries)
            {
                var endgames = country.endgames;
                if (!endgames.totalMobilization) continue;

                endgames.mobilizationMonthsRemaining--;

                // Extraordinary effort, extraordinary cost (GDD §21).
                country.resources.treasury -= 90f;
                country.resources.manpower = Math.Max(0f, country.resources.manpower - 12f);
                country.economy.growthRate -= 0.35f;
                country.governmentApproval = Clamp(country.governmentApproval - 1.2f);
                country.nationalUnity = Clamp(country.nationalUnity - 0.8f);
                country.pillars.military = Growth.Apply(country.pillars.military, 0.5f);
                EconomySystem.BuildIndustry(country, 0.3f);

                if (endgames.mobilizationMonthsRemaining > 0) continue;

                endgames.totalMobilization = false;
                country.military.posture = MilitaryPosture.Alert;
                country.warSupport = Clamp(country.warSupport - 15f);
                country.nationalUnity = Clamp(country.nationalUnity - 5f);

                state.AddNotification(
                    country.isPlayer ? NotificationClass.Priority : NotificationClass.Wire,
                    "MOBILIZATION ENDS",
                    $"{country.displayName} stands down from a war footing. The bill is now due.",
                    country.id, desk: ReportingDesk.Government);
                state.AddChronicle(ChronicleCategory.Political, country.id,
                    "Total mobilization ended.", Publicity.Public);
            }
        }

        /// <summary>
        /// Existential non-military action can justify a military response in the
        /// target's own strategic logic (GDD §21). Returns true when the target
        /// would be entitled to escalate over what was done to them.
        /// </summary>
        public static bool JustifiesMilitaryResponse(EndgameType type)
            => SeverityOf(type) == StrategicSeverity.Existential;

        /// <summary>
        /// Multiplier on reputational cost, growing with how many times this
        /// state has already reached for a decisive instrument. A state that has
        /// done this before is not judged on the incident but on the pattern
        /// (GDD §21), so a second use is markedly harder to survive politically.
        /// Capped so a serial offender cannot be punished without limit.
        /// </summary>
        public static float Recidivism(GameState state, string actorId)
        {
            int prior = 0;
            foreach (var record in state.endgameRecords)
            {
                if (record.actorId != actorId) continue;
                if (record.severity < StrategicSeverity.Severe) continue;
                prior++;
            }
            // Called while the instrument resolves, before this use is recorded.
            return Math.Min(2.5f, 1f + prior * 0.5f);
        }

        /// <summary>
        /// How loudly this reaches the player's desk.
        ///
        /// Something done *to* us demands an answer this month and is a FLASH.
        /// Something we did ourselves is a confirmation of an order we already
        /// gave — important, but there is nothing left to decide (GDD §28.2).
        /// Two other states doing it to each other is world news.
        /// </summary>
        static NotificationClass Reach(CountryState actor, CountryState target)
        {
            if (target != null && target.isPlayer) return NotificationClass.Flash;
            return actor.isPlayer ? NotificationClass.Priority : NotificationClass.Wire;
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
