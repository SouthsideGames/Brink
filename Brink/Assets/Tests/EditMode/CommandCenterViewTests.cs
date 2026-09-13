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
        }
    }
}
