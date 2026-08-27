using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The country dossier (GDD §28.1's Deep Terminal tier).
    ///
    /// NARROW PIPELINE: none. Every claim here is about the shared access gate
    /// and about the panel existing in the real rail — neither depends on a month
    /// resolving, and running one would only add noise.
    ///
    /// The dossier is a view, so most of what it does is not unit-testable. What
    /// *is* testable is the thing that would quietly break it: the fog gate it
    /// shares with the INTELLIGENCE panel. Two screens rendering the same dossier
    /// with two copies of a threshold is two thresholds, and the second one drifts.
    /// </summary>
    public class DossierTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 7311);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        CountryState Foreign()
        {
            foreach (var country in state.countries)
                if (!country.isPlayer) return country;
            return null;
        }

        [Test]
        public void TheDossierIsInTheRail()
        {
            bool found = false;
            foreach (var panel in Brink.UI.TerminalShellController.BuildPanels(true))
                if (panel.Id == "DOSSIER") found = true;

            Assert.IsTrue(found,
                "The dossier panel is not in the rail, so nothing can open it.");
        }

        // ---------- the fog gate ----------

        [Test]
        public void WithoutANetworkWeDoNotKnowWhoRunsTheirMinistries()
        {
            var target = Foreign();
            state.networks.RemoveAll(n => n.ownerId == state.playerCountryId
                                          && n.targetId == target.id);

            Assert.AreEqual(Brink.UI.IntelReadout.PersonnelAccess.None,
                Brink.UI.IntelReadout.PersonnelAccessOf(state, target.id),
                "A country with no collection against it still handed over its cabinet.");
        }

        [Test]
        public void ShallowAccessNamesThemAndDeepAccessJudgesThem()
        {
            var target = Foreign();
            state.networks.RemoveAll(n => n.ownerId == state.playerCountryId
                                          && n.targetId == target.id);

            var network = new IntelNetwork
            {
                ownerId = state.playerCountryId,
                targetId = target.id,
                penetration = Brink.UI.IntelReadout.NameThreshold + 1f
            };
            state.networks.Add(network);

            Assert.AreEqual(Brink.UI.IntelReadout.PersonnelAccess.Identities,
                Brink.UI.IntelReadout.PersonnelAccessOf(state, target.id),
                "Shallow access gave a competence judgement it has not earned.");

            network.penetration = Brink.UI.IntelReadout.AssessThreshold + 1f;

            Assert.AreEqual(Brink.UI.IntelReadout.PersonnelAccess.Assessed,
                Brink.UI.IntelReadout.PersonnelAccessOf(state, target.id),
                "Deep access still refused to judge their ministers.");
        }

        [Test]
        public void ABurnedNetworkTellsUsNothing()
        {
            var target = Foreign();
            state.networks.RemoveAll(n => n.ownerId == state.playerCountryId
                                          && n.targetId == target.id);
            state.networks.Add(new IntelNetwork
            {
                ownerId = state.playerCountryId,
                targetId = target.id,
                penetration = 90f,
                compromised = true
            });

            Assert.AreEqual(Brink.UI.IntelReadout.PersonnelAccess.None,
                Brink.UI.IntelReadout.PersonnelAccessOf(state, target.id),
                "A compromised network kept reporting. Being burned has to cost the access "
                + "it bought.");
        }

        [Test]
        public void WeAlwaysKnowOurOwnGovernment()
        {
            Assert.AreEqual(Brink.UI.IntelReadout.PersonnelAccess.Assessed,
                Brink.UI.IntelReadout.PersonnelAccessOf(state, state.playerCountryId),
                "The operator needed a spy ring to find out who their own economy minister "
                + "is.");
        }

        // ---------- the secret half stays secret ----------

        [Test]
        public void AnUnattributedSponsorshipIsNotInAnybodysDossier()
        {
            var sponsor = Foreign();
            var location = state.locations.Find(l => l.ownerId != sponsor.id);
            Assert.IsNotNull(location);

            var rising = InsurgencySystem.Open(state, location, InsurgencyCause.Deprivation, 40f);
            rising.sponsorId = sponsor.id;
            rising.sponsorExposed = false;

            Assert.IsFalse(InsurgencySystem.KnownSponsor(state, state.playerCountryId, rising),
                "An unattributed programme was readable. The dossier collects what is known; "
                + "it must not be a way to read somebody else's covert file.");

            rising.sponsorExposed = true;

            Assert.IsTrue(InsurgencySystem.KnownSponsor(state, state.playerCountryId, rising),
                "An attributed programme stayed hidden, so being caught cost nothing in "
                + "reputation.");
        }
    }
}
