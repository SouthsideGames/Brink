using System;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Other countries have problems too (GDD §23 amendment).
    ///
    /// `CrisisSystem.SystemicCheck` read `state.PlayerCountry` throughout, so in a
    /// sixteen-state world exactly one government ever had a bad month. Everyone
    /// else was permanently, invisibly fine — which meant there was no internal
    /// trouble anywhere to detect, and nothing collection was *for* beyond
    /// counting equipment.
    /// </summary>
    public class ForeignCrisisTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 5309);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        TurnManager Wired()
        {
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            return turns;
        }

        /// <summary>
        /// Run only this system, advancing the calendar by hand.
        ///
        /// **The isolation is the instrument.** The first version of these tests
        /// ran the full pipeline and then read `stability` — which four other
        /// systems write every month — so they measured mean reversion and
        /// reported it as crisis damage. A capable state pinned high falls toward
        /// its natural level; a failing one pinned low rises. The conclusion came
        /// out exactly inverted, and it looked plausible.
        ///
        /// Here the only thing that touches the world is the system under test, so
        /// anything that moves is attributable. That is a legitimate narrow scope
        /// rather than a convenience: what is being asserted is what *this system*
        /// does, not what a world containing it looks like.
        /// </summary>
        void RunForeignCrisesOnly(int months, Action beforeEachMonth = null)
        {
            for (int i = 0; i < months; i++)
            {
                beforeEachMonth?.Invoke();
                ForeignCrisisSystem.MonthlyUpdate(state);
                state.date = state.date.NextMonth();
            }
        }

        int ForeignChronicleEntries()
        {
            int count = 0;
            foreach (var entry in state.chronicle)
                if (entry.countryId != state.playerCountryId
                    && entry.category == ChronicleCategory.Political) count++;
            return count;
        }

        /// <summary>Put one state in a condition that qualifies for several situations.</summary>
        void Afflict(CountryState country)
        {
            country.resources.foodSecurity = 30f;
            country.resources.energy = 20f;
            country.economy.inflation = 16f;
            country.governmentApproval = 35f;
            country.livingStandards = 25f;
            country.socialUnrest = 65f;
            country.nationalUnity = 25f;
        }

        // ---------- the world has trouble of its own ----------

        [Test]
        public void ADecadeProducesCrisesInStatesOtherThanOurs()
        {
            RunForeignCrisesOnly(120, () =>
            {
                Afflict(state.FindCountry("RUS"));
                Afflict(state.FindCountry("IND"));
            });

            Assert.Greater(ForeignChronicleEntries(), 0,
                "Ten years, two states in visible distress, and nothing bad happened to " +
                "anybody but us. The world is scenery.");
        }

        [Test]
        public void AHealthyStateIsLeftAlone()
        {
            // The same invariant the player's own crises are held to: adversity
            // has to come from conditions, never from a card draw.
            var turns = Wired();

            void SettleEveryone()
            {
                foreach (var country in state.countries)
                {
                    country.resources.foodSecurity = 95f;
                    country.resources.energy = 95f;
                    country.economy.inflation = 2f;
                    country.governmentApproval = 85f;
                    country.livingStandards = 75f;
                    country.socialUnrest = 4f;
                    country.nationalUnity = 82f;
                    country.government.leader.age = 50f;
                    country.government.successorReadiness = 80f;
                }
            }

            RunForeignCrisesOnly(60, SettleEveryone);

            Assert.AreEqual(0, ForeignChronicleEntries(),
                "A world of well-supplied, popular, united states produced crises anyway. " +
                "Adversity has to come from conditions, never from a card draw.");
        }

        [Test]
        public void AWeakGovernmentSuffersMoreThanACapableOne()
        {
            // Tested on the pure function rather than by running a decade and
            // summing stability drops. That approach measured the *restoring
            // force*: a capable state pinned high falls toward its natural level
            // every month and scored 193 points of phantom damage, while a failing
            // one pinned low rose and scored almost none — the conclusion came out
            // exactly inverted, and read as a plausible finding about the game.
            var failing = state.FindCountry("RUS");
            failing.pillars.government = 20f;
            failing.stability = 25f;
            failing.government.leader.competence = 25f;

            var capable = state.FindCountry("CHN");
            capable.pillars.government = 85f;
            capable.stability = 80f;
            capable.government.leader.competence = 85f;

            float weakSeverity = ForeignCrisisSystem.SeverityFor(failing);
            float capableSeverity = ForeignCrisisSystem.SeverityFor(capable);

            Assert.Greater(weakSeverity, capableSeverity,
                $"A failing state takes {weakSeverity:F2} and a capable one {capableSeverity:F2}. " +
                "If they cope equally well, watching a rival's condition tells you nothing.");

            Assert.Greater(weakSeverity - capableSeverity, 0.3f,
                "The gap has to be wide enough to be worth reading an estimate for.");
        }

        // ---------- the settled boundary ----------

        [Test]
        public void AForeignCrisisNeverBecomesACrisisTurn()
        {
            // Settled decision: a Crisis Turn is an *operator interface*. A foreign
            // government simply decides. If somebody else's bad month ever blocked
            // the player's END MONTH, this feature would have crossed the line it
            // was explicitly built not to cross.
            // Asserted as "this system never appends to `activeCrises`", not as
            // "no open crisis names a foreign subject" — the first version tested
            // the latter and failed, correctly, because the player's *own* crises
            // legitimately name foreign states as their subject. DIPLOMATIC_INSULT
            // is about a rival by construction. That test would have forced a real
            // feature out of the game to satisfy a mis-stated rule.
            RunForeignCrisesOnly(120, () =>
            {
                Afflict(state.FindCountry("RUS"));
                Afflict(state.FindCountry("CHN"));

                Assert.AreEqual(0, state.activeCrises.Count,
                    "A foreign state's situation opened a Crisis Turn. A Crisis Turn "
                    + "interrupts the *operator's* month; a foreign government decides for "
                    + "itself. This is the boundary the feature was built not to cross.");
            });

            Assert.Greater(ForeignChronicleEntries(), 0,
                "Nothing happened at all, so the boundary was never actually tested.");
        }

        [Test]
        public void OneCountrysMisfortuneDoesNotImmunizeTheRest()
        {
            // Cooldowns are keyed by country as well as definition. Sharing the
            // player's bare-id list would mean a shortage in Brazil made every
            // other state immune to shortages for two and a half years.
            RunForeignCrisesOnly(240, () =>
            {
                foreach (var country in state.countries)
                    if (!country.isPlayer) Afflict(country);
            });

            var afflicted = new HashSet<string>();
            foreach (var entry in state.chronicle)
                if (entry.countryId != state.playerCountryId
                    && entry.category == ChronicleCategory.Political)
                    afflicted.Add(entry.countryId);

            Assert.Greater(afflicted.Count, 2,
                $"Only {afflicted.Count} state(s) ever had trouble across twenty years with " +
                "the whole world in distress — cooldowns are leaking between countries.");
        }

        [Test]
        public void ItDoesNotFloodTheRecord()
        {
            // A world where somebody is always in crisis is as flat as one where
            // nobody ever is, and it makes the chronicle unreadable.
            RunForeignCrisesOnly(120, () =>
            {
                foreach (var country in state.countries)
                    if (!country.isPlayer) Afflict(country);
            });

            // Counted in isolation. The first version ran the full pipeline and
            // counted every Political chronicle entry, which swept up elections,
            // leadership changes and coups across fifteen states — it reported 734
            // "foreign crises" in 120 months against an expected ~60, and the
            // number was almost entirely somebody else's election.
            int foreignEntries = ForeignChronicleEntries();

            Assert.Less(foreignEntries, 120,
                $"{foreignEntries} foreign crises in 120 months with everyone afflicted. " +
                "Somebody is in trouble every single month; the record stops meaning anything.");
            Assert.Greater(foreignEntries, 0, "Nothing fired, so the ceiling is untested.");
        }

        [Test]
        public void ADecadeStaysDeterministic()
        {
            string Fingerprint(int seed)
            {
                var world = WorldFactory.CreateDebugWorld(seed);

                for (int month = 0; month < 120; month++)
                {
                    foreach (var country in world.countries)
                        if (!country.isPlayer) country.resources.energy = 20f;
                    ForeignCrisisSystem.MonthlyUpdate(world);
                    world.date = world.date.NextMonth();
                }

                var parts = new List<string>();
                foreach (var country in world.countries)
                    parts.Add($"{country.id}:{country.stability:F2}:{country.governmentApproval:F2}");
                return string.Join("|", parts);
            }

            Assert.AreEqual(Fingerprint(5309), Fingerprint(5309));
        }
    }
}
