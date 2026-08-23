using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Total conquest (GDD §16, §19, §22).
    ///
    /// Holding every one of a country's locations used to mean nothing in
    /// particular: the war ran on, the ground sat under permanent occupation
    /// upkeep, and the only way to finish was to talk. That is wrong at the
    /// extreme — a state with no territory left has nothing to negotiate *with*,
    /// and asking the player to open a settlement panel to formalise a total
    /// military victory is paperwork, not strategy.
    ///
    /// So taking everything ends it, and the ground is **annexed** rather than
    /// occupied: `originalOwnerId` moves, which is what makes the gain permanent
    /// and stops the conqueror paying occupation costs on land nobody is coming
    /// back for.
    ///
    /// Three things keep it from being the dominant strategy:
    ///
    /// 1. **The conquered state survives.** GDD §22's rule is that catastrophe
    ///    produces a new gameplay state rather than a game over, and that holds
    ///    for the loser too. Removing a country would also orphan every
    ///    relationship, trade link, AI state and chronicle entry pointing at it —
    ///    the save validator rejects exactly that.
    /// 2. **The world reacts.** Annexing a state is the most alarming thing a
    ///    government can do, and every other state revises its threat perception
    ///    accordingly. Conquest buys land and costs the diplomatic pillar.
    /// 3. **It is genuinely hard.** Capitals are defended, and holding every
    ///    location at once means winning everywhere before losing anywhere.
    /// </summary>
    public static class ConquestSystem
    {
        /// <summary>Share of the conquered state's movable resources that transfers.</summary>
        public const float SpoilsShare = 0.6f;

        /// <summary>Checked after every operation resolves.</summary>
        public static void CheckForTotalConquest(GameState state, Confrontation confrontation)
        {
            if (confrontation == null || confrontation.resolved) return;

            TryConquer(state, confrontation, confrontation.initiatorId, confrontation.defenderId);
            TryConquer(state, confrontation, confrontation.defenderId, confrontation.initiatorId);
        }

        /// <summary>
        /// True when `conquerorId` holds every location `targetId` began with.
        /// Measured against `originalOwnerId`, so a state that has already been
        /// annexed does not keep counting as conquerable.
        /// </summary>
        public static bool HoldsEverything(GameState state, string conquerorId, string targetId)
        {
            bool foundAny = false;
            foreach (var location in state.locations)
            {
                if (location.originalOwnerId != targetId) continue;
                foundAny = true;
                if (location.ownerId != conquerorId) return false;
            }
            return foundAny;
        }

        static void TryConquer(GameState state, Confrontation confrontation,
            string conquerorId, string targetId)
        {
            if (!HoldsEverything(state, conquerorId, targetId)) return;

            var conqueror = state.FindCountry(conquerorId);
            var target = state.FindCountry(targetId);
            if (conqueror == null || target == null) return;

            Absorb(state, conqueror, target);
            AlarmTheWorld(state, conqueror, target);

            string summary =
                $"{target.displayName} has been overrun in its entirety. " +
                $"{conqueror.displayName} holds every city and installation it had.";

            ConfrontationSystem.CloseWithSettlement(state, confrontation, conquerorId, summary);

            state.AddNotification(
                conqueror.isPlayer ? NotificationClass.Priority : NotificationClass.Flash,
                "TOTAL CONQUEST",
                conqueror.isPlayer
                    ? $"{target.displayName} has fallen. Its territory and resources are ours. " +
                      "The world has taken note of what we are prepared to do."
                    : $"{conqueror.displayName} has annexed {target.displayName} entirely.",
                conqueror.id, desk: ReportingDesk.Military);

            state.AddChronicle(ChronicleCategory.Military, conquerorId,
                $"{target.displayName} annexed in its entirety by {conqueror.displayName}.",
                Publicity.Public);
            GameLog.Warn("CONQUEST", $"{conquerorId} annexed {targetId} entirely.");
        }

        /// <summary>
        /// One country takes another into itself: territory, resources, and the
        /// reduction of the absorbed state to a rump.
        ///
        /// Shared by conquest and by accession (`AccessionSystem`), because
        /// absorbing a country is absorbing a country however it was agreed — and
        /// two implementations of that would drift apart the first time either
        /// was tuned. What differs between the two routes is the **price**, which
        /// each system applies itself: conquest alarms the world, accession
        /// betrays it.
        /// </summary>
        public static void Absorb(GameState state, CountryState absorber, CountryState absorbed)
        {
            Annex(state, absorber, absorbed);
            TakeSpoils(absorber, absorbed);
        }

        /// <summary>
        /// Title passes. Moving `originalOwnerId` is what separates annexation
        /// from occupation: the ground stops costing garrison upkeep and unrest,
        /// stops generating a standing grievance, and starts counting as the
        /// conqueror's own in every territory calculation.
        /// </summary>
        static void Annex(GameState state, CountryState conqueror, CountryState target)
        {
            foreach (var location in state.locations)
            {
                if (location.originalOwnerId != target.id) continue;
                location.originalOwnerId = conqueror.id;
                location.ownerId = conqueror.id;
                location.foreignOperatorId = string.Empty;
            }
        }

        /// <summary>
        /// What conquest is actually for. Land-bound resources come with the
        /// land; the treasury is looted. Endowments move too — the conqueror now
        /// owns the ground those numbers described, and without that the gain
        /// would erode back to nothing within a decade.
        /// </summary>
        static void TakeSpoils(CountryState conqueror, CountryState target)
        {
            var from = target.resources;
            var to = conqueror.resources;

            void Move(ref float source, ref float destination, float cap)
            {
                float taken = source * SpoilsShare;
                source -= taken;
                destination = destination + taken > cap ? cap : destination + taken;
            }

            to.treasury += from.treasury * SpoilsShare;
            from.treasury *= 1f - SpoilsShare;

            Move(ref from.energyEndowment, ref to.energyEndowment, 100f);
            Move(ref from.materialsEndowment, ref to.materialsEndowment, 100f);
            Move(ref from.industrialCapacity, ref to.industrialCapacity, 100f);
            Move(ref from.manpowerBaseline, ref to.manpowerBaseline, float.MaxValue);
            Move(ref from.manpower, ref to.manpower, float.MaxValue);

            // A conquered state is not a functioning one. It keeps existing —
            // and can be liberated, or rebuilt by whoever inherits it — but it
            // stops being a power.
            target.pillars.military *= 0.15f;
            target.pillars.economy *= 0.3f;
            target.pillars.diplomacy *= 0.3f;
            target.stability = 10f;
            target.nationalUnity = 15f;
            target.governmentApproval = 15f;
        }

        /// <summary>
        /// Everyone watching revises their estimate of what this government will
        /// do. Annexation is the loudest possible signal of intent, and it has to
        /// cost more than the sum of the battles — otherwise conquest is simply
        /// the best move available and the design's promise that no single
        /// strategy dominates stops being true.
        /// </summary>
        static void AlarmTheWorld(GameState state, CountryState conqueror, CountryState target)
        {
            conqueror.pillars.diplomacy = Clamp(conqueror.pillars.diplomacy - 18f);

            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(conqueror.id)) continue;
                string other = relationship.PartnerOf(conqueror.id);
                if (other == target.id) continue;

                relationship.relations = Clamp(relationship.relations - 22f);
                relationship.trust = Clamp(relationship.trust - 18f);
                relationship.SetThreatPerceivedBy(other,
                    Clamp(relationship.ThreatPerceivedBy(other) + 30f));
                relationship.AddMemory(state.date,
                    $"Annexation of {target.displayName}", -3.5f);
            }
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
