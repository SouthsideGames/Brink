using System;
using Brink.Data;

namespace Brink.UI
{
    public enum WorldMapMode
    {
        Political,
        Military,
        Trade,
        Intelligence,
        Blocs
    }

    /// <summary>
    /// Live overlays for the strategic map. The base silhouette remains the same
    /// orientation aid; modes answer different strategic questions with routes
    /// and signals drawn from the current save. Player-private layers only read
    /// information the player's government actually owns.
    /// </summary>
    public static class AsciiMapModes
    {
        public static string Render(GameState state, string selectedCountryId,
            WorldMapMode mode, int columns, int rows)
        {
            string baseMap = AsciiWorldMap.Render(state, selectedCountryId, columns, rows);
            if (mode == WorldMapMode.Political) return baseMap;

            var canvas = AsciiCanvas.FromText(baseMap);
            switch (mode)
            {
                case WorldMapMode.Military: DrawMilitary(state, canvas); break;
                case WorldMapMode.Trade: DrawTrade(state, canvas); break;
                case WorldMapMode.Intelligence: DrawIntelligence(state, canvas); break;
                case WorldMapMode.Blocs: DrawBlocs(state, canvas); break;
            }
            return canvas.ToString();
        }

        /// <summary>
        /// The same five visual grammars used elsewhere in Phase C, paired to the
        /// question this map mode asks. Political is the Government picture;
        /// Trade is Economy; Blocs is Diplomacy.
        /// </summary>
        public static string Signature(GameState state, WorldMapMode mode, int width)
        {
            Pillar pillar;
            switch (mode)
            {
                case WorldMapMode.Military: pillar = Pillar.Military; break;
                case WorldMapMode.Trade: pillar = Pillar.Economy; break;
                case WorldMapMode.Intelligence: pillar = Pillar.Intelligence; break;
                case WorldMapMode.Blocs: pillar = Pillar.Diplomacy; break;
                default: pillar = Pillar.Government; break;
            }
            return AsciiPillarArt.Render(state, pillar, width);
        }

        public static string Legend(WorldMapMode mode)
        {
            switch (mode)
            {
                case WorldMapMode.Military:
                    return "  × active confrontation   * total-war front   ! occupied ground";
                case WorldMapMode.Trade:
                    return "  · our trade route   x embargoed route   ! sanctions involving us";
                case WorldMapMode.Intelligence:
                    return "  : collection network   ? thin access   + established access   # deep access";
                case WorldMapMode.Blocs:
                    return "  = members of the same standing bloc   uppercase code = state";
                default:
                    return AsciiWorldMap.Legend;
            }
        }

        public static string Summary(GameState state, WorldMapMode mode)
        {
            switch (mode)
            {
                case WorldMapMode.Military:
                {
                    int fronts = 0, total = 0;
                    foreach (var c in state.confrontations)
                        if (!c.resolved) { fronts++; if (c.escalation == EscalationState.TotalWar) total++; }
                    int occupied = 0;
                    foreach (var l in state.locations) if (l.IsOccupied) occupied++;
                    return $"ACTIVE FRONTS {fronts}   TOTAL WAR {total}   OCCUPIED SITES {occupied}";
                }
                case WorldMapMode.Trade:
                {
                    int links = 0, embargoes = 0, sanctions = 0;
                    foreach (var t in state.trade)
                        if (t.Involves(state.playerCountryId)) { links++; if (t.embargoed) embargoes++; }
                    foreach (var s in state.sanctions)
                        if (s.senderId == state.playerCountryId || s.targetId == state.playerCountryId) sanctions++;
                    return $"OUR TRADE LINKS {links}   EMBARGOED {embargoes}   SANCTIONS INVOLVING US {sanctions}";
                }
                case WorldMapMode.Intelligence:
                {
                    int networks = 0, deep = 0;
                    foreach (var n in state.networks)
                        if (n.ownerId == state.playerCountryId && !n.compromised)
                        { networks++; if (n.penetration >= 55f) deep++; }
                    return $"ACTIVE NETWORKS {networks}   DEEP ACCESS {deep}";
                }
                case WorldMapMode.Blocs:
                {
                    int active = 0, ours = 0;
                    foreach (var b in state.blocs)
                        if (!b.dissolved) { active++; if (b.Has(state.playerCountryId)) ours++; }
                    return $"ACTIVE BLOCS {active}   OUR MEMBERSHIPS {ours}";
                }
                default:
                    return "PUBLIC STANDING AND CURRENT TERRITORIAL CONTROL";
            }
        }

