using System;
using System.IO;
using Brink.Core;
using NUnit.Framework;

// Deliberately outside any namespace: NUnit runs this before every fixture in
// this assembly, including filtered runs and fixtures in future namespaces.
// This is a lifecycle boundary, not a fixture to add to a partition filter.
[SetUpFixture]
// Unity's custom NUnit 3.5 lacks NonParallelizableAttribute.
[Parallelizable(ParallelScope.None)]
public sealed class TestSaveIsolation
{
    string previousDirectory;
    string directory;

    [OneTimeSetUp]
    public void Begin()
    {
        previousDirectory = SaveSystem.SaveDirectoryOverride;
        directory = Path.Combine(Path.GetTempPath(), "brink-test-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        SaveSystem.SaveDirectoryOverride = directory;
    }

    [OneTimeTearDown]
    public void End()
    {
        // Inner fixtures must likewise restore their previous override, never
        // null it: null would reopen the real save directory during this run.
        SaveSystem.SaveDirectoryOverride = previousDirectory;
        if (directory != null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
