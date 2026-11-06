using Net.Code.Kbo;
using Net.Code.Kbo.Web;
using Net.Code.Kbo.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddRazorComponents();
// write é, ë, ... as-is instead of &#xE9; (names and descriptions are full of them)
builder.Services.AddWebEncoders(o => o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
builder.Services.AddSingleton(_ => ReleaseLocator.Open(builder.Configuration, builder.Environment));

var app = builder.Build();

// fail fast at startup when the release database can't be opened
var store = app.Services.GetRequiredService<CompanyStore>();
app.Logger.LogInformation("Serving KBO extract {Extract} ({Companies:N0} companies)", store.Release.ExtractNumber, store.CompanyCount);

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
