using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orders.Messaging;

namespace Orders.UnitTests;

public class MessagingRegistrationTests
{
    private const string EmulatorConnection =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    private static IOrderEventPublisher Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddOrdersMessaging()
            .BuildServiceProvider();
        return services.GetRequiredService<IOrderEventPublisher>();
    }

    [Fact]
    public void Defaults_to_in_memory_transport()
    {
        Assert.IsType<InMemoryOrderEventPublisher>(Resolve(new()));
    }

    [Fact]
    public void Service_bus_transport_uses_configured_queue()
    {
        var publisher = Resolve(new()
        {
            ["Messaging:Transport"] = "ServiceBus",
            ["Messaging:ServiceBus:ConnectionString"] = EmulatorConnection,
            ["Messaging:ServiceBus:QueueName"] = "orders-test",
        });

        Assert.Equal("orders-test", Assert.IsType<ServiceBusOrderEventPublisher>(publisher).QueueName);
    }

    [Fact]
    public void Storage_queue_transport_can_be_selected()
    {
        var publisher = Resolve(new()
        {
            ["Messaging:Transport"] = "StorageQueue",
            ["Messaging:Storage:ConnectionString"] = "UseDevelopmentStorage=true",
        });

        Assert.IsType<StorageQueueOrderEventPublisher>(publisher);
    }

    [Fact]
    public void Service_bus_without_connection_or_namespace_fails_fast()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(new() { ["Messaging:Transport"] = "ServiceBus" }));
        Assert.Contains("FullyQualifiedNamespace", ex.Message);
    }

    [Fact]
    public void Unknown_transport_fails_fast()
    {
        Assert.Throws<InvalidOperationException>(() => Resolve(new() { ["Messaging:Transport"] = "Carrier pigeon" }));
    }

    [Fact]
    public void Receipt_pipeline_resolves_with_azurite_defaults()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddOrderReceiptProcessing()
            .BuildServiceProvider();

        Assert.NotNull(services.GetRequiredService<OrderCreatedHandler>());
        Assert.IsType<BlobReceiptStore>(services.GetRequiredService<IReceiptStore>());
        Assert.IsType<StorageQueueNotifier>(services.GetRequiredService<INotificationQueue>());
    }
}
