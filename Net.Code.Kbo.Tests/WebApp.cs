using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TUnit.Core.Interfaces;

namespace Net.Code.Kbo.Tests;

/// <summary>The web app hosted in-process, serving the <see cref="KboFixture"/> release.</summary>
public sealed class WebApp : IAsyncInitializer, IAsyncDisposable
{
    private readonly BuiltRelease release = new();
    // any public type from the web assembly works as entry point marker (Program is ambiguous with the kbo tool's)
    private WebApplicationFactory<Net.Code.Kbo.Web.Components.App>? factory;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await release.InitializeAsync();
        factory = new WebApplicationFactory<Net.Code.Kbo.Web.Components.App>().WithWebHostBuilder(web => web
            .UseEnvironment("Testing")
            .UseSetting("Kbo:Database", release.DbPath)
            .UseSetting("Kbo:Share", null)
            .UseSetting("Kbo:CommonTermFraction", "0.5"));
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (factory is not null) await factory.DisposeAsync();
        release.Dispose();
    }
}
