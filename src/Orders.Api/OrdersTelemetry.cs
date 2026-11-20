using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Orders.Api;

public static class OrdersTelemetry
{
    public const string SourceName = "Orders.Api";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> OrdersCreated =
        Meter.CreateCounter<long>("orders.created", unit: "{order}", description: "Orders created");
}
