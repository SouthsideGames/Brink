using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(Brink.Tests.TestProgressLogger))]

namespace Brink.Tests
{
    /// <summary>
    /// Writes one line to the Unity log as each test starts.
    ///
    /// The suite deliberately silences `GameLog` mirroring, so a healthy run's
    /// log is empty between the runner's prebuild and cleanup lines — which
    /// means a *hung* run's log is indistinguishable from a healthy one's and
    /// names nothing. Two full-suite hangs each burned 85+ minutes precisely
    /// because the log froze at startup and the stuck test could not be
    /// identified; the fixture-level bisection that eventually localized things
    /// took six Unity launches. With this, the last `[TEST]` line in a frozen
    /// log is the culprit.
    ///
    /// Test *starts* only, and nothing on finish: the point is naming what is
    /// currently running, and half the volume keeps the log small (~1,050
    /// lines per full run — nowhere near the 577 MB mirroring hazard).
    /// </summary>
    public class TestProgressLogger : ITestRunCallback
    {
        public void RunStarted(ITest testsToRun)
            => Debug.Log($"[TESTRUN] starting: {testsToRun?.TestCaseCount ?? 0} tests");

        public void TestStarted(ITest test)
        {
            // Suites (fixtures, namespaces) also arrive here; log leaves only.
            if (test == null || test.HasChildren) return;
            Debug.Log($"[TEST] {test.FullName}");
        }

        public void TestFinished(ITestResult result) { }

        public void RunFinished(ITestResult testResults)
            => Debug.Log($"[TESTRUN] finished: {testResults?.ResultState}");
    }
}
