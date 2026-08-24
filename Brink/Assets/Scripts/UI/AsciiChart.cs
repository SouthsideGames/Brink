using System;
using System.Text;

namespace Brink.UI
{
    /// <summary>
    /// ASCII/terminal presentation primitives (GDD §4): bars, box headers and
    /// dividers rendered as text for monospace display. Plain C# — unit tested.
    /// </summary>
    public static class AsciiChart
    {
        public const char FillChar = '█';
        public const char EmptyChar = '░';

        /// <summary>Horizontal bar, e.g. "██████░░░░" for 60% at width 10.</summary>
        public static string Bar(float value, float max, int width)
        {
            if (width <= 0) return string.Empty;
            if (max <= 0f) max = 1f;
            float t = Math.Max(0f, Math.Min(1f, value / max));
            int filled = (int)Math.Round(t * width);
            return new string(FillChar, filled) + new string(EmptyChar, width - filled);
        }

        /// <summary>
        /// Padded label + bar + numeric value, e.g.
        /// "MILITARY     ██████░░░░  52.3".
        /// </summary>
        public static string LabeledBar(string label, float value, float max, int labelWidth, int barWidth)
        {
            label = label ?? string.Empty;
            if (label.Length > labelWidth) label = label.Substring(0, labelWidth);
            return $"{label.PadRight(labelWidth)} {Bar(value, max, barWidth)} {value,5:F1}";
        }

        /// <summary>Box-style section header, e.g. "┌─ TITLE ──────┐".</summary>
        public static string BoxHeader(string title, int width)
        {
            title = (title ?? string.Empty).ToUpperInvariant();
            string core = $"┌─ {title} ";
            if (core.Length >= width - 1)
                return core + "┐";
            return core + new string('─', width - core.Length - 1) + "┐";
        }

        public static string Divider(int width) => new string('─', Math.Max(0, width));

        /// <summary>
        /// A table cell of exactly <paramref name="width"/> characters: padded if
        /// short, truncated with an ellipsis if long.
        ///
        /// Fixed paddings like <c>{name,-22}</c> are what still push a row off a
        /// narrow screen even after the surrounding box is sized correctly — the
        /// rule fits and the row inside it does not. Derive the width from the
        /// terminal instead of assuming one.
        /// </summary>
        public static string Cell(string text, int width)
        {
            width = Math.Max(1, width);
            text = text ?? "";

            if (text.Length == width) return text;
            if (text.Length < width) return text.PadRight(width);
            return width == 1 ? "…" : text.Substring(0, width - 1) + "…";
        }

