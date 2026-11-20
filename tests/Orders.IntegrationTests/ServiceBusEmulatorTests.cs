using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging.Abstractions;
using Orders.Messaging;

namespace Orders.IntegrationTests;

/// <summary>
/// Runs against the official Azure Service Bus emulator (plus Azurite).
/// Start them with: docker compose up -d azurite sqledge servicebus-emulator
/// Run with: dotnet test --filter "Category=ServiceBusEmulator".
/// The main CI job excludes this category; a separate best-effort CI job runs it.
/// </summary>
[Trait("Category", "ServiceBusEmulator")]
public class ServiceBusEmulatorTests
{
    private static readonly string Connection =
        Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION_STRING")
        ?? "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    [Fact]
    public async Task Order_created_message_flows_through_service_bus_to_the_function_logic()
    {
        await using var client = new ServiceBusClient(Connection);
        var evt = new OrderCreatedEvent(Random.Shared.Next(100_000, 999_999), 1, 89.10m, "2026-11-20T03:00:05Z");

        await new ServiceBusOrderEventPublisher(client, "order-created").PublishOrderCreatedAsync(evt);

        await using var receiver = client.CreateReceiver("order-created");
        ServiceBusReceivedMessage? received = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (received is null && DateTime.UtcNow < deadline)
        {
            var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
            if (message is null)
            {
                continue;
            }

            await receiver.CompleteMessageAsync(message);
            if (message.MessageId == $"order-{evt.OrderId}")
            {
                received = message;
            }
        }

        Assert.NotNull(received);
        Assert.Equal(OrderCreatedEvent.Subject, received!.Subject);
        Assert.Equal("application/json", received.ContentType);

        // Same code path the Azure Function runs.
        var container = new BlobContainerClient(AzuriteTests.Connection, $"receipts-{Guid.NewGuid():N}");
        var queue = new QueueClient(AzuriteTests.Connection, $"notify-{Guid.NewGuid():N}");
        try
        {
            var handler = new OrderCreatedHandler(new BlobReceiptStore(container), new StorageQueueNotifier(queue), NullLogger<OrderCreatedHandler>.Instance);
            var receipt = await handler.HandleAsync(received.Body.ToString());
            Assert.Equal(evt.OrderId, receipt.OrderId);
            Assert.True((await container.GetBlobClient(receipt.BlobName).ExistsAsync()).Value);
        }
        finally
        {
            await container.DeleteIfExistsAsync();
            await queue.DeleteIfExistsAsync();
        }
    }
}
