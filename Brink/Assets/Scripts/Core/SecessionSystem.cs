using System;
using System.Collections.Generic;
using Brink.Data;
using Random = System.Random;

namespace Brink.Core
{
    /// <summary>
    /// States can come apart, and what comes apart can be put back together
    /// (GDD §16, §17.1).
    ///
    /// **Why this matters more than it sounds.** `new CountryState` appeared
    /// exactly once in the entire codebase — at world creation. Conquest and
    /// accession both move title between the same sixteen states, so the map
    /// could only ever *shrink*: sixteen countries at the start, sixteen or fewer
    /// forever after. §17.1 asks for "rare civil wars, fragmentation and
    /// unification that may permanently alter the map", and half of that
    /// sentence had no implementation at all.
    ///
    /// The design rules this follows, all of which exist to stop fragmentation
    /// becoming a random punishment:
    ///
    /// 1. **It is earned, not rolled.** A state fractures out of a civil conflict
    ///    it was already losing, with unity already gone and territory already
    ///    lost. There is no "3% chance per month" anywhere in here.
    /// 2. **The successor is real.** It gets ground, a government, a cabinet, an
    ///    AI mind and a place in every relationship — not a flag on a map. A
    ///    breakaway nobody can negotiate with is scenery.
    /// 3. **It is rare.** `MinimumUnrecoverableMonths` and the conditions below
    ///    mean most civil conflicts still end with the state intact. A world that
    ///    shatters every decade is not a world with stakes.
    /// 4. **The save continues.** If the *player's* country fractures, the
    ///    operator keeps their post in the rump state. The same rule the coup
    ///    system follows: consequences are severe and never terminal.
    /// </summary>
    public static class SecessionSystem
    {
        /// <summary>How long a civil conflict must have been going badly to fracture.</summary>
        public const int MinimumUnrecoverableMonths = 8;

        /// <summary>Unity below which the state is no longer holding itself together.</summary>
        public const float FractureUnity = 22f;

        /// <summary>Loyalty below which the army will not put it back together.</summary>
        public const float FractureLoyalty = 30f;

        /// <summary>Relations a breakaway needs with its former state to rejoin.</summary>
        public const float ReunificationRelations = 72f;

        /// <summary>Months a breakaway must exist before reunification is possible.</summary>
        public const int ReunificationSettlingMonths = 36;

        // ---------- fracture ----------

        /// <summary>
        /// Whether this state is coming apart. Deliberately demanding: every
        /// condition describes a government that has already failed at something,
        /// so fragmentation reads as the end of a story rather than a dice roll.
        /// </summary>
        /// <summary>The least industrial capacity a breakaway is born with.</summary>
        public const float MinimumBirthIndustry = 10f;

        public static bool IsFracturing(GameState state, CountryState country)
        {
            var gov = country.government;
            if (!gov.inCivilConflict) return false;
            if (gov.civilConflictMonthsElapsed < MinimumUnrecoverableMonths) return false;
            if (country.nationalUnity > FractureUnity) return false;
            if (gov.militaryLoyalty > FractureLoyalty) return false;

            // It needs somewhere to secede *to*. A state holding one location is
            // not a state that can be halved.
            return SecedableLocations(state, country.id).Count > 0;
        }

        /// <summary>
        /// The ground a breakaway would take: everything this country originally
        /// held except its capital.
        ///
        /// The capital stays with the rump deliberately — a government that loses
        /// its seat is a coup or a conquest, both of which are already modelled,
        /// and conflating them would make three systems produce the same event.
        /// </summary>
        public static List<StrategicLocation> SecedableLocations(GameState state, string countryId)
        {
            var eligible = new List<StrategicLocation>();
            foreach (var location in state.locations)
            {
                if (location.originalOwnerId != countryId) continue;
                if (location.ownerId != countryId) continue;   // already lost; not ours to split
                if (location.type == LocationType.Capital) continue;
                eligible.Add(location);
            }
            return eligible;
        }

