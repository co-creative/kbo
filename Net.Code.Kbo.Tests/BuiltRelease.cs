using Net.Code.Kbo.Build;
using TUnit.Core.Interfaces;

namespace Net.Code.Kbo.Tests;

/// <summary>Shared fixture: the <see cref="KboFixture"/> zip built into a release database.</summary>
public sealed class BuiltRelease : IAsyncInitializer, IDisposable
{
    private readonly TempDirectory temp = new();

    public string DbPath { get; private set; } = "";
    public IReadOnlyDictionary<string, long> Counts { get; private set; } = new Dictionary<string, long>();
    public IReadOnlyList<string> ValidationErrors { get; private set; } = [];
    public List<string> Log { get; } = [];

    /// <summary>
    /// Opened with a 40% common-word threshold: with 6 companies, the default 10% would make every word "common".
    /// "bv" (3 of 6 companies) is the common word in this fixture.
    /// </summary>
    public CompanyStore Store { get; private set; } = null!;

    public Task InitializeAsync()
    {
        var zip = KboFixture.CreateZip(temp.Path);
        DbPath = Path.Combine(temp.Path, "kbo-0502.db");
        Counts = ReleaseBuilder.Build(zip, DbPath, Log.Add);
        ValidationErrors = Validator.Validate(DbPath, new ValidationOptions(), Log.Add);
        Store = CompanyStore.Open(DbPath, commonTermFraction: 0.4);
        return Task.CompletedTask;
    }

    public void Dispose() => temp.Dispose();
}
