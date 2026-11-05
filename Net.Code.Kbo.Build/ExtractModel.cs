namespace Net.Code.Kbo.Build;

/// <summary>One company as written to the release database.</summary>
sealed record BuiltCompany(long Number, CompanyDocument Document, CompanySummary Summary, SearchFields Search);

/// <summary>Text for the company_fts columns.</summary>
sealed record SearchFields(string Name, string Street, string City, string PostalCode, string Activity);

/// <summary>
/// The whole extract in memory (about 3.2 GB for 2M enterprises), keyed by entity number.
/// Load once, then enumerate <see cref="Companies"/> in enterprise-number order.
/// </summary>
sealed class ExtractModel
{
    private static readonly string[] Classifications = ["MAIN", "SECO", "ANCI"];

    private readonly Dictionary<string, string> pool = new(StringComparer.Ordinal);
    private readonly Dictionary<long, EnterpriseRecord> enterprises = new(2_100_000);
    private readonly Dictionary<long, List<UnitRecord>> units = new(1_200_000);          // enterprise -> establishments/branches
    private readonly Dictionary<long, EntityData> entities = new(4_000_000);             // any entity -> names, addresses, ...
    private readonly HashSet<long> ownedUnits = new(1_800_000);
    private readonly List<string> naceKeys = [];                                         // "2008/62010"
    private readonly Dictionary<string, int> naceIds = new(StringComparer.Ordinal);

    public CodeBook Codes { get; } = new();
    public List<(string Category, string Code, string Language, string Description)> CodeRows { get; } = [];
    public IReadOnlyDictionary<string, string> Meta { get; private set; } = new Dictionary<string, string>();
    public Dictionary<string, long> Counts { get; } = new();

    public int ExtractNumber => int.Parse(Meta["ExtractNumber"]);
    public string SnapshotDate => IsoDate(Meta["SnapshotDate"])!;

    public static ExtractModel Load(string zipPath, Action<string> log)
    {
        using var extract = new Extract(zipPath);
        var model = new ExtractModel();
        model.Load(extract, log);
        return model;
    }

    private void Load(Extract extract, Action<string> log)
    {
        Meta = extract.Read<MetaRow>("meta.csv").ToDictionary(r => r.Variable, r => r.Value);
        foreach (var r in extract.Read<CodeRow>("code.csv"))
        {
            CodeRows.Add((I(r.Category), I(r.Code), I(r.Language), r.Description));
            Codes.Add(I(r.Category), I(r.Code), I(r.Language), r.Description);
        }
        log($"meta: extract {Meta.GetValueOrDefault("ExtractNumber")}, snapshot {Meta.GetValueOrDefault("SnapshotDate")}; {Codes.Count:N0} codes");

        foreach (var r in extract.Read<EnterpriseRow>("enterprise.csv"))
            enterprises[EntityNumber.Parse(r.EnterpriseNumber)] = new EnterpriseRecord(
                N(r.Status), N(r.JuridicalSituation), N(r.TypeOfEnterprise), N(r.JuridicalForm), N(r.JuridicalFormCAC), IsoDate(r.StartDate));
        Count("enterprises", enterprises.Count);

        foreach (var r in extract.Read<EstablishmentRow>("establishment.csv"))
            AddUnit(EntityNumber.Parse(r.EstablishmentNumber), EntityNumber.Parse(r.EnterpriseNumber), isBranch: false, IsoDate(r.StartDate), "establishments");
        foreach (var r in extract.Read<BranchRow>("branch.csv"))
            AddUnit(EntityNumber.Parse(r.Id), EntityNumber.Parse(r.EnterpriseNumber), isBranch: true, IsoDate(r.StartDate), "branches");
        log($"enterprises {Counts["enterprises"]:N0}, establishments {Counts["establishments"]:N0}, branches {Counts["branches"]:N0}");

        foreach (var r in extract.Read<DenominationRow>("denomination.csv"))
        {
            Count("denominations");
            if (string.IsNullOrEmpty(r.Denomination)) continue;
            (Entity(r.EntityNumber).Names ??= []).Add(new NameEntry(I(r.TypeOfDenomination), N(r.Language), r.Denomination));
        }
        log($"denominations {Counts["denominations"]:N0}");

        foreach (var r in extract.Read<AddressRow>("address.csv"))
        {
            Count("addresses");
            (Entity(r.EntityNumber).Addresses ??= []).Add(new AddressEntry
            {
                Type = I(r.TypeOfAddress),
                Zip = N(r.Zipcode),
                City = LocalizedText.Create(N(r.MunicipalityNL), N(r.MunicipalityFR)),
                Street = LocalizedText.Create(N(r.StreetNL), N(r.StreetFR)),
                Number = N(r.HouseNumber),
                Box = N(r.Box),
                Extra = N(r.ExtraAddressInfo),
                Country = LocalizedText.Create(N(r.CountryNL), N(r.CountryFR)),
                StrikingOff = IsoDate(r.DateStrikingOff),
            });
        }
        log($"addresses {Counts["addresses"]:N0}");

        foreach (var r in extract.Read<ContactRow>("contact.csv"))
        {
            Count("contacts");
            // code.csv has no FAX contact type; skipped by decision (specs/azure-architecture.md Q5)
            if (r.ContactType == "FAX") { Count("contactsFaxSkipped"); continue; }
            (Entity(r.EntityNumber).Contacts ??= []).Add(new ContactEntry(I(r.ContactType), r.Value));
        }
        log($"contacts {Counts["contacts"]:N0} (FAX skipped: {Counts.GetValueOrDefault("contactsFaxSkipped"):N0})");

        foreach (var r in extract.Read<ActivityRow>("activity.csv"))
        {
            Count("activities");
            var key = r.NaceVersion + "/" + r.NaceCode;
            if (!naceIds.TryGetValue(key, out var id)) { naceIds[key] = id = naceKeys.Count; naceKeys.Add(key); }
            var classification = (byte)Math.Max(0, Array.IndexOf(Classifications, r.Classification));
            (Entity(r.EntityNumber).Activities ??= []).Add(
                new ActivityRecord(byte.Parse(r.ActivityGroup), classification, short.Parse(r.NaceVersion), id));
            if (Counts["activities"] % 10_000_000 == 0) log($"  activities {Counts["activities"]:N0}");
        }
        log($"activities {Counts["activities"]:N0} ({naceKeys.Count:N0} distinct NACE codes)");

        var orphans = entities.Keys.Count(n => !enterprises.ContainsKey(n) && !ownedUnits.Contains(n));
        Counts["orphanEntities"] = orphans;
        pool.Clear();
        pool.TrimExcess();
    }