        static void DrawMilitary(GameState state, AsciiCanvas canvas)
        {
            foreach (var front in state.confrontations)
            {
                if (front.resolved) continue;
                if (!Point(front.initiatorId, canvas, out int ax, out int ay)
                    || !Point(front.defenderId, canvas, out int bx, out int by)) continue;
                canvas.Line(ax, ay, bx, by,
                    front.escalation == EscalationState.TotalWar ? '*' : '×', overwrite: false);
            }

            foreach (var location in state.locations)
            {
                if (!location.IsOccupied) continue;
                if (!Point(location.ownerId, canvas, out int x, out int y)) continue;
                canvas.Plot(x, Math.Min(canvas.Height - 1, y + 1), '!', overwrite: true);
            }
        }

        static void DrawTrade(GameState state, AsciiCanvas canvas)
        {
            foreach (var trade in state.trade)
            {
                if (!trade.Involves(state.playerCountryId)) continue;
                if (!Point(trade.countryA, canvas, out int ax, out int ay)
                    || !Point(trade.countryB, canvas, out int bx, out int by)) continue;
                canvas.Line(ax, ay, bx, by, trade.embargoed ? 'x' : '·', overwrite: false);
            }

            foreach (var sanction in state.sanctions)
            {
                if (sanction.senderId != state.playerCountryId && sanction.targetId != state.playerCountryId) continue;
                string other = sanction.senderId == state.playerCountryId ? sanction.targetId : sanction.senderId;
                if (!Point(other, canvas, out int x, out int y)) continue;
                canvas.Plot(x, Math.Max(0, y - 1), '!', overwrite: true);
            }
        }

        static void DrawIntelligence(GameState state, AsciiCanvas canvas)
        {
            if (!Point(state.playerCountryId, canvas, out int px, out int py)) return;
            foreach (var network in state.networks)
            {
                if (network.ownerId != state.playerCountryId || network.compromised) continue;
                if (!Point(network.targetId, canvas, out int tx, out int ty)) continue;
                canvas.Line(px, py, tx, ty, ':', overwrite: false);
                char access = network.penetration >= 55f ? '#'
                    : network.penetration >= 20f ? '+' : '?';
                canvas.Plot(tx, Math.Max(0, ty - 1), access, overwrite: true);
            }
        }

        static void DrawBlocs(GameState state, AsciiCanvas canvas)
        {
            foreach (var bloc in state.blocs)
            {
                if (bloc.dissolved || bloc.memberIds.Count < 2) continue;
                if (!Point(bloc.leaderId, canvas, out int lx, out int ly)) continue;
                foreach (var member in bloc.memberIds)
                {
                    if (member == bloc.leaderId) continue;
                    if (!Point(member, canvas, out int mx, out int my)) continue;
                    canvas.Line(lx, ly, mx, my, '=', overwrite: false);
                }
            }
        }

        static bool Point(string countryId, AsciiCanvas canvas, out int x, out int y)
        {
            x = y = 0;
            var profile = WorldFactory.FindProfile(countryId);
            if (profile == null) return false;
            float sx = (float)canvas.Width / AsciiWorldMap.Width;
            float sy = (float)canvas.Height / AsciiWorldMap.Height;
            x = Math.Max(0, Math.Min(canvas.Width - 1, (int)Math.Round((profile.mapX + 1.5f) * sx)));
            y = Math.Max(0, Math.Min(canvas.Height - 1, (int)Math.Round(profile.mapY * sy)));
            return true;
        }
    }
}