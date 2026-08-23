using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>Which service runs an operation. Used to group the order screen.</summary>
    public enum OperationDomain
    {
        Ground,
        Naval,
        Air,
        Joint
    }

    /// <summary>
    /// Whose ground an operation is conducted on.
    ///
    /// Until the wider verb list there was only one answer — theirs — and the
    /// military pillar had no defensive vocabulary at all. Fortifying a position,
    /// pacifying occupied ground, escorting our own convoys and defending against
    /// missiles are all things a real staff spends most of its time on, and none
    /// of them could be expressed.
    /// </summary>
    public enum OperationTargeting
    {
        EnemyGround,
        OwnGround,
        Either
    }

    /// <summary>
    /// What an operation is actually fought against.
    ///
    /// The single most important idea in the verb list: if every operation
    /// resolves against the garrison then every operation is an assault with a
    /// different name. An air campaign is stopped by air defences, a blockade by
    /// a fleet, a raid on a ministry by a security service.
    /// </summary>
    public enum DefenseModel
    {
        /// <summary>Garrison plus the defender's army. The standard land fight.</summary>
        Garrison,

        /// <summary>The works themselves, and nothing else.</summary>
        Fortifications,

        /// <summary>Their air force.</summary>
        EnemyAir,

        /// <summary>Their fleet.</summary>
        EnemyNavy,

        /// <summary>Their security services. A large army is nearly irrelevant.</summary>
        CounterIntel,

        /// <summary>Air defences over the position rather than the troops in it.</summary>
        AirDefenses,

        /// <summary>Little organised resistance — construction, logistics, withdrawal.</summary>
        Unopposed,

        /// <summary>Popular resistance in ground we hold but do not own.</summary>
        Insurgency
    }

    /// <summary>
    /// Everything one operation is, in one record (GDD §19).
    ///
    /// This exists because the behaviour of an operation used to be spread across
    /// six switch statements in <see cref="MilitarySystem"/>, and a verb that was
    /// given a row in five of them was a differently-named assault that looked
    /// finished. With the list at twenty-three verbs that was no longer a risk,
    /// it was a certainty. A test asserts every <see cref="OperationType"/> has a
    /// profile here, so the enum and the catalog cannot drift.
    ///
    /// Authored data, never saved — an operation's definition is part of the
    /// game, not part of a playthrough.
    /// </summary>
    public class OperationProfile
    {
        public OperationType type;
        public OperationDomain domain;
        public string displayName;
        public string description;

        /// <summary>Command capacity before escalation premium and skill discounts.</summary>
        public int cpCost;

        // Which branches carry it out. Need not sum to 1 — a special operation
        // deliberately sums to less, because it is small by definition.
        public float ground;
        public float air;
        public float naval;

        /// <summary>
        /// Power comes from the intelligence pillar rather than from the armed
        /// forces. Cyber is the only verb like this, and it is why the field
        /// exists rather than a fourth branch weight.
        /// </summary>
        public bool usesIntelligence;

        /// <summary>How much force is committed: sets both sides' losses.</summary>
        public float intensity;

        /// <summary>
        /// Civilian harm relative to committed weight. How force is applied
        /// matters more than how much of it there is — standoff fires against a
        /// populated place kill civilians out of all proportion to the troops
        /// involved, and a precise action does the opposite.
        /// </summary>
        public float civilianFactor = 1f;

        /// <summary>Can end with us holding the objective. Two verbs only.</summary>
        public bool canTakeGround;

        public OperationTargeting targeting = OperationTargeting.EnemyGround;
        public DefenseModel defense = DefenseModel.Garrison;

        /// <summary>Multiplier on the computed defence. An opposed landing is harder.</summary>
        public float defenseScale = 1f;

        // ---- what has to be true before the order can be given ----

        public bool needsOwnNavy;
        public bool needsOwnAir;

        /// <summary>The target country must have sea access at all.</summary>
        public bool needsMaritimeTarget;

        /// <summary>Nothing to fight otherwise.</summary>
        public bool needsEnemyAir;
        public bool needsEnemyNavy;

        /// <summary>Null means any location. Otherwise the target must be one of these.</summary>
        public LocationType[] locationTypes;

        /// <summary>Only on ground held but not owned.</summary>
        public bool needsOccupied;
    }

    /// <summary>
    /// The authored operation list (GDD §19).
    ///
    /// Ordering within a domain is roughly escalatory, which is the order the
    /// buttons appear in.
    /// </summary>
    public static class OperationCatalog
    {
        static readonly OperationProfile[] profiles =
        {
            // ---------------- Ground ----------------

            new OperationProfile
            {
                type = OperationType.Assault, domain = OperationDomain.Ground,
                displayName = "DELIBERATE ATTACK",
                description = "Ground forces take the objective. One of only two operations that can hold it.",
                cpCost = 3, ground = 0.60f, air = 0.25f, naval = 0.15f,
                intensity = 1f, civilianFactor = 1f, canTakeGround = true,
                defense = DefenseModel.Garrison
            },
            new OperationProfile
            {
                type = OperationType.Raid, domain = OperationDomain.Ground,
                displayName = "RAID",
                description = "Hit and withdraw. Degrades the garrison at limited cost.",
                cpCost = 2, ground = 0.50f, air = 0.50f,
                intensity = 0.5f, civilianFactor = 1f,
                defense = DefenseModel.Garrison
            },
            new OperationProfile
            {
                type = OperationType.Siege, domain = OperationDomain.Ground,
                displayName = "SIEGE",
                description = "Isolate and grind down. Slow, cheaper in lives, wears both sides.",
                cpCost = 2, ground = 0.85f, air = 0.15f,
                intensity = 0.7f, civilianFactor = 1.1f,
                defense = DefenseModel.Garrison
            },
            new OperationProfile
            {
                type = OperationType.PreparedDefense, domain = OperationDomain.Ground,
                displayName = "PREPARED DEFENCE",
                description = "Dig in on ground we hold. Raises its defence value permanently — "
                              + "the cheapest way to make a position expensive to take.",
                cpCost = 2, ground = 0.80f, air = 0.10f, naval = 0.10f,
                intensity = 0.2f, civilianFactor = 0f,
                targeting = OperationTargeting.OwnGround,
                defense = DefenseModel.Unopposed
            },
            new OperationProfile
            {
                type = OperationType.CounterInsurgency, domain = OperationDomain.Ground,
                displayName = "COUNTER-INSURGENCY",
                description = "Pacify occupied ground. Reduces what holding it costs us at home — "
                              + "and it is hard on the people who live there.",
                cpCost = 2, ground = 0.90f, air = 0.10f,
                intensity = 0.45f, civilianFactor = 1.4f,
                targeting = OperationTargeting.OwnGround,
                defense = DefenseModel.Insurgency,
                needsOccupied = true
            },
            new OperationProfile
            {
                type = OperationType.Withdraw, domain = OperationDomain.Ground,
                displayName = "WITHDRAWAL",
                description = "Break off. Relinquishes a captured position and preserves the force.",
                cpCost = 1, ground = 0.60f, air = 0.25f, naval = 0.15f,
                intensity = 0.2f, civilianFactor = 0.2f,
                targeting = OperationTargeting.Either,
                defense = DefenseModel.Unopposed
            },

            // ---------------- Naval ----------------

            new OperationProfile
            {
                type = OperationType.NavalBlockade, domain = OperationDomain.Naval,
                displayName = "BLOCKADE",
                description = "Close their sea lanes. Starves the country's trade and sustainment "
                              + "rather than the objective.",
                cpCost = 3, naval = 1f,
                intensity = 0.3f, civilianFactor = 0.5f,
                defense = DefenseModel.EnemyNavy,
                needsOwnNavy = true, needsMaritimeTarget = true
            },
            new OperationProfile
            {
                type = OperationType.SeaControl, domain = OperationDomain.Naval,
                displayName = "SEA CONTROL",
                description = "Fight their fleet for the water itself. Wins nothing on land and "
                              + "makes every later naval operation easier.",
                cpCost = 3, naval = 0.80f, air = 0.20f,
                intensity = 0.6f, civilianFactor = 0.2f,
                defense = DefenseModel.EnemyNavy,
                needsOwnNavy = true, needsMaritimeTarget = true, needsEnemyNavy = true
            },
            new OperationProfile
            {
                type = OperationType.CommerceRaiding, domain = OperationDomain.Naval,
                displayName = "COMMERCE RAIDING",
                description = "Hunt their merchant shipping. Cheaper than a blockade and slower, "
                              + "and it takes their treasury as well as their trade.",
                cpCost = 2, naval = 0.85f, air = 0.15f,
                intensity = 0.35f, civilianFactor = 0.3f,
                defense = DefenseModel.EnemyNavy, defenseScale = 0.6f,
                needsOwnNavy = true, needsMaritimeTarget = true
            },
            new OperationProfile
            {
                type = OperationType.MineWarfare, domain = OperationDomain.Naval,
                displayName = "MINE WARFARE",
                description = "Deny a port or a strait. Very cheap for what it closes, and it "
                              + "keeps working after the ships that laid it have gone.",
                cpCost = 2, naval = 0.90f, air = 0.10f,
                intensity = 0.25f, civilianFactor = 0.3f,
                defense = DefenseModel.EnemyNavy, defenseScale = 0.45f,
                needsOwnNavy = true, needsMaritimeTarget = true,
                locationTypes = new[] { LocationType.Port, LocationType.Chokepoint }
            },
            new OperationProfile
            {
                type = OperationType.ConvoyEscort, domain = OperationDomain.Naval,
                displayName = "CONVOY ESCORT",
                description = "Protect our own shipping. The answer to a blockade or a raiding "
                              + "campaign, and the only naval operation that defends.",
                cpCost = 2, naval = 0.85f, air = 0.15f,
                intensity = 0.25f, civilianFactor = 0f,
                targeting = OperationTargeting.OwnGround,
                defense = DefenseModel.EnemyNavy, defenseScale = 0.55f,
                needsOwnNavy = true
            },

            // ---------------- Air ----------------

            new OperationProfile
            {
                type = OperationType.SuppressDefenses, domain = OperationDomain.Air,
                displayName = "SUPPRESS DEFENCES",
                description = "Break their air defences and works. Permanently lowers what the "
                              + "next operation must fight through.",
                cpCost = 2, air = 0.75f, naval = 0.25f,
                intensity = 0.35f, civilianFactor = 1.2f,
                defense = DefenseModel.Fortifications,
                needsOwnAir = true
            },
            new OperationProfile
            {
                type = OperationType.AirStrike, domain = OperationDomain.Air,
                displayName = "AIR STRIKE",
                description = "Air power against the position. Cannot take ground; heavy civilian risk.",
                cpCost = 2, air = 1f,
                intensity = 0.55f, civilianFactor = 2.6f,
                defense = DefenseModel.AirDefenses,
                needsOwnAir = true
            },
            new OperationProfile
            {
                type = OperationType.CounterAirCampaign, domain = OperationDomain.Air,
                displayName = "COUNTER-AIR CAMPAIGN",
                description = "Fight their air force for the sky itself. Nothing else in the list "
                              + "can destroy an opponent's air power.",
                cpCost = 3, air = 1f,
                intensity = 0.6f, civilianFactor = 0.3f,
                defense = DefenseModel.EnemyAir,
                needsOwnAir = true, needsEnemyAir = true
            },
            new OperationProfile
            {
                type = OperationType.NoFlyZone, domain = OperationDomain.Air,
                displayName = "NO-FLY ZONE",
                description = "Deny them their own airspace. Grounds their air force without "
                              + "destroying it, and holding it costs us every month.",
                cpCost = 3, air = 1f,
                intensity = 0.4f, civilianFactor = 0.2f,
                defense = DefenseModel.EnemyAir, defenseScale = 0.7f,
                needsOwnAir = true, needsEnemyAir = true
            },
            new OperationProfile
            {
                type = OperationType.AirInterdiction, domain = OperationDomain.Air,
                displayName = "AIR INTERDICTION",
                description = "Cut the supply lines behind the front. The garrison we attack next "
                              + "month will be a hungrier one.",
                cpCost = 2, air = 0.90f, naval = 0.10f,
                intensity = 0.45f, civilianFactor = 0.9f,
                defense = DefenseModel.AirDefenses,
                needsOwnAir = true
            },
            new OperationProfile
            {
                type = OperationType.StrategicBombing, domain = OperationDomain.Air,
                displayName = "STRATEGIC BOMBING",
                description = "Attack the country's capacity to make war at all. The most "
                              + "destructive conventional instrument, and the most costly in civilians.",
                cpCost = 3, air = 1f,
                intensity = 0.7f, civilianFactor = 3f,
                defense = DefenseModel.AirDefenses,
                needsOwnAir = true,
                locationTypes = new[]
                {
                    LocationType.IndustrialCenter, LocationType.EnergyRegion, LocationType.Capital
                }
            },
            new OperationProfile
            {
                type = OperationType.LeadershipStrike, domain = OperationDomain.Air,
                displayName = "LEADERSHIP STRIKE",
                description = "Strike the government itself. Legitimacy is the price: partners "
                              + "recoil, and the country you hit closes ranks behind whoever survives.",
                cpCost = 3, air = 0.85f, ground = 0.15f,
                intensity = 0.3f, civilianFactor = 2.2f,
                defense = DefenseModel.CounterIntel,
                needsOwnAir = true,
                locationTypes = new[] { LocationType.Capital }
            },

            // ---------------- Joint ----------------

            new OperationProfile
            {
                type = OperationType.SpecialOperation, domain = OperationDomain.Joint,
                displayName = "SPECIAL OPERATION",
                description = "Small, precise and deniable. Degrades the garrison with little "
                              + "civilian harm — their counterintelligence is the real defence.",
                cpCost = 1, ground = 0.35f, air = 0.15f,
                intensity = 0.15f, civilianFactor = 0.4f,
                defense = DefenseModel.CounterIntel
            },
            new OperationProfile
            {
                type = OperationType.AmphibiousAssault, domain = OperationDomain.Joint,
                displayName = "AMPHIBIOUS ASSAULT",
                description = "Take a coastal objective from the sea. The other operation that can "
                              + "hold ground, and the hardest thing a military can attempt.",
                cpCost = 4, ground = 0.45f, naval = 0.40f, air = 0.15f,
                intensity = 1f, civilianFactor = 1.1f, canTakeGround = true,
                defense = DefenseModel.Garrison, defenseScale = 1.3f,
                needsOwnNavy = true, needsMaritimeTarget = true,
                locationTypes = new[] { LocationType.Port, LocationType.Chokepoint }
            },
            new OperationProfile
            {
                type = OperationType.CyberOperation, domain = OperationDomain.Joint,
                displayName = "CYBER OPERATION",
                description = "Degrade their command and coordination. Costs almost nothing, is "
                              + "deniable, and runs on the intelligence service rather than the army.",
                cpCost = 1, usesIntelligence = true,
                intensity = 0.1f, civilianFactor = 0.1f,
                defense = DefenseModel.CounterIntel
            },
            new OperationProfile
            {
                type = OperationType.MissileDefense, domain = OperationDomain.Joint,
                displayName = "MISSILE DEFENCE",
                description = "Build the shield. Blunts strikes and strategic bombing against us, "
                              + "and raises the bar for anything decisive aimed our way.",
                cpCost = 3, air = 0.60f, naval = 0.30f, ground = 0.10f,
                intensity = 0.15f, civilianFactor = 0f,
                targeting = OperationTargeting.OwnGround,
                defense = DefenseModel.Unopposed
            },
            new OperationProfile
            {
                type = OperationType.NoncombatantEvacuation, domain = OperationDomain.Joint,
                displayName = "EVACUATE NATIONALS",
                description = "Get our people out before it starts. Cheap, almost always succeeds, "
                              + "and removes the hostage that would otherwise price every later decision.",
                cpCost = 1, air = 0.40f, naval = 0.40f, ground = 0.20f,
                intensity = 0.15f, civilianFactor = 0f,
                defense = DefenseModel.Unopposed
            }
        };

        static Dictionary<OperationType, OperationProfile> byType;

        public static IReadOnlyList<OperationProfile> All => profiles;

        /// <summary>The profile for an operation. Never null for a catalogued type.</summary>
        public static OperationProfile For(OperationType type)
        {
            if (byType == null)
            {
                byType = new Dictionary<OperationType, OperationProfile>();
                foreach (var profile in profiles) byType[profile.type] = profile;
            }
            return byType.TryGetValue(type, out var found) ? found : null;
        }

        /// <summary>Operations in one domain, in authored order.</summary>
        public static List<OperationProfile> InDomain(OperationDomain domain)
        {
            var result = new List<OperationProfile>();
            foreach (var profile in profiles)
                if (profile.domain == domain) result.Add(profile);
            return result;
        }

        // ---------- availability ----------

        /// <summary>
        /// Whether this order can be given at all, and if not, why.
        ///
        /// The reason matters as much as the answer. A greyed-out button with no
        /// explanation reads as a bug; "WE HAVE NO FLEET" reads as the
        /// consequence of a procurement decision made three years ago, which is
        /// what it is. The order screen prints whatever comes back here.
        ///
        /// This is a *possibility* check, not an affordability or wisdom check —
        /// CP is handled separately, and ordering something unwise is the
        /// player's right.
        /// </summary>
        public static bool CanOrder(GameState state, string actorId, StrategicLocation target,
            OperationType type, out string reason)
        {
            reason = "";
            var profile = For(type);
            if (profile == null) { reason = "NO SUCH OPERATION"; return false; }

            var actor = state.FindCountry(actorId);
            if (actor == null) { reason = "NO ACTING STATE"; return false; }
            if (target == null) { reason = "NO OBJECTIVE SELECTED"; return false; }

            // Whose ground this is conducted on.
            bool ours = target.ownerId == actorId;
            switch (profile.targeting)
            {
                case OperationTargeting.OwnGround:
                    if (!ours) { reason = "ONLY ON GROUND WE HOLD"; return false; }
                    break;
                case OperationTargeting.EnemyGround:
                    if (ours) { reason = "WE ALREADY HOLD THIS"; return false; }
                    break;
            }

            if (profile.needsOccupied && !target.IsOccupied)
            {
                reason = "ONLY ON OCCUPIED GROUND";
                return false;
            }

            if (profile.locationTypes != null)
            {
                bool matches = false;
                foreach (var allowed in profile.locationTypes)
                    if (target.type == allowed) { matches = true; break; }
                if (!matches)
                {
                    reason = "WRONG KIND OF OBJECTIVE";
                    return false;
                }
            }

            // Our own force structure. This is the point of branch weighting:
            // what we chose to build decides what we are able to order.
            if (profile.needsOwnNavy && actor.military.naval.EffectivePower <= 0.01f)
            {
                reason = "WE HAVE NO FLEET";
                return false;
            }
            if (profile.needsOwnAir && actor.military.air.EffectivePower <= 0.01f)
            {
                reason = "WE HAVE NO AIR FORCE";
                return false;
            }

            // Geography. A landlocked country cannot be blockaded, mined or
            // landed on, and no amount of fleet changes that.
            string host = GeographySystem.HostOf(target);
            if (profile.needsMaritimeTarget && !GeographySystem.HasSeaAccess(host))
            {
                reason = "TARGET IS LANDLOCKED";
                return false;
            }

            // Nothing to fight.
            var defender = state.FindCountry(target.ownerId);
            if (profile.needsEnemyAir && (defender == null || defender.military.air.EffectivePower <= 0.05f))
            {
                reason = "THEY HAVE NO AIR FORCE";
                return false;
            }
            if (profile.needsEnemyNavy && (defender == null || defender.military.naval.EffectivePower <= 0.05f))
            {
                reason = "THEY HAVE NO FLEET";
                return false;
            }

            return true;
        }

        /// <summary>Convenience overload where the reason is not needed.</summary>
        public static bool CanOrder(GameState state, string actorId, StrategicLocation target,
            OperationType type) => CanOrder(state, actorId, target, type, out _);

        /// <summary>Every operation this actor could order against this objective right now.</summary>
        public static List<OperationType> AvailableAgainst(GameState state, string actorId,
            StrategicLocation target)
        {
            var result = new List<OperationType>();
            foreach (var profile in profiles)
                if (CanOrder(state, actorId, target, profile.type)) result.Add(profile.type);
            return result;
        }
    }
}
