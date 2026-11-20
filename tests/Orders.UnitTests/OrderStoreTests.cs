using Orders.Core;

namespace Orders.UnitTests;

public class OrderStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 30, 5, TimeSpan.FromHours(5.5));

    private readonly OrderStore _store = new(SeedData.LoadEmbedded());

    [Fact]
    public void Seed_data_is_loaded()
    {
        Assert.Equal(3, _store.Customers.Count);
        Assert.Equal(new[] { 1001, 1002, 1003 }, _store.Orders.Select(o => o.Id));
    }

    [Fact]
    public void New_order_gets_next_id_pending_status_and_utc_timestamp()
    {
        var order = _store.Create(_store.FindCustomer(3)!, [new OrderLine("CB-100", 4, 7.25m)], Now);

        Assert.Equal(1004, order.Id);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(29.00m, order.Total);
        Assert.Equal("2026-11-20T03:00:05Z", order.CreatedAt);
    }

    [Theory]
    [InlineData(1002, "paid", null)]
    [InlineData(1002, "cancelled", null)]
    [InlineData(1001, "shipped", null)]
    [InlineData(1003, "paid", "Invalid status transition from shipped to paid")]
    [InlineData(1002, "shipped", "Invalid status transition from pending to shipped")]
    [InlineData(1002, "pending", "Invalid status transition from pending to pending")]
    public void Status_transitions_follow_the_legacy_state_machine(int id, string to, string? error)
    {
        var result = _store.ChangeStatus(id, to);

        Assert.Equal(error, result.Error);
        if (error is null)
        {
            Assert.Equal(to, result.Order!.Status);
            Assert.Equal(to, _store.FindOrder(id)!.Status);
        }
    }

    [Fact]
    public void Unknown_order_is_reported_as_not_found()
    {
        Assert.True(_store.ChangeStatus(4040, "paid").NotFound);
    }

    [Fact]
    public void Reset_restores_the_seed()
    {
        _store.Create(_store.FindCustomer(1)!, [new OrderLine("X", 1, 1m)], Now);
        _store.ChangeStatus(1002, "paid");

        _store.Reset();

        Assert.Equal(3, _store.Orders.Count);
        Assert.Equal("pending", _store.FindOrder(1002)!.Status);
    }
}
