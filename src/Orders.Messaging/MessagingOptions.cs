namespace Orders.Messaging;

public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    /// <summary>InMemory (default), ServiceBus or StorageQueue.</summary>
    public string Transport { get; set; } = "InMemory";

    public ServiceBusSettings ServiceBus { get; set; } = new();

    public StorageSettings Storage { get; set; } = new();
}

public sealed class ServiceBusSettings
{
    /// <summary>Connection string (emulator / local). Leave empty in Azure and set FullyQualifiedNamespace.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>e.g. sb-orders-demo.servicebus.windows.net, used with DefaultAzureCredential.</summary>
    public string? FullyQualifiedNamespace { get; set; }

    public string QueueName { get; set; } = "order-created";
}

public sealed class StorageSettings
{
    /// <summary>"UseDevelopmentStorage=true" targets Azurite.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>e.g. https://storders.queue.core.windows.net, used with DefaultAzureCredential.</summary>
    public string? QueueServiceUri { get; set; }

    /// <summary>e.g. https://storders.blob.core.windows.net, used with DefaultAzureCredential.</summary>
    public string? BlobServiceUri { get; set; }

    public string OrderQueueName { get; set; } = "order-created";

    public string NotificationQueueName { get; set; } = "order-notifications";

    public string ReceiptsContainer { get; set; } = "receipts";
}
