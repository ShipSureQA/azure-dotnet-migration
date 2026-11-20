using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orders.Messaging;

namespace Orders.UnitTests;

public class OrderCreatedHandlerTests
{
    private sealed class FakeReceiptStore : IReceiptStore
    {
        public List<OrderReceipt> Saved { get; } = [];

        public Task SaveAsync(OrderReceipt receipt, CancellationToken cancellationToken = default)
        {
            Saved.Add(receipt);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotificationQueue : INotificationQueue
    {
        public List<string> Messages { get; } = [];

        public Task EnqueueAsync(string message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private readonly FakeReceiptStore _receipts = new();
    private readonly FakeNotificationQueue _notifications = new();

    private OrderCreatedHandler CreateHandler() =>
        new(_receipts, _notifications, NullLogger<OrderCreatedHandler>.Instance);

    [Fact]
    public async Task Writes_a_receipt_and_queues_a_notification()
    {
        var evt = new OrderCreatedEvent(1004, 2, 50.63m, "2026-11-20T03:00:05Z");

        var receipt = await CreateHandler().HandleAsync(evt.ToJson());

        Assert.Equal("order-1004.json", receipt.BlobName);
        Assert.Equal(receipt, Assert.Single(_receipts.Saved));
        using var notification = JsonDocument.Parse(Assert.Single(_notifications.Messages));
        Assert.Equal("order-confirmation", notification.RootElement.GetProperty("type").GetString());
        Assert.Equal(1004, notification.RootElement.GetProperty("orderId").GetInt32());
    }

    [Fact]
    public async Task Redelivered_message_targets_the_same_blob()
    {
        var body = new OrderCreatedEvent(1005, 1, 10m, "2026-11-20T03:00:05Z").ToJson();
        var handler = CreateHandler();

        await handler.HandleAsync(body);
        await handler.HandleAsync(body);

        Assert.Single(_receipts.Saved.Select(r => r.BlobName).Distinct());
    }

    [Theory]
    [InlineData("""{"orderId":0,"customerId":1,"total":1,"createdAt":"x"}""")]
    [InlineData("""{"customerId":1}""")]
    [InlineData("null")]
    public async Task Invalid_messages_throw_so_service_bus_retries_then_dead_letters(string body)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateHandler().HandleAsync(body));
        Assert.Empty(_receipts.Saved);
    }

    [Fact]
    public async Task Malformed_json_throws()
    {
        await Assert.ThrowsAnyAsync<JsonException>(() => CreateHandler().HandleAsync("{oops"));
    }

    [Fact]
    public void Event_json_uses_camel_case_contract()
    {
        var json = new OrderCreatedEvent(7, 3, 1.5m, "2026-01-01T00:00:00Z").ToJson();

        Assert.Equal("""{"orderId":7,"customerId":3,"total":1.5,"createdAt":"2026-01-01T00:00:00Z"}""", json);
    }
}
