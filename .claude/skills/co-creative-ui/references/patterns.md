# Page patterns: composing Co-Creative pages in Blazor static SSR

How to put the components together into whole pages. Parameters are in the component catalogue and token names in the
tokens reference; this file only shows composition. Everything here comes from `src/CoCreative.UI`, the gallery
(`samples/CoCreative.Gallery`) and the README. Where those are silent, it says so and gives the plain-Blazor approach.

Ground rules:
- Pages are **static SSR** (no `@rendermode`). When a component really needs interactivity (`Button OnClick`,
  `Chip OnClick`/`SelectedChanged`), put it in a WebAssembly island (`@rendermode InteractiveWebAssembly`, setup in
  section 1), not Interactive Server. `Button OnClick`, `Chip OnClick`/`@bind-Selected` and `Input Immediate` do
  nothing in static SSR.
- Use the components and the `cc-` classes as they are. If you need page CSS, use tokens only (`var(--space-6)`,
  `var(--ink-muted)`, `var(--line-strong)` …), never hex values or ad-hoc pixel sizes.
- Known gaps (request them in `co-creative/co-creative-ui`; don't build a styled version in the app): select, checkbox, radio,
  switch, date and file inputs; validation-summary styling; navigation beyond `SiteHeader`; page container/layout;
  skip link; standalone eyebrow/section label; theme switch; spinner, skeleton, progress, toast and empty-state
  components; `DataTable` paging. The sections below give the interim plain-Blazor approach where there is one.

## 1. App setup

Reference the package (it brings `CoCreative.Tokens`). It comes from the private co-creative GitHub Packages feed and
targets `net11.0`; see the README's "Use it" for `nuget.config` and the token.

```xml
<PackageReference Include="CoCreative.UI" Version="x.y.z" />
```

Put the usings in the `_Imports.razor` that covers every `.razor` file (in the Blazor Web App template:
`Components/_Imports.razor`). A `.razor` file outside its scope renders `<Callout>` etc. as a literal HTML tag, with
only an RZ10012 warning.

```razor
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using CoCreative.UI
@using CoCreative.Tokens
```

`Components/App.razor`: `<CoCreativeStyles />` in `<head>`, after Bootstrap if you kept it (Bootstrap also defines
`.h1`–`.h3` and `.small`; better to drop it), before your own stylesheet.

```razor
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <BasePath />
    <ResourcePreloader />
    <CoCreativeStyles />                         @* tokens, fonts, base (body + h1–h3), components *@
    <link rel="stylesheet" href="@Assets["app.css"]" />
    <link rel="stylesheet" href="@Assets["MyApp.styles.css"]" />
    <ImportMap />
    <HeadOutlet />
</head>
<body>
    <Routes />
    <script src="@Assets["_framework/blazor.web.js"]"></script>
</body>
```

`<CoCreativeStyles IncludeBase="false" />` leaves `body` and `h1`–`h3` alone (for libraries or embedded UI).

`Program.cs`: the design system registers **no services**. What the gallery needs is standard Blazor:
`AddRazorComponents()` (plus `.AddInteractiveWebAssemblyComponents()` only if you have islands), `UseAntiforgery()`
(forms), `MapStaticAssets()` (serves `_content/CoCreative.*`), `MapRazorComponents<App>()` (plus
`.AddInteractiveWebAssemblyRenderMode()` and `.AddAdditionalAssemblies(...)` for islands). Never add Interactive
Server. The gallery also sets
`<BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>` in the csproj, which matters
for the redirect in section 4.

WebAssembly island (only if a component needs interactivity): the components that use `@rendermode
InteractiveWebAssembly` live in a separate `.Client` project (`Microsoft.NET.Sdk.BlazorWebAssembly`). That project
needs its own `<PackageReference Include="CoCreative.UI" Version="x.y.z" />` and its own `_Imports.razor` with
`@using CoCreative.UI` (plus `@using CoCreative.Tokens` if it uses the C# token API). The styles still come from the
server project's `App.razor` (`<CoCreativeStyles />`); add nothing to the client project for CSS. (The gallery's
`samples/CoCreative.Gallery.Client` uses a `ProjectReference` only because it lives in the same repo.)

Clean-up after `dotnet new blazor`: delete the template's `.invalid` / `.valid.modified` outline rules from `app.css`
(they hit `Input`, which already marks errors), and its Bootstrap link.

Theming: apps rely on `prefers-color-scheme` (light by default, dark when the OS asks for it) and add **no theme
switch**. If an app needs one, request it in `co-creative/co-creative-ui`. `data-theme="dark"|"light"` on an element
to force a theme for that subtree (e.g. an always-dark section) is fine.

## 2. App shell and navigation

`SiteHeader` gives the `<header>`, the brand link (mark + product name, `aria-label` "Co-Creative" on the logo SVG)
and a `<nav aria-label="Main">`. Fill it with `<NavLink>`s: `NavLink` adds `class="active"` on the server, even in
static SSR, and the CSS underlines `.active` or `[aria-current="page"]` in rosewood. Use `Match="NavLinkMatch.All"`
for the home link, or it is active everywhere.

What the source does **not** provide, so the layout owns it:
- no `<main>` landmark, page container, max width or page gutter (`.cc-header` has no horizontal padding);
- no skip link and no visually-hidden utility;
- no footer, breadcrumb, side nav or mobile menu. On narrow screens `.cc-header` and its nav simply wrap
  (`flex-wrap`), which is the intended mobile behaviour; keep nav labels short and the link count small.

`Components/Layout/MainLayout.razor`:

```razor
@inherits LayoutComponentBase

<a class="app-skip" href="#main">skip to content</a>
<div class="app-shell">
    <SiteHeader Product="invoices">
        <NavLink href="" Match="NavLinkMatch.All">overview</NavLink>
        <NavLink href="invoices">invoices</NavLink>
        <NavLink href="settings">settings</NavLink>
    </SiteHeader>
    <main id="main" tabindex="-1" class="app-main">@Body</main>
</div>
```

```css
/* app.css: tokens only */
.app-shell { padding: 0 var(--space-6); }
.app-main { padding: var(--space-8) 0 var(--space-16); display: flex; flex-direction: column; gap: var(--space-6); }
.app-main:focus { outline: none; }
.app-skip { position: absolute; clip-path: inset(50%); white-space: nowrap; }
.app-skip:focus { clip-path: none; padding: var(--space-2) var(--space-3); background: var(--paper); color: var(--ink);
                  outline: var(--border-mark) solid var(--focus); }
```

Page title: every page sets `<PageTitle>` and has exactly one `<h1>`. Keep the template's
`<FocusOnNavigate RouteData="routeData" Selector="h1" />` in `Routes.razor` (the gallery does), so focus moves to the
new page's heading after enhanced navigation.

## 3. Content pages

Building blocks: plain `h1`–`h3` (styled by `base.css` like `.h1`–`.h3`; `h4`+ are unstyled), type classes for size
(`.display`, `.body-lg`, `.small`, `.code`) without changing the heading level, `Card` for teasers,
`FactList`/`Fact` for key-value details, `Callout` for notes and status.

- Headings go in order (`h1` → `h2` → `h3`); pick the level by structure, the class by looks:
  `<h1 class="display">` is fine, `<h3>` because it "looks right" is not.
- `Card` titles are `<div>`s, not headings. Put an `h2` above a group of cards. With `Href`, the whole card is the link,
  so the title must make sense as link text; don't nest other links or buttons in a linked card.
- Eyebrows (the small mono uppercase rosewood label with a `// ` prefix): the only complete one is `Card`'s `Eyebrow`
  parameter. The `.eyebrow` type class is neither uppercase nor coloured; don't use `<p class="eyebrow">` with
  hand-added `text-transform`/colour. A standalone eyebrow or section label outside a `Card` is a gap: request it.
- `Featured`: one per row. Lay cards out with your own token-based grid in scoped CSS (below). `.cc-grid`, `.cc-row`
  and `.cc-stack` are the design spec's preview helpers (with their own padding) and are **not for apps**.
- `FactList Mono` for machine values (ids, URLs, settings); plain `FactList` for human ones.

```razor
@page "/apps/{Name}"

<PageTitle>@Name · apps</PageTitle>

<h1>@Name</h1>
<p class="body-lg">Invoice generation for the finance team.</p>

<Callout Tone="Tone.Info">Changes to platform.app.json apply on the next push to main.</Callout>

<h2>Runtime</h2>
<FactList>
    <Fact Label="Health"><Chip Tone="Tone.Success">healthy</Chip></Fact>
    <Fact Label="Replicas">0–1</Fact>
</FactList>
<FactList Mono>
    <Fact Label="image">crcocreative.azurecr.io/invoices/web:296c561</Fact>
</FactList>

<h2>Related</h2>
<div class="app-cards">
    <Card Featured Eyebrow="Runbook" Title="Rotate the signing key" Meta="5 min · no downtime" Href="docs/rotate-key">
        Step by step, with the commands to copy.
    </Card>
    <Card Eyebrow="Runbook" Title="Restore a deleted invoice" Href="docs/restore">From the nightly backup.</Card>
</div>

@code {
    [Parameter] public string Name { get; set; } = "";
}
```

```css
/* the page's .razor.css: tokens only (px only for the grid track minimum) */
.app-cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: var(--space-6); }
```

## 4. Forms

`Input` is the only form component. From its source (`InputBase<string?>`):
- renders `<label>` + `<input>`, or `<textarea>` with `Multiline`;
- `Label` is required and always visible; `Hint` is helper text under the field; `Error` replaces the hint, sets
  `aria-invalid="true"`, the `danger` border and a `!` marker. Hint and error share one slot linked by
  `aria-describedby`;
- without `Error`, it shows the **first validation message** of its bound field from the cascading `EditContext`, so you
  don't need `<ValidationMessage>`; the message renders right under the field;
- any other attribute goes to the field: `type` (`email`, `tel`, `url`, `password`, `search`, `date` …; the value is
  always a `string?`), `placeholder`, `autocomplete`, `required`, `maxlength`, `id`. `class` goes on the wrapper;
- always bind (`@bind-Value`); a bare `<Input Label="…" />` throws. It renders a `name`, so it posts in SSR forms.
- No required-field marker. The gallery doesn't use the `required` attribute (it would trigger the browser's unstyled
  bubbles before your messages); it relies on `[Required]` with a helpful `ErrorMessage`. Mark the exception in the
  label instead: "Description (optional)".

