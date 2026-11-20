namespace Orders.Core;

public sealed record StatusChangeResult(Order? Order, string? Error, bool NotFound)
{
    public static StatusChangeResult Changed(Order order) => new(order, null, false);
    public static StatusChangeResult Missing() => new(null, "Order not found", true);
    public static StatusChangeResult Rejected(string error) => new(null, error, false);
}

/// <summary>
/// Thread-safe in-memory store, seeded from data/seed.json (same data as the legacy API).
/// A real deployment would use Azure SQL or Cosmos DB; the store is kept in memory so the
/// demo runs with zero infrastructure.
/// </summary>
public sealed class OrderStore
{
    private readonly SeedData _seed;
    private readonly object _gate = new();
    private List<Customer> _customers = [];
    private List<Order> _orders = [];

    public OrderStore(SeedData seed)
    {
        _seed = seed;
        Reset();
    }

    public void Reset()
    {
        lock (_gate)
        {
            _customers = _seed.Customers.ToList();
            _orders = _seed.Orders.ToList();
        }
    }

    public IReadOnlyList<Customer> Customers
    {
        get { lock (_gate) { return _customers.ToList(); } }
    }

    public IReadOnlyList<Order> Orders
    {
        get { lock (_gate) { return _orders.OrderBy(o => o.Id).ToList(); } }
    }

    public Customer? FindCustomer(int id)
    {
        lock (_gate) { return _customers.FirstOrDefault(c => c.Id == id); }
    }

    public Order? FindOrder(int id)
    {
        lock (_gate) { return _orders.FirstOrDefault(o => o.Id == id); }
    }

    public Order Create(Customer customer, IReadOnlyList<OrderLine> lines, DateTimeOffset now)
    {
        var quote = Pricing.Quote(lines, customer.Tier);
        lock (_gate)
        {
            var nextId = (_orders.Count == 0 ? 1000 : _orders.Max(o => o.Id)) + 1;
            var order = new Order(
                nextId,
                customer.Id,
                OrderStatus.Pending,
                lines,
                quote.Subtotal,
                quote.Discount,
                quote.Total,
                FormatTimestamp(now));
            _orders.Add(order);
            return order;
        }
    }

    public StatusChangeResult ChangeStatus(int id, string newStatus)
    {
        lock (_gate)
        {
            var index = _orders.FindIndex(o => o.Id == id);
            if (index < 0)
            {
                return StatusChangeResult.Missing();
            }

            var current = _orders[index];
            if (!OrderStatus.CanTransition(current.Status, newStatus))
            {
                return StatusChangeResult.Rejected(
                    $"Invalid status transition from {current.Status} to {newStatus}");
            }

            var updated = current with { Status = newStatus };
            _orders[index] = updated;
            return StatusChangeResult.Changed(updated);
        }
    }

    /// <summary>Same format as PHP gmdate('Y-m-d\TH:i:s\Z').</summary>
    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
