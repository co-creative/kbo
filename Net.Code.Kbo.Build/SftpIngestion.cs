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
        Action<string> log, CancellationToken cancellationToken)
    {
        var credential = await getCredential(cancellationToken);
        using var lease = Releaser.AcquireLease(share);
        using var source = createSource(credential);
        log("Connecting to KBO SFTP.");
        await source.ConnectAsync(cancellationToken);
        log("Listing KBO Full extracts.");
        var files = await source.ListAsync(cancellationToken);
        var candidate = SelectNewest(files, Releaser.CurrentExtract(share));
        if (candidate is null)
        {
            log("No newer Full extract is available.");
            return 0;
        }

        var incoming = Path.Combine(share, "incoming");
        Directory.CreateDirectory(incoming);
        var temporary = Path.Combine(incoming, $".sftp-{Guid.NewGuid():N}.part");
        var destination = Path.Combine(incoming, candidate.Name);
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
                    throw new InvalidDataException("The downloaded archive is not a Full extract.");

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destination))
            {
                if (!await SameContentAsync(temporary, destination, cancellationToken))
                    throw new IOException("A different incoming archive already has this name.");
            }
            else
                File.Move(temporary, destination);

            cancellationToken.ThrowIfCancellationRequested();
            log("Download complete; starting release validation.");
            return Releaser.RunWhileLeased(share, workDirectory, validation, log);
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
}
