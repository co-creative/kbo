using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Renci.SshNet;

namespace Net.Code.Kbo.Build;

sealed record RemoteExtract(string Name, long Length);

interface ISftpSource : IDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<RemoteExtract>> ListAsync(CancellationToken cancellationToken);
    Task DownloadAsync(string name, Stream destination, CancellationToken cancellationToken);
}

sealed class FpsSftpSource : ISftpSource
{
    private const string Host = "ftps.economie.fgov.be";
    private const string HostKey = "LcC2+VnBH01X/CGMd44inc/lxMJy/jH1RtwhW9PIGD8";
    private readonly SftpClient client;

    public FpsSftpSource(SftpCredential credential)
    {
        var interactive = new KeyboardInteractiveAuthenticationMethod(credential.Username);
        interactive.AuthenticationPrompt += (_, args) =>
        {
            if (args.Prompts.Count == 1 &&
                args.Prompts[0].Request.Contains("password", StringComparison.OrdinalIgnoreCase))
                args.Prompts[0].Response = credential.Password;
        };
        var connection = new ConnectionInfo(Host, 22, credential.Username,
            new PasswordAuthenticationMethod(credential.Username, credential.Password), interactive)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client = new SftpClient(connection) { OperationTimeout = TimeSpan.FromMinutes(5) };
        client.HostKeyReceived += (_, args) =>
            args.CanTrust = IsTrustedHostKey(args.HostKeyName, args.FingerPrintSHA256);
    }

    internal static bool IsTrustedHostKey(string algorithm, string fingerprint) =>
        algorithm == "ssh-ed25519" && fingerprint == HostKey;

    public Task ConnectAsync(CancellationToken cancellationToken) => client.ConnectAsync(cancellationToken);

    public async Task<IReadOnlyList<RemoteExtract>> ListAsync(CancellationToken cancellationToken)
    {
        var result = new List<RemoteExtract>();
        await foreach (var file in client.ListDirectoryAsync("opendata", cancellationToken))
            if (file.IsRegularFile) result.Add(new RemoteExtract(file.Name, file.Length));
        return result;
    }

    public async Task DownloadAsync(string name, Stream destination, CancellationToken cancellationToken)
    {
        var path = $"opendata/{name}";
        await client.DownloadFileAsync(path, destination, cancellationToken);
        var attributes = await client.GetAttributesAsync(path, cancellationToken);
        if (attributes.Size != destination.Length)
            throw new IOException("The remote extract changed during transfer.");
    }

    public void Dispose() => client.Dispose();
}

