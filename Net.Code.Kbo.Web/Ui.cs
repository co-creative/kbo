using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace Net.Code.Kbo.Web;

/// <summary>Small helpers for the static-SSR pages: the language lives in the query string (?lang=fr).</summary>
static class Ui
{
    /// <summary>Current language from the URL.</summary>
    public static string LangOf(NavigationManager nav) =>
        Lang.Normalize(QueryHelpers.ParseQuery(new Uri(nav.Uri).Query).TryGetValue("lang", out var l) ? l.ToString() : null);

    /// <summary>A link that keeps the language; empty query values are left out.</summary>
    public static string Link(string path, string lang, params (string Key, string? Value)[] query)
    {
        var values = query.Where(q => !string.IsNullOrWhiteSpace(q.Value)).ToDictionary(q => q.Key, q => q.Value);
        if (lang != "nl") values["lang"] = lang;
        return QueryHelpers.AddQueryString(path, values);
    }

    public static string CompanyLink(string number, string lang) => Link($"/companies/{number}", lang);

    /// <summary>The current page in another language.</summary>
    public static string SwitchLang(NavigationManager nav, string lang) =>
        "/" + nav.ToBaseRelativePath(nav.GetUriWithQueryParameter("lang", lang == "nl" ? null : lang));
}
