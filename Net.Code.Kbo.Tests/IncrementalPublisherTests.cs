using System.Security.Cryptography;
using Net.Code.Kbo.Build;

namespace Net.Code.Kbo.Tests;

public class IncrementalPublisherTests
{
    [Test]
    public async Task SourceBinding_WriteAndOpenReturnsMatchingSourceBinding()
    {
        using var temp = new TempDirectory();
        var binding = SeedRelease(temp, temp.Path, 100, makeCurrent: true);

        var opened = SourceBinding.Open(temp.Path, 100, _ => { }, CancellationToken.None);

        await Assert.That(opened).IsEqualTo(binding);
    }

    [Test]
    public async Task SourceBinding_OpenReturnsNullWhenSnapshotIsMissingOrChanged()
    {
        using var temp = new TempDirectory();
        var binding = SeedRelease(temp, temp.Path, 100, makeCurrent: true);
        var sourcePath = SourceBinding.PathFor(temp.Path, binding.Extract);

        File.Delete(sourcePath);
        var missing = SourceBinding.Open(temp.Path, binding.Extract, _ => { }, CancellationToken.None);
        await Assert.That(missing).IsNull();

        File.Copy(KboFixture.CreateZip(Path.Combine(temp.Path, "replacement"), binding.Extract), sourcePath);
        File.AppendAllText(sourcePath, "changed");
        var changed = SourceBinding.Open(temp.Path, binding.Extract, _ => { }, CancellationToken.None);
        await Assert.That(changed).IsNull();
    }

    [Test]
    public async Task Publish_PointerSwitchFailureLeavesBoundArtifactsAndRetryPublishesTarget()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var previousBinding = SeedRelease(temp, share, 100, makeCurrent: true);
        var (source, database, binding) = CreateTarget(temp, 101, previousExtract: 100);
        var pointer = Path.Combine(share, Releaser.CurrentFile);
        var pointerTempDirectory = pointer + ".tmp";
        Directory.CreateDirectory(pointerTempDirectory);

        var failed = false;
        try
        {
            IncrementalPublisher.Publish(share, database, source, binding, _ => { }, CancellationToken.None);
        }
        catch (IOException)
        {
            failed = true;
        }
        catch (UnauthorizedAccessException)
        {
            failed = true;
        }

        var targetDatabase = Path.Combine(share, Releaser.FileName(binding.Extract));
        var targetSource = SourceBinding.PathFor(share, binding.Extract);
        await Assert.That(failed).IsTrue();
        await Assert.That(File.ReadAllText(pointer)).IsEqualTo(Releaser.FileName(previousBinding.Extract));
        await Assert.That(File.Exists(targetDatabase)).IsTrue();
        await Assert.That(File.Exists(targetSource)).IsTrue();
        await Assert.That(SourceBinding.Open(share, binding.Extract, _ => { }, CancellationToken.None)).IsEqualTo(binding);

        Directory.Delete(pointerTempDirectory);
        IncrementalPublisher.Publish(share, database, source, binding, _ => { }, CancellationToken.None);

        await Assert.That(File.ReadAllText(pointer)).IsEqualTo(Releaser.FileName(binding.Extract));
        await Assert.That(SourceBinding.Open(share, binding.Extract, _ => { }, CancellationToken.None)).IsEqualTo(binding);

