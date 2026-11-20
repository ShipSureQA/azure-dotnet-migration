<?php
declare(strict_types=1);

/**
 * Legacy pricing rules. The .NET port (Orders.Core/Pricing.cs) must produce
 * exactly the same numbers. PHP round() rounds half away from zero.
 */
final class Pricing
{
    private const TIER_DISCOUNT = [
        'gold' => 0.10,
        'silver' => 0.05,
        'standard' => 0.0,
    ];

    /** @param array<int, array{sku:string, quantity:int, unitPrice:float|int}> $lines */
    public static function quote(array $lines, string $tier): array
    {
        $subtotal = 0.0;
        foreach ($lines as $line) {
            $subtotal += $line['quantity'] * $line['unitPrice'];
        }
        $subtotal = round($subtotal, 2);
        $rate = self::TIER_DISCOUNT[$tier] ?? 0.0;
        $discount = round($subtotal * $rate, 2);
        $total = round($subtotal - $discount, 2);

        return ['subtotal' => $subtotal, 'discount' => $discount, 'total' => $total];
    }
}
