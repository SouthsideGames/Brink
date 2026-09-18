using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Blocs: sides with names (GDD §15.2, §24).
    ///
    /// **The world had sides and no side had a name.** `RivalGravity` prices deep
    /// alignment with somebody's enemy, and the AI's bloc politics work — but
    /// there was no object. Nothing to join, nothing to lead, nothing to be
    /// excluded from and nothing to walk out of. Coalitions are raised for one war
    /// and dissolve; treaties are bilateral; alliance obligations are a
    /// consequence of a signature rather than a structure.
    ///
    /// Four rules keep it from being a second treaty system:
    ///
    /// 1. **Its commitments are uniform, and joining is judged on them.** A bloc
    ///    may carry `MutualDefense`, `Transit`, `IntelligenceSharing` or
    ///    `TradePreference`, and every member carries all of them toward every
    ///    other. It does not reimplement the bilateral bargain: acceptance still
    ///    prices burden through `DiplomacySystem.BurdenOf`, so a defence bloc is
    ///    genuinely harder to get somebody into than a talking shop. Asymmetric
    ///    clauses stay in `Treaty`, which is what `ClauseSide` is for.
    /// 2. **It is read in two places, both already load-bearing.** Members drift
    ///    into `strategicAlignment` with one another, and `CouncilSystem.VoteScore`
    ///    gains a term for following the bloc — so a bloc is worth having in the
    ///    one room where states act together, which is exactly what such a
    ///    structure is for.
    /// 3. **Leading costs, every month.** Holding a group of governments together
    ///    is work, and a leader who stops paying watches it come apart.
    /// 4. **Cohesion is a target.** It drifts toward what the members' own
    ///    relations will hold, so a bloc of states that dislike each other
    ///    dissolves on its own and no value here ratchets.
    ///
    /// Bounded at `MaxBlocs`, because a world of twenty-four one-member blocs is
    /// not a world with sides — it is a world with a list.
    /// </summary>
    public static class BlocSystem
    {
        /// <summary>How many can exist at once.</summary>
        public const int MaxBlocs = 3;

        /// <summary>A bloc that falls below this many members is not a side.</summary>
        public const int MinimumMembers = 2;

        /// <summary>Command capacity for the player to found one or bring somebody in.</summary>
        public const int FoundCost = 3;
        public const int InviteCost = 2;

        /// <summary>Political Capital the leader pays every month, per member beyond itself.</summary>
        public const float UpkeepPerMember = 0.35f;

        /// <summary>Standing a leader needs before anybody would follow them.</summary>
        public const float LeadershipThreshold = 55f;

        /// <summary>Below this a member stops believing in it.</summary>
        public const float CohesionFloor = 22f;

        // ---------- lookups ----------

        public static Bloc Find(GameState state, string blocId)
        {
            if (string.IsNullOrEmpty(blocId)) return null;
            for (int i = 0; i < state.blocs.Count; i++)
                if (state.blocs[i].id == blocId && !state.blocs[i].dissolved) return state.blocs[i];
            return null;
        }

        /// <summary>The bloc this country belongs to, or null.</summary>
        public static Bloc BlocOf(GameState state, string countryId)
        {
            if (state?.blocs == null || string.IsNullOrEmpty(countryId)) return null;
            for (int i = 0; i < state.blocs.Count; i++)
            {
                var bloc = state.blocs[i];
                if (!bloc.dissolved && bloc.Has(countryId)) return bloc;
            }
            return null;
        }

        /// <summary>True when both states are in the same bloc.</summary>
        public static bool SameBloc(GameState state, string a, string b)
        {
            var bloc = BlocOf(state, a);
            return bloc != null && bloc.Has(b);
        }

        /// <summary>True when each is in a bloc and they are different ones.</summary>
        public static bool OpposedBlocs(GameState state, string a, string b)
        {
            var blocA = BlocOf(state, a);
            var blocB = BlocOf(state, b);
            return blocA != null && blocB != null && blocA.id != blocB.id;
        }

        public static int LiveBlocCount(GameState state)
        {
            int count = 0;
            foreach (var bloc in state.blocs) if (!bloc.dissolved) count++;
            return count;
        }

        // ---------- founding one ----------

        public static bool CanFound(GameState state, string leaderId, out string reason)
        {
            var leader = state.FindCountry(leaderId);
            if (leader == null) { reason = "No such state."; return false; }

            if (BlocOf(state, leaderId) != null)
            {
                reason = "WE ARE ALREADY IN A BLOC. Leave it before founding another.";
                return false;
            }
            if (LiveBlocCount(state) >= MaxBlocs)
            {
                reason = $"THE WORLD ALREADY HAS {MaxBlocs} BLOCS. There is no room for a "
                       + "fourth side.";
                return false;
            }
            if (leader.pillars.diplomacy < LeadershipThreshold)
            {
                reason = $"NOBODY WOULD FOLLOW US — diplomacy {leader.pillars.diplomacy:F0}, "
                       + $"need {LeadershipThreshold:F0}.";
                return false;
            }

            reason = "";
            return true;
        }

        /// <summary>Actor-generic. Founds a bloc of one; it needs a second member to survive.</summary>
        public static Bloc FoundBy(GameState state, string leaderId, string name)
            => FoundBy(state, leaderId, name, null);

        /// <summary>
        /// Actor-generic, with the terms every member will carry.
        ///
        /// The commitments are fixed at founding and never edited afterwards: a
        /// bloc whose leader can add a defence obligation to it later is a bloc
        /// whose members did not agree to what they are now bound by. Widening
        /// the terms means founding a new one, which is the same rule
        /// `Treaty`/`DeepenTreatyBy` follows by making every addition answer to
        /// acceptance again.
        /// </summary>
        public static Bloc FoundBy(GameState state, string leaderId, string name,
            List<TreatyCommitment> commitments)
        {
            if (!CanFound(state, leaderId, out _)) return null;

            var leader = state.FindCountry(leaderId);
            var bloc = new Bloc
            {
                id = $"BLOC_{leaderId}_{state.date.SortKey}",
                name = string.IsNullOrEmpty(name) ? DefaultName(leader) : name,
                leaderId = leaderId,
                founded = state.date,
                cohesion = 55f
            };
            bloc.memberIds.Add(leaderId);
            if (commitments != null)
                foreach (var commitment in commitments)
                    if (!bloc.commitments.Contains(commitment)) bloc.commitments.Add(commitment);

            state.blocs.Add(bloc);

            state.AddChronicle(ChronicleCategory.Diplomatic, leaderId,
                $"{leader.displayName} founds {bloc.name}{DescribeTerms(bloc)}.", Publicity.Public);

            if (leader.isPlayer)
                state.AddNotification(NotificationClass.Priority, "BLOC FOUNDED",
                    $"{bloc.name} exists{DescribeTerms(bloc)}. It is one state until somebody "
                    + "joins it, and it dissolves if nobody does.", leaderId,
                    desk: ReportingDesk.Diplomacy);

            return bloc;
        }

        /// <summary>Player order: spends CP and records the initiative.</summary>
        public static Bloc Found(GameState state, TurnManager turns, string name)
            => Found(state, turns, name, null);

        /// <summary>Player order, with the terms the bloc will carry.</summary>
        public static Bloc Found(GameState state, TurnManager turns, string name,
            List<TreatyCommitment> commitments)
        {
            if (!AuthoritySystem.EnsureAuthority(state, Pillar.Diplomacy)) return null;
            if (!CanFound(state, state.playerCountryId, out string reason))
            {
                GameLog.Warn("DIPL", reason);
                return null;
            }

            if (!turns.SpendCommandPoints(FoundCost, "Found a bloc")) return null;

            var bloc = FoundBy(state, state.playerCountryId, name, commitments);
            if (bloc == null) return null;

            ProgressionSystem.AwardXP(state, 24, "Founded a bloc");
            ProgressionSystem.RecordInitiative(state);
            return bloc;
        }

        /// <summary>
        /// The bloc's terms, bare: `MUTUAL DEFENSE, TRADE PREFERENCE`. Empty when
        /// it carries none.
        /// </summary>
        public static string TermsLine(Bloc bloc)
        {
            if (bloc == null || bloc.commitments.Count == 0) return "";

            var parts = new List<string>();
            foreach (var commitment in bloc.commitments) parts.Add(Phrase.Caps(commitment));
            return string.Join(", ", parts);
        }

        /// <summary>The same, parenthesised for appending to a sentence.</summary>
        public static string DescribeTerms(Bloc bloc)
        {
            string terms = TermsLine(bloc);
            return terms.Length == 0 ? "" : $" ({terms})";
        }

        static string DefaultName(CountryState leader)
            => $"THE {leader.displayName.ToUpperInvariant()} UNDERSTANDING";

        // ---------- bringing somebody in ----------

        public static bool CanInvite(GameState state, string leaderId, string targetId,
            out string reason)
        {
            var bloc = BlocOf(state, leaderId);
            if (bloc == null || bloc.leaderId != leaderId)
            {
                reason = "WE DO NOT LEAD A BLOC.";
                return false;
            }
            if (bloc.Has(targetId)) { reason = "THEY ARE ALREADY IN IT."; return false; }
            if (BlocOf(state, targetId) != null)
            {
                reason = "THEY ARE IN SOMEBODY ELSE'S BLOC. They would have to leave it first.";
                return false;
            }
            if (state.FindCountry(targetId) == null) { reason = "No such state."; return false; }

            reason = "";
            return true;
        }

        /// <summary>
        /// How willing a state is to join, 0..100 against a threshold of 50.
        ///
        /// Read from the same six dimensions everything else in this pillar reads,
        /// so joining can be *bought* through exactly the instruments that buy
        /// anything else — and so a state that has been courted for a decade
        /// behaves differently from one that has not.
        /// </summary>
        public static float JoinWillingness(GameState state, Bloc bloc, string targetId)
        {
            var relationship = state.FindRelationship(bloc.leaderId, targetId);
            if (relationship == null) return 0f;

            var target = state.FindCountry(targetId);
            var leader = state.FindCountry(bloc.leaderId);
            if (target == null || leader == null) return 0f;

            float willingness = 20f
                // Relations and alignment are read as the warmth the world permits:
                // joining a bloc is taking a side. **Trust is deliberately left
                // underlying** — GDD §15.1 defines it as the belief that commitments
                // will be honoured, which is a fact about the leader's reliability
                // rather than about how close bloc politics lets us stand. Capping it
                // would have a third state's alignment make a partner look less
                // trustworthy, which is disposition, not permission. Written down
                // because two of three terms being capped is otherwise a drift trap.
                + (DiplomacySystem.Permitted(state, relationship, relationship.relations) - 50f) * 0.55f
                + (relationship.trust - 50f) * 0.45f
                + (DiplomacySystem.Permitted(state, relationship, relationship.strategicAlignment) - 50f) * 0.40f
                + relationship.DependenceOf(targetId) * 0.20f
                + bloc.cohesion * 0.12f;

            // Nobody joins a bloc led by somebody they are frightened of. This is
            // the term that stops a large power collecting the map: the stronger
            // and more threatening the leader looks, the harder the sell.
            willingness -= Math.Max(0f, relationship.ThreatPerceivedBy(targetId) - 40f) * 0.55f;

            // And nobody joins a side against a state they cannot do without.
            foreach (string memberId in bloc.memberIds)
            {
                var withMember = state.FindRelationship(targetId, memberId);
                if (withMember == null) continue;
                if (DiplomacySystem.Permitted(state, withMember, withMember.relations) < 25f) willingness -= 12f;
            }

            var treaty = state.FindTreaty(bloc.leaderId, targetId);
            if (treaty != null) willingness += 10f;
            if (treaty != null && treaty.HasActive(state, TreatyCommitment.MutualDefense)) willingness += 12f;

            // What the bloc actually asks of them.
            //
            // Priced through the same `BurdenOf` the bilateral acceptance logic
            // uses, so a defence bloc is genuinely a harder sell than a talking
            // shop and the two routes to an alliance cannot disagree about what a
            // promise is worth. Divided down because a burden owed to a group is
            // shared with that group — which is the honest reason multilateral
            // alliances are easier to build than N bilateral ones, and the whole
            // reason to have this object at all.
            foreach (var commitment in bloc.commitments)
                willingness -= DiplomacySystem.BurdenOf(commitment) * 0.45f;

            // But the same terms are worth more the more states already carry
            // them: a guarantee from seven governments is a different proposition
            // from a guarantee from one.
            if (bloc.commitments.Contains(TreatyCommitment.MutualDefense))
                willingness += Math.Min(24f, Math.Max(0, bloc.memberIds.Count - 1) * 6f);

            // Nobody walks back into a room they walked out of.
            if (bloc.repudiatedBy.Contains(targetId)) willingness -= 30f;

            return Clamp(willingness);
        }

        /// <summary>
        /// Put a member out, or record that they took themselves out.
        ///
        /// Distinct from <see cref="LeaveBy"/> because the two are not the same
        /// act and should not read the same: leaving is a policy, and being
        /// expelled for refusing a call is a disgrace. The expelled state is
        /// remembered in <see cref="Bloc.repudiatedBy"/> so the door does not
        /// simply reopen next month.
        /// </summary>
        public static bool Expel(GameState state, Bloc bloc, string countryId, string why)
        {
            if (bloc == null || !bloc.Has(countryId)) return false;

            var expelled = state.FindCountry(countryId);
            if (expelled == null) return false;

            bool wasLeader = bloc.leaderId == countryId;
            bloc.memberIds.Remove(countryId);
            if (!bloc.repudiatedBy.Contains(countryId)) bloc.repudiatedBy.Add(countryId);

            state.AddChronicle(ChronicleCategory.Diplomatic, countryId,
                $"{expelled.displayName} is out of {bloc.name}: {why}.", Publicity.Public);

            if (expelled.isPlayer)
                state.AddNotification(NotificationClass.Priority, "EXPELLED FROM THE BLOC",
                    $"We are out of {bloc.name} — {why}. Its remaining members will not "
                    + "readmit us on the strength of an apology.", bloc.leaderId,
                    desk: ReportingDesk.Diplomacy);

            if (wasLeader) Dissolve(state, bloc, "its leader would not honour it");
            else if (bloc.memberIds.Count < MinimumMembers)
                Dissolve(state, bloc, "there was nobody left in it");

            return true;
        }

        /// <summary>Actor-generic. They accept on their own interests or they do not.</summary>
        public static bool InviteBy(GameState state, string leaderId, string targetId)
        {
            if (!CanInvite(state, leaderId, targetId, out _)) return false;

            var bloc = BlocOf(state, leaderId);
            var target = state.FindCountry(targetId);
            var leader = state.FindCountry(leaderId);
            if (bloc == null || target == null || leader == null) return false;

            float willingness = JoinWillingness(state, bloc, targetId);
            if (willingness < 50f)
            {
                var relationship = state.FindRelationship(leaderId, targetId);
                if (leader.isPlayer)
                    state.AddNotification(NotificationClass.Advisory, "INVITATION DECLINED",
                        $"{target.displayName} will not join {bloc.name}. "
                        + (relationship != null
                           && relationship.ThreatPerceivedBy(targetId) > 55f
                            ? "They are more worried about us than about anyone we would line "
                              + "up against."
                            : "The relationship is not there yet."),
                        targetId, desk: ReportingDesk.Diplomacy);
                return false;
            }

            Admit(state, bloc, target);
            return true;
        }

        static void Admit(GameState state, Bloc bloc, CountryState member)
        {
            bloc.memberIds.Add(member.id);

            // Joining a side is a public act, and the states on the other side of
            // it read it that way. Standing, never capability.
            foreach (var other in state.countries)
            {
                if (other.id == member.id) continue;
                var relationship = state.FindRelationship(member.id, other.id);
                if (relationship == null) continue;

                if (bloc.Has(other.id))
                {
                    relationship.strategicAlignment = Clamp(relationship.strategicAlignment + 8f);
                    relationship.relations = Clamp(relationship.relations + 3f);
                }
                else if (BlocOf(state, other.id) != null)
                {
                    relationship.strategicAlignment = Clamp(relationship.strategicAlignment - 6f);
                }
            }

            state.AddChronicle(ChronicleCategory.Diplomatic, member.id,
                $"{member.displayName} joins {bloc.name}.", Publicity.Public);

            var leader = state.FindCountry(bloc.leaderId);
            if (leader != null && leader.isPlayer)
                state.AddNotification(NotificationClass.Priority, "BLOC EXPANDS",
                    $"{member.displayName} is in. {bloc.name} now holds {bloc.memberIds.Count} "
                    + "states, and holding it together is a monthly bill.",
                    member.id, desk: ReportingDesk.Diplomacy);
            else if (member.isPlayer)
                state.AddNotification(NotificationClass.Priority, "WE HAVE JOINED",
                    $"We are in {bloc.name}. Our votes will be read as theirs.",
                    bloc.leaderId, desk: ReportingDesk.Diplomacy);
        }

        /// <summary>Player order: spends CP and records the initiative.</summary>
        public static bool Invite(GameState state, TurnManager turns, string targetId)
        {
            if (!AuthoritySystem.EnsureAuthority(state, Pillar.Diplomacy)) return false;
            if (!CanInvite(state, state.playerCountryId, targetId, out string reason))
            {
                GameLog.Warn("DIPL", reason);
                return false;
            }

            if (!turns.SpendCommandPoints(InviteCost, "Invite into the bloc")) return false;

            bool joined = InviteBy(state, state.playerCountryId, targetId);
            ProgressionSystem.AwardXP(state, joined ? 20 : 6, "Bloc invitation");
            ProgressionSystem.RecordInitiative(state);
            return joined;
        }

        // ---------- walking out ----------

        /// <summary>
        /// Leave. Costs trust with everyone still in it — a bloc you can leave for
        /// nothing is a bloc that means nothing.
        /// </summary>
        public static bool LeaveBy(GameState state, string countryId)
        {
            var bloc = BlocOf(state, countryId);
            if (bloc == null) return false;

            var leaver = state.FindCountry(countryId);
            if (leaver == null) return false;

            bool wasLeader = bloc.leaderId == countryId;
            bloc.memberIds.Remove(countryId);

            foreach (string memberId in bloc.memberIds)
            {
                var relationship = state.FindRelationship(countryId, memberId);
                if (relationship == null) continue;
                relationship.trust = Clamp(relationship.trust - 9f);
                relationship.strategicAlignment = Clamp(relationship.strategicAlignment - 10f);
                relationship.AddMemory(state.date, $"Walked out of {bloc.name}.", -0.7f);
            }

            state.AddChronicle(ChronicleCategory.Diplomatic, countryId,
                $"{leaver.displayName} leaves {bloc.name}.", Publicity.Public);

            // A leader who walks out has dissolved it, whatever the members think.
            if (wasLeader) Dissolve(state, bloc, "its leader walked away from it");
            else if (bloc.memberIds.Count < MinimumMembers)
                Dissolve(state, bloc, "there was nobody left in it");

            return true;
        }

        static void Dissolve(GameState state, Bloc bloc, string why)
        {
            bloc.dissolved = true;
            state.AddChronicle(ChronicleCategory.Diplomatic, bloc.leaderId,
                $"{bloc.name} dissolves: {why}.", Publicity.Public);

            foreach (string memberId in bloc.memberIds)
            {
                var member = state.FindCountry(memberId);
                if (member == null || !member.isPlayer) continue;
                state.AddNotification(NotificationClass.Priority, "BLOC DISSOLVED",
                    $"{bloc.name} is finished — {why}.", desk: ReportingDesk.Diplomacy);
            }
            bloc.memberIds.Clear();
        }

        // ---------- the monthly tick ----------

        public static void MonthlyUpdate(GameState state)
        {
            for (int i = state.blocs.Count - 1; i >= 0; i--)
            {
                var bloc = state.blocs[i];
                if (bloc.dissolved) continue;

                var leader = state.FindCountry(bloc.leaderId);
                if (leader == null || !bloc.Has(bloc.leaderId))
                {
                    Dissolve(state, bloc, "it had no leader");
                    continue;
                }

                // What the members' own relations will actually hold.
                bloc.cohesion = Approach(bloc.cohesion, CohesionTargetFor(state, bloc), 0.08f);

                // Leading is work. A leader who cannot pay watches it come apart —
                // the bill is what makes a large bloc a commitment rather than a
                // collection.
                float upkeep = UpkeepPerMember * Math.Max(0, bloc.memberIds.Count - 1);
                if (upkeep > 0f
                    && !GovernmentSystem.SpendPoliticalCapitalBy(
                        state, bloc.leaderId, upkeep, "Hold the bloc together"))
                    bloc.cohesion = Clamp(bloc.cohesion - 4f);

                // What being in it is worth: the members drift into alignment.
                // Small, and through the existing dimension rather than a new one,
                // so everything that already reads alignment reads this too.
                Bind(state, bloc);

                if (bloc.cohesion < CohesionFloor) ShedTheLeastConvinced(state, bloc);

                if (!bloc.dissolved && bloc.memberIds.Count < MinimumMembers
                    && state.date.MonthsSince(bloc.founded) >= 12)
                    Dissolve(state, bloc, "nobody ever joined it");
            }

            ConsiderBlocs(state);
        }

        /// <summary>
        /// The level of cohesion this membership can sustain — the average warmth
        /// and alignment among the members themselves, not the leader's wishes.
        /// </summary>
        public static float CohesionTargetFor(GameState state, Bloc bloc)
        {
            if (bloc.memberIds.Count < 2) return 40f;

            float total = 0f;
            int pairs = 0;

            for (int i = 0; i < bloc.memberIds.Count; i++)
                for (int j = i + 1; j < bloc.memberIds.Count; j++)
                {
                    var relationship = state.FindRelationship(bloc.memberIds[i], bloc.memberIds[j]);
                    if (relationship == null) continue;
                    total += relationship.relations * 0.6f + relationship.strategicAlignment * 0.4f;
                    pairs++;
                }

            if (pairs == 0) return 40f;

            // A shared enemy holds a bloc together better than shared enthusiasm.
            float external = 0f;
            foreach (var other in state.countries)
            {
                if (bloc.Has(other.id)) continue;
                foreach (string memberId in bloc.memberIds)
                {
                    var relationship = state.FindRelationship(memberId, other.id);
                    if (relationship == null) continue;
                    external += Math.Max(0f, relationship.ThreatPerceivedBy(memberId) - 55f) * 0.02f;
                }
            }

            return Clamp(total / pairs + Math.Min(15f, external));
        }

        /// <summary>What membership is worth: a slow pull into alignment.</summary>
        static void Bind(GameState state, Bloc bloc)
        {
            float pull = bloc.cohesion / 100f * 0.45f;

            for (int i = 0; i < bloc.memberIds.Count; i++)
                for (int j = i + 1; j < bloc.memberIds.Count; j++)
                {
                    var relationship = state.FindRelationship(bloc.memberIds[i], bloc.memberIds[j]);
                    if (relationship == null) continue;
                    relationship.strategicAlignment =
                        Clamp(relationship.strategicAlignment + pull);
                }
        }

        /// <summary>
        /// A bloc nobody believes in loses whoever believes least. One at a time,
        /// so a bad month is a warning rather than a collapse.
        /// </summary>
        static void ShedTheLeastConvinced(GameState state, Bloc bloc)
        {
            string worst = null;
            float worstScore = float.MaxValue;

            foreach (string memberId in bloc.memberIds)
            {
                if (memberId == bloc.leaderId) continue;
                float score = JoinWillingness(state, bloc, memberId);
                if (score >= worstScore) continue;
                worstScore = score;
                worst = memberId;
            }

            if (worst == null) return;
            LeaveBy(state, worst);
        }

        /// <summary>
        /// Whether a foreign government founds or grows a bloc.
        ///
        /// Outside the objective budget, on the `ConsiderDetente` precedent: a
        /// standing alignment is not a strategy competing for this month's
        /// actions. Without this the world would only ever have blocs the player
        /// built, which is the most-repeated bug in this codebase.
        /// </summary>
        static void ConsiderBlocs(GameState state)
        {
            int monthIndex = state.date.MonthsSince(state.startDate);
            var rng = new Random(unchecked(state.rngSeed * 9187 + monthIndex * 277));

            // Rarely. A bloc founded every month is a list, not a side.
            if (rng.NextDouble() > 0.10) return;

            // Grow an existing one first: an alignment with members is worth more
            // to the world than another banner with one state under it.
            foreach (var bloc in state.blocs)
            {
                if (bloc.dissolved) continue;
                var leader = state.FindCountry(bloc.leaderId);
                if (leader == null || leader.isPlayer) continue;

                string best = null;
                float bestWillingness = 50f;

                foreach (var candidate in state.countries)
                {
                    if (bloc.Has(candidate.id) || candidate.isPlayer) continue;
                    if (BlocOf(state, candidate.id) != null) continue;

                    float willingness = JoinWillingness(state, bloc, candidate.id);
                    if (willingness <= bestWillingness) continue;
                    bestWillingness = willingness;
                    best = candidate.id;
                }

                if (best != null && GovernmentSystem.SpendPoliticalCapitalBy(
                        state, bloc.leaderId, 1f, "Bloc invitation"))
                {
                    InviteBy(state, bloc.leaderId, best);
                    return;
                }
            }

            if (LiveBlocCount(state) >= MaxBlocs) return;

            // Otherwise the most capable unaligned diplomat founds one.
            CountryState founder = null;
            float bestStanding = LeadershipThreshold;

            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (BlocOf(state, country.id) != null) continue;
                if (country.pillars.diplomacy <= bestStanding) continue;
                bestStanding = country.pillars.diplomacy;
                founder = country;
            }

            if (founder == null) return;
            if (!GovernmentSystem.SpendPoliticalCapitalBy(
                    state, founder.id, 2f, "Found a bloc")) return;

            // A government founds the bloc its own situation argues for. Without
            // this every foreign bloc is a talking shop while the player's is an
            // alliance, which is the most-repeated bug in this codebase wearing a
            // new coat: a verb the AI can technically call but never calls with
            // the arguments that make it matter.
            var terms = new List<TreatyCommitment>();
            if (FeelsThreatened(state, founder.id)) terms.Add(TreatyCommitment.MutualDefense);
            else terms.Add(TreatyCommitment.TradePreference);
            if (founder.pillars.intelligence >= 60f) terms.Add(TreatyCommitment.IntelligenceSharing);

            FoundBy(state, founder.id, null, terms);
        }

        /// <summary>Whether this state has somebody to be frightened of.</summary>
        static bool FeelsThreatened(GameState state, string countryId)
        {
            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(countryId)) continue;
                if (relationship.ThreatPerceivedBy(countryId) > 58f) return true;
            }
            return false;
        }

        static float Approach(float current, float target, float rate)
            => Clamp(current + (target - current) * rate);

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}
