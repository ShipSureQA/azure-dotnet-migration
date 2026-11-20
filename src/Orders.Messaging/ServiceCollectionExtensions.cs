using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Orders.Messaging;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IOrderEventPublisher. The transport is chosen from configuration when the
    /// publisher is first resolved, so values from Key Vault or test overrides are honoured.
    /// </summary>
    public static IServiceCollection AddOrdersMessaging(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryOrderEventPublisher>();
        services.AddSingleton<IOrderEventPublisher>(sp =>
        {
            var options = GetOptions(sp.GetRequiredService<IConfiguration>());
            return options.Transport switch
            {
                "ServiceBus" => new ServiceBusOrderEventPublisher(CreateServiceBusClient(options.ServiceBus), options.ServiceBus.QueueName),
                "StorageQueue" => new StorageQueueOrderEventPublisher(CreateQueueClient(options.Storage, options.Storage.OrderQueueName)),
                "InMemory" => sp.GetRequiredService<InMemoryOrderEventPublisher>(),
                _ => throw new InvalidOperationException($"Unknown Messaging:Transport '{options.Transport}'."),
            };
        });
        return services;
    }

    /// <summary>Registers the receipt pipeline used by the Azure Function (Blob + Storage queue).</summary>
    public static IServiceCollection AddOrderReceiptProcessing(this IServiceCollection services)
    {
        services.AddSingleton<IReceiptStore>(sp =>
        {
            var storage = GetOptions(sp.GetRequiredService<IConfiguration>()).Storage;
            return new BlobReceiptStore(CreateBlobServiceClient(storage).GetBlobContainerClient(storage.ReceiptsContainer));
        });
        services.AddSingleton<INotificationQueue>(sp =>
        {
            var storage = GetOptions(sp.GetRequiredService<IConfiguration>()).Storage;
            return new StorageQueueNotifier(CreateQueueClient(storage, storage.NotificationQueueName));
        });
        services.AddSingleton(sp => new OrderCreatedHandler(
            sp.GetRequiredService<IReceiptStore>(),
            sp.GetRequiredService<INotificationQueue>(),
            sp.GetRequiredService<ILogger<OrderCreatedHandler>>()));
        return services;
    }

    public static MessagingOptions GetOptions(IConfiguration configuration) =>
        configuration.GetSection(MessagingOptions.SectionName).Get<MessagingOptions>() ?? new MessagingOptions();

    public static ServiceBusClient CreateServiceBusClient(ServiceBusSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            return new ServiceBusClient(settings.ConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(settings.FullyQualifiedNamespace))
        {
            // Managed identity in Azure, `az login` / Visual Studio locally. No secrets in config.
            return new ServiceBusClient(settings.FullyQualifiedNamespace, new DefaultAzureCredential());
        }

        throw new InvalidOperationException(
            "Set Messaging:ServiceBus:ConnectionString (emulator) or Messaging:ServiceBus:FullyQualifiedNamespace (Azure).");
    }

    public static QueueClient CreateQueueClient(StorageSettings settings, string queueName)
    {
        if (!string.IsNullOrWhiteSpace(settings.QueueServiceUri))
        {
            return new QueueServiceClient(new Uri(settings.QueueServiceUri), new DefaultAzureCredential())
                .GetQueueClient(queueName);
        }

        return new QueueClient(settings.ConnectionString ?? "UseDevelopmentStorage=true", queueName);
    }

    public static BlobServiceClient CreateBlobServiceClient(StorageSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BlobServiceUri))
        {
            return new BlobServiceClient(new Uri(settings.BlobServiceUri), new DefaultAzureCredential());
        }

        return new BlobServiceClient(settings.ConnectionString ?? "UseDevelopmentStorage=true");
    }
}
