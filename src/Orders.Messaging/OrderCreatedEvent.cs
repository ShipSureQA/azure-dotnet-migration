using System.Text.Json;

namespace Orders.Messaging;

/// <summary>Published by the API when an order is created; consumed by the Azure Function.</summary>
public sealed record OrderCreatedEvent(int OrderId, int CustomerId, decimal Total, string CreatedAt)
{
    public const string Subject = "OrderCreated";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static OrderCreatedEvent FromJson(string json)
    {
        var evt = JsonSerializer.Deserialize<OrderCreatedEvent>(json, JsonOptions)
            ?? throw new InvalidOperationException("Message body is empty.");
        if (evt.OrderId <= 0 || evt.CustomerId <= 0)
        {
            throw new InvalidOperationException("Message is missing orderId or customerId.");
        }

        return evt;
    }
}
