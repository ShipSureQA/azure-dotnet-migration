Feature: Legacy PHP to .NET 10 migration parity
  The ASP.NET Core port must be a drop-in replacement for the legacy PHP Orders API.
  Every scenario sends the same requests to both running APIs and compares the answers.
  Intentional differences are listed in docs/MIGRATION.md and pinned by the last scenarios.

  Background:
    Given both Orders APIs are healthy
    And both APIs are reset to the seed data

  Scenario: Customer lookup returns identical JSON
    When I send GET "/api/customers/1" to both APIs
    Then both APIs respond with status 200
    And both responses have identical JSON bodies

  Scenario: Filtering orders by customer and status
    When I send GET "/api/orders?customerId=1&status=shipped" to both APIs
    Then both APIs respond with status 200
    And both responses have identical JSON bodies

  Scenario Outline: Tier discounts are calculated identically
    When I create an order for customer <customer> with <quantity> x "<sku>" at <price> on both APIs
    Then both APIs respond with status 201
    And both responses have identical JSON bodies
    And the "discount" field is "<discount>" on both APIs
    And the "total" field is "<total>" on both APIs

    Examples:
      | tier                 | customer | quantity | sku    | price | discount | total |
      | gold 10%             | 1        | 2        | KB-001 | 49.50 | 9.9      | 89.1  |
      | silver 5% (midpoint) | 2        | 2        | MS-777 | 26.65 | 2.67     | 50.63 |
      | standard             | 3        | 4        | CB-100 | 7.25  | 0        | 29    |

  Scenario: Full order lifecycle stays in sync
    When I create an order for customer 3 with 1 x "HD-010" at 120 on both APIs
    And I change the status of the new order to "paid" on both APIs
    And I change the status of the new order to "shipped" on both APIs
    And I fetch the new order from both APIs
    Then both APIs respond with status 200
    And both responses have identical JSON bodies
    And the "status" field is "shipped" on both APIs

  Scenario: An invalid status transition is rejected the same way
    When I send PATCH "/api/orders/1003/status" to both APIs with body:
      """
      {"status":"paid"}
      """
    Then both APIs respond with status 409
    And both responses have identical JSON bodies
    And the "error" field is "Invalid status transition from shipped to paid" on both APIs

  Scenario: Validation errors use the same status and message
    When I send POST "/api/orders" to both APIs with body:
      """
      {"customerId":1,"lines":[{"sku":"KB-001","quantity":0,"unitPrice":49.5}]}
      """
    Then both APIs respond with status 400
    And both responses have identical JSON bodies

  @intentional-difference
  Scenario: Intentional difference - quantity must be a JSON integer
    When I send POST "/api/orders" to both APIs with body:
      """
      {"customerId":2,"lines":[{"sku":"MS-777","quantity":"2","unitPrice":26.65}]}
      """
    Then the legacy API responds with status 201
    And the .NET API responds with status 400 and error "Line 1: quantity must be a positive integer"

  @intentional-difference
  Scenario: Intentional difference - no server fingerprint headers
    When I send GET "/health" to both APIs
    Then both APIs respond with status 200
    And the legacy API sends the "X-Powered-By" header
    And the .NET API does not send the "X-Powered-By" header
    And the .NET API does not send the "Server" header
