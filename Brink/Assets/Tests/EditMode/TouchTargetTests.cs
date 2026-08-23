using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Brink.UI;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Touch targets for a landscape phone game (GDD §4).
    ///
    /// Every control was between 23 and 35 panel pixels tall, which is why the
    /// terminal felt fiddly on a real handset. Like `ReadabilityTests`, these
    /// read the **real** stylesheet rather than a copy of its numbers, so a
    /// padding tweak that quietly shrinks a button fails here instead of on a
    /// device.
    ///
    /// **Why 44 panel pixels, with no DPI anywhere.** The shell is a
    /// `ConstantPixelSize` panel whose scale is derived from resolution to hit a
    /// column target (`TerminalScale`) — deliberately, because `Screen.dpi` is
    /// not trustworthy and collapses the whole UI when a platform lies about it.
    /// That makes a panel pixel proportional to physical size on every device, so
    /// the target can be stated in panel pixels and checked without hardware.
    /// The familiar 44pt guideline is about 3.4× body text; body text here is
    /// 13px, so 44 panel px is the direct analogue and lands near 7mm on a phone.
    /// </summary>
    public class TouchTargetTests
    {
        const string StylesheetPath = "Assets/Resources/UI/TerminalShell.uss";

        /// <summary>Minimum comfortable target, in panel pixels. See the class note.</summary>
        const int MinimumTargetPx = 44;

        /// <summary>Every selector the operator actually taps or drags.</summary>
        static readonly string[] InteractiveSelectors =
        {
            ".cmd-button",
            ".nav-button",
            ".unity-base-slider"
        };

        static string Stylesheet()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), StylesheetPath);
            Assert.IsTrue(File.Exists(path), $"Stylesheet not found at {path}");
            return File.ReadAllText(path);
        }

        /// <summary>The body of the first rule whose selector is exactly this one.</summary>
        static string Block(string uss, string selector)
        {
            var match = Regex.Match(uss,
                @"(?<![-a-zA-Z.])" + Regex.Escape(selector) + @"\s*\{(.*?)\}",
                RegexOptions.Singleline);
            Assert.IsTrue(match.Success, $"No rule found for {selector}.");
            return match.Groups[1].Value;
        }

        static int? PixelValue(string block, string property)
        {
            var match = Regex.Match(block,
                @"(?<![-a-zA-Z])" + Regex.Escape(property) + @"\s*:\s*(\d+)px");
            return match.Success ? int.Parse(match.Groups[1].Value) : (int?)null;
        }

        // ---------- the guarantee ----------

        [Test]
        public void EveryInteractiveControlIsBigEnoughToHit()
        {
            string uss = Stylesheet();

            foreach (var selector in InteractiveSelectors)
            {
                int? minHeight = PixelValue(Block(uss, selector), "min-height");

                Assert.IsNotNull(minHeight,
                    $"{selector} declares no min-height, so its size is whatever padding and " +
                    "font-size happen to add up to. That is how every control in this game " +
                    "ended up between 23 and 35 pixels tall.");
                Assert.GreaterOrEqual(minHeight.Value, MinimumTargetPx,
                    $"{selector} is {minHeight}px against a {MinimumTargetPx}px minimum.");
            }
        }

        [Test]
        public void NoBreakpointShrinksAControlBelowTheMinimum()
        {
            // The trap this guards: a breakpoint that buys back screen space by
            // shrinking controls. A short screen may take smaller *text*; it may
            // not take an unhittable button, because a control you cannot
            // reliably press is broken whatever shape the screen is.
            string uss = Stylesheet();

            foreach (Match match in Regex.Matches(uss,
                         @"\.(bp-[a-z]+)\s+(\.[a-z-]+)\s*\{(.*?)\}", RegexOptions.Singleline))
            {
                string selector = match.Groups[2].Value;
                bool interactive = false;
                foreach (var known in InteractiveSelectors)
                    if (known == selector) { interactive = true; break; }
                if (!interactive) continue;

                int? overridden = PixelValue(match.Groups[3].Value, "min-height");
                if (overridden == null) continue; // inherits the base rule, which is the point

                Assert.GreaterOrEqual(overridden.Value, MinimumTargetPx,
                    $"{match.Groups[1].Value} {selector} overrides min-height down to " +
                    $"{overridden}px. Shrink the text on a cramped screen, never the target.");
            }
        }

        [Test]
        public void TheCompactNavRailHasRoomForItsButtons()
        {
            // In compact layout the rail becomes a horizontal strip with a fixed
            // height, so it can silently squash the buttons inside it however
            // large they claim to be.
            string uss = Stylesheet();
            var match = Regex.Match(uss, @"\.bp-compact\s+\.nav-rail\s*\{(.*?)\}", RegexOptions.Singleline);
            Assert.IsTrue(match.Success, "No compact nav rail rule.");

            int? height = PixelValue(match.Groups[1].Value, "height");
            Assert.IsNotNull(height);

            int margins = PixelValue(Block(uss, ".nav-button"), "margin") ?? 2;
            Assert.GreaterOrEqual(height.Value, MinimumTargetPx + margins * 2,
                $"The compact rail is {height}px, which cannot hold a {MinimumTargetPx}px " +
                "button and its margins. The rail exists to be tapped; size it to the target " +
                "rather than the other way round.");
        }

        [Test]
        public void ASliderHandleIsFindableWithoutLookingForIt()
        {
            string uss = Stylesheet();
            int? dragger = PixelValue(Block(uss, ".unity-base-slider__dragger"), "height");

            Assert.IsNotNull(dragger, "The slider handle has no explicit size.");
            Assert.GreaterOrEqual(dragger.Value, 20,
                $"A {dragger}px slider handle is a pixel-hunt on a phone.");
        }

        // ---------- the reasoning behind the number ----------

        [Test]
        public void TheTargetHoldsAcrossEveryDeviceTheScaleSupports()
        {
            // The claim that makes a panel-pixel threshold meaningful: the panel
            // scale is derived from resolution, so the same panel-pixel size
            // stays a comparable physical size everywhere. If that stopped being
            // true, stating the minimum in panel pixels would be meaningless.
            var resolutions = new List<(int w, int h, string name)>
            {
                (1920, 1080, "phone, landscape"),
                (2400, 1080, "tall phone, landscape"),
                (2340, 1080, "modern handset"),
                (2732, 2048, "tablet"),
                (3840, 2160, "large tablet / desktop")
            };

            float smallest = float.MaxValue;
            float largest = 0f;

            foreach (var (w, h, _) in resolutions)
            {
                float scale = TerminalScale.ScaleFor(w, h);
                // Target height as a fraction of the screen's short side is the
                // closest DPI-free proxy for "how big does this feel".
                float fraction = MinimumTargetPx * scale / System.Math.Min(w, h);
                if (fraction < smallest) smallest = fraction;
                if (fraction > largest) largest = fraction;
            }

            Assert.Greater(smallest, 0.05f,
                $"On some supported resolution a {MinimumTargetPx}px control is only " +
                $"{smallest:P1} of the short screen edge — too small to be a comfortable target.");
            Assert.Less(largest, 0.30f,
                $"On some supported resolution a {MinimumTargetPx}px control is {largest:P1} of " +
                "the short screen edge, which would make the terminal all buttons.");
        }

        [Test]
        public void BodyTextIsStillTheThingTheLayoutIsBuiltAround()
        {
            // The 44 comes from body text, so if body text moves the constant is
            // no longer the right one and somebody has to decide again.
            Assert.AreEqual(13f, TerminalScale.BaseFontPx, 0.001f,
                $"Body text is no longer 13px, so the {MinimumTargetPx}px touch minimum — " +
                "derived as roughly 3.4x body text — needs re-deriving.");
        }
    }
}
