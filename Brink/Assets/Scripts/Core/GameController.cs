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
        public void NewGameFromAssessment(Data.AssessmentResult result, int seed,
            Data.WorldSize size = Data.WorldSize.Standard,
            Data.Difficulty difficulty = Data.Difficulty.Challenging)
        {
            GameLog.Info("GAME", $"New posting: {result.assignedCountryId}. Seed: {seed}. World: {size}. Difficulty: {difficulty}.");
            var state = WorldFactory.CreateWorld(seed, result.assignedCountryId, size);
            // Every balance figure is measured at Challenging (spec 12); a real
            // game used to open at Standard because nothing ever set this.
            state.difficulty = difficulty;
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

        /// <summary>
        /// Arm an existing movement in somebody else's country (GDD §17.1).
        ///
        /// Note what is missing: there is no verb here that *starts* one. A
        /// government finds a rising and supplies it; it does not commission a
        /// grievance. That is the same rule `RegimeSystem` runs on, and it is
        /// what keeps a stable state un-destabilisable by clicking.
        /// </summary>
        public bool SupportInsurgency(string insurgencyId)
        {
            if (!MayCommand(Data.Pillar.Intelligence)) return false;
            bool ok = InsurgencySystem.Support(State, Turns,
                InsurgencySystem.Find(State, insurgencyId));
            SaveSystem.Save(State, AutosaveSlot);   // CP was spent either way
            return ok;
        }

        /// <summary>Close the channel. What they hold, they keep.</summary>
        public bool WithdrawInsurgencySupport(string insurgencyId)
        {
            if (!IsRunning) return false;
            bool ok = InsurgencySystem.WithdrawSupportBy(State, State.playerCountryId,
                InsurgencySystem.Find(State, insurgencyId));
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Stand back for a stretch of quiet months (GDD §6).
        ///
        /// Not a fast-forward: it ends the moment anything needs deciding, and
        /// the capacity of the months it consumes is genuinely forgone.
        /// </summary>
        public HoldSystem.Result Hold(int months)
        {
            if (!IsRunning) return new HoldSystem.Result { months = 0, stopped = "No session." };

            var result = HoldSystem.Hold(State, Turns, months);
            if (result.months > 0) SaveSystem.Save(State, AutosaveSlot);
            return result;
        }

        /// <summary>Found a standing bloc and lead it (GDD §15.2).</summary>
        public bool FoundBloc(string name) => FoundBloc(name, null);

        /// <summary>
        /// Found a bloc carrying explicit commitments — the multilateral alliance
        /// (GDD §15.2, user decision 2026-08-27).
        ///
        /// A defence bloc obliges every member to every other, so it is one
        /// signature where the bilateral route needs N² treaties and pays
        /// `PactAnxiety` on each. The terms are fixed here and never edited: a
        /// leader who could add an obligation later would be binding members to
        /// something they never agreed to.
        /// </summary>
        public bool FoundBloc(string name, System.Collections.Generic.List<Data.TreatyCommitment> commitments)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            var bloc = BlocSystem.Found(State, Turns, name, commitments);
            SaveSystem.Save(State, AutosaveSlot);   // CP was spent either way
            return bloc != null;
        }

        /// <summary>Ask a state into the bloc we lead. They decide.</summary>
        public bool InviteToBloc(string targetId)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            bool ok = BlocSystem.Invite(State, Turns, targetId);
            SaveSystem.Save(State, AutosaveSlot);   // CP was spent either way
            return ok;
        }

        /// <summary>Walk out. It costs trust with everyone still in it.</summary>
        public bool LeaveBloc()
        {
            if (!IsRunning) return false;
            bool ok = BlocSystem.LeaveBy(State, State.playerCountryId);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Put a motion to the multilateral chamber (GDD §15.2).
        ///
        /// The motion itself is drafted from live world state by
        /// `CouncilSystem.AvailableMotions` — the operator chooses which
        /// grievance to spend the agenda on, not what to allege.
        /// </summary>
        public bool RaiseCouncilMotion(Data.CouncilMotion motion)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            var resolved = CouncilSystem.Raise(State, Turns, motion);
            SaveSystem.Save(State, AutosaveSlot);   // CP was spent either way
            return resolved != null;
        }

        /// <summary>Begin an industrial programme (GDD §20 amendment).</summary>
        public bool BeginIndustrialProgramme(EconomicSector sector, IndustrialScale scale)
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            bool ok = IndustrialSystem.Begin(State, Turns, sector, scale);
            SaveSystem.Save(State, AutosaveSlot);   // CP was spent either way
            return ok;
        }

        // ---------- fiscal statecraft (spec 02 §9, spec 25 Tranche A) ----------
        //
        // Each of these spends the operator's resource, delegates to the
        // actor-generic verb, and records the initiative. That last line is not
        // optional: a pillar whose actions do not call `RecordInitiative` grades
        // *worse than doing nothing* (spec 07).

        /// <summary>Set the share of the economy the state takes.</summary>
        public bool SetTaxRate(float rate)
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            if (State.PlayerCountry == null) return false;
            if (System.Math.Abs(rate - State.PlayerCountry.fiscal.taxRate) < 0.5f) return false;

            if (!GovernmentSystem.SpendPoliticalCapital(State, FiscalSystem.SetTaxRateCost, "Set tax rate"))
                return false;

            bool ok = FiscalSystem.SetTaxRateBy(State, State.playerCountryId, rate);
            if (ok)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 10, "Tax rate set");
            }
            SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>Balanced, Austerity or Expansionary. A standing choice.</summary>
        public bool SetBudgetPosture(Data.BudgetPosture posture)
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            if (State.PlayerCountry == null || State.PlayerCountry.fiscal.budgetPosture == posture)
                return false;

            if (!Turns.SpendCommandPoints(FiscalSystem.SetBudgetPostureCost, "Set budget posture"))
                return false;

            bool ok = FiscalSystem.SetBudgetPostureBy(State, State.playerCountryId, posture);
            if (ok)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 12, "Budget posture set");
            }
            SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>Raise money on the state's paper. Serviced forever after.</summary>
        public bool IssueSovereignDebt()
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            if (!FiscalSystem.CanIssueDebt(State, State.playerCountryId, out string reason))
            {
                GameLog.Warn("FISCAL", reason);
                return false;
            }
            if (!Turns.SpendCommandPoints(FiscalSystem.IssueDebtCost, "Issue sovereign debt")) return false;

            // Authorship asserted here, not inside the generic verb: the AI
            // borrows through the same call when its treasury runs dry.
            bool ok = FiscalSystem.IssueSovereignDebtBy(State, State.playerCountryId,
                Data.CausalCategory.PlayerDecision, nameof(IssueSovereignDebt));
            if (ok)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 10, "Sovereign debt issued");
            }
            SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>Prop one sector up for as long as it is paid for.</summary>
        public bool SubsidiseSector(EconomicSector sector)
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            if (State.PlayerCountry.resources.treasury < FiscalSystem.SubsidyTreasury)
            {
                GameLog.Warn("FISCAL", "The treasury cannot cover a subsidy.");
                return false;
            }
            if (!Turns.SpendCommandPoints(FiscalSystem.SubsidiseCost, "Subsidise a sector")) return false;

            bool ok = FiscalSystem.SubsidiseSectorBy(State, State.playerCountryId, sector);
            if (ok)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 10, "Sector subsidised");
            }
            SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>Buy down the bite of a future blockade or sanctions regime.</summary>
        public bool BuildReserves(Data.TradeFocus resource)
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            float cost = FiscalSystem.ReserveOrderPoints * FiscalSystem.ReserveCostPerPoint;
            if (State.PlayerCountry.resources.treasury < cost)
            {
                GameLog.Warn("FISCAL", $"The treasury cannot cover a reserve order ({cost:F0}).");
                return false;
            }
            if (!Turns.SpendCommandPoints(FiscalSystem.ReservesCost, "Build strategic reserves")) return false;

            bool ok = FiscalSystem.BuildReservesBy(State, State.playerCountryId, resource);
            if (ok)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 10, "Reserves built");
            }
            SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>Spend the buffer now rather than holding it.</summary>
        public bool ReleaseReserves(Data.TradeFocus resource)
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            if (!Turns.SpendCommandPoints(FiscalSystem.ReservesCost, "Release strategic reserves")) return false;

            bool ok = FiscalSystem.ReleaseReservesBy(State, State.playerCountryId, resource);
            if (ok)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 8, "Reserves released");
            }
            SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>Write the debt down. Remembered for five years.</summary>
        public bool RestructureDebt()
        {
            if (!MayCommand(Data.Pillar.Economy)) return false;
            if (State.PlayerCountry.fiscal.sovereignDebt <= 0f) return false;
            if (!GovernmentSystem.SpendPoliticalCapital(State, FiscalSystem.RestructureCost, "Restructure debt"))
                return false;

            // The operator boundary is the only place authorship is asserted:
            // the generic verb serves the AI and the finance ministry too.
            bool ok = FiscalSystem.RestructureDebtBy(State, State.playerCountryId,
                Data.CausalCategory.PlayerDecision, nameof(RestructureDebt));
            if (ok)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 16, "Debt restructured");
            }
            SaveSystem.Save(State, AutosaveSlot);
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

        /// <summary>
        /// Set the service a question (spec 03 §10). Months of work, and the
        /// answer comes back with a grade — and can be wrong.
        /// </summary>
        public bool CommissionEstimate(string targetId, Data.EstimateQuestion question)
        {
            if (!MayCommand(Data.Pillar.Intelligence)) return false;
            if (!IntelProductSystem.CanCommission(State, State.playerCountryId, targetId,
                    question, out string reason))
            {
                GameLog.Warn("INTEL", reason);
                return false;
            }
            if (!Turns.SpendCommandPoints(IntelProductSystem.CommissionCost,
                    $"Commission {question} assessment")) return false;

            var product = IntelProductSystem.CommissionBy(
                State, State.playerCountryId, targetId, question);
            if (product != null)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 10, "Assessment commissioned");
            }
            SaveSystem.Save(State, AutosaveSlot);
            return product != null;
        }

        /// <summary>Hunt for a foreign service inside our own (spec 03 §7c).</summary>
        public bool MoleHunt()
        {
            if (!MayCommand(Data.Pillar.Intelligence)) return false;
            if (!Turns.SpendCommandPoints(IntelligenceSystem.MoleHuntCost, "Mole hunt")) return false;

            bool found = IntelligenceSystem.MoleHuntBy(State, State.playerCountryId);
            ProgressionSystem.RecordInitiative(State);
            ProgressionSystem.AwardXP(State, found ? 18 : 6, "Mole hunt");
            SaveSystem.Save(State, AutosaveSlot);   // CP was spent either way
            return found;
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

        /// <summary>
        /// Bargain with a **named bloc** (spec 05 §2g). Knowing who you are
        /// talking to is worth more than an undirected approach.
        /// </summary>
        public bool CourtFaction(Data.OppositionTheme bloc)
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            bool ok = GovernmentSystem.BuildPoliticalSupportBy(State, State.playerCountryId, bloc);
            if (ok)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 10, "Bloc courted");
                SaveSystem.Save(State, AutosaveSlot);
            }
            return ok;
        }

        public bool BuildPoliticalSupport()
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            bool ok = GovernmentSystem.BuildPoliticalSupport(State);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Shut the border to arrivals, or open it again (GDD §12, §27).
        ///
        /// A national posture with a standing price on both settings, not a
        /// filter: closing it does not make the pressure go away, it leaves it on
        /// the other side of the line — and every state still carrying that
        /// crisis notices who stopped carrying it.
        /// </summary>
        public bool SetBorderPolicy(bool closed)
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            bool ok = DisplacementSystem.SetBorderPolicy(State, closed);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Give ground to the opposition (GDD §13). Always works; the cost is
        /// chosen to match what was conceded.
        /// </summary>
        public bool ConcedeToOpposition()
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            bool ok = OppositionSystem.Concede(State);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Take the opposition on in public. Effective against a case made of
        /// mood; counter-productive against one made of facts.
        /// </summary>
        public bool ConfrontOpposition()
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            bool ok = OppositionSystem.Confront(State);
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
        /// <summary>
        /// Begin rewriting what this state is (spec 05 §2f). Thirty months, paid
        /// for every one of them, and genuinely losable.
        /// </summary>
        public bool BeginConstitutionalChange(Data.GovernmentType target)
        {
            if (!MayCommand(Data.Pillar.Government)) return false;
            if (!GovernmentSystem.CanChangeConstitution(State, State.playerCountryId, target,
                    out string reason))
            {
                GameLog.Warn("GOV", reason);
                return false;
            }

            bool ok = GovernmentSystem.BeginConstitutionalChangeBy(
                State, State.playerCountryId, target);
            if (ok)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 30, "Constitutional process opened");
            }
            SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

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

        public bool DeepenTreaty(string partnerId, Data.TreatyCommitment commitment)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            bool ok = DiplomacySystem.DeepenTreaty(State, Turns, partnerId,
                new System.Collections.Generic.List<Data.TreatyCommitment> { commitment });
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        public bool SeekSanctionsRelief(string senderId)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            bool ok = EconomySystem.SeekSanctionsRelief(State, Turns, senderId);
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

        /// <summary>
        /// Admit a breakaway state exists (spec 04 §5b). Buys a grateful new
        /// state and an angry old one.
        /// </summary>
        public bool RecogniseState(string successorId)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            if (!DiplomacySystem.CanRecognise(State, State.playerCountryId, successorId,
                    out string reason))
            {
                GameLog.Warn("DIPLO", reason);
                return false;
            }
            if (!Turns.SpendCommandPoints(DiplomacySystem.RecogniseCost, "Recognise a state"))
                return false;

            bool ok = DiplomacySystem.RecogniseBy(State, State.playerCountryId, successorId);
            if (ok)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 14, "State recognised");
            }
            SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Offer to mediate a war we are not in (spec 04 §5c). Failing is public
        /// and costs standing with both sides.
        /// </summary>
        public bool OfferMediation(Data.Confrontation confrontation)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            if (!DiplomacySystem.CanMediate(State, State.playerCountryId, confrontation,
                    out string reason))
            {
                GameLog.Warn("DIPLO", reason);
                return false;
            }
            if (!Turns.SpendCommandPoints(DiplomacySystem.MediationCost, "Offer mediation"))
                return false;

            bool settled = DiplomacySystem.OfferMediationBy(State, State.playerCountryId, confrontation);
            ProgressionSystem.RecordInitiative(State);
            ProgressionSystem.AwardXP(State, settled ? 26 : 8, "Mediation offered");
            SaveSystem.Save(State, AutosaveSlot);   // CP was spent either way
            return settled;
        }

        /// <summary>
        /// Put a war behind us (spec 04 §5d). The one verb that reduces
        /// historical memory — unpopular with the people who fought it.
        /// </summary>
        public bool BeginNormalisation(string partnerId)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            if (!DiplomacySystem.CanNormalise(State, State.playerCountryId, partnerId,
                    out string reason))
            {
                GameLog.Warn("DIPLO", reason);
                return false;
            }
            if (!Turns.SpendCommandPoints(DiplomacySystem.NormalisationCost, "Normalise relations"))
                return false;

            bool ok = DiplomacySystem.BeginNormalisationBy(State, State.playerCountryId, partnerId);
            if (ok)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 16, "Relations normalised");
            }
            SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Post the foreign minister to one capital, or recall them by passing
        /// an empty id (spec 04 §5e). One posting at a time.
        /// </summary>
        public bool AssignEnvoy(string postingId)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;

            var official = State.PlayerCountry?.FindOfficial(Data.Pillar.Diplomacy);
            if (official == null)
            {
                GameLog.Warn("DIPLO", "There is no foreign minister to post.");
                return false;
            }
            if (official.envoyToCountryId == (postingId ?? "")) return false;

            // Influence, not Command Points: this is the operator asking an
            // official to do something they would not have chosen, which is
            // exactly what Influence prices (spec 15).
            if (State.influence < 1)
            {
                GameLog.Warn("DIPLO", "No Influence to spend on the posting.");
                return false;
            }
            State.influence--;

            bool ok = DiplomacySystem.AssignEnvoyBy(State, State.playerCountryId, postingId);
            if (ok)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 8, "Envoy posted");
            }
            SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Announce a summit (spec 04 §5g). Four months of preparation, and it
        /// is judged on the relationship as it stands when it meets.
        /// </summary>
        public bool ConveneSummit(string partnerId)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            if (!DiplomacySystem.CanConveneSummit(State, State.playerCountryId, partnerId,
                    out string reason))
            {
                GameLog.Warn("DIPLO", reason);
                return false;
            }
            if (!Turns.SpendCommandPoints(DiplomacySystem.SummitCost, "Convene a summit"))
                return false;

            bool ok = DiplomacySystem.ConveneSummitBy(State, State.playerCountryId, partnerId);
            if (ok)
            {
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 14, "Summit convened");
            }
            SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>
        /// Offer a supply guarantee they need for one commitment they carry
        /// (spec 04 §5h). Invalid offers spend nothing; a declined one is a
        /// spent attempt, exactly like a treaty proposal.
        /// </summary>
        public bool OfferSupplyForCommitment(string targetId, Data.TradeFocus focus, Data.TreatyCommitment commitment)
        {
            if (!MayCommand(Data.Pillar.Diplomacy)) return false;
            if (!DiplomaticLeverage.CanOffer(State, State.playerCountryId, targetId, focus, commitment,
                    out string reason))
            {
                GameLog.Warn("DIPLO", reason);
                return false;
            }
            if (!Turns.SpendCommandPoints(DiplomaticLeverage.OfferCost, "Supply-for-commitment offer"))
                return false;

            bool ok = DiplomaticLeverage.OfferBy(State, State.playerCountryId, targetId, focus, commitment);
            if (ok)
            {
                // The treaty paths award the treaty's own XP; this is the
                // exchange itself, recorded once (spec 07).
                ProgressionSystem.RecordInitiative(State);
                ProgressionSystem.AwardXP(State, 12, "Leverage exchange concluded");
            }
            SaveSystem.Save(State, AutosaveSlot);   // CP was spent either way
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

        /// <summary>
        /// Hand occupied ground back to the state it was taken from (GDD §16).
        ///
        /// The exit a post-war occupation never had. Note what is *not* here:
        /// nothing calls this on the operator's behalf. A garrison the player
        /// chose to leave in place stays there, however much it costs — the
        /// government the AI runs makes this decision for itself, and the one
        /// the operator runs does not.
        /// </summary>
        public bool RelinquishLocation(string locationId)
        {
            if (!MayCommand(Data.Pillar.Military)) return false;

            if (!TerritorySystem.CanRelinquish(State, State.playerCountryId, locationId,
                    out string reason))
            {
                GameLog.Warn("TERRITORY", reason);
                return false;
            }

            if (!Turns.SpendCommandPoints(RelinquishCost, "Relinquish occupied ground"))
                return false;

            bool ok = TerritorySystem.RelinquishBy(State, State.playerCountryId, locationId);
            if (ok) ProgressionSystem.RecordInitiative(State);
            SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>Command points to give ground up. Ordering a withdrawal is cheap; deciding to is not.</summary>
        public const int RelinquishCost = 1;

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

        public bool SetStandingOrder(string confrontationId, bool active)
        {
            if (!IsRunning || (active && !MayCommand(Data.Pillar.Military))) return false;
            bool ok = OperationPlanningSystem.SetStandingOrder(State, confrontationId, active);
            if (ok) SaveSystem.Save(State, AutosaveSlot);
            return ok;
        }

        /// <summary>Put a specific set of terms to the other side (GDD §26).</summary>
        public bool ProposeTerms(Data.PeaceProposal proposal)
        {
            if (!IsRunning || State.ActiveConfrontation == null) return false;
            bool ok = PeaceSystem.ProposeTerms(State, State.ActiveConfrontation,
                State.playerCountryId, proposal, CausalCategory.PlayerDecision,
                nameof(ProposeTerms));
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
