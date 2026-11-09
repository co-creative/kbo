# Testing

- **What:** test behaviour, not implementation. Domain rules get fast unit tests; endpoints get a few integration
  tests through the real pipeline (`WebApplicationFactory`); external services are faked at the adapter interface,
  never by mocking SDK internals.
- **Pyramid, pragmatically:** many unit tests, some integration tests, a smoke test in delivery (`/health`). End-to-end
  browser tests only for critical flows.
- **Frameworks:** follow the repository (`dotnet-aca-v1` uses TUnit on Microsoft.Testing.Platform; older repos may
  use xUnit). Don't mix frameworks in one repository.
- **Naming:** `Method_Scenario_Expectation` or a sentence (`Lookup_returns_404_for_unknown_number`). One reason to
  fail per test; arrange–act–assert.
- **Determinism:** no real clock (`TimeProvider`/`FakeTimeProvider`), no network, no shared mutable state; tests run in
  parallel.
- **Contract:** keep a test that starts the web app without Azure configuration and gets `/health` 200: it mirrors the
  platform's contract check.
- **Before declaring done:** `dotnet test` green locally; for UI changes, check the page in a browser
  (`dotnet run` or the AppHost).
- Use the .NET test skills (`run-tests`, `test-anti-patterns`, `assertion-quality`, `coverage-analysis`) when
  writing or reviewing tests.
