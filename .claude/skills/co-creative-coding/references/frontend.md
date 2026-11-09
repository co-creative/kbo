# Frontend (Blazor)

- **Static SSR by default.** Server-rendered pages with streaming rendering and enhanced navigation; forms post back
  with `EditForm` + antiforgery. Interactive render modes only where interaction is essential:
  - prefer **WebAssembly islands** (`@rendermode InteractiveWebAssembly`) for small widgets: no server circuit;
  - avoid Interactive Server: its SignalR circuit keeps a replica awake and needs a single replica
    (`maxReplicas: 1`) on the platform.
- **Design system:** use `CoCreative.UI` components and tokens (`co-creative/co-creative-ui`) for a consistent look.
- **Every view has explicit states:** loading, empty, success, partial, and failure (with what failed and what the
  user can do). Never render a blank page on error.
- **Accessibility:** semantic HTML first (headings in order, labels for inputs, buttons for actions, links for
  navigation); keyboard-usable; visible focus; don't rely on colour alone; text alternatives for images and charts.
- **Performance:** no large JavaScript bundles; small inline SVG for charts; paginate long tables; cache expensive
  server calls briefly in memory (apps scale to zero, so caches are cheap and short-lived).
- **Security:** output is encoded by Razor; never inject raw HTML from data. Sign-in comes from Easy Auth
  (`X-MS-CLIENT-PRINCIPAL`); authorize by group/object IDs, not display names or email addresses.
