# Azure services: local emulators and the real Azure path

Everything here runs **without an Azure subscription**. Each service has a local stand-in for development and CI,
and a documented path to the real service. The Bicep template in [`infra/main.bicep`](../infra/main.bicep)
describes the Azure resources; it is compiled in CI but **has not been deployed**.

| Service | Used for | Locally / in CI | Code | Tests |
|---|---|---|---|---|
| Azure Service Bus | `order-created` queue (API to Function) | Official **Service Bus emulator** (Docker, needs SQL Server); in-memory transport by default | `ServiceBusOrderEventPublisher` | `ServiceBusEmulatorTests` (best-effort CI job), `MessagingRegistrationTests` |
| Azure Functions (isolated worker, .NET 10) | Processes `OrderCreated`: receipt blob + notification message | Logic in `OrderCreatedHandler`, run directly by tests; `func start` optional | `src/Orders.Functions`, `OrderCreatedHandler` | `OrderCreatedHandlerTests`, `AzuriteTests`, `ServiceBusEmulatorTests` |
| Storage Queues + Blob | Notifications queue, receipts container, alternative order transport | **Azurite** (Docker) | `StorageQueueNotifier`, `BlobReceiptStore`, `StorageQueueOrderEventPublisher` | `AzuriteTests` (main CI job) |
| Key Vault | Secrets as configuration | Not needed: same keys from appsettings / env vars; vault mapping tested with the real `KeyVaultSecretManager` types | `KeyVaultConfiguration`, `PrefixKeyVaultSecretManager` | `KeyVaultConfigurationTests` |
| Application Insights | Traces, metrics, logs via OpenTelemetry | Telemetry collected, exported only if a connection string is set | `Program.cs`, `OrdersTelemetry` | `ApiTests.Order_creation_emits_an_open_telemetry_activity` |
| API Management | Front door: JWT validation, rate limiting, header policies | Policy XML validated by tests (APIM has no local emulator) | `infra/apim/orders-api-policy.xml` | `ApimPolicyTests`, CI XML check |

## Service Bus

```bash
ACCEPT_EULA=Y MSSQL_SA_PASSWORD='Choose-A-Strong-Pa55word' docker compose --profile servicebus up -d
curl -s http://localhost:5300/health          # emulator ready?
dotnet test tests/Orders.IntegrationTests -f net10.0 --filter "Category=ServiceBusEmulator"
```
`ACCEPT_EULA=Y` accepts the licence terms of the Service Bus emulator and SQL Server images.
Queue definitions live in [`infra/servicebus-emulator/Config.json`](../infra/servicebus-emulator/Config.json).

Run the API against the emulator with the `servicebus-emulator` launch profile
(`dotnet run --project src/Orders.Api -f net10.0 --launch-profile servicebus-emulator`).

The emulator is heavier than Azurite (it needs SQL Server and about a minute to start). CI therefore runs it in a
separate **best-effort** job (`continue-on-error`). If the emulator is unavailable the main pipeline still proves
the same code path with Azurite and the in-memory transport, because the API depends only on `IOrderEventPublisher`.

**In Azure:** set `Messaging__Transport=ServiceBus` and `Messaging__ServiceBus__FullyQualifiedNamespace`.
The client uses `DefaultAzureCredential` (managed identity). The namespace has local (SAS) auth disabled; the API
gets *Azure Service Bus Data Sender* and the Function *Data Receiver* on the queue only. Duplicate detection is on
and messages use `MessageId = order-<id>`, so a re-send is dropped. After 5 failed deliveries a message goes to the
dead-letter queue.

## Azure Functions

`ProcessOrderCreated` is a Service Bus triggered function on the isolated worker model (.NET 10). It is a thin
adapter; `OrderCreatedHandler` holds the logic and is idempotent (a redelivered message overwrites the same blob).