Static SSR form: `EditForm` with `FormName`, `Enhance` (posts without a full reload), a `[SupplyParameterFromForm]`
model and `DataAnnotationsValidator`. `EditForm` adds the antiforgery token itself (`UseAntiforgery()` must be in
`Program.cs`); a plain `<form method="post" @formname="…">` needs `<AntiforgeryToken />` inside.
On the platform, Data Protection keys live in memory: with scale-to-zero or more than one replica, antiforgery tokens
(and protected cookies) don't survive a cold start or a replica switch. Keep `maxReplicas: 1` in `platform.app.json`
or persist the keys (see the co-creative-platform skill, runtime contract).

Buttons: one `Primary` `type="submit"` per form (it gets the `>` prompt). Cancel is a `Ghost` or `Secondary` with
`Href` (a link, so it works without JS). Labels are lowercase, verb first.

After a successful post, redirect (post-redirect-get) and show a success `Callout` from a query parameter. The
gallery only flips a flag on the same response; the redirect below is standard Blazor, not shown there.

```razor
@page "/brief"
@using System.ComponentModel.DataAnnotations
@inject NavigationManager Nav
@inject IBriefStore Briefs

<PageTitle>Send a brief</PageTitle>
<h1>Send a brief</h1>

@if (Sent)
{
    <Callout Tone="Tone.Success" Title="Brief sent" role="status">We reply within one working day.</Callout>
}

<EditForm Model="Brief" OnValidSubmit="SubmitAsync" FormName="brief" Enhance class="app-form">
    <DataAnnotationsValidator />
    <Input Label="Work email" type="email" autocomplete="email" Hint="We reply within one working day."
           @bind-Value="Brief.Email" Error="@_emailError" />
    <Input Label="Company" autocomplete="organization" @bind-Value="Brief.Company" />
    <Input Label="What are you building? (optional)" Multiline @bind-Value="Brief.Description" />
    <div class="app-actions">
        <Button Variant="ButtonVariant.Primary" type="submit">send brief</Button>
        <Button Variant="ButtonVariant.Ghost" Href="">cancel</Button>
    </div>
</EditForm>

@code {
    private string? _emailError;

    [SupplyParameterFromForm(FormName = "brief")] private BriefModel Brief { get; set; } = null!;
    [SupplyParameterFromQuery] private bool Sent { get; set; }

    protected override void OnInitialized() => Brief ??= new();

    private async Task SubmitAsync()
    {
        if (await Briefs.ExistsAsync(Brief.Email!)) { _emailError = "We already have a brief from this address."; return; }
        await Briefs.SaveAsync(Brief);
        Nav.NavigateTo("brief?sent=true");   // a redirect in SSR; with BlazorDisableThrowNavigationException it doesn't throw
    }

    public sealed class BriefModel
    {
        [Required(ErrorMessage = "Enter your work email so we can reply.")]
        [EmailAddress(ErrorMessage = "That doesn't look like an email address. Check for typos.")]
        public string? Email { get; set; }
        [Required(ErrorMessage = "Enter your company name.")] public string? Company { get; set; }
        public string? Description { get; set; }
    }
}
```

