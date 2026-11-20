using Orders.Core;
using Orders.Messaging;

namespace Orders.Api;

/// <summary>
/// Port of legacy-php/public/index.php. Routes, status codes and JSON bodies are identical;
/// the parity suite (tests/Orders.ParityTests) proves it.
/// </summary>
public static class OrdersEndpoints
{
    public static WebApplication MapOrdersEndpoints(this WebApplication app, bool enableTestEndpoints)
    {
        app.MapGet("/health", () => Results.Json(new { status = "ok" }));

        if (enableTestEndpoints)
        {
            // Test hook used by the parity suite. Off unless EnableTestEndpoints=true.
            app.MapPost("/api/test/reset", (OrderStore store) =>
            {
                store.Reset();
                return Results.NoContent();
            });
        }

        app.MapGet("/api/customers", (OrderStore store) => Results.Json(store.Customers));

        app.MapGet("/api/customers/{id:int}", (int id, OrderStore store) =>
            store.FindCustomer(id) is { } customer
                ? Results.Json(customer)
                : Error(StatusCodes.Status404NotFound, "Customer not found"));

        app.MapGet("/api/orders", (HttpRequest request, OrderStore store) =>
        {
            IEnumerable<Order> orders = store.Orders;

            if (request.Query.TryGetValue("customerId", out var customerIdValues))
            {
                var raw = customerIdValues.ToString();
                if (raw.Length == 0 || !raw.All(char.IsAsciiDigit))
                {
                    return Error(StatusCodes.Status400BadRequest, "customerId must be an integer");
                }

                var customerId = int.TryParse(raw, out var parsed) ? parsed : -1;
                orders = orders.Where(o => o.CustomerId == customerId);
            }

            if (request.Query.TryGetValue("status", out var statusValues))
            {
                var status = statusValues.ToString();
                if (!OrderStatus.IsValid(status))
                {
                    return Error(StatusCodes.Status400BadRequest, "Invalid status");
                }

                orders = orders.Where(o => o.Status == status);
            }

            return Results.Json(orders.OrderBy(o => o.Id).ToList());
        });

        app.MapGet("/api/orders/{id:int}", (int id, OrderStore store) =>
            store.FindOrder(id) is { } order
                ? Results.Json(order)
                : Error(StatusCodes.Status404NotFound, "Order not found"));

        app.MapPost("/api/orders", CreateOrderAsync);

        app.MapPatch("/api/orders/{id:int}/status", async (int id, HttpRequest request, OrderStore store) =>
        {
            if (store.FindOrder(id) is null)
            {
                return Error(StatusCodes.Status404NotFound, "Order not found");
            }

            var parsed = OrderRequestParser.ParseStatusChange(await ReadBodyAsync(request));
            if (!parsed.IsValid)
            {
                return Error(StatusCodes.Status400BadRequest, parsed.Error!);
            }

            var result = store.ChangeStatus(id, parsed.Value!);
            if (result.NotFound)
            {
                return Error(StatusCodes.Status404NotFound, result.Error!);
            }

            return result.Order is { } updated
                ? Results.Json(updated)
                : Error(StatusCodes.Status409Conflict, result.Error!);
        });

        // Anything else: same JSON 404 as the legacy router.
        app.MapFallback(() => Error(StatusCodes.Status404NotFound, "Not found"));

        return app;
    }

    private static async Task<IResult> CreateOrderAsync(
        HttpRequest request,
        OrderStore store,
        IOrderEventPublisher publisher,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var parsed = OrderRequestParser.ParseCreateOrder(await ReadBodyAsync(request));
        if (!parsed.IsValid)
        {
            return Error(StatusCodes.Status400BadRequest, parsed.Error!);
        }

        var customer = store.FindCustomer(parsed.Value!.CustomerId);
        if (customer is null)
        {
            return Error(StatusCodes.Status422UnprocessableEntity, "Unknown customer");
        }

        using var activity = OrdersTelemetry.ActivitySource.StartActivity("orders.create");
        var order = store.Create(customer, parsed.Value.Lines, clock.GetUtcNow());
        activity?.SetTag("order.id", order.Id);
        activity?.SetTag("customer.tier", customer.Tier);
        OrdersTelemetry.OrdersCreated.Add(1, new KeyValuePair<string, object?>("customer.tier", customer.Tier));

        var logger = loggerFactory.CreateLogger("Orders.Api.Orders");
        logger.LogInformation("Order {OrderId} created for customer {CustomerId}, total {Total}", order.Id, order.CustomerId, order.Total);

        try
        {
            await publisher.PublishOrderCreatedAsync(
                new OrderCreatedEvent(order.Id, order.CustomerId, order.Total, order.CreatedAt),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // The order is already stored. A production system would use an outbox here;
            // the demo logs the failure (visible in Application Insights) and still returns 201.
            logger.LogError(ex, "Publishing OrderCreated for order {OrderId} failed", order.Id);
        }

        return Results.Created($"/api/orders/{order.Id}", order);
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        return await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
    }

    private static IResult Error(int statusCode, string message) =>
        Results.Json(new ErrorBody(message), statusCode: statusCode);
}
