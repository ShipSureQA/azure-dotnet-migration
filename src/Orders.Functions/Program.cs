using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;
using Orders.Messaging;

var builder = FunctionsApplication.CreateBuilder(args);

// Blob (receipts) + Storage queue (notifications). Azurite locally, managed identity in Azure.
builder.Services.AddOrderReceiptProcessing();

builder.Build().Run();
