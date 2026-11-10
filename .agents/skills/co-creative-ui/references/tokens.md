# Co-Creative tokens and base styles

Everything here comes from `CoCreative.Tokens` (`wwwroot/tokens.css`, generated from
`src/CoCreative.Tokens/tokens/co-creative.tokens.json`) and `CoCreative.UI` (`wwwroot/base.css`, `fonts.css`,
`cocreative.css`). The "Use" column quotes the `usage` field of the token source. If a token is not listed
here, it does not exist: don't invent `--shadow-*`, `--z-*`, `--duration-*` or `--font-size-*`.

## 1. Loading the CSS

Put the `CoCreativeStyles` component in the `<head>` of `Components/App.razor` (needs `@using CoCreative.UI`):

```razor
<CoCreativeStyles />                      @* tokens + fonts + base + components *@
<CoCreativeStyles IncludeBase="false" />  @* same, but leaves <body> and h1-h3 alone *@
```

It emits these links, in this order, through `@Assets[...]` (so fingerprinted URLs are used when the host
maps static assets):

| Order | Path | Contents |
| --- | --- | --- |
| 1 | `_content/CoCreative.Tokens/tokens.css` | Custom properties for both themes, plus one class per type style |
| 2 | `_content/CoCreative.UI/fonts.css` | `@font-face` for Archivo, IBM Plex Sans, IBM Plex Mono (self-hosted woff2, latin + latin-ext) |
| 3 | `_content/CoCreative.UI/base.css` | Only when `IncludeBase` is true (the default) |
| 4 | `_content/CoCreative.UI/cocreative.css` | All `cc-` component styles |

Standalone WebAssembly (`wwwroot/index.html`) uses the same four paths as plain `<link rel="stylesheet">`
tags in that order (base.css optional). Put your own `app.css` and `<App>.styles.css` links *after*
`<CoCreativeStyles />`, as the gallery does.

If the app still links Bootstrap (the default Blazor template does), put `<CoCreativeStyles />` after it or
remove Bootstrap: Bootstrap also defines `.h1`, `.h2`, `.h3` and `.small`, and whichever loads last wins.

### What you get for free (don't restyle these)

`base.css` is the whole page base. It sets exactly:

| Selector | Styles |
| --- | --- |
| `body` | `margin: 0; font-family: var(--font-sans); background: var(--paper); color: var(--ink)` |
| `h1` | same as `.h1`: Archivo 36px/42px, 800, `letter-spacing: -0.01em`, `font-stretch: 115%` |
| `h2` | same as `.h2`: Archivo 26px/32px, 700, `font-stretch: 115%` |
| `h3` | same as `.h3`: Plex Sans 20px/26px, 600 |

`tokens.css` also sets `color-scheme: light|dark` per theme, so native form controls and scrollbars follow
the theme. A class on a heading (`<h1 class="display">`) wins over the element rule.

Not styled by base.css: links (`a`), `p`, lists, `h4`-`h6`, and a global focus ring. Focus rings exist only on
the library's own interactive parts (`.cc-btn`, `.cc-chip-btn`, `.cc-card-link`, `.cc-input-field`, header
nav, data-table links): `outline: var(--border-mark) solid var(--focus); outline-offset: 2px`. Use
components (`Button`, `Card`, `Input`, `SiteHeader` ...) rather than bare elements so you get them. If you
must style a bare interactive element, copy that exact focus rule.

## 2. Tokens

All colour tokens are already **semantic** (named by role: `paper`, `ink`, `line`, `danger` ...). There is no
raw palette scale (no `--rose-500` etc.); the hex values below are the per-theme values the role resolves to.
Always use `var(--token)` and never the hex: the hex is wrong in the other theme.

