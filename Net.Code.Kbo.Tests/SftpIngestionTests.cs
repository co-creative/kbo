using System.Text;
using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Net.Code.Kbo.Build;

namespace Net.Code.Kbo.Tests;

public class SftpIngestionTests
{
    private static readonly ValidationOptions Lenient = new() { MaxOrphanFraction = 0.5 };
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private const string SecretName = "sftp-credential-test";

    [Test]
    public async Task SelectNewest_UsesHighestValidExtractAboveCurrent()
    {
        var selected = SftpIngestion.SelectNewest(
        [
            new RemoteExtract("KboOpenData_0503_2026_10_04_Full.zip", 30),
            new RemoteExtract("KboOpenData_0505_2026_10_04_Full.zip", 50),
            new RemoteExtract("KboOpenData_0504_2026_99_99_Full.zip", 40),
            new RemoteExtract("KboOpenData_0506_2026_10_04_Update.zip", 60),
            new RemoteExtract("notes.txt", 70)
        ], current: 503);

        await Assert.That(selected).IsEqualTo(new RemoteExtract("KboOpenData_0505_2026_10_04_Full.zip", 50));
    }

    [Test]
    public async Task SelectNewest_ReturnsNullWhenNoNewerFullExtractExists()
    {
        var selected = SftpIngestion.SelectNewest(
        [
            new RemoteExtract("KboOpenData_0503_2026_10_04_Full.zip", 30),
            new RemoteExtract("KboOpenData_0502_2026_10_04_Full.zip", 20),
            new RemoteExtract("KboOpenData_0504_2026_10_04_Update.zip", 40)
        ], current: 503);

        await Assert.That(selected).IsNull();
    }

    [Test]
    public async Task SelectNewest_DuplicateNumberIsRejected()
    {
        var error = Capture(() => SftpIngestion.SelectNewest(
        [
            new RemoteExtract("KboOpenData_0505_2026_10_04_Full.zip", 50),
            new RemoteExtract("KboOpenData_0505_2026_10_05_Full.zip", 51)
        ], current: 503));

        await Assert.That(error?.GetType()).IsEqualTo(typeof(InvalidDataException));
    }

    [Test]
    public async Task IsTrustedHostKey_RejectsAlgorithmOrFingerprintMismatch()
    {
        const string trustedFingerprint = "LcC2+VnBH01X/CGMd44inc/lxMJy/jH1RtwhW9PIGD8";

        await Assert.That(FpsSftpSource.IsTrustedHostKey("ssh-ed25519", trustedFingerprint)).IsTrue();
        await Assert.That(FpsSftpSource.IsTrustedHostKey("ssh-rsa", trustedFingerprint)).IsFalse();
        await Assert.That(FpsSftpSource.IsTrustedHostKey("ssh-ed25519", "unit-test-mismatched-fingerprint")).IsFalse();
    }

