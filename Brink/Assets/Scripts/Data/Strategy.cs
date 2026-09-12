using System;
using System.Collections.Generic;

namespace Brink.Data
{
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

    /// <summary>
    /// A goal written by the player rather than handed down by the game.
    /// `achieved` means the condition is true now; `everAchieved` records whether
    /// the posting has ever reached it. This keeps objectives standing rather
    /// than turning them into one-shot quest checkboxes.
    /// </summary>
    [Serializable]
    public class PlayerObjective
    {
        public string id;
        public string title;
        public MandateObjective condition;
        public GameDate created;
        public bool achieved;
        public bool everAchieved;
        public GameDate achievedDate;
    }

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