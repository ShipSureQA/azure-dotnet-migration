using System.Net;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging.Abstractions;
using Orders.Messaging;

namespace Orders.IntegrationTests;

/// <summary>
/// Runs against Azurite (the official Azure Storage emulator).
/// CI starts it as a service container; locally: docker compose up -d azurite.
/// Exclude with: dotnet test --filter "Category!=Azurite".
/// </summary>
[Trait("Category", "Azurite")]
public class AzuriteTests : IAsyncLifetime
{
    public static readonly string Connection =
        Environment.GetEnvironmentVariable("AZURITE_CONNECTION_STRING") ?? "UseDevelopmentStorage=true";

    private readonly BlobContainerClient _container = new(Connection, $"receipts-{Guid.NewGuid():N}");
    private readonly QueueClient _notifications = new(Connection, $"notify-{Guid.NewGuid():N}");
    private readonly QueueClient _orders = new(Connection, $"orders-{Guid.NewGuid():N}");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _container.DeleteIfExistsAsync();
        await _notifications.DeleteIfExistsAsync();
        await _orders.DeleteIfExistsAsync();
    }

    private OrderCreatedHandler Handler() =>
        new(new BlobReceiptStore(_container), new StorageQueueNotifier(_notifications), NullLogger<OrderCreatedHandler>.Instance);

    [Fact]
    public async Task Handler_writes_receipt_blob_and_notification_message()
    {
        var evt = new OrderCreatedEvent(1004, 2, 50.63m, "2026-11-20T03:00:05Z");

        await Handler().HandleAsync(evt.ToJson());

        var blob = await _container.GetBlobClient("order-1004.json").DownloadContentAsync();
        using var receipt = JsonDocument.Parse(blob.Value.Content.ToString());
        Assert.Equal(50.63m, receipt.RootElement.GetProperty("total").GetDecimal());

        var messages = (await _notifications.ReceiveMessagesAsync(maxMessages: 10)).Value;
        var message = Assert.Single(messages);
        Assert.Contains("order-confirmation", message.Body.ToString());
    }

    [Fact]
    public async Task Redelivery_overwrites_the_same_receipt()
    {
        var body = new OrderCreatedEvent(1010, 1, 10m, "2026-11-20T03:00:05Z").ToJson();

        await Handler().HandleAsync(body);
        // A second delivery must not fail on the existing blob (upload uses overwrite: true).
        await Handler().HandleAsync(body);

        Assert.True((await _container.GetBlobClient("order-1010.json").ExistsAsync()).Value);
        Assert.Equal(2, (await _notifications.ReceiveMessagesAsync(maxMessages: 10)).Value.Length);
    }

    [Fact]
    public async Task Storage_queue_publisher_round_trips_the_event()
    {
        var evt = new OrderCreatedEvent(2001, 3, 29m, "2026-11-20T03:00:05Z");

        await new StorageQueueOrderEventPublisher(_orders).PublishOrderCreatedAsync(evt);

        var message = Assert.Single((await _orders.ReceiveMessagesAsync(maxMessages: 10)).Value);
        Assert.Equal(evt, OrderCreatedEvent.FromJson(message.Body.ToString()));
    }

    [Fact]
    public async Task Api_to_queue_to_function_logic_end_to_end()
    {
        using var factory = new StorageQueueApiFactory(_orders.Name);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/orders", TestJson.Body(TestJson.ValidOrder));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var message = Assert.Single((await _orders.ReceiveMessagesAsync(maxMessages: 10)).Value);
        var receipt = await Handler().HandleAsync(message.Body.ToString());

        Assert.True((await _container.GetBlobClient(receipt.BlobName).ExistsAsync()).Value);
        Assert.Equal(50.63m, receipt.Total);
    }

    private sealed class StorageQueueApiFactory(string queueName) : OrdersApiFactory
    {
        protected override Dictionary<string, string?> Settings => new()
        {
            ["Messaging:Transport"] = "StorageQueue",
            ["Messaging:Storage:ConnectionString"] = Connection,
            ["Messaging:Storage:OrderQueueName"] = queueName,
        };
    }
}
