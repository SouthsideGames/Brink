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

        /// <summary>Orders here are Military pillar orders — see TerminalView.GateOnAuthority.</summary>
        protected override Pillar? CommandPillar => Pillar.Military;

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        readonly OperationDirective directive = new OperationDirective();
        string selectedLocationId;
        string defensiveLocationId;
        OperationRecord lastDefensiveProgramme;
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
            AddPillarArt(state, Pillar.Military);

            BuildForceStructure(state, player);
            BuildStanding(state, player);
            BuildHomeExposure(state, player);
            foreach (var site in state.locations)
            {
                int remaining = MilitarySystem.MineMonthsRemaining(state, site);
                if (site.ownerId != player.id || remaining <= 0) continue;
                AddText("sig-advice").text = $" MINE DISRUPTION: {site.displayName} — {remaining} months remain. "
                    + "Mapped links using this port lose up to 6 effective volume; unmodelled links use the national-holder rule. Trade is not closed; hazards do not stack. "
                    + "Routine clearance ends it without an order. Successful Convoy Escort at this site removes 3 months; "
                    + "select it under defensive programmes. Other mined sites may still disrupt trade.";
            }
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

            // Who we are actually fighting, before which front we are commanding.
            // In a cascading alliance war the operator can acquire belligerents
            // they never declared against, and the front selector names only the
            // opponent of each front — so the one question a coalition war raises
            // had no answer on this screen.
            if (active.Count > 0) BuildBelligerents(state);

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
        /// Where we stand against everyone else, and what our wars came to
        /// (GDD §20 amendment).
        ///
        /// **Ranked by what we believe, not by what is true.** A global power
        /// table built from real values would hand the operator every rival's
        /// exact strength for free and make collecting on them pointless — the
        /// intelligence pillar rests on views never printing a foreign true
        /// value. So the order is our *estimate's* order: a state we have never
        /// tasked anything against is unranked, and a rival running a deception
        /// programme sits in the wrong place on purpose.
        ///
        /// That is a better table than an accurate one. Being wrong about who is
        /// second is a consequence of not having looked.
        /// </summary>
        void BuildStanding(GameState state, CountryState player)
        {
            AddText("terminal-text-bright").text =
                AsciiChart.BoxHeader("STANDING — OUR ASSESSMENT", W);

            // Our own record is ours to know exactly.
            AddText("terminal-text").text =
                $" OUR RECORD  {player.WarRecordText}  (won–lost–drawn)"
                + $"   FORCE {player.military.TotalPower * MilitarySystem.PowerScale:F0}";

            AddFigure().text = StandingFigure(state, W);

            AddText("terminal-text-dim").text =
                " Ranked by our own reporting. A state we are not collecting against cannot be "
                + "placed, and a state running deception will not be where it looks.";
        }

        /// <summary>
        /// The STANDING table, built to <paramref name="width"/> columns and never
        /// wider (spec 09). A figure, so nothing downstream wraps it: the
        /// assessment cell used to be a fixed 28 columns beside a name cell and a
        /// record — roughly 57 columns on a 49-column phone, running off the
        /// right edge under the file's own comment warning about exactly that.
        /// The assessment now takes whatever the row has left, and when that is
        /// too little to read it moves to a second indented line rather than
        /// being truncated to nothing.
        /// </summary>
        public static string StandingFigure(GameState state, int width)
        {
            width = System.Math.Max(TerminalMetrics.MinColumns, width);

            var ranked = new System.Collections.Generic.List<CountryState>();
            var unranked = new System.Collections.Generic.List<CountryState>();
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (IntelReadout.WhyNoAssessment(state, country.id) == null) ranked.Add(country);
                else unranked.Add(country);
            }

            // Sorted by the estimate, so the order carries our error too.
            ranked.Sort((a, b) => IntelReadout.EstimatedMilitary(state, b.id)
                .CompareTo(IntelReadout.EstimatedMilitary(state, a.id)));

            int nameWidth = AsciiChart.NameWidth(width, 0.34f);
            int recordWidth = 5;
            foreach (var country in state.countries)
                recordWidth = System.Math.Max(recordWidth, country.WarRecordText.Length);

            // " NN  name assessment record": 5 columns of rank, a space each side
            // of the assessment, the record at the end.
            const int RankColumns = 5;
            int assessWidth = width - RankColumns - nameWidth - 2 - recordWidth;
            bool twoLine = assessWidth < 14;
            const int Indent = 6;
            if (twoLine) assessWidth = width - Indent;

            var sb = new StringBuilder();
            void Row(string rank, CountryState country, string assessment)
            {
                string name = AsciiChart.Cell(country.displayName.ToUpperInvariant(), nameWidth);
                if (twoLine)
                {
                    sb.AppendLine($"{rank}{name} {country.WarRecordText}");
                    sb.AppendLine(new string(' ', Indent) + AsciiChart.Cell(assessment, assessWidth).TrimEnd());
                }
                else
                {
                    sb.AppendLine($"{rank}{name} {AsciiChart.Cell(assessment, assessWidth)} {country.WarRecordText}");
                }
            }

            int place = 1;
            foreach (var country in ranked)
                Row($" {place++,2}  ", country, IntelReadout.ForDomain(state, country.id, IntelDomain.Military));
            foreach (var country in unranked)
                Row("  —  ", country, IntelReadout.WhyNoAssessment(state, country.id));

            return sb.ToString();
        }

        /// <summary>
        /// What we are leaving uncovered (GDD §16, §19).
        ///
        /// **The AI could see this and the operator could not.**
        /// `TheatreSystem.IsOverstretched` had exactly two readers — `AISystem`,
        /// which treats it as an opening to attack us, and an event trigger. There
        /// was no player readout of it anywhere, and the measured consequence was
        /// visible in the balance harness: the military playstyle **takes its
        /// objective in four seeds out of five and still ends at net −1 location**,
        /// because it wins the war it chose and loses ground elsewhere while
        /// committed.
        ///
        /// That is a fine thing to have happen. It is not a fine thing to have
        /// happen *invisibly* — the difference between a trap and a decision is
        /// whether the operator could have known. So this names the commitment,
        /// the theatres we are absent from, and the specific holdings that are
        /// thinnest right now.
        ///
        /// Shown whenever we hold ground, not only at war: a garrison that has
        /// been quietly hollowed out by upkeep is worth seeing before somebody
        /// else notices it.
        /// </summary>
        void BuildHomeExposure(GameState state, CountryState player)
        {
            var ours = new System.Collections.Generic.List<StrategicLocation>();
            foreach (var location in state.locations)
                if (location.ownerId == state.playerCountryId) ours.Add(location);
            if (ours.Count == 0) return;

            float commitment = TheatreSystem.TotalCommitment(state, state.playerCountryId);
            bool overstretched = TheatreSystem.IsOverstretched(state, state.playerCountryId);

            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("WHAT WE ARE LEAVING UNCOVERED", W);

            var committedTheatres = TheatreSystem.ActiveTheatresFor(state, state.playerCountryId);
            if (committedTheatres.Count > 0)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var theatre in committedTheatres) names.Add(TheatreSystem.Name(theatre));
                AddText(overstretched ? "sig-hostile" : "terminal-text-dim").text =
                    $" COMMITTED IN {string.Join(", ", names)} — weight {commitment:F1}"
                    + (overstretched
                        ? ".  OVERSTRETCHED: every government that can see us knows it."
                        : ".");
            }
            else
            {
                AddText("terminal-text-dim").text =
                    " The force is uncommitted. Everything we hold is defended at its own weight.";
            }

            // The three thinnest positions we hold. Ranked by garrison and works
            // together, because either alone is misleading: a fortress with nobody
            // in it and a full garrison in the open are both openings.
            ours.Sort((a, b) => (a.garrison + a.defenseValue * 0.7f)
                .CompareTo(b.garrison + b.defenseValue * 0.7f));

            int shown = 0;
            foreach (var location in ours)
            {
                if (shown++ >= 3) break;

                var theatre = TheatreSystem.Of(location);
                bool covered = committedTheatres.Contains(theatre);
                float focus = TheatreSystem.FocusFactor(state, state.playerCountryId, theatre);

                AddText(location.garrison < 25f ? "sig-hostile" : "terminal-text-dim").text =
                    $"   {AsciiChart.Cell(location.displayName.ToUpperInvariant(), AsciiChart.NameWidth(W, 0.34f))}"
                    + $" GARRISON {location.garrison,4:F0}  WORKS {location.defenseValue,4:F0}"
                    + $"  {TheatreSystem.Name(theatre)}"
                    + (covered ? "" : $"  (we would fight here at {focus * 100f:F0}% weight)");
            }
        }

        /// <summary>
        /// THE WAR — every belligerent, on both sides, and why each of them is in
        /// it (GDD §18, §15.2).
        ///
        /// **Belligerency is public; strength is not.** Who has declared against
        /// whom is an observable fact, so it is stated plainly and completely.
        /// Nothing here prints a capability figure — BALANCE OF FORCES above still
        /// owns that, through `IntelReadout`, so a state we have never collected
        /// on appears in this roster by name and nowhere near a number.
        ///
        /// Colour is the second channel, never the only one: `sig-hostile` and
        /// `sig-ally` carry standing, and the `-` / `+` prefixes carry the same
        /// reading for anyone the palette does not reach.
        /// </summary>
        void BuildBelligerents(GameState state)
        {
            var enemies = BelligerentRoster.EnemiesOf(state, state.playerCountryId);
            var partners = BelligerentRoster.PartnersOf(state, state.playerCountryId);
            if (enemies.Count == 0 && partners.Count == 0) return;

            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("THE WAR", W);

            AddText("terminal-text-dim").text =
                $"   AGAINST US {enemies.Count,-3}      WITH US {partners.Count}";

            if (enemies.Count > 0)
            {
                AddText("terminal-text").text = "\n AGAINST US";
                foreach (var entry in enemies) AddBelligerent(state, entry, "sig-hostile", "-");
            }

            if (partners.Count > 0)
            {
                AddText("terminal-text").text = "\n WITH US";
                foreach (var entry in partners) AddBelligerent(state, entry, "sig-ally", "+");
            }

            // The guarantees that have not been called. An alliance earns its
            // price mostly in the war that does not happen, and an operator who
            // cannot see what they are holding cannot judge what it was worth.
            var owed = BelligerentRoster.ObligationsOwedBy(state, state.playerCountryId);
            if (owed.Count > 0)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (string id in owed)
                {
                    var country = state.FindCountry(id);
                    if (country != null) names.Add(country.displayName);
                }
                AddText("terminal-text-dim").text =
                    "\n STILL OWED: we are obliged to defend "
                    + string.Join(", ", names) + ".";
            }
        }

        /// <summary>
        /// One belligerent: name, why they are here, and — for a state we face
        /// directly — a jump to that front, so the roster is a way of commanding
        /// the war rather than a thing to read beside it.
        /// </summary>
        void AddBelligerent(GameState state, BelligerentRoster.Entry entry, string signal, string glyph)
        {
            var country = state.FindCountry(entry.countryId);
            if (country == null) return;

            // Never a hardcoded column count: the name column is a share of the
            // real panel width, truncated with an ellipsis rather than pushed off
            // the right edge of a phone.
            string name = AsciiChart.Cell(country.displayName.ToUpperInvariant(),
                AsciiChart.NameWidth(W, 0.42f));

            var line = AddText(signal);
            line.text = $"  {glyph} {name} {entry.because}";

            if (!entry.direct || string.IsNullOrEmpty(entry.confrontationId)) return;

            var target = state.FindConfrontation(entry.confrontationId);
            if (target == null || target.resolved) return;
            if (state.ActiveConfrontation != null
                && state.ActiveConfrontation.id == entry.confrontationId) return;

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

            string captured = entry.confrontationId;
            AddButton(row, $"COMMAND THIS FRONT — {country.displayName.ToUpperInvariant()}", null, () =>
            {
                state.commandingConfrontationId = captured;
                Refresh();
            });
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
        static string Sponsor(GameState state, Data.Insurgency rising)
        {
            var sponsor = state.FindCountry(rising.sponsorId);
            return sponsor != null ? sponsor.displayName.ToUpperInvariant() : "AN UNKNOWN STATE";
        }

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

            // **An armed movement on ground we hold is a military fact and it
            // belongs on the military screen.** Its numbers are precise here
            // because this is our own territory — we are counting our own
            // problem, not estimating somebody else's.
            var rising = InsurgencySystem.At(state, site.id);
            if (rising != null)
            {
                var line = AddText("sig-advice");
                line.text =
                    $"   ARMED MOVEMENT — STRENGTH {rising.strength:F0}  SUPPORT {rising.support:F0}"
                    + (InsurgencySystem.Denies(state, site)
                        ? "\n   This ground is producing nothing for us while it is contested."
                        : "")
                    + "\n   Counter-insurgency holds it down; only answering "
                    + InsurgencySystem.Describe(rising.cause) + " ends it."
                    + (InsurgencySystem.KnownSponsor(state, player.id, rising)
                        ? "\n   ARMED BY " + Sponsor(state, rising)
                        : "");
            }

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

                // **The assessed odds, before the order.** A defensive programme
                // used to be a button with no forecast attached, so an operator
                // working on ground they had just taken — where a low garrison and
                // an unpacified population make the work genuinely hard — met a
                // run of failures with nothing to reason from. Read through
                // `EstimateOdds`, which is the same function that resolves it, so
                // the forecast and the outcome cannot disagree.
                //
                // Forecast with **exactly what the order will pass**:
                // `LaunchDefensiveProgramme` sends a default directive and the
                // active confrontation, so the preview must too. The order
                // screen's `directive` is the operator's *offensive* settings
                // and is not what this path uses.
                float odds = possible
                    ? MilitarySystem.EstimateOdds(
                        state, state.playerCountryId, site, captured, new OperationDirective(),
                        ConfrontationSystem.CoalitionSupportFor(
                            state, state.ActiveConfrontation, state.playerCountryId, captured))
                    : 0f;

                var button = new Button(() =>
                {
                    var launched = GameController.Instance
                        .LaunchDefensiveProgramme(defensiveLocationId, captured);
                    if (launched != null) lastDefensiveProgramme = launched;
                    Refresh();
                })
                { text = $"{profile.displayName} [{cost} CP]" + (possible ? $"  {odds * 100f:F0}%" : "") };
                button.AddToClassList("cmd-button");
                if (!possible) Block(button, blocked);
                row.Add(button);
            }

            AddText("terminal-text-dim").text =
                "   Percentages are our own staff's assessment of the work succeeding. Peacetime "
                + "work: none of it escalates a standoff, and none of it is surcharged as an act "
                + "of war.";

            // Ground we hold but do not own is harder to work on, and saying so
            // is the difference between a run of failures reading as the map and
            // reading as the dice.
            if (site.IsOccupied)
                AddText("sig-rival").text =
                    "   THIS IS OCCUPIED GROUND. Until it is pacified the population is part of "
                    + "the defence, and every programme here is contested. COUNTER-INSURGENCY "
                    + "raises pacification; everything else gets easier as it rises.";

            // **The way out.** Occupied ground could be fortified, pacified and
            // garrisoned from this panel and never put down — the only verb that
            // released it needed a live confrontation, so once the war ended the
            // bill ran forever with no control anywhere that could stop it.
            if (site.IsOccupied)
            {
                var exitRow = new VisualElement();
                exitRow.AddToClassList("button-row");
                Root.Add(exitRow);

                bool canRelinquish = TerritorySystem.CanRelinquish(
                    state, state.playerCountryId, site.id, out string relinquishBlock);

                float monthlyBill = TerritorySystem.HoldingBill(state, site);

                var relinquish = new Button(() =>
                {
                    GameController.Instance.RelinquishLocation(site.id);
                    Refresh();
                })
                { text = $"RELINQUISH [{GameController.RelinquishCost} CP]" };
                relinquish.AddToClassList("cmd-button");
                if (!canRelinquish) Block(relinquish, relinquishBlock);
                exitRow.Add(relinquish);

                AddText("terminal-text-dim").text =
                    $"   HOLDING THIS COSTS {monthlyBill:F0} A MONTH. Returning it ends that bill "
                    + $"and costs {TerritorySystem.RelinquishWarSupportCost:F0} war support at home. "
                    + "The ground goes back to its government, not to nobody.";
            }

            BuildLastDefensiveProgramme(state);
        }

        /// <summary>
        /// What the last defensive programme came to, and why (GDD §19, §28.1).
        ///
        /// A peacetime programme's after-action report went to a single ADVISORY
        /// notification and nowhere else — it is not part of any war, so it has
        /// no confrontation diary to live in. The operator pressed a button on
        /// this screen, the screen said nothing, and the explanation was in the
        /// briefing behind a dozen unread items. Held in the view rather than in
        /// `GameState`: it is a note about what just happened on this screen, not
        /// a fact about the world, and the world's copy is in CHRONICLE.
        /// </summary>
        void BuildLastDefensiveProgramme(GameState state)
        {
            if (lastDefensiveProgramme == null) return;

            var record = lastDefensiveProgramme;
            var location = state.FindLocation(record.locationId);

            AddText(record.success ? "sig-ally" : "sig-hostile").text =
                $"   LAST PROGRAMME — {record.operationType} AT "
                + $"{(location?.displayName ?? "UNKNOWN").ToUpperInvariant()}: "
                + (record.success ? "COMPLETE" : "FELL SHORT");

            AddText("terminal-text-dim").text = "   " + record.summary;
            if (!string.IsNullOrEmpty(record.explanation))
                AddText("terminal-text-dim").text = record.explanation;
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

            // A figure, not prose: the columns are built to an exact grid and
            // wrapping would break the alignment that makes it readable.
            AddFigure(ours ? "terminal-text" : "terminal-text-dim").text =
                InventoryFigure(state, viewing, ours, W);

            if (!ours)
                AddText("terminal-text-dim").text =
                    "   Foreign figures are estimates. Better collection narrows the band; it never " +
                    "produces the true number.";
        }

        /// <summary>
        /// The ORDER OF BATTLE figure, built to <paramref name="width"/> columns
        /// and never wider. The rows were a fixed 14-column label plus an
        /// 18-column value plus an ON-ORDER suffix — ~56 columns on a 49-column
        /// phone. Label and value now share the width, and an ON-ORDER note that
        /// does not fit beside its row goes on its own indented line.
        /// </summary>
        public static string InventoryFigure(GameState state, CountryState viewing, bool ours, int width)
        {
            width = System.Math.Max(TerminalMetrics.MinColumns, width);
            const int Indent = 3;
            int labelWidth = AsciiChart.NameWidth(width, 0.30f);
            int valueWidth = System.Math.Max(6, width - Indent - labelWidth - 1);

            var sb = new StringBuilder();
            foreach (ForceBranch branch in System.Enum.GetValues(typeof(ForceBranch)))
            {
                var force = viewing.military.Get(branch);
                sb.AppendLine();
                sb.AppendLine(AsciiChart.Cell(
                    $" {branch.ToString().ToUpperInvariant()}" + (ours ? $"   STRENGTH {force.strength:F0}" : ""),
                    width).TrimEnd());

                foreach (var asset in AssetCatalog.InBranch(branch))
                {
                    string value = ours
                        ? AssetCatalog.Format(force.inventory.CountOf(asset.kind))
                        : IntelReadout.ForeignAssetCount(state, viewing.id, asset.kind);

                    string line = new string(' ', Indent)
                                  + AsciiChart.Cell(asset.label, labelWidth) + " "
                                  + AsciiChart.Cell(value, valueWidth).TrimEnd();

                    string ordered = "";
                    if (ours)
                    {
                        float onOrder = force.inventory.OnOrderOf(asset.kind);
                        if (onOrder > 0.5f) ordered = $"(+{AssetCatalog.Format(onOrder)} ON ORDER)";
                    }

                    if (ordered.Length > 0 && line.Length + 3 + ordered.Length <= width)
                    {
                        sb.AppendLine(line + "   " + ordered);
                    }
                    else
                    {
                        sb.AppendLine(line);
                        if (ordered.Length > 0)
                            sb.AppendLine(new string(' ', Indent + 3)
                                          + AsciiChart.Cell(ordered, width - Indent - 3).TrimEnd());
                    }
                }
            }
            return sb.ToString();
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
                if (!allowed) Block(button, blocked.ToUpperInvariant() + ".");
                footingRow.Add(button);

                if (allowed) AddText("terminal-text-dim").text =
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
            AddText("terminal-text-dim").text =
                $"   {chosen.displayName} — {AssetCatalog.Format(order)} per order, "
                + $"{cost:F0} treasury paid upfront. Deliveries arrive incrementally, not on a fixed completion date."
                + (player.resources.treasury < cost ? "   TREASURY CANNOT COVER IT" : "");

            var backlog = new StringBuilder(" PAID EQUIPMENT BACKLOG\n");
            bool pending = false;
            foreach (var asset in AssetCatalog.All)
            {
                var stock = player.military.Get(asset.branch).inventory.Get(asset.kind);
                if (stock == null || stock.onOrder <= 0.01f) continue;
                pending = true;
                backlog.AppendLine($"{asset.displayName}: {stock.onOrder:0.##} still to arrive.");
            }
            if (!pending) backlog.AppendLine("No equipment awaiting delivery.");
            backlog.AppendLine("Orders of the same equipment share a backlog. Already paid: no monthly purchase instalment. "
                + "Industry and war footing change delivery tempo; this is not a dated order ledger. "
                + "Equipment upkeep and war-footing costs are separate.");
            AddText("terminal-text-dim").text = backlog.ToString();

            var orderRow = MakeRow();
            var place = new Button(() =>
            {
                GameController.Instance.OrderAssets(selectedAsset, order);
                Refresh();
            })
            { text = $"ORDER {AssetCatalog.Format(order)} {chosen.label} [{AcquisitionSystem.OrderCost} CP]" };
            place.AddToClassList("cmd-button");
            place.AddToClassList("primary");
            if (player.resources.treasury < cost)
                Block(place, $"TREASURY {player.resources.treasury:F0} — THIS ORDER COSTS {cost:F0}.");
            orderRow.Add(place);

            var bulk = new Button(() =>
            {
                GameController.Instance.OrderAssets(selectedAsset, order * 4f);
                Refresh();
            })
            { text = $"ORDER {AssetCatalog.Format(order * 4f)} [{AcquisitionSystem.OrderCost} CP]" };
            bulk.AddToClassList("cmd-button");
            if (player.resources.treasury < cost * 4f)
                Block(bulk, $"TREASURY {player.resources.treasury:F0} — FOUR ORDERS COST {cost * 4f:F0}.");
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
                if (current) button.SetEnabled(false);
                else if (!allowed) Block(button, blocked.ToUpperInvariant());
                postureRow.Add(button);
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
                psb.AppendLine(MilitarySystem.ProcurementReadout(program));
            if (mil.programs.Count == 0)
                psb.AppendLine("   Force structure is built over years, and paid for every month.");
            psb.AppendLine(" RECENT PROCUREMENT RECORD (LATEST 5)");
            int shown = 0;
            for (int i = state.chronicle.Count - 1; i >= 0 && shown < 5; i--)
            {
                var entry = state.chronicle[i];
                if (entry.countryId != player.id || entry.category != ChronicleCategory.Military || entry.text == null) continue;
                if (!entry.text.StartsWith("PROCUREMENT AUTHORIZED:", System.StringComparison.Ordinal)
                    && !entry.text.StartsWith("PROCUREMENT TERMINATED:", System.StringComparison.Ordinal)
                    && !entry.text.StartsWith("PROCUREMENT COMPLETED:", System.StringComparison.Ordinal)) continue;
                psb.AppendLine($"{entry.date.DisplayString}: {entry.text}");
                shown++;
            }
            if (shown == 0) psb.AppendLine("No identified procurement records yet. Older general entries remain in the Chronicle.");
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
                if (locked)
                    Block(button, "NO YARD OR LINE CAN ABSORB A PROGRAMME THAT SIZE — "
                                  + "REQUIRES STRATEGIC INDUSTRY.");
                scaleRow.Add(button);
            }

            // Money and industrial slots refuse a programme just as firmly as
            // Command Points do, and the operator can see neither from this
            // screen. Read through the same gate the order itself uses, so what
            // is offered and what is accepted cannot disagree.
            bool canProcure = MilitarySystem.CanBeginProcurement(
                state, selectedProgramScale, out string procurementBlocked);
            bool canSustain = MilitarySystem.CanInvestInLogistics(state, out string logisticsBlocked);

            var programRow = MakeRow();
            foreach (ForceBranch branch in System.Enum.GetValues(typeof(ForceBranch)))
            {
                var captured = branch;
                var scale = selectedProgramScale;
                var button = AddButton(programRow,
                    $"{branch.ToString().ToUpperInvariant()} PROGRAM [{MilitarySystem.ProgramCpCost(scale)} CP]",
                    null, () =>
                {
                    GameController.Instance.BeginProcurement(captured, scale);
                    Refresh();
                });
                if (!canProcure) Block(button, procurementBlocked);
            }

            var logistics = AddButton(programRow,
                $"LOGISTICS [{MilitarySystem.LogisticsInvestmentCost} CP]", null, () =>
            {
                GameController.Instance.InvestInLogistics();
                Refresh();
            });
            if (!canSustain) Block(logistics, logisticsBlocked);

            AddText("terminal-text-dim").text =
                $"   TREASURY {player.resources.treasury:F0}. A programme is paid for every month it runs, "
                + "not when it is authorized.";
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

        static void AppendBranch(StringBuilder sb, string label, BranchForce force)
        {
            sb.AppendLine($" {label}  (effective power {force.EffectivePower:F1})");
            sb.AppendLine("   " + AsciiChart.LabeledBar("STRENGTH", force.strength, 100, 10, 18));
            sb.AppendLine("   " + AsciiChart.LabeledBar("READINESS", force.readiness, 100, 10, 18));
            sb.AppendLine("   " + AsciiChart.LabeledBar("SUPPLY", force.supply, 100, 10, 18));
            sb.AppendLine("   " + AsciiChart.LabeledBar("EXPERIENCE", force.experience, 100, 10, 18)
                          + $"  {force.ExperienceBand}");
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
        /// <summary>
        /// Their disposition toward ending the war, as our reporting reads it
        /// (GDD §26). Never the acceptance test.
        /// </summary>
        static string DispositionText(SettlementDisposition disposition)
        {
            switch (disposition)
            {
                case SettlementDisposition.LikelyReceptive: return "LIKELY RECEPTIVE";
                case SettlementDisposition.PotentiallyReceptive: return "POTENTIALLY RECEPTIVE";
                case SettlementDisposition.Uncertain: return "UNCERTAIN";
                case SettlementDisposition.Resistant: return "RESISTANT";
                case SettlementDisposition.HighlyResistant: return "HIGHLY RESISTANT";
                default: return "NO READ — COLLECT AGAINST THEM";
            }
        }

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
            if (!canRun)
            {
                ExerciseSystem.CanExerciseWith(state, selectedPartnerId, out string blocked);
                Block(run, blocked.ToUpperInvariant());
            }
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

            BuildBalanceOfForces(state);
        }

        /// <summary>
        /// What a war with each state would look like, *before* opening one.
        ///
        /// `MilitaryAdvice` tells the operator whether this strike is wise; it
        /// exists only once a confrontation is running. Nothing said whether the
        /// war itself was winnable, so intelligence paid off during a war and
        /// never in the decision to start one — which is the decision it should
        /// most obviously inform.
        ///
        /// Every foreign figure here routes through `IntelReadout`, so a state we
        /// have never collected against reads NO ASSESSMENT rather than a number.
        /// **That absence is the feature.** The comparison is worth buying, and an
        /// operator who opens a war against an unread opponent should be able to
        /// see that they are doing exactly that.
        /// </summary>
        void BuildBalanceOfForces(GameState state)
        {
            var player = state.PlayerCountry;
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("BALANCE OF FORCES", W);

            // **Two lines per state, widths derived from the measured grid.**
            //
            // The first version was a single row of `name + 49` fixed characters —
            // about 61 columns against a phone's ~49, unwrapped because it is an
            // `AddFigure` label. It was a fresh instance of the rule this file is
            // supposed to enforce: never hardcode a column count in a view.
            var sb = new StringBuilder();
            int nameWidth = AsciiChart.NameWidth(W, 0.42f);
            int bandWidth = System.Math.Max(9, (W - 10) / 3 - 3);

            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;

                sb.AppendLine($" {AsciiChart.Cell(country.displayName.ToUpperInvariant(), nameWidth)}"
                              + $"  REACH {ReachTo(state, country)}");
                sb.AppendLine($"   G {AsciiChart.Cell(IntelReadout.ForeignBranchStrength(state, country.id, ForceBranch.Ground), bandWidth)}"
                              + $" A {AsciiChart.Cell(IntelReadout.ForeignBranchStrength(state, country.id, ForceBranch.Air), bandWidth)}"
                              + $" N {AsciiChart.Cell(IntelReadout.ForeignBranchStrength(state, country.id, ForceBranch.Naval), bandWidth)}");
            }

            var figure = AddFigure("terminal-text");
            figure.text = sb.ToString();

            AddText("terminal-text-dim").text =
                $" Ours: ground {player.military.ground.EffectivePower:F1}"
                + $" ({player.military.ground.ExperienceBand.ToLowerInvariant()}),"
                + $" air {player.military.air.EffectivePower:F1}"
                + $" ({player.military.air.ExperienceBand.ToLowerInvariant()}),"
                + $" naval {player.military.naval.EffectivePower:F1}"
                + $" ({player.military.naval.ExperienceBand.ToLowerInvariant()}).";

            AddText("terminal-text-dim").text =
                " REACH is how much of our weight would arrive there. Bands widen with poor "
                + "collection — buy intelligence before buying a war.";
            AddText("terminal-text-dim").text = " " + IntelReadout.AssessmentLegend;
        }

        /// <summary>How much of our force would actually reach this country's ground.</summary>
        static string ReachTo(GameState state, CountryState country)
        {
            foreach (var location in state.locations)
                if (location.originalOwnerId == country.id)
                    return $"{GeographySystem.ReachFactorFor(state, state.playerCountryId, location) * 100f:F0}%";
            return "—";
        }

        void BuildConfrontationConsole(GameState state, Confrontation confrontation)
        {
            var opponent = state.FindCountry(confrontation.OpponentOf(state.playerCountryId));
            bool playerIsInitiator = state.playerCountryId == confrontation.initiatorId;
            float ourExhaustion = IntelReadout.OwnExhaustion(state, confrontation);
            // Theirs is a bin our reporting supports, never the figure — the
            // exact value was the largest term of the acceptance test, printed
            // to one decimal beside casualties this same block bands.
            string theirExhaustion = IntelReadout.ForeignExhaustion(state, confrontation);
            float ourMomentum = playerIsInitiator ? confrontation.momentum : -confrontation.momentum;

            var header = AddText("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader($"CONFRONTATION — {opponent?.displayName.ToUpperInvariant()}", W));
            sb.AppendLine($" OBJECTIVE:       {ConfrontationSystem.ObjectiveText(state, confrontation)}");
            sb.AppendLine($" PRIMARY STRATEGY:{confrontation.primaryStrategy}");
            sb.AppendLine($" ESCALATION:      {EscalationBar(confrontation.escalation)}");
            sb.AppendLine($" MONTHS ACTIVE:   {confrontation.monthsActive}");
            sb.AppendLine($" MOMENTUM:        {ourMomentum,+6:F1}");
            sb.AppendLine($" OUR EXHAUSTION:  {ourExhaustion,6:F1}    THEIRS (EST): {theirExhaustion}");
            sb.AppendLine($" CIVILIAN HARM:   {confrontation.civilianHarmTotal,6:F1}");

            // The human cost of the war, which was counted every single operation
            // and displayed nowhere. `initiatorCasualties` and `defenderCasualties`
            // had zero readers in the entire UI — a game that takes the cost of
            // force seriously everywhere else was silent about the one number that
            // measures it, so a war that killed two hundred thousand of our people
            // read exactly like one that killed five thousand.
            //
            // Ours is true because they are ours. Theirs is an estimate, like
            // every other foreign figure: we count our own dead and guess at the
            // enemy's, which is both correct fog discipline and the honest
            // description of what a government actually knows.
            bool weInitiated = confrontation.initiatorId == state.playerCountryId;
            float ourDead = weInitiated ? confrontation.initiatorCasualties
                                        : confrontation.defenderCasualties;
            float theirDead = weInitiated ? confrontation.defenderCasualties
                                          : confrontation.initiatorCasualties;

            sb.AppendLine($" OUR LOSSES:      {IntelReadout.OwnCasualties(ourDead),-10}"
                          + $"THEIR LOSSES (EST): {IntelReadout.ForeignCasualties(state, opponent?.id, theirDead)}");
            // Our reporting's read of their mood, never the acceptance test.
            // "OPEN TO TERMS / RESISTING" was the true willingness bit, and
            // watching it flip told the operator the month they became willing.
            sb.AppendLine($" THEIR DISPOSITION (OUR READ): {DispositionText(PeaceSystem.AssessDisposition(state, confrontation, state.playerCountryId))}");
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

            // **A target list must say whose ground each place is.** Reported from
            // play as "hard to tell what is my places and what is theirs": the
            // list mixed our own locations in with the opponent's and printed
            // nothing but a name, so ordering PREPARED DEFENCE on Norfolk and
            // ordering an assault on Ankara looked like the same kind of decision.
            //
            // Glyph first, colour second — the same rule the map follows, so the
            // reading survives any palette and any colour vision. The type code
            // is the other half: whether a place is a port or an airbase decides
            // which of the twenty-three verbs can even point at it.
            AddText("terminal-text-dim").text =
                "   + OURS    ! THEIRS    - THIRD PARTY";

            var targetRow = new VisualElement();
            targetRow.AddToClassList("button-row");
            Root.Add(targetRow);
            foreach (var loc in targets)
            {
                var captured = loc;
                bool current = selectedLocationId == loc.id;
                bool advised = advice != null && advice.targetLocationId == loc.id;

                bool ours = loc.ownerId == state.playerCountryId;
                bool theirs = loc.ownerId == confrontation?.OpponentOf(state.playerCountryId);
                string glyph = ours ? "+" : theirs ? "!" : "-";

                var button = new Button(() => { selectedLocationId = captured.id; Refresh(); })
                {
                    // Kept deliberately tight. MILITARY is the densest screen in
                    // the game and every button grew ~30% taller when touch
                    // targets went to 44px, so the marker is four characters —
                    // glyph, type, space — not a sentence. The type code earns
                    // its place: it decides which of the twenty-three verbs can
                    // point at the target at all.
                    text = (current ? "► " : "  ")
                           + glyph + " " + loc.TypeCode + " "
                           + loc.displayName.ToUpperInvariant()
                           + (advised ? " ★" : "")
                };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");

                // The star carries the meaning and the colour reinforces it, so
                // the recommendation survives any palette and any colour vision —
                // the same rule the map's standing glyphs follow.
                //
                // Advice wins the colour slot when both apply: `sig-advice` is a
                // recommendation about *this* order, while ownership is already
                // carried by the glyph, so nothing is lost by yielding it.
                if (advised) button.AddToClassList("sig-advice");
                else if (ours) button.AddToClassList("sig-friendly");
                else if (theirs) button.AddToClassList("sig-hostile");

                targetRow.Add(button);
            }

            // What distance will do to the force, *before* the order is given
            // (GDD §16). A player who only learns this from a run of failed
            // operations reads it as unfair dice rather than as the map.
            var selectedTarget = state.FindLocation(selectedLocationId);
            if (selectedTarget != null)
            {
                // The button row has to stay terse, so the identity of whatever
                // is actually selected is spelled out here in full — including
                // the case the glyph cannot express, where ground physically in
                // their country is currently held by us or by a third party.
                var owner = state.FindCountry(selectedTarget.ownerId);
                var origin = state.FindCountry(selectedTarget.originalOwnerId);
                var identity = new StringBuilder();
                identity.Append("   TARGET: ")
                        .Append(selectedTarget.displayName.ToUpperInvariant())
                        .Append(" — ").Append(Phrase.Of(selectedTarget.type).ToLowerInvariant())
                        .Append(", held by ")
                        .Append(selectedTarget.ownerId == state.playerCountryId
                            ? "us" : owner?.displayName ?? "no one");

                if (origin != null && origin.id != selectedTarget.ownerId)
                    identity.Append(" (").Append(origin.displayName).Append("'s ground)");

                AddText(selectedTarget.ownerId == state.playerCountryId
                    ? "sig-friendly" : "terminal-text-bright").text = identity.ToString();

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
                // Ask once and keep the answer: the reason is needed below, and
                // calling the gate a second time to recover it is how the two
                // drift. Named apart from the `blocked` further down this method,
                // which is the *selected* order's refusal rather than this one's.
                bool possible = OperationCatalog.CanOrder(
                    state, state.playerCountryId, selectedTarget, captured, out string whyNot);

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
                if (!possible) Block(button, whyNot);
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

            // The one-press way out, offered first — from our **assessment**,
            // never from the acceptance test.
            //
            // The game told the operator "they are prepared to negotiate" and
            // then offered only PUT TERMS TO THEM — so the answer to "how do I
            // accept?" was "assemble a proposal from ten checkboxes and guess".
            // Naming an opportunity the interface cannot act on reads as a
            // missing button, not as a deep negotiation system.
            //
            // The first version of this drafted the offer from
            // `BestAcceptableProposal`, which walks `WouldAccept` — ground
            // truth — and printed it under "THEY WOULD SIGN THIS TODAY", with no
            // collection at all. That was the settlement oracle the fog design
            // had closed once, re-shipped as a button. The recommendation now
            // stops where our *reporting* says they would likely sign, which
            // with poor reporting means giving more ground than strictly
            // necessary and with none means no recommendation.
            var readyDeal = PeaceSystem.RecommendedProposal(
                state, confrontation, state.playerCountryId, out var readyOutlook);
            if (readyDeal != null)
            {
                var summary = new StringBuilder();
                summary.AppendLine(readyOutlook == SettlementOutlook.Likely
                    ? "  OUR STAFF'S RECOMMENDATION — THEY WOULD LIKELY SIGN:"
                    : "  OUR STAFF'S RECOMMENDATION — THEY MIGHT SIGN:");
                foreach (var term in readyDeal.terms)
                    summary.AppendLine($"    · {PeaceSystem.Describe(term)}");
                AddText("terminal-text-bright").text = summary.ToString().TrimEnd();

                var acceptRow = MakeRow();
                var accept = new Button(() =>
                {
                    GameController.Instance.ProposeTerms(readyDeal);
                    draftTerms.Clear();
                    Refresh();
                })
                { text = "PUT THESE TERMS TO THEM" };
                accept.AddToClassList("cmd-button");
                accept.AddToClassList("primary");
                acceptRow.Add(accept);

                AddText("terminal-text-dim").text =
                    "  Our staff's reading of what they would take, at the precision our reporting " +
                    "on their politics allows. Build your own below if you want more — but more may " +
                    "be refused, and the war continues.";
            }
            else
            {
                AddText("terminal-text-dim").text = readyOutlook == SettlementOutlook.Unknown
                    ? "  NO RECOMMENDATION: we have no read on their politics. Terms can still be put " +
                      "to them below; whether they sign is a guess until we collect against them."
                    : "  NO RECOMMENDATION: on what we hold, nothing we would offer reads as signable " +
                      "yet. Terms can still be put to them below.";
            }

            var demandRow = MakeRow();
            var concessionRow = MakeRow();
            foreach (PeaceTerm term in System.Enum.GetValues(typeof(PeaceTerm)))
            {
                var captured = term;
                bool selected = draftTerms.Contains(term);
                string inert = PeaceSystem.WhyInert(state, confrontation, state.playerCountryId, term);

                var button = new Button(() =>
                {
                    if (!draftTerms.Remove(captured)) draftTerms.Add(captured);
                    Refresh();
                })
                {
                    text = inert != null
                        ? "[-] " + PeaceSystem.Describe(term) + " — " + inert
                        : (selected ? "[X] " : "[ ] ") + PeaceSystem.Describe(term)
                };
                button.AddToClassList("cmd-button");
                if (selected && inert == null) button.AddToClassList("primary");
                if (inert != null) Block(button, inert.ToUpperInvariant());

                // **Route by what the term *is*, not by what it currently prices
                // at.** Sorting on `cost > 0` filed every conditional demand under
                // concessions the moment its condition was unmet — so a demand
                // appeared beneath a header promising concessions, which is the
                // one thing this two-row layout exists to communicate.
                if (PeaceSystem.IsDemand(term)) demandRow.Add(button);
                else concessionRow.Add(button);
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
            if (draftTerms.Count == 0)
                Block(propose, "NO TERMS ON THE TABLE.");
            actionRow.Add(propose);

            AddButton(actionRow, "CONCEDE OBJECTIVE", "danger", () =>
            {
                GameController.Instance.ProposeSettlement(true);
                Refresh();
            });
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

                // Ours exact, theirs banded — the same rule as the war's totals
                // above. Per-operation enemy losses used to print to one decimal
                // here, so summing the log reconstructed the banded total.
                AddText("terminal-text-dim").text =
                    $"   {op.summary}\n"
                    + $"   {IntelReadout.OperationLosses(GameController.Instance.State, confrontation, op)}"
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
    }
}
