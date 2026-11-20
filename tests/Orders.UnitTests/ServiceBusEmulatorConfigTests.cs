using System.Text.Json;

namespace Orders.UnitTests;

public class ServiceBusEmulatorConfigTests
{
    [Fact]
    public void Emulator_config_declares_the_order_created_queue()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "infra", "servicebus-emulator", "Config.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var queues = doc.RootElement.GetProperty("UserConfig").GetProperty("Namespaces")[0].GetProperty("Queues");

        Assert.Contains(queues.EnumerateArray(), q => q.GetProperty("Name").GetString() == "order-created");
    }
}