    [Test]
    public async Task RunAsync_FixtureDownloadPublishesReleaseAndProcessesArchive()
    {
        using var share = new TempDirectory();
        var zip = CreateFixtureBytes(502);
        var source = new FakeSftpSource([new RemoteExtract(FixtureName(502), zip.Length)], zip);
        SftpCredential? suppliedCredential = null;

        var exitCode = await RunAsync(share, source, credential =>
        {
            suppliedCredential = credential;
            return source;
        });

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(Path.Combine(share.Path, Releaser.CurrentFile))).IsEqualTo("kbo-0502.db");
        await Assert.That(CompanyStore.Open(Path.Combine(share.Path, "kbo-0502.db")).Release.ExtractNumber).IsEqualTo(502);
        await Assert.That(source.DownloadedNames).IsEquivalentTo(new[] { FixtureName(502) });
        await Assert.That(source.Disposed).IsTrue();
        await Assert.That(suppliedCredential?.Username).IsEqualTo("unit-test-user");
        await Assert.That(File.Exists(Path.Combine(share.Path, "incoming", "processed", FixtureName(502)))).IsTrue();
        await Assert.That(Directory.GetFiles(Path.Combine(share.Path, "incoming"), ".sftp-*.part").Length).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_RetrievesVersionlessCredentialOncePerRunAndUsesRotationNextRun()
    {
        using var share = new TempDirectory();
        var secrets = new Queue<string>(
        [
            CredentialPayload("unit-test-user-a", "unit-test-password-a"),
            CredentialPayload("unit-test-user-b", "unit-test-password-b")
        ]);
        var client = new TestSecretClient((_, _, _) => Task.FromResult(TestSecret(secrets.Dequeue())));
        var time = new FixedTimeProvider(Now);
        var usedUsers = new List<string>();
        var sources = new List<FakeSftpSource>();

        for (var run = 0; run < 2; run++)
        {
            var source = new FakeSftpSource([], []);
            sources.Add(source);
            var requestsBeforeRun = client.Requests.Count;
            var exitCode = await SftpIngestion.RunAsync(
                share.Path,
                Path.Combine(share.Path, "work"),
                Lenient,
                token => SftpCredentialProvider.GetAsync(client, SecretName, time, token),
                credential =>
                {
                    usedUsers.Add(credential.Username);
                    return source;
                },
                _ => { },
                CancellationToken.None);
            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(client.Requests.Count - requestsBeforeRun).IsEqualTo(1);
        }

        await Assert.That(client.Requests.Count).IsEqualTo(2);
        await Assert.That(client.Requests.All(request => request.Name == SecretName)).IsTrue();
        await Assert.That(client.Requests.All(request => request.Version is null)).IsTrue();
        await Assert.That(usedUsers).IsEquivalentTo(new[] { "unit-test-user-a", "unit-test-user-b" });
        await Assert.That(sources.All(source => source.Disposed)).IsTrue();
    }

    [Test]
    public async Task RunAsync_ConnectFailurePreservesCurrentRelease()
    {
        using var share = new TempDirectory();
        var baseline = SeedCurrentRelease(share, 501);
        var source = new FakeSftpSource([], []) { ConnectFailure = new IOException("synthetic connect failure") };

        var error = await CaptureAsync(() => RunAsync(share, source));

        await AssertPreserved(share, baseline);
        await Assert.That(error?.GetType()).IsEqualTo(typeof(IOException));
        await Assert.That(source.Disposed).IsTrue();
    }

    [Test]
    public async Task RunAsync_MissingCredentialPreservesCurrentReleaseWithoutOpeningSftp()
    {
        using var share = new TempDirectory();
        var baseline = SeedCurrentRelease(share, 501);
        var source = new FakeSftpSource([], []);
        var sourceCreated = false;

        var error = await CaptureAsync(() => SftpIngestion.RunAsync(
            share.Path,
            Path.Combine(share.Path, "work"),
            Lenient,
            _ => Task.FromException<SftpCredential>(new RequestFailedException(404, "Not Found")),
            _ =>
            {
                sourceCreated = true;
                return source;
            },
            _ => { },
            CancellationToken.None));

        await AssertPreserved(share, baseline);
        await Assert.That(error?.GetType()).IsEqualTo(typeof(RequestFailedException));
        await Assert.That(((RequestFailedException)error!).Status).IsEqualTo(404);
        await Assert.That(sourceCreated).IsFalse();
    }

