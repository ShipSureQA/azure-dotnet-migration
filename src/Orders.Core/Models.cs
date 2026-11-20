namespace Orders.Core;

// Property order matches the legacy PHP JSON output, so responses diff cleanly.
public sealed record Customer(int Id, string Name, string Email, string Tier);

public sealed record OrderLine(string Sku, int Quantity, decimal UnitPrice);

public sealed record Order(
    int Id,
    int CustomerId,
    string Status,
    IReadOnlyList<OrderLine> Lines,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    string CreatedAt);

public sealed record ErrorBody(string Error);
