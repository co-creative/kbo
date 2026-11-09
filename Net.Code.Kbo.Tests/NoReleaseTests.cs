using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Net.Code.Kbo.Build;

namespace Net.Code.Kbo.Tests;

/// <summary>
/// The platform contract: the web app starts on an empty share (first deployment) and picks up the first
/// release once the job has published it, without a restart.
/// </summary>
public class NoReleaseTests
{
    [Test]
    public async Task StartsWithoutRelease_ThenPicksUpTheFirstOne()
    {
        using var share = new TempDirectory();
        await using var factory = new WebApplicationFactory<Net.Code.Kbo.Web.Components.App>().WithWebHostBuilder(web => web
            .UseEnvironment("Testing")
            .UseSetting("Kbo:Database", null)
            .UseSetting("Kbo:Share", share.Path)
            .UseSetting("Kbo:RetryInterval", "00:00:00")
            .UseSetting("Kbo:CommonTermFraction", "0.4"));
        using var client = factory.CreateClient();

        var api = await client.GetAsync("/api/release");
        await Assert.That(api.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(api.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");

        var page = await client.GetAsync("/?q=acme");
        await Assert.That(page.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await page.Content.ReadAsStringAsync()).Contains("No KBO release is available yet");

        KboFixture.CreateZip(Path.Combine(share.Path, "incoming"), 502);
        await Assert.That(Releaser.Run(share.Path, Path.Combine(share.Path, "work"), new ValidationOptions { MaxOrphanFraction = 1 }, _ => { })).IsEqualTo(0);

        await Assert.That((await client.GetAsync("/api/release")).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await client.GetStringAsync("/?q=acme")).Contains(KboFixture.Acme);
    }

    [Test]
    public async Task SwitchesToANewRelease_AndBackOnRollback()
    {
        using var share = new TempDirectory();
        var work = Path.Combine(share.Path, "work");
        var validation = new ValidationOptions { MaxOrphanFraction = 1 };
        KboFixture.CreateZip(Path.Combine(share.Path, "incoming"), 502);
        await Assert.That(Releaser.Run(share.Path, work, validation, _ => { })).IsEqualTo(0);

        await using var factory = new WebApplicationFactory<Net.Code.Kbo.Web.Components.App>().WithWebHostBuilder(web => web
            .UseEnvironment("Testing")
            .UseSetting("Kbo:Database", null)
            .UseSetting("Kbo:Share", share.Path)
            .UseSetting("Kbo:CheckInterval", "00:00:00")
            .UseSetting("Kbo:CommonTermFraction", "0.4"));
        using var client = factory.CreateClient();
        await Assert.That(await client.GetStringAsync("/api/release")).Contains("\"extractNumber\":502");

        KboFixture.CreateZip(Path.Combine(share.Path, "incoming"), 503);
        await Assert.That(Releaser.Run(share.Path, work, validation, _ => { })).IsEqualTo(0);
        await Assert.That(await client.GetStringAsync("/api/release")).Contains("\"extractNumber\":503");

        // a broken current.txt keeps the open release
        await File.WriteAllTextAsync(Path.Combine(share.Path, Releaser.CurrentFile), "kbo-9999.db");
        await Assert.That(await client.GetStringAsync("/api/release")).Contains("\"extractNumber\":503");

        await File.WriteAllTextAsync(Path.Combine(share.Path, Releaser.CurrentFile), "kbo-0502.db");
        await Assert.That(await client.GetStringAsync("/api/release")).Contains("\"extractNumber\":502");
        await Assert.That(await client.GetStringAsync("/?q=acme")).Contains(KboFixture.Acme);
    }
}
