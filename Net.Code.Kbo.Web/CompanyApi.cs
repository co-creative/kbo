using Microsoft.AspNetCore.Http.HttpResults;

namespace Net.Code.Kbo.Web;

/// <summary>JSON API (specs/azure-architecture.md §7.2). All responses carry the release as ETag.</summary>
static class CompanyApi
{
    public static void MapCompanyApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<ReleaseCacheFilter>();

        api.MapGet("/companies/{number}", Results<Ok<CompanyView>, NotFound> (KboNr number, string? lang, CompanyStore store) =>
                store.Get(number) is { } doc
                    ? TypedResults.Ok(CompanyView.Create(doc, store.Codes, Lang.Normalize(lang)))
                    : TypedResults.NotFound())
            .WithName("GetCompany")
            .WithSummary("A company by enterprise number (any format with 10 digits), with descriptions in nl or fr.");

        api.MapGet("/companies", (string? q, string? name, string? street, string? city, string? postalCode,
                                  string? lang, int? skip, int? take, CompanyStore store) =>
            {
                var language = Lang.Normalize(lang);
                var result = store.Search(new SearchQuery { Text = q, Name = name, Street = street, City = city, PostalCode = postalCode },
                                          skip ?? 0, take ?? CompanyStore.MaxTake);
                return TypedResults.Ok(new SearchResponse(
                    result.Items.Select(s => SummaryView.Create(s, store.Codes, language)).ToList(),
                    result.Ranked,
                    result.IgnoredTerms));
            })
            .WithName("SearchCompanies")
            .WithSummary("Ranked full-text search on name, street, city, postal code and activity; at most 25 results per call.");

        api.MapGet("/release", (CompanyStore store) => TypedResults.Ok(store.Release))
            .WithName("GetRelease")
            .WithSummary("The KBO extract being served.");
    }
}

/// <param name="Ranked">False when the query only had very common words (results are then unranked).</param>
/// <param name="IgnoredTerms">Very common words left out of the query.</param>
sealed record SearchResponse(IReadOnlyList<SummaryView> Items, bool Ranked, IReadOnlyList<string> IgnoredTerms);

/// <summary>Data only changes with a new release: use it as ETag and let clients cache for an hour.</summary>
sealed class ReleaseCacheFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var release = http.RequestServices.GetRequiredService<CompanyStore>().Release;
        var etag = $"\"{release.ExtractNumber}-{release.BuiltAt}\"";
        if (http.Request.Headers.IfNoneMatch == etag) return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = "public, max-age=3600";
        return await next(context);
    }
}
