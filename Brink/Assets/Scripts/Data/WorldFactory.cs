using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// What kind of sea power a country can be at all (GDD §16).
    ///
    /// Three levels rather than a coastal/landlocked bool, because the
    /// interesting distinction is not "has a coast" — almost everyone does — but
    /// whether the sea is the country's front door or a side entrance. An
    /// archipelago and a state with one Baltic port are not doing the same thing
    /// when they buy a frigate.
    /// </summary>
    public enum NavalAccess
    {
        /// <summary>No usable sea access. Cannot build a navy and cannot be blockaded.</summary>
        Landlocked,

        /// <summary>A coast, but not a maritime identity. A real but limited fleet.</summary>
        Coastal,

        /// <summary>The sea is how this country lives. Full naval capability.</summary>
        Maritime
    }

    /// <summary>
    /// Authored starting profile for one nation. Values are gameplay archetype
    /// baselines tuned for balance — they are not claims about real-world
    /// capability. Each country is hand-authored and then evolves systemically
    /// (GDD §31.1).
    /// </summary>
    public class CountryProfile
    {
        public string id;
        public string displayName;

        public float military, economy, intelligence, diplomacy, government;
        public float treasury, manpower, energy, industry, materials, food;
        public float approval, stability, unity;

        public GovernmentType governmentType;
        public int termLengthMonths;

        /// <summary>Position on the ASCII strategic map, in character cells (GDD §16).</summary>
        public int mapX, mapY;

        /// <summary>
        /// How much of a maritime power this country can be (GDD §16, §19).
        ///
        /// Authored rather than derived, and authored *here* rather than saved,
        /// for the same reason coordinates are: a country's coastline does not
        /// change during a playthrough. Before this, naval strength was a flat
        /// 0.75× of the military score for every state, so landlocked Kazakhstan
        /// launched fleets and drew global naval reach on exactly the terms the
        /// United States did — and "we cannot blockade them, they have no sea"
        /// was a sentence the simulation had no way to say.
        /// </summary>
        public NavalAccess navalAccess = NavalAccess.Coastal;

        /// <summary>
        /// Authored Strategic DNA (GDD §17.1): how this government reasons,
        /// 0..100 on the same four axes as <see cref="AIProfile"/>.
        ///
        /// Authored rather than rolled, and this is the whole point. Personality
        /// used to be drawn uniformly in [20,80] at world creation, so a country
        /// was a different country in every save — which quietly defeated the
        /// anti-memorisation work in spec 06 §7b. That design says the world
        /// should *answer* a player who repeats an opening; it cannot do that if
        /// the player has no stable read on who they are answering. Learning that
        /// this state is patient and cautious has to still be true next time.
        ///
        /// A small per-save jitter is still applied on top, so two saves are not
        /// identical — but the character holds.
        /// </summary>
        public float aggression = 50f, caution = 50f, opportunism = 50f, patience = 50f;

        /// <summary>
        /// Authored national traits (GDD §10, §17.1). Ids resolved against
        /// <see cref="Brink.Core.NationalTraitCatalog"/>.
        ///
        /// Fifteen of sixteen countries had none — traits existed only on the
        /// player's country, generated from the assessment. So every foreign
        /// state was structurally anonymous: the same numbers with a different
        /// flag.
        /// </summary>
        public string[] traitIds = new string[0];

        /// <summary>Two-letter code drawn on the map.</summary>
        public string mapCode;

        public string[] firstNames;
        public string[] lastNames;

        /// <summary>Office titles in pillar order: Military, Economy, Intelligence, Diplomacy, Government.</summary>
        public string[] officeTitles;
    }

    /// <summary>
    /// How much of the authored world a new save opens with (GDD §31.2
    /// amendment). Chosen once, at the assessment, and fixed for the save —
    /// world composition is a fact about a playthrough, not a setting.
    ///
    /// `Standard` is declared first so its ordinal is zero: an old save
    /// deserializes to the sixteen-state world it was actually created in.
    /// Every measured balance figure was taken on Standard; Regional and Full
    /// are curated subsets/supersets whose margins have not been separately
    /// measured.
    /// </summary>
    public enum WorldSize
    {
        /// <summary>The sixteen-state measured world. The default.</summary>
        Standard,

        /// <summary>Ten states: the great-power core plus its key theatres.</summary>
        Regional,

        /// <summary>Every authored country.</summary>
        Full
    }

    /// <summary>
    /// Builds the four-country MVP world (GDD §34 vertical slice). The launch
    /// target is roughly 16 authored countries; these four prove the simulation.
    /// </summary>
    public static class WorldFactory
    {
        public const string PlayerCountryId = "USA";

        /// <summary>
        /// The measured sixteen-state roster — the world every balance figure in
        /// the project was taken on, and what <see cref="CreateDebugWorld"/>
        /// builds. Kept as an explicit list rather than "everything authored
        /// before a date" so growing <see cref="Profiles"/> can never silently
        /// change the world the harness measures.
        /// </summary>
        public static readonly string[] StandardRoster =
        {
            "USA", "CHN", "RUS", "IND", "DEU", "JPN", "BRA", "TUR",
            "NGA", "SAU", "AUS", "KOR", "MEX", "IDN", "POL", "KAZ"
        };

        /// <summary>
        /// The ten-state regional world. Curated, not truncated: the great-power
        /// core stays (the AI rivalry systems were tuned against it), every
        /// member keeps several trade links inside the set, and all three
        /// authored food dependencies survive intact. Trimming took the
        /// mid-tier and buffer states, never the majors.
        /// </summary>
        public static readonly string[] RegionalRoster =
        {
            "USA", "CHN", "RUS", "IND", "DEU", "JPN", "KOR", "SAU", "TUR", "AUS"
        };

        /// <summary>Which country ids a world of the given size contains.</summary>
        public static string[] RosterFor(WorldSize size)
        {
            switch (size)
            {
                case WorldSize.Regional: return RegionalRoster;
                case WorldSize.Full:
                {
                    var ids = new string[Profiles.Length];
                    for (int i = 0; i < Profiles.Length; i++) ids[i] = Profiles[i].id;
                    return ids;
                }
                default: return StandardRoster;
            }
        }


        /// <summary>
        /// The authored launch roster (GDD §31.2). Sixteen countries chosen for
        /// archetype diversity — major powers, regional powers, resource powers,
        /// industrial mid-tiers, a maritime archipelago and buffer states —
        /// because a world of peers has no texture. Every country carries at
        /// least one genuine vulnerability; that is what creates play.
        ///
        /// All values are gameplay archetype baselines tuned for balance. They
        /// are not claims about real-world capability, and government types are
        /// structural descriptions rather than judgments.
        /// </summary>
        public static readonly CountryProfile[] Profiles =
        {
            // ---------------- major powers ----------------
            new CountryProfile
            {
                id = "USA", displayName = "United States",
                military = 84, economy = 82, intelligence = 86, diplomacy = 76, government = 66,
                // Vulnerability: dependent on imported critical minerals, and
                // politically divided at home.
                treasury = 2100, manpower = 900, energy = 78, industry = 70, materials = 42, food = 88,
                approval = 48, stability = 64, unity = 46,
                governmentType = GovernmentType.PresidentialRepublic, termLengthMonths = 48,
                mapX = 14, mapY = 6, mapCode = "US",
                aggression = 62, caution = 45, opportunism = 68, patience = 40,
                traitIds = new[] { Core.NationalTraitCatalog.Convening, Core.NationalTraitCatalog.Maritime },
                navalAccess = NavalAccess.Maritime,
                firstNames = new[] { "JAMES", "MARGARET", "ANDRE", "CAROL", "DAVID", "PRIYA", "ROBERT", "ELENA" },
                lastNames = new[] { "HOLLAND", "REYES", "CALDWELL", "OKAFOR", "WHITFIELD", "NAKAMURA", "BRENNAN", "SHAW" },
                officeTitles = new[]
                {
                    "Secretary of Defense", "Secretary of the Treasury",
                    "Director of National Intelligence", "Secretary of State",
                    "White House Chief of Staff"
                }
            },
            new CountryProfile
            {
                id = "CHN", displayName = "China",
                military = 76, economy = 88, intelligence = 72, diplomacy = 68, government = 78,
                // Vulnerability: industrial weight resting on imported energy.
                treasury = 2400, manpower = 1800, energy = 38, industry = 92, materials = 68, food = 60,
                approval = 62, stability = 72, unity = 70,
                governmentType = GovernmentType.DominantPartyState, termLengthMonths = 0,
                mapX = 62, mapY = 8, mapCode = "CN",
                aggression = 55, caution = 62, opportunism = 72, patience = 78,
                traitIds = new[] { Core.NationalTraitCatalog.Industrial, Core.NationalTraitCatalog.Technocratic },
                navalAccess = NavalAccess.Maritime,
                firstNames = new[] { "WEI", "LI", "JIAN", "MEI", "HAO", "YAN", "FENG", "XIU" },
                lastNames = new[] { "ZHANG", "CHEN", "LIU", "HUANG", "ZHAO", "SUN", "XU", "GUO" },
                officeTitles = new[]
                {
                    "Minister of National Defense", "Minister of Finance",
                    "Minister of State Security", "Minister of Foreign Affairs",
                    "Premier"
                }
            },
            new CountryProfile
            {
                id = "RUS", displayName = "Russia",
                military = 72, economy = 46, intelligence = 78, diplomacy = 50, government = 62,
                treasury = 900, manpower = 700, energy = 96, industry = 56, materials = 86, food = 70,
                approval = 55, stability = 58, unity = 60,
                governmentType = GovernmentType.CentralizedRepublic, termLengthMonths = 0,
                mapX = 52, mapY = 4, mapCode = "RU",
                aggression = 74, caution = 38, opportunism = 70, patience = 52,
                traitIds = new[] { Core.NationalTraitCatalog.Besieged, Core.NationalTraitCatalog.ResourceState },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "DMITRI", "IRINA", "SERGEI", "NATALIA", "PAVEL", "OLGA", "MIKHAIL", "TATIANA" },
                lastNames = new[] { "VOLKOV", "SOKOLOVA", "ORLOV", "KUZNETSOV", "PETROVA", "MEDVEDEV", "ROMANOV", "ZHUKOV" },
                officeTitles = new[]
                {
                    "Minister of Defence", "Minister of Finance",
                    "Director of Foreign Intelligence", "Minister of Foreign Affairs",
                    "Prime Minister"
                }
            },
            new CountryProfile
            {
                id = "IND", displayName = "India",
                military = 64, economy = 68, intelligence = 58, diplomacy = 70, government = 64,
                treasury = 1100, manpower = 1700, energy = 38, industry = 62, materials = 48, food = 74,
                approval = 58, stability = 60, unity = 56,
                governmentType = GovernmentType.ParliamentaryRepublic, termLengthMonths = 60,
                mapX = 56, mapY = 11, mapCode = "IN",
                aggression = 42, caution = 66, opportunism = 55, patience = 74,
                traitIds = new[] { Core.NationalTraitCatalog.Fractious, Core.NationalTraitCatalog.Technocratic },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "ARJUN", "MEERA", "VIKRAM", "ANANYA", "RAVI", "KAVITA", "SANJAY", "DEEPA" },
                lastNames = new[] { "SHARMA", "IYER", "KAPOOR", "BANERJEE", "RAO", "SINGH", "PATEL", "MENON" },
                officeTitles = new[]
                {
                    "Minister of Defence", "Minister of Finance",
                    "Director of Intelligence", "Minister of External Affairs",
                    "Principal Secretary"
                }
            },
            new CountryProfile
            {
                // Industrial heavyweight with a standing energy import problem.
                id = "DEU", displayName = "Germany",
                military = 52, economy = 80, intelligence = 62, diplomacy = 78, government = 76,
                treasury = 1500, manpower = 420, energy = 30, industry = 86, materials = 34, food = 70,
                approval = 52, stability = 74, unity = 66,
                governmentType = GovernmentType.ParliamentaryRepublic, termLengthMonths = 48,
                mapX = 37, mapY = 5, mapCode = "DE",
                aggression = 28, caution = 72, opportunism = 40, patience = 68,
                traitIds = new[] { Core.NationalTraitCatalog.Industrial, Core.NationalTraitCatalog.Mercantile },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "LUKAS", "ANNIKA", "MARKUS", "GRETA", "STEFAN", "HELENA", "JONAS", "KATRIN" },
                lastNames = new[] { "RICHTER", "BAUMANN", "KELLER", "VOGEL", "SCHREIBER", "HOFFMAN", "WERNER", "LANG" },
                officeTitles = new[]
                {
                    "Minister of Defence", "Minister of Finance",
                    "President of the Federal Intelligence Service", "Foreign Minister",
                    "Chancellery Chief of Staff"
                }
            },

            // ---------------- regional powers ----------------
            new CountryProfile
            {
                // Technological and maritime, structurally short of energy and people.
                id = "JPN", displayName = "Japan",
                military = 56, economy = 78, intelligence = 64, diplomacy = 70, government = 74,
                treasury = 1400, manpower = 380, energy = 22, industry = 84, materials = 26, food = 44,
                approval = 50, stability = 78, unity = 72,
                governmentType = GovernmentType.ParliamentaryRepublic, termLengthMonths = 48,
                mapX = 71, mapY = 7, mapCode = "JP",
                aggression = 34, caution = 70, opportunism = 46, patience = 72,
                traitIds = new[] { Core.NationalTraitCatalog.Maritime, Core.NationalTraitCatalog.Technocratic },
                navalAccess = NavalAccess.Maritime,
                firstNames = new[] { "HARUKI", "AIKO", "KENJI", "YUMI", "SATOSHI", "NORIKO", "TAKUMI", "REI" },
                lastNames = new[] { "TANAKA", "MORIMOTO", "ISHIKAWA", "KOBAYASHI", "YAMADA", "OKADA", "SAITO", "FUJIWARA" },
                officeTitles = new[]
                {
                    "Minister of Defense", "Minister of Finance",
                    "Director of Cabinet Intelligence", "Minister for Foreign Affairs",
                    "Chief Cabinet Secretary"
                }
            },
            new CountryProfile
            {
                // Agricultural and resource depth, thin institutions and treasury.
                id = "BRA", displayName = "Brazil",
                military = 48, economy = 60, intelligence = 44, diplomacy = 66, government = 54,
                treasury = 800, manpower = 900, energy = 72, industry = 56, materials = 80, food = 94,
                approval = 46, stability = 52, unity = 58,
                governmentType = GovernmentType.PresidentialRepublic, termLengthMonths = 48,
                mapX = 23, mapY = 15, mapCode = "BR",
                aggression = 32, caution = 58, opportunism = 52, patience = 56,
                traitIds = new[] { Core.NationalTraitCatalog.ResourceState, Core.NationalTraitCatalog.Fractious },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "RAFAEL", "LUANA", "TIAGO", "CAMILA", "BRUNO", "ISABELA", "MATEUS", "LARISSA" },
                lastNames = new[] { "ALMEIDA", "CARVALHO", "RIBEIRO", "MENDES", "BARBOSA", "TEIXEIRA", "MOREIRA", "AZEVEDO" },
                officeTitles = new[]
                {
                    "Minister of Defence", "Minister of Economy",
                    "Director of Intelligence", "Minister of Foreign Affairs",
                    "Chief of Staff of the Presidency"
                }
            },
            new CountryProfile
            {
                // A bridge between blocs: leverage from position rather than mass.
                id = "TUR", displayName = "Türkiye",
                military = 58, economy = 52, intelligence = 60, diplomacy = 62, government = 56,
                treasury = 600, manpower = 620, energy = 26, industry = 58, materials = 40, food = 76,
                approval = 50, stability = 50, unity = 48,
                governmentType = GovernmentType.PresidentialRepublic, termLengthMonths = 60,
                mapX = 45, mapY = 8, mapCode = "TR",
                aggression = 66, caution = 44, opportunism = 74, patience = 46,
                traitIds = new[] { Core.NationalTraitCatalog.Besieged, Core.NationalTraitCatalog.Convening },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "EMRE", "SELIN", "BURAK", "ZEYNEP", "KEREM", "ESRA", "MURAT", "DILEK" },
                lastNames = new[] { "YILMAZ", "DEMIR", "KAYA", "AYDIN", "ARSLAN", "DOGAN", "KILIC", "OZTURK" },
                officeTitles = new[]
                {
                    "Minister of National Defence", "Minister of Treasury and Finance",
                    "Director of the National Intelligence Organization", "Minister of Foreign Affairs",
                    "Head of Administrative Affairs"
                }
            },
            new CountryProfile
            {
                // Populous energy exporter carrying real internal fragility.
                id = "NGA", displayName = "Nigeria",
                military = 38, economy = 40, intelligence = 34, diplomacy = 50, government = 38,
                treasury = 400, manpower = 1100, energy = 82, industry = 32, materials = 56, food = 54,
                approval = 42, stability = 36, unity = 34,
                governmentType = GovernmentType.PresidentialRepublic, termLengthMonths = 48,
                mapX = 36, mapY = 12, mapCode = "NG",
                aggression = 38, caution = 50, opportunism = 60, patience = 42,
                traitIds = new[] { Core.NationalTraitCatalog.ResourceState, Core.NationalTraitCatalog.Fractious },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "CHIDI", "AMARA", "TUNDE", "NGOZI", "EMEKA", "FUNMI", "OBI", "ZAINAB" },
                lastNames = new[] { "ADEYEMI", "OKONKWO", "BALOGUN", "ELUEMUNO", "ADEBAYO", "NWOSU", "OYELARAN", "IBRAHIM" },
                officeTitles = new[]
                {
                    "Minister of Defence", "Minister of Finance",
                    "Director-General of National Intelligence", "Minister of Foreign Affairs",
                    "Secretary to the Government"
                }
            },

            // ---------------- resource powers ----------------
            new CountryProfile
            {
                // Energy superpower that cannot feed itself.
                id = "SAU", displayName = "Saudi Arabia",
                military = 46, economy = 58, intelligence = 48, diplomacy = 58, government = 60,
                treasury = 1900, manpower = 300, energy = 100, industry = 34, materials = 44, food = 18,
                approval = 56, stability = 62, unity = 64,
                governmentType = GovernmentType.Monarchy, termLengthMonths = 0,
                mapX = 47, mapY = 10, mapCode = "SA",
                aggression = 48, caution = 56, opportunism = 66, patience = 62,
                traitIds = new[] { Core.NationalTraitCatalog.ResourceState, Core.NationalTraitCatalog.Opaque },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "FAISAL", "NOURA", "KHALID", "LAYLA", "SAUD", "HESSA", "TURKI", "MAHA" },
                lastNames = new[] { "AL-RASHID", "AL-QAHTANI", "AL-HARBI", "AL-MUTAIRI", "AL-DOSARI", "AL-SHAMMARI", "AL-OTAIBI", "AL-GHAMDI" },
                officeTitles = new[]
                {
                    "Minister of Defence", "Minister of Finance",
                    "President of State Security", "Minister of Foreign Affairs",
                    "Chief of the Royal Court"
                }
            },
            new CountryProfile
            {
                // Materials and food surplus, tiny population, long sea lines.
                id = "AUS", displayName = "Australia",
                military = 42, economy = 62, intelligence = 58, diplomacy = 64, government = 74,
                treasury = 900, manpower = 220, energy = 78, industry = 44, materials = 92, food = 90,
                approval = 52, stability = 76, unity = 68,
                governmentType = GovernmentType.ParliamentaryRepublic, termLengthMonths = 36,
                mapX = 68, mapY = 17, mapCode = "AU",
                aggression = 26, caution = 64, opportunism = 44, patience = 66,
                traitIds = new[] { Core.NationalTraitCatalog.Maritime, Core.NationalTraitCatalog.ResourceState },
                navalAccess = NavalAccess.Maritime,
                firstNames = new[] { "LIAM", "CHARLOTTE", "ANGUS", "MADDISON", "CALLUM", "TESS", "DECLAN", "HARRIET" },
                lastNames = new[] { "WHITLOCK", "MCKENNA", "DAWSON", "FLETCHER", "HAYES", "PRESCOTT", "BRENNAN", "GALLAGHER" },
                officeTitles = new[]
                {
                    "Minister for Defence", "Treasurer",
                    "Director-General of Security", "Minister for Foreign Affairs",
                    "Secretary of the Department of the Prime Minister"
                }
            },

            // ---------------- industrial mid-tiers ----------------
            new CountryProfile
            {
                // Advanced industry in a dangerous neighbourhood, no energy of its own.
                id = "KOR", displayName = "South Korea",
                military = 60, economy = 74, intelligence = 60, diplomacy = 58, government = 68,
                treasury = 1000, manpower = 400, energy = 18, industry = 88, materials = 24, food = 38,
                approval = 46, stability = 66, unity = 60,
                governmentType = GovernmentType.PresidentialRepublic, termLengthMonths = 60,
                mapX = 66, mapY = 6, mapCode = "KR",
                aggression = 44, caution = 68, opportunism = 50, patience = 70,
                traitIds = new[] { Core.NationalTraitCatalog.Industrial, Core.NationalTraitCatalog.Besieged },
                navalAccess = NavalAccess.Maritime,
                firstNames = new[] { "JIHOON", "SEOYEON", "MINJUN", "HAEUN", "DONGHYUN", "YUNA", "SEUNGWOO", "JIWOO" },
                lastNames = new[] { "PARK", "KIM", "LEE", "CHOI", "JUNG", "KANG", "YOON", "LIM" },
                officeTitles = new[]
                {
                    "Minister of National Defense", "Minister of Economy and Finance",
                    "Director of the National Intelligence Service", "Minister of Foreign Affairs",
                    "Presidential Chief of Staff"
                }
            },
            new CountryProfile
            {
                // Manufacturing tied tightly to one neighbour's market.
                id = "MEX", displayName = "Mexico",
                military = 36, economy = 58, intelligence = 40, diplomacy = 56, government = 48,
                treasury = 600, manpower = 700, energy = 54, industry = 66, materials = 50, food = 62,
                approval = 50, stability = 44, unity = 52,
                governmentType = GovernmentType.PresidentialRepublic, termLengthMonths = 72,
                mapX = 15, mapY = 10, mapCode = "MX",
                aggression = 30, caution = 60, opportunism = 54, patience = 50,
                traitIds = new[] { Core.NationalTraitCatalog.Mercantile, Core.NationalTraitCatalog.Fractious },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "SANTIAGO", "XIMENA", "MATEO", "REGINA", "DIEGO", "VALERIA", "EMILIANO", "PAOLA" },
                lastNames = new[] { "HERRERA", "VARGAS", "CASTILLO", "NAVARRO", "SALAZAR", "CAMPOS", "DELGADO", "ESQUIVEL" },
                officeTitles = new[]
                {
                    "Secretary of National Defense", "Secretary of Finance",
                    "Director of the National Intelligence Centre", "Secretary of Foreign Affairs",
                    "Chief of the Office of the Presidency"
                }
            },

            // ---------------- maritime / archipelago ----------------
            new CountryProfile
            {
                // Sits astride the sea lanes everyone else depends on.
                id = "IDN", displayName = "Indonesia",
                military = 40, economy = 54, intelligence = 42, diplomacy = 60, government = 52,
                treasury = 550, manpower = 1000, energy = 62, industry = 48, materials = 66, food = 64,
                approval = 54, stability = 54, unity = 46,
                governmentType = GovernmentType.PresidentialRepublic, termLengthMonths = 60,
                mapX = 65, mapY = 14, mapCode = "ID",
                aggression = 36, caution = 58, opportunism = 56, patience = 60,
                traitIds = new[] { Core.NationalTraitCatalog.Maritime, Core.NationalTraitCatalog.Mercantile },
                navalAccess = NavalAccess.Maritime,
                firstNames = new[] { "BUDI", "SRI", "AGUS", "DEWI", "RIZKI", "INDAH", "BAMBANG", "PUTRI" },
                lastNames = new[] { "WIJAYA", "SANTOSO", "HARTONO", "PURNOMO", "SUSILO", "KUSUMA", "HALIM", "PRASETYO" },
                officeTitles = new[]
                {
                    "Minister of Defence", "Minister of Finance",
                    "Head of the State Intelligence Agency", "Minister of Foreign Affairs",
                    "Cabinet Secretary"
                }
            },

            // ---------------- buffer states ----------------
            new CountryProfile
            {
                // Frontline industrial buffer: exposed, and knows it.
                id = "POL", displayName = "Poland",
                military = 50, economy = 56, intelligence = 48, diplomacy = 54, government = 62,
                treasury = 500, manpower = 380, energy = 42, industry = 64, materials = 38, food = 72,
                approval = 48, stability = 60, unity = 62,
                governmentType = GovernmentType.ParliamentaryRepublic, termLengthMonths = 48,
                mapX = 41, mapY = 5, mapCode = "PL",
                aggression = 52, caution = 54, opportunism = 48, patience = 58,
                traitIds = new[] { Core.NationalTraitCatalog.Besieged, Core.NationalTraitCatalog.Martial },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "PIOTR", "AGNIESZKA", "TOMASZ", "MAGDALENA", "JAKUB", "EWA", "MARCIN", "ZOFIA" },
                lastNames = new[] { "KOWALCZYK", "NOWAK", "WOJCIK", "LEWANDOWSKI", "ZIELINSKI", "KAMINSKA", "MAZUR", "DABROWSKI" },
                officeTitles = new[]
                {
                    "Minister of National Defence", "Minister of Finance",
                    "Head of the Foreign Intelligence Agency", "Minister of Foreign Affairs",
                    "Head of the Chancellery"
                }
            },
            new CountryProfile
            {
                // Landlocked resource buffer between two larger neighbours.
                id = "KAZ", displayName = "Kazakhstan",
                military = 30, economy = 42, intelligence = 38, diplomacy = 48, government = 50,
                treasury = 450, manpower = 260, energy = 88, industry = 36, materials = 84, food = 66,
                approval = 52, stability = 54, unity = 50,
                governmentType = GovernmentType.CentralizedRepublic, termLengthMonths = 0,
                mapX = 51, mapY = 7, mapCode = "KZ",
                aggression = 34, caution = 70, opportunism = 58, patience = 64,
                traitIds = new[] { Core.NationalTraitCatalog.ResourceState, Core.NationalTraitCatalog.Opaque },
                navalAccess = NavalAccess.Landlocked,
                firstNames = new[] { "ASKAR", "AIGUL", "NURLAN", "DINARA", "YERLAN", "SAULE", "TIMUR", "ZARINA" },
                lastNames = new[] { "ABENOV", "SULEIMENOV", "ISKAKOV", "BEKTUROV", "OMAROVA", "TULEGENOV", "SAGYNDYK", "AMANZHOL" },
                officeTitles = new[]
                {
                    "Minister of Defence", "Minister of Finance",
                    "Chairman of the National Security Committee", "Minister of Foreign Affairs",
                    "Head of the Presidential Administration"
                }
            },

            // ---------------- expansion roster (Full world) ----------------
            //
            // Authored strictly at the tail: CreateWorld skips excluded profiles
            // without consuming a draw, so the Standard world stays bit-identical
            // to the one every balance figure was measured on.
            new CountryProfile
            {
                // Global intelligence and naval reach on a mid-sized base;
                // vulnerability: imports much of what it eats and uses.
                id = "GBR", displayName = "United Kingdom",
                military = 58, economy = 70, intelligence = 78, diplomacy = 74, government = 70,
                treasury = 1300, manpower = 340, energy = 46, industry = 58, materials = 30, food = 52,
                approval = 44, stability = 66, unity = 52,
                governmentType = GovernmentType.ParliamentaryRepublic, termLengthMonths = 60,
                mapX = 34, mapY = 4, mapCode = "GB",
                aggression = 46, caution = 58, opportunism = 60, patience = 60,
                traitIds = new[] { Core.NationalTraitCatalog.Maritime, Core.NationalTraitCatalog.Convening },
                navalAccess = NavalAccess.Maritime,
                firstNames = new[] { "OLIVER", "FIONA", "HARRY", "IMOGEN", "ALISTAIR", "GRACE", "EDWARD", "SIOBHAN" },
                lastNames = new[] { "PEMBERTON", "MACLEOD", "HARGREAVES", "ASHWORTH", "CAVENDISH", "OSEI", "THORNE", "GRIFFITHS" },
                officeTitles = new[]
                {
                    "Secretary of State for Defence", "Chancellor of the Exchequer",
                    "Chief of the Secret Intelligence Service", "Foreign Secretary",
                    "Cabinet Secretary"
                }
            },
            new CountryProfile
            {
                // Diplomatic weight and independent energy; vulnerability: a
                // restive public and thin strategic materials.
                id = "FRA", displayName = "France",
                military = 60, economy = 68, intelligence = 66, diplomacy = 76, government = 64,
                treasury = 1200, manpower = 380, energy = 64, industry = 62, materials = 32, food = 80,
                approval = 40, stability = 58, unity = 48,
                governmentType = GovernmentType.PresidentialRepublic, termLengthMonths = 60,
                mapX = 35, mapY = 6, mapCode = "FR",
                aggression = 52, caution = 48, opportunism = 62, patience = 55,
                traitIds = new[] { Core.NationalTraitCatalog.Convening, Core.NationalTraitCatalog.Fractious },
                navalAccess = NavalAccess.Maritime,
                firstNames = new[] { "ANTOINE", "CAMILLE", "OLIVIER", "MARGAUX", "PASCAL", "ELODIE", "THIERRY", "AMELIE" },
                lastNames = new[] { "MOREAU", "LEFEVRE", "GARNIER", "ROUSSEAU", "DUBOIS", "MARCHAND", "BERTRAND", "CHEVALIER" },
                officeTitles = new[]
                {
                    "Minister of the Armed Forces", "Minister of the Economy",
                    "Director-General for External Security", "Minister for Europe and Foreign Affairs",
                    "Secretary-General of the Élysée"
                }
            },
            new CountryProfile
            {
                // Industrial north on imported energy, governments that do not
                // last: institutional churn is the authored character.
                id = "ITA", displayName = "Italy",
                military = 44, economy = 60, intelligence = 52, diplomacy = 64, government = 48,
                treasury = 800, manpower = 340, energy = 24, industry = 66, materials = 28, food = 74,
                approval = 42, stability = 52, unity = 50,
                governmentType = GovernmentType.ParliamentaryRepublic, termLengthMonths = 36,
                mapX = 38, mapY = 7, mapCode = "IT",
                aggression = 30, caution = 56, opportunism = 58, patience = 48,
                traitIds = new[] { Core.NationalTraitCatalog.Mercantile, Core.NationalTraitCatalog.Fractious },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "MARCO", "GIULIA", "ALESSANDRO", "CHIARA", "LORENZO", "FRANCESCA", "MATTEO", "SILVIA" },
                lastNames = new[] { "MORETTI", "CONTI", "RICCI", "MARINO", "GRECO", "LOMBARDI", "BARBIERI", "FONTANA" },
                officeTitles = new[]
                {
                    "Minister of Defence", "Minister of Economy and Finance",
                    "Director of the Security Intelligence Department", "Minister of Foreign Affairs",
                    "Secretary of the Council of Ministers"
                }
            },
            new CountryProfile
            {
                // Resource depth across every column and a stable state on top;
                // vulnerability: a small population and one overwhelming market.
                id = "CAN", displayName = "Canada",
                military = 38, economy = 62, intelligence = 54, diplomacy = 66, government = 76,
                treasury = 850, manpower = 200, energy = 90, industry = 48, materials = 88, food = 92,
                approval = 54, stability = 78, unity = 60,
                governmentType = GovernmentType.ParliamentaryRepublic, termLengthMonths = 48,
                mapX = 14, mapY = 3, mapCode = "CA",
                aggression = 20, caution = 70, opportunism = 40, patience = 70,
                traitIds = new[] { Core.NationalTraitCatalog.ResourceState, Core.NationalTraitCatalog.Mercantile },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "NOAH", "AVERY", "GRAHAM", "CHLOE", "DUNCAN", "MARIE", "ETIENNE", "HEATHER" },
                lastNames = new[] { "MACDONALD", "TREMBLAY", "SINCLAIR", "LAVOIE", "CARMICHAEL", "BOUCHARD", "REDDICK", "FRASER" },
                officeTitles = new[]
                {
                    "Minister of National Defence", "Minister of Finance",
                    "Director of the Security Intelligence Service", "Minister of Foreign Affairs",
                    "Clerk of the Privy Council"
                }
            },
            new CountryProfile
            {
                // An army-anchored state astride the world's shortest sea route;
                // vulnerability: it cannot feed itself and knows it.
                id = "EGY", displayName = "Egypt",
                military = 52, economy = 38, intelligence = 56, diplomacy = 58, government = 46,
                treasury = 350, manpower = 800, energy = 55, industry = 40, materials = 36, food = 30,
                approval = 46, stability = 44, unity = 56,
                governmentType = GovernmentType.CentralizedRepublic, termLengthMonths = 0,
                mapX = 43, mapY = 10, mapCode = "EG",
                aggression = 50, caution = 52, opportunism = 62, patience = 50,
                traitIds = new[] { Core.NationalTraitCatalog.Martial, Core.NationalTraitCatalog.Besieged },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "AHMED", "MONA", "KARIM", "SALMA", "TAREK", "HODA", "MAHMOUD", "NADIA" },
                lastNames = new[] { "EL-SAYED", "MANSOUR", "ABDEL-AZIZ", "FAHMY", "SHALABY", "GHANEM", "EL-MASRY", "HASSANEIN" },
                officeTitles = new[]
                {
                    "Minister of Defence", "Minister of Finance",
                    "Director of General Intelligence", "Minister of Foreign Affairs",
                    "Chief of the Presidential Office"
                }
            },
            new CountryProfile
            {
                // A mineral vault with a failing grid: materials wealth the
                // state struggles to keep the lights on above.
                id = "ZAF", displayName = "South Africa",
                military = 32, economy = 44, intelligence = 40, diplomacy = 58, government = 48,
                treasury = 380, manpower = 480, energy = 40, industry = 44, materials = 90, food = 70,
                approval = 38, stability = 42, unity = 44,
                governmentType = GovernmentType.ParliamentaryRepublic, termLengthMonths = 60,
                mapX = 40, mapY = 17, mapCode = "ZA",
                aggression = 24, caution = 60, opportunism = 50, patience = 58,
                traitIds = new[] { Core.NationalTraitCatalog.ResourceState, Core.NationalTraitCatalog.Fractious },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "SIPHO", "THANDIWE", "PIETER", "NALEDI", "JOHAN", "ZANELE", "KAGISO", "ANNELIE" },
                lastNames = new[] { "NKOSI", "VAN DER MERWE", "DLAMINI", "BOTHA", "MOKOENA", "PRETORIUS", "KHUMALO", "NAIDOO" },
                officeTitles = new[]
                {
                    "Minister of Defence", "Minister of Finance",
                    "Director-General of the State Security Agency", "Minister of International Relations",
                    "Director-General in the Presidency"
                }
            },
            new CountryProfile
            {
                // A breadbasket that cannot keep its own books: enormous food
                // surplus over a chronically unstable economy.
                id = "ARG", displayName = "Argentina",
                military = 30, economy = 42, intelligence = 36, diplomacy = 52, government = 44,
                treasury = 250, manpower = 380, energy = 58, industry = 42, materials = 60, food = 96,
                approval = 40, stability = 46, unity = 52,
                governmentType = GovernmentType.PresidentialRepublic, termLengthMonths = 48,
                mapX = 22, mapY = 18, mapCode = "AR",
                aggression = 26, caution = 54, opportunism = 58, patience = 44,
                traitIds = new[] { Core.NationalTraitCatalog.ResourceState, Core.NationalTraitCatalog.Fractious },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "JOAQUIN", "VALENTINA", "NICOLAS", "MILAGROS", "FACUNDO", "SOFIA", "GONZALO", "CATALINA" },
                lastNames = new[] { "FERNANDEZ", "AGUIRRE", "MOLINA", "CABRERA", "SOSA", "VILLALBA", "QUIROGA", "LEDESMA" },
                officeTitles = new[]
                {
                    "Minister of Defence", "Minister of Economy",
                    "Director of the Federal Intelligence Agency", "Minister of Foreign Affairs",
                    "Chief of the Cabinet of Ministers"
                }
            },
            new CountryProfile
            {
                // A disciplined rising manufacturer living beside a giant:
                // patient, wary, and building.
                id = "VNM", displayName = "Vietnam",
                military = 46, economy = 52, intelligence = 44, diplomacy = 54, government = 62,
                treasury = 400, manpower = 850, energy = 48, industry = 70, materials = 38, food = 82,
                approval = 58, stability = 68, unity = 66,
                governmentType = GovernmentType.DominantPartyState, termLengthMonths = 0,
                mapX = 63, mapY = 11, mapCode = "VN",
                aggression = 38, caution = 66, opportunism = 56, patience = 76,
                traitIds = new[] { Core.NationalTraitCatalog.Industrial, Core.NationalTraitCatalog.Besieged },
                navalAccess = NavalAccess.Coastal,
                firstNames = new[] { "MINH", "LINH", "DUC", "HUONG", "QUANG", "THAO", "BAO", "NGOC" },
                lastNames = new[] { "NGUYEN", "TRAN", "PHAM", "HOANG", "VU", "DANG", "BUI", "DO" },
                officeTitles = new[]
                {
                    "Minister of National Defence", "Minister of Finance",
                    "Head of the General Intelligence Department", "Minister of Foreign Affairs",
                    "Chairman of the Office of the Government"
                }
            }
        };

        public static GameState CreateDebugWorld(int seed) => CreateWorld(seed, PlayerCountryId);

        /// <summary>Build a world with the player posted to any authored nation.</summary>
        public static GameState CreateWorld(int seed, string playerCountryId,
            WorldSize size = WorldSize.Standard)
        {
            var included = new HashSet<string>(RosterFor(size));

            // A posting outside the chosen world falls back to the default post,
            // which every roster contains — same shape as the unknown-id fallback.
            bool validPosting = FindProfile(playerCountryId) != null && included.Contains(playerCountryId);

            var rng = new Random(seed);
            var state = new GameState
            {
                rngSeed = seed,
                worldSize = size,
                playerCountryId = validPosting ? playerCountryId : PlayerCountryId
            };

            // Excluded profiles are skipped without consuming a single random
            // draw, and everything new is authored at the tail of the profile
            // array, the location list, the trade network and the posture table.
            // Together those two rules make the Standard world **bit-identical**
            // to the world before Regional/Full existed — the measured balance
            // figures still describe the game the default builds.
            foreach (var profile in Profiles)
            {
                if (!included.Contains(profile.id)) continue;
                state.countries.Add(MakeCountry(rng, profile, profile.id == state.playerCountryId));
            }

            MakeGovernments(rng, state);
            MakeCabinets(rng, state);

            // MakeMap is told the roster rather than pruned afterwards: every
            // authored location draws a garrison roll from the shared stream, so
            // an add-then-prune would consume draws for ground that does not
            // exist and silently regenerate everything MakeEconomies rolls after
            // it — the Kazakhstan-fleet lesson, one system further down.
            MakeMap(rng, state, included);
            MakeEconomies(rng, state);
            MakeTradeNetwork(state);

            // The trade network is authored for the full world; a smaller one
            // keeps only the links both ends of exist (no rng involved). A
            // hosted base whose operator is absent is simply national ground.
            state.trade.RemoveAll(link =>
                !included.Contains(link.countryA) || !included.Contains(link.countryB));
            foreach (var location in state.locations)
                if (!string.IsNullOrEmpty(location.foreignOperatorId)
                    && !included.Contains(location.foreignOperatorId))
                    location.foreignOperatorId = string.Empty;

            Core.DiplomacySystem.SeedRelationships(state);
            SeedDiplomaticPosture(state);
            Core.AISystem.SeedAI(state, rng);

            // A world built by this build is already at this build's schema.
            // Leaving it at the field default made every freshly created world
            // claim to be from the oldest version, so a live state and a
            // round-tripped one disagreed on their own version.
            state.saveVersion = Core.SaveSystem.CurrentSaveVersion;

            state.commandPoints.BeginMonth();
            state.influence = GameState.InfluencePerMonth;
            Core.ProgressionSystem.CaptureYearSnapshot(state);

            // The decade before the operator arrived. Written last, after
            // relationships exist, and deliberately **after** every random draw
            // is spent: it consumes none, so the Standard world stays
            // bit-identical to the one every balance figure was measured on.
            Core.HistoryCatalog.Seed(state);

            state.AddChronicle(ChronicleCategory.System, null,
                "Command terminal initialized. Operator access granted.");
            state.AddChronicle(ChronicleCategory.Political, state.playerCountryId,
                "Cabinet appointed. Five pillar leaders sworn in.");
            // After the history, so the chronicle reads forward in time.
            Core.MandateSystem.Assign(state);
            return state;
        }

        public static CountryProfile FindProfile(string id)
        {
            foreach (var profile in Profiles)
                if (profile.id == id) return profile;
            return null;
        }

        /// <summary>
        /// How much of the military score a country can put to sea.
        ///
        /// Landlocked is exactly zero, not merely small: a fleet you cannot
        /// float is not a weak fleet, and every naval verb checks for one.
        /// </summary>
        /// <summary>
        /// Resolve authored trait ids against the catalogue. An id that does not
        /// resolve is skipped rather than producing an empty trait object, so a
        /// typo shows up as a country missing its character instead of carrying
        /// a nameless one.
        /// </summary>
        public static List<NationalTrait> ResolveTraits(string[] ids)
        {
            var traits = new List<NationalTrait>();
            if (ids == null) return traits;

            foreach (var id in ids)
            {
                var trait = Core.NationalTraitCatalog.Find(id);
                if (trait != null) traits.Add(trait);
            }
            return traits;
        }

        public static float NavalScaleFor(NavalAccess access)
        {
            switch (access)
            {
                case NavalAccess.Landlocked: return 0f;
                case NavalAccess.Maritime: return 0.95f;
                default: return 0.55f;
            }
        }

        static CountryState MakeCountry(Random rng, CountryProfile profile, bool isPlayer)
        {
            // Small authored jitter so no two saves open identically.
            float Jitter(float baseValue, float spread)
                => Clamp((float)Math.Round(baseValue + (rng.NextDouble() * 2.0 - 1.0) * spread, 1));

            // Drawn unconditionally and then zeroed, rather than skipped, so that
            // a landlocked country does not shift the random stream for everything
            // created after it. Skipping the draw silently regenerated the entire
            // world downstream of Kazakhstan and moved measured balance figures
            // that had nothing to do with navies.
            float navalRoll = Jitter(profile.military * NavalScaleFor(profile.navalAccess), 6f);
            if (profile.navalAccess == NavalAccess.Landlocked) navalRoll = 0f;

            var built = BuildCountry(rng, profile, isPlayer, Jitter, navalRoll);

            // Fill each branch's inventory to the strength it was authored with,
            // so a country opens the save holding real aircraft and hulls rather
            // than an abstract number that later has to invent them.
            foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
            {
                var force = built.military.Get(branch);
                Core.AssetCatalog.FillToStrength(force.inventory, branch, force.strength);
                force.SyncStrength();
            }
            return built;
        }

        static CountryState BuildCountry(Random rng, CountryProfile profile, bool isPlayer,
            Func<float, float, float> Jitter, float navalRoll)
        {
            return new CountryState
            {
                id = profile.id,
                displayName = profile.displayName,
                isPlayer = isPlayer,
                pillars = new PillarScores
                {
                    military = Jitter(profile.military, 4f),
                    economy = Jitter(profile.economy, 4f),
                    intelligence = Jitter(profile.intelligence, 4f),
                    diplomacy = Jitter(profile.diplomacy, 4f),
                    government = Jitter(profile.government, 4f)
                },
                resources = new NationalResources
                {
                    treasury = (float)Math.Round(profile.treasury * (0.92 + rng.NextDouble() * 0.16), 0),
                    manpower = (float)Math.Round(profile.manpower * (0.92 + rng.NextDouble() * 0.16), 0),
                    manpowerBaseline = (float)Math.Round(profile.manpower, 0),
                    energy = Jitter(profile.energy, 5f),
                    industrialCapacity = Jitter(profile.industry, 5f),
                    strategicMaterials = Jitter(profile.materials, 5f),
                    foodSecurity = Jitter(profile.food, 5f),

                    // The authored position each country returns to. Keeping these
                    // separate is what stops every state drifting to a uniform
                    // resource profile over a long save.
                    energyEndowment = profile.energy,
                    materialsEndowment = profile.materials,
                    foodEndowment = profile.food
                },
                military = new MilitaryState
                {
                    ground = new BranchForce
                    {
                        branch = ForceBranch.Ground,
                        strength = Jitter(profile.military * 0.95f, 5f),
                        experience = Jitter(22f + profile.government * 0.18f, 5f),
                        readiness = Jitter(55f + profile.government * 0.15f, 6f),
                        supply = Jitter(60f + profile.industry * 0.25f, 6f)
                    },
                    air = new BranchForce
                    {
                        branch = ForceBranch.Air,
                        strength = Jitter(profile.military * 0.9f, 5f),
                        experience = Jitter(22f + profile.government * 0.18f, 5f),
                        readiness = Jitter(55f + profile.government * 0.15f, 6f),
                        supply = Jitter(60f + profile.industry * 0.25f, 6f)
                    },
                    // Naval strength follows the coastline, not the defence
                    // budget. A flat multiple of the military score gave a
                    // landlocked state the same fleet-per-point as an
                    // archipelago, which made "they have no navy" unsayable and
                    // every naval verb universally available.
                    naval = new BranchForce
                    {
                        branch = ForceBranch.Naval,
                        strength = navalRoll,
                        experience = Jitter(20f + profile.government * 0.18f, 5f),
                        readiness = Jitter(50f + profile.government * 0.15f, 6f),
                        supply = Jitter(58f + profile.industry * 0.25f, 6f)
                    }
                },
                counterIntel = new CounterIntelState
                {
                    // An opaque state is harder to collect against from the day
                    // the save opens, not after it has invested in becoming so.
                    counterIntelligence = Jitter(25f + profile.intelligence * 0.45f, 4f)
                                          + (Array.IndexOf(profile.traitIds,
                                              Core.NationalTraitCatalog.Opaque) >= 0 ? 10f : 0f)
                },
                governmentApproval = Jitter(profile.approval, 5f),
                stability = Jitter(profile.stability, 5f),
                nationalUnity = Jitter(profile.unity, 5f),

                // Seeded from the economy that would have produced them, so a
                // wealthy state does not open its save living like a poor one
                // and then spend three years drifting to where it should be.
                livingStandards = Jitter(38f + profile.economy * 0.32f, 4f),
                socialUnrest = Jitter(Math.Max(0f, 34f - profile.stability * 0.35f), 3f),

                // Authored identity, resolved from the catalogue so a typo in a
                // profile is a missing trait rather than a silent blank one — a
                // test asserts every authored id resolves.
                traits = ResolveTraits(profile.traitIds),
                warSupport = Jitter(45f, 6f)
            };
        }

        /// <summary>Seat the initial leadership and schedule the first elections (GDD §13).</summary>
        static void MakeGovernments(Random rng, GameState state)
        {
            var priorities = (NationalPriority[])Enum.GetValues(typeof(NationalPriority));

            foreach (var country in state.countries)
            {
                var profile = FindProfile(country.id);
                var gov = country.government;
                gov.type = profile.governmentType;
                gov.termLengthMonths = profile.termLengthMonths > 0 ? profile.termLengthMonths : 48;

                gov.leader = new Leader
                {
                    name = $"{profile.firstNames[rng.Next(profile.firstNames.Length)]} " +
                           $"{profile.lastNames[rng.Next(profile.lastNames.Length)]}",
                    faction = gov.IsElective ? "GOVERNING PARTY" : "PARTY LEADERSHIP",
                    priority = priorities[rng.Next(priorities.Length)],
                    competence = (float)Math.Round(45f + rng.NextDouble() * 40f, 1),
                    age = (float)Math.Round(50f + rng.NextDouble() * 22f, 1),
                    monthsInOffice = rng.Next(0, 24)
                };

                // Presidential republics limit consecutive terms; parliamentary
                // systems do not. Non-elective systems have no scheduled contest.
                gov.consecutiveTermLimit = gov.type == GovernmentType.PresidentialRepublic ? 2 : 0;

                gov.legislativeSupport = (float)Math.Round(48f + rng.NextDouble() * 22f, 1);
                gov.eliteCohesion = (float)Math.Round(55f + rng.NextDouble() * 25f, 1);

                // Stagger the first election so the slice does not open on one.
                var electionDate = state.startDate;
                int monthsOut = gov.IsElective
                    ? Math.Max(6, gov.termLengthMonths - gov.leader.monthsInOffice)
                    : 0;
                for (int i = 0; i < monthsOut; i++) electionDate = electionDate.NextMonth();
                gov.nextElectionDate = electionDate;
            }
        }

        /// <summary>
        /// Appoint a cabinet for **every** country (GDD §8).
        ///
        /// Foreign governments used to have none, so their capability grew from
        /// a bespoke AI routine with no people behind it. Now the same five
        /// offices run every state: a rival's economy is run by someone who is
        /// either good at it or not, that fact is discoverable by collection,
        /// and a coup that wrecks their cabinet actually degrades them.
        ///
        /// Names come from each country's own pools, so a foreign cabinet reads
        /// as belonging to that nation rather than to the player's.
        /// </summary>
        static void MakeCabinets(Random rng, GameState state)
        {
            foreach (var country in state.countries)
                MakeCabinetFor(rng, country, FindProfile(country.id));
        }

        /// <summary>
        /// Appoint a cabinet for one country. Public so `SaveMigration` can fill
        /// in the foreign cabinets that saves written before v2 do not have.
        /// </summary>
        /// <summary>
        /// Staff a cabinet for a country created after world generation.
        ///
        /// `nameSourceId` supplies the name pool and office titles for a state
        /// that has no authored profile of its own — a secession successor is
        /// staffed by the parent's people, because that is who lives there. It
        /// used to fall through the `profile == null` guard below and be handed an
        /// empty cabinet: a sovereign state with nobody governing it, which no
        /// caller checked and nothing reported.
        /// </summary>
        public static void AppointCabinet(Random rng, CountryState country, string nameSourceId = null)
        {
            var profile = FindProfile(country.id)
                          ?? (nameSourceId != null ? FindProfile(nameSourceId) : null);

            if (profile == null)
            {
                // Loud, because the silent return is exactly how a state ended up
                // ungoverned. A cabinet is not optional furniture.
                Brink.Core.GameLog.Warn("WORLD", $"No name source for {country.id}'s cabinet — it will be unstaffed.");
                return;
            }

            MakeCabinetFor(rng, country, profile);
        }

        static void MakeCabinetFor(Random rng, CountryState country, CountryProfile profile)
        {
            if (profile == null) return;
            float Roll(float min, float max) => (float)Math.Round(min + (max - min) * rng.NextDouble(), 1);

            var usedNames = new HashSet<string>();
            int index = 0;
            foreach (Pillar office in Enum.GetValues(typeof(Pillar)))
            {
                string name;
                do
                {
                    name = $"{profile.firstNames[rng.Next(profile.firstNames.Length)]} " +
                           $"{profile.lastNames[rng.Next(profile.lastNames.Length)]}";
                } while (!usedNames.Add(name));

                country.cabinet.Add(new Official
                {
                    // Country-qualified: five officials per state means the old
                    // scheme collided on every id across sixteen cabinets.
                    id = $"OFF_{country.id}_{office.ToString().ToUpperInvariant()}",
                    displayName = name,
                    title = profile.officeTitles[index],
                    office = office,
                    competence = Roll(40, 78),
                    loyalty = Roll(35, 80),
                    riskTolerance = Roll(20, 80),
                    trust = Roll(50, 70),
                    age = Roll(48, 70),
                    mode = ControlMode.Autonomous
                });
                index++;
            }
        }

        /// <summary>Seed each economy from its pillar strength and resource base (GDD §20).</summary>
        static void MakeEconomies(Random rng, GameState state)
        {
            float Roll(float min, float max) => (float)Math.Round(min + (max - min) * rng.NextDouble(), 2);

            foreach (var country in state.countries)
            {
                var eco = country.economy;
                eco.gdp = (float)Math.Round(600f + country.pillars.economy * 22f + Roll(-80, 80), 1);
                eco.growthRate = Roll(0.8f, 3.4f);
                eco.inflation = Roll(1.5f, 4.5f);
                eco.unemployment = Roll(4f, 9f);
                eco.debtToGdp = Roll(28f, 75f);
                eco.confidence = Roll(45f, 68f);
                eco.marketIndex = 100f;
                eco.RecordMarket();

                foreach (EconomicSector sector in Enum.GetValues(typeof(EconomicSector)))
                {
                    float baseOutput;
                    switch (sector)
                    {
                        case EconomicSector.Energy: baseOutput = country.resources.energy; break;
                        case EconomicSector.Agriculture: baseOutput = country.resources.foodSecurity; break;
                        case EconomicSector.Industry: baseOutput = country.resources.industrialCapacity; break;
                        case EconomicSector.Defense: baseOutput = country.pillars.military * 0.8f; break;
                        default: baseOutput = country.pillars.economy; break;
                    }
                    eco.sectors.Add(new SectorState
                    {
                        sector = sector,
                        output = Clamp(baseOutput + Roll(-8f, 8f)),
                        health = Roll(70f, 95f)
                    });
                }
            }
        }

        /// <summary>
        /// Authored trade network. The US–China link is by far the heaviest, so
        /// coercing that partner carries the largest blowback — the slice's
        /// designed economic dilemma (GDD §20).
        /// </summary>
        static void MakeTradeNetwork(GameState state)
        {
            void Link(string a, string b, float volume, TradeFocus focus = TradeFocus.General)
                => state.trade.Add(new TradeRelation { countryA = a, countryB = b, volume = volume, focus = focus });

            // Heavy interdependence — coercing these partners costs us most.
            Link("USA", "CHN", 78f);
            Link("USA", "MEX", 62f);
            Link("USA", "JPN", 48f);
            Link("CHN", "KOR", 52f);
            Link("CHN", "JPN", 50f);
            Link("DEU", "POL", 46f);

            // Substantial but manageable.
            Link("CHN", "RUS", 44f);
            Link("CHN", "AUS", 44f);
            Link("CHN", "IND", 40f);
            Link("CHN", "IDN", 38f);
            Link("DEU", "USA", 40f);
            // The first authored *focused* links (the mechanic shipped with no
            // starting world using one — the Ramstein lesson). Each pairs a
            // food-poor archetype with a plausible supplier, so an authored food
            // dependency is a live lever from month one: embargo or sanction the
            // supplier and the importer's food ceiling genuinely falls.
            Link("JPN", "AUS", 36f, TradeFocus.Food);
            Link("USA", "IND", 34f);
            Link("USA", "KOR", 34f, TradeFocus.Food);
            Link("DEU", "TUR", 32f);
            Link("RUS", "IND", 30f);
            Link("RUS", "KAZ", 34f);
            Link("SAU", "IND", 34f, TradeFocus.Food);
            Link("SAU", "JPN", 32f);
            Link("SAU", "KOR", 30f);
            Link("BRA", "CHN", 36f);
            Link("KOR", "JPN", 28f);
            Link("IDN", "JPN", 26f);

            // Marginal links — the affordable targets for economic coercion.
            Link("USA", "BRA", 22f);
            Link("USA", "SAU", 24f);
            Link("USA", "AUS", 26f);
            Link("USA", "TUR", 18f);
            Link("USA", "POL", 16f);
            Link("USA", "NGA", 14f);
            Link("USA", "IDN", 16f);
            Link("USA", "RUS", 12f);
            Link("USA", "KAZ", 8f);
            Link("DEU", "RUS", 24f);
            Link("TUR", "RUS", 22f);
            Link("IND", "NGA", 16f);
            Link("BRA", "NGA", 12f);
            Link("KAZ", "CHN", 26f);

            // ---------------- expansion roster (Full world) ----------------
            // Links whose either end is absent are pruned by CreateWorld, so
            // these exist only when the Full world does. Two more authored food
            // dependencies (France feeds Egypt, Canada feeds Britain) join the
            // three in the standard network; Argentina's soy link to China is a
            // food *export* lever pointing the other way.
            Link("USA", "GBR", 40f);
            Link("USA", "CAN", 58f);
            Link("USA", "FRA", 28f);
            Link("FRA", "DEU", 44f);
            Link("GBR", "DEU", 30f);
            Link("GBR", "FRA", 26f);
            Link("ITA", "DEU", 34f);
            Link("ITA", "TUR", 18f);
            Link("CAN", "CHN", 18f);
            Link("CAN", "GBR", 20f, TradeFocus.Food);
            Link("FRA", "EGY", 24f, TradeFocus.Food);
            Link("EGY", "SAU", 22f);
            Link("EGY", "TUR", 18f);
            Link("ZAF", "CHN", 26f);
            Link("ZAF", "DEU", 18f);
            Link("ZAF", "GBR", 16f);
            Link("ARG", "BRA", 30f);
            Link("ARG", "CHN", 22f, TradeFocus.Food);
            Link("VNM", "CHN", 36f);
            Link("VNM", "USA", 30f);
            Link("VNM", "KOR", 22f);
            Link("VNM", "JPN", 20f);
        }

        /// <summary>
        /// Authored starting diplomatic posture. Gameplay setup for a sixteen-power
        /// world, not a model of present-day relations. Pairs left unset start
        /// neutral, which is correct for states with little to do with each other.
        /// </summary>
        static void SeedDiplomaticPosture(GameState state)
        {
            void Set(string a, string b, float relations, float trust, float alignment)
            {
                var relationship = state.FindRelationship(a, b);
                if (relationship == null) return;
                relationship.relations = relations;
                relationship.trust = trust;
                relationship.strategicAlignment = alignment;
            }

            // Rivalries and cool distances.
            Set("USA", "CHN", 34f, 28f, 25f);
            Set("USA", "RUS", 26f, 22f, 20f);
            Set("CHN", "IND", 36f, 32f, 34f);
            Set("CHN", "JPN", 34f, 30f, 28f);
            Set("RUS", "POL", 22f, 18f, 20f);
            Set("RUS", "DEU", 34f, 28f, 30f);
            Set("KOR", "JPN", 48f, 42f, 58f);
            Set("SAU", "TUR", 44f, 38f, 42f);

            // Partnerships and blocs.
            Set("USA", "IND", 62f, 55f, 60f);
            Set("USA", "DEU", 76f, 72f, 78f);
            Set("USA", "JPN", 80f, 76f, 82f);
            Set("USA", "KOR", 76f, 70f, 78f);
            Set("USA", "AUS", 82f, 80f, 84f);
            Set("USA", "POL", 72f, 68f, 74f);
            Set("USA", "MEX", 66f, 58f, 62f);
            Set("USA", "SAU", 58f, 48f, 54f);
            Set("USA", "BRA", 58f, 52f, 56f);
            Set("DEU", "POL", 70f, 64f, 72f);
            Set("JPN", "AUS", 74f, 70f, 76f);
            Set("CHN", "RUS", 64f, 52f, 66f);
            Set("RUS", "KAZ", 66f, 56f, 64f);
            Set("CHN", "KAZ", 58f, 48f, 56f);
            Set("RUS", "IND", 58f, 54f, 52f);
            Set("IND", "IDN", 58f, 52f, 56f);
            Set("BRA", "IND", 56f, 50f, 54f);

            // Buffer states hedge: warm with everyone, committed to nobody.
            Set("KAZ", "TUR", 54f, 46f, 50f);
            Set("IDN", "AUS", 56f, 48f, 52f);
            Set("NGA", "USA", 52f, 44f, 48f);
            Set("NGA", "CHN", 56f, 46f, 52f);

            // ---------------- expansion roster (Full world) ----------------
            // Set() no-ops when a relationship does not exist, so these bind
            // only in worlds that contain both ends.
            Set("USA", "GBR", 84f, 82f, 86f);
            Set("USA", "CAN", 86f, 84f, 88f);
            Set("USA", "FRA", 70f, 64f, 72f);
            Set("GBR", "FRA", 68f, 62f, 70f);
            Set("GBR", "DEU", 70f, 66f, 72f);
            Set("FRA", "DEU", 78f, 74f, 80f);
            Set("ITA", "DEU", 68f, 62f, 70f);
            Set("CAN", "GBR", 74f, 70f, 76f);
            Set("GBR", "RUS", 30f, 24f, 22f);
            Set("FRA", "RUS", 36f, 30f, 30f);
            Set("EGY", "SAU", 62f, 54f, 58f);
            Set("EGY", "USA", 56f, 46f, 52f);
            Set("VNM", "CHN", 38f, 32f, 30f);
            Set("VNM", "USA", 54f, 46f, 50f);
            Set("VNM", "KOR", 56f, 50f, 54f);
            Set("ARG", "BRA", 62f, 56f, 58f);
            Set("ZAF", "CHN", 58f, 48f, 54f);
        }

        /// <summary>
        /// Strategic locations for the slice: a capital plus meaningful economic
        /// and military nodes per country, and one contested sea lane that serves
        /// as the flashpoint. The lane is a deliberately generic maritime
        /// chokepoint rather than any real-world disputed territory.
        /// </summary>
        static void MakeMap(Random rng, GameState state, HashSet<string> included)
        {
            float Roll(float min, float max) => (float)Math.Round(min + (max - min) * rng.NextDouble(), 1);

            void Add(string id, string name, LocationType type, string owner, float defense, float value,
                     string hostedOperator = null)
            {
                // Skipped entries consume no draw — see the CreateWorld comment.
                if (!included.Contains(owner)) return;

                state.locations.Add(new StrategicLocation
                {
                    id = id,
                    displayName = name,
                    type = type,
                    ownerId = owner,
                    originalOwnerId = owner,
                    foreignOperatorId = hostedOperator ?? string.Empty,
                    defenseValue = defense,
                    strategicValue = value,
                    garrison = Roll(30, 60)
                });
            }

            Add("USA_CAP", "Washington D.C.", LocationType.Capital, "USA", 74, 95);
            Add("USA_PRT", "Norfolk Naval Complex", LocationType.Port, "USA", 55, 72);
            Add("USA_ENR", "Gulf Coast Energy Belt", LocationType.EnergyRegion, "USA", 38, 76);

            Add("CHN_CAP", "Beijing", LocationType.Capital, "CHN", 76, 95);
            Add("CHN_PRT", "Port of Shanghai", LocationType.Port, "CHN", 52, 80);
            Add("CHN_IND", "Pearl River Delta", LocationType.IndustrialCenter, "CHN", 44, 82);

            Add("RUS_CAP", "Moscow", LocationType.Capital, "RUS", 72, 95);
            Add("RUS_ENR", "Western Siberian Fields", LocationType.EnergyRegion, "RUS", 40, 84);

            Add("IND_CAP", "New Delhi", LocationType.Capital, "IND", 68, 95);
            Add("IND_IND", "Mumbai Industrial Region", LocationType.IndustrialCenter, "IND", 46, 74);

            Add("DEU_CAP", "Berlin", LocationType.Capital, "DEU", 64, 92);
            Add("DEU_IND", "Ruhr Industrial Belt", LocationType.IndustrialCenter, "DEU", 44, 80);

            Add("JPN_CAP", "Tokyo", LocationType.Capital, "JPN", 66, 92);
            Add("JPN_PRT", "Port of Yokohama", LocationType.Port, "JPN", 50, 76);

            Add("BRA_CAP", "Brasília", LocationType.Capital, "BRA", 58, 88);
            Add("BRA_ENR", "Santos Basin", LocationType.EnergyRegion, "BRA", 34, 70);

            Add("TUR_CAP", "Ankara", LocationType.Capital, "TUR", 60, 88);
            Add("TUR_CHK", "Bosphorus Transit", LocationType.Chokepoint, "TUR", 58, 78);

            Add("NGA_CAP", "Abuja", LocationType.Capital, "NGA", 48, 85);
            Add("NGA_ENR", "Niger Delta Fields", LocationType.EnergyRegion, "NGA", 26, 78);

            Add("SAU_CAP", "Riyadh", LocationType.Capital, "SAU", 62, 90);
            Add("SAU_ENR", "Eastern Province Fields", LocationType.EnergyRegion, "SAU", 44, 94);

            Add("AUS_CAP", "Canberra", LocationType.Capital, "AUS", 56, 86);
            // Filed as an industrial centre until MaterialsRegion existed, which
            // meant taking the largest mining region on earth paid out in factory
            // capacity. The id always said MAT; only the type was wrong.
            Add("AUS_MAT", "Pilbara Mining Region", LocationType.MaterialsRegion, "AUS", 30, 76);

            Add("KOR_CAP", "Seoul", LocationType.Capital, "KOR", 70, 92);
            Add("KOR_IND", "Ulsan Industrial Complex", LocationType.IndustrialCenter, "KOR", 46, 78);

            Add("MEX_CAP", "Mexico City", LocationType.Capital, "MEX", 54, 88);
            Add("MEX_IND", "Bajío Manufacturing Belt", LocationType.IndustrialCenter, "MEX", 36, 68);

            Add("IDN_CAP", "Jakarta", LocationType.Capital, "IDN", 52, 86);
            Add("IDN_CHK", "Malacca Approaches", LocationType.Chokepoint, "IDN", 48, 88);

            Add("POL_CAP", "Warsaw", LocationType.Capital, "POL", 62, 88);
            Add("POL_PAS", "Eastern Frontier Corridor", LocationType.MountainPass, "POL", 66, 72);

            Add("KAZ_CAP", "Astana", LocationType.Capital, "KAZ", 50, 84);
            Add("KAZ_MAT", "Caspian Resource Belt", LocationType.MaterialsRegion, "KAZ", 28, 80);

            // Basing. `LocationType.Airbase` existed for the whole project with
            // no authored location using it, so the one thing that gives a force
            // reach could never change hands. Deliberately spread across powers
            // and partners so projection is contestable rather than a birthright.
            // Ramstein is **German ground the United States operates from**, not
            // American territory. It was authored as USA-owned, which was
            // harmless while position meant nothing and became wrong the moment
            // geography did: a forward base whose entire identity is that it is
            // forward sat at Washington's coordinates and gave its operator no
            // European reach at all.
            //
            // This is also the world's one authored example of the hosting
            // mechanic. `foreignOperatorId` shipped with no starting world using
            // it, so the strategic argument for a Transit commitment — reach you
            // did not have to conquer — had nothing to point at. Germany can
            // revoke it if the relationship sours, which is the point.
            Add("DEU_AIR", "Ramstein Air Complex", LocationType.Airbase, "DEU", 58, 80,
                hostedOperator: "USA");
            Add("CHN_AIR", "Southern Theatre Airfields", LocationType.Airbase, "CHN", 54, 76);
            Add("RUS_AIR", "Western Military District Airfields", LocationType.Airbase, "RUS", 56, 74);
            Add("IND_AIR", "Western Air Command", LocationType.Airbase, "IND", 50, 70);
            Add("TUR_AIR", "İncirlik Air Complex", LocationType.Airbase, "TUR", 52, 78);
            Add("AUS_AIR", "Northern Airfields", LocationType.Airbase, "AUS", 40, 64);

            // ---------------- maritime ground ----------------
            //
            // Nine of the twenty-three operations need a maritime target, and the
            // world had three ports and three chokepoints. Blockade, sea control,
            // commerce raiding, mine warfare, convoy escort and amphibious
            // assault were mostly buttons with nothing to point at — the naval
            // half of the verb list existed in code and not in the world.
            //
            // Every maritime state now has a port, so a navy has somewhere to be
            // and somewhere to be attacked. Ports are authored a little softer
            // than capitals: a harbour is a thing you take, not a fortress.
            Add("KOR_PRT", "Busan Container Terminal", LocationType.Port, "KOR", 48, 74);
            Add("IDN_PRT", "Tanjung Priok", LocationType.Port, "IDN", 40, 70);
            Add("AUS_PRT", "Fremantle Approaches", LocationType.Port, "AUS", 38, 62);
            Add("DEU_PRT", "Hamburg Container Port", LocationType.Port, "DEU", 42, 72);
            Add("RUS_PRT", "Northern Fleet Anchorage", LocationType.Port, "RUS", 54, 68);
            Add("IND_PRT", "Mumbai Port Trust", LocationType.Port, "IND", 44, 70);
            Add("BRA_PRT", "Port of Santos", LocationType.Port, "BRA", 36, 66);
            Add("NGA_PRT", "Lagos Deepwater Terminal", LocationType.Port, "NGA", 30, 64);
            Add("MEX_PRT", "Manzanillo Terminal", LocationType.Port, "MEX", 32, 60);
            Add("SAU_PRT", "Red Sea Terminal", LocationType.Port, "SAU", 46, 76);
            Add("POL_PRT", "Baltic Container Port", LocationType.Port, "POL", 38, 58);
            Add("TUR_PRT", "Eastern Mediterranean Anchorage", LocationType.Port, "TUR", 42, 64);

            // Chokepoints are where the sea stops being open water and starts
            // being leverage. Deliberately generic names: these are pressure
            // points in the simulation, not claims about anybody's waters.
            Add("SAU_CHK", "Southern Red Sea Narrows", LocationType.Chokepoint, "SAU", 52, 82);
            Add("IDN_CHK2", "Eastern Archipelago Passage", LocationType.Chokepoint, "IDN", 44, 74);
            Add("MEX_CHK", "Isthmus Transit", LocationType.Chokepoint, "MEX", 40, 80);
            Add("DEU_CHK", "Baltic Approaches", LocationType.Chokepoint, "DEU", 46, 66);
            Add("JPN_CHK", "Northern Straits", LocationType.Chokepoint, "JPN", 50, 70);

            // A second materials region so the commodity is contestable rather
            // than owned by two states nobody borders.
            Add("BRA_MAT", "Amazonian Mineral Belt", LocationType.MaterialsRegion, "BRA", 26, 68);
            Add("NGA_MAT", "Jos Plateau Workings", LocationType.MaterialsRegion, "NGA", 24, 62);

            // The flashpoint: a deliberately generic maritime chokepoint rather
            // than any real-world disputed territory.
            Add("CONTESTED_LANE", "Contested Sea Lane", LocationType.Chokepoint, "CHN", 62, 70);

            // ---------------- expansion roster ground (Full world) ----------------
            //
            // Authored after every Standard-world location so a Standard build
            // draws an identical garrison stream — the same tail rule as the
            // profiles. Maritime powers get ports (the fleet-basing invariant),
            // Egypt gets the world's shortest sea route, and the southern
            // hemisphere finally produces food and minerals on the map.
            Add("GBR_CAP", "London", LocationType.Capital, "GBR", 66, 92);
            Add("GBR_PRT", "Clyde Naval Anchorage", LocationType.Port, "GBR", 48, 70);

            Add("FRA_CAP", "Paris", LocationType.Capital, "FRA", 66, 92);
            Add("FRA_PRT", "Toulon Naval Harbour", LocationType.Port, "FRA", 46, 68);

            Add("ITA_CAP", "Rome", LocationType.Capital, "ITA", 58, 88);
            Add("ITA_IND", "Po Valley Industrial Belt", LocationType.IndustrialCenter, "ITA", 42, 72);

            Add("CAN_CAP", "Ottawa", LocationType.Capital, "CAN", 54, 86);
            Add("CAN_ENR", "Prairie Energy Corridor", LocationType.EnergyRegion, "CAN", 30, 72);
            Add("CAN_MAT", "Shield Mineral Belt", LocationType.MaterialsRegion, "CAN", 26, 70);

            Add("EGY_CAP", "Cairo", LocationType.Capital, "EGY", 56, 88);
            Add("EGY_CHK", "Suez Transit", LocationType.Chokepoint, "EGY", 54, 90);

            Add("ZAF_CAP", "Pretoria", LocationType.Capital, "ZAF", 48, 84);
            Add("ZAF_MAT", "Highveld Mineral Complex", LocationType.MaterialsRegion, "ZAF", 28, 82);

            Add("ARG_CAP", "Buenos Aires", LocationType.Capital, "ARG", 46, 84);
            Add("ARG_PRT", "River Plate Terminal", LocationType.Port, "ARG", 32, 62);

            Add("VNM_CAP", "Hanoi", LocationType.Capital, "VNM", 56, 86);
            Add("VNM_IND", "Red River Manufacturing Belt", LocationType.IndustrialCenter, "VNM", 38, 70);
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
