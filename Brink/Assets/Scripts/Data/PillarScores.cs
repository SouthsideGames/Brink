using System;

namespace Brink.Data
{
    /// <summary>Pillar identifiers — the five permanent strategic pillars (GDD §9).</summary>
    public enum Pillar
    {
        Military,
        Economy,
        Intelligence,
        Diplomacy,
        Government
    }

    /// <summary>
    /// Headline pillar readout (GDD §10): a fast 0..100 read of each pillar.
    /// Capabilities live in deeper models added in later phases.
    /// </summary>
    [Serializable]
    public class PillarScores
    {
        public float military;
        public float economy;
        public float intelligence;
        public float diplomacy;
        public float government;

        public float Get(Pillar pillar)
        {
            switch (pillar)
            {
                case Pillar.Military: return military;
                case Pillar.Economy: return economy;
                case Pillar.Intelligence: return intelligence;
                case Pillar.Diplomacy: return diplomacy;
                case Pillar.Government: return government;
                default: throw new ArgumentOutOfRangeException(nameof(pillar));
            }
        }

        public void Set(Pillar pillar, float value)
        {
            switch (pillar)
            {
                case Pillar.Military: military = value; break;
                case Pillar.Economy: economy = value; break;
                case Pillar.Intelligence: intelligence = value; break;
                case Pillar.Diplomacy: diplomacy = value; break;
                case Pillar.Government: government = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(pillar));
            }
        }
    }
}
