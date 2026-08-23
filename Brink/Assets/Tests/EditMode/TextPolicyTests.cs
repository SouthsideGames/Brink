using System.IO;
using System.Text.RegularExpressions;
using Brink.UI;
using Brink.UI.Views;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Brink.Tests
{
    /// <summary>
    /// One wrapping rule, applied everywhere (GDD §4).
    ///
    /// The shell is a character grid under `white-space: pre`, so nothing wraps
    /// on its own and an over-long line runs straight off the right edge. The
    /// policy that prevents that existed in **two** places — the shell's and the
    /// view base class's — and they drifted: the shell's was widened to cover
    /// `terminal-text-dim` and `terminal-text-bright`, and the view's was not.
    ///
    /// Because a view rebuilds itself on every button press and the *view's* copy
    /// is the one that runs then, every line of secondary explanation in the game
    /// overflowed until something happened to trigger a shell-level refresh.
    /// Folding a phone and reopening it appeared to fix the text, which is what
    /// the bug looked like from the outside.
    ///
    /// Same class as the monthly system list living in two places, and caught the
    /// same way: assert there is only one implementation.
    /// </summary>
    public class TextPolicyTests
    {
        const int Columns = 40;

        static Label Readout(string cssClass, string text)
        {
            var label = new Label(text);
            label.AddToClassList(cssClass);
            return label;
        }

        static void Apply(VisualElement root)
            => TerminalShellController.ApplyTextPolicy(root, 4f, Columns);

        static int LongestLine(string text)
        {
            int longest = 0;
            foreach (var line in text.Split('\n'))
                if (line.Length > longest) longest = line.Length;
            return longest;
        }

        // ---------- every readout class wraps ----------

        [TestCase("terminal-text")]
        [TestCase("terminal-text-dim")]
        [TestCase("terminal-text-bright")]
        public void EveryReadoutClassIsWrapped(string cssClass)
        {
            // `-dim` is what all secondary explanation uses, and secondary
            // explanation is the longest prose in the game. It was excluded.
            var label = Readout(cssClass,
                "We are committed against China. A war that is neither prosecuted nor " +
                "settled costs us every month it runs, and the cost is not only money.");

            var root = new VisualElement();
            root.Add(label);
            Apply(root);

            Assert.LessOrEqual(LongestLine(label.text), Columns,
                $"A '{cssClass}' label was left unwrapped and would run off the right edge.");
        }

        [Test]
        public void AsciiFiguresAreNeverWrapped()
        {
            // A figure is already built to an exact grid; wrapping one corrupts
            // the vertical strokes that make it a picture.
            const string figure = "+--------------------------------------------------+";
            var label = Readout("terminal-text", figure);
            label.AddToClassList("terminal-figure");

            var root = new VisualElement();
            root.Add(label);
            Apply(root);

            Assert.AreEqual(figure, label.text, "An ASCII figure was wrapped and corrupted.");
        }

        [Test]
        public void ThePolicyReachesNestedContent()
        {
            // Views build rows inside rows; a shallow pass would miss most of it.
            var deep = Readout("terminal-text-dim",
                "Strategic materials at 26. Industry and procurement both draw on this, and " +
                "we cannot buy our way past the shortfall.");

            var root = new VisualElement();
            var middle = new VisualElement();
            var inner = new VisualElement();
            inner.Add(deep);
            middle.Add(inner);
            root.Add(middle);

            Apply(root);

            Assert.LessOrEqual(LongestLine(deep.text), Columns,
                "Nested content escaped the wrapper.");
        }

        // ---------- there is only one implementation ----------

        [Test]
        public void TheViewBaseClassDoesNotKeepItsOwnCopyOfTheRule()
        {
            // The actual regression guard. `TerminalView.FormatText` must
            // delegate rather than re-implement, or the two drift again and the
            // failure is invisible until someone plays on a phone.
            string source = File.ReadAllText(Path.Combine(
                Directory.GetCurrentDirectory(), "Assets/Scripts/UI/Views/TerminalView.cs"));

            var body = Regex.Match(source,
                @"public static void FormatText\(VisualElement element\)(.*?)(?=\n        /// <summary>)",
                RegexOptions.Singleline);
            Assert.IsTrue(body.Success, "FormatText not found — has it been renamed?");

            StringAssert.Contains("ApplyTextPolicy", body.Groups[1].Value,
                "FormatText no longer delegates to the shell's single implementation.");
            Assert.IsFalse(body.Groups[1].Value.Contains("WrapBlock"),
                "FormatText has grown its own copy of the wrapping rule again. There must be " +
                "exactly one implementation — the last time there were two, every line of " +
                "secondary explanation in the game ran off the right edge.");
        }

        [Test]
        public void TheShellAppliesThePolicyToOverlaysNotJustTheContentHost()
        {
            // The monthly briefing, the crisis modal and the tutorial panel are
            // siblings of the content host, not children of it. A policy applied
            // to the content host alone never reached a single line of any of
            // them — which is why the briefing's wire lines overflowed.
            string source = File.ReadAllText(Path.Combine(
                Directory.GetCurrentDirectory(), "Assets/Scripts/UI/TerminalShellController.cs"));

            Assert.IsFalse(
                Regex.IsMatch(source, @"ApplyTextPolicy\(\s*contentHost\s*,"),
                "The shell still wraps only the content host. Overlays are siblings of it, so " +
                "their prose is never wrapped.");
            StringAssert.Contains("ApplyTextPolicy(root,", source,
                "The shell must walk the whole tree so overlays are covered.");
        }
    }
}
