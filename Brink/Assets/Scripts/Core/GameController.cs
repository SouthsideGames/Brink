using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Session orchestrator: owns the active GameState and TurnManager, handles
    /// new game / continue / reset, and autosaves after every resolved month
    /// (GDD §30: persistent autosave with frequent safe checkpoints).
    /// Plain C# singleton so tests can construct their own instances.
    /// </summary>
    public class GameController
    {
        public const int AutosaveSlot = 0;

        static GameController instance;
        public static GameController Instance => instance ?? (instance = new GameController());

        public GameState State { get; private set; }
        public TurnManager Turns { get; private set; }

        public bool IsRunning => State != null;

        public event Action StateReplaced;

        /// <summary>
        /// True when there is no save and the operator has not yet completed the
        /// first-launch assessment (GDD §5).
        /// </summary>
        public bool AwaitingAssessment => !IsRunning;

        /// <summary>
        /// Continue the autosave if one exists. Otherwise leave the session
        /// unstarted so the shell presents the first-launch assessment.
        /// </summary>
        public void EnsureStarted()
        {
            if (IsRunning) return;
            if (!SaveSystem.SaveExists(AutosaveSlot)) return;

            try
            {
                Attach(SaveSystem.Load(AutosaveSlot));
            }
            catch (Exception e)
            {
                GameLog.Error("SAVE", $"Autosave failed to load. Returning to assessment. ({e.Message})");
            }
        }

        /// <summary>Begin a save from a completed assessment (GDD §5).</summary>
        public void NewGameFromAssessment(Data.AssessmentResult result, int seed)
        {
            GameLog.Info("GAME", $"New posting: {result.assignedCountryId}. Seed: {seed}.");
            var state = WorldFactory.CreateWorld(seed, result.assignedCountryId);
            AssessmentSystem.ApplyToWorld(state, result);
            ProgressionSystem.CaptureYearSnapshot(state);
            TutorialSystem.Begin(state);
            Attach(state);
            SaveSystem.Save(State, AutosaveSlot);
        }

        public void NewGame(int seed)
        {
            GameLog.Info("GAME", $"New game. Seed: {seed}.");
            Attach(WorldFactory.CreateDebugWorld(seed));
            SaveSystem.Save(State, AutosaveSlot);
        }

        /// <summary>
        /// Full reset (GDD §5.1): erase the nation, world history, Cabinet,
        /// relationships and all Strategist progression, then return the operator
        /// to the first-launch assessment.
        /// </summary>
        public void ResetGame()
        {
            GameLog.Warn("GAME", "FULL RESET requested. Erasing state and progression.");
            // Every slot, not just the autosave — otherwise a manual save can
            // restore the world the reset was supposed to erase (GDD §5.1).
            SaveSystem.DeleteAll();
            State = null;
            Turns = null;
            StateReplaced?.Invoke();
        }

        public bool EndMonth()
        {
            if (!IsRunning) return false;
            bool advanced = Turns.EndMonth();
            if (advanced)
            {
                TutorialSystem.Evaluate(State);
                SaveSystem.Save(State, AutosaveSlot);
            }
            return advanced;
        }

        /// <summary>Advance orientation after any player action. Cheap and idempotent.</summary>
        public void RefreshTutorial()
        {
            if (IsRunning) TutorialSystem.Evaluate(State);
        }

        public void SkipTutorial()
        {
            if (!IsRunning) return;
            TutorialSystem.Skip(State);
            SaveSystem.Save(State, AutosaveSlot);
        }

        /// <summary>Acknowledge a read-only orientation step.</summary>
        public void AcknowledgeTutorialStep()
        {
            if (!IsRunning) return;
            TutorialSystem.Evaluate(State);
            SaveSystem.Save(State, AutosaveSlot);
        }

        public bool SetControlMode(Data.Official official, Data.ControlMode mode)
        {
            if (!IsRunning) return false;
            bool ok = CabinetSystem.SetMode(State, official, mode);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool SetDirective(Data.Official official, string directiveId)
        {
            if (!IsRunning) return false;
            bool ok = CabinetSystem.SetDirective(State, official, directiveId);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool ExecuteDirectAction(Data.Pillar pillar)
        {
            if (!IsRunning) return false;
            bool ok = CabinetSystem.TryDirectAction(State, Turns, pillar);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Fill a vacant cabinet office from its shortlist (GDD §7.3).
        ///
        /// Costs no Command Points: appointing is not an operation, it is the
        /// operator doing the one thing only they can do about a seat that is
        /// already empty. Charging for it would mean an unlucky month could
        /// leave a ministry unfilled because the budget ran out.
        /// </summary>
        public bool AppointOfficial(Data.Pillar office, int candidateIndex)
        {
            if (!IsRunning) return false;
            if (!MayCommand(Data.Pillar.Government)) return false;

            bool ok = CabinetLifecycle.Appoint(State, office, candidateIndex);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Whether the operator may personally act in this pillar, obtaining the
        /// grant if the constitution allows it to be obtained (GDD §3).
        ///
        /// Every verb that acts on a pillar routes through here, which is the
        /// whole point: `AuthoritySystem` previously had one call site guarding
        /// the Cabinet mode toggle, so a Parliamentary operator holding only
        /// advisory standing over the economy could still impose sanctions and
        /// set tariffs directly. The rule read as enforced and was not.
        ///
        /// `DeclareEmergencyPowers` is deliberately **not** gated: it is the act
        /// of claiming authority beyond the ordinary distribution, so it cannot
        /// require the authority it grants. Without that exemption a
        /// Parliamentary operator — advisory over Government — could never reach
        /// the one instrument designed to break the deadlock.
        /// </summary>
        bool MayCommand(Data.Pillar pillar)
            => IsRunning && AuthoritySystem.EnsureAuthority(State, pillar);

        // ---------- economic commands ----------

        public bool ImposeSanctions(string targetId, Data.SanctionSeverity severity)
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            bool ok = EconomySystem.ImposeSanctions(State, Turns, targetId, severity);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool LiftSanctions(string targetId)
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            bool ok = EconomySystem.LiftSanctions(State, Turns, targetId);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Begin working to bring a friendly country into ours (GDD §15.1).
        ///
        /// Gated on the Diplomacy pillar rather than Intelligence: the effort
        /// runs on a relationship, and the constitutional question is whether
        /// this operator may commit the nation's foreign policy.
        /// </summary>
        public bool BeginAccession(string targetId, Data.AccessionRoute route)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            bool ok = AccessionSystem.Begin(State, Turns, targetId, route);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>Call off an accession effort. The work is lost, the friend is not.</summary>
        public bool AbandonAccession(string targetId)
        {
            if (!IsRunning) return false;
            bool ok = AccessionSystem.Abandon(State, State.playerCountryId, targetId);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Put a trade agreement to another government (GDD §20). They sign or
        /// they do not, on their own interests.
        /// </summary>
        public bool ProposeTrade(Data.TradeDeal deal)
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            bool ok = TradeSystem.ProposeAgreement(State, Turns, deal);
            SaveSystem.Save(State, AutosaveSlot); // the attempt is worth recording
            return ok;
        }

        /// <summary>Walk away from a trade arrangement. The partner remembers.</summary>
        public bool WithdrawFromTrade(string partnerId)
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            bool ok = TradeSystem.Withdraw(State, Turns, partnerId);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool SetTariff(string partnerId, float tariff)
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            bool ok = EconomySystem.SetTariff(State, Turns, partnerId, tariff);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        // ---------- intelligence commands ----------

        public bool EstablishNetwork(string targetId, Data.IntelDomain focus)
        {
            if (!MayCommand(Data.Pillar.Intelligence)) return false;
            bool ok = IntelligenceSystem.EstablishNetwork(State, Turns, targetId, focus);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool ExpandNetwork(string targetId)
        {
            if (!MayCommand(Data.Pillar.Intelligence)) return false;
            bool ok = IntelligenceSystem.ExpandNetwork(State, Turns, targetId);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool SetIntelFocus(string targetId, Data.IntelDomain focus)
        {
            if (!IsRunning) return false;
            bool ok = IntelligenceSystem.SetFocus(State, targetId, focus);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>Propose a treaty whose clauses say who carries what (GDD §15.1 amendment).</summary>
        public bool ProposeNegotiatedTreaty(string targetId, System.Collections.Generic.List<Data.TreatyClause> clauses)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            if (!Turns.SpendCommandPoints(DiplomacySystem.TreatyProposalCost, "Treaty proposal")) return false;
            bool ok = DiplomacySystem.ProposeNegotiatedTreatyBy(State, State.playerCountryId, targetId, clauses);
            if (ok) ProgressionSystem.RecordInitiative(State);
            SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>An operation against a named foreign official (GDD §14 amendment).</summary>
        public bool RunAgentOperation(string targetCountryId, Data.Official official, AgentAction action)
        {
            if (!MayCommand(Data.Pillar.Intelligence)) return false;
            bool ok = AgentSystem.Run(State, Turns, targetCountryId, official, action);
            SaveSystem.Save(State, AutosaveSlot);   // CP was spent either way
            return ok;
        }

        /// <summary>Begin an industrial programme (GDD §20 amendment).</summary>
        public bool BeginIndustrialProgramme(EconomicSector sector, IndustrialScale scale)
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            bool ok = IndustrialSystem.Begin(State, Turns, sector, scale);
            SaveSystem.Save(State, AutosaveSlot);   // CP was spent either way
            return ok;
        }

        public bool RunCovertOperation(string targetId, CovertOperation operation, float deceptionBias = 1f,
            Data.IntelDomain deceptionDomain = Data.IntelDomain.Military)
        {
            if (!MayCommand(Data.Pillar.Intelligence)) return false;
            bool ok = IntelligenceSystem.RunCovertOperation(State, Turns, targetId, operation, deceptionBias, deceptionDomain);
            SaveSystem.Save(State, AutosaveSlot); // save regardless: CP was spent, consequences applied
            return ok;
        }

        public bool StrengthenCounterIntelligence()
        {
            if (!MayCommand(Data.Pillar.Intelligence)) return false;
            bool ok = IntelligenceSystem.StrengthenCounterIntelligence(State, Turns);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        // ---------- progression ----------

        public bool UnlockSkill(string nodeId)
        {
            if (!IsRunning) return false;
            bool ok = ProgressionSystem.Unlock(State, nodeId);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        // ---------- government commands ----------

        public bool PublicMessaging()
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            bool ok = GovernmentSystem.PublicMessaging(State);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool DismissOfficial(Data.Pillar office)
        {
            if (!IsRunning) return false;
            bool ok = GovernmentSystem.DismissOfficial(State, office);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool InstitutionalReform()
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            bool ok = GovernmentSystem.InstitutionalReform(State);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool DeclareEmergencyPowers()
        {
            if (!IsRunning) return false;
            bool ok = GovernmentSystem.DeclareEmergencyPowers(State);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool CallEarlyElection()
        {
            if (!IsRunning) return false;
            bool ok = GovernmentSystem.CallEarlyElection(State);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool SecureMilitaryLoyalty()
        {
            if (!IsRunning) return false;
            bool ok = RegimeSystem.SecureMilitaryLoyalty(State);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool SetNationalPriority(Data.NationalPriority priority)
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            bool ok = GovernmentSystem.SetNationalPriority(State, priority);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool BuildPoliticalSupport()
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            bool ok = GovernmentSystem.BuildPoliticalSupport(State);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool DistributePatronage()
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            bool ok = GovernmentSystem.DistributePatronage(State);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool LaunchInquiry()
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            bool ok = GovernmentSystem.LaunchInquiry(State);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool GroomSuccessor()
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            bool ok = GovernmentSystem.GroomSuccessor(State);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool SetCivicPosture(Data.CivicPosture posture)
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            bool ok = GovernmentSystem.SetCivicPosture(State, posture);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Deliberately not behind <see cref="MayCommand"/>. Amending what the
        /// operator may command cannot itself require the authority being
        /// amended, or a parliamentary operator — the one who most needs it —
        /// could never reach it. Same reasoning as emergency powers.
        /// </summary>
        public bool ConsolidateAuthority(Data.Pillar pillar)
        {
            if (!IsRunning) return false;
            bool ok = GovernmentSystem.ConsolidateAuthority(State, pillar);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        // ---------- diplomacy commands ----------

        public bool DiplomaticOutreach(string targetId)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            bool ok = DiplomacySystem.Outreach(State, Turns, targetId);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool ProposeTreaty(string targetId, System.Collections.Generic.List<Data.TreatyCommitment> commitments)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            bool ok = DiplomacySystem.ProposeTreaty(State, Turns, targetId, commitments);
            SaveSystem.Save(State, AutosaveSlot); // CP was spent either way
            return ok;
        }

        public bool BreakTreaty(string partnerId)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            bool ok = DiplomacySystem.BreakTreaty(State, partnerId);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public Data.Coalition RequestCoalition()
        {
            if (!IsRunning) return null;
            var coalition = DiplomacySystem.RequestCoalition(State, Turns);
            if (coalition != null) SaveSystem.Save(State, AutosaveSlot);
            return coalition;
        }

        public bool PrepareEndgame(Data.EndgameType type)
        {
            if (!IsRunning) return false;
            bool ok = EndgameSystem.Prepare(State, Turns, type);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool ExecuteEndgame(Data.EndgameType type, string targetId)
        {
            if (!IsRunning) return false;
            bool ok = EndgameSystem.Execute(State, Turns, type, targetId);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool BeginResearch(string capabilityId)
        {
            if (!IsRunning) return false;
            bool ok = TechnologySystem.BeginResearch(State, Turns, capabilityId);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool SetPosture(Data.MilitaryPosture posture)
        {
            if (!MayCommand(Data.Pillar.Military)) return false;
            bool ok = MilitarySystem.SetPosture(State, Turns, posture);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool BeginProcurement(Data.ForceBranch branch, MilitarySystem.ProgramScale scale)
        {
            if (!MayCommand(Data.Pillar.Military)) return false;
            bool ok = MilitarySystem.BeginProcurement(State, Turns, branch, scale);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool InvestInLogistics()
        {
            if (!MayCommand(Data.Pillar.Military)) return false;
            bool ok = MilitarySystem.InvestInLogistics(State, Turns);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool SetDoctrine(Data.MilitaryDoctrine doctrine)
        {
            if (!MayCommand(Data.Pillar.Military)) return false;
            bool ok = MilitarySystem.SetDoctrine(State, Turns, doctrine);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public Data.ExerciseRecord ConductExercise(string partnerId, Data.ExerciseScale scale, Data.ExerciseFocus focus)
        {
            if (!IsRunning) return null;
            var record = ExerciseSystem.Conduct(State, Turns, partnerId, scale, focus);
            if (record != null) SaveSystem.Save(State, AutosaveSlot);
            return record;
        }

        // ---------- confrontation commands ----------

        public Data.Confrontation BeginConfrontation(string defenderId, Data.ConfrontationObjective objective,
            string locationId, Data.PrimaryStrategy strategy)
        {
            if (!MayCommand(Data.Pillar.Military)) return null;
            var confrontation = ConfrontationSystem.Begin(State, Turns, State.playerCountryId, defenderId, objective, locationId, strategy);
            if (confrontation != null) SaveSystem.Save(State, AutosaveSlot);
            return confrontation;
        }

        /// <summary>Strategic Pivot (GDD §18.2).</summary>
        public bool Pivot(Data.PrimaryStrategy strategy)
        {
            if (!MayCommand(Data.Pillar.Military) || State.ActiveConfrontation == null) return false;
            bool ok = ConfrontationSystem.Pivot(State, Turns, State.ActiveConfrontation, strategy);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool SetEscalation(Data.EscalationState target)
        {
            if (!MayCommand(Data.Pillar.Military) || State.ActiveConfrontation == null) return false;
            bool ok = ConfrontationSystem.SetEscalation(State, Turns, State.ActiveConfrontation, target);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// A defensive programme on ground we hold. Needs no confrontation, which
        /// is the whole point — these are most valuable before a war, and were
        /// previously reachable only during one.
        /// </summary>
        public bool OrderAssets(Data.AssetKind kind, float count)
        {
            if (!MayCommand(Data.Pillar.Military)) return false;
            bool ok = AcquisitionSystem.Order(State, Turns, kind, count);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Re-cutting the budget is a government act, not a military one, so it
        /// sits behind the Government pillar's authority rather than the
        /// Military's — the same reasoning that puts emergency powers there.
        /// </summary>
        public bool SetWarFooting(bool active)
        {
            if (!IsRunning) return false;
            bool ok = AcquisitionSystem.SetWarFooting(State, active);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public Data.OperationRecord LaunchDefensiveProgramme(string locationId, OperationType type)
        {
            if (!MayCommand(Data.Pillar.Military)) return null;
            if (ConfrontationSystem.RequiresConfrontation(type)) return null;

            var record = ConfrontationSystem.LaunchOperation(
                State, Turns, State.ActiveConfrontation, locationId, type,
                new Data.OperationDirective());
            if (record != null) SaveSystem.Save(State, AutosaveSlot);
            return record;
        }

        public Data.OperationRecord LaunchOperation(string locationId, OperationType type, Data.OperationDirective directive)
        {
            if (!IsRunning || State.ActiveConfrontation == null) return null;
            var record = ConfrontationSystem.LaunchOperation(State, Turns, State.ActiveConfrontation, locationId, type, directive);
            if (record != null) SaveSystem.Save(State, AutosaveSlot);
            return record;
        }

        /// <summary>Put a specific set of terms to the other side (GDD §26).</summary>
        public bool ProposeTerms(Data.PeaceProposal proposal)
        {
            if (!IsRunning || State.ActiveConfrontation == null) return false;
            bool ok = PeaceSystem.ProposeTerms(State, State.ActiveConfrontation, State.playerCountryId, proposal);
            SaveSystem.Save(State, AutosaveSlot); // the attempt itself is worth recording
            if (ok) ProgressionSystem.RecordInitiative(State);
            return ok;
        }

        public bool ProposeSettlement(bool concede = false)
        {
            if (!IsRunning || State.ActiveConfrontation == null) return false;
            bool ok = ConfrontationSystem.ProposeSettlement(State, State.ActiveConfrontation, concede);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>Answer a Crisis Turn, then checkpoint (GDD §30: consequences matter).</summary>
        public void ResolveCrisis(Data.ActiveCrisis crisis, int optionIndex)
        {
            if (!IsRunning) return;
            CrisisSystem.Resolve(State, crisis, optionIndex);
            SaveSystem.Save(State, AutosaveSlot);
        }

        public void SaveToSlot(int slot) { if (IsRunning) SaveSystem.Save(State, slot); }

        public void LoadFromSlot(int slot) => Attach(SaveSystem.Load(slot));

        void Attach(GameState state)
        {
            State = state;
            Turns = new TurnManager(state);

            // One wiring list, shared with the validation harness. Do not add
            // systems here — add them to SimulationPipeline, or they will run in
            // play and be absent from every decade-long validation run.
            SimulationPipeline.Wire(Turns, State);

            // A recording covers one continuous run. Loading a save or starting
            // a new posting begins a new one, because a session log's whole
            // value is that it describes a single unbroken sequence.
            Telemetry.BeginSession(State);

            StateReplaced?.Invoke();
        }
    }
}