### Colour: surfaces and text

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `--paper` | `#f5eeea` | `#1d191a` | Page background: warm blush-white. Never pure white for full pages. |
| `--surface` | `#fffbf9` | `#272223` | Cards, panels, inputs on paper. |
| `--ink` | `#2a2426` | `#f3eae6` | Warm charcoal. Headlines and body text on paper, surface and blush. |
| `--ink-muted` | `#6a5f61` | `#b8aba8` | Secondary text, captions, metadata on paper and surface. |
| `--blush` | `#eed6d8` | `#5a3a3f` | Tint for highlighted sections, selected rows, callouts and chips; ink text on it. (Also the hover fill of buttons and chips.) |

### Colour: lines (borders)

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `--line` | `#e3d8d4` | `#3a3234` | Decorative hairlines, table rules, the plus grid on large areas. |
| `--line-strong` | `#928688` | `#7a6d6f` | Control borders and the 1px card/section rules that carry structure; 3:1 on paper and surface. |

### Colour: brand (rosewood)

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `--rosewood` | `#a94f5e` | `#e39aa5` | Co-Creative rosewood. Primary buttons, the one accent block per view, the mark's second bracket. Fills and borders, not text. |
| `--on-rosewood` | `#ffffff` | `#1d191a` | Text and icons on rosewood fills. |
| `--rosewood-text` | `#a94f5e` | `#e39aa5` | Rosewood as text: links, eyebrows, the `>` prompt, on paper and surface. |

### Colour: inverse slab

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `--inverse` | `#2a2426` | `#f3eae6` | Charcoal slab: hero bands, footers, code panels. |
| `--on-inverse` | `#f3eae6` | `#2a2426` | Text on inverse. |
| `--rosewood-on-inverse` | `#e39aa5` | `#a94f5e` | The rosewood bracket of the logo, and rosewood accents, on inverse slabs (3:1+). |

### Colour: focus and status

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `--focus` | alias `var(--rosewood-text)` | alias `var(--rosewood-text)` | 2px solid focus ring, 2px offset, on paper and surface. The only alias token. |
| `--success` | `#2f6b4f` | `#7fcfa6` | Success text and icons on paper and surface; always with a word or icon. |
| `--warning` | `#83591a` | `#e8b96a` | Warning text and icons on paper and surface; always with a word or icon. |
| `--danger` | `#b3261e` | `#ff8f80` | Errors only, never decorative. Always with an icon and a word; brighter and redder than rosewood so the two never mix. |

There is no `--info` colour: the library's "info" tone (`.cc-chip-info`, `.cc-callout-info`) uses
`--rosewood-text` / `--rosewood`. There are no status background tints; status colours are for text,
icons and borders.

Pairing rules that follow from the usage notes: text on `paper`/`surface`/`blush` is `ink` or `ink-muted`;
text on `rosewood` is `on-rosewood`; text on `inverse` is `on-inverse`; rosewood-coloured text is
`rosewood-text` (or `rosewood-on-inverse` on a slab).

### Spacing (theme-independent)

| Token | Value | Use |
| --- | --- | --- |
| `--space-1` | 4px | Icon-to-label gaps. |
| `--space-2` | 8px | Tight stacks, chip padding. |
| `--space-3` | 12px | Button vertical padding. |
| `--space-4` | 16px | Default gap; button horizontal padding. |
| `--space-6` | 24px | Card padding; the plus-grid pitch. |
| `--space-8` | 32px | Between related blocks. |
| `--space-12` | 48px | Between sections on mobile; the large plus-grid pitch. |
| `--space-16` | 64px | Between sections on desktop. |
| `--space-24` | 96px | Hero padding. |

The scale has gaps on purpose (no 5, 7, 10, 20). Pick the nearest step; don't write `calc()` in-betweens.

### Radius

| Token | Value | Use |
| --- | --- | --- |
| `--radius-0` | 0px | Default for everything: blocks, cards, buttons, chips, images. Co-Creative is square. |
| `--radius-sm` | 2px | Text inputs and checkboxes only, to soften small controls. |

### Borders (and why there are no shadows)

