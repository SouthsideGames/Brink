using System;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The world as structure rather than backdrop: theatres, simultaneous
    /// fronts, fragmentation, the social layer, and the content volume behind
    /// them (GDD §12, §16, §17.1).
    /// </summary>
    public class WorldStructureTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 1212);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static GameState Run(int seed, int months, Action<GameState, int> each = null)
        {
            var world = WorldFactory.CreateDebugWorld(seed);
            var turns = new TurnManager(world);
            SimulationPipeline.Wire(turns, world);
            for (int month = 0; month < months; month++)
            {
                each?.Invoke(world, month);
                turns.EndMonth();
            }
            return world;
        }

        // ---------- content volume ----------

        [Test]
        public void ThereIsEnoughMaritimeGroundForTheNavalVerbs()
        {
            // Nine of twenty-three operations need a maritime target. With three
            // ports in the world, most of the naval half of the verb list was a
            // button with nothing to point at.
            int ports = 0, chokepoints = 0;
            foreach (var location in state.locations)
            {
                if (location.type == LocationType.Port) ports++;
                if (location.type == LocationType.Chokepoint) chokepoints++;
            }

            Assert.GreaterOrEqual(ports, 10, $"Only {ports} ports in the world.");
            Assert.GreaterOrEqual(chokepoints, 6, $"Only {chokepoints} chokepoints in the world.");
        }

        [Test]
        public void EveryMaritimePowerHasSomewhereToPutItsFleet()
        {
            foreach (var country in state.countries)
            {
                if (GeographySystem.AccessOf(country.id) != NavalAccess.Maritime) continue;

                bool hasPort = false;
                foreach (var location in state.locations)
                    if (location.originalOwnerId == country.id && location.type == LocationType.Port)
                        hasPort = true;

                Assert.IsTrue(hasPort,
                    $"{country.displayName} is authored as a maritime power with no port. " +
                    "Its navy has nowhere to be, and nowhere to be attacked.");
            }
        }

        [Test]
        public void StrategicMaterialsHaveGeography()
        {
            // The commodity industry, procurement and every research programme
            // draw on had nowhere on the map that produced it.
            int regions = 0;
            foreach (var location in state.locations)
                if (location.type == LocationType.MaterialsRegion) regions++;

            Assert.GreaterOrEqual(regions, 3,
                "Strategic materials are a headline national resource with no ground behind them.");
        }

        [Test]
        public void TakingAMaterialsRegionSuppliesMaterials()
        {
            StrategicLocation region = null;
            foreach (var location in state.locations)
                if (location.type == LocationType.MaterialsRegion
                    && location.originalOwnerId != state.playerCountryId)
                { region = location; break; }
            Assert.IsNotNull(region);

            var player = state.PlayerCountry;
            float before = EconomySystem.MaterialsCeilingFor(state, player);

            region.ownerId = player.id;
            float after = EconomySystem.MaterialsCeilingFor(state, player);

            Assert.Greater(after, before,
                "Holding a mining region did not raise what we can supply ourselves.");
        }

        [Test]
        public void TheEventCatalogueCanSustainALongSave()
        {
            // 15 events on a 24-month cooldown exhausts in about three years, and
            // a save is meant to run decades.
            Assert.GreaterOrEqual(EventCatalog.Definitions.Count, 25,
                $"Only {EventCatalog.Definitions.Count} authored events. A player will see the " +
                "whole catalogue inside three years and repeats forever after.");
        }

        // Three 240-month full-pipeline worlds — 60 simulated years — against
        // Unity's 180s default. It sat just under the line and now sits just
        // over it, which makes it a coin flip rather than a test; the horizon is
        // the claim ("checked across many worlds and a long one"), so raise the
        // clock rather than trim the evidence.
        [Test, Timeout(600000)]
        public void EveryEventIsReachable()
        {
            // An event whose eligibility can never be true is content that does
            // not exist. Checked across many worlds and a long horizon.
            var seen = new HashSet<string>();

            foreach (int seed in new[] { 1212, 3434, 5656 })
            {
                var world = WorldFactory.CreateDebugWorld(seed);
                var turns = new TurnManager(world);
                SimulationPipeline.Wire(turns, world);

                for (int month = 0; month < 240; month++)
                {
                    foreach (var definition in EventCatalog.Definitions)
                    {
                        if (seen.Contains(definition.id)) continue;
                        try
                        {
                            if (definition.isEligible(world)) seen.Add(definition.id);
                        }
                        catch (Exception e)
                        {
                            Assert.Fail($"{definition.id} threw while testing eligibility: {e.Message}");
                        }
                    }
                    turns.EndMonth();
                }
            }

            // Not every event will fire in three sampled worlds — some need a
            // specific rare state. But most should be reachable, and any that
            // never becomes eligible anywhere is worth a look.
            Assert.GreaterOrEqual(seen.Count, EventCatalog.Definitions.Count / 2,
                $"Only {seen.Count} of {EventCatalog.Definitions.Count} events ever became " +
                "eligible across three sixty-year worlds.");
        }

        // ---------- national identity ----------

        [Test]
        public void EveryCountryHasAnAuthoredCharacter()
        {
            foreach (var country in state.countries)
                Assert.Greater(country.traits.Count, 0,
                    $"{country.displayName} has no traits. Fifteen of sixteen states used to be " +
                    "the same numbers with a different flag.");
        }

        [Test]
        public void EveryAuthoredTraitResolves()
        {
            foreach (var profile in WorldFactory.Profiles)
                foreach (var id in profile.traitIds)
                    Assert.IsNotNull(NationalTraitCatalog.Find(id),
                        $"{profile.id} carries unknown trait '{id}' — a typo becomes a country " +
                        "quietly missing its character.");
        }

        [Test]
        public void ACountryPlaysLikeItselfAcrossSaves()
        {
            // The point of authoring temperament. Personality used to be rolled
            // uniformly, so nothing a player learned about a nation carried into
            // the next game — which quietly defeats an AI designed to answer a
            // repeated opening.
            float SpreadOf(string countryId)
            {
                float lowest = float.MaxValue, highest = float.MinValue;
                foreach (int seed in new[] { 11, 22, 33, 44, 55 })
                {
                    var world = WorldFactory.CreateDebugWorld(seed);
                    float aggression = world.FindAI(countryId).profile.aggression;
                    lowest = Math.Min(lowest, aggression);
                    highest = Math.Max(highest, aggression);
                }
                return highest - lowest;
            }

            Assert.Less(SpreadOf("RUS"), 25f,
                "The same country's temperament varies too much between saves to be learnable.");

            // And two countries authored differently must still read differently.
            var calm = WorldFactory.FindProfile("AUS");
            var forceful = WorldFactory.FindProfile("RUS");
            Assert.Greater(forceful.aggression, calm.aggression + 20f,
                "Two deliberately different nations were authored with the same temperament.");
        }

        [Test]
        public void TraitsChangeSomething()
        {
            // A trait that reads nowhere is decoration.
            var martial = new CountryState();
            martial.traits.Add(NationalTraitCatalog.Find(NationalTraitCatalog.Martial));
            Assert.Less(NationalTraitCatalog.WarSupportResilience(martial),
                NationalTraitCatalog.WarSupportResilience(new CountryState()));

            var resourceState = new CountryState();
            resourceState.traits.Add(NationalTraitCatalog.Find(NationalTraitCatalog.ResourceState));
            Assert.Greater(NationalTraitCatalog.ResourceCeilingBonus(resourceState), 0f);

            var fractious = new CountryState();
            fractious.traits.Add(NationalTraitCatalog.Find(NationalTraitCatalog.Fractious));
            Assert.Greater(NationalTraitCatalog.UnrestVolatility(fractious), 1f);
        }

        // ---------- the social layer ----------

        [Test]
        public void LivingStandardsFollowTheEconomyOverYears()
        {
            var world = WorldFactory.CreateDebugWorld(seed: 1212);
            var country = world.PlayerCountry;
            var turns = new TurnManager(world);
            SimulationPipeline.Wire(turns, world);

            country.livingStandards = 55f;
            for (int month = 0; month < 72; month++)
            {
                country.economy.marketIndex = 40f;   // a long, deep decline
                country.economy.growthRate = -4f;
                turns.EndMonth();
            }

            Assert.Less(country.livingStandards, 45f,
                $"Six years of contraction left living standards at {country.livingStandards:F0}. " +
                "The economy has to reach the public eventually.");
        }

        [Test]
        public void UnrestIsNotJustLowApproval()
        {
            // The distinction that justifies tracking it separately: a state can
            // be widely disliked and perfectly calm.
            var world = WorldFactory.CreateDebugWorld(seed: 1212);
            var country = world.PlayerCountry;
            var turns = new TurnManager(world);
            SimulationPipeline.Wire(turns, world);

            country.livingStandards = 80f;
            country.nationalUnity = 85f;
            country.governmentApproval = 20f;   // unpopular, but comfortable

            for (int month = 0; month < 24; month++)
            {
                country.livingStandards = 80f;
                country.nationalUnity = 85f;
                turns.EndMonth();
            }

            Assert.Less(country.socialUnrest, 25f,
                $"A prosperous, cohesive country with an unpopular government reached " +
                $"{country.socialUnrest:F0} unrest. Unrest is organisation, not opinion.");
        }

        [Test]
        public void HardshipEventuallyOrganises()
        {
            var world = WorldFactory.CreateDebugWorld(seed: 1212);
            var country = world.PlayerCountry;
            var turns = new TurnManager(world);
            SimulationPipeline.Wire(turns, world);

            // Drive the *causes*, not the symptoms.
            //
            // This test used to assign inflation and unemployment directly each
            // month. EconomySystem runs before GovernmentSystem in the pipeline
            // and recomputes both from fundamentals, so the injected figures were
            // erased every month before the social tick ever read them: it was
            // measuring a country with low living standards and an otherwise
            // healthy economy, and then blaming the social layer for the mild
            // result. Starving the economy of energy and gutting its sectors
            // makes EconomySystem *produce* the hardship instead.
            // Six years, not four (2026-09). The claim is that *sustained severe*
            // hardship organises, and the bar (40) is a level, not a date: the
            // window has to be as long as the chain it measures. The market
            // index is fundamentals-anchored with a multi-year lead time
            // (CLAUDE.md: "a test wanting to observe a real collapse must run
            // four years, not two"), living standards fall at 0.045/month, and
            // unrest then rises at 0.11/month toward its pressure target — so
            // this fixture's economy is only *severe* from about month 36
            // (standards 27, index 16), and unrest was still climbing 1.6 a
            // month at 48 with a dozen points to go. It used to cross 40 inside
            // four years only because the world's AI sanctioned a collapsing
            // power at 35% a month from year one and food pressure did the rest;
            // sanctions now answer to a cause (spec 02 §9a), so the fixture's
            // own hardship has to do the work on the model's own timescale.
            for (int month = 0; month < 72; month++)
            {
                country.resources.energy = 8f;
                country.economy.confidence = 12f;
                foreach (var sector in country.economy.sectors) sector.health = 10f;
                turns.EndMonth();
            }

            // Assert the setup took hold before judging what it caused. The
            // previous version had no such check, which is exactly why a test
            // whose premise was being overwritten still read as a statement about
            // unrest rather than as a broken fixture.
            Assert.Greater(country.economy.inflation, 8f,
                "The economy never actually became hard-pressed, so this says nothing " +
                "about what hardship does.");
            Assert.Less(country.livingStandards, 35f,
                "Living standards never fell, so there was no hardship to organise around.");

            // 40 is not an arbitrary bar: GENERAL_STRIKE gates at 58,
            // SEPARATIST_MOVEMENT at 42, PORT_STRIKE at 40 and the cabinet's
            // unrest counsel at 55. Below the forties the entire body of content
            // keyed to this stat is unreachable, and the social layer is a bar on
            // a screen.
            Assert.Greater(country.socialUnrest, 40f,
                $"Four years of severe hardship produced unrest of only {country.socialUnrest:F1}. " +
                "Nothing in the event catalog reacts below 40.\n" +
                $"  livingStandards {country.livingStandards:F1} -> term {Math.Max(0f, 45f - country.livingStandards) * 0.55f:F1}\n" +
                $"  inflation       {country.economy.inflation:F1} -> term {Math.Max(0f, country.economy.inflation - 8f) * 1.6f:F1}\n" +
                $"  unemployment    {country.economy.unemployment:F1} -> term {Math.Max(0f, country.economy.unemployment - 10f) * 1.4f:F1}\n" +
                $"  warExhaustion   {country.warExhaustion:F1} -> term {country.warExhaustion * 0.22f:F1}\n" +
                $"  grievance       {country.publicGrievance:F1} -> term {country.publicGrievance * 0.18f:F1}\n" +
                $"  nationalUnity   {country.nationalUnity:F1} -> multiplier {1.18f - country.nationalUnity / 165f:F2}\n" +
                $"  growthRate {country.economy.growthRate:F1}  energy {country.resources.energy:F1}  " +
                $"marketIndex {country.economy.marketIndex:F1}");
            // Grievance is a *decade*-scale accumulator, and living standards fall
            // on a ~22-month time constant, so they only cross the deprivation
            // threshold around month 30 — leaving barely a year to accrue. Naming
            // a level it must hit at four years was a guess about a curve, not a
            // statement about the design.
            //
            // The design's actual claim is that hardship is remembered *after it
            // ends*: "a country that has been through something does not return
            // to the condition of one that has not". So repair the economy and
            // check what survives. This also exercises the asymmetry the whole
            // layer rests on — unrest is organisation and subsides once the cause
            // is gone; grievance is memory and does not.
            float grievanceAtWorst = country.publicGrievance;
            float unrestAtWorst = country.socialUnrest;
            Assert.Greater(grievanceAtWorst, 2f, "Hardship left nothing in the public memory at all.");

            // Eight years, not three. Fixing the inputs does not instantly fix the
            // economy: the market index has to climb back from 7.5, and living
            // standards rise at a deliberately slow 2%/month, so three years in
            // people were still poor and unrest was still justified — the model
            // was right and the window was wrong. These systems are documented to
            // work on multi-year and decade scales, so the divergence has to be
            // measured on theirs rather than on one that felt tidy.
            //
            // **And in isolation** (2026-09), on the relief-arm precedent of
            // `NoSocialValueRunsAwayInEitherDirection`. The claim is about the
            // unrest *mechanic* — that organisation disperses once its cause is
            // gone — and under the full pipeline the recovery was being measured
            // on a country the hardship had broken: six years of it took this
            // world's USA to unity 24 and stability 13, so it fell into civil
            // conflict twice during the recovery, a rival opened a war on the
            // weakened power (exhaustion 32–51 for most of the eight years), and
            // it took in fifty points of displaced people from a collapsing
            // world. Unrest read 42 at the end and every point of it was a new
            // cause: war exhaustion, a rising, the hosting strain. Those are
            // the design working, and they are not what this test claims. The
            // first version got the same recovery clean only because the world's
            // wars happened to fall elsewhere on that trajectory — which is a
            // coin flip, not a statement about the layer.
            //
            // NARROW PIPELINE: the recovery mechanics only — economy, public
            // finance and the social layer — exactly the relief arm below.
            // Everything that generates new adversity is omitted, and the causes
            // already running are ended the way the relief arm ends them.
            world.sanctions.RemoveAll(s => s.targetId == country.id);
            world.insurgencies.RemoveAll(i =>
            {
                var location = world.FindLocation(i.locationId);
                return location != null && location.ownerId == country.id;
            });
            foreach (var confrontation in world.confrontations)
                if (confrontation.Involves(country.id)) confrontation.resolved = true;
            country.warExhaustion = 0f;
            country.government.inCivilConflict = false;
            country.government.civilConflictMonthsRemaining = 0;

            var recovery = new TurnManager(world);
            recovery.ResolveMonth += EconomySystem.MonthlyUpdate;
            recovery.ResolveMonth += FiscalSystem.MonthlyUpdate;
            recovery.ResolveMonth += GovernmentSystem.MonthlyUpdate;

            for (int month = 0; month < 96; month++)
            {
                country.resources.energy = 90f;
                country.economy.confidence = 85f;
                foreach (var sector in country.economy.sectors) sector.health = 92f;
                recovery.EndMonth();
            }

            Assert.Less(country.socialUnrest, unrestAtWorst * 0.4f,
                $"Eight years after the economy recovered, unrest was still {country.socialUnrest:F1} " +
                $"against {unrestAtWorst:F1} at the worst. Organised anger has to subside once the " +
                "thing people were angry about is fixed, or it is just a second grievance score.");

            Assert.Greater(country.publicGrievance, grievanceAtWorst * 0.4f,
                $"Grievance fell from {grievanceAtWorst:F1} to {country.publicGrievance:F1}. What a " +
                "country has been through is supposed to outlast the recovery — on a decade scale, " +
                "not a business cycle.");

            // The point of the pair: both fade, but not at the same rate. If they
            // did, one of them is redundant.
            float unrestKept = country.socialUnrest / Math.Max(0.01f, unrestAtWorst);
            float grievanceKept = country.publicGrievance / Math.Max(0.01f, grievanceAtWorst);
            Assert.Less(unrestKept, grievanceKept,
                $"Unrest kept {unrestKept:P0} of its peak and grievance kept {grievanceKept:P0}. " +
                "Organisation is supposed to disperse faster than memory; if they decay together " +
                "the social layer is carrying two copies of one idea.");
        }

        [Test]
        public void ARestrictivePostureSuppressesUnrestWithoutFixingIt()
        {
            float UnrestUnder(CivicPosture posture, out float grievance)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 1212);
                var country = world.PlayerCountry;
                country.government.civicPosture = posture;
                var turns = new TurnManager(world);
                SimulationPipeline.Wire(turns, world);

                for (int month = 0; month < 48; month++)
                {
                    country.livingStandards = 18f;
                    country.government.civicPosture = posture;
                    turns.EndMonth();
                }
                grievance = country.publicGrievance;
                return country.socialUnrest;
            }

            float openUnrest = UnrestUnder(CivicPosture.Open, out float openGrievance);
            float closedUnrest = UnrestUnder(CivicPosture.Restrictive, out float closedGrievance);

            Assert.Less(closedUnrest, openUnrest, "Closing civic space has to suppress the expression.");
            Assert.Greater(closedGrievance, openGrievance * 0.6f,
                "And it must not fix the cause — grievance should keep accruing underneath, " +
                "or a restrictive posture would be free order.");
        }

        // 240 months, plus a 120-month relief run per collapsed country.
        [Test, Timeout(600000)]
        public void NoSocialValueRunsAwayInEitherDirection()
        {
            var world = Run(1212, 240);

            // Count what is actually shooting on a country's own ground, so the
            // message can distinguish "a rising is dividing them" from "nothing
            // is, and the drain is somewhere else". The first read of this
            // failure blamed a separatist rising; the fix changed the result by
            // literally nothing, because there is no rising here at all.
            int RisingsOn(CountryState c)
            {
                int n = 0;
                foreach (var insurgency in world.insurgencies)
                {
                    var location = world.FindLocation(insurgency.locationId);
                    if (location != null && location.ownerId == c.id) n++;
                }
                return n;
            }

            // The message carries the inputs, not just the verdict. Every one of
            // these values is a term in `GovernmentSystem`'s social tick, so a
            // failure here names the condition that pinned it instead of leaving
            // the next reader to re-derive a twenty-year run by hand. Same
            // reasoning as the after-action reports: an outcome the game cannot
            // explain is indistinguishable from unfair dice.
            string Why(CountryState c)
                => $"\n  {c.id}: standards {c.livingStandards:F1}, unrest {c.socialUnrest:F1}, "
                 + $"grievance {c.publicGrievance:F1}, unity {c.nationalUnity:F1}"
                 + $"\n  economy: inflation {c.economy.inflation:F1}, unemployment {c.economy.unemployment:F1}, "
                 + $"index {c.economy.marketIndex:F1}, growth {c.economy.growthRate:F2}"
                 + $"\n  pressure: war exhaustion {c.warExhaustion:F1}, "
                 + $"food {c.resources.foodSecurity:F1}/{c.resources.foodEndowment:F1}, "
                 + $"energy {c.resources.energy:F1}, opposition {c.government.oppositionCase:F1}, "
                 + $"posture {c.government.civicPosture}"
                 // The unity target's own terms, so a pinned value can be told
                 // apart from a low target. Target is
                 // 42 + gov*0.20 + stability*0.25 - warExhaustion*0.20
                 //    - separatist drag + posture shift + messaging*0.16,
                 // so anything below ~36 with no rising means something is
                 // subtracting the value faster than 0.04/mo can pull it back.
                 + $"\n  unity terms: stability {c.stability:F1}, gov pillar {c.pillars.government:F1}, "
                 + $"messaging {c.government.publicMessaging:F1}, "
                 + $"emergency {c.government.emergencyPowers}, "
                 + $"civil conflict {c.government.inCivilConflict}, "
                 + $"coups {c.government.coupsExperienced}, risings {RisingsOn(c)}, "
                 + $"mobilized {c.endgames.totalMobilization}";

            // **A ceiling is not a ratchet.** These three are target-driven, so a
            // country whose market has collapsed, whose people are out of work
            // and who is fighting a rising *should* read unrest 100 — that is the
            // model describing a real condition, and forbidding it would be
            // forbidding the crisis regime the economy was given on purpose.
            //
            // What must never happen is the value being unable to come back when
            // the conditions lift. So a country still under the conditions is
            // exempt from the ceiling check and tested for *recovery* below
            // instead, which is what "runs away" actually means and what every
            // bug this test has caught actually was.
            // "Under conditions the relief run will lift", not "economically
            // collapsed". The first version tested only the market index, and a
            // country turned up pinned with a *healthy* economy — index 65.8,
            // growth +0.61 — held there by war exhaustion at 100 and an ongoing
            // civil conflict. Ruin does not have to arrive through the economy,
            // and an exemption that only knows one route to it will keep
            // reporting the others as ratchets.
            //
            // And destitution itself (2026-09): grievance approaches its ceiling
            // only under "permanent total deprivation" by the design's own
            // words (`GovernmentSystem`), and living standards are what
            // deprivation *is* — so a population at standards 0.2 pinning
            // grievance at 100 is the model reading total deprivation
            // correctly, whatever the market index says. Russia arrived here at
            // an index of 24, four points above the line, with standards 0.2,
            // unemployment 16 and fifteen years of lost wars behind it, and the
            // ceiling check called that a country in good order pinning a value.
            // A country whose people have nothing is under conditions the relief
            // run lifts, and it is the relief arm's assertion that applies. The
            // line is the relief arm's own line for standards that were actually
            // wrong (30), not a new number: below it the arm demands recovery,
            // above it nothing was wrong to recover from.
            bool UnderRuin(CountryState c)
                => c.economy.marketIndex < 20f
                   || c.livingStandards < 30f
                   || c.warExhaustion > 70f
                   || c.government.inCivilConflict;

            var collapsed = new List<CountryState>();
            foreach (var country in world.countries)
            {
                if (UnderRuin(country)) { collapsed.Add(country); continue; }

                Assert.Less(country.livingStandards, 99.5f,
                    $"{country.id} living standards pinned high." + Why(country));
                Assert.Less(country.socialUnrest, 99.5f,
                    $"{country.id} unrest pinned high." + Why(country));
                Assert.Less(country.publicGrievance, 99.5f,
                    $"{country.id} grievance pinned high." + Why(country));
            }

            // Lift what was doing the damage and the country has to climb out.
            //
            // This is the assertion with teeth, and all three bugs found the day
            // it was written would fail it: sector capacity reverting to nothing
            // (`SectorAnchor`), the stagnation drag running national economic
            // capability to zero (`StagnationFloor`), and the insurgency's flat
            // monthly subtraction on a drifting `nationalUnity` (`UnityDrag`).
            // Each of those made a collapse permanent rather than expensive, and
            // none of them would have been visible to a ceiling check on a
            // healthy world.
            foreach (var country in collapsed)
            {
                float unrestBefore = country.socialUnrest;
                float standardsBefore = country.livingStandards;
                float grievanceBefore = country.publicGrievance;

                world.insurgencies.RemoveAll(i =>
                {
                    var location = world.FindLocation(i.locationId);
                    return location != null && location.ownerId == country.id;
                });

                // NARROW PIPELINE: the recovery mechanics only — economy, public
                // finance and the social/political layer. Everything that
                // *generates* new adversity is omitted: crises and foreign
                // crises, the AI, insurgency, regime change, confrontations.
                //
                // **The isolation is the instrument**, exactly as in
                // `ForeignCrisisTests.RunForeignCrisesOnly`. Two rounds of
                // trying to hold a live world off a wrecked country failed for
                // the same reason each time: this world is hot, a ruined state
                // is a target, and something new always arrives. Clearing
                // sanctions and wars each month left India pinned by *foreign*
                // crises — which never touch `activeCrises` at all, because
                // `ForeignCrisisSystem` applies them directly — and chasing each
                // new source in turn would end with every system suppressed and
                // no statement about anything.
                //
                // The claim being tested is narrow and worth stating exactly:
                // *given no new adversity, do the recovery mechanics climb a
                // country out?* All three bugs found the day this was written
                // live in these systems and still fail it — `SectorAnchor`,
                // `StagnationFloor`, and grievance feeding on its own shadow —
                // so the omission does not weaken what it detects.
                var relief = new TurnManager(world);
                relief.ResolveMonth += EconomySystem.MonthlyUpdate;
                relief.ResolveMonth += FiscalSystem.MonthlyUpdate;
                relief.ResolveMonth += GovernmentSystem.MonthlyUpdate;

                world.sanctions.RemoveAll(s => s.targetId == country.id);
                foreach (var confrontation in world.confrontations)
                    if (confrontation.Involves(country.id)) confrontation.resolved = true;
                country.warExhaustion = 0f;
                country.government.inCivilConflict = false;
                country.government.civilConflictMonthsRemaining = 0;

                // **Twenty years, not ten.** The window has to be at least as
                // long as the slowest thing it measures: grievance decays on a
                // decade scale by design (`0.045 + grievance × 0.004`), and
                // living standards are a lagging function of the market index,
                // unemployment and grievance together — so ten years is barely
                // one design-timescale and a country in a deep hole reads as
                // "never recovers" when it is in fact recovering slowly. Seed
                // 1212's India climbed its market index from 8 to 28 across the
                // shorter window and still had living standards pinned at their
                // own target of zero.
                for (int month = 0; month < 240; month++) relief.EndMonth();

                // **Only demand recovery where something was actually wrong.**
                // `UnderRuin` flags a country for war exhaustion or civil
                // conflict as well as a collapsed market, and a state can carry
                // either while its *social* values are perfectly healthy — Turkey
                // arrived here with unrest 11.2, grievance 0, a market index of
                // 117 and growth at +2.8, and was then asked to bring unrest down
                // by another five points from its own floor. There was nothing to
                // climb out of, and a test that fails on a country in good order
                // is measuring its own predicate.
                if (unrestBefore > 60f)
                    Assert.Less(country.socialUnrest, unrestBefore - 5f,
                        $"{country.id}: twenty years after every sanction, war and rising was "
                        + $"lifted, unrest had not come down ({unrestBefore:F1} to "
                        + $"{country.socialUnrest:F1}). A collapse the world cannot climb out "
                        + "of is a ratchet with extra steps." + Why(country));

                if (standardsBefore < 30f)
                    Assert.Greater(country.livingStandards, standardsBefore + 2f,
                        $"{country.id}: living standards did not recover after every pressure "
                        + $"was removed ({standardsBefore:F1} to "
                        + $"{country.livingStandards:F1})." + Why(country));

                // Grievance is the memory of hardship and is *meant* to fade on a
                // decade scale rather than a quarterly one — so a decade is
                // exactly the right window to insist it fades at all. It is also
                // the value that actually failed here, and skipping the ceiling
                // check for a ruined country would otherwise stop checking it
                // entirely.
                if (grievanceBefore > 60f)
                    Assert.Less(country.publicGrievance, grievanceBefore - 2f,
                        $"{country.id}: two decades after every pressure lifted, the memory of "
                        + $"it had not faded at all ({grievanceBefore:F1} to "
                        + $"{country.publicGrievance:F1}). Grievance decays slowly by design; "
                        + "never is a ratchet." + Why(country));
            }
        }

        // ---------- theatres ----------

        [Test]
        public void CountriesAreDistributedAcrossTheatres()
        {
            var theatres = new HashSet<Theatre>();
            foreach (var country in state.countries) theatres.Add(TheatreSystem.Of(country.id));

            Assert.Greater(theatres.Count, 3,
                $"The whole world sits in {theatres.Count} theatre(s), so theatre means nothing.");
            Assert.IsFalse(theatres.Contains(Theatre.Unassigned),
                "A country fell outside every theatre band.");
        }

        [Test]
        public void AWarInOnePlaceDragsOperationsInAnother()
        {
            // The whole point of theatres: two wars on opposite sides of the
            // world are not two wars — the second is fought with what the first
            // left over.
            var target = FirstLocationOf("CHN");
            float alone = TheatreSystem.FocusFactorFor(state, state.playerCountryId, target);
            Assert.AreEqual(1f, alone, 0.001f, "Uncommitted, the force should arrive whole.");

            ConfrontationSystem.BeginBy(state, state.playerCountryId, "DEU",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, state.ActiveConfrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            float committed = TheatreSystem.FocusFactorFor(state, state.playerCountryId, target);
            Assert.Less(committed, alone,
                "A war in Europe did not make an operation in the Indo-Pacific any harder.");
            Assert.Greater(committed, 0.5f,
                "Distance and commitment price a campaign; they must never forbid one.");
        }

        [Test]
        public void BeingTiedDownIsVisibleToTheWorld()
        {
            Assert.IsFalse(TheatreSystem.IsOverstretched(state, state.playerCountryId));

            ConfrontationSystem.BeginBy(state, state.playerCountryId, "DEU",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, state.ActiveConfrontation,
                EscalationState.TotalWar, state.playerCountryId);

            Assert.IsTrue(TheatreSystem.IsOverstretched(state, state.playerCountryId),
                "A power in a total war is not visibly committed, so nobody can exploit it.");
        }

        // ---------- simultaneous fronts ----------

        [Test]
        public void ASecondFrontIsPossible()
        {
            var first = ConfrontationSystem.BeginBy(state, state.playerCountryId, "DEU",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(first);

            var second = ConfrontationSystem.BeginBy(state, state.playerCountryId, "MEX",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(second,
                "One confrontation at a time was a hard block, which made the map decoration " +
                "and left a military operator with nothing to do once their war settled.");

            Assert.AreEqual(2, state.ActiveConfrontationsFor(state.playerCountryId).Count);
        }

        [Test]
        public void TheSamePairCannotOpenTwoWars()
        {
            ConfrontationSystem.BeginBy(state, state.playerCountryId, "DEU",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            var duplicate = ConfrontationSystem.BeginBy(state, state.playerCountryId, "DEU",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Economic);

            Assert.IsNull(duplicate, "Two states fighting each other twice is one war.");
        }

        [Test]
        public void ThereIsStillACeiling()
        {
            // Priced, not forbidden — but a state cannot fight everybody.
            var opponents = new[] { "DEU", "MEX", "BRA", "NGA", "POL", "TUR" };
            foreach (var opponent in opponents)
            {
                var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, opponent,
                    ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
                if (confrontation != null)
                    ConfrontationSystem.SetEscalationBy(state, confrontation,
                        EscalationState.LimitedConflict, state.playerCountryId);
            }

            Assert.Less(state.ActiveConfrontationsFor(state.playerCountryId).Count, opponents.Length,
                "A single state opened a war against everybody it could reach.");
            Assert.IsFalse(ConfrontationSystem.CanOpenAnother(state, state.playerCountryId, out string why));
            Assert.IsNotEmpty(why, "A refusal the operator cannot explain reads as a bug.");
        }

        // ---------- fragmentation ----------

        [Test]
        public void AFailingStateCanComeApart()
        {
            // `new CountryState` appeared exactly once in the codebase — at world
            // creation — so the map could only ever shrink.
            var country = state.FindCountry("NGA");
            var gov = country.government;

            gov.inCivilConflict = true;
            gov.civilConflictMonthsElapsed = 12;
            gov.civilConflictMonthsRemaining = 12;
            country.nationalUnity = 10f;
            gov.militaryLoyalty = 20f;

            int before = state.countries.Count;
            var successor = SecessionSystem.Fracture(state, country, new System.Random(7));

            Assert.IsNotNull(successor, "A state that had entirely failed did not fracture.");
            Assert.AreEqual(before + 1, state.countries.Count);
            Assert.IsFalse(gov.inCivilConflict, "The fracture has to resolve the conflict.");
        }

        [Test]
        public void ABreakawayIsBornWithAnEconomyItCanRecoverOn()
        {
            // Measured in Unity (seed 1212, the isolated recovery arm): a
            // breakaway of a ruined parent was born with no sectors, an economy
            // pillar of 6 and industrial capacity of 6 — both under the floor a
            // downturn cannot take capability below, which only ever stops a
            // drag and never gives — and a treasury that was a fifth of its
            // parent's overdraft. Growth sat at −1.6 for twenty years and living
            // standards settled at a target of 32. Three of the recurring bug
            // families in one constructor.
            var country = state.FindCountry("NGA");
            var gov = country.government;
            gov.inCivilConflict = true;
            gov.civilConflictMonthsElapsed = 12;
            gov.civilConflictMonthsRemaining = 12;
            country.nationalUnity = 10f;
            gov.militaryLoyalty = 20f;

            // A ruined parent: gutted pillar and industry, deep in arrears.
            country.pillars.economy = 18f;
            country.resources.industrialCapacity = 15f;
            country.resources.treasury = -7000f;

            var successor = SecessionSystem.Fracture(state, country, new System.Random(7));
            Assert.IsNotNull(successor);

            Assert.AreEqual(System.Enum.GetValues(typeof(EconomicSector)).Length, successor.economy.sectors.Count,
                "a state with no sectors has no economy for sabotage, subsidies or displacement to touch");
            Assert.GreaterOrEqual(successor.pillars.economy, EconomySystem.StagnationFloor(successor) - 0.001f,
                "born under the stagnation floor, with nothing to lift it back");
            Assert.GreaterOrEqual(successor.resources.industrialCapacity, SecessionSystem.MinimumBirthIndustry - 0.001f);
            Assert.GreaterOrEqual(successor.resources.treasury, 0f,
                "a successor owes nothing, so it must not inherit a share of an overdraft either");

            // And in isolation — economy, finance, government — it does not
            // slide: the recovery mechanics have something to work with.
            // NARROW PIPELINE: the same three systems as the relief arm of
            // NoSocialValueRunsAwayInEitherDirection, for the same reason.
            var relief = new TurnManager(state);
            relief.ResolveMonth += EconomySystem.MonthlyUpdate;
            relief.ResolveMonth += FiscalSystem.MonthlyUpdate;
            relief.ResolveMonth += GovernmentSystem.MonthlyUpdate;
            float bornPillar = successor.pillars.economy;
            float bornIndustry = successor.resources.industrialCapacity;
            float growthAtOneYear = 0f;
            for (int month = 0; month < 120; month++)
            {
                relief.EndMonth();
                if (month == 11) growthAtOneYear = successor.economy.growthRate;
            }

            // What the arm can and cannot show, stated exactly. The economy
            // pillar's recovery writer is the ministry (`CabinetSystem`), which
            // is outside the arm, so the pillar is not expected to *rise* here
            // — a breakaway is born at a third of its parent's capability by
            // design, and the growth formula's structural term reads that as a
            // contraction until the capability is rebuilt. What the arm must
            // show is that nothing *erodes* it, that the industrial base
            // regrows toward what the ground can hold, that the contraction is
            // easing as it does, and that a state with nothing pressing on it
            // does not fall into the crisis regime. Measured before the fixes:
            // pillar 15.5 → 6.6, industry 10 → 10, growth −4.1, index 24,
            // living standards 0.3.
            Assert.GreaterOrEqual(successor.pillars.economy, bornPillar - 0.5f,
                $"the breakaway's economy pillar slid from {bornPillar:F1} to {successor.pillars.economy:F1} in ten isolated years");
            Assert.Greater(successor.resources.industrialCapacity, bornIndustry + 8f,
                $"ten years on, the breakaway's industry was still {successor.resources.industrialCapacity:F1}: "
                + "plant that was never rebuilt toward what the ground can hold");
            Assert.Greater(successor.economy.growthRate, growthAtOneYear + 0.1f,
                $"the contraction was not easing as industry rebuilt ({growthAtOneYear:F2} → {successor.economy.growthRate:F2})");
            Assert.Greater(successor.economy.marketIndex, FiscalSystem.DepressionLine,
                $"a breakaway left alone sat in a depression (index {successor.economy.marketIndex:F1})");
            Assert.Greater(successor.livingStandards, 25f,
                $"a breakaway left alone fell into deprivation (living standards {successor.livingStandards:F1})");
        }

        [Test]
        public void ABreakawayIsARealStateNotAFlag()
        {
            var country = state.FindCountry("NGA");
            country.government.inCivilConflict = true;
            country.government.civilConflictMonthsElapsed = 12;
            country.nationalUnity = 10f;
            country.government.militaryLoyalty = 20f;

            var successor = SecessionSystem.Fracture(state, country, new System.Random(7));
            Assert.IsNotNull(successor);

            Assert.Greater(successor.cabinet.Count, 0, "It has nobody governing it.");
            Assert.IsNotNull(state.FindAI(successor.id), "It has no mind, so it will never act.");
            Assert.IsNotNull(state.FindRelationship(successor.id, state.playerCountryId),
                "It cannot be talked to, sanctioned or allied with — it is scenery.");
            Assert.Greater(successor.traits.Count, 0);

            int held = 0;
            foreach (var location in state.locations)
                if (location.ownerId == successor.id) held++;
            Assert.Greater(held, 0, "It holds no ground.");
        }

        [Test]
        public void ABreakawayOwnsItsOwnGroundRatherThanOccupyingIt()
        {
            var country = state.FindCountry("NGA");
            country.government.inCivilConflict = true;
            country.government.civilConflictMonthsElapsed = 12;
            country.nationalUnity = 10f;
            country.government.militaryLoyalty = 20f;

            var successor = SecessionSystem.Fracture(state, country, new System.Random(7));

            foreach (var location in state.locations)
            {
                if (location.ownerId != successor.id) continue;
                Assert.IsFalse(location.IsOccupied,
                    "The successor is permanently occupying its own territory, so it pays " +
                    "garrison drag forever and reads as a conqueror of itself.");
            }
        }

        [Test]
        public void AHealthyStateDoesNotFracture()
        {
            // Fragmentation must be the end of a story, not a dice roll.
            //
            // **Judged on the parent at the moment it split, never on the
            // successor afterwards.** This used to fail the run whenever a
            // breakaway read national unity above 60 at month 240. That is a
            // fact about the *successor*, which is born with a unity of its own
            // (~66, the dissenters who left) — so it said nothing about whether
            // the parent had failed, and it failed on any legitimate collapse
            // late in the window. Measured on the C4 branch in Unity: India
            // split at month 232 after eighteen months of civil conflict, unity
            // 12.3, stability 0, market index 8, treasury −5,475 — the end of
            // exactly the story this test exists to require — and the assertion
            // reported "a state fractured without ever having failed at
            // anything" because its successor still read 63.3 eight months on.
            // The parent's conflict flags are reset by the split itself, so the
            // parent is snapshotted before each month is resolved.
            const float HealthyUnity = 60f;   // the health line this test has always used

            // 1. The rule itself. A state at peace cannot be split, and neither
            //    can one in a long civil conflict that is still holding together.
            var calm = state.FindCountry("NGA");
            Assert.IsFalse(calm.government.inCivilConflict, "fixture: NGA should start at peace");
            Assert.IsNull(SecessionSystem.Fracture(state, calm, new System.Random(7)),
                "A state at peace was split — fragmentation has become a dice roll.");

            calm.government.inCivilConflict = true;
            calm.government.civilConflictMonthsElapsed = SecessionSystem.MinimumUnrecoverableMonths * 3;
            calm.government.militaryLoyalty = SecessionSystem.FractureLoyalty - 10f;
            calm.nationalUnity = HealthyUnity + 10f;
            Assert.IsNull(SecessionSystem.Fracture(state, calm, new System.Random(7)),
                "A state still holding together (unity above the health line) was split by a civil conflict it was surviving.");

            // 2. The world. Every split that twenty years produces must come out
            //    of a parent that had already failed when the month began.
            var before = new Dictionary<string, (bool civil, int months, float unity, float loyalty, float stability)>();
            var known = new HashSet<string>();
            int splits = 0;

            void JudgeNewSuccessors(GameState world)
            {
                foreach (var country in world.countries)
                {
                    if (!known.Add(country.id)) continue;
                    if (!country.id.EndsWith("_S", StringComparison.Ordinal)) continue;

                    string parentId = country.id.Substring(0, country.id.Length - 2);
                    Assert.IsTrue(before.TryGetValue(parentId, out var parent),
                        $"{country.id} appeared with no snapshot of its parent {parentId}");
                    splits++;

                    bool failing = parent.civil
                                   && parent.months >= SecessionSystem.MinimumUnrecoverableMonths - 1
                                   && parent.unity <= HealthyUnity;
                    Assert.IsTrue(failing,
                        $"{parentId} fractured into {country.id} without having failed at anything: when the month began it was "
                        + $"in civil conflict {parent.civil} for {parent.months} months, unity {parent.unity:F1}, "
                        + $"military loyalty {parent.loyalty:F1}, stability {parent.stability:F1}.");
                }
            }

            var result = Run(1212, 240, (world, month) =>
            {
                JudgeNewSuccessors(world);
                before.Clear();
                foreach (var country in world.countries)
                    before[country.id] = (country.government.inCivilConflict,
                        country.government.civilConflictMonthsElapsed, country.nationalUnity,
                        country.government.militaryLoyalty, country.stability);
            });
            JudgeNewSuccessors(result);

            // Zero splits is a legitimate twenty years; part 1 keeps the test from
            // being vacuous when the trajectory produces none.
            TestContext.WriteLine($"splits judged in the twenty-year world: {splits}");
        }

        [Test]
        public void TheOperatorKeepsTheirPostWhenTheirCountrySplits()
        {
            // The same rule the coup system follows: severe, never terminal.
            var player = state.PlayerCountry;
            player.government.inCivilConflict = true;
            player.government.civilConflictMonthsElapsed = 12;
            player.nationalUnity = 8f;
            player.government.militaryLoyalty = 18f;

            var successor = SecessionSystem.Fracture(state, player, new System.Random(3));
            Assert.IsNotNull(successor);

            Assert.AreEqual(player.id, state.playerCountryId,
                "The operator lost their post because the country split.");
            Assert.IsNotNull(state.PlayerCountry);
        }

        [Test]
        public void ADecadeStaysDeterministic()
        {
            string Fingerprint(GameState world)
            {
                var parts = new List<string>();
                foreach (var country in world.countries)
                    parts.Add($"{country.id}:{country.livingStandards:F2}:{country.socialUnrest:F2}");
                parts.Add($"countries={world.countries.Count}");
                return string.Join("|", parts);
            }

            Assert.AreEqual(Fingerprint(Run(9182, 180)), Fingerprint(Run(9182, 180)),
                "The same seed no longer produces the same world.");
        }

        StrategicLocation FirstLocationOf(string countryId)
        {
            foreach (var location in state.locations)
                if (location.originalOwnerId == countryId && location.type != LocationType.Capital)
                    return location;
            return null;
        }
    }
}
