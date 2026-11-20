using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;

namespace Orders.Messaging;

public sealed record OrderReceipt(int OrderId, int CustomerId, decimal Total, string CreatedAt, string BlobName);

public interface IReceiptStore
{
    Task SaveAsync(OrderReceipt receipt, CancellationToken cancellationToken = default);
}

public interface INotificationQueue
{
    Task EnqueueAsync(string message, CancellationToken cancellationToken = default);
}

public sealed class BlobReceiptStore(BlobContainerClient container) : IReceiptStore
{
    public async Task SaveAsync(OrderReceipt receipt, CancellationToken cancellationToken = default)
    {
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var content = BinaryData.FromString(JsonSerializer.Serialize(receipt, OrderCreatedEvent.JsonOptions));
        await container.GetBlobClient(receipt.BlobName).UploadAsync(content, overwrite: true, cancellationToken: cancellationToken);
    }
}

public sealed class StorageQueueNotifier(QueueClient queue) : INotificationQueue
{
    public async Task EnqueueAsync(string message, CancellationToken cancellationToken = default)
    {
        await queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await queue.SendMessageAsync(message, cancellationToken);
    }
}

/// <summary>
/// Business logic of the Azure Function: turns an OrderCreated message into a receipt blob
/// and a customer-notification queue message. Kept outside the Functions project so it can
/// be unit tested with fakes and integration tested against Azurite.
/// Idempotent: a redelivered message overwrites the same blob.
/// </summary>
public sealed class OrderCreatedHandler(
    IReceiptStore receipts,
    INotificationQueue notifications,
    ILogger<OrderCreatedHandler> logger)
{
    public async Task<OrderReceipt> HandleAsync(string messageBody, CancellationToken cancellationToken = default)
    {
        var evt = OrderCreatedEvent.FromJson(messageBody);
        var receipt = new OrderReceipt(evt.OrderId, evt.CustomerId, evt.Total, evt.CreatedAt, $"order-{evt.OrderId}.json");

        await receipts.SaveAsync(receipt, cancellationToken);
        await notifications.EnqueueAsync(
            JsonSerializer.Serialize(new { type = "order-confirmation", evt.OrderId, evt.CustomerId }, OrderCreatedEvent.JsonOptions),
            cancellationToken);

        logger.LogInformation("Receipt {BlobName} written for order {OrderId}", receipt.BlobName, evt.OrderId);
        return receipt;
    }
}