| Token | Value | Use |
| --- | --- | --- |
| `--border-hair` | 1px | Cards, chips, secondary buttons, section rules (`line-strong`); table rows (`line`). |
| `--border-mark` | 2px | Focus rings and the active-tab underline. (Also the featured-card top rule and callout left bar.) |

"Structure comes from 1px rules, not shadows." There are **no shadow/elevation tokens**; don't add
`box-shadow`. Separate things with a `--border-hair` rule in `--line` or `--line-strong`, or a `--surface` /
`--blush` fill.

### Typography

Font families (custom properties):

| Token | Value | Use |
| --- | --- | --- |
| `--font-display` | `"Archivo", "Arial Black", system-ui, sans-serif` | Display and h1/h2 headings, card titles. Self-hosted weights 700-800, `font-stretch` 115%-125% only. |
| `--font-sans` | `"IBM Plex Sans", system-ui, sans-serif` | Body text, h3. Weights 400-600. |
| `--font-mono` | `"IBM Plex Mono", ui-monospace, monospace` | Eyebrows, button labels, chips, labels, code, data. Weights 400, 500, 600. |

Sizes, weights and line heights are **not** custom properties. They exist as type-style classes in
`tokens.css`. Put the class on the element instead of writing `font-size`:

| Class | Family | Size / line height | Weight | Extra | Use |
| --- | --- | --- | --- | --- | --- |
| `.display-xl` | display | 88px / 0.95 | 800 | `-0.02em`, stretch 125% | One per page: hero statements, covers. |
| `.display` | display | 56px / 1 | 800 | `-0.02em`, stretch 125% | Section openers, slide titles. |
| `.h1` | display | 36px / 42px | 800 | `-0.01em`, stretch 115% | Page titles. |
| `.h2` | display | 26px / 32px | 700 | stretch 115% | Section headings. |
| `.h3` | sans | 20px / 26px | 600 | | Card titles, sub-sections. |
| `.body-lg` | sans | 18px / 28px | 400 | | Lead paragraphs, intros. |
| `.body` | sans | 16px / 24px | 400 | | Default body copy. |
| `.small` | sans | 14px / 20px | 400 | | Captions, metadata, helper text. |
| `.eyebrow` | mono | 12px / 16px | 500 | `0.12em` | Spec: "Uppercase labels above headings, prefixed with `//`, in rosewood-text or ink-muted." The class alone is neither; see below. |
| `.button` | mono | 14px / 20px | 500 | | Button and link-button labels, lowercase; the primary one starts with `>`. |
| `.code` | mono | 14px / 20px | 400 | | Inline code, terminal snippets, data, numbers in tables. |

The classes set font only, not colour, case or margin. In particular `.eyebrow` (from the design spec's generated
`tokens.css`) is **not** uppercase and **not** coloured, so it is not a finished eyebrow. The only complete eyebrow
is `Card`'s `Eyebrow` parameter (mono, uppercase, `rosewood-text`, with a `// ` prefix): use `Card Eyebrow`. Don't
use `<p class="eyebrow">` with hand-added `text-transform` or colour; a standalone eyebrow outside a `Card` is a gap:
request it in `co-creative/co-creative-ui`.

C# access (`@using CoCreative.Tokens`): `CoCreativeTokens.Color.Rosewood.Var` (`"var(--rosewood)"`),
`.Light` / `.Dark` (hex), `CoCreativeTokens.Spacing.Space6.Value` (`"24px"`), `CoCreativeTokens.TypeStyle.*`,
and `CoCreativeTokens.Themes.Light` / `.Dark` (`"light"` / `"dark"`).

### Kinds that do not exist

| Kind | Status | What to do instead |
| --- | --- | --- |
| Shadow / elevation | none, by design | Rules and fills (see Borders). |
| Motion / duration / easing | none | The library hard-codes `.14s ease-out` on `.cc-btn` colour transitions and drops it under `prefers-reduced-motion: reduce`. Prefer no motion; if you add any, match that and honour reduced motion. |
| Breakpoints | none | Use intrinsic layout (`flex-wrap`, `grid` with `auto-fit`/`auto-fill` + `minmax()`) in your own scoped CSS (section 4). |
| Layout / container widths | none | See "Size literals" below: `px`/`ch` only for grid track minimums and text measure. |

