using Net.Code.Kbo.Build;

namespace Net.Code.Kbo.Tests;

/// <summary>The monthly release flow on a (local) share directory.</summary>
public class ReleaserTests
{
    // the fixture has one orphan row of six entities; allow it so validation passes
    static readonly ValidationOptions Lenient = new() { MaxOrphanFraction = 0.5 };

    static int Release(TempDirectory share, ValidationOptions? options = null) =>
        Releaser.Run(share.Path, Path.Combine(share.Path, "work"), options ?? Lenient, _ => { });

    static string Incoming(TempDirectory share) => Path.Combine(share.Path, "incoming");
    static string Current(TempDirectory share) => File.ReadAllText(Path.Combine(share.Path, "current.txt"));
    static string[] Releases(TempDirectory share) => Directory.GetFiles(share.Path, "kbo-*.db").Select(Path.GetFileName).Order().ToArray()!;

    [Test]
    public async Task NothingToDo_WithoutIncomingZip()
    {
        using var share = new TempDirectory();

        await Assert.That(Release(share)).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(share.Path, "current.txt"))).IsFalse();
    }

    [Test]
    public async Task PublishesRelease_SwitchesCurrent_AndMovesZip()
    {
        using var share = new TempDirectory();
        var zip = KboFixture.CreateZip(Incoming(share), 502);

        await Assert.That(Release(share)).IsEqualTo(0);

        await Assert.That(Current(share)).IsEqualTo("kbo-0502.db");
        await Assert.That(Releases(share)).IsEquivalentTo(new[] { "kbo-0502.db" });
        await Assert.That(File.Exists(zip)).IsFalse();
        await Assert.That(File.Exists(Path.Combine(Incoming(share), "processed", Path.GetFileName(zip)))).IsTrue();
        await Assert.That(CompanyStore.Open(Path.Combine(share.Path, "kbo-0502.db")).Release.ExtractNumber).IsEqualTo(502);
    }

    [Test]
    public async Task SecondRun_IsNoOp()
    {
        using var share = new TempDirectory();
        KboFixture.CreateZip(Incoming(share), 502);
        Release(share);
        var written = File.GetLastWriteTimeUtc(Path.Combine(share.Path, "kbo-0502.db"));

        await Assert.That(Release(share)).IsEqualTo(0);
        await Assert.That(File.GetLastWriteTimeUtc(Path.Combine(share.Path, "kbo-0502.db"))).IsEqualTo(written);
    }

    [Test]
    public async Task KeepsCurrentAndPrevious_DeletesOlder()
    {
        using var share = new TempDirectory();
        foreach (var n in new[] { 502, 503, 504 })
        {
            KboFixture.CreateZip(Incoming(share), n);
            Release(share);
        }

        await Assert.That(Current(share)).IsEqualTo("kbo-0504.db");
        await Assert.That(Releases(share)).IsEquivalentTo(new[] { "kbo-0503.db", "kbo-0504.db" });
    }

    [Test]
    public async Task PicksNewestZip_AndIgnoresOlderThanCurrent()
    {
        using var share = new TempDirectory();
        KboFixture.CreateZip(Incoming(share), 503);
        KboFixture.CreateZip(Incoming(share), 505);
        Release(share);
        await Assert.That(Current(share)).IsEqualTo("kbo-0505.db");

        await Assert.That(Release(share)).IsEqualTo(0); // 503 is older than current: ignored
        await Assert.That(Current(share)).IsEqualTo("kbo-0505.db");
        await Assert.That(File.Exists(Path.Combine(Incoming(share), "KboOpenData_0503_2026_10_04_Full.zip"))).IsTrue();
    }

    [Test]
    public async Task FailedValidation_PublishesNothing()
    {
        using var share = new TempDirectory();
        var zip = KboFixture.CreateZip(Incoming(share), 502);

        await Assert.That(Release(share, Lenient with { MaxBytes = 1 })).IsEqualTo(1);

        await Assert.That(File.Exists(Path.Combine(share.Path, "current.txt"))).IsFalse();
        await Assert.That(Releases(share).Length).IsEqualTo(0);
        await Assert.That(File.Exists(zip)).IsTrue();
    }
}