    public IEnumerable<BuiltCompany> Companies()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var streets = new HashSet<string>(StringComparer.Ordinal);
        var cities = new HashSet<string>(StringComparer.Ordinal);
        var zips = new HashSet<string>(StringComparer.Ordinal);
        var nace = new HashSet<int>();
        var activityText = new HashSet<string>(StringComparer.Ordinal);

        foreach (var number in enterprises.Keys.Order())
        {
            var e = enterprises[number];
            var self = entities.GetValueOrDefault(number);
            var myUnits = units.GetValueOrDefault(number) ?? [];

            var document = new CompanyDocument
            {
                Number = EntityNumber.Format(number),
                Status = e.Status,
                StartDate = e.StartDate,
                TypeOfEnterprise = e.Type,
                JuridicalSituation = e.Situation,
                JuridicalForm = e.Form,
                JuridicalFormCac = e.FormCac,
                Names = self?.Names,
                Addresses = self?.Addresses,
                Contacts = self?.Contacts,
                Activities = Newest(self?.Activities),
                Establishments = NullIfEmpty(myUnits.Where(u => !u.IsBranch).Select(Unit).ToList()),
                Branches = NullIfEmpty(myUnits.Where(u => u.IsBranch).Select(Unit).ToList()),
            };

            names.Clear(); streets.Clear(); cities.Clear(); zips.Clear(); nace.Clear(); activityText.Clear();
            AddEntityText(self);
            foreach (var u in myUnits) AddEntityText(entities.GetValueOrDefault(u.Number));
            foreach (var id in nace)
            {
                var (version, code) = Split(naceKeys[id]);
                foreach (var d in Codes.AllDescriptions("Nace" + version, code, "NL", "FR")) activityText.Add(d);
            }

            var main = MainAddress(self, myUnits);
            var summary = new CompanySummary
            {
                Number = document.Number,
                Name = BestName(self?.Names) ?? myUnits.Select(u => BestName(entities.GetValueOrDefault(u.Number)?.Names)).FirstOrDefault(n => n is not null),
                Zip = main?.Zip,
                City = main?.City,
                JuridicalForm = e.Form,
                Status = e.Status,
                Establishments = myUnits.Count(u => !u.IsBranch),
            };

            yield return new BuiltCompany(number, document, summary, new SearchFields(
                string.Join(' ', names), string.Join(' ', streets), string.Join(' ', cities), string.Join(' ', zips), string.Join(' ', activityText)));
        }

