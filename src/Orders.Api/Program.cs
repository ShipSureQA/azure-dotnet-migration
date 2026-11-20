using Azure.Monitor.OpenTelemetry.AspNetCore;
using Orders.Api;
using Orders.Core;
using Orders.Messaging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Key Vault: only when KeyVault:Uri is configured (Azure). Locally and in CI the same
// settings come from appsettings / environment variables. See docs/AZURE.md.
builder.Configuration.AddOrdersKeyVault(builder.Configuration);

// Hardening: the legacy app leaked "X-Powered-By: PHP/8.x"; the port sends no server fingerprint.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => new OrderStore(SeedData.LoadEmbedded()));
builder.Services.AddOrdersMessaging();

// Application Insights through OpenTelemetry. Traces and metrics are always collected;
// they are exported only when a connection string is present, so no key is needed locally.
var otel = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(OrdersTelemetry.SourceName))
    .WithMetrics(metrics => metrics.AddMeter(OrdersTelemetry.SourceName));
var appInsights = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsights))
{
    otel.UseAzureMonitor(options => options.ConnectionString = appInsights);
}

var app = builder.Build();

app.MapOrdersEndpoints(app.Configuration.GetValue<bool>("EnableTestEndpoints"));

app.Run();

// Lets WebApplicationFactory<Program> reach the entry point (.NET 10 also generates this).
public partial class Program
{
}
