using System.Text.Json;

namespace Orders.Core;

public sealed record CreateOrderRequest(int CustomerId, IReadOnlyList<OrderLine> Lines);

public sealed record ParseResult<T>(T? Value, string? Error)
{
    public bool IsValid => Error is null;
    public static ParseResult<T> Ok(T value) => new(value, null);
    public static ParseResult<T> Fail(string error) => new(default, error);
}

/// <summary>
/// Port of the request validation in legacy-php/public/index.php.
/// Checks run in the same order and return the same messages as the legacy API.
/// One intentional difference: quantities must be JSON integers. The legacy API also
/// accepted numeric strings such as "2" (see docs/MIGRATION.md).
/// </summary>
public static class OrderRequestParser
{
    public const string InvalidJson = "Invalid JSON body";

    public static ParseResult<CreateOrderRequest> ParseCreateOrder(string body)
    {
        if (!TryParseObject(body, out var doc))
        {
            return ParseResult<CreateOrderRequest>.Fail(InvalidJson);
        }

        using (doc)
        {
            var root = doc!.RootElement;

            if (!root.TryGetProperty("customerId", out var customerIdElement)
                || customerIdElement.ValueKind != JsonValueKind.Number
                || !customerIdElement.TryGetInt32(out var customerId))
            {
                return ParseResult<CreateOrderRequest>.Fail("customerId is required");
            }

            if (!root.TryGetProperty("lines", out var linesElement)
                || linesElement.ValueKind != JsonValueKind.Array
                || linesElement.GetArrayLength() == 0)
            {
                return ParseResult<CreateOrderRequest>.Fail("Order must have at least one line");
            }

            var lines = new List<OrderLine>();
            var n = 0;
            foreach (var line in linesElement.EnumerateArray())
            {
                n++;
                if (line.ValueKind != JsonValueKind.Object
                    || !line.TryGetProperty("sku", out var skuElement)
                    || skuElement.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(skuElement.GetString()))
                {
                    return ParseResult<CreateOrderRequest>.Fail($"Line {n}: sku is required");
                }

                if (!line.TryGetProperty("quantity", out var qtyElement)
                    || qtyElement.ValueKind != JsonValueKind.Number
                    || !qtyElement.TryGetInt32(out var quantity)
                    || quantity <= 0)
                {
                    return ParseResult<CreateOrderRequest>.Fail($"Line {n}: quantity must be a positive integer");
                }

                if (!line.TryGetProperty("unitPrice", out var priceElement)
                    || priceElement.ValueKind != JsonValueKind.Number
                    || !priceElement.TryGetDecimal(out var unitPrice)
                    || unitPrice < 0)
                {
                    return ParseResult<CreateOrderRequest>.Fail($"Line {n}: unitPrice must be a non-negative number");
                }

                lines.Add(new OrderLine(skuElement.GetString()!, quantity, unitPrice));
            }

            return ParseResult<CreateOrderRequest>.Ok(new CreateOrderRequest(customerId, lines));
        }
    }

    public static ParseResult<string> ParseStatusChange(string body)
    {
        if (!TryParseObject(body, out var doc))
        {
            return ParseResult<string>.Fail(InvalidJson);
        }

        using (doc)
        {
            if (!doc!.RootElement.TryGetProperty("status", out var statusElement)
                || statusElement.ValueKind != JsonValueKind.String
                || !OrderStatus.IsValid(statusElement.GetString()))
            {
                return ParseResult<string>.Fail("Invalid status");
            }

            return ParseResult<string>.Ok(statusElement.GetString()!);
        }
    }

    private static bool TryParseObject(string body, out JsonDocument? doc)
    {
        doc = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return false;
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            doc.Dispose();
            doc = null;
            return false;
        }

        return true;
    }
}
