<?php
declare(strict_types=1);

/*
 * Legacy Orders API (plain PHP 8, no framework).
 * Run: php -S 127.0.0.1:8080 legacy-php/public/index.php
 *
 * This is the "before" side of the migration demo. Its behaviour is the
 * contract that the .NET 10 port is parity-tested against.
 */

require __DIR__ . '/../src/Pricing.php';
require __DIR__ . '/../src/Store.php';

const VALID_STATUSES = ['pending', 'paid', 'shipped', 'cancelled'];
const TRANSITIONS = [
    'pending' => ['paid', 'cancelled'],
    'paid' => ['shipped', 'cancelled'],
    'shipped' => [],
    'cancelled' => [],
];

function respond(int $status, mixed $body = null, array $headers = []): void
{
    http_response_code($status);
    foreach ($headers as $name => $value) {
        header("$name: $value");
    }
    if ($body !== null) {
        header('Content-Type: application/json; charset=utf-8');
        echo json_encode($body, JSON_UNESCAPED_SLASHES);
    }
}

function error(int $status, string $message): void
{
    respond($status, ['error' => $message]);
}

function readJsonBody(): mixed
{
    $raw = file_get_contents('php://input');
    if ($raw === false || trim($raw) === '') {
        return null;
    }
    try {
        // Objects stay stdClass so `{}` and `[]` can be told apart.
        return json_decode($raw, false, 512, JSON_THROW_ON_ERROR);
    } catch (JsonException) {
        return null;
    }
}

function findById(array $items, int $id): ?array
{
    foreach ($items as $item) {
        if ($item['id'] === $id) {
            return $item;
        }
    }
    return null;
}

/** Returns [lines, null] on success or [null, errorMessage]. */
function validateLines(mixed $lines): array
{
    if (!is_array($lines) || count($lines) === 0) {
        return [null, 'Order must have at least one line'];
    }
    $clean = [];
    foreach (array_values($lines) as $i => $line) {
        $n = $i + 1;
        if (!($line instanceof stdClass)) {
            return [null, "Line $n: sku is required"];
        }
        $sku = $line->sku ?? null;
        if (!is_string($sku) || trim($sku) === '') {
            return [null, "Line $n: sku is required"];
        }
        $qty = $line->quantity ?? null;
        // Legacy quirk: numeric strings such as "2" are accepted (loose typing).
        if (is_bool($qty) || !is_numeric($qty) || (int) $qty != $qty || (int) $qty <= 0) {
            return [null, "Line $n: quantity must be a positive integer"];
        }
        $price = $line->unitPrice ?? null;
        if (!(is_int($price) || is_float($price)) || $price < 0) {
            return [null, "Line $n: unitPrice must be a non-negative number"];
        }
        $clean[] = ['sku' => $sku, 'quantity' => (int) $qty, 'unitPrice' => $price];
    }
    return [$clean, null];
}

$store = new Store(
    getenv('ORDERS_DATA_FILE') ?: sys_get_temp_dir() . '/legacy-orders.json',
    __DIR__ . '/../../data/seed.json'
);

$method = $_SERVER['REQUEST_METHOD'];
$path = rtrim(parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH) ?: '/', '/') ?: '/';

// --- Routes -----------------------------------------------------------------

if ($method === 'GET' && $path === '/health') {
    respond(200, ['status' => 'ok']);
    return;
}

if ($method === 'POST' && $path === '/api/test/reset') {
    if (getenv('ENABLE_TEST_ENDPOINTS') !== 'true') {
        error(404, 'Not found');
        return;
    }
    $store->reset();
    respond(204);
    return;
}

if ($method === 'GET' && $path === '/api/customers') {
    respond(200, $store->read()['customers']);
    return;
}

if ($method === 'GET' && preg_match('#^/api/customers/(\d+)$#', $path, $m)) {
    $customer = findById($store->read()['customers'], (int) $m[1]);
    $customer === null ? error(404, 'Customer not found') : respond(200, $customer);
    return;
}

if ($method === 'GET' && $path === '/api/orders') {
    $orders = $store->read()['orders'];
    $customerId = $_GET['customerId'] ?? null;
    if ($customerId !== null) {
        if (!is_string($customerId) || !ctype_digit($customerId)) {
            error(400, 'customerId must be an integer');
            return;
        }
        $orders = array_filter($orders, fn ($o) => $o['customerId'] === (int) $customerId);
    }
    $status = $_GET['status'] ?? null;
    if ($status !== null) {
        if (!in_array($status, VALID_STATUSES, true)) {
            error(400, 'Invalid status');
            return;
        }
        $orders = array_filter($orders, fn ($o) => $o['status'] === $status);
    }
    usort($orders, fn ($a, $b) => $a['id'] <=> $b['id']);
    respond(200, array_values($orders));
    return;
}

if ($method === 'GET' && preg_match('#^/api/orders/(\d+)$#', $path, $m)) {
    $order = findById($store->read()['orders'], (int) $m[1]);
    $order === null ? error(404, 'Order not found') : respond(200, $order);
    return;
}

if ($method === 'POST' && $path === '/api/orders') {
    $body = readJsonBody();
    if (!($body instanceof stdClass)) {
        error(400, 'Invalid JSON body');
        return;
    }
    $customerId = $body->customerId ?? null;
    if (!is_int($customerId)) {
        error(400, 'customerId is required');
        return;
    }
    [$lines, $lineError] = validateLines($body->lines ?? null);
    if ($lineError !== null) {
        error(400, $lineError);
        return;
    }
    $customer = findById($store->read()['customers'], $customerId);
    if ($customer === null) {
        error(422, 'Unknown customer');
        return;
    }

    $order = $store->update(function (array $data) use ($customer, $lines) {
        $nextId = max(array_column($data['orders'], 'id') ?: [1000]) + 1;
        $order = [
            'id' => $nextId,
            'customerId' => $customer['id'],
            'status' => 'pending',
            'lines' => $lines,
        ] + Pricing::quote($lines, $customer['tier']) + [
            'createdAt' => gmdate('Y-m-d\TH:i:s\Z'),
        ];
        $data['orders'][] = $order;
        return [$data, $order];
    });

    respond(201, $order, ['Location' => '/api/orders/' . $order['id']]);
    return;
}

if ($method === 'PATCH' && preg_match('#^/api/orders/(\d+)/status$#', $path, $m)) {
    $id = (int) $m[1];
    if (findById($store->read()['orders'], $id) === null) {
        error(404, 'Order not found');
        return;
    }
    $body = readJsonBody();
    if (!($body instanceof stdClass)) {
        error(400, 'Invalid JSON body');
        return;
    }
    $newStatus = $body->status ?? null;
    if (!is_string($newStatus) || !in_array($newStatus, VALID_STATUSES, true)) {
        error(400, 'Invalid status');
        return;
    }

    $result = $store->update(function (array $data) use ($id, $newStatus) {
        foreach ($data['orders'] as $i => $order) {
            if ($order['id'] !== $id) {
                continue;
            }
            if (!in_array($newStatus, TRANSITIONS[$order['status']], true)) {
                return [$data, ['error' => "Invalid status transition from {$order['status']} to $newStatus"]];
            }
            $data['orders'][$i]['status'] = $newStatus;
            return [$data, $data['orders'][$i]];
        }
        return [$data, ['error' => 'Order not found']];
    });

    isset($result['error']) ? error(409, $result['error']) : respond(200, $result);
    return;
}

error(404, 'Not found');
