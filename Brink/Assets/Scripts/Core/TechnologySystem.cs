using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Technology and research (GDD Phase-2 system, §11).
    ///
    /// Four rules shape this:
    ///
    /// 1. **Capability-based, not a tech tree.** Research is distributed across
    ///    the five pillars; there is no sixth pillar and no linear ladder.
    /// 2. **Technology unlocks capability, not substance.** A completed programme
    ///    never hands over force structure, trained personnel, infrastructure or
    ///    money — those must still be built and paid for.
    /// 3. **Knowledge spreads.** Through treaties, joint exercises, contact and
    ///    espionage. You cannot keep an advantage forever by holding it close.
    /// 4. **Not all knowledge is equal.** What you developed you understand;
    ///    what you stole you merely possess, until you have used it for years.
    /// </summary>
    public static class TechnologySystem
    {
        public const int StartResearchCost = 2;

        /// <summary>Concurrent programmes a state can fund.</summary>
        public const int MaxPrograms = 2;

        // ---------- queries ----------

        public static bool Has(CountryState country, string capabilityId)
            => country.technology.Has(capabilityId);

        /// <summary>
        /// 0..1 effectiveness of a held capability. Shallow knowledge delivers
        /// less than mastery, so a stolen edge is real but partial.
        /// </summary>
        public static float Effectiveness(CountryState country, string capabilityId)
        {
            var held = country.technology.Find(capabilityId);
            return held == null ? 0f : Math.Max(0f, Math.Min(1f, held.maturity / 100f));
        }

        /// <summary>Whether a state could begin this programme at all.</summary>
        public static bool CanResearch(GameState state, CountryState country,
            string capabilityId, out string reason)
        {
            var definition = CapabilityCatalog.Find(capabilityId);
            if (definition == null) { reason = "No such programme."; return false; }
            if (country.technology.Has(capabilityId)) { reason = "Already held."; return false; }
            if (country.technology.IsResearching(capabilityId)) { reason = "Already under way."; return false; }

            if (country.technology.programs.Count >= MaxPrograms)
            {
                reason = "The research base cannot carry another programme.";
                return false;
            }

            foreach (var prerequisite in definition.prerequisites)
            {
                if (country.technology.Has(prerequisite)) continue;
                reason = $"Requires {CapabilityCatalog.Find(prerequisite)?.name ?? prerequisite}.";
                return false;
            }

            if (country.resources.industrialCapacity < definition.requiredIndustry)
            {
                reason = "The industrial base cannot support that work.";
                return false;
            }
            if (country.pillars.Get(definition.pillar) < definition.requiredPillar)
            {
                reason = $"Our {definition.pillar} institutions are not equal to it yet.";
                return false;
            }
            if (country.resources.treasury < definition.monthlyCost * 4f)
            {
                reason = "We cannot responsibly fund it.";
                return false;
            }

            reason = "";
            return true;
        }

        // ---------- player command ----------

        public static bool BeginResearch(GameState state, TurnManager turns, string capabilityId)
        {
            var player = state.PlayerCountry;
            if (!CanResearch(state, player, capabilityId, out string reason))
            {
                GameLog.Warn("TECH", $"Cannot begin programme: {reason}");
                return false;
            }

            var definition = CapabilityCatalog.Find(capabilityId);
            if (!turns.SpendCommandPoints(StartResearchCost, $"Authorize {definition.name} programme"))
                return false;

            StartProgram(player, definition);
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 16, "Research programme authorized");

            state.AddNotification(NotificationClass.Advisory, "PROGRAMME AUTHORIZED",
                $"{definition.name}: {definition.researchMonths} months at " +
                $"{definition.monthlyCost:F0} per month.", player.id);
            state.AddChronicle(ChronicleCategory.System, player.id,
                $"{definition.name} research programme authorized.");
            return true;
        }

        /// <summary>
        /// Actor-generic programme start: the same gates as the player's, no CP.
        ///
        /// Until the 2026-08 playtest no AI government ever *held* an instrument
        /// capability. `ConsiderAiResearch` below did exist — 6% a month, along
        /// national priority only — but the pre-fix treasury (spec 02) meant
        /// `CanResearch` almost never passed, and priority-only choice never
        /// walked a prerequisite chain toward an instrument. Every strategic
        /// instrument is gated on a researched capability (spec 14), which is why
        /// the world used one against the player in 4 of 556 measured decades.
        /// `AISystem.ConsiderResearch` uses this entry point to build toward the
        /// instrument a government could actually use.
        /// </summary>
        public static bool BeginResearchBy(GameState state, string actorId, string capabilityId)
        {
            var actor = state.FindCountry(actorId);
            if (actor == null) return false;

            // C4 belongs at the AI commitment boundary, not in CanResearch.
            // The shared eligibility rule therefore remains exactly the same for
            // the player while actor-generic AI starts acquire the fiscal guard.
            if (!actor.isPlayer && !AIFiscalDiscipline.CanStartNewDiscretionaryProgramme(state, actor))
                return false;

            if (!CanResearch(state, actor, capabilityId, out _)) return false;

            var definition = CapabilityCatalog.Find(capabilityId);
            StartProgram(actor, definition);
            // Not chronicled: sixteen governments starting programmes is a
            // record nobody reads and it pushed the chronicle past its
            // thirty-year growth cap. A foreign programme becomes news when
            // intelligence finds it, not when it is authorized.
            return true;
        }

        static void StartProgram(CountryState country, CapabilityDef definition)
        {
            country.technology.programs.Add(new ResearchProgram
            {
                capabilityId = definition.id,
                label = definition.name,
                pillar = definition.pillar,
                monthsRemaining = definition.researchMonths,
                monthlyCost = definition.monthlyCost
            });
        }

        // ---------- monthly ----------

        public static void MonthlyUpdate(GameState state)
        {
            int monthIndex = state.date.MonthsSince(state.startDate);

            foreach (var country in state.countries)
            {
                var rng = new Random(unchecked(
                    state.rngSeed * 32452843 + monthIndex * 1543 + Hash.Of(country.id)));

                AdvancePrograms(state, country);
                MatureCapabilities(country);
                SpreadKnowledgeTo(state, country, rng);
                ConsiderAiResearch(state, country, rng);
            }
        }

        static void AdvancePrograms(GameState state, CountryState country)
        {
            var tech = country.technology;
            for (int i = tech.programs.Count - 1; i >= 0; i--)
            {
                var program = tech.programs[i];

                if (country.resources.treasury < program.monthlyCost)
                {
                    tech.programs.RemoveAt(i);
                    if (country.isPlayer)
                        state.AddNotification(NotificationClass.Priority, "PROGRAMME SUSPENDED",
                            $"{program.label} cannot be funded and has been wound up.", country.id,
                            desk: ReportingDesk.Economy);
                    state.AddChronicle(ChronicleCategory.System, country.id,
                        $"{program.label} programme suspended for lack of funds.");
                    continue;
                }

                country.resources.treasury -= program.monthlyCost;
                program.monthsRemaining--;
                if (program.monthsRemaining > 0) continue;

                tech.programs.RemoveAt(i);
                Grant(state, country, program.capabilityId, CapabilitySource.Developed);
            }
        }

        /// <summary>Knowledge deepens with use; only developed work starts deep.</summary>
        static void MatureCapabilities(CountryState country)
        {
            foreach (var held in country.technology.capabilities)
            {
                if (held.maturity >= 100f) continue;
                float rate = held.source == CapabilitySource.Developed ? 1.2f
                           : held.source == CapabilitySource.Shared ? 0.9f
                           : 0.6f;

                // Dual-use (spec 13 §6): a state with a transfer regime absorbs
                // what it did not build faster, because it has the institutions
                // for taking knowledge in. It does **not** speed up our own
                // research — what we developed we already understand, so there is
                // nothing there to absorb.
                if (held.source != CapabilitySource.Developed)
                    rate *= 1f + Effectiveness(country, "CAP_TECHTRANSFER") * 0.6f;

                held.maturity = Math.Min(100f, held.maturity + rate);
            }
        }

        /// <summary>
        /// Diffusion (GDD §11). A state acquires what its partners already hold —
        /// slowly through contact and exercises, faster through a treaty, and
        /// fastest when someone is stealing it outright.
        /// </summary>
        static void SpreadKnowledgeTo(GameState state, CountryState country, Random rng)
        {
            foreach (var definition in CapabilityCatalog.Definitions)
            {
                if (country.technology.Has(definition.id)) continue;

                float best = 0f;
                CapabilitySource route = CapabilitySource.Observed;

                foreach (var other in state.countries)
                {
                    if (other.id == country.id) continue;
                    if (!other.technology.Has(definition.id)) continue;

                    var relationship = state.FindRelationship(country.id, other.id);
                    if (relationship == null) continue;

                    // Treaty partners share deliberately.
                    var treaty = state.FindTreaty(country.id, other.id);
                    if (treaty != null)
                    {
                        float chance = treaty.Has(TreatyCommitment.IntelligenceSharing) ? 0.018f : 0.008f;
                        if (chance > best) { best = chance; route = CapabilitySource.Shared; }
                    }

                    // Exercises and contact leak procedure and practice.
                    float contact = relationship.interoperability * 0.00012f
                                    + Math.Max(0f, relationship.relations - 55f) * 0.00006f;
                    if (contact > best) { best = contact; route = CapabilitySource.Observed; }

                    // And someone may simply be taking it.
                    var network = state.FindNetwork(country.id, other.id);
                    if (network != null && !network.compromised)
                    {
                        float theft = network.penetration * 0.00022f;
                        if (theft > best) { best = theft; route = CapabilitySource.Stolen; }
                    }
                }

                if (best <= 0f || rng.NextDouble() >= best) continue;
                Grant(state, country, definition.id, route);
                return; // at most one acquisition a month
            }
        }

        /// <summary>
        /// Take a capability the target holds and we do not (spec 03 §6).
        ///
        /// Actor-generic, and it arrives `Stolen` — 25 maturity against the 70 a
        /// programme of our own delivers, which is the existing rule that stolen
        /// knowledge is shallower. Returns the capability taken, or null when
        /// there was nothing worth taking.
        ///
        /// Deliberately cannot take what we could not have built: the industrial
        /// and pillar floors still apply, so espionage is a shortcut through the
        /// *years*, never through the prerequisites.
        /// </summary>
        public static string StealCapability(GameState state, string thiefId, string targetId)
        {
            var thief = state.FindCountry(thiefId);
            var target = state.FindCountry(targetId);
            if (thief == null || target == null) return null;

            foreach (var held in target.technology.capabilities)
            {
                if (thief.technology.Has(held.capabilityId)) continue;

                var definition = CapabilityCatalog.Find(held.capabilityId);
                if (definition == null) continue;
                if (thief.resources.industrialCapacity < definition.requiredIndustry) continue;
                if (thief.pillars.Get(definition.pillar) < definition.requiredPillar) continue;

                Grant(state, thief, held.capabilityId, CapabilitySource.Stolen);
                return definition.name;
            }
            return null;
        }

        static void Grant(GameState state, CountryState country, string capabilityId, CapabilitySource source)
        {
            var definition = CapabilityCatalog.Find(capabilityId);
            if (definition == null || country.technology.Has(capabilityId)) return;

            country.technology.capabilities.Add(new HeldCapability
            {
                capabilityId = capabilityId,
                source = source,
                acquired = state.date,
                // What we built, we understand. What we took, we merely have.
                maturity = source == CapabilitySource.Developed ? 70f
                         : source == CapabilitySource.Shared ? 45f
                         : source == CapabilitySource.Stolen ? 25f : 30f
            });

            if (country.isPlayer)
            {
                string how = source == CapabilitySource.Developed
                    ? "Our programme has delivered."
                    : source == CapabilitySource.Shared ? "Transferred by a partner."
                    : source == CapabilitySource.Stolen ? "Acquired through collection."
                    : "Learned through contact.";
                state.AddNotification(NotificationClass.Advisory,
                    $"CAPABILITY: {definition.name.ToUpperInvariant()}", $"{how} {definition.description}",
                    country.id, desk: ReportingDesk.Economy);
                ProgressionSystem.AwardXP(state, source == CapabilitySource.Developed ? 35 : 12,
                    "Capability acquired");
            }
            else if (source == CapabilitySource.Stolen || source == CapabilitySource.Developed)
            {
                // "Assessed" has to mean assessed. Foreign governments now
                // research in earnest, and announcing every completion on the
                // wire told the operator sixteen states' true capability the
                // month it existed — 83 items a decade, and a breach of the rule
                // that no view prints a foreign truth without collection. A
                // network with real penetration on that state earns the item;
                // otherwise it surfaces where it always could: the dossier.
                var network = state.FindNetwork(state.playerCountryId, country.id);
                if (network != null && network.penetration >= 35f)
                    state.AddNotification(NotificationClass.Wire, "FOREIGN CAPABILITY",
                        $"{country.displayName} is assessed to have fielded {definition.name}.", country.id,
                        desk: ReportingDesk.Intelligence);
            }

            // Only our own acquisitions are chronicled. Sixteen governments now
            // research in earnest, and a line per foreign capability pushed the
            // chronicle past its thirty-year growth cap; what we *know* of a
            // foreign programme is the dossier's business.
            if (country.isPlayer)
                state.AddChronicle(ChronicleCategory.System, country.id,
                    $"{definition.name} capability acquired ({source}).");
            GameLog.Info("TECH", $"{country.id} acquired {capabilityId} ({source}).");
        }

        /// <summary>AI states fund research along their national priority.</summary>
        static void ConsiderAiResearch(GameState state, CountryState country, Random rng)
        {
            if (country.isPlayer) return;
            if (country.technology.programs.Count >= MaxPrograms) return;
            if (!AIFiscalDiscipline.CanStartNewDiscretionaryProgramme(state, country)) return;
            if (rng.NextDouble() >= 0.06) return;

            var priority = country.government.leader.priority;
            foreach (var definition in CapabilityCatalog.Definitions)
            {
                bool favored;
                switch (priority)
                {
                    case NationalPriority.Security:
                        favored = definition.pillar == Pillar.Military || definition.pillar == Pillar.Intelligence;
                        break;
                    case NationalPriority.Prosperity: favored = definition.pillar == Pillar.Economy; break;
                    case NationalPriority.Influence: favored = definition.pillar == Pillar.Diplomacy; break;
                    default: favored = definition.pillar == Pillar.Government; break;
                }
                if (!favored) continue;
                if (!CanResearch(state, country, definition.id, out _)) continue;

                StartProgram(country, definition);
                return;
            }
        }
    }
}