static partial class SftpIngestion
{
    internal static async Task<int> RunAsync(
        string share, string workDirectory, ValidationOptions validation,
        Func<CancellationToken, Task<SftpCredential>> getCredential,
        Func<SftpCredential, ISftpSource> createSource,
        Action<string> log, CancellationToken cancellationToken,
        TimeProvider? timeProvider = null, bool incrementalEnabled = false)
    {
        var credential = await getCredential(cancellationToken);
        using var lease = Releaser.AcquireLease(share);
        using var source = createSource(credential);
        log("Connecting to KBO SFTP.");
        await source.ConnectAsync(cancellationToken);
        log("Listing KBO extracts.");
        var files = await source.ListAsync(cancellationToken);
        var current = Releaser.CurrentExtract(share);
        var candidate = SelectNewest(files, current);
        if (candidate is null)
        {
            log("No newer Full extract is available.");
            if (incrementalEnabled && SourceBinding.Open(share, current, log, cancellationToken) is { } existing)
                Releaser.PruneReleases(share, current, existing.PreviousExtract, log);
            return 0;
        }

        var match = FullName().Match(candidate.Name);
        var target = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = (timeProvider ?? TimeProvider.System).GetUtcNow().ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (!incrementalEnabled)
            return await PublishFullAsync(source, candidate, share, workDirectory, validation, month, log, cancellationToken);
        var binding = SourceBinding.Open(share, current, log, cancellationToken);
        if (binding is null || binding.FullMonth != month)
        {
            if (binding is not null && binding.FullMonth != month)
                log("A monthly Full rebuild is due.");
            return await PublishFullAsync(source, candidate, share, workDirectory, validation, month, log, cancellationToken);
        }
        var chain = SelectUpdates(files, current, target);
        if (chain is null)
        {
            log("The Update chain is incomplete; using a Full extract.");
            return await PublishFullAsync(source, candidate, share, workDirectory, validation, month, log, cancellationToken);
        }

        Directory.CreateDirectory(workDirectory);
        var run = Path.Combine(workDirectory, "ingest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var downloads = new List<string>();
        var state = Path.Combine(run, "source.zip");
        var database = Path.Combine(run, "release.db");
        try
        {
            foreach (var update in chain)
            {
                var path = Path.Combine(run, update.Name);
                await DownloadAsync(source, update, path, log, cancellationToken);
                downloads.Add(path);
            }

            var oldSource = SourceBinding.PathFor(share, current);
            IncrementalChanges changes;
            try
            {
                changes = IncrementalState.Apply(oldSource, downloads, state, cancellationToken);
            }
            catch (SourceIncompatibleException)
            {
                log("The source snapshot or Update format is incompatible; using a Full extract.");
                return await PublishFullAsync(source, candidate, share, workDirectory, validation, month, log, cancellationToken);
            }
            if (changes.CodesChanged)
            {
                log("The codebook changed; using a Full extract.");
                return await PublishFullAsync(source, candidate, share, workDirectory, validation, month, log, cancellationToken);
            }
            IncrementalRelease.Build(oldSource, state, Path.Combine(share, Releaser.FileName(current)),
                database, changes, log, cancellationToken);
            var next = new SourceBinding(target, SourceBinding.HashFile(state, cancellationToken), binding.FullMonth, current);
            SourceBinding.Write(database, next);
            if (Validator.Validate(database, validation, log, target).Count > 0) return 1;
            IncrementalPublisher.Publish(share, database, state, next, log, cancellationToken);
            return 0;
        }
        finally
        {
            foreach (var path in downloads) if (File.Exists(path)) File.Delete(path);
            if (File.Exists(state)) File.Delete(state);
            if (File.Exists(database)) File.Delete(database);
            Directory.Delete(run);
        }
    }

    private static async Task<int> PublishFullAsync(
        ISftpSource source, RemoteExtract candidate, string share, string workDirectory,
        ValidationOptions validation, string month, Action<string> log, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workDirectory);
        var run = Path.Combine(workDirectory, "full-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var destination = Path.Combine(run, candidate.Name);
        var database = Path.Combine(run, "release.db");
        try
        {
            await DownloadAsync(source, candidate, destination, log, cancellationToken);
            var expected = int.Parse(FullName().Match(candidate.Name).Groups[1].Value, CultureInfo.InvariantCulture);
            using (var extract = new Extract(destination))
            {
                var meta = extract.Read<MetaRow>("meta.csv").ToDictionary(r => r.Variable, r => r.Value);
                if (meta.GetValueOrDefault("ExtractType") != "full" ||
                    !int.TryParse(meta.GetValueOrDefault("ExtractNumber"), NumberStyles.None,
                        CultureInfo.InvariantCulture, out var number) || number != expected)
                    throw new InvalidDataException("The Full archive metadata does not match its filename.");
            }
            log("Download complete; starting Full build.");
            ReleaseBuilder.Build(destination, database, log);
            var next = new SourceBinding(expected, SourceBinding.HashFile(destination, cancellationToken),
                month, Releaser.CurrentExtract(share));
            SourceBinding.Write(database, next);
            if (Validator.Validate(database, validation, log, expected).Count > 0) return 1;
            cancellationToken.ThrowIfCancellationRequested();
            IncrementalPublisher.Publish(share, database, destination, next, log, cancellationToken);
            return 0;
        }
        finally
        {
            if (File.Exists(database)) File.Delete(database);
            if (File.Exists(destination)) File.Delete(destination);
            Directory.Delete(run);
        }
    }

    private static async Task DownloadAsync(
        ISftpSource source, RemoteExtract candidate, string destination, Action<string> log,
        CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".sftp-{Guid.NewGuid():N}.part");
        try
        {
            log($"Downloading {candidate.Name}.");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 131072, FileOptions.Asynchronous))
            {
                await source.DownloadAsync(candidate.Name, stream, cancellationToken);
                if (candidate.Length <= 0 || stream.Length != candidate.Length)
                    throw new IOException("The extract transfer was incomplete.");
            }
            using (var zip = ZipFile.OpenRead(temporary))
                if (!zip.Entries.Any(e => e.FullName == "meta.csv"))
                    throw new InvalidDataException("The downloaded archive has no metadata.");
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destination))
            {
                if (!await SameContentAsync(temporary, destination, cancellationToken))
                    throw new IOException("A different archive already has this name.");
            }
            else
                File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static RemoteExtract? SelectNewest(IEnumerable<RemoteExtract> files, int current)
    {
        var candidates = new List<(RemoteExtract File, int Number)>();
        foreach (var file in files)
        {
            var match = FullName().Match(file.Name);
            if (!match.Success ||
                !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
                !DateOnly.TryParseExact(match.Groups[2].Value, "yyyy_MM_dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _) || number <= current)
                continue;
            candidates.Add((file, number));
        }

        if (candidates.Count == 0) return null;
        var newest = candidates.Max(c => c.Number);
        var matches = candidates.Where(c => c.Number == newest).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("Multiple Full extracts have the same extract number.");
        return matches[0].File;
    }

    internal static IReadOnlyList<RemoteExtract>? SelectUpdates(IEnumerable<RemoteExtract> files, int current, int target)
    {
        if (current < 1 || target <= current) return null;
        var updates = new Dictionary<int, RemoteExtract>();
        foreach (var file in files)
        {
            var match = UpdateName().Match(file.Name);
            if (!match.Success ||
                !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
                !DateOnly.TryParseExact(match.Groups[2].Value, "yyyy_MM_dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _) || number <= current || number > target)
                continue;
            if (!updates.TryAdd(number, file))
                throw new InvalidDataException("Multiple Updates have the same extract number.");
        }
        var result = new List<RemoteExtract>();
        for (var number = current + 1; ; number++)
        {
            if (!updates.TryGetValue(number, out var update)) return null;
            result.Add(update);
            if (number == target) break;
        }
        return result;
    }

    private static async Task<bool> SameContentAsync(string first, string second, CancellationToken cancellationToken)
    {
        await using var a = File.OpenRead(first);
        await using var b = File.OpenRead(second);
        if (a.Length != b.Length) return false;
        var hashA = await SHA256.HashDataAsync(a, cancellationToken);
        var hashB = await SHA256.HashDataAsync(b, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(hashA, hashB);
    }

    [GeneratedRegex(@"\AKboOpenData_(\d+)_(\d{4}_\d{2}_\d{2})_Full\.zip\z", RegexOptions.CultureInvariant)]
    private static partial Regex FullName();

    [GeneratedRegex(@"\AKboOpenData_(\d+)_(\d{4}_\d{2}_\d{2})_Update\.zip\z", RegexOptions.CultureInvariant)]
    private static partial Regex UpdateName();
}
