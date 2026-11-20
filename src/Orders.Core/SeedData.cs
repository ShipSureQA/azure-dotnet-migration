using System.Text.Json;

namespace Orders.Core;

public sealed record SeedData(IReadOnlyList<Customer> Customers, IReadOnlyList<Order> Orders)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static SeedData Load(Stream json) =>
        JsonSerializer.Deserialize<SeedData>(json, Options)
        ?? throw new InvalidOperationException("Seed data is empty.");

    /// <summary>Loads data/seed.json, embedded into this assembly at build time.</summary>
    public static SeedData LoadEmbedded()
    {
        using var stream = typeof(SeedData).Assembly.GetManifestResourceStream("Orders.Core.seed.json")
            ?? throw new InvalidOperationException("Embedded resource Orders.Core.seed.json not found.");
        return Load(stream);
    }
}
