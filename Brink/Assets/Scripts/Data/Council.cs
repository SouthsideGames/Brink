using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>What a motion is asking the world to do.</summary>
    public enum MotionKind
    {
        /// <summary>Say publicly that a state has done something. Costs them standing.</summary>
        Condemnation = 0,

        /// <summary>Authorise measures. Makes coercion cheaper to run and harder to end.</summary>
        SanctionsMandate = 1,

        /// <summary>Pay for something. The one motion that is not aimed at anybody.</summary>
        Relief = 2
    }

    public enum MotionOutcome
    {
        Pending = 0,
        Passed = 1,
        Failed = 2,
        Vetoed = 3
    }

    /// <summary>One thing that was put to the chamber, and what happened to it.</summary>
    [Serializable]
    public class CouncilMotion
    {
        public string id;
        public MotionKind kind;
        public string moverId;
        public string subjectId;
        public GameDate raised;
        public MotionOutcome outcome;

        public int yes;
        public int no;
        public int abstain;

        /// <summary>Who killed it, when a permanent member did. Empty otherwise.</summary>
        public string vetoedById = "";

        public string summary = "";
    }

    /// <summary>A standing authorisation of measures against one state.</summary>
    [Serializable]
    public class CouncilMandate
    {
        public string subjectId;
        public int monthsRemaining;
    }

    /// <summary>A standing public finding against one state.</summary>
    [Serializable]
    public class CouncilCensure
    {
        public string subjectId;
        public int monthsRemaining;
    }

    /// <summary>
    /// The multilateral chamber (GDD §15.2, §28).
    ///
    /// Diplomacy was entirely bilateral plus ad-hoc coalitions: there was no room
    /// where everybody was present, no vote, no public finding, and sanctions
    /// were strictly unilateral — an operator could not organise a joint regime
    /// against anybody, which is the actual instrument states reach for before
    /// they reach for a war.
    ///
    /// Note what is *not* stored: how any given state feels about a motion. That
    /// is computed from the six dimensions of `Relationship` every time, so a
    /// vote is a live read of the world rather than a second, drifting copy of
    /// the diplomatic model.
    /// </summary>
    [Serializable]
    public class CouncilState
    {
        /// <summary>
        /// The seats that can stop anything. Chosen once, from the capability the
        /// world actually opened with, and never revised — a chamber whose
        /// permanent membership tracked this year's league table would have no
        /// grievance in it, and the grievance is the interesting part.
        /// </summary>
        public List<string> permanentMembers = new List<string>();

        /// <summary>Everything ever put to the chamber, newest last.</summary>
        public List<CouncilMotion> record = new List<CouncilMotion>();

        public List<CouncilMandate> mandates = new List<CouncilMandate>();
        public List<CouncilCensure> censures = new List<CouncilCensure>();

        /// <summary>Month index of the last motion, so the chamber is not a firehose.</summary>
        public int lastMotionMonth = -99;

        public bool IsPermanent(string countryId) => permanentMembers.Contains(countryId);
    }
}
