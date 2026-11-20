using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orders.Messaging;

namespace Orders.Functions;

/// <summary>
/// Service Bus triggered function: consumes "order-created" messages published by the API.
/// All logic lives in <see cref="OrderCreatedHandler"/> (unit + Azurite integration tested).
/// A message that keeps failing is retried and then dead-lettered by Service Bus (MaxDeliveryCount).
/// </summary>
public sealed class ProcessOrderCreated(OrderCreatedHandler handler, ILogger<ProcessOrderCreated> logger)
{
    [Function(nameof(ProcessOrderCreated))]
    public async Task Run(
        [ServiceBusTrigger("order-created", Connection = "ServiceBusConnection")] string message,
        CancellationToken cancellationToken)
    {
        var receipt = await handler.HandleAsync(message, cancellationToken);
        logger.LogInformation("Processed OrderCreated for order {OrderId}", receipt.OrderId);
    }
}