        /// <summary>
        /// Split a state. Returns the new country, or null when it could not
        /// happen — a caller must never assume it did.
        /// </summary>
        public static CountryState Fracture(GameState state, CountryState country, Random rng)
        {
            if (!IsFracturing(state, country)) return null;

            var candidates = SecedableLocations(state, country.id);
            if (candidates.Count == 0) return null;

            // The largest holding secedes, plus roughly half the rest. A
            // breakaway that takes one minor region is not worth modelling as a
            // state; one that takes everything leaves no rump to play.
            candidates.Sort((a, b) => b.strategicValue.CompareTo(a.strategicValue));
            int take = Math.Max(1, candidates.Count / 2);

            var successor = MakeSuccessor(state, country, rng);
            if (successor == null) return null;

            for (int i = 0; i < take; i++)
            {
                var location = candidates[i];
                location.ownerId = successor.id;

                // The successor's ground is *its own* ground, not occupied
                // territory. Leaving `originalOwnerId` alone would have the new
                // state permanently occupying itself, paying garrison drag
                // forever and reading as a conqueror of its own capital.
                location.originalOwnerId = successor.id;
                location.pacification = 0f;
            }

            // What the rump has left of itself.
            country.nationalUnity = Clamp(country.nationalUnity + 12f);   // the dissenters left
            country.stability = Clamp(country.stability - 10f);
            country.publicGrievance = Clamp(country.publicGrievance + 18f);
            country.pillars.economy = Clamp(country.pillars.economy - 6f);
            country.pillars.military = Clamp(country.pillars.military - 8f);
            country.government.inCivilConflict = false;
            country.government.civilConflictMonthsRemaining = 0;
            country.government.civilConflictMonthsElapsed = 0;
            country.government.conspiracyLevel = Clamp(country.government.conspiracyLevel - 25f);

            state.AddNotification(
                country.isPlayer ? NotificationClass.Flash : NotificationClass.Priority,
                "STATE FRACTURES",
                country.isPlayer
                    ? $"{successor.displayName} has declared independence and holds {take} of our " +
                      "regions. The government continues from the capital. So does this office."
                    : $"{country.displayName} has split. {successor.displayName} now holds " +
                      $"{take} of its regions.",
                country.id, desk: ReportingDesk.Government);

            state.AddChronicle(ChronicleCategory.Political, country.id,
                $"{successor.displayName} secedes from {country.displayName}.", Publicity.Public,
                HistoricalEvent.TurningPoint, successor.id);

            return successor;
        }