```css
.app-form { display: flex; flex-direction: column; gap: var(--space-4); }
.app-actions { display: flex; flex-wrap: wrap; gap: var(--space-3); }
```

Server-side errors: set `Error` on the field (as above), or add them to a `ValidationMessageStore` on the
`EditContext` and `Input` picks them up. Form-level failures (service down) go in a `Callout Tone="Tone.Danger"` above
the form. `ValidationSummary` is not styled by the design system; prefer per-field messages.

Not available as components: select, checkbox, radio, switch, date picker, number, file upload.
**Controls the library lacks:** use the plain Blazor built-ins `InputSelect`, `InputCheckbox`,
`InputRadioGroup`/`InputRadio`, `InputDate`, `InputNumber`, `InputTextArea`, `InputFile`, each wrapped with a
visible `<label>`, layout via tokens only, no restyling (no `cc-` classes, no custom colours or type), and request the
component in `co-creative/co-creative-ui`.

## 5. Tables and lists

| Need | Use |
| --- | --- |
| A handful of rows you write by hand, no sorting | `Table` with your own `<thead>`/`<tbody>` |
| A list from data that people sort, filter or search | `DataTable` + `DataColumn` |
| One record's properties | `FactList` |
| A few linked items with a sentence each | `Card`s in a grid |

