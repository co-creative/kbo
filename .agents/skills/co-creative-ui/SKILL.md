---
name: co-creative-ui
description: "Build UI in a co-creative app with the CoCreative.UI design system (Blazor components plus CoCreative.Tokens CSS tokens): which component to use, its parameters, the design tokens, page layouts (shell, forms, tables, loading/empty/error/success states), accessibility, and what not to do. WHEN: creating or changing any page, layout, component, form, table or CSS in an app that references CoCreative.UI; choosing colours, spacing or type; adding the package to an app; when a needed component doesn't exist."
metadata:
  author: co-creative
  version: "2.0.0-preview.6"
---

# CoCreative.UI

The co-creative design system for Blazor (.NET 11): Razor components in `CoCreative.UI`, CSS custom properties and
type classes in `CoCreative.Tokens`. Apps are Blazor static SSR by default; the library has **no JavaScript** and
every component renders in static SSR (only `Button`/`Chip` click callbacks need interactivity: use a WebAssembly
island, never Interactive Server).

## Setup (once per app)

1. Reference the NuGet package `CoCreative.UI` (it brings `CoCreative.Tokens`) from the private co-creative feed
   `https://nuget.pkg.github.com/co-creative/index.json`. In `nuget.config`, map `CoCreative.*` to that source
   (`packageSourceMapping`) with `ClearTextPassword` `%NUGET_AUTH_TOKEN%`. Locally:
   `NUGET_AUTH_TOKEN=$(gh auth token)` (token needs `read:packages`); in GitHub Actions: `github.token` with
   `packages: read`; in a Docker build: a BuildKit secret, never a build argument.
2. The `_Imports.razor` that covers every `.razor` file (in the Blazor Web App template:
   `Components/_Imports.razor`): `@using CoCreative.UI` and `@using CoCreative.Tokens`.
3. `App.razor` `<head>`: `<CoCreativeStyles />`, **after** any Bootstrap link (better: remove Bootstrap).

Details, the WebAssembly-island `.Client` project and the standalone-WebAssembly link order: [references/patterns.md](references/patterns.md#1-app-setup).

## Pick the component

| Need | Use | Not |
| --- | --- | --- |
| An action (submit, save, open) | `Button`: `Primary` once per view, `Secondary` (default), `Ghost` inline; `ButtonSize.Sm` in dense rows | a styled `<a>`/`<div>` |
| A tag, status label, filter or toggle | `Chip` (`Tone`) | `Button` |
| A grouped block of content with a title | `Card` | `Callout` |
| An inline message: info, success, warning, error | `Callout` (`Tone`) | a coloured `Card` |
| A labelled text field in a form | `Input` (binds `string?`) | raw `<input>` |
| A small, static table | `Table` | `DataTable` |
| A list people sort, filter or search | `DataTable` + `DataColumn<TItem>` (state in the URL, no paging) | hand-rolled sorting |
| Label/value pairs (details, summaries) | `FactList` + `Fact` | a two-column `Table` |
| The app's top bar | `SiteHeader` | a custom header |
| The brand mark | `Logo` (`LogoVariant`) | an `<img>` of the logo |
| Loading the CSS | `CoCreativeStyles` | hand-written `<link>`s (unless standalone WASM) |

Parameters, render-mode notes and a Razor example for each: [references/components.md](references/components.md).
Page composition (shell, content pages, forms with validation, tables, loading/empty/error/success states,
accessibility checklist): [references/patterns.md](references/patterns.md).

## Tokens

Use the CSS custom properties from `tokens.css`; **never hard-code a colour, font, size or spacing**. Colours are
semantic and switch with the theme (`--paper`, `--surface`, `--ink`, `--ink-muted`, `--line`, `--rosewood`,
`--success`, `--warning`, `--danger`, …); spacing is `--space-1` … `--space-24`; type comes from the classes
`.display-xl`, `.h1`–`.h3`, `.body`, `.small`, `.code` … and `--font-display|sans|mono` (for an eyebrow use
`Card Eyebrow`: the `.eyebrow` class is not uppercase or coloured). There are no shadow, motion, breakpoint,
z-index or font-size tokens: don't invent them, and don't redefine tokens. Dark mode is automatic
(`prefers-color-scheme`; apps add no theme switch), or forced for a subtree with `data-theme="dark|light"`. Full
tables and intended use: [references/tokens.md](references/tokens.md).

## Don'ts

- **No custom CSS where a component or token exists.** App CSS (scoped `.razor.css`) is layout glue only: grid,
  gaps, widths, using `var(--space-*)`. Never restyle `cc-` classes or a component's internals.
- **No hard-coded colours or sizes** (`#hex`, `rgb()`, colour names, px font sizes). If no token fits, ask for one.
  `px` or `ch` literals are allowed only for grid track minimums (e.g. `minmax(16rem, 1fr)` or `minmax(220px, 1fr)`)
  and text measure (`max-width: 65ch`) in layout CSS; everything else (gaps, padding, margins) uses `--space-*`, and
  colours and type always come from tokens and type classes.
- **Don't copy a component's markup or CSS into the app**, and don't fork or subclass it to change its look.
- **No other UI frameworks** (Bootstrap, Tailwind, MudBlazor, Radzen, Fluent UI …) and no extra JS for UI
  behaviour. The Blazor template's Bootstrap clashes with `.h1`–`.h3` and `.small`: remove it.
- **Don't rely on undocumented internals**: `CoCreative.UI.Internal.*`, or `cc-` classes used outside their
  component (e.g. styling your own `<select>` with `cc-input-field`) are not a supported API.
- **Colour is never the only signal**: pair a tone with text or an icon; keep labels on every field.

## A component is missing

Known gaps today: select, checkbox, radio, switch, date and file inputs; validation-summary styling; navigation
beyond `SiteHeader` (side nav, breadcrumb, mobile menu, footer); page container/layout; skip link; standalone
eyebrow/section label; theme switch; spinner, skeleton, progress, toast and empty-state components; `DataTable`
paging (until then: cap `Items` server-side and say so in a `Callout`; no pager in the app).

When you need one of these (or anything else the catalogue lacks): **don't build a styled version in the app.**
Open an issue in `co-creative/co-creative-ui` (what you need, where, which states), or make the change there
(spec first, then component, tests and gallery page; a new tag publishes it). Until a control ships, use the plain
Blazor built-in (`InputSelect`, `InputCheckbox`, `InputRadioGroup`/`InputRadio`, `InputDate`, `InputNumber`,
`InputTextArea`, `InputFile`) wrapped with a visible `<label>`, layout via tokens only, no restyling, and say so in
your summary.
