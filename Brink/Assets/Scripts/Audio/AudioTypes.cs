namespace Brink.Audio
{
    /// <summary>
    /// What the world is doing (GDD §23, §18.1). The *situation* half of the
    /// audio state.
    ///
    /// Deliberately separate from <see cref="PillarContext"/>: where the operator
    /// is looking and what is happening to the country are independent, and
    /// collapsing them into one enum is the decision that would make synchronised
    /// pillar stems impossible to add later.
    ///
    /// Append-only. These are persisted only transiently, but the debug panel and
    /// any future save of "what was playing" would read them by ordinal.
    /// </summary>
    public enum MusicState
    {
        /// <summary>Before a posting exists — the assessment and first launch.</summary>
        MainMenu = 0,

        Peace = 1,
        Tension = 2,
        Crisis = 3,
        War = 4
    }

    /// <summary>
    /// Where the operator is looking. The *place* half of the audio state.
    ///
    /// Maps to the five pillars plus the world view. Tracked from today and
    /// deliberately not yet used to choose a track — see
    /// <see cref="AudioLibrary.Resolve"/> — so that changing panels provably
    /// cannot interrupt the music.
    /// </summary>
    public enum PillarContext
    {
        World = 0,
        Military = 1,
        Economy = 2,
        Intelligence = 3,
        Diplomacy = 4,
        Government = 5
    }

    /// <summary>
    /// Semantic sound identities.
    ///
    /// **Gameplay code names these, never a file.** The whole point of the
    /// indirection is that replacing what `FlashAlert` sounds like is an edit to
    /// one ScriptableObject rather than a search across forty call sites — the
    /// same reasoning that put the monthly system list in `SimulationPipeline`
    /// and the wrapping rule in one method.
    ///
    /// Several of these deliberately have no clip yet. An unmapped id is silent
    /// and harmless; forcing a wrong sound into a role would define the game's
    /// audio identity by accident and be much harder to walk back.
    /// </summary>
    public enum SfxId
    {
        None = 0,

        // ---- general UI ----
        UIHover = 1,
        UIConfirm = 2,
        UICancel = 3,
        PanelOpen = 4,
        PanelClose = 5,

        // ---- issuing orders ----
        CommandAccepted = 10,
        CommandRejected = 11,

        // ---- the turn ----
        EndMonth = 20,

        // ---- traffic, in the notification hierarchy's own order ----
        IncomingReport = 30,
        AdvisoryAlert = 31,
        PriorityAlert = 32,
        FlashAlert = 33,

        // ---- outcomes ----
        PositiveOutcome = 40,
        NegativeOutcome = 41,

        // ---- pillar events ----
        IntelligenceTransmission = 50,
        CrisisStarted = 60,
        WarDeclared = 70,

        // ---- progression and system ----
        AnnualEvaluation = 80,
        SaveComplete = 90
    }
}
