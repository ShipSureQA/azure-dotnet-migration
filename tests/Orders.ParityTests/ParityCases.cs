using System.Text.Json;

namespace Orders.ParityTests;

/// <summary>One request sent to both APIs. Defined in Contracts/parity-cases.json.</summary>
public sealed record ParityCase(
    string Name,
    string Method,
    string Path,
    string? Body,
    IReadOnlyList<string>? Volatile,
    IReadOnlyList<string>? Headers)
{
    public IReadOnlyList<string> VolatileFields => Volatile ?? [];

    public IReadOnlyList<string> ComparedHeaders => Headers ?? [];

    public override string ToString() => Name;
}

/// <summary>Response recorded from the legacy PHP API (tools/record_golden.py).</summary>
public sealed record GoldenResponse(int Status, Dictionary<string, string>? Headers, JsonElement? Body);

public static class ParityCases
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string ContractsDirectory => Path.Combine(AppContext.BaseDirectory, "Contracts");

    public static IReadOnlyList<ParityCase> All { get; } =
        JsonSerializer.Deserialize<List<ParityCase>>(
            File.ReadAllText(Path.Combine(ContractsDirectory, "parity-cases.json")), Options)!;

    public static ParityCase Get(string name) => All.Single(c => c.Name == name);

    public static string GoldenPath(string name) => Path.Combine(ContractsDirectory, "golden", name + ".json");

    public static GoldenResponse Golden(string name) =>
        JsonSerializer.Deserialize<GoldenResponse>(File.ReadAllText(GoldenPath(name)), Options)!;
}
