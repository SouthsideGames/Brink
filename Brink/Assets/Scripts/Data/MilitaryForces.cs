using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// Force branches (GDD §19). Force structure uses branches and enablers,
    /// never individual vehicle counts.
    /// </summary>
    public enum ForceBranch
    {
        Ground,
        Air,
        Naval
    }

    /// <summary>
    /// One branch of a nation's armed forces. Strength is durable capacity;
    /// readiness and supply are the volatile multipliers that decide whether
    /// that capacity can actually be used (GDD §19).
    /// </summary>
    [Serializable]
    public class BranchForce
    {
        public ForceBranch branch;
        public float strength;  // 0..100 durable force capacity
        public float readiness; // 0..100 trained, manned, deployable now
        public float supply;    // 0..100 fuel, munitions, sustainment

        /// <summary>
        /// What this branch actually holds (GDD §19, amended). Counts are the
        /// truth; <see cref="strength"/> is a cached mirror of them.
        ///
        /// The mirror exists so that resolution, the AI and every balance figure
        /// keep reading one aggregate number, while the operator reads and buys
        /// real things. Anything that changes the inventory must call
        /// <see cref="SyncStrength"/>, and anything that changes strength should
        /// go through <see cref="SetStrength"/> so the inventory follows.
        /// </summary>
        public ForceInventory inventory = new ForceInventory();

        /// <summary>Recompute cached strength from what the branch holds.</summary>
        public void SyncStrength()
            => strength = Core.AssetCatalog.StrengthFrom(inventory, branch);

        /// <summary>
        /// Set strength and scale the inventory to match.
        ///
        /// Every pre-existing site that assigned `strength` directly now routes
        /// here, so a loss computed in the aggregate becomes a loss of actual
        /// aircraft and hulls without each of those sites needing to know what a
        /// tanker is.
        /// </summary>
        public void SetStrength(float value)
        {
            float target = value < 0f ? 0f : (value > 100f ? 100f : value);

            if (strength > 0.01f)
            {
                Core.AssetCatalog.Scale(inventory, branch, target / strength);
            }
            else if (target > 0.01f)
            {
                // Coming back from nothing: there is no ratio to scale, so refill
                // to the shape the catalogue describes.
                Core.AssetCatalog.FillToStrength(inventory, branch, target);
            }

            strength = target;
        }

        /// <summary>Combat power actually available this month.</summary>
        public float EffectivePower => strength * (0.35f + 0.65f * readiness / 100f) * (0.4f + 0.6f * supply / 100f) / 100f;
    }

    /// <summary>
    /// Standing posture (GDD §19). A deliberate choice with real costs, not a
    /// flag the simulation flips: readiness, upkeep and how dangerous neighbors
    /// think we are all follow from it.
    /// </summary>
    public enum MilitaryPosture
    {
        Peacetime,
        Alert,
        Forward
    }

    /// <summary>
    /// Force employment doctrine (GDD §19). Shapes how operations resolve rather
    /// than granting raw capability.
    /// </summary>
    public enum MilitaryDoctrine
    {
        Balanced,
        Maneuver,    // speed and tempo; fewer own losses, more collateral risk
        Attrition,   // grinding pressure; heavier losses both ways
        Deterrence   // posture over employment; credible threats, cautious operations
    }

    /// <summary>
    /// A multi-year procurement program (GDD §19). Force structure is built over
    /// time and paid for monthly — capability cannot be bought in an afternoon.
    /// </summary>
    [Serializable]
    public class ProcurementProgram
    {
        public ForceBranch branch;
        public int monthsRemaining;
        public float strengthPerMonth;
        public float costPerMonth;
        public string label;
    }

    /// <summary>National military posture and force structure.</summary>
    [Serializable]
    public class MilitaryState
    {
        public BranchForce ground = new BranchForce { branch = ForceBranch.Ground };
        public BranchForce air = new BranchForce { branch = ForceBranch.Air };
        public BranchForce naval = new BranchForce { branch = ForceBranch.Naval };

        public MilitaryPosture posture = MilitaryPosture.Peacetime;
        public MilitaryDoctrine doctrine = MilitaryDoctrine.Balanced;

        /// <summary>0..100 investment in sustainment: raises the supply ceiling.</summary>
        public float logistics = 40f;

        /// <summary>
        /// Whether the budget has been re-cut toward the military (GDD §19, §13).
        ///
        /// Roughly doubles delivery tempo. Gated on political backing rather than
        /// on money, because that is what the request actually is: moving money
        /// faster than the normal budget allows is something a legislature or an
        /// inner circle grants, and it costs standing every month it is held.
        /// </summary>
        public bool warFooting;

        /// <summary>
        /// 0..100 the shield against things that arrive through the air (GDD §19).
        ///
        /// Blunts incoming strikes, strategic bombing and leadership strikes, and
        /// raises the bar for a decisive instrument aimed our way. Zero is right
        /// for an old save — nobody had built any — so no migration is needed.
        /// </summary>
        public float missileDefense;

        public List<ProcurementProgram> programs = new List<ProcurementProgram>();

        /// <summary>True when the force is held above peacetime readiness.</summary>
        public bool alertPosture
        {
            get => posture != MilitaryPosture.Peacetime;
            set => posture = value
                ? (posture == MilitaryPosture.Peacetime ? MilitaryPosture.Alert : posture)
                : MilitaryPosture.Peacetime;
        }

        public BranchForce Get(ForceBranch branch)
        {
            switch (branch)
            {
                case ForceBranch.Ground: return ground;
                case ForceBranch.Air: return air;
                case ForceBranch.Naval: return naval;
                default: throw new ArgumentOutOfRangeException(nameof(branch));
            }
        }

        /// <summary>Aggregate deployable power across all branches.</summary>
        public float TotalPower => ground.EffectivePower + air.EffectivePower + naval.EffectivePower;
    }
}