        void AddEntityText(EntityData? data)
        {
            if (data is null) return;
            foreach (var n in data.Names ?? []) names.Add(n.Value);
            foreach (var a in data.Addresses ?? [])
            {
                if (a.StrikingOff is not null) continue; // struck-off addresses are kept in the document but not searchable
                AddText(streets, a.Street); AddText(cities, a.City);
                if (a.Zip is not null) zips.Add(a.Zip);
            }
            foreach (var a in data.Activities ?? []) nace.Add(a.Nace); // all NACE versions are searchable
        }

        UnitDocument Unit(UnitRecord u)
        {
            var data = entities.GetValueOrDefault(u.Number);
            return new UnitDocument
            {
                Number = EntityNumber.Format(u.Number),
                StartDate = u.StartDate,
                Names = data?.Names,
                Addresses = data?.Addresses,
                Contacts = data?.Contacts,
                Activities = Newest(data?.Activities),
            };
        }
    }

    /// <summary>Per (group, classification) only the codes of the newest NACE version present (Q3).</summary>
    private List<ActivityEntry>? Newest(List<ActivityRecord>? activities) =>
        activities is null ? null :
        (from a in activities
         group a by (a.Group, a.Classification) into g
         let newest = g.Max(a => a.Version)
         from a in g.Where(a => a.Version == newest).DistinctBy(a => a.Nace)
         select new ActivityEntry(a.Group.ToString("000"), naceKeys[a.Nace], Classifications[a.Classification])).ToList();

    private static AddressEntry? MainAddress(EntityData? self, List<UnitRecord> myUnits)
    {
        var current = (self?.Addresses ?? []).Where(a => a.StrikingOff is null).ToList();
        return current.FirstOrDefault(a => a.Type == "REGO") ?? current.FirstOrDefault()
            ?? myUnits.SelectMany(u => u.Data?.Addresses ?? []).FirstOrDefault(a => a.StrikingOff is null);
    }

    /// <summary>Legal name (001), preferring Dutch, then French, then any; otherwise the first name.</summary>
    private static string? BestName(List<NameEntry>? names) =>
        names is null ? null :
        (names.FirstOrDefault(n => n.Type == "001" && n.Lang == "2")
         ?? names.FirstOrDefault(n => n.Type == "001" && n.Lang == "1")
         ?? names.FirstOrDefault(n => n.Type == "001")
         ?? names.FirstOrDefault())?.Value;

    private void AddUnit(long unit, long enterprise, bool isBranch, string? startDate, string counter)
    {
        Count(counter);
        if (!enterprises.ContainsKey(enterprise)) return; // orphan unit: counted via orphanEntities if it has data
        ownedUnits.Add(unit);
        if (!units.TryGetValue(enterprise, out var list)) units[enterprise] = list = [];
        list.Add(new UnitRecord(unit, isBranch, startDate, this));
    }

    internal EntityData? DataOf(long number) => entities.GetValueOrDefault(number);

    private EntityData Entity(string number)
    {
        var n = EntityNumber.Parse(number);
        if (!entities.TryGetValue(n, out var data)) entities[n] = data = new EntityData();
        return data;
    }

    private void Count(string key, long by = 1) => Counts[key] = Counts.GetValueOrDefault(key) + by;
    private string I(string s) { if (pool.TryGetValue(s, out var v)) return v; pool[s] = s; return s; }
    private string? N(string? s) => string.IsNullOrEmpty(s) ? null : I(s);
    private static void AddText(HashSet<string> set, LocalizedText? text) { if (text?.Nl is { } nl) set.Add(nl); if (text?.Fr is { } fr) set.Add(fr); }
    private static (string Version, string Code) Split(string key) { var i = key.IndexOf('/'); return (key[..i], key[(i + 1)..]); }
    private static List<T>? NullIfEmpty<T>(List<T> list) => list.Count == 0 ? null : list;

    /// <summary>KBO dates are dd-MM-yyyy; documents use ISO yyyy-MM-dd.</summary>
    internal static string? IsoDate(string? s) =>
        string.IsNullOrEmpty(s) ? null : s.Length == 10 && s[2] == '-' && s[5] == '-' ? $"{s[6..]}-{s[3..5]}-{s[..2]}" : s;

    sealed record EnterpriseRecord(string? Status, string? Situation, string? Type, string? Form, string? FormCac, string? StartDate);

    sealed record UnitRecord(long Number, bool IsBranch, string? StartDate, ExtractModel Model)
    {
        public EntityData? Data => Model.DataOf(Number);
    }

    internal sealed class EntityData
    {
        public List<NameEntry>? Names;
        public List<AddressEntry>? Addresses;
        public List<ContactEntry>? Contacts;
        public List<ActivityRecord>? Activities;
    }

    /// <summary>8 bytes per activity row; there are 34M of them.</summary>
    internal readonly record struct ActivityRecord(byte Group, byte Classification, short Version, int Nace);
}