**Size literals:** `px` or `ch` literals are allowed only for grid track minimums (e.g. `minmax(16rem, 1fr)` or
`minmax(220px, 1fr)`) and text measure (`max-width: 65ch`) in layout CSS; everything else (gaps, padding, margins)
uses `--space-*`, and colours and type always come from tokens and type classes.
| Z-index | none | Avoid stacking contexts; the library has no overlays. |
| Font-size / line-height / weight vars | none | Use the type classes above. |

## 3. Theming (light / dark)

`tokens.css` defines the colour tokens three times; spacing, radius, border and font tokens are on `:root`
only and are the same in both themes.

```css
:root, [data-theme="light"] { color-scheme: light; --paper: #f5eeea; ... }   /* default */
[data-theme="dark"]         { color-scheme: dark;  --paper: #1d191a; ... }
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) { color-scheme: dark; --paper: #1d191a; ... }
}
```

- No attribute: the page follows the OS (`prefers-color-scheme`).
- `data-theme="dark"` on `<html>` forces dark; `data-theme="light"` forces light (opts out of following the OS).
- The attribute works on **any element**: it re-declares the tokens there, so a subtree can be themed
  differently from the page.
- Apps rely on `prefers-color-scheme` and add **no theme switch** (no script, no stored preference). If an app
  needs one, request it in `co-creative/co-creative-ui`. Forcing a theme for a subtree with `data-theme` (e.g. an
  always-dark section) is fine.
- Apps don't redefine tokens (`--paper`, `--rosewood`, `--space-*` …); the values are generated (`tokens.css` says
  "Do not edit"). If a value is missing, ask for a token in `co-creative/co-creative-ui`.
- Code that needs a hex per theme (charts, canvas, email) uses `CoCreativeTokens.Color.X.For(themeId)` or
  `.Light` / `.Dark`, not copied literals.

## 4. Using tokens in app CSS

App CSS is for **layout glue only**: arranging components on a page (flex/grid, gaps, padding, widths) and the
occasional page-level band (`--paper`, `--surface`, `--inverse`). Never restyle a component (no overriding
`.cc-btn`, `.cc-card` ... and no `::deep` into them); if a component lacks a variant you need, that is a
design-system change. Prefer a type class or component parameter over any CSS. The gallery's own CSS is
the model: "Gallery layout only. Everything visual comes from the design system."

Correct (`Pages/Projects.razor.css`, scoped):

```css
.projects { display: flex; flex-direction: column; gap: var(--space-8); padding: var(--space-8) var(--space-6); }
.projects-band { background: var(--inverse); color: var(--on-inverse); padding: var(--space-6); }
.projects-list { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: var(--space-6); }  /* px only as a grid track minimum */
.projects-intro { max-width: 65ch; }   /* ch only as text measure */
.projects-meta { color: var(--ink-muted); }   /* with class="small" on the element for size */
.projects-rule { border-top: var(--border-hair) solid var(--line-strong); }
```

Wrong:

```css
.projects { gap: 30px; padding: 2rem; }                 /* off-scale spacing: use --space-* */
.projects-band { background: #2a2426; color: white; }   /* hex and colour names: break in dark mode */
.projects-meta { font-size: 13px; color: gray; }        /* px font size: use .small; gray: use --ink-muted */
.projects-card { border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,.1); }  /* Co-Creative is square, no shadows */
.projects ::deep .cc-btn { background: #a94f5e; }       /* restyling a component */
```

Inline `style="color:var(--ink-muted)"` or `style="margin-top:var(--space-6)"` is acceptable for one-offs
(the gallery does it), with the same rules: tokens only.
