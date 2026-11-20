using Orders.Core;

namespace Orders.UnitTests;

public class OrderRequestParserTests
{
    [Fact]
    public void Valid_request_is_parsed()
    {
        var result = OrderRequestParser.ParseCreateOrder(
            """{"customerId":2,"lines":[{"sku":"A-1","quantity":3,"unitPrice":4.25}]}""");

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Value!.CustomerId);
        Assert.Equal(new OrderLine("A-1", 3, 4.25m), Assert.Single(result.Value.Lines));
    }

    [Theory]
    [InlineData("", "Invalid JSON body")]
    [InlineData("{not json", "Invalid JSON body")]
    [InlineData("[]", "Invalid JSON body")]
    [InlineData("42", "Invalid JSON body")]
    [InlineData("""{"lines":[]}""", "customerId is required")]
    [InlineData("""{"customerId":"1","lines":[]}""", "customerId is required")]
    [InlineData("""{"customerId":1}""", "Order must have at least one line")]
    [InlineData("""{"customerId":1,"lines":[]}""", "Order must have at least one line")]
    [InlineData("""{"customerId":1,"lines":{}}""", "Order must have at least one line")]
    [InlineData("""{"customerId":1,"lines":[1]}""", "Line 1: sku is required")]
    [InlineData("""{"customerId":1,"lines":[{"sku":" ","quantity":1,"unitPrice":1}]}""", "Line 1: sku is required")]
    [InlineData("""{"customerId":1,"lines":[{"sku":"A","quantity":0,"unitPrice":1}]}""", "Line 1: quantity must be a positive integer")]
    [InlineData("""{"customerId":1,"lines":[{"sku":"A","quantity":1.5,"unitPrice":1}]}""", "Line 1: quantity must be a positive integer")]
    [InlineData("""{"customerId":1,"lines":[{"sku":"A","quantity":1,"unitPrice":-1}]}""", "Line 1: unitPrice must be a non-negative number")]
    [InlineData("""{"customerId":1,"lines":[{"sku":"A","quantity":1,"unitPrice":1},{"sku":"B","quantity":1}]}""", "Line 2: unitPrice must be a non-negative number")]
    public void Invalid_requests_return_the_legacy_error_message(string body, string expected)
    {
        var result = OrderRequestParser.ParseCreateOrder(body);

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public void Intentional_difference_numeric_string_quantity_is_rejected()
    {
        // The legacy PHP API accepted "2" (loose typing). The port is strict. See docs/MIGRATION.md.
        var result = OrderRequestParser.ParseCreateOrder(
            """{"customerId":1,"lines":[{"sku":"A","quantity":"2","unitPrice":1}]}""");

        Assert.Equal("Line 1: quantity must be a positive integer", result.Error);
    }

    [Theory]
    [InlineData("""{"status":"paid"}""", null)]
    [InlineData("""{"status":"lost"}""", "Invalid status")]
    [InlineData("""{"status":1}""", "Invalid status")]
    [InlineData("""{}""", "Invalid status")]
    [InlineData("nope", "Invalid JSON body")]
    public void Status_change_validation(string body, string? expectedError)
    {
        Assert.Equal(expectedError, OrderRequestParser.ParseStatusChange(body).Error);
    }
}
