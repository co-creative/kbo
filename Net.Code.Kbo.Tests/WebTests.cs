using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using static Net.Code.Kbo.Tests.KboFixture;

namespace Net.Code.Kbo.Tests;

[ClassDataSource<WebApp>(Shared = SharedType.PerClass)]
public class WebTests(WebApp app)
{
    HttpClient Client => app.Client;

    async Task<JsonElement> Json(string url)
    {
        var response = await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    async Task<(HttpStatusCode Status, string Body)> Get(string url)
    {
        var response = await Client.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // ---- API

    [Test]
    public async Task Api_GetCompany_ResolvesCodesInRequestedLanguage()
    {
        var c = await Json($"/api/companies/{Acme}?lang=fr");

        await Assert.That(c.GetProperty("number").GetString()).IsEqualTo(Acme);
        await Assert.That(c.GetProperty("name").GetString()).IsEqualTo("Acme Software BV");
        await Assert.That(c.GetProperty("juridicalForm").GetString()).IsEqualTo("Société anonyme");
        await Assert.That(c.GetProperty("addresses")[0].GetProperty("line").GetString()).IsEqualTo("Meir 1, 2000 Anvers");
        await Assert.That(c.GetProperty("establishments")[0].GetProperty("addresses")[0].GetProperty("city").GetString()).IsEqualTo("Gand");
        await Assert.That(c.GetProperty("activities")[0].GetProperty("description").GetString())
            .IsEqualTo("Production d'électricité à partir de sources non renouvelables");
    }

    [Test]
    [Arguments("0403170701")]
    [Arguments("BE0403.170.701")]
    public async Task Api_GetCompany_AcceptsAnyNumberFormat(string number)
    {
        await Assert.That((await Json($"/api/companies/{number}")).GetProperty("number").GetString()).IsEqualTo(Acme);
    }

    [Test]
    public async Task Api_UnknownCompany_Is404ProblemDetails()
    {
        var (status, body) = await Get("/api/companies/0999.999.999");

        await Assert.That(status).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(body).Contains("\"status\":404");
    }

    [Test]
    public async Task Api_InvalidNumber_Is400()
    {
        await Assert.That((await Get("/api/companies/abc")).Status).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Api_Search_ReturnsLanguageResolvedSummaries()
    {
        var r = await Json("/api/companies?q=veldstraat&lang=fr");
        var first = r.GetProperty("items")[0];

        await Assert.That(r.GetProperty("ranked").GetBoolean()).IsTrue();
        await Assert.That(first.GetProperty("number").GetString()).IsEqualTo(Acme);
        await Assert.That(first.GetProperty("city").GetString()).IsEqualTo("Anvers");
        await Assert.That(first.GetProperty("juridicalForm").GetString()).IsEqualTo("Société anonyme");
    }

    [Test]
    public async Task Api_Search_FieldFilters_AndCommonWords()
    {
        var byField = await Json("/api/companies?name=acme&postalCode=9000");
        var common = await Json("/api/companies?q=bv");

        await Assert.That(byField.GetProperty("items").GetArrayLength()).IsEqualTo(1);
        await Assert.That(common.GetProperty("ranked").GetBoolean()).IsFalse();
        await Assert.That(common.GetProperty("items").GetArrayLength()).IsEqualTo(3);
    }

    [Test]
    public async Task Api_Release()
    {
        var r = await Json("/api/release");

        await Assert.That(r.GetProperty("extractNumber").GetInt32()).IsEqualTo(502);
        await Assert.That(r.GetProperty("counts").GetProperty("companies").GetInt64()).IsEqualTo(4);
    }

    [Test]
    public async Task Api_CachesPerRelease_WithETag()
    {
        var first = await Client.GetAsync($"/api/companies/{Acme}");
        var etag = first.Headers.ETag;
        await Assert.That(etag).IsNotNull();
        await Assert.That(first.Headers.CacheControl!.Public).IsTrue();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/companies/{Acme}");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag!.Tag));
        var second = await Client.SendAsync(request);

        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
    }

    [Test]
    public async Task Api_OpenApiDocument()
    {
        var (status, body) = await Get("/openapi/v1.json");

        await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).Contains("SearchCompanies");
    }

    // ---- UI

    [Test]
    public async Task Ui_Home_HasSearchFormAndReleaseFooter()
    {
        var (status, html) = await Get("/");

        await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("action=\"/search\"");
        await Assert.That(html).Contains("extract 502");
    }

    [Test]
    public async Task Ui_Search_ListsResultsWithLinks()
    {
        var (_, html) = await Get("/search?q=acme");

        await Assert.That(html).Contains($"href=\"/companies/{Acme}\"");
        await Assert.That(html).Contains("Acme Software BV");
        await Assert.That(html).Contains("1 establishment");
    }

    [Test]
    public async Task Ui_Search_KeepsLanguageInLinks()
    {
        var (_, html) = await Get("/search?q=acme&lang=fr");

        await Assert.That(html).Contains($"href=\"/companies/{Acme}?lang=fr\"");
        await Assert.That(html).Contains("Société anonyme");
    }

    [Test]
    public async Task Ui_Search_CommonWordsHint()
    {
        var (_, html) = await Get("/search?q=bv");

        await Assert.That(html).Contains("results are not ranked");
    }

    [Test]
    public async Task Ui_Search_EnterpriseNumberRedirectsToCompany()
    {
        var response = await Client.GetAsync("/search?q=0403170701");

        await Assert.That((int)response.StatusCode).IsEqualTo(302);
        await Assert.That(response.Headers.Location!.ToString()).EndsWith($"/companies/{Acme}");
    }

    [Test]
    public async Task Ui_Company_ShowsDetailsInFrench()
    {
        var (status, html) = await Get($"/companies/{Acme}?lang=fr");

        await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(html).Contains("<h1>Acme Software BV</h1>");
        await Assert.That(html).Contains("Société anonyme");
        await Assert.That(html).Contains("Veldstraat 10, 9000 Gand");
        await Assert.That(html).Contains("mailto:info@acme.example");
    }

    [Test]
    public async Task Ui_Company_StruckOffAddressIsMarked()
    {
        var (_, html) = await Get($"/companies/{Delta}");

        await Assert.That(html).Contains("struck off 2020-01-01");
    }

    [Test]
    public async Task Ui_UnknownPath_Is404Page()
    {
        var (status, html) = await Get("/no/such/page");

        await Assert.That(status).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(html).Contains("<h1>Not found</h1>");
    }

    [Test]
    public async Task Ui_UnknownCompany_Is404Page()
    {
        var (status, html) = await Get("/companies/0999.999.999");

        await Assert.That(status).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(html).Contains("<h1>Not found</h1>");
    }
}
