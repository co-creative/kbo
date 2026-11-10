# Frontend (Blazor)

- **Static SSR by default.** Server-rendered pages with streaming rendering and enhanced navigation; forms post back
  with `EditForm` + antiforgery. Interactive render modes only where interaction is essential:
  - prefer **WebAssembly islands** (`@rendermode InteractiveWebAssembly`) for small widgets: no server circuit;
  - avoid Interactive Server: its SignalR circuit keeps a replica awake and needs a single replica
    (`maxReplicas: 1`) on the platform.
- **Design system:** build every page, form, table and style with `CoCreative.UI` components and `CoCreative.Tokens`
  tokens (`co-creative/co-creative-ui`); no custom styling where a component or token exists. Skill **co-creative-ui**
  has the component catalogue, tokens, page patterns, don'ts and what to do when a component is missing.
- **Every view has explicit states:** loading, empty, success, partial, and failure (with what failed and what the
  user can do). Never render a blank page on error.
- **Accessibility:** semantic HTML first (headings in order, labels for inputs, buttons for actions, links for
  navigation); keyboard-usable; visible focus; don't rely on colour alone; text alternatives for images and charts.
- **Performance:** no large JavaScript bundles; small inline SVG for charts; cache expensive server calls briefly in
  memory (apps scale to zero, so caches are cheap and short-lived).
- **Long lists:** cap the rows server-side (a bounded query) and say in a `Callout` that the list is truncated and how
  to narrow it. `DataTable` has no paging yet: request it in `co-creative/co-creative-ui`; build no pager UI in the app.
- **Security:** output is encoded by Razor; never inject raw HTML from data. Sign-in comes from Easy Auth
  (`X-MS-CLIENT-PRINCIPAL`); authorize by group/object IDs, not display names or email addresses.
