using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orders.Api;
using Orders.Messaging;

namespace Orders.IntegrationTests;

public class ApiTests(OrdersApiFactory factory) : IClassFixture<OrdersApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task ResetAsync() =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/api/test/reset", null)).StatusCode);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task Health_endpoint_reports_ok()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", (await JsonAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Customer_is_returned_with_the_legacy_shape()
    {
        var json = await JsonAsync(await _client.GetAsync("/api/customers/1"));

        Assert.Equal(
            new[] { "id", "name", "email", "tier" },
            json.EnumerateObject().Select(p => p.Name));
        Assert.Equal("gold", json.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task Creating_an_order_returns_201_with_location_and_publishes_order_created()
    {
        await ResetAsync();

        var response = await _client.PostAsync("/api/orders", TestJson.Body(TestJson.ValidOrder));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await JsonAsync(response);
        var id = order.GetProperty("id").GetInt32();
        Assert.Equal($"/api/orders/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal(50.63m, order.GetProperty("total").GetDecimal());

        var published = factory.Services.GetRequiredService<InMemoryOrderEventPublisher>().Published;
        Assert.Contains(published, e => e.OrderId == id && e.Total == 50.63m);
    }

    [Theory]
    [InlineData("/api/unknown")]
    [InlineData("/api/orders/abc")]
    public async Task Unknown_routes_return_the_legacy_json_404(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Not found", (await JsonAsync(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Validation_errors_return_400_with_error_body()
    {
        var response = await _client.PostAsync("/api/orders", TestJson.Body("""{"customerId":1,"lines":[]}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Order must have at least one line", (await JsonAsync(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Response_does_not_leak_a_server_fingerprint()
    {
        var response = await _client.GetAsync("/health");

        Assert.False(response.Headers.Contains("X-Powered-By"));
    }

    [Fact]
    public async Task Test_endpoints_are_disabled_outside_test_configuration()
    {
        // Added after the factory's settings, so this source wins.
        using var production = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["EnableTestEndpoints"] = "false" })));
        var client = production.CreateClient();

        var response = await client.PostAsync("/api/test/reset", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Order_creation_emits_an_open_telemetry_activity()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OrdersTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var response = await _client.PostAsync("/api/orders", TestJson.Body(TestJson.ValidOrder));
        var id = (await JsonAsync(response)).GetProperty("id").GetInt32();

        Assert.Contains(stopped, a =>
            a.OperationName == "orders.create" && a.GetTagItem("order.id")?.ToString() == id.ToString());
    }

    [Fact]
    public async Task A_messaging_outage_does_not_fail_the_order()
    {
        using var broken = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<IOrderEventPublisher, ThrowingPublisher>()));
        var client = broken.CreateClient();

        var response = await client.PostAsync("/api/orders", TestJson.Body(TestJson.ValidOrder));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private sealed class ThrowingPublisher : IOrderEventPublisher
    {
        public Task PublishOrderCreatedAsync(OrderCreatedEvent evt, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Service Bus unavailable");
    }
}
