using System;
using System.Text;

namespace Brink.UI
{
    /// <summary>
    /// Small retained-mode ASCII surface for terminal art. Views build scenes by
    /// layering glyphs onto a fixed grid, then render once. Keeping clipping and
    /// line drawing here prevents every future map/chart from inventing its own
    /// off-by-one rules.
    /// </summary>
    public sealed class AsciiCanvas
    {
        readonly char[][] cells;
        public int Width { get; }
        public int Height { get; }

        public AsciiCanvas(int width, int height, char fill = ' ')
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            cells = new char[Height][];
            for (int y = 0; y < Height; y++)
            {
                cells[y] = new char[Width];
                for (int x = 0; x < Width; x++) cells[y][x] = fill;
            }
        }

        public static AsciiCanvas FromText(string text)
        {
            text = (text ?? "").Replace("\r", "");
            var lines = text.Split('\n');
            int width = 1;
            foreach (var line in lines) width = Math.Max(width, line.Length);
            var canvas = new AsciiCanvas(width, Math.Max(1, lines.Length));
            for (int y = 0; y < lines.Length; y++) canvas.Text(0, y, lines[y]);
            return canvas;
        }

        public char At(int x, int y)
            => InBounds(x, y) ? cells[y][x] : '\0';

        public void Plot(int x, int y, char glyph, bool overwrite = true)
        {
            if (!InBounds(x, y)) return;
            if (!overwrite && cells[y][x] != ' ') return;
            cells[y][x] = glyph;
        }

        public void Text(int x, int y, string text, bool overwrite = true)
        {
            if (string.IsNullOrEmpty(text) || y < 0 || y >= Height) return;
            for (int i = 0; i < text.Length; i++) Plot(x + i, y, text[i], overwrite);
        }

        public void Line(int x0, int y0, int x1, int y1, char glyph = '·', bool overwrite = false)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Plot(x0, y0, glyph, overwrite);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public void Box(int x, int y, int width, int height)
        {
            if (width < 2 || height < 2) return;
            for (int xx = x + 1; xx < x + width - 1; xx++)
            {
                Plot(xx, y, '─');
                Plot(xx, y + height - 1, '─');
            }
            for (int yy = y + 1; yy < y + height - 1; yy++)
            {
                Plot(x, yy, '│');
                Plot(x + width - 1, yy, '│');
            }
            Plot(x, y, '┌'); Plot(x + width - 1, y, '┐');
            Plot(x, y + height - 1, '└'); Plot(x + width - 1, y + height - 1, '┘');
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int y = 0; y < Height; y++)
            {
                sb.Append(cells[y]);
                if (y < Height - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
    }
}