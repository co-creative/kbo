using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Net.Code.Kbo.Build;

namespace Net.Code.Kbo.Tests;

public class SftpCredentialTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly object EnvironmentGate = new();
    private const string SecretName = "sftp-credential-test";
    private const string TestUser = "unit-test-user";
    private const string TestPassword = "unit-test-password-only";

    [Test]
    public async Task Parse_ValidCredential_ReturnsCredentials_AndRedactsToString()
    {
        var secret = CreateSecret(ValidPayload(TestUser, TestPassword));
        secret.Properties.ExpiresOn = Now.AddMinutes(1);
        secret.Properties.NotBefore = Now;

        var credential = SftpCredentialProvider.Parse(secret, Now);

        await Assert.That(credential.Username).IsEqualTo(TestUser);
        await Assert.That(credential.Password).IsEqualTo(TestPassword);
        await Assert.That(credential.ToString()).IsEqualTo("[SFTP credential]");
        await Assert.That(credential.ToString()).DoesNotContain(TestPassword);
        await Assert.That(credential.ToString()).DoesNotContain(TestUser);
    }

    [Test]
    public async Task Parse_EnabledCredentialWithoutOptionalValidityDates_IsAccepted()
    {
        var secret = CreateSecret(ValidPayload(TestUser, TestPassword));
        secret.Properties.ExpiresOn = null;
        secret.Properties.NotBefore = null;

        var credential = SftpCredentialProvider.Parse(secret, Now);

        await Assert.That(credential.Username).IsEqualTo(TestUser);
        await Assert.That(credential.Password).IsEqualTo(TestPassword);
    }

    [Test]
    public async Task Parse_ExplicitlyDisabledCredential_IsRejected()
    {
        var secret = CreateSecret(ValidPayload(TestUser, TestPassword));
        secret.Properties.Enabled = false;

        var error = Capture(() => SftpCredentialProvider.Parse(secret, Now));

        await Assert.That(error?.GetType()).IsEqualTo(typeof(InvalidOperationException));
    }

    [Test]
    public async Task Parse_NullEnabled_UsesKeyVaultDefaultEnabledBehavior()
    {
        var secret = CreateSecret(ValidPayload(TestUser, TestPassword));
        secret.Properties.Enabled = null;

        var credential = SftpCredentialProvider.Parse(secret, Now);

        await Assert.That(credential.Username).IsEqualTo(TestUser);
        await Assert.That(credential.Password).IsEqualTo(TestPassword);
    }

    [Test]
    public async Task Parse_ExpiryAtOrBeforeNow_RejectsCredential()
    {
        foreach (var expiresOn in new[] { Now, Now.AddTicks(-1) })
        {
            var secret = CreateSecret(ValidPayload(TestUser, TestPassword));
            secret.Properties.ExpiresOn = expiresOn;

            var error = Capture(() => SftpCredentialProvider.Parse(secret, Now));

            await Assert.That(error?.GetType()).IsEqualTo(typeof(InvalidOperationException));
        }
    }

    [Test]
    public async Task Parse_NotBeforeAfterNow_RejectsButAtNowIsAccepted()
    {
        var notYetValid = CreateSecret(ValidPayload(TestUser, TestPassword));
        notYetValid.Properties.NotBefore = Now.AddTicks(1);
        var error = Capture(() => SftpCredentialProvider.Parse(notYetValid, Now));
        await Assert.That(error?.GetType()).IsEqualTo(typeof(InvalidOperationException));

        var validAtBoundary = CreateSecret(ValidPayload(TestUser, TestPassword));
        validAtBoundary.Properties.NotBefore = Now;
        var credential = SftpCredentialProvider.Parse(validAtBoundary, Now);
        await Assert.That(credential.Username).IsEqualTo(TestUser);
    }

    [Test]
    public async Task Parse_MalformedPayloads_AreRejectedWithoutEchoingPayload()
    {
        var malformedPayloads = new[]
        {
            "{",
            "[]",
            "{\"username\":\"unit-test-user\"}",
            "{\"password\":\"unit-test-password-only\"}",
            "{\"username\":\" \",\"password\":\"unit-test-password-only\"}",
            "{\"username\":\"unit-test-user\",\"password\":\"\"}",
            "{\"username\":7,\"password\":\"unit-test-password-only\"}",
            "{\"username\":\"unit-test-user\",\"password\":\"unit-test-password-only\",\"extra\":\"value\"}",
            "{\"username\":\"first\",\"username\":\"second\",\"password\":\"unit-test-password-only\"}",
            "{\"Username\":\"unit-test-user\",\"password\":\"unit-test-password-only\"}"
        };

        foreach (var payload in malformedPayloads)
        {
            var secret = CreateSecret(payload);
            var error = Capture(() => SftpCredentialProvider.Parse(secret, Now));

            await Assert.That(error?.GetType()).IsEqualTo(typeof(InvalidOperationException));
            await Assert.That(error!.Message).DoesNotContain(TestPassword);
        }
    }

    [Test]
    public async Task GetAsync_UsesSecretNameVersionlessAndInjectedTime()
    {
        var time = new FixedTimeProvider(Now);
        var client = new FakeSecretClient((_, _, _) => Task.FromResult(
            CreateSecret(ValidPayload(TestUser, TestPassword), expiresOn: Now.AddTicks(1))));

        var credential = await SftpCredentialProvider.GetAsync(
            client, SecretName, time, CancellationToken.None);

        await Assert.That(credential.Username).IsEqualTo(TestUser);
        await Assert.That(time.GetUtcNowCalls).IsEqualTo(1);
        await Assert.That(client.Requests.Count).IsEqualTo(1);
        await Assert.That(client.Requests[0].Name).IsEqualTo(SecretName);
        await Assert.That(client.Requests[0].Version).IsNull();
    }

    [Test]
    public async Task GetAsync_MissingSecret_PropagatesNotFound()
    {
        var client = new FakeSecretClient((_, _, _) =>
            Task.FromException<KeyVaultSecret>(new RequestFailedException(404, "Not Found")));

        var error = await CaptureAsync(async () =>
            await SftpCredentialProvider.GetAsync(client, SecretName, new FixedTimeProvider(Now), CancellationToken.None));

        await Assert.That(error?.GetType()).IsEqualTo(typeof(RequestFailedException));
        await Assert.That(((RequestFailedException)error!).Status).IsEqualTo(404);
        await Assert.That(client.Requests.Count).IsEqualTo(1);
        await Assert.That(client.Requests[0].Version).IsNull();
    }

    [Test]
    public async Task GetAsync_CancellationPassesTokenToSecretClient()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new FakeSecretClient((_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(CreateSecret(ValidPayload(TestUser, TestPassword)));
        });

        var error = await CaptureAsync(async () =>
            await SftpCredentialProvider.GetAsync(client, SecretName, new FixedTimeProvider(Now), cancellation.Token));

        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(((OperationCanceledException)error!).CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(client.Requests.Single().CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task GetAsync_RejectsInvalidConfigurationBeforeClientCreation()
    {
        const string vaultUriVariable = "PLATFORM_SECRETS_VAULT_URI";
        const string clientIdVariable = "PLATFORM_SECRETS_CLIENT_ID";
        const string namesVariable = "PLATFORM_SECRETS_NAMES";
        Task<SftpCredential>? request = null;

        lock (EnvironmentGate)
        {
            var oldVaultUri = Environment.GetEnvironmentVariable(vaultUriVariable);
            var oldClientId = Environment.GetEnvironmentVariable(clientIdVariable);
            var oldNames = Environment.GetEnvironmentVariable(namesVariable);
            try
            {
                Environment.SetEnvironmentVariable(vaultUriVariable, "http://unit-test.vault.invalid");
                Environment.SetEnvironmentVariable(clientIdVariable, "unit-test-client-id");
                Environment.SetEnvironmentVariable(namesVariable, """{"sftp-credential":"sftp-credential-test"}""");
                // The async method reads and rejects the invalid URI before its first await or any client/network work.
                request = SftpCredentialProvider.GetAsync(CancellationToken.None);
            }
            finally
            {
                Environment.SetEnvironmentVariable(vaultUriVariable, oldVaultUri);
                Environment.SetEnvironmentVariable(clientIdVariable, oldClientId);
                Environment.SetEnvironmentVariable(namesVariable, oldNames);
            }
        }

        var error = await CaptureAsync(async () => await request!);

        await Assert.That(error?.GetType()).IsEqualTo(typeof(InvalidOperationException));
    }

    [Test]
    public async Task GetAsync_SecondVersionlessCallObservesRotatedSecret()
    {
        var secrets = new Queue<KeyVaultSecret>(
        [
            CreateSecret(ValidPayload("unit-test-user-a", "unit-test-password-a")),
            CreateSecret(ValidPayload("unit-test-user-b", "unit-test-password-b"))
        ]);
        var client = new FakeSecretClient((_, _, _) => Task.FromResult(secrets.Dequeue()));
        var time = new FixedTimeProvider(Now);

        var first = await SftpCredentialProvider.GetAsync(client, SecretName, time, CancellationToken.None);
        var second = await SftpCredentialProvider.GetAsync(client, SecretName, time, CancellationToken.None);

        await Assert.That(first.Username).IsEqualTo("unit-test-user-a");
        await Assert.That(second.Username).IsEqualTo("unit-test-user-b");
        await Assert.That(first.Password).IsEqualTo("unit-test-password-a");
        await Assert.That(second.Password).IsEqualTo("unit-test-password-b");
        await Assert.That(client.Requests.Count).IsEqualTo(2);
        await Assert.That(client.Requests.All(request => request.Name == SecretName)).IsTrue();
        await Assert.That(client.Requests.All(request => request.Version is null)).IsTrue();
    }

    private static KeyVaultSecret CreateSecret(
        string payload, bool? enabled = true, DateTimeOffset? expiresOn = null, DateTimeOffset? notBefore = null)
    {
        var secret = new KeyVaultSecret(SecretName, payload);
        secret.Properties.Enabled = enabled;
        secret.Properties.ExpiresOn = expiresOn;
        secret.Properties.NotBefore = notBefore;
        return secret;
    }

    private static string ValidPayload(string username, string password) =>
        $$"""{"username":"{{username}}","password":"{{password}}"}""";

    private static Exception? Capture(Action action)
    {
        try { action(); }
        catch (Exception error) { return error; }
        return null;
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) { return error; }
        return null;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public int GetUtcNowCalls { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            GetUtcNowCalls++;
            return now;
        }
    }

    private sealed class FakeSecretClient(
        Func<string, string?, CancellationToken, Task<KeyVaultSecret>> getSecret)
        : SecretClient
    {
        public List<SecretRequest> Requests { get; } = [];

        public override Task<Response<KeyVaultSecret>> GetSecretAsync(
            string name, string? version = null, CancellationToken cancellationToken = default)
            => GetSecretAsync(name, version, null, cancellationToken);

        // SDK 4.12 resolves calls with optional arguments to this overload.
        public override async Task<Response<KeyVaultSecret>> GetSecretAsync(
            string name, string? version, SecretContentType? outContentType,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(new SecretRequest(name, version, cancellationToken));
            var secret = await getSecret(name, version, cancellationToken);
            return Response.FromValue(secret, response: null!);
        }
    }

    private sealed record SecretRequest(string Name, string? Version, CancellationToken CancellationToken);

}