Local run (optional, needs [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local)):
```bash
docker compose --profile servicebus up -d       # Azurite + Service Bus emulator
cd src/Orders.Functions && func start           # uses local.settings.json (emulator values only, no secrets)
```
**In Azure:** Flex Consumption plan (`FC1`), runtime `dotnet-isolated 10.0`, identity-based connections
(`AzureWebJobsStorage__accountName`, `ServiceBusConnection__fullyQualifiedNamespace`). .NET 10 apps cannot run on
the Linux Consumption plan, which is why the template uses Flex Consumption.

**Build SDK:** the project uses the established `Microsoft.Azure.Functions.Worker.Sdk` package (2.1.0) with
`Microsoft.Azure.Functions.Worker` 2.52.0 (.NET 10 needs 2.50.0 or later). Microsoft now recommends the newer
`Azure.Functions.Sdk` project SDK (1.0.0+) for .NET 10. Migrating means: `<Project Sdk="Azure.Functions.Sdk/1.0.0">`,
remove the `Worker.Sdk` package reference and the `OutputType` / `AzureFunctionsVersion` properties. That is a
planned follow-up and does not change any function code.

## Storage (Azurite)

```bash
docker compose up -d azurite
dotnet test tests/Orders.IntegrationTests -f net10.0 --filter "Category=Azurite"
```
`UseDevelopmentStorage=true` points the SDK at Azurite. Azurite runs with `--skipApiVersionCheck` because current
Storage SDKs can send a newer service API version than the emulator knows.
**In Azure:** set `Messaging__Storage__BlobServiceUri` / `QueueServiceUri`; clients use `DefaultAzureCredential`.
The storage account has shared-key access disabled.

## Key Vault

The application only reads `IConfiguration`. When `KeyVault:Uri` is set, `AddOrdersKeyVault` adds Key Vault as a
configuration source using `DefaultAzureCredential`:

- **In Azure:** the App Service managed identity (role *Key Vault Secrets User*).
- **On a developer machine:** `az login` (or Visual Studio / VS Code sign-in) and the same role on a dev vault.
- **In tests and CI:** no vault. `KeyVaultConfigurationTests` use the real `KeyVaultSecretManager` mapping and an
  in-memory provider standing in for the vault, so the "secret name to configuration key to behaviour" chain is
  tested without network access.

Secret naming: `OrdersApi--Messaging--ServiceBus--ConnectionString` becomes `Messaging:ServiceBus:ConnectionString`.
Secrets without the `OrdersApi--` prefix are ignored, so one vault can serve several apps.

## Application Insights

OpenTelemetry tracing (`ActivitySource "Orders.Api"`, span `orders.create` with `order.id` and `customer.tier`),
a metric (`orders.created` counter) and structured `ILogger` logs are always produced. They are exported to
Application Insights only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set (`UseAzureMonitor`), so no key is
needed locally or in CI. An integration test listens to the activity source to prove the span is emitted.

## API Management

[`infra/apim/orders-api-policy.xml`](../infra/apim/orders-api-policy.xml) shows:
- `rate-limit-by-key`: 100 calls per 60 s per subscription key (IP fallback), server errors not counted;
- `validate-jwt`: Entra ID OpenID configuration, audience `api://orders-api`, required `roles` claim;
- `set-header`: correlation id in, caller-supplied identity header removed, `X-Powered-By` / `Server` removed out.

The tenant id is the APIM named value `{{entra-tenant-id}}`, created by the Bicep template. APIM has no local
emulator, so `ApimPolicyTests` check that the XML is well formed, the four sections exist in order and call
`<base />`, and the guard rails above are present.

## Deploying (not done for this demo)

```bash
az group create -n rg-orders-demo -l westeurope
az deployment group what-if -g rg-orders-demo -f infra/main.bicep -p apimPublisherEmail=you@example.com
az deployment group create  -g rg-orders-demo -f infra/main.bicep -p apimPublisherEmail=you@example.com
```
Resources: Log Analytics + Application Insights, Storage (receipts container, notifications queue, Functions
deployment container), Service Bus Standard with the `order-created` queue, Key Vault (RBAC), App Service B1 for
the API, Functions Flex Consumption, API Management Consumption, and least-privilege role assignments.
These resources cost money once deployed; delete the resource group afterwards.