        /// <summary>
        /// Build the breakaway as a real state: government, cabinet, AI mind,
        /// relationships with everyone.
        ///
        /// Everything is derived from the parent rather than rolled fresh — a
        /// successor is made of the same people and the same industry, which is
        /// what makes it feel like a piece of somewhere rather than a new nation
        /// that appeared from nowhere.
        /// </summary>
        static CountryState MakeSuccessor(GameState state, CountryState parent, Random rng)
        {
            string id = $"{parent.id}_S";
            if (state.FindCountry(id) != null) return null;   // already fractured once

            var successor = new CountryState
            {
                id = id,
                displayName = BreakawayName(parent, rng),
                isPlayer = false,

                // Roughly a third of the parent's capability: a breakaway is not
                // a peer, and pretending otherwise would make fragmentation a
                // way of duplicating a great power.
                pillars = new PillarScores
                {
                    military = Clamp(parent.pillars.military * 0.32f),
                    economy = Clamp(parent.pillars.economy * 0.30f),
                    intelligence = Clamp(parent.pillars.intelligence * 0.25f),
                    diplomacy = Clamp(parent.pillars.diplomacy * 0.22f),
                    government = Clamp(parent.pillars.government * 0.35f)
                },

                stability = 38f,
                nationalUnity = 66f,        // secession is a unifying act, briefly
                governmentApproval = 60f,
                livingStandards = Clamp(parent.livingStandards * 0.8f),
                socialUnrest = 20f,
                publicGrievance = Clamp(parent.publicGrievance * 0.6f),
                warSupport = 55f,
                foundedDate = state.date
            };

            // A share of what is in the account — never a share of an overdraft.
            // The debt stays with the rump (a successor is born owing nothing),
            // and a state that inherits arrears without the debt behind them is
            // born in a fiscal crisis it never ran up. Measured: a breakaway of
            // a ruined parent opened at −1,576.
            successor.resources.treasury = Math.Max(0f, parent.resources.treasury) * 0.20f;
            successor.resources.manpower = parent.resources.manpower * 0.28f;
            // The same land and the same people, so the same *position* on
            // every resource, not only the same endowments. The first version
            // copied the endowments and left the levels at their zero default:
            // a breakaway opened at energy 0 against an endowment of 82, which
            // is the largest single term in the growth formula (−2.0 a year),
            // and at a food endowment of 0 with food security 56, so its own
            // ceiling was below what it had. The field-initialised-under-its-
            // floor family, four fields at once.
            successor.resources.energyEndowment = parent.resources.energyEndowment;
            successor.resources.materialsEndowment = parent.resources.materialsEndowment;
            successor.resources.foodEndowment = parent.resources.foodEndowment;
            successor.resources.energy = parent.resources.energy;
            successor.resources.strategicMaterials = parent.resources.strategicMaterials;
            successor.resources.foodSecurity = parent.resources.foodSecurity;
            EconomySystem.EnsureIndustrialEndowment(parent);
            successor.resources.industrialEndowment = parent.resources.industrialEndowment;

            // **Born at the floor, not under it.** `StagnationFloor` bounds how
            // far a downturn can take national economic capability, and it only
            // ever stops a drag — it never gives. A breakaway of a ruined parent
            // was born at 30% of an already-ruined pillar (6) and 30% of a gutted
            // industrial capacity (6): below the floor, with the distress and
            // stagnation drags still pulling, and nothing that could lift either
            // — so growth sat at −1.6 for twenty isolated years and living
            // standards settled at a target of 32. A new state starts with the
            // capability its ground can hold at the least.
            successor.resources.industrialCapacity =
                Clamp(Math.Max(parent.resources.industrialCapacity * 0.3f, MinimumBirthIndustry));
            successor.pillars.economy =
                Clamp(Math.Max(successor.pillars.economy, EconomySystem.StagnationFloor(successor)));

            successor.economy.gdp = parent.economy.gdp * 0.25f;
            successor.economy.confidence = 45f;

            // **An economy with sectors in it.** `WorldFactory` builds the seven
            // sectors for every authored state; the successor had none, so
            // sabotage, subsidies, import displacement and the endgame's sector
            // damage all fell on an empty list and `SectorStrength` read a flat
            // neutral 55 forever. Anchored to the same level the monthly tick
            // reverts toward, with the parent's health — the same industries,
            // in the same condition. No random draw: the successor's RNG stream
            // is what the names below are seeded from.
            foreach (EconomicSector sector in Enum.GetValues(typeof(EconomicSector)))
            {
                float parentHealth = 80f;
                foreach (var parentSector in parent.economy.sectors)
                    if (parentSector.sector == sector) parentHealth = parentSector.health;
                successor.economy.sectors.Add(new SectorState
                {
                    sector = sector,
                    output = Clamp(EconomySystem.SectorAnchor(successor, sector)),
                    health = Clamp(parentHealth)
                });
            }

            // A breakaway is born fractious and suspicious of everyone. It is the
            // one place a trait is assigned rather than authored, because the
            // state did not exist to be authored.
            successor.traits.Add(NationalTraitCatalog.Find(NationalTraitCatalog.Fractious));
            successor.traits.Add(NationalTraitCatalog.Find(NationalTraitCatalog.Besieged));

            successor.military.ground.SetStrength(Clamp(parent.military.ground.strength * 0.30f));
            successor.military.ground.readiness = 55f;
            successor.military.ground.supply = 50f;
            successor.military.air.SetStrength(Clamp(parent.military.air.strength * 0.18f));
            successor.military.naval.SetStrength(Clamp(parent.military.naval.strength * 0.15f));

            successor.government.type = parent.government.type;
            successor.government.militaryLoyalty = 60f;
            successor.government.leader = new Leader
            {
                name = parent.government.leader.name,   // replaced immediately below
                faction = "PROVISIONAL AUTHORITY",
                priority = NationalPriority.Cohesion,
                competence = 45f + (float)rng.NextDouble() * 30f,
                age = 46f + (float)rng.NextDouble() * 20f
            };

            var profile = WorldFactory.FindProfile(parent.id);
            if (profile != null)
                successor.government.leader.name =
                    $"{profile.firstNames[rng.Next(profile.firstNames.Length)]} " +
                    $"{profile.lastNames[rng.Next(profile.lastNames.Length)]}";

            state.countries.Add(successor);

            // A cabinet, so the successor is governed by people rather than by a
            // number — and so a coup or an election there means something.
            // Named from the parent's pool: a breakaway has no authored profile,
            // and the people in it are the parent's people.
            WorldFactory.AppointCabinet(rng, successor, parent.id);

            // Relationships with everyone, including its parent. Without these
            // the new state cannot be talked to, sanctioned, allied with or
            // collected against, which would make it scenery.
            foreach (var other in state.countries)
            {
                if (other.id == successor.id) continue;
                if (state.FindRelationship(successor.id, other.id) != null) continue;

                bool isParent = other.id == parent.id;
                state.relationships.Add(new Relationship
                {
                    countryA = successor.id,
                    countryB = other.id,
                    relations = isParent ? 12f : 45f,
                    trust = isParent ? 8f : 40f,
                    threatPerceptionOfA = isParent ? 55f : 25f,
                    threatPerceptionOfB = isParent ? 70f : 25f
                });
            }

            // An AI mind, inheriting the parent's temperament — the same people,
            // more suspicious.
            var parentAi = state.FindAI(parent.id);
            state.aiStates.Add(new AIState
            {
                countryId = successor.id,
                path = StrategicPath.Survival,
                profile = new AIProfile
                {
                    aggression = Clamp((parentAi?.profile.aggression ?? 50f) * 0.8f),
                    caution = Clamp((parentAi?.profile.caution ?? 50f) + 15f),
                    opportunism = parentAi?.profile.opportunism ?? 50f,
                    patience = Clamp((parentAi?.profile.patience ?? 50f) - 10f)
                }
            });

            return successor;
        }

