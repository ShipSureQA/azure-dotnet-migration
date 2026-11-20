using System.Globalization;
using System.Text.Json;
using Reqnroll;

namespace Orders.ParityTests.Steps;

[Binding]
public sealed class ParitySteps(ParityContext context)
{
    private static readonly string[] NewOrderVolatileFields = ["createdAt"];

    [Given(@"^both Orders APIs are healthy$")]
    public async Task GivenBothApisAreHealthy()
    {
        var (legacy, dotnet) = await ParityApis.SendToBothAsync("GET", "/health");
        Assert.True(legacy.Status == 200, $"Legacy PHP API not healthy at {ParityApis.LegacyBaseUrl}");
        Assert.True(dotnet.Status == 200, $".NET API not healthy at {ParityApis.DotNetBaseUrl}");
    }

    [Given(@"^both APIs are reset to the seed data$")]
    public Task GivenBothApisAreReset() => ParityApis.ResetBothAsync();

    [When(@"^I send (GET|POST|PATCH) ""([^""]*)"" to both APIs$")]
    public Task WhenISend(string method, string path) => SendAsync(method, path, null, isNewOrder: false);

    [When(@"^I send (GET|POST|PATCH) ""([^""]*)"" to both APIs with body:$")]
    public Task WhenISendWithBody(string method, string path, string body) => SendAsync(method, path, body, isNewOrder: false);

    [When(@"^I create an order for customer (\d+) with (\d+) x ""([^""]+)"" at ([0-9.]+) on both APIs$")]
    public async Task WhenICreateAnOrder(int customerId, int quantity, string sku, string price)
    {
        // The price is inserted as a raw JSON number so both APIs receive exactly the same bytes.
        var body = $$"""{"customerId":{{customerId}},"lines":[{"sku":"{{sku}}","quantity":{{quantity}},"unitPrice":{{price}}}]}""";
        await SendAsync("POST", "/api/orders", body, isNewOrder: true);

        var legacyId = context.LegacyResponse.Json.GetProperty("id").GetInt32();
        var dotnetId = context.DotNetResponse.Json.GetProperty("id").GetInt32();
        Assert.Equal(legacyId, dotnetId);
        context.NewOrderId = legacyId;
    }

    [When(@"^I change the status of the new order to ""([^""]+)"" on both APIs$")]
    public async Task WhenIChangeTheStatus(string status)
    {
        await SendAsync("PATCH", $"/api/orders/{RequireNewOrderId()}/status", $$"""{"status":"{{status}}"}""", isNewOrder: true);
        Assert.Equal(200, context.LegacyResponse.Status);
        Assert.Equal(200, context.DotNetResponse.Status);
    }

    [When(@"^I fetch the new order from both APIs$")]
    public Task WhenIFetchTheNewOrder() => SendAsync("GET", $"/api/orders/{RequireNewOrderId()}", null, isNewOrder: true);

    [Then(@"^both APIs respond with status (\d+)$")]
    public void ThenBothRespondWith(int status)
    {
        Assert.Equal(status, context.LegacyResponse.Status);
        Assert.Equal(status, context.DotNetResponse.Status);
    }

    [Then(@"^both responses have identical JSON bodies$")]
    public void ThenBothBodiesAreIdentical()
    {
        Assert.Equal(
            ParityApis.NormalizeContentType(context.LegacyResponse.ContentType),
            ParityApis.NormalizeContentType(context.DotNetResponse.ContentType));

        IReadOnlyCollection<string> volatileFields = context.LastResponseIsNewOrder ? NewOrderVolatileFields : Array.Empty<string>();
        var legacy = JsonParity.NormalizeVolatile(context.LegacyResponse.Json, volatileFields);
        var dotnet = JsonParity.NormalizeVolatile(context.DotNetResponse.Json, volatileFields);

        Assert.Empty(JsonParity.Diff(legacy, dotnet));
    }

    [Then(@"^the ""([^""]+)"" field is ""([^""]*)"" on both APIs$")]
    public void ThenTheFieldIs(string field, string expected)
    {
        AssertField(context.LegacyResponse, field, expected);
        AssertField(context.DotNetResponse, field, expected);
    }

    [Then(@"^the legacy API responds with status (\d+)$")]
    public void ThenTheLegacyApiRespondsWith(int status) => Assert.Equal(status, context.LegacyResponse.Status);

    [Then(@"^the \.NET API responds with status (\d+) and error ""([^""]+)""$")]
    public void ThenTheDotNetApiRespondsWithError(int status, string error)
    {
        Assert.Equal(status, context.DotNetResponse.Status);
        Assert.Equal(error, context.DotNetResponse.Json.GetProperty("error").GetString());
    }

    [Then(@"^the legacy API sends the ""([^""]+)"" header$")]
    public void ThenTheLegacyApiSendsHeader(string header) => Assert.NotNull(context.LegacyResponse.Header(header));

    [Then(@"^the \.NET API does not send the ""([^""]+)"" header$")]
    public void ThenTheDotNetApiDoesNotSendHeader(string header) => Assert.Null(context.DotNetResponse.Header(header));

    private async Task SendAsync(string method, string path, string? body, bool isNewOrder)
    {
        var (legacy, dotnet) = await ParityApis.SendToBothAsync(method, path, body);
        context.Legacy = legacy;
        context.DotNet = dotnet;
        context.LastResponseIsNewOrder = isNewOrder;
    }

    private int RequireNewOrderId() =>
        context.NewOrderId ?? throw new InvalidOperationException("Create an order first.");

    private static void AssertField(ApiResponse response, string field, string expected)
    {
        var value = response.Json.GetProperty(field);
        if (value.ValueKind == JsonValueKind.Number)
        {
            Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), value.GetDecimal());
        }
        else
        {
            Assert.Equal(expected, value.GetString());
        }
    }
}
