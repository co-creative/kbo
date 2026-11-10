# CoCreative.UI component catalogue

[CoCreativeStyles](#cocreativestyles) · [Button](#button) · [Chip](#chip) · [Card](#card) · [Callout](#callout) · [Input](#input) · [Table](#table) · [DataTable / DataColumn](#datatable--datacolumn) · [FactList / Fact](#factlist--fact) · [SiteHeader](#siteheader) · [Logo](#logo) · [Public types](#public-types)

Ground rules that apply to every component:
- Namespace `CoCreative.UI` (tokens: `CoCreative.Tokens`). Put `@using CoCreative.UI` and `@using CoCreative.Tokens` in the `_Imports.razor` that covers every `.razor` file (in the Blazor Web App template: `Components/_Imports.razor`). A `.razor` file outside its scope renders `<Callout>` etc. as a literal HTML tag, with only an RZ10012 warning.
- No DI registration, no service, no extension method, and the library ships **no JavaScript**. Everything renders plain HTML and works in static SSR. Only `EventCallback`s (`OnClick`, `SelectedChanged`) need interactivity: put that component in a WebAssembly island (`@rendermode InteractiveWebAssembly`), not Interactive Server; the island's `.Client` project setup is in the page patterns (section 1).
- Every component except `CoCreativeStyles` has `AdditionalAttributes` (`CaptureUnmatchedValues`): any HTML attribute passes through to the element named below. A `class` you pass is **merged** with the component's own `cc-` classes (exception: `Fact` passes attributes to `<dd>` as-is).
- No hard-coded colours. Use parameters (`Tone`, `Variant`, `Featured`, `Reversed`) and tokens: `var(--ink-muted)`, `var(--space-6)`, or `CoCreativeTokens.Color.InkMuted.Var`.

---

## CoCreativeStyles
Emits the design system's `<link>` tags in load order: `tokens.css`, `fonts.css`, `base.css` (optional), `cocreative.css`.

**Use when:** always, once, in the `<head>` of `App.razor`. Place it **after** Bootstrap's link (or drop Bootstrap): both define `.h1`–`.h3` and `.small`.
**Don't use when:** standalone WebAssembly `index.html`; write the four `<link>`s by hand (`_content/CoCreative.Tokens/tokens.css`, `_content/CoCreative.UI/fonts.css`, `.../base.css`, `.../cocreative.css`).

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `IncludeBase` | `bool` | `true` | Also load `base.css` (body font, `paper` background, `ink` text, plain `h1`–`h3`). Set `false` to leave `<body>` alone. |

Render mode: static. URLs go through `Assets[...]`, so fingerprinted URLs are used when the host calls `MapStaticAssets()`. No `AdditionalAttributes`.

```razor
<head>
    ...
    <CoCreativeStyles />
    <link rel="stylesheet" href="@Assets["app.css"]" />
    <HeadOutlet />
</head>
```

Theme: light by default, follows `prefers-color-scheme`; `data-theme="dark"`/`"light"` on `<html>` or any element forces it for that subtree. Apps add no theme switch (request one in `co-creative/co-creative-ui` if needed).

---

## Button
Mono, lowercase, square button; the primary one starts with a `>` prompt. Renders `<button type="button">`, or `<a>` when `Href` is set.

**Use when:** an action (submit, navigate, trigger). One `Primary` per view. `Ghost` for inline actions next to text.
**Don't use when:** a tag, status or filter toggle (use `Chip`); a whole clickable content block (use `Card Href`).

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Variant` | `ButtonVariant` | `Secondary` | `Secondary` (ink outline), `Primary` (rosewood fill, `>` prompt), `Ghost` (rosewood text, no box) |
| `Size` | `ButtonSize` | `Md` | `Md` or `Sm` (dense rows, tables) |
| `Prompt` | `bool?` | `null` | Show leading `>`. `null` = on for `Primary` only |
| `Href` | `string?` | `null` | Render as `<a href>` styled as a button |
| `ChildContent` | `RenderFragment?` | | Label: lowercase, verb first ("book a call") |
| `OnClick` | `EventCallback<MouseEventArgs>` | | Click handler (needs interactivity) |
| `AdditionalAttributes` | splat | | On the `<button>`/`<a>`: `type`, `disabled`, `aria-*`, `class` (merged) |

Nothing required. Render mode: static SSR works for `Href` links and `type="submit"` inside a form. `OnClick` only fires in an interactive render mode; apps use a WebAssembly island (`@rendermode InteractiveWebAssembly`), not Interactive Server.

```razor
<Button Variant="ButtonVariant.Primary" type="submit">send brief</Button>
<Button Href="/cases">see our work</Button>
<Button Variant="ButtonVariant.Ghost" Size="ButtonSize.Sm">read the case</Button>
<Button disabled>unavailable</Button>
@* interactive component only *@
<Button Variant="ButtonVariant.Primary" OnClick="@(() => _clicks++)">book a call</Button>
```

---

## Chip
A small mono tag in a 1px outline. A static `<span>`, or a toggle `<button aria-pressed>` when `OnClick` or `SelectedChanged` is set.

**Use when:** labels (tech, owner), statuses (`Tone`), or toggleable filters (`@bind-Selected`).
**Don't use when:** a real action (use `Button`); a message that needs a sentence (use `Callout`).

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Selected` | `bool` | `false` | Blush fill. With a callback set, it's the `aria-pressed` state |
| `SelectedChanged` | `EventCallback<bool>` | | Raised with `!Selected` on click; enables `@bind-Selected` |
| `OnClick` | `EventCallback<MouseEventArgs>` | | Click handler; also makes it a toggle button |
| `Tone` | `Tone` | `Neutral` | Label and outline colour: `Neutral`, `Info` (rosewood), `Success`, `Warning`, `Danger`. Combines with `Selected` |
| `ChildContent` | `RenderFragment?` | | Short lowercase label |
| `AdditionalAttributes` | splat | | On the `<span>`/`<button>`; `class` merged |

The consumer owns the selected state. Render mode: static chips work everywhere. Toggle chips need interactivity (a WebAssembly island, not Interactive Server); in static SSR they render as a `<button>` that does nothing. On click, `OnClick` runs first, then `SelectedChanged`.

```razor
<Chip>kotlin</Chip>
<Chip Tone="Tone.Success">healthy</Chip>
<Chip Selected>app-owned</Chip>
@* interactive component only *@
<Chip @bind-Selected="_kotlinOn">kotlin</Chip>
<Chip Selected="@_selected.Contains(tech)" SelectedChanged="@(on => Toggle(tech, on))">@tech</Chip>
```

---

## Card
An open content block: top and bottom hairlines, no box or shadow. With `Href` the whole card is one `<a>`.

**Use when:** a teaser or summary item (case study, service, app) in a grid or list.
**Don't use when:** a status/error message (use `Callout`); key-value details (use `FactList`). With `Href`, don't put links or buttons inside (the card is already an `<a>`).

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Eyebrow` | `string?` | | Short label, shown mono, uppercase, in `rosewood-text`, with a `// ` prefix (don't type the `//`). The only complete eyebrow in the library; there is no standalone one (request it) |
| `Title` / `TitleContent` | `string?` / `RenderFragment?` | | Title; `TitleContent` (markup) wins |
| `Meta` / `MetaContent` | `string?` / `RenderFragment?` | | Mono facts line ("2 weeks · 1 backlog"); `MetaContent` wins |
| `Featured` | `bool` | `false` | 2px rosewood top rule. One per row |
| `Href` | `string?` | | Makes the whole card a link |
| `ChildContent` | `RenderFragment?` | | One or two sentences of body |
| `AdditionalAttributes` | splat | | On the root `<div>`/`<a>`; `class` merged |

Render order: eyebrow, title, body, meta. Nothing required. Render mode: static, no interactivity.

```razor
<Card Featured Eyebrow="Case study 03" Title="A claims platform in 10 weeks"
      Meta="4 engineers · 1 designer · insurance" Href="/cases/claims">
    We paired with the in-house team to replace a 15-year-old claims flow.
</Card>
```

---

## Callout
A message block: 2px left rule in the tone colour, on `surface`. For a view's error state, a drift warning, or a note.

**Use when:** telling the user something about the page or data (error, warning, success, info).
**Don't use when:** a content teaser (use `Card`); a one-word status in a row (use `Chip Tone`).

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Tone` | `Tone` | `Neutral` | Rule colour; `Neutral` = `line-strong`. `Danger` adds `role="alert"` |
| `Title` / `TitleContent` | `string?` / `RenderFragment?` | | One semibold line ("Cost data unavailable"); `TitleContent` wins |
| `ChildContent` | `RenderFragment?` | | Detail in `ink-muted`: cause, what to do next |
| `AdditionalAttributes` | splat | | On the root `<div>`; `class` merged. Pass `role` to override the danger `alert` |

Render mode: static.

```razor
<Callout Tone="Tone.Danger" Title="Cost data unavailable">Cost Management answered TooManyRequests. Try again in a minute.</Callout>
<Callout Tone="Tone.Info">Changes to platform.app.json apply on the next push to main.</Callout>
```

---

## Input
A labelled text field (or `<textarea>`) with an optional hint or error. Derives from `InputBase<string?>`, like `InputText`.

**Use when:** any text/email/number-as-text field, in an `EditForm` or alone.
**Don't use when:** non-string values: there is no select, checkbox, radio, date, number or file component.
**Controls the library lacks:** use the plain Blazor built-ins `InputSelect`, `InputCheckbox`,
`InputRadioGroup`/`InputRadio`, `InputDate`, `InputNumber`, `InputTextArea`, `InputFile`, each wrapped with a
visible `<label>`, layout via tokens only, no restyling (no `cc-` classes, no custom colours or type), and request the
component in `co-creative/co-creative-ui`.

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Label` | `string` | `""` | **Required** (`EditorRequired`). Visible label; never rely on a placeholder |
| `@bind-Value` (`Value`, `ValueChanged`, `ValueExpression`) | `string?` | | **Required** in practice: a bare `<Input Label="…" />` throws (no `ValueExpression`), as `InputText` does |
| `Hint` | `string?` | | Helper text under the field |
| `Error` | `string?` | | Replaces the hint, sets `aria-invalid`, danger border and `!` marker. Unset: the first validation message for the bound field from the cascading `EditContext` |
| `Multiline` | `bool` | `false` | Render a `<textarea>` |
| `Immediate` | `bool` | `false` | Bind on `oninput` (every keystroke) instead of `onchange` |
| `DisplayName` | `string?` | | Inherited from `InputBase` |
| `AdditionalAttributes` | splat | | `class` goes on the wrapper `<div class="cc-input">`; `id` replaces the generated id; `value`/`name`/`onchange`/`oninput` are ignored; everything else (`type`, `placeholder`, `autocomplete` …) goes on the field |

Also: `Element` (`ElementReference?`) property, e.g. `await input.Element!.Value.FocusAsync()` (interactive only). `id`, `for`, `aria-describedby` (`{id}-hint`) are wired for you. EditContext state classes (`modified`, `valid`, `invalid`) are added to the field.
Render mode: works in **static SSR** forms: it renders a `name` attribute so the field posts (use `EditForm` with `FormName` + `[SupplyParameterFromForm]`, optionally `Enhance`). In interactive modes it binds as usual.
Watch out: the default Blazor template's `app.css` has `.invalid { outline: … }` and `.valid.modified { outline: … }` rules that also hit `Input`; remove them.

```razor
<EditForm Model="Brief" OnValidSubmit="Submit" FormName="brief" Enhance>
    <DataAnnotationsValidator />
    <Input Label="Work email" type="email" Hint="We reply within one working day." @bind-Value="Brief.Email" />
    <Input Label="What are you building?" Multiline @bind-Value="Brief.Description" />
    <Button Variant="ButtonVariant.Primary" type="submit">send brief</Button>
</EditForm>

@code {
    [SupplyParameterFromForm(FormName = "brief")]
    private BriefModel Brief { get; set; } = null!;
    protected override void OnInitialized() => Brief ??= new();
    private void Submit() { /* ... */ }
}
```

---

## Table
A styled `<table>` you write by hand: hairline rows, mono uppercase heads, `surface` on row hover, wrapper that scrolls horizontally on narrow screens.

**Use when:** small or fixed tables, hand-written markup, rows that need full control, or no sorting/filtering.
**Don't use when:** a list of items the user should sort, filter or search (use `DataTable`); key-value pairs of one thing (use `FactList`).

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Caption` | `string?` | | `<caption>` above the table in `ink-muted`; names the table for screen readers |
| `ChildContent` | `RenderFragment?` | | Your `<thead>`, `<tbody>`, `<tr>`, `<th>`, `<td>` |
| `AdditionalAttributes` | splat | | On the `<table>` (not the wrapper); `class` merged |

`class="cc-num"` on a `th`/`td` right-aligns tabular numbers. Render mode: static.

```razor
<Table Caption="Apps on the platform">
    <thead><tr><th>App</th><th>Health</th><th class="cc-num">Cost / month</th></tr></thead>
    <tbody>
        <tr><td>ui</td><td><Chip Tone="Tone.Success">healthy</Chip></td><td class="cc-num">€ 3.10</td></tr>
    </tbody>
</Table>
```

---

## DataTable / DataColumn
`DataTable<TItem>` is a data-driven `Table`: rows from `Items`, columns from `DataColumn<TItem>` children. Sorting, filtering and search are **in the URL query**, so it works in static SSR with no JavaScript, and every view is bookmarkable.

**Use when:** a list of records the user scans, sorts, filters or searches; an admin/overview page.
**Don't use when:** a handful of fixed rows or rows needing custom `<tr>` markup, row spans, or row actions per `<tr>` (use `Table`). `Items` is fully enumerated (`ToList()`) on every render.

**Long lists:** `DataTable` has no paging and filters in memory. Cap `Items` server-side (a bounded query), show a
`Callout` (`Tone.Info`) saying the list is truncated and how to narrow it, request paging in
`co-creative/co-creative-ui`, and build no pager UI in the app.

### DataTable parameters

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Items` | `IEnumerable<TItem>?` | | The rows. Sort/filter/search run in memory over all of them. `null` = no rows |
| `ChildContent` | `RenderFragment?` | | The `DataColumn`s, in display order |
| `Caption` | `string?` | | `<caption>`; also names the toolbar for screen readers |
| `Name` | `string` | `"t"` | Prefix of this table's query keys. **Give each table on a page its own** |
| `Search` | `bool` | `false` | Show a search field (case-insensitive "contains" over searchable columns). Without it, `{Name}.q` is ignored |
| `Toolbar` | `bool` | `false` | Toolbar above the table: search (if `Search`), every column filter, and "sort by / then by" selects |
| `MaxSortLevels` | `int` | `2` | Number of sort selects in the toolbar (capped at the number of sortable columns) |
| `EmptyText` | `string` | `"No matching rows."` | Body text when no row matches |
| `AdditionalAttributes` | splat | | On the `<table>`; `class` merged |

`TItem` is inferred from `Items` and cascades to the columns (`CascadingTypeParameter`), so `DataColumn` needs no `TItem`.

### DataColumn parameters

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Title` | `string` | `""` | **Required** (`EditorRequired`). Header text |
| `Key` | `string?` | slug of `Title` | Name in the URL (`t.sort=key`, `t.f.key=…`). Slug = lowercase ASCII letters/digits, other runs → `-` ("Duration (s)" → `duration-s`). Set it for stable/short URLs. Uniqueness isn't checked; keep keys unique |
| `Value` | `Func<TItem, object?>?` | | Drives sort, filter, search, and display (without `ChildContent`). **Without `Value` the column can't sort, filter or search** |
| `ChildContent` | `RenderFragment<TItem>?` | | Cell markup; use `Context="x"` to name the row. Default: formatted `Value` |
| `Format` | `string?` | | Format string for `IFormattable` values (`"N2"`, `"yyyy-MM-dd"`), current culture |
| `Sortable` | `bool` | `true` | Header is a sort link (needs `Value`) |
| `Filter` | `ColumnFilter` | `None` | Filter kind (needs `Value`): `None`, `Text`, `Select`, `Number`, `Date`, `Auto` |
| `Searchable` | `bool` | `true` | Included in the table search (needs `Value`) |
| `Numeric` | `bool` | `false` | Right-aligned tabular numbers (`cc-num`) on header and cells |

A `DataColumn` outside a `DataTable` throws `InvalidOperationException`. It renders nothing itself.

### URL state (prefix = `Name`, default `t`)

| Query key | Meaning |
| --- | --- |
| `t.sort=cost` / `t.sort=-cost` | Sort ascending / descending. More levels: repeat the key or comma-separate (`t.sort=-cost,app`) |
| `t.f.{key}=…` | Filter value; `Select` filters repeat it, one per ticked value |
| `t.op.{key}=…` | Filter operator (unknown → the kind's default) |
| `t.f2.{key}=…` | Second value, for `between` |
| `t.q=…` | Search text (only when `Search` is on) |

All links and forms preserve query parameters they don't own (other tables', the page's). The "reset" link removes every key starting with `{Name}.`.

### Sorting
- **Header click** (link): unsorted or secondary → ascending; ascending primary → descending; descending primary → unsorted. A header click **replaces** the whole sort with that one column.
- Chevrons: faint up/down when unsorted, up or down when sorted; with several levels a small number shows the order. The primary column gets `aria-sort`.
- **Multi-level** sort: the toolbar's "sort by / then by" selects, or the URL directly.
- Compare: nulls first; values of the same type that are `IComparable` natively; otherwise formatted text, case-insensitive.

### Filtering (`ColumnFilter`)
A filterable column gets a funnel icon in its header (filled when active) that opens a `<details>` menu with sort links worded for the type ("A to Z", "smallest to largest", "oldest to newest"), a "clear sort" link, and a GET filter form with apply/clear. The same filter fields also appear in the toolbar.

| Kind | Control | Operators (`t.op.{key}`) | Matches |
| --- | --- | --- | --- |
| `Text` | operator + text field | `contains` (default), `notcontains`, `eq`, `ne`, `sw` (begins with), `ew` (ends with) | formatted text, case-insensitive |
| `Select` | tick list; rows matching any ticked value stay | none | formatted text, case-insensitive |
| `Number` | operator + two number fields | `eq` (default), `ne`, `gt`, `ge`, `lt`, `le`, `between` | raw `Value` (numeric types only), as `decimal` |
| `Date` | operator + two date fields, by day | `eq` "on" (default), `ne`, `lt` "before", `le`, `gt` "after", `ge`, `between` | raw `Value`: `DateTime`, `DateTimeOffset`, `DateOnly` |
| `Auto` | from the value type | enum/`bool` → `Select`; numeric → `Number`; `DateTime`/`DateTimeOffset`/`DateOnly` → `Date`; else → `Text` | |

- `Select` choices: every enum member (even unused), `true`/`false` for `bool`, else the distinct formatted values sorted; plus `(blank)` when some cell is empty.
- `between` is inclusive and accepts bounds in either order.
- Incomplete or unparsable filters are ignored (e.g. `between` with one bound, number "lots").
- The value type is inferred from the **first non-null** `Value` in `Items` (`Value` returns `object`). If every value is null, `Auto` becomes `Text`.
- Filter input parsing: invariant first (what `<input type=number|date>` sends), then current culture.

### Search and count
- `Search` adds one field: case-insensitive "contains" over the formatted text of every `Searchable` column with a `Value`.
- With `Search` but no `Toolbar` you get a search-only form. With `Toolbar` the form holds search, filters and sort selects, plus "apply" and "reset".
- "N of M rows" shows when `Toolbar` is on or a search/filter is active.

### Render mode
- Static SSR: full support. Links are plain `href`s, forms are `method="get"`; no JS, no interactivity, no `@rendermode` needed.
- Interactive modes: the table reads `NavigationManager.Uri` and re-renders on `LocationChanged`. State still lives only in the URL; there's no callback/`EventCallback` and no C# API for sort or filters; drive them by navigating to a URL (`NavigationManager.GetUriWithQueryParameters`).

### What the consuming page must provide
- `Items`: an in-memory sequence, capped server-side (see "Long lists" above).
- A `Value` on every column that should sort, filter or search, and a type that fits the filter kind (e.g. a real `DateOnly`, not a pre-formatted string, for a `Date` filter).
- A unique `Name` per table on the same page.
- A routable page (`@page`); the table reads the current URL itself, so no `[SupplyParameterFromQuery]` is needed.

```razor
<DataTable Items="Deliveries" Caption="Deliveries" Name="d" Search Toolbar>
    <DataColumn Title="App" Value="d => d.App" Filter="ColumnFilter.Text" />
    <DataColumn Title="Status" Value="d => d.Status" Filter="ColumnFilter.Auto" Context="d">
        <Chip Tone="@(d.Status == DeliveryStatus.Failed ? Tone.Danger : Tone.Success)">@d.Status.ToString().ToLowerInvariant()</Chip>
    </DataColumn>
    <DataColumn Title="Production" Key="prod" Value="d => d.Production" Filter="ColumnFilter.Auto" />
    <DataColumn Title="Duration (s)" Key="duration" Value="d => d.Seconds" Filter="ColumnFilter.Auto" Numeric />
    <DataColumn Title="Date" Value="d => d.Date" Format="yyyy-MM-dd" Filter="ColumnFilter.Auto" />
    <DataColumn Title="Notes" Value="d => d.Notes" Sortable="false" />
</DataTable>

@code {
    public enum DeliveryStatus { Succeeded, Failed, Running }
    public sealed record Delivery(string App, DeliveryStatus Status, bool Production, int Seconds, DateOnly Date, string? Notes);
    private static readonly Delivery[] Deliveries = [ new("ui", DeliveryStatus.Succeeded, true, 173, new(2026, 11, 8), null) ];
}
```
Example URL: `/deliveries?d.sort=-date&d.f.status=Failed&d.op.duration=gt&d.f.duration=60`.

---

## FactList / Fact
Key-value facts as a two-column `<dl>` grid: labels in `ink-muted`, values in `ink`. `Fact` renders one `<dt>`/`<dd>` pair.

**Use when:** the details of one thing (revision, replicas, settings, ids).
**Don't use when:** many items with the same fields (use `Table`/`DataTable`).

`FactList`:

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Mono` | `bool` | `false` | Mono, 13px, tighter rows, values break anywhere. For settings, ids, URLs |
| `ChildContent` | `RenderFragment?` | | `Fact`s, or your own `<dt>`/`<dd>` pairs |
| `AdditionalAttributes` | splat | | On the `<dl>`; `class` merged |

`Fact`:

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Label` / `LabelContent` | `string?` / `RenderFragment?` | | The key; `LabelContent` wins |
| `ChildContent` | `RenderFragment?` | | The value |
| `AdditionalAttributes` | splat | | On the `<dd>`, passed as-is (`class` is not merged) |

Render mode: static.

```razor
<FactList>
    <Fact Label="Active revision">ui-web--0000003</Fact>
    <Fact Label="Health"><Chip Tone="Tone.Success">healthy</Chip></Fact>
</FactList>
<FactList Mono style="margin-top:var(--space-6)">
    <Fact Label="ASPNETCORE_HTTP_PORTS">8080</Fact>
</FactList>
```

---

## SiteHeader
The app's top bar: brand on the left (the mark plus a mono uppercase product name, linking home) and the main `<nav>`.

**Use when:** the top of every page, typically in `MainLayout.razor`.
**Don't use when:** a section heading inside a page (use a heading element such as `h2`, optionally with a type class).

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Product` | `string?` | | Product name next to the mark ("platform"). Ignored with `BrandContent` |
| `BrandHref` | `string` | `"/"` | Brand link target |
| `BrandContent` | `RenderFragment?` | | Replaces mark + product name inside the brand link |
| `NavLabel` | `string` | `"Main"` | `aria-label` of the `<nav>` |
| `ChildContent` | `RenderFragment?` | | Nav links, typically `<NavLink>`s. No `<nav>` is rendered when empty |
| `AdditionalAttributes` | splat | | On the `<header>`; `class` merged |

The active link (class `active`, which `NavLink` sets, or `aria-current="page"`) gets `ink` text and a 2px rosewood underline. Render mode: static (`NavLink` computes `active` on the server too).

```razor
<SiteHeader Product="platform">
    <NavLink href="" Match="NavLinkMatch.All">apps</NavLink>
    <NavLink href="costs">costs</NavLink>
</SiteHeader>
```

---

## Logo
The Co-Creative mark, lockup or wordmark as inline `<svg role="img" aria-label="Co-Creative">`, coloured with `var(--ink)`/`var(--rosewood)` so it follows the theme.

**Use when:** brand in headers/footers (`SiteHeader` already includes the mark).
**Don't use when:** a file is needed (decks, email, print, favicons): use `_content/CoCreative.UI/logos/co-creative-{lockup,mark,wordmark}[-reversed].svg`.

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Variant` | `LogoVariant` | `Lockup` | `Lockup` (mark + wordmark), `Mark` (brackets only), `Wordmark` (word only) |
| `Height` | `int` | `32` | Height in px; width follows. Minimum 20 for mark and lockup |
| `Reversed` | `bool` | `false` | On an `inverse` slab: uses `on-inverse` / `rosewood-on-inverse` |
| `AdditionalAttributes` | splat | | On the `<svg>`; `class` merged |

Render mode: static.

```razor
<Logo />
<Logo Variant="LogoVariant.Mark" Height="40" />
<div style="background:var(--inverse);padding:var(--space-6)"><Logo Height="40" Reversed /></div>
```

---

## Public types

| Type | Namespace | Values / members |
| --- | --- | --- |
| `ButtonVariant` | `CoCreative.UI` | `Secondary` (default), `Primary`, `Ghost` |
| `ButtonSize` | `CoCreative.UI` | `Md` (default), `Sm` |
| `Tone` | `CoCreative.UI` | `Neutral` (default), `Info`, `Success`, `Warning`, `Danger` (used by `Chip`, `Callout`) |
| `LogoVariant` | `CoCreative.UI` | `Lockup` (default), `Mark`, `Wordmark` |
| `ColumnFilter` | `CoCreative.UI` | `None` (default), `Text`, `Select`, `Number`, `Date`, `Auto` |
| `DataTable<TItem>`, `DataColumn<TItem>` | `CoCreative.UI` | see above |
| `CoCreativeTokens` | `CoCreative.Tokens` | `Color.*` (`ColorToken`: `Var`, `CssVariable`, `Light`, `Dark`, `AliasOf`, `For(themeId)`), `Spacing.*`, `Radius.*`, `Border.*`, `FontFamily.*` (`Token`: `Var`, `Value`), `TypeStyle.*` (`TypeStyleToken`; same-named CSS class), `Themes.Light`/`Themes.Dark`; each group has `All` |
| `Defer` | `CoCreative.UI.Internal` | Public only so Razor can find it (`EditorBrowsable(Never)`); internal to `DataTable`. Don't use |

Token use in markup: `style="color:@CoCreativeTokens.Color.InkMuted.Var"` equals `style="color:var(--ink-muted)"`. Type classes from `tokens.css`: `display-xl`, `display`, `h1`–`h3`, `body-lg`, `body`, `small`, `eyebrow`, `button`, `code` (they set font only; `eyebrow` is not uppercase or coloured, so use `Card Eyebrow` instead).
