using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Where in the world a thing is happening (GDD §16).
    ///
    /// Derived from authored map coordinates rather than stored, for the same
    /// reason coordinates themselves are authored: a country does not move
    /// during a save, so a theatre is a fact about the world and not part of a
    /// playthrough.
    ///
    /// Appended, never reordered — a confrontation persists the one it belongs to.
    /// </summary>
    public enum Theatre
    {
        Unassigned,
        Americas,
        Atlantic,
        Europe,
        Africa,
        MiddleEast,
        CentralAsia,
        IndoPacific
    }

    /// <summary>
    /// Theatres: the reason a war in one place changes what is possible in
    /// another (GDD §16).
    ///
    /// `GeographySystem` already priced *distance* — a far campaign costs more
    /// because the force arrives lighter. What it could not express is the other
    /// half of geography, which is that a military is a finite thing with a
    /// finite attention span. Two wars on opposite sides of the world are not
    /// simply two wars; the second one is fought by whatever the first one left
    /// over, and everyone watching can see that.
    ///
    /// Three things a theatre does, and each of them is read somewhere:
    ///
    /// 1. **Commitment.** Forces committed in one theatre are not available in
    ///    another. `CommitmentIn` is subtracted from operation power elsewhere.
    /// 2. **Opportunity.** A power visibly tied down invites pressure — the
    ///    AI reads `IsOverstretched` as weakness, which is how a distant war
    ///    becomes a neighbour's opening.
    /// 3. **Legibility.** A confrontation gets a name a player can hold in their
    ///    head — "the Indo-Pacific war" rather than "the confrontation".
    ///
    /// The map stops being decoration at exactly the point where the answer to
    /// "can I afford this war" depends on where the last one was.
    /// </summary>
    public static class TheatreSystem
    {
        /// <summary>
        /// Commitment above which a power is visibly overstretched, and the world
        /// starts treating it as an opportunity.
        /// </summary>
        public const float OverstretchThreshold = 1.4f;

        /// <summary>How much a war in another theatre drains an operation here.</summary>
        public const float DistantDrag = 0.22f;

        /// <summary>
        /// Which theatre a country sits in, from its authored map position.
        ///
        /// Bands rather than a lookup table so a country added later lands
        /// somewhere sensible without anyone remembering to classify it — the
        /// same reasoning that put reach on coordinates rather than on an
        /// authored adjacency list.
        /// </summary>
        public static Theatre Of(string countryId)
        {
            var profile = WorldFactory.FindProfile(countryId);
            if (profile == null) return Theatre.Unassigned;
            return FromPosition(profile.mapX, profile.mapY);
        }

        public static Theatre FromPosition(int mapX, int mapY)
        {
            // Longitude bands across the cylinder, split by latitude where a band
            // genuinely contains two different strategic worlds.
            if (mapX < 30) return Theatre.Americas;
            if (mapX < 34) return Theatre.Atlantic;
            if (mapX < 44) return mapY >= 10 ? Theatre.Africa : Theatre.Europe;
            if (mapX < 50) return mapY >= 9 ? Theatre.MiddleEast : Theatre.Europe;
            if (mapX < 58) return mapY >= 9 ? Theatre.MiddleEast : Theatre.CentralAsia;
            return Theatre.IndoPacific;
        }

        /// <summary>Where a location physically sits.</summary>
        public static Theatre Of(StrategicLocation location)
            => location == null ? Theatre.Unassigned : Of(GeographySystem.HostOf(location));

        /// <summary>
        /// The theatre a confrontation belongs to: where the defender is, since
        /// that is where the fighting happens.
        /// </summary>
        public static Theatre Of(Confrontation confrontation)
            => confrontation == null ? Theatre.Unassigned : Of(confrontation.defenderId);

        /// <summary>Plain name, for the briefing and the chronicle.</summary>
        public static string Name(Theatre theatre)
        {
            switch (theatre)
            {
                case Theatre.Americas: return "THE AMERICAS";
                case Theatre.Atlantic: return "THE ATLANTIC";
                case Theatre.Europe: return "EUROPE";
                case Theatre.Africa: return "AFRICA";
                case Theatre.MiddleEast: return "THE MIDDLE EAST";
                case Theatre.CentralAsia: return "CENTRAL ASIA";
                case Theatre.IndoPacific: return "THE INDO-PACIFIC";
                default: return "UNASSIGNED";
            }
        }

        // ---------- commitment ----------

        /// <summary>
        /// How heavily this country is committed in a given theatre, roughly in
        /// units of "one war". Escalation weights it: a standoff ties down far
        /// less than a total war.
        /// </summary>
        public static float CommitmentIn(GameState state, string countryId, Theatre theatre)
        {
            float total = 0f;
            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved) continue;
                if (!confrontation.Involves(countryId)) continue;
                if (Of(confrontation) != theatre) continue;
                total += WeightOf(confrontation.escalation);
            }
            return total;
        }

        /// <summary>Total commitment everywhere.</summary>
        public static float TotalCommitment(GameState state, string countryId)
        {
            float total = 0f;
            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved) continue;
                if (!confrontation.Involves(countryId)) continue;
                total += WeightOf(confrontation.escalation);
            }
            return total;
        }

        /// <summary>Commitment anywhere other than the named theatre.</summary>
        public static float CommitmentElsewhere(GameState state, string countryId, Theatre theatre)
            => Math.Max(0f, TotalCommitment(state, countryId) - CommitmentIn(state, countryId, theatre));

        static float WeightOf(EscalationState escalation)
        {
            switch (escalation)
            {
                case EscalationState.TotalWar: return 1.6f;
                case EscalationState.LimitedConflict: return 1f;
                case EscalationState.Crisis: return 0.45f;
                default: return 0.2f;
            }
        }

        /// <summary>
        /// Multiplier on operation power here, given what this country is doing
        /// everywhere else.
        ///
        /// Floored, like reach: another war makes a campaign harder, never
        /// impossible. A hard gate would turn a strategic cost into a rule, and
        /// §18.1's lesson was that this simulation prices things rather than
        /// forbidding them.
        /// </summary>
        public static float FocusFactor(GameState state, string countryId, Theatre theatre)
        {
            float elsewhere = CommitmentElsewhere(state, countryId, theatre);
            if (elsewhere <= 0.01f) return 1f;
            return Math.Max(0.55f, 1f - elsewhere * DistantDrag);
        }

        /// <summary>Convenience: the factor for an operation against this location.</summary>
        public static float FocusFactorFor(GameState state, string countryId, StrategicLocation target)
            => FocusFactor(state, countryId, Of(target));

        /// <summary>
        /// Whether this power is visibly tied down. Read by the AI as an
        /// opportunity — which is how a war on one side of the world becomes a
        /// neighbour's opening on the other.
        /// </summary>
        public static bool IsOverstretched(GameState state, string countryId)
            => TotalCommitment(state, countryId) >= OverstretchThreshold;

        /// <summary>Every theatre this country is currently committed in.</summary>
        public static List<Theatre> ActiveTheatresFor(GameState state, string countryId)
        {
            var theatres = new List<Theatre>();
            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved) continue;
                if (!confrontation.Involves(countryId)) continue;
                var theatre = Of(confrontation);
                if (!theatres.Contains(theatre)) theatres.Add(theatre);
            }
            return theatres;
        }

        /// <summary>One line on what we are carrying, for the order screen.</summary>
        public static string DescribeCommitment(GameState state, string countryId, Theatre theatre)
        {
            float elsewhere = CommitmentElsewhere(state, countryId, theatre);
            if (elsewhere <= 0.01f)
                return $"{Name(theatre)}: our only commitment. The force arrives at full weight.";

            float factor = FocusFactor(state, countryId, theatre);
            return $"{Name(theatre)}: committed elsewhere as well — operations here land at "
                   + $"{factor * 100f:F0}% of full weight.";
        }
    }
}
