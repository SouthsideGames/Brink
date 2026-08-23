using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Military command (GDD Phase 4, §19): force structure with readiness and
    /// supply, the strategic map, and the confrontation console — escalation,
    /// objective-based operations with a delegated directive, and negotiated
    /// settlement.
    /// </summary>
    public class MilitaryView : TerminalView
    {
        public override string Id => "MILITARY";
        public override string ShortCode => "MIL";

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        readonly OperationDirective directive = new OperationDirective();
        string selectedLocationId;
        string defensiveLocationId;
        string inventorySubjectId;
        AssetKind selectedAsset = AssetKind.Fighters;
        OperationType selectedOperation = OperationType.Assault;
        OperationDomain selectedDomain = OperationDomain.Ground;
        string selectedPartnerId;
        ExerciseScale selectedScale = ExerciseScale.Standard;
        ExerciseFocus selectedFocus = ExerciseFocus.Combined;
        MilitarySystem.ProgramScale selectedProgramScale = MilitarySystem.ProgramScale.Major;
        readonly System.Collections.Generic.List<PeaceTerm> draftTerms = new System.Collections.Generic.List<PeaceTerm>();

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;
            var player = state.PlayerCountry;
            if (player == null) return;

            Root.Clear();
            AddAuthorityBadge(state, Pillar.Military);

            BuildForceStructure(state, player);
            BuildStrategicMap(state);

            BuildExercises(state);

            // Before the confrontation console, and outside it. Fortifying a
            // position, pacifying occupied ground, escorting our own shipping and
            // building the shield are peacetime national work — reachable only
            // during a war, they were reachable only once it was too late for any
            // of them to matter.
            BuildDefensiveProgrammes(state, player);

            // A state can be committed on more than one front now, so the console
            // commands *a* war rather than *the* war, and the operator picks
            // which. Opening controls stay available alongside it, because taking
            // on a second front is a decision the game should let you consider
            // while you are already in one.
            var active = state.ActiveConfrontationsFor(state.playerCountryId);
            if (active.Count > 1) BuildFrontSelector(state, active);

            var confrontation = state.ActiveConfrontation;
            if (confrontation != null) BuildConfrontationConsole(state, confrontation);
            if (ConfrontationSystem.CanOpenAnother(state, state.playerCountryId, out _))
                BuildOpeningControls(state);
            else if (confrontation != null)
                AddText("sig-hostile").text =
                    "\n THE FORCE IS COMMITTED TO ITS LIMIT. Settle or wind down a front " +
                    "before opening another.";
        }

        /// <summary>
        /// Which war the operator is currently commanding (GDD §16).
        ///
        /// Only shown when there is more than one, because a selector over a
        /// single item is noise. Each front is named by its theatre, which is
        /// what makes two simultaneous wars legible as different things rather
        /// than as one confusing screen.
        /// </summary>
        void BuildFrontSelector(GameState state, System.Collections.Generic.List<Confrontation> active)
        {
            AddText("terminal-text-bright").text =
                AsciiChart.BoxHeader("ACTIVE FRONTS", W);

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

            var commanding = state.ActiveConfrontation;
            foreach (var front in active)
            {
                var captured = front;
                bool current = commanding != null && commanding.id == front.id;
                var opponent = state.FindCountry(front.OpponentOf(state.playerCountryId));

                var button = new Button(() =>
                {
                    state.commandingConfrontationId = captured.id;
                    Refresh();
                })
                {
                    text = (current ? "► " : "")
                           + $"{TheatreSystem.Name(front.theatre)} — {opponent?.displayName?.ToUpperInvariant()}"
                };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                row.Add(button);
            }

            // What being in two places at once actually costs.
            float commitment = TheatreSystem.TotalCommitment(state, state.playerCountryId);
            AddText(commitment >= TheatreSystem.OverstretchThreshold ? "sig-hostile" : "terminal-text-dim")
                .text = $"   COMMITMENT {commitment:F1} of {ConfrontationSystem.MaxCommitment:F1}" +
                        (commitment >= TheatreSystem.OverstretchThreshold
                            ? "   OVERSTRETCHED — the world can see it."
                            : "");
        }

        /// <summary>
        /// Defensive programmes on ground we hold (GDD §19). Always available:
        /// they need no confrontation, do not escalate one, and pay no
        /// abruptness surcharge.
        /// </summary>
        void BuildDefensiveProgrammes(GameState state, CountryState player)
        {
            var ours = new System.Collections.Generic.List<StrategicLocation>();
            foreach (var location in state.locations)
                if (location.ownerId == state.playerCountryId) ours.Add(location);
            if (ours.Count == 0) return;

            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("DEFENSIVE PROGRAMMES", W);

            if (defensiveLocationId == null || state.FindLocation(defensiveLocationId) == null
                || state.FindLocation(defensiveLocationId).ownerId != state.playerCountryId)
                defensiveLocationId = ours[0].id;

            var siteRow = new VisualElement();
            siteRow.AddToClassList("button-row");
            Root.Add(siteRow);
            foreach (var location in ours)
            {
                var captured = location;
                bool current = defensiveLocationId == location.id;
                var button = new Button(() => { defensiveLocationId = captured.id; Refresh(); })
                { text = (current ? "► " : "") + location.displayName.ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                siteRow.Add(button);
            }

            var site = state.FindLocation(defensiveLocationId);
            AddText("terminal-text-dim").text =
                $"   DEFENCE {site.defenseValue:F0}   GARRISON {site.garrison:F0}" +
                (site.IsOccupied ? $"   PACIFICATION {site.pacification:F0}   (OCCUPIED)" : "") +
                $"   OUR SHIELD {player.military.missileDefense:F0}";

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

            foreach (var profile in OperationCatalog.All)
            {
                if (profile.targeting != OperationTargeting.OwnGround) continue;

                var captured = profile.type;
                bool possible = OperationCatalog.CanOrder(
                    state, state.playerCountryId, site, captured, out string blocked);

                int cost = ConfrontationSystem.OperationCostFor(state, null, captured);
                var button = new Button(() =>
                {
                    GameController.Instance.LaunchDefensiveProgramme(defensiveLocationId, captured);
                    Refresh();
                })
                { text = $"{profile.displayName} [{cost} CP]" };
                button.AddToClassList("cmd-button");
                if (!possible)
                {
                    button.AddToClassList("terminal-text-dim");
                    button.tooltip = blocked;
                }
                button.SetEnabled(possible);
                row.Add(button);
            }

            AddText("terminal-text-dim").text =
                "   Peacetime work. None of it escalates a standoff, and none of it is " +
                "surcharged as an act of war.";
        }

        void BuildForceStructure(GameState state, CountryState player)
        {
            var mil = player.military;
            var text = AddText("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("FORCE STRUCTURE", W));
            sb.AppendLine($" POSTURE:  {mil.posture.ToString().ToUpperInvariant()}   " +
                          $"UPKEEP {MilitarySystem.PostureUpkeep(mil.posture, false):F0}/MO");
            sb.AppendLine($" DOCTRINE: {mil.doctrine.ToString().ToUpperInvariant()}");
            sb.AppendLine($" DEPLOYABLE POWER: {mil.TotalPower:F1}");
            sb.AppendLine("  " + AsciiChart.LabeledBar("LOGISTICS", mil.logistics, 100, 11, 18));
            sb.AppendLine();
            AppendBranch(sb, "GROUND", mil.ground);
            AppendBranch(sb, "AIR", mil.air);
            AppendBranch(sb, "NAVAL", mil.naval);
            text.text = sb.ToString();

            BuildInventory(state, player);
            BuildStandingDecisions(state, player);
        }

        /// <summary>
        /// What the force actually consists of (GDD §19, amended).
        ///
        /// "Air strength 68" is not a fact an operator can reason about — it has
        /// no units and no comparison class. "912 fighters against their 340" is.
        /// Our own counts are exact; a foreign force is only ever a band from
        /// collection, which is what makes buying intelligence before buying a
        /// war a real decision.
        /// </summary>
        void BuildInventory(GameState state, CountryState player)
        {
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("ORDER OF BATTLE", W);

            // Whose force we are looking at. Ourselves by default; any state we
            // have an opinion about otherwise.
            var row = MakeRow();
            var subjects = new System.Collections.Generic.List<CountryState> { player };
            foreach (var country in state.countries)
                if (country.id != player.id) subjects.Add(country);

            if (string.IsNullOrEmpty(inventorySubjectId)) inventorySubjectId = player.id;

            foreach (var subject in subjects)
            {
                var captured = subject;
                bool current = inventorySubjectId == subject.id;
                var button = new Button(() => { inventorySubjectId = captured.id; Refresh(); })
                { text = (current ? "► " : "") + subject.displayName.ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                row.Add(button);
            }

            var viewing = state.FindCountry(inventorySubjectId) ?? player;
            bool ours = viewing.id == player.id;

            var sb = new StringBuilder();
            foreach (ForceBranch branch in System.Enum.GetValues(typeof(ForceBranch)))
            {
                var force = viewing.military.Get(branch);
                sb.AppendLine();
                sb.AppendLine($" {branch.ToString().ToUpperInvariant()}"
                              + (ours ? $"   STRENGTH {force.strength:F0}" : ""));

                foreach (var asset in AssetCatalog.InBranch(branch))
                {
                    string value = ours
                        ? AssetCatalog.Format(force.inventory.CountOf(asset.kind))
                        : IntelReadout.ForeignAssetCount(state, viewing.id, asset.kind);

                    string ordered = "";
                    if (ours)
                    {
                        float onOrder = force.inventory.OnOrderOf(asset.kind);
                        if (onOrder > 0.5f) ordered = $"   (+{AssetCatalog.Format(onOrder)} ON ORDER)";
                    }

                    sb.AppendLine($"   {AsciiChart.Cell(asset.label, 14)} {value,-18}{ordered}");
                }
            }

            // A figure, not prose: the columns are built to an exact grid and
            // wrapping would break the alignment that makes it readable.
            AddFigure(ours ? "terminal-text" : "terminal-text-dim").text = sb.ToString();

            if (!ours)
                AddText("terminal-text-dim").text =
                    "   Foreign figures are estimates. Better collection narrows the band; it never " +
                    "produces the true number.";
        }

        /// <summary>
        /// Ordering equipment, and the budget that decides how fast it arrives
        /// (GDD §19, §20).
        /// </summary>
        void BuildAcquisition(GameState state, CountryState player)
        {
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("ACQUISITION", W);

            // War footing first: it changes what every order below is worth.
            var mil = player.military;
            var footingRow = MakeRow();

            if (mil.warFooting)
            {
                AddButton(footingRow, "END WAR FOOTING", "danger",
                    () => { GameController.Instance.SetWarFooting(false); Refresh(); });
                AddText("sig-ally").text =
                    $"   ON A WAR FOOTING — deliveries at {AcquisitionSystem.WarFootingSpeed:F1}×, " +
                    $"costing {AcquisitionSystem.WarFootingUpkeep:F1} PC every month.";
            }
            else
            {
                bool allowed = AcquisitionSystem.CanDeclareWarFooting(
                    state, player.id, out string blocked);
                var button = new Button(() => { GameController.Instance.SetWarFooting(true); Refresh(); })
                { text = $"DECLARE WAR FOOTING [{AcquisitionSystem.WarFootingCost:F0} PC]" };
                button.AddToClassList("cmd-button");
                button.AddToClassList("danger");
                button.SetEnabled(allowed);
                footingRow.Add(button);

                if (!allowed) AddText("terminal-text-dim").text = $"   {blocked}.";
                else AddText("terminal-text-dim").text =
                    "   Moves money to the military. Roughly doubles delivery tempo, and the " +
                    "chamber will want it back.";
            }

            // What to buy.
            var kindRow = MakeRow();
            foreach (var asset in AssetCatalog.All)
            {
                var captured = asset.kind;
                bool current = selectedAsset == asset.kind;
                var button = new Button(() => { selectedAsset = captured; Refresh(); })
                { text = (current ? "► " : "") + asset.label };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                kindRow.Add(button);
            }

            var chosen = AssetCatalog.For(selectedAsset);
            if (chosen == null) return;

            float order = chosen.orderIncrement;
            float cost = AssetCatalog.CostOf(selectedAsset, order);
            float rate = AcquisitionSystem.DeliveryRateFor(player, selectedAsset);
            float months = rate > 0.001f ? 1f / rate : chosen.leadMonths;

            AddText("terminal-text-dim").text =
                $"   {chosen.displayName} — {AssetCatalog.Format(order)} per order, "
                + $"{cost:F0} treasury, roughly {months:F0} months to deliver."
                + (player.resources.treasury < cost ? "   TREASURY CANNOT COVER IT" : "");

            var orderRow = MakeRow();
            var place = new Button(() =>
            {
                GameController.Instance.OrderAssets(selectedAsset, order);
                Refresh();
            })
            { text = $"ORDER {AssetCatalog.Format(order)} {chosen.label} [{AcquisitionSystem.OrderCost} CP]" };
            place.AddToClassList("cmd-button");
            place.AddToClassList("primary");
            place.SetEnabled(player.resources.treasury >= cost);
            orderRow.Add(place);

            var bulk = new Button(() =>
            {
                GameController.Instance.OrderAssets(selectedAsset, order * 4f);
                Refresh();
            })
            { text = $"ORDER {AssetCatalog.Format(order * 4f)} [{AcquisitionSystem.OrderCost} CP]" };
            bulk.AddToClassList("cmd-button");
            bulk.SetEnabled(player.resources.treasury >= cost * 4f);
            orderRow.Add(bulk);
        }

        /// <summary>The peacetime work of a military operator (GDD §19).</summary>
        void BuildStandingDecisions(GameState state, CountryState player)
        {
            var mil = player.military;

            BuildAcquisition(state, player);

            AddText().text = " POSTURE — held readiness against standing cost";
            var postureRow = MakeRow();
            foreach (MilitaryPosture posture in System.Enum.GetValues(typeof(MilitaryPosture)))
            {
                var captured = posture;
                bool current = mil.posture == posture;
                bool allowed = MilitarySystem.CanSetPosture(state, posture, out string blocked);
                int cost = MilitarySystem.PostureCost(posture);
                var button = new Button(() => { GameController.Instance.SetPosture(captured); Refresh(); })
                {
                    text = (current ? "► " : "") + posture.ToString().ToUpperInvariant() +
                           (current || cost == 0 ? "" : $" [{cost} CP]")
                };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                if (posture == MilitaryPosture.Forward && !current) button.AddToClassList("danger");
                button.SetEnabled(!current && allowed);
                postureRow.Add(button);

                // Say why it is locked rather than presenting a dead control.
                if (!current && !allowed)
                    AddText("terminal-text-dim").text = $"   {blocked}";
            }

            AddText().text = " DOCTRINE — how the force fights, not how strong it is";
            var doctrineRow = MakeRow();
            foreach (MilitaryDoctrine doctrine in System.Enum.GetValues(typeof(MilitaryDoctrine)))
            {
                var captured = doctrine;
                bool current = mil.doctrine == doctrine;
                var button = new Button(() => { GameController.Instance.SetDoctrine(captured); Refresh(); })
                {
                    text = (current ? "► " : "") + doctrine.ToString().ToUpperInvariant() +
                           (current ? "" : $" [{MilitarySystem.DoctrineCost} CP]")
                };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                button.SetEnabled(!current);
                doctrineRow.Add(button);
            }
            AddText("terminal-text-dim").text = DoctrineDescription(mil.doctrine);

            // Procurement.
            var procurement = AddText();
            var psb = new StringBuilder();
            psb.AppendLine($" PROCUREMENT — {mil.programs.Count}/{MilitarySystem.MaxPrograms} programs running");
            foreach (var program in mil.programs)
                psb.AppendLine($"   {program.label.ToUpperInvariant()}: {program.monthsRemaining} MO REMAINING " +
                               $"AT {program.costPerMonth:F0}/MO");
            if (mil.programs.Count == 0)
                psb.AppendLine("   Force structure is built over years, and paid for every month.");
            procurement.text = psb.ToString();

            // Scale is the operator's choice, not a hardcoded Major. Transformative
            // is gated on SkillEffect.StrategicIndustry — without a button for it,
            // a tier-4 skill point bought something unreachable.
            AddText("terminal-text-dim").text = $"   SCALE: {selectedProgramScale.ToString().ToUpperInvariant()}";
            var scaleRow = MakeRow();
            foreach (MilitarySystem.ProgramScale scale in System.Enum.GetValues(typeof(MilitarySystem.ProgramScale)))
            {
                var captured = scale;
                bool current = selectedProgramScale == scale;
                bool locked = scale == MilitarySystem.ProgramScale.Transformative
                              && ProgressionSystem.EffectValue(state, SkillEffect.StrategicIndustry) <= 0f;

                var button = new Button(() => { selectedProgramScale = captured; Refresh(); })
                { text = (current ? "► " : "") + scale.ToString().ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                button.SetEnabled(!locked);
                scaleRow.Add(button);

                if (locked)
                    AddText("terminal-text-dim").text =
                        "   TRANSFORMATIVE: no yard or line can absorb a programme that size " +
                        "— requires Strategic Industry.";
            }

            var programRow = MakeRow();
            foreach (ForceBranch branch in System.Enum.GetValues(typeof(ForceBranch)))
            {
                var captured = branch;
                var scale = selectedProgramScale;
                AddButton(programRow,
                    $"{branch.ToString().ToUpperInvariant()} PROGRAM [{MilitarySystem.ProgramCpCost(scale)} CP]",
                    null, () =>
                {
                    GameController.Instance.BeginProcurement(captured, scale);
                    Refresh();
                });
            }
            AddButton(programRow, $"LOGISTICS [{MilitarySystem.LogisticsInvestmentCost} CP]", null, () =>
            {
                GameController.Instance.InvestInLogistics();
                Refresh();
            });
        }

        static string DoctrineDescription(MilitaryDoctrine doctrine)
        {
            switch (doctrine)
            {
                case MilitaryDoctrine.Maneuver:
                    return "  Tempo and penetration. Fewer of our own losses; more collateral harm.";
                case MilitaryDoctrine.Attrition:
                    return "  Grinding pressure. Heavier losses on both sides; the enemy feels it more.";
                case MilitaryDoctrine.Deterrence:
                    return "  Built to threaten rather than to attack. Weaker in the assault, but our\n" +
                           "  demands are taken more seriously at the table.";
                default:
                    return "  No pronounced emphasis. Competent everywhere, decisive nowhere.";
            }
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);
            return row;
        }

        static void AppendBranch(StringBuilder sb, string label, BranchForce force)
        {
            sb.AppendLine($" {label}  (effective power {force.EffectivePower:F1})");
            sb.AppendLine("   " + AsciiChart.LabeledBar("STRENGTH", force.strength, 100, 10, 18));
            sb.AppendLine("   " + AsciiChart.LabeledBar("READINESS", force.readiness, 100, 10, 18));
            sb.AppendLine("   " + AsciiChart.LabeledBar("SUPPLY", force.supply, 100, 10, 18));
            sb.AppendLine();
        }

        void BuildStrategicMap(GameState state)
        {
            var text = AddText();
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("STRATEGIC LOCATIONS", W));
            foreach (var loc in state.locations)
            {
                var owner = state.FindCountry(loc.ownerId);
                bool ours = loc.ownerId == state.playerCountryId;
                string marker = ours ? "►" : " ";
                string occupied = loc.IsOccupied ? " *OCCUPIED*" : "";

                // Enemy garrisons are an intelligence problem, not a fact (GDD §14).
                string garrison = ours ? $"GAR {loc.garrison,4:F0}" : EstimatedGarrison(state, loc);
                sb.AppendLine($" {marker}[{loc.TypeCode}] {AsciiChart.Cell(loc.displayName, AsciiChart.NameWidth(W, 0.30f))} {AsciiChart.Cell(owner?.displayName, AsciiChart.NameWidth(W, 0.22f))} " +
                              $"DEF {loc.defenseValue,4:F0}  {garrison}{occupied}");
            }
            text.text = sb.ToString();
        }

        /// <summary>
        /// How our analysts phrase a draft settlement's prospects. Political
        /// collection on the opponent is what buys a sharp answer.
        /// </summary>
        static string OutlookText(SettlementOutlook outlook)
        {
            switch (outlook)
            {
                case SettlementOutlook.NoTerms: return "NO TERMS ON THE TABLE.";
                case SettlementOutlook.Likely: return "THEY WOULD LIKELY SIGN THIS.";
                case SettlementOutlook.Unlikely: return "THEY WOULD ALMOST CERTAINLY REFUSE.";
                case SettlementOutlook.Uncertain: return "TOO CLOSE TO CALL ON WHAT WE HOLD.";
                default:
                    return "NO READ ON THEIR POLITICS — WE WOULD BE GUESSING.";
            }
        }

        /// <summary>
        /// Foreign garrison strength reported as a band whose width reflects the
        /// confidence of our military collection against that owner.
        /// </summary>
        static string EstimatedGarrison(GameState state, StrategicLocation loc)
        {
            if (!IntelligenceSystem.TryEstimateGarrison(state, state.playerCountryId, loc,
                    out float low, out float high, out var confidence))
                return "GAR  ???";

            if (loc.ownerId == state.playerCountryId) return $"GAR {low:F0}";
            return $"GAR ~{low:F0}-{high:F0} ({confidence.ToString().Substring(0, 3).ToUpperInvariant()})";
        }

        /// <summary>
        /// Joint exercises (GDD §15.3): training and trust bought with exposure.
        /// </summary>
        void BuildExercises(GameState state)
        {
            var header = AddText("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("JOINT EXERCISES", W));

            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var relationship = state.FindRelationship(state.playerCountryId, country.id);
                if (relationship == null) continue;
                bool eligible = ExerciseSystem.CanExerciseWith(state, country.id, out string reason);
                sb.AppendLine($" {country.displayName.ToUpperInvariant(),-16} " +
                              $"INTEROP {relationship.interoperability,5:F0}  " +
                              $"THEIR READ ON US {relationship.doctrineFamiliarity,5:F0}   " +
                              (eligible ? "AVAILABLE" : reason.ToUpperInvariant()));
            }
            header.text = sb.ToString();

            if (selectedPartnerId == null)
                foreach (var country in state.countries)
                    if (!country.isPlayer) { selectedPartnerId = country.id; break; }

            var partnerRow = new VisualElement();
            partnerRow.AddToClassList("button-row");
            Root.Add(partnerRow);
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var captured = country;
                bool current = selectedPartnerId == country.id;
                var button = new Button(() => { selectedPartnerId = captured.id; Refresh(); })
                { text = (current ? "► " : "") + country.displayName.ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                partnerRow.Add(button);
            }

            var settingsRow = new VisualElement();
            settingsRow.AddToClassList("button-row");
            Root.Add(settingsRow);
            foreach (ExerciseScale scale in System.Enum.GetValues(typeof(ExerciseScale)))
            {
                var captured = scale;
                bool current = selectedScale == scale;
                var button = new Button(() => { selectedScale = captured; Refresh(); })
                { text = (current ? "► " : "") + scale.ToString().ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                settingsRow.Add(button);
            }
            foreach (ExerciseFocus focus in System.Enum.GetValues(typeof(ExerciseFocus)))
            {
                var captured = focus;
                bool current = selectedFocus == focus;
                var button = new Button(() => { selectedFocus = captured; Refresh(); })
                { text = (current ? "► " : "") + focus.ToString().ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                settingsRow.Add(button);
            }

            var runRow = new VisualElement();
            runRow.AddToClassList("button-row");
            Root.Add(runRow);
            bool canRun = ExerciseSystem.CanExerciseWith(state, selectedPartnerId, out _);
            var run = new Button(() =>
            {
                GameController.Instance.ConductExercise(selectedPartnerId, selectedScale, selectedFocus);
                Refresh();
            })
            { text = $"CONDUCT EXERCISE [{ExerciseSystem.CostFor(selectedScale)} CP]" };
            run.AddToClassList("cmd-button");
            run.AddToClassList("primary");
            run.SetEnabled(canRun);
            runRow.Add(run);

            var hint = AddText("terminal-text-dim");
            var hintText = new StringBuilder();
            hintText.AppendLine("  Deeper participation trains harder and builds more trust, but shows");
            hintText.AppendLine("  the partner more of how we actually fight. They keep that knowledge.");
            if (state.exercises.Count > 0)
            {
                hintText.AppendLine("\n  RECENT EXERCISES");
                int start = System.Math.Max(0, state.exercises.Count - 4);
                for (int i = state.exercises.Count - 1; i >= start; i--)
                {
                    var record = state.exercises[i];
                    hintText.AppendLine($"   {record.date.SortKey} {record.scale} {record.focus} vs " +
                                        $"{state.FindCountry(record.partnerId)?.displayName} — " +
                                        $"{(record.weOutperformed ? "OUTPERFORMED" : "LAGGED")}");
                    hintText.AppendLine($"     {record.lesson}");
                }
            }
            hint.text = hintText.ToString();
        }

        void BuildOpeningControls(GameState state)
        {
            var text = AddText("terminal-text-dim");
            text.text = AsciiChart.BoxHeader("CONFRONTATION", W) + "\n" +
                        " NO ACTIVE CONFRONTATION.\n" +
                        " Opening a confrontation costs " + ConfrontationSystem.OpenCost + " CP and begins at TENSION.\n";

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

            var lane = state.FindLocation("CONTESTED_LANE");
            if (lane != null && lane.ownerId != state.playerCountryId)
            {
                AddButton(row, $"CONTEST {lane.displayName.ToUpperInvariant()} [{ConfrontationSystem.OpenCost} CP]", "primary", () =>
                {
                    GameController.Instance.BeginConfrontation(lane.ownerId,
                        ConfrontationObjective.TerritorialConcession, lane.id, PrimaryStrategy.Military);
                    Refresh();
                });
            }

            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var target = country;
                AddButton(row, $"DETER {country.displayName.ToUpperInvariant()} [{ConfrontationSystem.OpenCost} CP]", null, () =>
                {
                    GameController.Instance.BeginConfrontation(target.id,
                        ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
                    Refresh();
                });
            }
        }

        void BuildConfrontationConsole(GameState state, Confrontation confrontation)
        {
            var opponent = state.FindCountry(confrontation.OpponentOf(state.playerCountryId));
            bool playerIsInitiator = state.playerCountryId == confrontation.initiatorId;
            float ourExhaustion = playerIsInitiator ? confrontation.initiatorWarExhaustion : confrontation.defenderWarExhaustion;
            float theirExhaustion = playerIsInitiator ? confrontation.defenderWarExhaustion : confrontation.initiatorWarExhaustion;
            float ourMomentum = playerIsInitiator ? confrontation.momentum : -confrontation.momentum;

            var header = AddText("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader($"CONFRONTATION — {opponent?.displayName.ToUpperInvariant()}", W));
            sb.AppendLine($" OBJECTIVE:       {ConfrontationSystem.ObjectiveText(state, confrontation)}");
            sb.AppendLine($" PRIMARY STRATEGY:{confrontation.primaryStrategy}");
            sb.AppendLine($" ESCALATION:      {EscalationBar(confrontation.escalation)}");
            sb.AppendLine($" MONTHS ACTIVE:   {confrontation.monthsActive}");
            sb.AppendLine($" MOMENTUM:        {ourMomentum,+6:F1}");
            sb.AppendLine($" OUR EXHAUSTION:  {ourExhaustion,6:F1}    THEIR EXHAUSTION: {theirExhaustion,6:F1}");
            sb.AppendLine($" CIVILIAN HARM:   {confrontation.civilianHarmTotal,6:F1}");
            sb.AppendLine($" OPPONENT POSTURE:{(ConfrontationSystem.OpponentWouldAccept(state, confrontation) ? " OPEN TO TERMS" : " RESISTING")}");
            header.text = sb.ToString();

            // Strategic Pivot (GDD §18.2): the approach can be changed, at a
            // price. Committing to a domain is what concentrates its pressure on
            // the opponent, so this is a real decision rather than a label.
            AddText("terminal-text-dim").text =
                $" PIVOT — change the approach. Costs {ConfrontationSystem.PivotCost} CP and the\n" +
                " momentum already built. Effort in the old domain does not carry over.";
            var pivotRow = MakeRow();
            foreach (PrimaryStrategy strategy in System.Enum.GetValues(typeof(PrimaryStrategy)))
            {
                var captured = strategy;
                bool current = confrontation.primaryStrategy == strategy;
                var button = new Button(() => { GameController.Instance.Pivot(captured); Refresh(); })
                {
                    text = (current ? "► " : "") + strategy.ToString().ToUpperInvariant()
                           + (current ? "" : $" [{ConfrontationSystem.PivotCost} CP]")
                };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                button.SetEnabled(!current);
                pivotRow.Add(button);
            }

            // Escalation controls.
            var escRow = new VisualElement();
            escRow.AddToClassList("button-row");
            Root.Add(escRow);
            foreach (EscalationState level in System.Enum.GetValues(typeof(EscalationState)))
            {
                var target = level;
                bool current = confrontation.escalation == level;
                var button = new Button(() => { GameController.Instance.SetEscalation(target); Refresh(); })
                { text = (current ? "► " : "") + level.ToString().ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                if (level > confrontation.escalation + 1) button.AddToClassList("danger"); // premium applies
                button.SetEnabled(!current);
                escRow.Add(button);
            }

            if (confrontation.escalation >= EscalationState.LimitedConflict)
                BuildOperationControls(state, confrontation);
            else
                AddText("terminal-text-dim").text = " Operations require LIMITED CONFLICT or higher.";

            BuildSettlementControls();
            BuildAfterAction(confrontation);
        }

        void BuildOperationControls(GameState state, Confrontation confrontation)
        {
            var targets = new System.Collections.Generic.List<StrategicLocation>();
            foreach (var loc in state.locations)
                if (loc.ownerId != state.playerCountryId && confrontation.Involves(loc.ownerId))
                    targets.Add(loc);

            // Ground we hold is a legitimate objective too, now that the list has
            // defensive verbs. Fortifying a position, pacifying occupied ground,
            // escorting our own shipping and building the shield all need a place
            // of ours to be conducted at, and before this the screen could not
            // even select one.
            foreach (var loc in state.locations)
                if (loc.ownerId == state.playerCountryId)
                    targets.Add(loc);

            if (targets.Count == 0) return;
            if (selectedLocationId == null || state.FindLocation(selectedLocationId) == null)
                selectedLocationId = targets[0].id;

            AddText("terminal-text-bright").text = "\n OPERATION PLANNING";

            // What the defence minister thinks (GDD §7.2, §8). Shown before the
            // controls, in advisory yellow, and never binding — the operator
            // still gives the order. How much it is worth depends entirely on who
            // they appointed, which is the point: a decision made months ago at
            // the appointment is now a decision they feel every month of the war.
            var advice = MilitaryAdvice.Recommend(state, confrontation);
            if (advice != null)
            {
                AddText("sig-advice").text = " " + MilitaryAdvice.Header(state, advice);

                if (advice.reliability < MilitaryAdvice.Unreliable / 100f)
                    AddText("sig-hostile").text =
                        "   This desk has been wrong before. Weigh it accordingly.";
            }
            else if (MilitaryAdvice.Minister(state) != null)
            {
                // Absence of counsel has to read as a consequence of delegating
                // rather than as a missing feature.
                AddText("terminal-text-dim").text =
                    $" {MilitaryAdvice.Minister(state).displayName} is running this pillar. Take "
                    + "DIRECT CONTROL in CABINET for their read on each order.";
            }

            var standing = MilitaryAdvice.DescribeStanding(state);
            if (!string.IsNullOrEmpty(standing))
                AddText("terminal-text-dim").text = $"   {standing}";

            var targetRow = new VisualElement();
            targetRow.AddToClassList("button-row");
            Root.Add(targetRow);
            foreach (var loc in targets)
            {
                var captured = loc;
                bool current = selectedLocationId == loc.id;
                bool advised = advice != null && advice.targetLocationId == loc.id;

                var button = new Button(() => { selectedLocationId = captured.id; Refresh(); })
                {
                    text = (current ? "► " : "") + loc.displayName.ToUpperInvariant()
                           + (advised ? "  ★" : "")
                };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");

                // The star carries the meaning and the colour reinforces it, so
                // the recommendation survives any palette and any colour vision —
                // the same rule the map's standing glyphs follow.
                if (advised) button.AddToClassList("sig-advice");
                targetRow.Add(button);
            }

            // What distance will do to the force, *before* the order is given
            // (GDD §16). A player who only learns this from a run of failed
            // operations reads it as unfair dice rather than as the map.
            var selectedTarget = state.FindLocation(selectedLocationId);
            if (selectedTarget != null)
            {
                float reach = GeographySystem.ReachFactorFor(state, state.playerCountryId, selectedTarget);
                var reachLine = AddText(reach >= 0.999f ? "terminal-text-dim" : "terminal-text-bright");
                reachLine.text = reach >= 0.999f
                    ? $"   REACH: {GeographySystem.ReachText(reach)} — force arrives at full weight."
                    : $"   REACH: {GeographySystem.ReachText(reach)} — force arrives at "
                      + $"{reach * 100f:F0}% of full weight. Basing or ground held nearer would help.";

                // Distance is only half of geography. The other half is how much
                // of the force is already busy somewhere else (GDD §16).
                float focus = TheatreSystem.FocusFactorFor(state, state.playerCountryId, selectedTarget);
                AddText(focus >= 0.999f ? "terminal-text-dim" : "sig-rival").text =
                    "   " + TheatreSystem.DescribeCommitment(
                        state, state.playerCountryId, TheatreSystem.Of(selectedTarget));
            }

            // Twenty-three verbs cannot be one flat row on a phone. Grouped by
            // the service that runs them, which is also how a staff would present
            // them, so at most seven buttons are ever on screen at once.
            var domainRow = new VisualElement();
            domainRow.AddToClassList("button-row");
            Root.Add(domainRow);
            foreach (OperationDomain domain in System.Enum.GetValues(typeof(OperationDomain)))
            {
                var capturedDomain = domain;
                bool currentDomain = selectedDomain == domain;
                var tab = new Button(() => { selectedDomain = capturedDomain; Refresh(); })
                { text = (currentDomain ? "► " : "") + Phrase.Caps(domain) };
                tab.AddToClassList("cmd-button");
                if (currentDomain) tab.AddToClassList("primary");
                domainRow.Add(tab);
            }

            // Keep the selection inside the visible domain, or the brief below
            // would describe an operation none of the buttons show as chosen.
            if (OperationCatalog.For(selectedOperation)?.domain != selectedDomain)
            {
                var first = OperationCatalog.InDomain(selectedDomain);
                if (first.Count > 0) selectedOperation = first[0].type;
            }

            var typeRow = new VisualElement();
            typeRow.AddToClassList("button-row");
            Root.Add(typeRow);
            foreach (var opProfile in OperationCatalog.InDomain(selectedDomain))
            {
                var captured = opProfile.type;
                bool current = selectedOperation == captured;
                bool possible = OperationCatalog.CanOrder(
                    state, state.playerCountryId, selectedTarget, captured, out _);

                // The price belongs on the choice, not only on the confirmation —
                // sequencing suppression before an assault is only a decision if
                // the operator can see what the sequence costs them.
                bool advisedVerb = advice != null && advice.operation == captured
                                   && advice.targetLocationId == selectedLocationId;

                var button = new Button(() => { selectedOperation = captured; Refresh(); })
                {
                    text = (current ? "► " : "") + opProfile.displayName
                           + $" [{opProfile.cpCost} CP]" + (advisedVerb ? "  ★" : "")
                };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                if (advisedVerb && possible) button.AddToClassList("sig-advice");

                // An impossible order is shown and dimmed rather than hidden. The
                // operator should be able to see that a blockade exists and that
                // we cannot run one, which is a fact about our fleet and their
                // coastline — hiding it just looks like the feature is missing.
                if (!possible) button.AddToClassList("terminal-text-dim");
                button.SetEnabled(possible);
                typeRow.Add(button);
            }

            // What this order actually is, and what it will draw on. Force
            // composition decides what is realistic — a blockade with no fleet
            // is a button that does nothing useful, and the operator should be
            // able to see that before spending on it.
            var player = state.PlayerCountry;
            float committed = MilitarySystem.OperationPower(player, selectedOperation);
            var brief = AddText("terminal-text-dim");
            brief.text =
                $"   {MilitarySystem.DescribeOperation(selectedOperation)}\n" +
                $"   OUR COMMITTED WEIGHT: {committed * MilitarySystem.PowerScale:F0}" +
                (MilitarySystem.CanTakeGround(selectedOperation)
                    ? "   CAN HOLD THE OBJECTIVE"
                    : "   CANNOT HOLD THE OBJECTIVE");

            // Why an order cannot be given. The reason matters as much as the
            // refusal: "WE HAVE NO FLEET" reads as the consequence of a
            // procurement decision, which is what it is. A greyed button with no
            // explanation reads as a bug.
            bool orderable = OperationCatalog.CanOrder(
                state, state.playerCountryId, selectedTarget, selectedOperation, out string blocked);
            if (!orderable)
                AddText("sig-hostile").text = $"   UNAVAILABLE: {blocked}.";

            // The minister's read of *this* order, not just their preferred one.
            // Deliberately their assessment rather than the true odds: the number
            // is only as good as the person who produced it, and the operator
            // should be weighing a person as much as a percentage.
            if (orderable && CabinetAdvice.ShouldAdvise(state, Pillar.Military))
            {
                float staffOdds = MilitaryAdvice.AssessOdds(
                    state, selectedTarget, selectedOperation, directive,
                    DiplomacySystem.CoalitionStrength(
                        state, confrontation, state.playerCountryId, selectedOperation));

                AddText("sig-advice").text =
                    $"   STAFF ASSESSMENT: {staffOdds * 100f:F0}% — "
                    + (advice != null && advice.operation == selectedOperation
                                      && advice.targetLocationId == selectedLocationId
                        ? "this is what they recommend."
                        : "not their recommendation.");
            }

            // What our partners are actually adding to *this* order. Coalition
            // weight was real but invisible, and it was being added to a figure
            // so small that it could not move an outcome — which is why
            // recruiting allies genuinely felt like it did nothing. Printed
            // beside our own weight so the comparison is the whole message.
            float coalition = DiplomacySystem.CoalitionStrength(
                state, confrontation, state.playerCountryId, selectedOperation);
            if (coalition > 0.01f)
                AddText("sig-ally").text =
                    $"   PARTNERS ADD: +{coalition * MilitarySystem.PowerScale:F0} " +
                    "to this operation.";

            // EXECUTE sits directly under the choice it confirms, above the
            // directive sliders rather than below them.
            //
            // It used to come last, after three sliders, on a view that now runs
            // to twenty-three verbs across four domains — so on a phone the only
            // button that actually spends anything was off the bottom of the
            // screen. The verb buttons carry a "[3 CP]" tag and *look* like they
            // spend, so pressing one and seeing nothing happen reads as "the game
            // says I can afford this and then ignores me". The sliders are
            // refinements to an order; the order itself has to be reachable.
            if (orderable)
            {
                var execRow = new VisualElement();
                execRow.AddToClassList("button-row");
                Root.Add(execRow);
                int cost = ConfrontationSystem.OperationCostFor(state, confrontation, selectedOperation);
                AddButton(execRow, $"EXECUTE {MilitarySystem.NameOf(selectedOperation)} [{cost} CP]",
                    "danger", () =>
                    {
                        GameController.Instance.LaunchOperation(
                            selectedLocationId, selectedOperation, directive);
                        Refresh();
                    });

                // Says plainly what the row above it does not: choosing is free,
                // and nothing is spent until this is pressed.
                AddText("terminal-text-dim").text =
                    "   Selecting an operation costs nothing. EXECUTE spends the capacity.";
            }

            // Delegated directive settings (GDD §19).
            AddSlider("SPEED PRIORITY", directive.speedPriority, v => directive.speedPriority = v);
            AddSlider("CASUALTY TOLERANCE", directive.casualtyTolerance, v => directive.casualtyTolerance = v);
            AddSlider("CIVILIAN RISK LIMIT", directive.civilianRiskLimit, v => directive.civilianRiskLimit = v);
        }

        void AddSlider(string label, float value, System.Action<float> setter)
        {
            var slider = new Slider(label, 0f, 100f) { value = value, showInputField = false };
            slider.AddToClassList("terminal-text");
            slider.RegisterValueChangedCallback(evt => setter(evt.newValue));
            Root.Add(slider);
        }

        /// <summary>
        /// The negotiating table (GDD §26). Assemble terms, see what they cost
        /// the other side, and find out whether they will sign.
        /// </summary>
        void BuildSettlementControls()
        {
            var gc = GameController.Instance;
            var state = gc.State;
            var confrontation = state.ActiveConfrontation;
            if (confrontation == null) return;

            if (draftTerms.Count == 0)
                foreach (var term in PeaceSystem.SuggestProposal(state, confrontation, state.playerCountryId).terms)
                    draftTerms.Add(term);

            AddText("terminal-text-bright").text = "\n" + AsciiChart.BoxHeader("NEGOTIATED SETTLEMENT", W);

            // The one-press way out, offered first.
            //
            // The game told the operator "they are prepared to negotiate" and
            // then offered only PUT TERMS TO THEM — so the answer to "how do I
            // accept?" was "assemble a proposal from ten checkboxes and guess".
            // Naming an opportunity the interface cannot act on reads as a
            // missing button, not as a deep negotiation system.
            //
            // The detailed table below is still the real instrument; this is the
            // floor, for an operator who wants out on the best terms available
            // rather than the best terms imaginable.
            var readyDeal = PeaceSystem.BestAcceptableProposal(state, confrontation, state.playerCountryId);
            if (readyDeal != null)
            {
                var summary = new StringBuilder();
                summary.AppendLine("  THEY WOULD SIGN THIS TODAY:");
                foreach (var term in readyDeal.terms)
                    summary.AppendLine($"    · {Humanize(term)}");
                AddText("terminal-text-bright").text = summary.ToString().TrimEnd();

                var acceptRow = MakeRow();
                var accept = new Button(() =>
                {
                    GameController.Instance.ProposeTerms(readyDeal);
                    draftTerms.Clear();
                    Refresh();
                })
                { text = "ACCEPT THESE TERMS — END THE WAR" };
                accept.AddToClassList("cmd-button");
                accept.AddToClassList("primary");
                acceptRow.Add(accept);

                AddText("terminal-text-dim").text =
                    "  These are the best terms they would actually take. Build your own below " +
                    "if you want more — but more may be refused, and the war continues.";
            }

            var demandRow = MakeRow();
            var concessionRow = MakeRow();
            foreach (PeaceTerm term in System.Enum.GetValues(typeof(PeaceTerm)))
            {
                var captured = term;
                bool selected = draftTerms.Contains(term);
                float cost = PeaceSystem.TermCost(state, confrontation, state.playerCountryId, term);

                var button = new Button(() =>
                {
                    if (!draftTerms.Remove(captured)) draftTerms.Add(captured);
                    Refresh();
                })
                { text = (selected ? "[X] " : "[ ] ") + Humanize(term) };
                button.AddToClassList("cmd-button");
                if (selected) button.AddToClassList("primary");
                if (cost > 0f) demandRow.Add(button); else concessionRow.Add(button);
            }

            var proposal = new PeaceProposal();
            proposal.terms.AddRange(draftTerms);

            // Never print the true acceptance test: what our analysts believe,
            // at the precision our political collection on them supports.
            var outlook = PeaceSystem.Assess(state, confrontation, state.playerCountryId, proposal);

            var assessment = AddText(outlook == SettlementOutlook.Likely
                ? "terminal-text-bright" : "terminal-text-dim");
            var sb = new StringBuilder();
            sb.AppendLine("  Upper row demands; lower row concessions. A hard term becomes");
            sb.AppendLine("  signable when paired with something they want.");
            sb.AppendLine();
            sb.AppendLine($"  TERMS ON THE TABLE: {draftTerms.Count}");
            sb.AppendLine("  ASSESSMENT: " + OutlookText(outlook));
            assessment.text = sb.ToString();

            var actionRow = MakeRow();
            var propose = new Button(() =>
            {
                GameController.Instance.ProposeTerms(proposal);
                Refresh();
            })
            { text = "PUT TERMS TO THEM" };
            propose.AddToClassList("cmd-button");
            propose.AddToClassList("primary");
            propose.SetEnabled(draftTerms.Count > 0);
            actionRow.Add(propose);

            AddButton(actionRow, "CONCEDE OBJECTIVE", "danger", () =>
            {
                GameController.Instance.ProposeSettlement(true);
                Refresh();
            });
        }

        static string Humanize(PeaceTerm term)
        {
            switch (term)
            {
                case PeaceTerm.TerritorialCession: return "CEDE OBJECTIVE";
                case PeaceTerm.Reparations: return "REPARATIONS";
                case PeaceTerm.Demilitarization: return "DEMILITARIZE";
                case PeaceTerm.ResourceAccess: return "RESOURCE ACCESS";
                case PeaceTerm.Recognition: return "RECOGNITION";
                case PeaceTerm.TreatyRevision: return "REVISE TREATIES";
                case PeaceTerm.Withdrawal: return "WE WITHDRAW";
                case PeaceTerm.SanctionsRelief: return "WE LIFT SANCTIONS";
                case PeaceTerm.PrisonerExchange: return "PRISONER EXCHANGE";
                default: return "WE GUARANTEE THEM";
            }
        }

        void BuildAfterAction(Confrontation confrontation)
        {
            if (confrontation.operations.Count == 0) return;

            AddText("terminal-text-bright").text = "\n" + AsciiChart.BoxHeader("AFTER-ACTION LOG", W);

            // The most recent operation gets its full analysis; the ones before
            // it are a one-line record. Showing four full reports would bury the
            // one the operator is actually deciding against.
            int start = System.Math.Max(0, confrontation.operations.Count - 6);
            for (int i = confrontation.operations.Count - 1; i >= start; i--)
            {
                var op = confrontation.operations[i];
                bool latest = i == confrontation.operations.Count - 1;

                AddText(op.success ? "sig-ally" : "sig-hostile").text =
                    $" {op.date.SortKey} {op.operationType} — {(op.success ? "SUCCESS" : "FAILURE")}"
                    + (op.oddsAtOrder > 0f ? $"   (assessed {op.oddsAtOrder * 100f:F0}%)" : "");

                AddText("terminal-text-dim").text =
                    $"   {op.summary}\n"
                    + $"   LOSSES OWN {op.attackerLosses:F1} / ENEMY {op.defenderLosses:F1}"
                    + $" / CIV {op.civilianHarm:F1}";

                // The explanation is the point of the panel. Without it the log
                // records that something failed four times and says nothing about
                // why, which leaves retrying as the only available strategy.
                if (latest && !string.IsNullOrEmpty(op.explanation))
                    AddText("terminal-text").text = op.explanation;
            }
        }

        static string EscalationBar(EscalationState state)
        {
            string[] names = { "PEACE", "TENSION", "CRISIS", "LIMITED", "TOTAL WAR" };
            var sb = new StringBuilder();
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0) sb.Append(" > ");
                sb.Append(i == (int)state ? $"[{names[i]}]" : names[i]);
            }
            return sb.ToString();
        }

        void AddButton(VisualElement row, string text, string extraClass, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("cmd-button");
            if (extraClass != null) button.AddToClassList(extraClass);
            row.Add(button);
        }
    }
}
