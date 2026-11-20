namespace Orders.Core;

public readonly record struct PriceQuote(decimal Subtotal, decimal Discount, decimal Total);

/// <summary>
/// Port of legacy-php/src/Pricing.php.
/// </summary>
public static class Pricing
{
    public static decimal DiscountRate(string tier) => tier switch
    {
        "gold" => 0.10m,
        "silver" => 0.05m,
        _ => 0m,
    };

    public static PriceQuote Quote(IEnumerable<OrderLine> lines, string tier)
    {
        var subtotal = Round(lines.Sum(l => l.Quantity * l.UnitPrice));
        var discount = Round(subtotal * DiscountRate(tier));
        return new PriceQuote(subtotal, discount, Round(subtotal - discount));
    }

    /// <summary>
    /// PHP round() rounds half AWAY FROM ZERO. .NET Math.Round defaults to banker's rounding
    /// (half to even), which gives 2.66 instead of 2.67 for 2.665. Found by the parity suite;
    /// see docs/MIGRATION.md.
    /// </summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
