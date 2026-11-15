using System.Security.Cryptography;
using Net.Code.Kbo.Build;

namespace Net.Code.Kbo.Tests;

public class ReleaseSafetyTests
{
    private static readonly ValidationOptions Lenient = new() { MaxOrphanFraction = 0.5 };

    [Test]
    public async Task AcquireLease_RejectsOverlappingReleaseAndRemovesLockAfterOwnerDisposes()
    {
        using var share = new TempDirectory();
        var lockPath = Path.Combine(share.Path, ".release.lock");
        var lease = Releaser.AcquireLease(share.Path);

        try
        {
            var error = Capture(() => Releaser.Run(
                share.Path, Path.Combine(share.Path, "work"), Lenient, _ => { }));

            await Assert.That(error?.GetType()).IsEqualTo(typeof(IOException));
            await Assert.That(File.Exists(lockPath)).IsTrue();
            await Assert.That(File.Exists(Path.Combine(share.Path, Releaser.CurrentFile))).IsFalse();
        }
        finally
        {
            lease.Dispose();
        }

        await Assert.That(File.Exists(lockPath)).IsFalse();
        using var nextLease = Releaser.AcquireLease(share.Path);
        await Assert.That(File.Exists(lockPath)).IsTrue();
    }

    [Test]
    public async Task Run_AfterRollbackDoesNotOverwriteExistingImmutableRelease()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, 501);

        // Publish 0502, roll the pointer back to 0501, then present the same extract again.
        KboFixture.CreateZip(Path.Combine(share.Path, "incoming"), 502);
        var firstPublish = Releaser.Run(share.Path, Path.Combine(share.Path, "work"), Lenient, _ => { });
        if (firstPublish != 0) throw new InvalidOperationException("The synthetic 0502 release did not publish.");
        var immutablePath = Path.Combine(share.Path, "kbo-0502.db");
        var originalHash = HashFile(immutablePath);
        File.WriteAllText(Path.Combine(share.Path, Releaser.CurrentFile), "kbo-0501.db");
        var candidate = KboFixture.CreateZip(Path.Combine(share.Path, "incoming"), 502);

        var exitCode = Releaser.Run(share.Path, Path.Combine(share.Path, "work"), Lenient, _ => { });

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(Path.Combine(share.Path, Releaser.CurrentFile))).IsEqualTo("kbo-0501.db");
        await Assert.That(HashFile(immutablePath)).IsEqualTo(originalHash);
        await Assert.That(File.Exists(candidate)).IsTrue();
        await Assert.That(File.Exists(immutablePath + ".tmp")).IsFalse();
    }

    [Test]
    public async Task Run_MalformedCurrentPointerRejectsWithoutPublishing()
    {
        using var share = new TempDirectory();
        var incoming = Path.Combine(share.Path, "incoming");
        var candidate = KboFixture.CreateZip(incoming, 502);
        var currentPath = Path.Combine(share.Path, Releaser.CurrentFile);
        File.WriteAllText(currentPath, "not-a-release.db");

        var error = Capture(() => Releaser.Run(
            share.Path, Path.Combine(share.Path, "work"), Lenient, _ => { }));

        await Assert.That(error?.GetType()).IsEqualTo(typeof(InvalidDataException));
        await Assert.That(File.ReadAllText(currentPath)).IsEqualTo("not-a-release.db");
        await Assert.That(File.Exists(candidate)).IsTrue();
        await Assert.That(File.Exists(Path.Combine(share.Path, "kbo-0502.db"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(share.Path, ".release.lock"))).IsFalse();
    }

    [Test]
    public async Task Run_ArchiveFilenameAndEmbeddedExtractMismatchIsNotPublished()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, 501);
        var incoming = Path.Combine(share.Path, "incoming");
        var mismatchedArchive = KboFixture.CreateZip(incoming, 503);
        var expectedFileName = Path.Combine(incoming, FixtureName(502));
        File.Move(mismatchedArchive, expectedFileName);

        var exitCode = Releaser.Run(share.Path, Path.Combine(share.Path, "work"), Lenient, _ => { });

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(Path.Combine(share.Path, Releaser.CurrentFile))).IsEqualTo("kbo-0501.db");
        await Assert.That(File.Exists(Path.Combine(share.Path, "kbo-0502.db"))).IsFalse();
        await Assert.That(File.Exists(expectedFileName)).IsTrue();
        await Assert.That(File.Exists(Path.Combine(incoming, "processed", FixtureName(502)))).IsFalse();
    }

    private static void SeedCurrentRelease(TempDirectory share, int extractNumber)
    {
        KboFixture.CreateZip(Path.Combine(share.Path, "incoming"), extractNumber);
        var result = Releaser.Run(share.Path, Path.Combine(share.Path, "baseline-work"), Lenient, _ => { });
        if (result != 0) throw new InvalidOperationException("The synthetic baseline release did not publish.");
    }

    private static string FixtureName(int extractNumber) =>
        $"KboOpenData_{extractNumber:0000}_2026_10_04_Full.zip";

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static Exception? Capture(Action action)
    {
        try { action(); }
        catch (Exception error) { return error; }
        return null;
    }
}
