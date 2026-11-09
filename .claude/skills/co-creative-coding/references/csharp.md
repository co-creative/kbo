# C# and .NET conventions

- **Target the repository's SDK** (`global.json`); the platform runs .NET 10/11. Nullable reference types and
  `TreatWarningsAsErrors` stay on; fix warnings, don't suppress them without a comment explaining why.
- **Types:** `record`/`record struct` for data, `required` members, `init` setters, primary constructors for DI.
  Keep classes `sealed` unless designed for inheritance. Small types with one reason to change (SRP).
- **Async:** async all the way; every I/O method takes a `CancellationToken` and passes it on. No `async void`
  (except event handlers), no `.Result`/`.Wait()`. Use `ConfigureAwait(false)` only in libraries.
- **Errors:**
  - expected outcomes (not found, invalid input) are return values (`Results.NotFound()`, a result type), not
    exceptions;
  - unexpected failures throw and are handled once at the edge (exception handler, problem details, job exit code 1);
  - never swallow exceptions; log once, with structured context (`logger.LogError(ex, "Import of {File} failed", file)`).
- **Logging:** structured templates (`{Name}`), no string interpolation in log calls, no secrets or personal data in
  logs. Use `ILogger<T>`; source-generated `[LoggerMessage]` for hot paths.
- **Configuration:** bind settings to options classes (`IOptions<T>`), validate at startup (`ValidateOnStart`), read
  from environment variables (`Section__Key`). No secrets in configuration.
- **Azure SDKs:** create clients once (DI singletons) with `ManagedIdentityCredential(AZURE_CLIENT_ID)` /
  `DefaultAzureCredential`; endpoints come from capability settings (`STORAGE__TABLEENDPOINT`, …).
- **HTTP:** `IHttpClientFactory`/typed clients with resilience (ServiceDefaults), timeouts and cancellation.
- **Dates and culture:** `DateTimeOffset`/`TimeProvider` (testable time), invariant culture for machine formats.
- **Refactoring:** small behaviour-preserving steps under green tests (after Fowler's *Refactoring*); rename freely,
  extract methods/types when a name would explain the code better than a comment.
- **Comments** explain *why*, not *what*. Public APIs get XML docs only when they're consumed outside the project.