        static string BreakawayName(CountryState parent, Random rng)
        {
            string[] forms =
            {
                "Free {0}", "{0} Federation", "Northern {0}", "Southern {0}",
                "{0} Provisional Republic", "United Provinces of {0}"
            };
            return string.Format(forms[rng.Next(forms.Length)], parent.displayName);
        }

        // ---------- unification ----------

        /// <summary>
        /// A breakaway rejoining the state it left (GDD §17.1).
        ///
        /// Deliberately routed through the *same* absorption used by accession
        /// rather than a bespoke path: reunification is a state agreeing to be
        /// absorbed, and it should cost and read exactly like one. What differs
        /// is only the conditions — an old quarrel takes longer to settle, and
        /// the relationship has to have genuinely healed.
        /// </summary>
        public static bool CanReunify(GameState state, CountryState parent, CountryState breakaway)
        {
            if (parent == null || breakaway == null) return false;
            if (breakaway.id != $"{parent.id}_S") return false;
            if (breakaway.government.inCivilConflict) return false;
            if (state.IsAtWar(breakaway.id)) return false;

            var relationship = state.FindRelationship(parent.id, breakaway.id);
            if (relationship == null) return false;
            if (DiplomacySystem.Permitted(state, relationship, relationship.relations) < ReunificationRelations) return false;

            return state.date.MonthsSince(breakaway.foundedDate) >= ReunificationSettlingMonths;
        }

        /// <summary>The breakaway that came out of this state, if any.</summary>
        public static CountryState BreakawayOf(GameState state, string parentId)
            => state.FindCountry($"{parentId}_S");

        /// <summary>
        /// Put a fractured state back together. Routed through the same
        /// absorption accession uses, so reunification costs and reads like the
        /// negotiated thing it is rather than a second annexation path.
        /// </summary>
        public static bool Reunify(GameState state, CountryState parent, CountryState breakaway)
        {
            if (!CanReunify(state, parent, breakaway)) return false;

            ConquestSystem.Absorb(state, parent, breakaway);

            parent.nationalUnity = Clamp(parent.nationalUnity + 10f);
            parent.publicGrievance = Math.Max(0f, parent.publicGrievance - 12f);
            parent.pillars.diplomacy = Growth.Apply(parent.pillars.diplomacy, 4f);

            state.AddNotification(
                parent.isPlayer ? NotificationClass.Priority : NotificationClass.Wire,
                "REUNIFICATION",
                $"{breakaway.displayName} has rejoined {parent.displayName}. The quarrel that " +
                "split them is over; the memory of it is not.",
                parent.id, desk: ReportingDesk.Government);
            state.AddChronicle(ChronicleCategory.Political, parent.id,
                $"{breakaway.displayName} rejoins {parent.displayName}.", Publicity.Public,
                HistoricalEvent.TurningPoint, breakaway.id);
            return true;
        }

        /// <summary>
        /// Monthly check for a breakaway drifting back. Rare by construction: it
        /// needs three years, warm relations and peace on both sides, so most
        /// fractures are permanent and reunification is a story rather than a
        /// timer running out.
        /// </summary>
        public static void MonthlyUpdate(GameState state)
        {
            for (int i = state.countries.Count - 1; i >= 0; i--)
            {
                var breakaway = state.countries[i];
                int split = breakaway.id.IndexOf("_S", StringComparison.Ordinal);
                if (split <= 0 || split != breakaway.id.Length - 2) continue;

                var parent = state.FindCountry(breakaway.id.Substring(0, split));
                if (parent == null) continue;
                if (!CanReunify(state, parent, breakaway)) continue;

                Reunify(state, parent, breakaway);
            }
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