        // The prior immutable database and its matching source remain available for rollback.
        await Assert.That(File.Exists(Path.Combine(share, Releaser.FileName(previousBinding.Extract)))).IsTrue();
        await Assert.That(File.Exists(SourceBinding.PathFor(share, previousBinding.Extract))).IsTrue();
        File.WriteAllText(pointer, Releaser.FileName(previousBinding.Extract));
        await Assert.That(SourceBinding.Open(share, previousBinding.Extract, _ => { }, CancellationToken.None))
            .IsEqualTo(previousBinding);
    }

    [Test]
    public async Task Publish_RejectsIncompatibleImmutableTargetWithoutOverwritingIt()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var previous = SeedRelease(temp, share, 100, makeCurrent: true);
        var (source, _, binding) = CreateTarget(temp, 101, previousExtract: 100);
        var incompatibleDatabase = BuildUnboundDatabase(temp, 101);
        var targetDatabase = Path.Combine(share, Releaser.FileName(binding.Extract));
        File.Copy(incompatibleDatabase, targetDatabase);
        File.Copy(source, SourceBinding.PathFor(share, binding.Extract));
        var originalDatabaseHash = HashFile(targetDatabase);
        var pointer = Path.Combine(share, Releaser.CurrentFile);

        var rejected = false;
        try
        {
            IncrementalPublisher.Publish(share, incompatibleDatabase, source, binding, _ => { }, CancellationToken.None);
        }
        catch (IOException)
        {
            rejected = true;
        }

        await Assert.That(rejected).IsTrue();
        await Assert.That(HashFile(targetDatabase)).IsEqualTo(originalDatabaseHash);
        await Assert.That(File.ReadAllText(pointer)).IsEqualTo(Releaser.FileName(previous.Extract));
    }

    [Test]
    public async Task Publish_RejectsBindingForDifferentCurrentExtractBeforeCreatingArtifacts()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var previous = SeedRelease(temp, share, 100, makeCurrent: true);
        var (source, database, _) = CreateTarget(temp, 101, previousExtract: 99);

        var rejected = false;
        try
        {
            var incompatibleBinding = new SourceBinding(
                101, SourceBinding.HashFile(source, CancellationToken.None), "2026-10", PreviousExtract: 99);
            IncrementalPublisher.Publish(share, database, source, incompatibleBinding, _ => { }, CancellationToken.None);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        await Assert.That(rejected).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(share, Releaser.CurrentFile)))
            .IsEqualTo(Releaser.FileName(previous.Extract));
        await Assert.That(File.Exists(Path.Combine(share, Releaser.FileName(101)))).IsFalse();
        await Assert.That(File.Exists(SourceBinding.PathFor(share, 101))).IsFalse();
    }

    [Test]
    public async Task Publish_CancellationBeforeCopyPreservesPointerAndCreatesNoTarget()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var previous = SeedRelease(temp, share, 100, makeCurrent: true);
        var (source, database, binding) = CreateTarget(temp, 101, previousExtract: 100);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = false;
        try
        {
            IncrementalPublisher.Publish(share, database, source, binding, _ => { }, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        await Assert.That(cancelled).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(share, Releaser.CurrentFile)))
            .IsEqualTo(Releaser.FileName(previous.Extract));
        await Assert.That(File.Exists(Path.Combine(share, Releaser.FileName(binding.Extract)))).IsFalse();
        await Assert.That(File.Exists(SourceBinding.PathFor(share, binding.Extract))).IsFalse();
    }

    [Test]
    public async Task Publish_SourceCopyFailurePreservesCurrentRelease()
    {
        using var temp = new TempDirectory();
        var share = Path.Combine(temp.Path, "share");
        Directory.CreateDirectory(share);
        var previous = SeedRelease(temp, share, 100, makeCurrent: true);
        var (source, database, binding) = CreateTarget(temp, 101, previousExtract: 100);
        var pointer = Path.Combine(share, Releaser.CurrentFile);

        var failed = false;
        try
        {
            IncrementalPublisher.Publish(
                share, database, Path.Combine(temp.Path, "missing-source.zip"), binding,
                _ => { }, CancellationToken.None);
        }
        catch (IOException)
        {
            failed = true;
        }

        await Assert.That(failed).IsTrue();
        await Assert.That(File.ReadAllText(pointer)).IsEqualTo(Releaser.FileName(previous.Extract));
        await Assert.That(File.Exists(Path.Combine(share, Releaser.FileName(binding.Extract)))).IsFalse();
        await Assert.That(File.Exists(SourceBinding.PathFor(share, binding.Extract))).IsFalse();
        await Assert.That(File.Exists(source)).IsTrue();
    }

    private static SourceBinding SeedRelease(TempDirectory temp, string share, int extract, bool makeCurrent)
    {
        var directory = Path.Combine(temp.Path, $"seed-{extract}");
        Directory.CreateDirectory(directory);
        var source = KboFixture.CreateZip(directory, extract);
        var database = Path.Combine(directory, Releaser.FileName(extract));
        ReleaseBuilder.Build(source, database, _ => { });
        var binding = new SourceBinding(extract, SourceBinding.HashFile(source, CancellationToken.None), "2026-10");
        SourceBinding.Write(database, binding);

        Directory.CreateDirectory(share);
        File.Copy(database, Path.Combine(share, Releaser.FileName(extract)), overwrite: true);
        var sourceTarget = SourceBinding.PathFor(share, extract);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceTarget)!);
        File.Copy(source, sourceTarget, overwrite: true);
        if (makeCurrent)
            File.WriteAllText(Path.Combine(share, Releaser.CurrentFile), Releaser.FileName(extract));
        return binding;
    }

    private static (string Source, string Database, SourceBinding Binding) CreateTarget(
        TempDirectory temp, int extract, int previousExtract)
    {
        var directory = Path.Combine(temp.Path, $"target-{extract}");
        Directory.CreateDirectory(directory);
        var source = KboFixture.CreateZip(directory, extract);
        var database = Path.Combine(directory, Releaser.FileName(extract));
        ReleaseBuilder.Build(source, database, _ => { });
        var binding = new SourceBinding(
            extract, SourceBinding.HashFile(source, CancellationToken.None), "2026-10", previousExtract);
        SourceBinding.Write(database, binding);
        return (source, database, binding);
    }

    private static string BuildUnboundDatabase(TempDirectory temp, int extract)
    {
        var directory = Path.Combine(temp.Path, $"unbound-{extract}");
        Directory.CreateDirectory(directory);
        var source = KboFixture.CreateZip(directory, extract);
        var database = Path.Combine(directory, Releaser.FileName(extract));
        ReleaseBuilder.Build(source, database, _ => { });
        return database;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
