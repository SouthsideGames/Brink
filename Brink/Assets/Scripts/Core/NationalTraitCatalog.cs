using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// What a country *is*, beyond its numbers (GDD §10, §17.1).
    ///
    /// Every trait carries a strength and a matching vulnerability. That pairing
    /// is the design rule, not decoration: a trait that was purely good would
    /// make one country strictly better than another, and a roster where the
    /// answer is "play the strong one" has no texture.
    ///
    /// Traits are **authored per country and durable across saves**. Fifteen of
    /// sixteen states previously had none — traits existed only on the player's
    /// country, generated from their assessment — so every foreign power was the
    /// same numbers with a different flag. Combined with personality being rolled
    /// per save, nothing a player learned about a nation carried into the next
    /// game, which quietly defeats the whole premise of an AI that answers a
    /// repeated opening (spec 06 §7b).
    ///
    /// **Every trait here is read somewhere.** A trait with no effect is the
    /// "written but never read" bug in costume, and a test asserts that each id
    /// authored on a country resolves to a catalogue entry.
    /// </summary>
    public static class NationalTraitCatalog
    {
        public const string Martial = "TRAIT_MARTIAL";
        public const string Industrial = "TRAIT_INDUSTRIAL";
        public const string Opaque = "TRAIT_OPAQUE";
        public const string Convening = "TRAIT_CONVENING";
        public const string Maritime = "TRAIT_MARITIME";
        public const string ResourceState = "TRAIT_RESOURCE";
        public const string Fractious = "TRAIT_FRACTIOUS";
        public const string Technocratic = "TRAIT_TECHNOCRATIC";
        public const string Mercantile = "TRAIT_MERCANTILE";
        public const string Besieged = "TRAIT_BESIEGED";

        static readonly Dictionary<string, NationalTrait> byId = new Dictionary<string, NationalTrait>();

        static readonly NationalTrait[] all =
        {
            new NationalTrait
            {
                id = Martial, name = "Martial Tradition",
                description = "Service is respected and the public holds through a long war. " +
                              "The same tradition makes a settlement look like a defeat."
            },
            new NationalTrait
            {
                id = Industrial, name = "Industrial Depth",
                description = "Productive capacity runs deep and sustains a war effort. " +
                              "It also depends on imported inputs nobody controls."
            },
            new NationalTrait
            {
                id = Opaque, name = "Opaque State",
                description = "The state guards its own information well and foreign services " +
                              "struggle against it. Partners find it hard to trust."
            },
            new NationalTrait
            {
                id = Convening, name = "Convening Power",
                description = "Others come to the table when this state calls. Expectations are " +
                              "correspondingly high, and disappointing them costs more."
            },
            new NationalTrait
            {
                id = Maritime, name = "Maritime Nation",
                description = "The sea is the front door. Reach and naval sustainment are " +
                              "exceptional; the economy lives or dies by open lanes."
            },
            new NationalTrait
            {
                id = ResourceState, name = "Resource Endowment",
                description = "What the world needs is in the ground here. Revenue is reliable " +
                              "and the rest of the economy never had to become competitive."
            },
            new NationalTrait
            {
                id = Fractious, name = "Fractious Politics",
                description = "Argument is the normal condition of this state. Nothing is settled " +
                              "quietly, and hardship organises faster than elsewhere."
            },
            new NationalTrait
            {
                id = Technocratic, name = "Technocratic Establishment",
                description = "Capability is pursued methodically and research compounds. " +
                              "The machinery is slow to respond to something it did not plan for."
            },
            new NationalTrait
            {
                id = Mercantile, name = "Mercantile Instinct",
                description = "Trade is the instrument of first resort. Commerce runs deep, " +
                              "and so does exposure to anyone who closes a lane."
            },
            new NationalTrait
            {
                id = Besieged, name = "Siege Mentality",
                description = "This state expects to be encircled and plans accordingly. " +
                              "Preparedness is high; so is the readiness to see a threat."
            }
        };

        public static IReadOnlyList<NationalTrait> All => all;

        public static NationalTrait Find(string id)
        {
            if (byId.Count == 0)
                foreach (var trait in all) byId[trait.id] = trait;
            return byId.TryGetValue(id, out var found) ? found : null;
        }

        /// <summary>Whether this country carries a given trait.</summary>
        public static bool Has(CountryState country, string traitId)
        {
            if (country == null) return false;
            for (int i = 0; i < country.traits.Count; i++)
                if (country.traits[i].id == traitId) return true;
            return false;
        }

        // ---------- what traits actually do ----------
        //
        // Deliberately few call sites, each chosen because it is somewhere the
        // player can *feel* the difference rather than read it in a tooltip.

        /// <summary>Multiplier on how fast public willingness to fight decays.</summary>
        public static float WarSupportResilience(CountryState country)
            => Has(country, Martial) ? 0.6f : 1f;

        /// <summary>Bonus to the strategic-materials and energy ceilings.</summary>
        public static float ResourceCeilingBonus(CountryState country)
            => Has(country, ResourceState) ? 12f : 0f;

        /// <summary>Bonus to counterintelligence, and a penalty to being trusted.</summary>
        public static float CounterIntelBonus(CountryState country)
            => Has(country, Opaque) ? 10f : 0f;

        /// <summary>How readily others accept this state's proposals.</summary>
        public static float PersuasionBonus(CountryState country)
            => Has(country, Convening) ? 8f : 0f;

        /// <summary>Extra projection range, for a state that lives by the sea.</summary>
        public static float ProjectionBonus(CountryState country)
            => Has(country, Maritime) ? 6f : 0f;

        /// <summary>Multiplier on how quickly hardship becomes organised anger.</summary>
        public static float UnrestVolatility(CountryState country)
            => Has(country, Fractious) ? 1.35f : 1f;

        /// <summary>Multiplier on research progress.</summary>
        public static float ResearchRate(CountryState country)
            => Has(country, Technocratic) ? 1.25f : 1f;

        /// <summary>Bonus to trade volume this state sustains.</summary>
        public static float TradeBonus(CountryState country)
            => Has(country, Mercantile) ? 8f : 0f;

        /// <summary>How much more readily this state reads another as a threat.</summary>
        public static float ThreatSensitivity(CountryState country)
            => Has(country, Besieged) ? 1.3f : 1f;
    }
}
