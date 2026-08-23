using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// Hidden doctrine axes scored by the assessment. Never shown to the player
    /// as numbers — the GDD forbids "+2 Intelligence" style feedback (§5).
    /// </summary>
    [Serializable]
    public class DoctrineProfile
    {
        public float force;        // willingness to apply pressure
        public float secrecy;      // preference for covert instruments
        public float coalition;    // multilateral vs unilateral instinct
        public float order;        // institutional control vs openness
        public float horizon;      // long-term patience vs immediate results

        public float militaryAffinity;
        public float economyAffinity;
        public float intelligenceAffinity;
        public float diplomacyAffinity;
        public float governmentAffinity;

        public float AffinityFor(Pillar pillar)
        {
            switch (pillar)
            {
                case Pillar.Military: return militaryAffinity;
                case Pillar.Economy: return economyAffinity;
                case Pillar.Intelligence: return intelligenceAffinity;
                case Pillar.Diplomacy: return diplomacyAffinity;
                default: return governmentAffinity;
            }
        }

        public Pillar StrongestPillar()
        {
            var best = Pillar.Military;
            float bestValue = float.MinValue;
            foreach (Pillar pillar in Enum.GetValues(typeof(Pillar)))
            {
                float value = AffinityFor(pillar);
                if (value > bestValue) { bestValue = value; best = pillar; }
            }
            return best;
        }
    }

    /// <summary>
    /// A national trait: memorable structural identity with both a strength and
    /// a matching vulnerability (GDD §10).
    /// </summary>
    [Serializable]
    public class NationalTrait
    {
        public string id;
        public string name;
        public string description;
    }

    /// <summary>One answer option and the hidden scoring it carries.</summary>
    public class AssessmentOption
    {
        public string text;
        public DoctrineProfile scores = new DoctrineProfile();
    }

    /// <summary>One in-universe assessment scenario (GDD §5).</summary>
    public class AssessmentQuestion
    {
        public string id;
        public string situation;
        public string prompt;
        public AssessmentOption[] options;
    }

    /// <summary>The outcome of the assessment, retained for the save.</summary>
    [Serializable]
    public class AssessmentResult
    {
        public DoctrineProfile doctrine = new DoctrineProfile();
        public string assignedCountryId;
        public NationalPriority startingPriority;
        public List<NationalTrait> traits = new List<NationalTrait>();

        /// <summary>In-universe summary shown at acceptance — never raw numbers.</summary>
        public string classificationText = "";
        public string doctrineText = "";
    }
}