`Table`: always pass `Caption` (it names the table for screen readers), `class="cc-num"` on number `th`/`td`, and
status as a `Chip` with a text label.

```razor
<Table Caption="Apps on the platform">
    <thead><tr><th>App</th><th>Health</th><th class="cc-num">Cost / month</th></tr></thead>
    <tbody>
        @foreach (var a in Apps)
        {
            <tr><td><a href="apps/@a.Name">@a.Name</a></td><td><Chip Tone="@a.Tone">@a.Health</Chip></td><td class="cc-num">@a.Cost.ToString("C")</td></tr>
        }
    </tbody>
</Table>
```

`DataTable`: all state (sort, filters, search) lives in the URL query, so it works in static SSR without JS and
every view can be bookmarked. Header links sort; `Filter` adds a column menu; `Toolbar` adds one GET form with every
filter and multi-level sort; it shows "N of M rows". Give each table on a page its own `Name`, and keep `Key`s stable
(they are in people's bookmarks).

**Long lists:** `DataTable` has no paging and filters in memory. Cap `Items` server-side (a bounded query), show a
`Callout` (`Tone.Info`) saying the list is truncated and how to narrow it, request paging in
`co-creative/co-creative-ui`, and build no pager UI in the app.

```razor
<DataTable Items="_deliveries" Caption="Deliveries" Name="d" Search Toolbar EmptyText="No deliveries match these filters.">
    <DataColumn Title="App" Value="d => d.App" Filter="ColumnFilter.Text" />
    <DataColumn Title="Status" Value="d => d.Status" Filter="ColumnFilter.Auto" Context="d">
        <Chip Tone="@ToneOf(d.Status)">@d.Status.ToString().ToLowerInvariant()</Chip>
    </DataColumn>
    <DataColumn Title="Duration (s)" Key="duration" Value="d => d.Seconds" Numeric Filter="ColumnFilter.Auto" />
    <DataColumn Title="Date" Value="d => d.Date" Format="yyyy-MM-dd" Filter="ColumnFilter.Auto" />
</DataTable>
```

Empty results: `EmptyText` is for "nothing matches the filters" (a row in the table body). When there is no data at
all, don't render an empty table with filters; render the empty state from section 6 instead.

## 6. States

There is **no spinner, skeleton, progress, toast or empty-state component**. Use these:

| State | Pattern |
| --- | --- |
| Loading | `[StreamRendering]` on the page: the first chunk renders the heading and a short muted line, the data replaces it when ready. No animated indicator. |
| Empty (no data yet) | `Callout` (neutral or `Info`) saying what will appear here, plus the one action that creates it (`Button Variant="ButtonVariant.Primary" Href=…`). |
| Empty (filters) | `DataTable EmptyText`. |
| Error | `Callout Tone="Tone.Danger"`: `Title` says what failed, body says what to do next. It gets `role="alert"` automatically. Keep the rest of the page usable. |
| Warning / degraded | `Callout Tone="Tone.Warning"`; per row, `Chip Tone="Tone.Warning"`. |
| Success | after a post: redirect and show `Callout Tone="Tone.Success"` (section 4). Pass `role="status"`. |

```razor
@page "/costs"
@attribute [StreamRendering]
@inject ICostService Costs

<PageTitle>Costs</PageTitle>
<h1>Costs</h1>

@if (_error is not null)
{
    <Callout Tone="Tone.Danger" Title="Cost data unavailable">@_error Try again in a minute.</Callout>
}
else if (_rows is null)
{
    <p class="small" style="color: var(--ink-muted)" role="status">Loading costs…</p>
}
else if (_rows.Count == 0)
{
    <Callout Title="No costs yet">Costs appear the day after your first deployment.</Callout>
}
else
{
    <DataTable Items="_rows" Caption="Cost per app" Name="c" Search>
        <DataColumn Title="App" Value="r => r.App" />
        <DataColumn Title="Cost / month" Key="cost" Value="r => r.Amount" Format="N2" Numeric />
    </DataTable>
}

@code {
    private List<CostRow>? _rows;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        try { _rows = await Costs.GetAsync(); }
        catch (CostUnavailableException ex) { _error = ex.Message; }
    }
}
```

Unhandled exceptions go to the `/Error` page via `UseExceptionHandler`; give that page the same shell, an `h1` and a
danger `Callout` (the template's `text-danger` classes don't exist in this design system). The template's
`#blazor-error-ui` bar only matters for interactive islands; restyle it with tokens if you keep it.

## 7. Accessibility and keyboard checklist

Already handled by the components (verified in source):
- Focus: a `--focus`-coloured `--border-mark` outline on `:focus-visible` for buttons, toggle chips, linked cards,
  input fields, header brand and nav links, and data table links, menus and choices.
- Semantics: `Button` is a real `<button type="button">` or `<a>` (prompt `>` is `aria-hidden`); toggle `Chip` is a
  `<button aria-pressed>`; `Card Href` is one `<a>`; `FactList` is a `<dl>`; `Table` uses `<caption>`; `SiteHeader` is
  `<header>` + labelled `<nav>`; `Logo` is `role="img"` with `aria-label="Co-Creative"`.
- `Input`: `label for`, generated `id`, `aria-describedby` to the hint/error, `aria-invalid`; errors show as text, not
  only the red border.
- `Callout Tone.Danger` gets `role="alert"`.
- `DataTable`: `aria-sort` on the primary sort column, `role="search"` labelled toolbar, labelled filter groups and
  menu toggles, sorting and filtering by plain links and GET forms (fully keyboard-usable, no JS).
- `prefers-reduced-motion` turns off button transitions; dark theme follows the OS.

Still yours:
- `<main>` landmark, skip link, one `<h1>` per page, headings in order, `<PageTitle>` per page.
- A meaningful `Label` on every `Input` (never a placeholder as the label); error messages that say how to fix it;
  `autocomplete` on personal fields.
- Link and button text that makes sense on its own ("read the claims case", not "click here"); linked `Card` titles
  likewise.
- `alt` text on your own images (`alt=""` if decorative); a `Caption` on every table.
- Colour is never the only signal: every `Chip`/`Callout` tone needs words ("failed", "drift").
- Focus styles for your own plain links: `base.css` adds none outside the components (copy the library's focus rule
  from the tokens reference). Built-in form controls keep the browser's focus ring; don't restyle them.
- `role="status"` on success and loading messages you render yourself.
- `Table`'s horizontal-scroll wrapper is not focusable; on narrow screens keep columns few or put a focusable element
  in each row.
- One `Primary` button per view; `disabled` buttons are dimmed only, so say why an action is unavailable.
