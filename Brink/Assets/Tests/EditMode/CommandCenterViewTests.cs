using System.Collections.Generic;
using Brink.UI.Views;
using NUnit.Framework;

namespace Brink.Tests.EditMode
{
    public class CommandCenterViewTests
    {
        [Test]
        public void CommandCenter_IsARealShippingPanel()
        {
            List<TerminalView> panels = Brink.UI.TerminalShellController.BuildPanels(false);
            Assert.That(panels.Exists(p => p.Id == "COMMAND CENTER" && p.ShortCode == "CMD"), Is.True);
        }

        [Test]
        public void CommandCenter_IsFirstPanel()
        {
            List<TerminalView> panels = Brink.UI.TerminalShellController.BuildPanels(false);
            Assert.That(panels[0].Id, Is.EqualTo("COMMAND CENTER"));
            Assert.That(panels[1].Id, Is.EqualTo("BRIEFING"));
        }

        [TestCase(41, Brink.UI.SizeClass.Compact)]
        [TestCase(49, Brink.UI.SizeClass.Compact)]
        [TestCase(60, Brink.UI.SizeClass.Compact)]
        [TestCase(64, Brink.UI.SizeClass.Medium)]
        [TestCase(79, Brink.UI.SizeClass.Medium)]
        [TestCase(82, Brink.UI.SizeClass.Large)]
        [TestCase(100, Brink.UI.SizeClass.Large)]
        [TestCase(104, Brink.UI.SizeClass.Large)]
        public void VerticalSliceWidths_MapToResponsiveClasses(int columns, Brink.UI.SizeClass expected)
        {
            Assert.That(Brink.UI.Breakpoints.FromColumns(columns), Is.EqualTo(expected));
        }
    }
}