        /// <summary>
        /// Hard-wrap a block of terminal text to <paramref name="width"/>.
        ///
        /// The shell renders under `white-space: pre` because that is the only
        /// way ASCII bars and box rules stay aligned — which also means nothing
        /// wraps by itself and any over-long line simply runs off the right edge.
        /// Sizing the *boxes* to the panel was not enough: notification bodies and
        /// other prose are arbitrary length and had nothing constraining them.
        ///
        /// Wrapped rather than truncated: a briefing that silently loses its
        /// second half is worse than one that takes two lines. Continuation lines
        /// carry a hanging indent so the wrap reads as one entry rather than two.
        /// </summary>
        public static string WrapBlock(string block, int width)
        {
            if (string.IsNullOrEmpty(block) || width < 12) return block;

            var lines = block.Replace("\r", "").Split('\n');
            var sb = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                AppendWrapped(sb, lines[i], width);
                if (i < lines.Length - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Characters a line actually occupies, ignoring rich-text markup.
        ///
        /// `&lt;color=#FF6E64&gt;…&lt;/color&gt;` is 24 invisible characters. Counting them
        /// toward the width wrapped a coloured line two dozen characters early —
        /// which is exactly what happened to FLASH notifications.
        /// </summary>
        public static int VisibleLength(string line)
        {
            if (string.IsNullOrEmpty(line)) return 0;

            int length = 0;
            bool inTag = false;
            foreach (char c in line)
            {
                if (c == '<') { inTag = true; continue; }
                if (inTag) { if (c == '>') inTag = false; continue; }
                length++;
            }
            return length;
        }

        static void AppendWrapped(StringBuilder sb, string line, int width)
        {
            // Measured on visible characters, but sliced on real ones — so a line
            // carrying markup is not wrapped early, and a tag is never cut in half.
            if (VisibleLength(line) <= width)
            {
                sb.Append(line);
                return;
            }
            if (line.IndexOf('<') >= 0)
            {
                // Markup present: leave it alone rather than risk splitting a tag.
                // Views should prefer a label per coloured line (see BriefingView).
                sb.Append(line);
                return;
            }

            int leading = 0;
            while (leading < line.Length && line[leading] == ' ') leading++;

            // Hanging indent, bounded so a deeply indented line still has room
            // to say something.
            string hang = new string(' ', Math.Min(leading + 2, Math.Max(0, width - 20)));

            int start = 0;
            bool first = true;

            while (start < line.Length)
            {
                int available = width - (first ? 0 : hang.Length);
                if (available < 8) available = 8;

                if (line.Length - start <= available)
                {
                    if (!first) sb.Append(hang);
                    sb.Append(line.Substring(start));
                    return;
                }

                // Break on the last space that fits, so words stay whole.
                int limit = Math.Min(start + available, line.Length - 1);
                int breakAt = -1;
                for (int i = limit; i > start; i--)
                {
                    if (line[i] != ' ') continue;
                    breakAt = i;
                    break;
                }
                if (breakAt <= start) breakAt = start + available; // one very long word

                if (!first) sb.Append(hang);
                sb.Append(line.Substring(start, breakAt - start).TrimEnd());
                sb.Append('\n');

                start = breakAt;
                while (start < line.Length && line[start] == ' ') start++;
                first = false;
            }
        }

        /// <summary>
        /// Share of the terminal width to give a name column: roughly a third,
        /// kept inside sane bounds so it neither vanishes on a phone nor sprawls
        /// on a tablet.
        /// </summary>
        public static int NameWidth(int terminalWidth, float share = 0.34f)
            => Math.Max(10, Math.Min(30, (int)(terminalWidth * share)));

        /// <summary>Two-column terminal row, e.g. "TREASURY ........ 1240".</summary>
        public static string Row(string label, string value, int width)
        {
            label = label ?? string.Empty;
            value = value ?? string.Empty;
            int dots = width - label.Length - value.Length - 2;
            if (dots < 1) dots = 1;
            return $"{label} {new string('.', dots)} {value}";
        }

        /// <summary>
        /// Multi-row ASCII line chart for a series (GDD §20.1 market index).
        /// Newest value last; returns `height` rows plus a scale legend.
        /// </summary>
        /// <summary>
        /// Columns a <see cref="LineChart"/> row spends on its scale gutter:
        /// eight for the value, one space, one rule. Named because the plot width
        /// and the gutter drawing have to agree, and they silently did not.
        /// </summary>
        public const int GutterColumns = 10;

        public static string LineChart(float[] values, int width, int height)
        {
            if (values == null || values.Length == 0 || width <= 0 || height <= 0)
                return string.Empty;

            // Sample the tail of the series to fit the available width.
            //
            // **`width` is the width of the whole figure, gutter included.** Each
            // row is `"{value,8:F1} │"` — eight columns of scale, a space and a
            // rule — so the plot area is `width - GutterColumns`, and this used to
            // plot `width` points on top of that: every chart came out ten columns
            // wider than it was told to be.
            //
            // It was invisible until it wasn't. `marketHistory` gains an entry per
            // resolved month, so the line grew one column every END MONTH — fitting
            // fine for the first three years, then creeping off the right edge of a
            // phone one character at a time. Figures opt out of `ApplyTextPolicy`
            // by design (wrapping would corrupt the vertical strokes), so nothing
            // downstream catches it.
            int plotWidth = Math.Max(1, width - GutterColumns);
            int count = Math.Min(plotWidth, values.Length);
            var sampled = new float[count];
            for (int i = 0; i < count; i++)
                sampled[i] = values[values.Length - count + i];

            float min = sampled[0], max = sampled[0];
            foreach (float v in sampled)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (Math.Abs(max - min) < 0.0001f) { max = min + 1f; }

            var rows = new char[height][];
            for (int r = 0; r < height; r++)
            {
                rows[r] = new char[count];
                for (int c = 0; c < count; c++) rows[r][c] = ' ';
            }

            for (int c = 0; c < count; c++)
            {
                float t = (sampled[c] - min) / (max - min);
                int row = height - 1 - (int)Math.Round(t * (height - 1));
                row = Math.Max(0, Math.Min(height - 1, row));
                rows[row][c] = c > 0 && sampled[c] < sampled[c - 1] ? '\\'
                             : c > 0 && sampled[c] > sampled[c - 1] ? '/'
                             : '─';
            }

            var sb = new StringBuilder();
            for (int r = 0; r < height; r++)
            {
                float rowValue = max - (max - min) * r / Math.Max(1, height - 1);
                sb.Append($"{rowValue,8:F1} │");
                sb.Append(rows[r]);
                sb.AppendLine();
            }
            sb.Append("         └").Append(new string('─', count));
            return sb.ToString();
        }

        /// <summary>Simple multi-line sparkline column chart for a series, newest last.</summary>
        public static string Sparkline(float[] values, float max)
        {
            if (values == null || values.Length == 0) return string.Empty;
            if (max <= 0f) max = 1f;
            const string levels = " ▁▂▃▄▅▆▇█";
            var sb = new StringBuilder(values.Length);
            foreach (float v in values)
            {
                float t = Math.Max(0f, Math.Min(1f, v / max));
                sb.Append(levels[(int)Math.Round(t * (levels.Length - 1))]);
            }
            return sb.ToString();
        }
    }
}
