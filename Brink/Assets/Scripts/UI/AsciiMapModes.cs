using System;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;

namespace Brink.UI
{
    public enum WorldMapMode
    {
        Political,
        Military,
        Trade,
        Intelligence,
        Blocs,
        Activity
    }

    /// <summary>
    /// Live overlays for the strategic map. Overlay glyphs are deliberately
    /// distinct from the base political grammar (! ~ + - and #) so a mode never
    /// asks one character to mean two strategic facts at once.
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
                case WorldMapMode.Activity: DrawActivity(state, canvas); break;
            }
            return canvas.ToString();
        }

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
                    return "  × confrontation   * total-war front   O occupied ground";
                case WorldMapMode.Trade:
                    return "  · our trade route   x embargoed route   $ sanctions involving us";
                case WorldMapMode.Intelligence:
                    return "  : collection route   ? thin access   ^ established access   @ deep access";
                case WorldMapMode.Blocs:
                    return "  = standing bloc connection   uppercase code = state";
                case WorldMapMode.Activity:
                    return "  • one public event last month   * multiple public events";
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
                case WorldMapMode.Activity:
                {
                    var activity = RecentActivity(state);
                    int events = 0;
                    foreach (int count in activity.Values) events += count;
                    return $"PUBLIC EVENTS LAST MONTH {events}   ACTIVE STATES {activity.Count}";
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

            var claimed = new HashSet<int>();
            foreach (var location in state.locations)
            {
                if (!location.IsOccupied) continue;
                if (!Point(GeographySystem.HostOf(location), canvas, out int x, out int y)) continue;
                PlotSignal(canvas, claimed, x, y, +1, 'O');
            }
        }

        static void DrawTrade(GameState state, AsciiCanvas canvas)
        {
            var claimed = new HashSet<int>();
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
                PlotSignal(canvas, claimed, x, y, -1, '$');
            }
        }

        static void DrawIntelligence(GameState state, AsciiCanvas canvas)
        {
            var claimed = new HashSet<int>();
            if (!Point(state.playerCountryId, canvas, out int px, out int py)) return;
            foreach (var network in state.networks)
            {
                if (network.ownerId != state.playerCountryId || network.compromised) continue;
                if (!Point(network.targetId, canvas, out int tx, out int ty)) continue;
                canvas.Line(px, py, tx, ty, ':', overwrite: false);
                char access = network.penetration >= 55f ? '@'
                    : network.penetration >= 20f ? '^' : '?';
                PlotSignal(canvas, claimed, tx, ty, -1, access);
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

        static void DrawActivity(GameState state, AsciiCanvas canvas)
        {
            var claimed = new HashSet<int>();
            foreach (var pair in RecentActivity(state))
            {
                if (!Point(pair.Key, canvas, out int x, out int y)) continue;
                PlotSignal(canvas, claimed, x, y, -1, pair.Value > 1 ? '*' : '•');
            }
        }

        static Dictionary<string, int> RecentActivity(GameState state)
        {
            var counts = new Dictionary<string, int>();
            foreach (var item in WorldWire.LastMonth(state))
            {
                if (state.FindCountry(item.countryId) == null
                    || WorldFactory.FindProfile(item.countryId) == null) continue;
                counts[item.countryId] = counts.TryGetValue(item.countryId, out int count)
                    ? count + 1 : 1;
            }
            return counts;
        }

        /// <summary>
        /// Place an overlay's point marker beside a country without erasing the
        /// map's own labels.
        ///
        /// Every overlay used to plot straight onto `y - 1` (or `y + 1`) with
        /// `overwrite: true`. That cell is only reliably free at full scale: the
        /// map is squeezed from <see cref="AsciiWorldMap.Height"/> into 11, 17 or
        /// 23 rows, and once rows collapse the cell above one country is the code
        /// row of another. Measured on the authored roster, 14 of 24 countries
        /// collide at 23 rows and 21 of 24 at 11 rows. ACTIVITY only made it
        /// visible, because it can mark every state at once where trade and
        /// intelligence mark a handful.
        ///
        /// So the preferred cell is tried first and kept whenever it is free,
        /// then the cells immediately around it, and the marker is dropped only
        /// if a country's own label has genuinely boxed it in. The order is
        /// fixed, so the same world always draws the same map.
        /// </summary>
        static void PlotSignal(AsciiCanvas canvas, HashSet<int> claimed,
            int x, int y, int preferred, char glyph)
        {
            int away = preferred >= 0 ? 1 : -1;
            var candidates = new[]
            {
                (x, y + away), (x, y - away),
                (x - 1, y + away), (x + 1, y + away),
                (x - 1, y - away), (x + 1, y - away),
            };

            foreach (var (cx, cy) in candidates)
            {
                if (cx < 0 || cx >= canvas.Width || cy < 0 || cy >= canvas.Height) continue;

                if (IsCountryLabel(canvas.At(cx, cy))) continue;

                // A cell another state's signal already took this render. The
                // canvas cannot answer this on its own: a marker is not a
                // country label, so the label test waved it through and the
                // later state simply erased the earlier one — leaving the
                // summary counting activity the map no longer showed.
                if (!claimed.Add(cy * canvas.Width + cx)) continue;

                canvas.Plot(cx, cy, glyph, overwrite: true);
                return;
            }
        }

        /// <summary>
        /// A cell the base map has spent on a country's identity: the two-letter
        /// code, the player's brackets, or the selection arrows. Terrain uses no
        /// letters or digits, so this cannot mistake scenery for a label.
        /// </summary>
        static bool IsCountryLabel(char cell)
            => char.IsLetterOrDigit(cell)
               || cell == '[' || cell == ']' || cell == '<' || cell == '>';

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
