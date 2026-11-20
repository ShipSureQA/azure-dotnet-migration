using Orders.Core;

namespace Orders.UnitTests;

public class PricingTests
{
    [Theory]
    [InlineData("gold", 0.10)]
    [InlineData("silver", 0.05)]
    [InlineData("standard", 0.0)]
    [InlineData("unknown-tier", 0.0)]
    public void Discount_rate_matches_legacy_tiers(string tier, double expected)
    {
        Assert.Equal((decimal)expected, Pricing.DiscountRate(tier));
    }

    [Fact]
    public void Gold_customer_gets_ten_percent_off()
    {
        var quote = Pricing.Quote([new OrderLine("KB-001", 2, 49.50m)], "gold");

        Assert.Equal(new PriceQuote(99.00m, 9.90m, 89.10m), quote);
    }

    [Fact]
    public void Midpoint_discount_rounds_half_away_from_zero_like_php()
    {
        // 53.30 * 5% = 2.665. PHP round() gives 2.67; .NET's default banker's rounding gives 2.66.
        var quote = Pricing.Quote([new OrderLine("MS-777", 2, 26.65m)], "silver");

        Assert.Equal(2.67m, quote.Discount);
        Assert.Equal(50.63m, quote.Total);
    }

    [Fact]
    public void Default_math_round_would_have_broken_parity()
    {
        // Documents the bug the parity suite caught during the port.
        Assert.Equal(2.66m, Math.Round(2.665m, 2));
        Assert.Equal(2.67m, Pricing.Round(2.665m));
    }

    [Fact]
    public void Multi_line_subtotal_is_summed_before_rounding()
    {
        var quote = Pricing.Quote(
            [new OrderLine("A", 3, 0.333m), new OrderLine("B", 1, 10m)],
            "standard");

        Assert.Equal(11.00m, quote.Subtotal);
        Assert.Equal(0m, quote.Discount);
        Assert.Equal(11.00m, quote.Total);
    }

    [Fact]
    public void Seed_orders_are_consistent_with_the_pricing_rules()
    {
        var seed = SeedData.LoadEmbedded();

        foreach (var order in seed.Orders)
        {
            var tier = seed.Customers.Single(c => c.Id == order.CustomerId).Tier;
            var quote = Pricing.Quote(order.Lines, tier);
            Assert.Equal(new PriceQuote(order.Subtotal, order.Discount, order.Total), quote);
        }
    }
}
