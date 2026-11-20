namespace Orders.ParityTests.Steps;

/// <summary>Per-scenario state, injected by Reqnroll's context injection.</summary>
public sealed class ParityContext
{
    public ApiResponse? Legacy { get; set; }

    public ApiResponse? DotNet { get; set; }

    public int? NewOrderId { get; set; }

    public bool LastResponseIsNewOrder { get; set; }

    public ApiResponse LegacyResponse => Legacy ?? throw new InvalidOperationException("No request sent yet.");

    public ApiResponse DotNetResponse => DotNet ?? throw new InvalidOperationException("No request sent yet.");
}
