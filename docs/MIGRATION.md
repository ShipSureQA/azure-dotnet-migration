# PHP to .NET migration: approach, parity testing and intentional differences

## Approach

1. **Characterise the legacy API first.** Before touching .NET, every request in
   [`parity-cases.json`](../tests/Orders.ParityTests/Contracts/parity-cases.json) was sent to the PHP API
   and its responses were saved as golden files (`tools/record_golden.py`). The golden files are the contract.
2. **Port one module at a time** to ASP.NET Core minimal APIs, keeping the same routes, status codes,
   error messages and validation order.
3. **Run the parity suite on every commit.** CI starts both APIs side by side (PHP on `:8080`, .NET on `:5080`)
   and sends identical requests to both:
   - **Golden/contract tests** (`GoldenParityTests`, 36 cases): legacy == .NET == golden file
     (status, `Content-Type`, `Location`, JSON body). Checking against the golden file as well catches a
     change that breaks both sides the same way.
   - **Reqnroll BDD scenarios** (`Features/MigrationParity.feature`): readable business flows (tier discounts,
     order lifecycle, invalid transitions) plus scenarios that pin the intentional differences below.
4. **Pin every intentional difference with a test**, so "different on purpose" never becomes "different by accident".

## Code mapping

| Legacy PHP | .NET 10 port |
|---|---|
| `legacy-php/public/index.php` (router, `preg_match` routes) | `src/Orders.Api/OrdersEndpoints.cs` (minimal API, `{id:int}` constraints, `MapFallback` for the JSON 404) |
| `legacy-php/src/Pricing.php` | `src/Orders.Core/Pricing.cs` |
| Inline validation in `index.php` | `src/Orders.Core/OrderRequestParser.cs` (same checks, same order, same messages) |
| `legacy-php/src/Store.php` (JSON file + `flock`) | `src/Orders.Core/OrderStore.cs` (in-memory, `lock`) |
| `data/seed.json` | same file, embedded into `Orders.Core` |
| none | `IOrderEventPublisher` sends an `OrderCreated` message to Service Bus, processed by an Azure Function |

## Defect found by the parity suite

**Rounding.** PHP `round()` rounds half away from zero. .NET `Math.Round(decimal, 2)` uses banker's rounding
(half to even) by default. For a silver customer with a 53.30 subtotal the 5 % discount is 2.665:

| | discount | total |
|---|---|---|
| PHP (legacy, correct for this business) | 2.67 | 50.63 |
| First .NET port (`Math.Round` default) | 2.66 | 50.64 |
| Fixed port (`MidpointRounding.AwayFromZero`) | 2.67 | 50.63 |

Covered by the `create-silver-midpoint-rounding` golden case, the "silver 5% (midpoint)" BDD example and
`PricingTests.Default_math_round_would_have_broken_parity`.

## Normalisations used when comparing (not behaviour differences)

| Topic | Legacy | .NET | How the suite compares |
|---|---|---|---|
| Number formatting | PHP floats: `53.3`, `6` | `decimal`: `53.30`, `6.00` | Numbers compared by value (`JsonParity`) |
| `createdAt` of a new order | `gmdate('Y-m-d\TH:i:s\Z')` | `yyyy-MM-dd'T'HH:mm:ss'Z'` | Format checked by regex, then replaced by a placeholder (the value contains "now") |
| `Content-Type` | `application/json; charset=utf-8` | `application/json; charset=utf-8` | Parsed and lower-cased before comparing |
| Property order | insertion order | declaration order | Ignored (same order today, but JSON objects are unordered) |

Money is `decimal` in .NET and `float` in PHP. For inputs with at most two decimals both produce the same
rounded values (the golden cases include a midpoint case); the port is the safer of the two.

## Intentional differences

| # | Behaviour | Legacy PHP | .NET 10 port | Why | Pinned by |
|---|---|---|---|---|---|
| 1 | `quantity` sent as a numeric string (`"2"`) or `2.0` | Accepted (loose typing) | `400 Line 1: quantity must be a positive integer` | Strict contract; the loose behaviour hid client bugs | BDD `@intentional-difference`, `OrderRequestParserTests` |
| 2 | Server fingerprint headers | `X-Powered-By: PHP/8.x` | No `X-Powered-By`, no `Server` (Kestrel `AddServerHeader = false`) | Security hardening; APIM also strips them | BDD `@intentional-difference`, `ApimPolicyTests` |
| 3 | Side effects of `POST /api/orders` | none | Publishes `OrderCreated` (Service Bus); a Function writes a receipt blob and a notification message | New capability, invisible in the HTTP contract | `ApiTests`, `AzuriteTests`, `ServiceBusEmulatorTests` |
| 4 | Persistence | JSON file in the temp directory | In-memory store (would be Azure SQL / Cosmos DB in production) | Demo runs with zero infrastructure | Both seeded from `data/seed.json`, reset via the test hook |

## Not part of the contract

- `POST /api/test/reset` exists only when `ENABLE_TEST_ENDPOINTS=true` (PHP) / `EnableTestEndpoints=true` (.NET).
  Both return the JSON 404 otherwise.
- Wrong HTTP method on a known path, ids larger than a 32-bit integer and duplicate query parameters are not
  covered by golden cases and may differ.

## Re-recording golden files

Only when the legacy behaviour is meant to change:

```bash
ENABLE_TEST_ENDPOINTS=true php -S 127.0.0.1:8080 legacy-php/public/index.php &
python3 tools/record_golden.py
git diff tests/Orders.ParityTests/Contracts/golden   # review every change
```