    [Test]
    public async Task RunAsync_CancellationDuringConnectPreservesCurrentRelease()
    {
        using var share = new TempDirectory();
        var baseline = SeedCurrentRelease(share, 501);
        var source = new FakeSftpSource([], []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await CaptureAsync(() => SftpIngestion.RunAsync(
            share.Path,
            Path.Combine(share.Path, "work"),
            Lenient,
            _ => Task.FromResult(new SftpCredential("unit-test-user", "unit-test-password-only")),
            _ => source,
            _ => { },
            cancellation.Token));

        await AssertPreserved(share, baseline);
        await Assert.That(error?.GetType()).IsEqualTo(typeof(OperationCanceledException));
        await Assert.That(source.Disposed).IsTrue();
    }

    [Test]
    public async Task RunAsync_CancellationAfterDownloadDoesNotHandoffPartialRun()
    {
        using var share = new TempDirectory();
        var baseline = SeedCurrentRelease(share, 501);
        var zip = CreateFixtureBytes(502);
        using var cancellation = new CancellationTokenSource();
        var source = new FakeSftpSource([new RemoteExtract(FixtureName(502), zip.Length)], zip)
        {
            CancelAfterDownload = cancellation
        };

        var error = await CaptureAsync(() => SftpIngestion.RunAsync(
            share.Path,
            Path.Combine(share.Path, "work"),
            Lenient,
            _ => Task.FromResult(new SftpCredential("unit-test-user", "unit-test-password-only")),
            _ => source,
            _ => { },
            cancellation.Token));

        await AssertPreserved(share, baseline);
        await Assert.That(error?.GetType()).IsEqualTo(typeof(OperationCanceledException));
        await Assert.That(File.Exists(Path.Combine(share.Path, "incoming", FixtureName(502)))).IsFalse();
        await Assert.That(Directory.GetFiles(Path.Combine(share.Path, "incoming"), ".sftp-*.part").Length).IsEqualTo(0);
        await Assert.That(source.Disposed).IsTrue();
    }

    [Test]
    public async Task RunAsync_ListFailurePreservesCurrentRelease()
    {
        using var share = new TempDirectory();
        var baseline = SeedCurrentRelease(share, 501);
        var source = new FakeSftpSource([], []) { ListFailure = new IOException("synthetic list failure") };

        var error = await CaptureAsync(() => RunAsync(share, source));

        await AssertPreserved(share, baseline);
        await Assert.That(error?.GetType()).IsEqualTo(typeof(IOException));
        await Assert.That(source.Disposed).IsTrue();
    }

    [Test]
    public async Task RunAsync_DownloadFailurePreservesCurrentRelease()
    {
        using var share = new TempDirectory();
        var baseline = SeedCurrentRelease(share, 501);
        var zip = CreateFixtureBytes(502);
        var source = new FakeSftpSource([new RemoteExtract(FixtureName(502), zip.Length)], zip)
        {
            DownloadFailure = new IOException("synthetic download failure")
        };

        var error = await CaptureAsync(() => RunAsync(share, source));

        await AssertPreserved(share, baseline);
        await Assert.That(error?.GetType()).IsEqualTo(typeof(IOException));
        await Assert.That(File.Exists(Path.Combine(share.Path, "incoming", FixtureName(502)))).IsFalse();
        await Assert.That(Directory.GetFiles(Path.Combine(share.Path, "incoming"), ".sftp-*.part").Length).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_IncompleteDownloadPreservesCurrentReleaseAndRemovesPartialFile()
    {
        using var share = new TempDirectory();
        var baseline = SeedCurrentRelease(share, 501);
        var zip = CreateFixtureBytes(502);
        var source = new FakeSftpSource([new RemoteExtract(FixtureName(502), zip.Length)], zip)
        {
            BytesToWrite = zip.Length - 1
        };

        var error = await CaptureAsync(() => RunAsync(share, source));

        await AssertPreserved(share, baseline);
        await Assert.That(error?.GetType()).IsEqualTo(typeof(IOException));
        await Assert.That(File.Exists(Path.Combine(share.Path, "incoming", FixtureName(502)))).IsFalse();
        await Assert.That(Directory.GetFiles(Path.Combine(share.Path, "incoming"), ".sftp-*.part").Length).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_InvalidArchivePreservesCurrentRelease()
    {
        using var share = new TempDirectory();
        var baseline = SeedCurrentRelease(share, 501);
        var invalidArchive = Encoding.UTF8.GetBytes("synthetic non-zip content");
        var source = new FakeSftpSource([new RemoteExtract(FixtureName(502), invalidArchive.Length)], invalidArchive);

        var error = await CaptureAsync(() => RunAsync(share, source));

        await AssertPreserved(share, baseline);
        await Assert.That(error?.GetType()).IsEqualTo(typeof(InvalidDataException));
        await Assert.That(File.Exists(Path.Combine(share.Path, "incoming", FixtureName(502)))).IsFalse();
        await Assert.That(Directory.GetFiles(Path.Combine(share.Path, "incoming"), ".sftp-*.part").Length).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_ValidationFailurePreservesCurrentRelease()
    {
        using var share = new TempDirectory();
        var baseline = SeedCurrentRelease(share, 501);
        var zip = CreateFixtureBytes(502);
        var source = new FakeSftpSource([new RemoteExtract(FixtureName(502), zip.Length)], zip);

        var exitCode = await RunAsync(share, source, validation: Lenient with { MaxBytes = 1 });

        await AssertPreserved(share, baseline);
        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(share.Path, "incoming", FixtureName(502)))).IsTrue();
        await Assert.That(source.Disposed).IsTrue();
    }

    [Test]
    public async Task RunAsync_NoNewerExtractDoesNotDownloadAndPreservesCurrentRelease()
    {
        using var share = new TempDirectory();
        var baseline = SeedCurrentRelease(share, 501);
        var source = new FakeSftpSource([new RemoteExtract(FixtureName(501), 10)], []);

        var exitCode = await RunAsync(share, source);

        await AssertPreserved(share, baseline);
        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.DownloadedNames.Count).IsEqualTo(0);
        await Assert.That(source.Disposed).IsTrue();
    }

    [Test]
    public async Task RunAsync_ExistingDifferentIncomingArchivePreservesCurrentRelease()
    {
        using var share = new TempDirectory();
        var baseline = SeedCurrentRelease(share, 501);
        var incoming = Path.Combine(share.Path, "incoming");
        Directory.CreateDirectory(incoming);
        var candidatePath = Path.Combine(incoming, FixtureName(502));
        File.WriteAllBytes(candidatePath, Encoding.UTF8.GetBytes("existing content"));
        var zip = CreateFixtureBytes(502);
        var source = new FakeSftpSource([new RemoteExtract(FixtureName(502), zip.Length)], zip);

        var error = await CaptureAsync(() => RunAsync(share, source));

        await AssertPreserved(share, baseline);
        await Assert.That(error?.GetType()).IsEqualTo(typeof(IOException));
        await Assert.That(File.ReadAllText(candidatePath)).IsEqualTo("existing content");
        await Assert.That(Directory.GetFiles(incoming, ".sftp-*.part").Length).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_ManualIncomingZipStillPublishesWithoutSftp()
    {
        using var share = new TempDirectory();
        KboFixture.CreateZip(Path.Combine(share.Path, "incoming"), 502);

        var exitCode = Releaser.Run(share.Path, Path.Combine(share.Path, "work"), Lenient, _ => { });

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(Path.Combine(share.Path, Releaser.CurrentFile))).IsEqualTo("kbo-0502.db");
        await Assert.That(CompanyStore.Open(Path.Combine(share.Path, "kbo-0502.db")).Release.ExtractNumber).IsEqualTo(502);
        await Assert.That(File.Exists(Path.Combine(share.Path, "incoming", "processed", FixtureName(502)))).IsTrue();
    }

    private static async Task<int> RunAsync(
        TempDirectory share,
        FakeSftpSource source,
        Func<SftpCredential, ISftpSource>? sourceFactory = null,
        ValidationOptions? validation = null)
    {
        return await SftpIngestion.RunAsync(
            share.Path,
            Path.Combine(share.Path, "work"),
            validation ?? Lenient,
            _ => Task.FromResult(new SftpCredential("unit-test-user", "unit-test-password-only")),
            sourceFactory ?? (_ => source),
            _ => { },
            CancellationToken.None);
    }

    private static byte[] CreateFixtureBytes(int extractNumber)
    {
        using var temp = new TempDirectory();
        return File.ReadAllBytes(KboFixture.CreateZip(temp.Path, extractNumber));
    }

    private static string FixtureName(int extractNumber) =>
        $"KboOpenData_{extractNumber:0000}_2026_10_04_Full.zip";

    private static string CredentialPayload(string username, string password) =>
        $$"""{"username":"{{username}}","password":"{{password}}"}""";

    private static KeyVaultSecret TestSecret(string payload)
    {
        var secret = new KeyVaultSecret(SecretName, payload);
        secret.Properties.Enabled = true;
        secret.Properties.ExpiresOn = Now.AddDays(1);
        return secret;
    }

    private static (string Current, string DatabaseHash) SeedCurrentRelease(TempDirectory share, int extractNumber)
    {
        KboFixture.CreateZip(Path.Combine(share.Path, "incoming"), extractNumber);
        var exitCode = Releaser.Run(share.Path, Path.Combine(share.Path, "baseline-work"), Lenient, _ => { });
        if (exitCode != 0) throw new InvalidOperationException("The synthetic baseline release did not publish.");
        var fileName = File.ReadAllText(Path.Combine(share.Path, Releaser.CurrentFile));
        return (fileName, HashFile(Path.Combine(share.Path, fileName)));
    }

    private static async Task AssertPreserved(TempDirectory share, (string Current, string DatabaseHash) baseline)
    {
        var current = File.ReadAllText(Path.Combine(share.Path, Releaser.CurrentFile));
        await Assert.That(current).IsEqualTo(baseline.Current);
        await Assert.That(HashFile(Path.Combine(share.Path, baseline.Current))).IsEqualTo(baseline.DatabaseHash);
        await Assert.That(File.Exists(Path.Combine(share.Path, "kbo-0502.db"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(share.Path, ".release.lock"))).IsFalse();
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) { return error; }
        return null;
    }

    private static Exception? Capture(Action action)
    {
        try { action(); }
        catch (Exception error) { return error; }
        return null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestSecretClient(
        Func<string, string?, CancellationToken, Task<KeyVaultSecret>> getSecret)
        : SecretClient
    {
        public List<SecretRequest> Requests { get; } = [];

        public override Task<Response<KeyVaultSecret>> GetSecretAsync(
            string name, string? version = null, CancellationToken cancellationToken = default)
            => GetSecretAsync(name, version, null, cancellationToken);

        public override async Task<Response<KeyVaultSecret>> GetSecretAsync(
            string name, string? version, SecretContentType? outContentType,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(new SecretRequest(name, version));
            var secret = await getSecret(name, version, cancellationToken);
            return Response.FromValue(secret, response: null!);
        }
    }

    private sealed record SecretRequest(string Name, string? Version);

    private sealed class FakeSftpSource(IReadOnlyList<RemoteExtract> files, byte[] content) : ISftpSource
    {
        public Exception? ConnectFailure { get; init; }
        public Exception? ListFailure { get; init; }
        public Exception? DownloadFailure { get; init; }
        public int? BytesToWrite { get; init; }
        public CancellationTokenSource? CancelAfterDownload { get; init; }
        public bool Disposed { get; private set; }
        public List<string> DownloadedNames { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ConnectFailure is null ? Task.CompletedTask : Task.FromException(ConnectFailure);
        }

        public Task<IReadOnlyList<RemoteExtract>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ListFailure is null
                ? Task.FromResult(files)
                : Task.FromException<IReadOnlyList<RemoteExtract>>(ListFailure);
        }

        public async Task DownloadAsync(string name, Stream destination, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DownloadedNames.Add(name);
            if (DownloadFailure is not null) throw DownloadFailure;
            var length = Math.Clamp(BytesToWrite ?? content.Length, 0, content.Length);
            await destination.WriteAsync(content.AsMemory(0, length), cancellationToken);
            CancelAfterDownload?.Cancel();
        }

        public void Dispose() => Disposed = true;
    }
}
