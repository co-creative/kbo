using Net.Code.Kbo.Build;

namespace Net.Code.Kbo.Tests;

public class ReleasePruningTests
{
    [Test]
    public async Task Run_PrunesOnlyStrictlyNamedTemporaryArtifactsWhileHoldingLease()
    {
        using var share = new TempDirectory();
        using var work = new TempDirectory();
        using var seed = new TempDirectory();
        var currentSource = KboFixture.CreateZip(seed.Path, 500);
        var currentDatabase = Path.Combine(seed.Path, Releaser.FileName(500));
        ReleaseBuilder.Build(currentSource, currentDatabase, _ => { });
        File.Copy(currentDatabase, Path.Combine(share.Path, Releaser.FileName(500)));
        File.WriteAllText(Path.Combine(share.Path, Releaser.CurrentFile), Releaser.FileName(500));

        var state = Path.Combine(share.Path, ".state");
        Directory.CreateDirectory(state);
        File.Copy(currentSource, SourceBinding.PathFor(share.Path, 500));
        File.WriteAllText(Path.Combine(share.Path, Releaser.FileName(400)), "stale release");
        File.WriteAllText(Path.Combine(state, "source-0400.zip"), "stale source");

        var validDatabaseTemp = Path.Combine(share.Path, "kbo-0999.db.tmp");
        var malformedDatabaseTemp = Path.Combine(share.Path, "kbo-not-a-number.db.tmp");
        var databaseTempWithSuffix = Path.Combine(share.Path, "kbo-0999.db.tmp.bak");
        var validSourceTemp = Path.Combine(state, "source-0999.zip.tmp");
        var malformedSourceTemp = Path.Combine(state, "source-nope.zip.tmp");
        var sourceTempWithSuffix = Path.Combine(state, "source-0999.zip.tmp.bak");
        foreach (var path in new[]
        {
            validDatabaseTemp, malformedDatabaseTemp, databaseTempWithSuffix,
            validSourceTemp, malformedSourceTemp, sourceTempWithSuffix
        })
            File.WriteAllText(path, "temporary");

        var incoming = Path.Combine(share.Path, "incoming");
        var targetZip = KboFixture.CreateZip(incoming, 501);
        var lockPath = Path.Combine(share.Path, ".release.lock");
        var observedLockDuringPruning = false;
        var exitCode = Releaser.Run(share.Path, work.Path,
            new ValidationOptions { MaxOrphanFraction = 0.5 },
            message =>
            {
                if (message == "deleted old release kbo-0400.db")
                    observedLockDuringPruning = File.Exists(lockPath);
            });

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(observedLockDuringPruning).IsTrue();
        await Assert.That(File.Exists(lockPath)).IsFalse();
        await Assert.That(File.Exists(validDatabaseTemp)).IsFalse();
        await Assert.That(File.Exists(validSourceTemp)).IsFalse();
        await Assert.That(File.Exists(malformedDatabaseTemp)).IsTrue();
        await Assert.That(File.Exists(databaseTempWithSuffix)).IsTrue();
        await Assert.That(File.Exists(malformedSourceTemp)).IsTrue();
        await Assert.That(File.Exists(sourceTempWithSuffix)).IsTrue();
        await Assert.That(File.Exists(Path.Combine(share.Path, Releaser.FileName(500)))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(share.Path, Releaser.FileName(501)))).IsTrue();
        await Assert.That(File.Exists(SourceBinding.PathFor(share.Path, 500))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(state, "source-0400.zip"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(incoming, "processed", Path.GetFileName(targetZip)))).IsTrue();
    }
}
