using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Read-only operator home-page model. It composes already-authoritative
    /// systems instead of inventing a second priority engine.
    /// </summary>
    public static class CommandCenterSystem
    {
        public sealed class Section
        {
            public string title;
            public string viewId;
            public int urgency;
            public string summary;
        }

        public static List<Section> Build(GameState state)
        {
            var result = new List<Section>();
            if (state == null || state.PlayerCountry == null) return result;

            var attention = AttentionSystem.Collect(state);
            int decisions = AttentionSystem.DecisionCount(attention);
            if (decisions > 0)
                result.Add(new Section {
                    title = "DECISIONS WAITING", viewId = "BRIEFING", urgency = 5,
                    summary = decisions + " unresolved decision" + (decisions == 1 ? "" : "s") + " require operator judgement."
                });

            var board = StrategicBoardSystem.Build(state);
            if (board.Count > 0)
            {
                var lead = board[0];
                result.Add(new Section {
                    title = "STRATEGIC PRESSURE", viewId = "BRIEFING", urgency = Math.Max(1, lead.urgency),
                    summary = lead.label + " is " + lead.direction.ToLowerInvariant() + ". " + DriverText(lead)
                });
            }

            var meeting = CabinetMeetingSystem.Build(state);
            if (meeting.Count > 0)
            {
                var lead = meeting[0];
                result.Add(new Section {
                    title = "CABINET PRESSURE", viewId = "CABINET", urgency = Math.Max(1, lead.pressure),
                    summary = lead.title + " " + lead.officialName + ": " + lead.position
                });
            }

            var surprises = StrategicSurpriseSystem.Assess(state);
            if (surprises.Count > 0)
            {
                surprises.Sort((a, b) => b.severity.CompareTo(a.severity));
                var lead = surprises[0];
                result.Add(new Section {
                    title = "UNCERTAINTY", viewId = "INTELLIGENCE", urgency = Math.Max(1, lead.severity),
                    summary = lead.title + " — " + lead.known
                });
            }

            var era = StrategicEraSystem.Current(state);
            if (era != null)
                result.Add(new Section {
                    title = "STANDING COURSE", viewId = "STRATEGIST", urgency = 0,
                    summary = era.name + " — " + era.character
                });

            result.Sort((a, b) => {
                int urgency = b.urgency.CompareTo(a.urgency);
                return urgency != 0 ? urgency : string.CompareOrdinal(a.title, b.title);
            });
            return result;
        }

        public static string Render(GameState state, int maxSections = 5)
        {
            if (state == null || state.PlayerCountry == null)
                return "COMMAND CENTER — NO ACTIVE POSTING.";

            var sections = Build(state);
            var sb = new StringBuilder();
            sb.Append("COMMAND CENTER — ").AppendLine(state.date.DisplayString);
            sb.Append("CP ").Append(state.commandPoints.current).Append("   ")
              .Append(state.PlayerCountry.displayName.ToUpperInvariant()).AppendLine();
            sb.AppendLine("WHAT DESERVES YOUR ATTENTION");

            int shown = 0;
            foreach (var section in sections)
            {
                if (shown++ >= Math.Max(1, maxSections)) break;
                string glyph = section.urgency >= 4 ? "!" : section.urgency >= 2 ? ">" : ".";
                sb.Append(glyph).Append(" [").Append(section.viewId).Append("] ")
                  .AppendLine(section.title);
                sb.Append("  ").AppendLine(section.summary);
            }

            if (shown == 0)
                sb.AppendLine(". NO EXCEPTIONAL PRESSURE. THE WORLD WILL STILL MOVE IF YOU END THE MONTH.");

            sb.Append("THE CENTER RANKS ATTENTION. IT DOES NOT MAKE THE DECISION FOR YOU.");
            return sb.ToString();
        }

        static string DriverText(StrategicBoardSystem.Item item)
        {
            if (string.IsNullOrEmpty(item.driver))
                return item.incomplete ? "Reporting is incomplete." : "No dominant disclosed driver.";
            return "Primary disclosed driver: " + item.driver + ".";
        }
    }
}
