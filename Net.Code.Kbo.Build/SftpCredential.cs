using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace Net.Code.Kbo.Build;

sealed class SftpCredential(string username, string password)
{
    public string Username { get; } = username;
    public string Password { get; } = password;

    public override string ToString() => "[SFTP credential]";
}

static class SftpCredentialProvider
{
    public static async Task<SftpCredential> GetAsync(CancellationToken cancellationToken)
    {
        var uri = Environment.GetEnvironmentVariable("PLATFORM_SECRETS_VAULT_URI");
        var clientId = Environment.GetEnvironmentVariable("PLATFORM_SECRETS_CLIENT_ID");
        var names = Environment.GetEnvironmentVariable("PLATFORM_SECRETS_NAMES");
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var vaultUri) ||
            vaultUri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(names))
            throw new InvalidOperationException("The job secret configuration is invalid.");

        string? secretName;
        try
        {
            using var mapping = JsonDocument.Parse(names);
            secretName = mapping.RootElement.GetProperty("sftp-credential").GetString();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException("The job secret mapping is invalid.");
        }

        if (string.IsNullOrWhiteSpace(secretName))
            throw new InvalidOperationException("The job secret mapping is invalid.");

        var identity = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
        var options = new SecretClientOptions
        {
            Retry =
            {
                MaxRetries = 3,
                Mode = RetryMode.Exponential,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(5),
                NetworkTimeout = TimeSpan.FromSeconds(30)
            }
        };
        var client = new SecretClient(vaultUri, identity, options);
        return await GetAsync(client, secretName, TimeProvider.System, cancellationToken);
    }

    internal static async Task<SftpCredential> GetAsync(
        SecretClient client, string secretName, TimeProvider time, CancellationToken cancellationToken)
    {
        var secret = (await client.GetSecretAsync(secretName, cancellationToken: cancellationToken)).Value;
        return Parse(secret, time.GetUtcNow());
    }

    internal static SftpCredential Parse(KeyVaultSecret secret, DateTimeOffset now)
    {
        var properties = secret.Properties;
        if (properties.Enabled == false ||
            properties.ExpiresOn is { } expires && expires <= now ||
            properties.NotBefore is { } notBefore && notBefore > now)
            throw new InvalidOperationException("The SFTP credential is disabled or outside its validity period.");

        try
        {
            using var document = JsonDocument.Parse(secret.Value);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException();

            string? username = null;
            string? password = null;
            var count = 0;
            foreach (var property in root.EnumerateObject())
            {
                count++;
                if (property.Value.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException();
                if (property.NameEquals("username") && username is null) username = property.Value.GetString();
                else if (property.NameEquals("password") && password is null) password = property.Value.GetString();
                else throw new InvalidOperationException();
            }

            if (count != 2 || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException();
            return new SftpCredential(username, password);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentNullException)
        {
            throw new InvalidOperationException("The SFTP credential payload is invalid.");
        }
    }
}
