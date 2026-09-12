using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// The government's standing strategic posture. Doctrine is not a victory
    /// path: it tells delegated institutions what to favour when several sound
    /// choices compete. The operator can still contradict it through ordinary
    /// directives or Direct Control and live with that friction.
    /// </summary>
    public enum StrategicDoctrine
    {
        Balanced,
        Deterrence,
        Prosperity,
        Influence,
        Resilience,
        Transformation
    }

    [Serializable]
    public class StrategicPolicyChoice
    {
        public string slotId;
        public string policyId;
        public GameDate adopted;
    }

    /// <summary>A goal written by the player rather than handed down by the game.</summary>
    [Serializable]
    public class PlayerObjective
    {
        public string id;
        public string title;
        public MandateObjective condition;
        public GameDate created;
        public bool achieved;
        public GameDate achievedDate;
    }

    /// <summary>
    /// Persistent strategy for this posting. It grants no magic national bonus;
    /// doctrine changes delegated emphasis, policies exchange one priority for
    /// another, and objectives are measurements only.
    /// </summary>
    [Serializable]
    public class StrategicPlan
    {
        public StrategicDoctrine doctrine = StrategicDoctrine.Balanced;
        public bool doctrineChosen;
        public GameDate doctrineAdopted;
        public List<StrategicPolicyChoice> policies = new List<StrategicPolicyChoice>();
        public List<PlayerObjective> objectives = new List<PlayerObjective>();
        public int revisionCount;
    }
}