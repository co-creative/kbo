using Net.Code.Kbo;
using Net.Code.Kbo.Web;
using Net.Code.Kbo.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();
// 503 problem details when there is no release yet (NoReleaseException), see ReleaseProvider
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    if (ctx.Exception is NoReleaseException e) ctx.ProblemDetails.Detail = e.Message;
});
builder.Services.Configure<ExceptionHandlerOptions>(o =>
{
    o.StatusCodeSelector = e => e is NoReleaseException ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;
    // expected until the first release: not an unhandled-exception error (log, telemetry, failure alerts)
    o.SuppressDiagnosticsCallback = ctx => ctx.Exception is NoReleaseException;
});
builder.Services.AddRazorComponents();
// write é, ë, ... as-is instead of &#xE9; (names and descriptions are full of them)
builder.Services.AddWebEncoders(o => o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
builder.Services.AddSingleton<ReleaseProvider>();
// per request, so a release that appears after startup is picked up; the UI checks ReleaseProvider.Current first
builder.Services.AddScoped(sp => sp.GetRequiredService<ReleaseProvider>().Current ?? throw new NoReleaseException());

var app = builder.Build();

// open the release now if there is one; without one the app still starts (/health 200, API 503, UI "no release yet")
_ = app.Services.GetRequiredService<ReleaseProvider>().Current;

app.UseExceptionHandler();
// problem details for empty API errors (404, 400); pages render their own "not found" content
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), api => api.UseStatusCodePages());
// unknown non-API URLs render the Blazor "not found" page
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"),
            ui => ui.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseRouting(); // explicitly after the status code middleware, so a re-executed request is routed again
app.UseAntiforgery();

app.MapDefaultEndpoints();
app.MapOpenApi();
app.MapStaticAssets();
app.MapCompanyApi();
app.MapRazorComponents<App>();

app.Run();
