using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Crises that change the world (GDD §23).
    ///
    /// `CrisisOption` used to resolve to four scalars on the player's own
    /// country and nothing else. A crisis could not touch a relationship, a
    /// market, a foreign state or a war, so the most dramatic moments in the
    /// simulation were stat pokes that left nothing behind — the opposite of
    /// §23's premise that events drive the world.
    /// </summary>
    public class CrisisEffectTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 6120);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- the guard that matters most ----------

        [Test]
        public void EveryEffectTheCatalogNamesIsOneWeCanApply()
        {
            // A typo'd effect id would be a choice that resolves, prints its
            // result text, and silently does nothing in the world — which is the
            // exact failure this whole system was built to fix, one layer down.
            var seen = new HashSet<string>();

            foreach (var definition in EventCatalog.Definitions)
            {
                Assert.IsTrue(CrisisEffects.IsKnown(definition.lapseEffectId),
                    $"{definition.id} lapses into unknown effect '{definition.lapseEffectId}'.");
                if (!string.IsNullOrEmpty(definition.lapseEffectId)) seen.Add(definition.lapseEffectId);

                foreach (var option in definition.options(state))
                {
                    Assert.IsTrue(CrisisEffects.IsKnown(option.effectId),
                        $"{definition.id} option '{option.label}' names unknown effect " +
                        $"'{option.effectId}'. It would resolve and do nothing.");
                    if (!string.IsNullOrEmpty(option.effectId)) seen.Add(option.effectId);
                }
            }

            Assert.Greater(seen.Count, 6,
                $"Only {seen.Count} kinds of world effect are used across the whole catalog. " +
                "If crises only ever move relations, they are still stat pokes with a longer name.");
        }

        [Test]
        public void EveryEffectActuallyChangesTheWorld()
        {
            // The "written but never read" class, applied to the registry: an
            // effect nobody can observe is decoration.
            foreach (var effectId in CrisisEffects.All)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 6120);

                // Preconditions an effect needs before it has anything to act on.
                // A fresh world has no covert networks, so EXPOSE_NETWORK would
                // correctly do nothing and this test would blame the effect for
                // the setup's omission.
                world.networks.Add(new IntelNetwork
                {
                    ownerId = world.playerCountryId,
                    targetId = "CHN",
                    focus = IntelDomain.Military,
                    penetration = 45f
                });

                string before = Fingerprint(world);

                // Signed so that every effect is pushed in its meaningful
                // direction; magnitude 2 also selects a real sanction severity.
                CrisisEffects.Apply(world, effectId, "CHN", EffectProbe(effectId));

                Assert.AreNotEqual(before, Fingerprint(world),
                    $"Effect '{effectId}' left the world byte-identical. It is decoration.");
            }
        }

        static float EffectProbe(string effectId)
        {
            switch (effectId)
            {
                case CrisisEffects.ImposeSanction:
                case CrisisEffects.SufferSanction:
                    return 2f;
                case CrisisEffects.OpenConfrontation:
                case CrisisEffects.SufferConfrontation:
                case CrisisEffects.ExposeNetwork:
                    return 0f; // magnitude is not read by these

                case CrisisEffects.Conspiracy:
                    // Starts at zero, so only an increase is observable. Pushing
                    // it negative clamps to where it already was and the effect
                    // looks inert when it is working correctly.
                    return 12f;

                default:
                    return -12f;
            }
        }

        /// <summary>Coarse fingerprint across everything an effect might touch.</summary>
        static string Fingerprint(GameState world)
        {
            var us = world.PlayerCountry;
            var them = world.FindCountry("CHN");
            var relationship = world.FindRelationship(us.id, "CHN");

            float trade = 0f;
            foreach (var link in world.trade) trade += link.volume;

            int compromised = 0;
            foreach (var network in world.networks) if (network.compromised) compromised++;

            return $"{relationship?.relations:F2}|{relationship?.trust:F2}|" +
                   $"{relationship?.threatPerceptionOfA:F2}|{relationship?.threatPerceptionOfB:F2}|" +
                   $"{world.confrontations.Count}|{world.sanctions.Count}|{trade:F2}|" +
                   $"{us.economy.confidence:F2}|{us.economy.marketIndex:F2}|{us.warSupport:F2}|" +
                   $"{us.military.ground.readiness:F2}|{us.government.conspiracyLevel:F2}|" +
                   $"{them?.stability:F2}|{them?.economy.confidence:F2}|" +
                   $"{them?.economy.marketIndex:F2}|{compromised}";
        }

        // ---------- a crisis can start a war ----------

        [Test]
        public void ACrisisCanOpenAConfrontation()
        {
            Assert.IsNull(state.ActiveConfrontation, "Test premise: we are at peace.");

            string report = CrisisEffects.Apply(state, CrisisEffects.OpenConfrontation, "CHN", 0f);

            Assert.IsNotEmpty(report, "Nothing was reported to the operator.");
            Assert.IsNotNull(state.ActiveConfrontation,
                "No event in the game could open a confrontation. The world's most dramatic " +
                "moments could not lead to its most consequential outcome.");
            Assert.AreEqual(state.playerCountryId, state.ActiveConfrontation.initiatorId);
        }

        [Test]
        public void ACrisisCanHaveAConfrontationForcedUponUs()
        {
            CrisisEffects.Apply(state, CrisisEffects.SufferConfrontation, "CHN", 0f);

            var confrontation = state.ActiveConfrontation;
            Assert.IsNotNull(confrontation);
            Assert.AreEqual("CHN", confrontation.initiatorId,
                "A confrontation forced upon us must be initiated by them — who started it " +
                "decides who is judged for it.");
        }

        [Test]
        public void ACrisisWillNotStartAWarOnTopOfAnother()
        {
            ConfrontationSystem.BeginBy(state, state.playerCountryId, "RUS",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            int before = state.confrontations.Count;

            string report = CrisisEffects.Apply(state, CrisisEffects.OpenConfrontation, "CHN", 0f);

            Assert.AreEqual(before, state.confrontations.Count,
                "A crisis resolved into a second simultaneous war.");
            Assert.IsEmpty(report,
                "It reported opening a confrontation that it did not open.");
        }

        [Test]
        public void AnEffectAgainstAVanishedTargetDoesNothingRatherThanThrowing()
        {
            // A crisis fires naming a state, the operator leaves it open, and the
            // world moves on. Resolving into a contradiction must be quiet.
            foreach (var effectId in CrisisEffects.All)
                Assert.DoesNotThrow(
                    () => CrisisEffects.Apply(state, effectId, "NOT_A_COUNTRY", -10f),
                    $"Effect '{effectId}' threw on an unknown target.");

            Assert.DoesNotThrow(() => CrisisEffects.Apply(state, "MADE_UP_EFFECT", "CHN", 5f));
        }

        // ---------- resolution and drift ----------

        [Test]
        public void ResolvingACrisisAppliesItsWorldEffect()
        {
            var crisis = new ActiveCrisis
            {
                defId = "TEST",
                title = "TEST CRISIS",
                body = "…",
                startDate = state.date,
                options = new List<CrisisOption>
                {
                    new CrisisOption
                    {
                        label = "ACT", description = "…", resultText = "Done.",
                        effectId = CrisisEffects.Relations,
                        effectTargetId = "CHN",
                        effectMagnitude = -20f
                    }
                }
            };
            state.activeCrises.Add(crisis);

            var relationship = state.FindRelationship(state.playerCountryId, "CHN");
            float before = relationship.relations;

            CrisisSystem.Resolve(state, crisis, 0);

            Assert.Less(relationship.relations, before,
                "The option carried a world effect and resolving it changed nothing abroad.");
        }

        [Test]
        public void DriftingHasConsequencesBeyondOurOwnStanding()
        {
            // Ignoring a crisis used to cost standing and nothing else, which
            // made drifting the cheapest way to dodge a decision's consequences.
            var crisis = new ActiveCrisis
            {
                defId = "TEST",
                title = "TEST CRISIS",
                body = "…",
                startDate = state.date,
                options = new List<CrisisOption> { new CrisisOption { label = "ACT" } },
                lapseEffectId = CrisisEffects.SufferConfrontation,
                lapseTargetId = "CHN"
            };
            state.activeCrises.Add(crisis);

            CrisisSystem.LapseUnanswered(state);

            Assert.IsEmpty(state.activeCrises);
            Assert.IsNotNull(state.ActiveConfrontation,
                "The situation was left to itself and the situation did nothing. If the " +
                "operator will not decide, the world decides for them.");
        }

        [Test]
        public void TheOperatorIsToldWhatHappenedAbroad()
        {
            // A consequence the player only discovers three months later reads as
            // the simulation cheating.
            var crisis = new ActiveCrisis
            {
                defId = "TEST", title = "TEST CRISIS", body = "…", startDate = state.date,
                options = new List<CrisisOption>
                {
                    new CrisisOption
                    {
                        label = "ACT", resultText = "Done.",
                        effectId = CrisisEffects.SufferConfrontation, effectTargetId = "CHN"
                    }
                }
            };
            state.activeCrises.Add(crisis);
            CrisisSystem.Resolve(state, crisis, 0);

            bool mentioned = false;
            foreach (var entry in state.chronicle)
                if (entry.text.Contains("confrontation")) { mentioned = true; break; }

            Assert.IsTrue(mentioned,
                "The decision started a war and the report said only 'Done.'");
        }

        // ---------- the crisis names one country and keeps naming it ----------

        [Test]
        public void ACrisisActsOnTheStateItNamedWhenItFired()
        {
            // The body text is materialised at fire time. If the effect target
            // were looked up at resolve time instead, a crisis could open by
            // naming China and resolve against Russia because relations shifted
            // while the operator was thinking.
            var crisis = CrisisSystem.Create(state, "BORDER_INCIDENT");
            Assert.IsNotEmpty(crisis.subjectCountryId, "The crisis captured no subject.");

            foreach (var option in crisis.options)
                if (!string.IsNullOrEmpty(option.effectId)
                    && option.effectId != CrisisEffects.Conspiracy)
                    Assert.AreEqual(crisis.subjectCountryId, option.effectTargetId,
                        $"Option '{option.label}' acts on a different state than the one the " +
                        "situation named.");
        }

        [Test]
        public void ACrisisSurvivesBeingSavedAndReloaded()
        {
            // The reason effects are named strings rather than delegates: an
            // option carrying a lambda works perfectly until the operator saves
            // with a crisis open, and then silently loses its consequence.
            var crisis = CrisisSystem.Trigger(state, "CHOKEPOINT_INCIDENT");
            Assert.IsNotNull(crisis);

            string json = SaveSystem.ToJson(state);
            var loaded = SaveSystem.FromJson(json);

            Assert.AreEqual(1, loaded.activeCrises.Count);
            var restored = loaded.activeCrises[0];

            for (int i = 0; i < crisis.options.Count; i++)
            {
                Assert.AreEqual(crisis.options[i].effectId, restored.options[i].effectId,
                    "An option lost its effect across a save.");
                Assert.AreEqual(crisis.options[i].effectTargetId, restored.options[i].effectTargetId,
                    "An option lost its effect target across a save.");
            }
            Assert.AreEqual(crisis.lapseEffectId, restored.lapseEffectId);
            Assert.AreEqual(crisis.subjectCountryId, restored.subjectCountryId);
        }

        // ---------- the catalog stays honest ----------

        [Test]
        public void MostCrisesReachBeyondOurOwnBorders()
        {
            int withEffects = 0;
            foreach (var definition in EventCatalog.Definitions)
            {
                bool reaches = !string.IsNullOrEmpty(definition.lapseEffectId);
                foreach (var option in definition.options(state))
                    if (!string.IsNullOrEmpty(option.effectId)) reaches = true;
                if (reaches) withEffects++;
            }

            Assert.GreaterOrEqual(withEffects, EventCatalog.Definitions.Count / 2,
                $"Only {withEffects} of {EventCatalog.Definitions.Count} events touch the world " +
                "at all. Some crises are genuinely domestic, but most of them should not be.");
        }

        [Test]
        public void ADecadeOfCrisesLeavesAMarkOnTheWorld()
        {
            // End to end: play a decade answering every crisis with its first
            // option and check the world is measurably different from one where
            // nothing was ever decided.
            int faced = 0;

            string PlayDecade(bool answer)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 6120);
                var turns = new TurnManager(world);
                SimulationPipeline.Wire(turns, world);

                int seen = 0;
                for (int month = 0; month < 120; month++)
                {
                    if (answer)
                        while (world.activeCrises.Count > 0)
                        {
                            seen++;
                            CrisisSystem.Resolve(world, world.activeCrises[0], 0);
                        }
                    turns.EndMonth();
                }

                if (answer) faced = seen;

                // **The broad fingerprint, not three fields.**
                //
                // This used to sample summed relations, confrontation count and
                // sanction count — and passed for a long time on a coincidence.
                // Crisis effects in this world are overwhelmingly TRUST and
                // MARKET_SHOCK, none of which it looked at: the two decades
                // genuinely differ (trust 44 vs 28, confidence 4.5 vs 0.0, market
                // index 9.1 vs 7.1, conspiracy 20.4 vs 31.9) and it reported them
                // identical.
                //
                // The one field it did watch was the worst possible choice.
                // Relations mean-revert and clamp at 0/100, so a one-off ±8 nudge
                // is fully erased inside about twenty months — **any test relying
                // on a relations delta surviving a decade is flaky by
                // construction.** It only ever passed because DIPLOMATIC_INSULT
                // was eligible in every world, and tightening that eligibility
                // (it was gated on `ColdestRival != null`, which is always true)
                // removed the last routinely-firing RELATIONS effect and exposed
                // this.
                return Fingerprint(world)
                       + $"|{world.PlayerCountry.stability:F2}"
                       + $"|{world.PlayerCountry.governmentApproval:F2}"
                       + $"|{world.PlayerCountry.nationalUnity:F2}"
                       + $"|{world.PlayerCountry.resources.treasury:F2}";
            }

            string answered = PlayDecade(true);
            string drifted = PlayDecade(false);

            // A decade that produced no crises would compare two identical runs
            // and pass while proving nothing. `crisesFacedThisYear` is no use as
            // the counter — it resets annually and reads 0 at the end of both.
            Assert.GreaterOrEqual(faced, 5,
                $"Only {faced} crises fired in the whole decade, so this says nothing about " +
                "whether answering them matters.");

            Assert.AreNotEqual(answered, drifted,
                "Answering every crisis for a decade and answering none produced the same " +
                $"world. Crisis decisions are not reaching it.\n  answered: {answered}\n  drifted:  {drifted}");
        }
    }
}
