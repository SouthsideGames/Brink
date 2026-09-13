using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// A remembered operator/minister relationship event. These are institutional
    /// scars and confidence, not another currency. They explain why a minister's
    /// current relationship feels different from their starting traits.
    /// </summary>
    [Serializable]
    public class InstitutionalMemory
    {
        public string officialId = "";
        public Pillar pillar;
        public string kind = "";
        public string summary = "";
        public GameDate date;
        public float weight;
    }

    [Serializable]
    public class InstitutionalMemoryLedger
    {
        public List<InstitutionalMemory> entries = new List<InstitutionalMemory>();
    }
}
