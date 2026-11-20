using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues;

namespace Orders.Messaging;

public interface IOrderEventPublisher
{
    Task PublishOrderCreatedAsync(OrderCreatedEvent evt, CancellationToken cancellationToken = default);
}

/// <summary>Default for local runs and tests: keeps events in memory.</summary>
public sealed class InMemoryOrderEventPublisher : IOrderEventPublisher
{
    private readonly ConcurrentQueue<OrderCreatedEvent> _events = new();

    public IReadOnlyCollection<OrderCreatedEvent> Published => _events.ToArray();

    public Task PublishOrderCreatedAsync(OrderCreatedEvent evt, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(evt);
        return Task.CompletedTask;
    }
}

/// <summary>Azure Service Bus queue (real namespace or the Service Bus emulator).</summary>
public sealed class ServiceBusOrderEventPublisher(ServiceBusClient client, string queueName) : IOrderEventPublisher
{
    private readonly ServiceBusSender _sender = client.CreateSender(queueName);

    public string QueueName { get; } = queueName;

    public Task PublishOrderCreatedAsync(OrderCreatedEvent evt, CancellationToken cancellationToken = default)
    {
        var message = new ServiceBusMessage(evt.ToJson())
        {
            ContentType = "application/json",
            Subject = OrderCreatedEvent.Subject,
            // Stable id: duplicate detection on the queue drops accidental re-sends.
            MessageId = $"order-{evt.OrderId}",
        };
        return _sender.SendMessageAsync(message, cancellationToken);
    }
}

/// <summary>Azure Storage queue (real account or Azurite). Cheaper alternative transport.</summary>
public sealed class StorageQueueOrderEventPublisher(QueueClient queue) : IOrderEventPublisher
{
    public async Task PublishOrderCreatedAsync(OrderCreatedEvent evt, CancellationToken cancellationToken = default)
    {
        await queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await queue.SendMessageAsync(evt.ToJson(), cancellationToken);
    }
}
