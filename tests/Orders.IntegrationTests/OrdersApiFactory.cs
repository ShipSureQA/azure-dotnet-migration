using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Orders.IntegrationTests;

/// <summary>In-process Orders API with the in-memory transport and test endpoints on.</summary>
public class OrdersApiFactory : WebApplicationFactory<Program>
{
    protected virtual Dictionary<string, string?> Settings => new()
    {
        ["EnableTestEndpoints"] = "true",
        ["Messaging:Transport"] = "InMemory",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
    }
}

public static class TestJson
{
    public const string ValidOrder =
        """{"customerId":2,"lines":[{"sku":"MS-777","quantity":2,"unitPrice":26.65}]}""";

    public static StringContent Body(string json) => new(json, System.Text.Encoding.UTF8, "application/json");
}
