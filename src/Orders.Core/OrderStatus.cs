namespace Orders.Core;

public static class OrderStatus
{
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Shipped = "shipped";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Pending, Paid, Shipped, Cancelled];

    private static readonly Dictionary<string, string[]> Transitions = new()
    {
        [Pending] = [Paid, Cancelled],
        [Paid] = [Shipped, Cancelled],
        [Shipped] = [],
        [Cancelled] = [],
    };

    public static bool IsValid(string? status) => status is not null && All.Contains(status);

    public static bool CanTransition(string from, string to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
}
