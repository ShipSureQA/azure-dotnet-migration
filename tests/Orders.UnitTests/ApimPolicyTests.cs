using System.Xml;
using System.Xml.Linq;

namespace Orders.UnitTests;

/// <summary>
/// The APIM policy is deployed by Bicep (infra/main.bicep, loadTextContent). These checks
/// catch broken XML and missing guard rails before a deployment would.
/// </summary>
public class ApimPolicyTests
{
    private static readonly string PolicyPath =
        Path.Combine(AppContext.BaseDirectory, "infra", "apim", "orders-api-policy.xml");

    private static XElement Load() => XDocument.Load(PolicyPath).Root!;

    [Fact]
    public void Policy_is_well_formed_xml()
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
        using var reader = XmlReader.Create(PolicyPath, settings);
        while (reader.Read())
        {
        }
    }

    [Fact]
    public void Policy_has_the_four_sections_in_order_each_calling_base()
    {
        var root = Load();

        Assert.Equal("policies", root.Name.LocalName);
        Assert.Equal(
            new[] { "inbound", "backend", "outbound", "on-error" },
            root.Elements().Select(e => e.Name.LocalName));
        Assert.All(root.Elements(), section => Assert.NotNull(section.Element("base")));
    }

    [Fact]
    public void Inbound_rate_limits_per_subscription()
    {
        var rateLimit = Load().Element("inbound")!.Element("rate-limit-by-key");

        Assert.NotNull(rateLimit);
        Assert.True(int.Parse(rateLimit!.Attribute("calls")!.Value) > 0);
        Assert.True(int.Parse(rateLimit.Attribute("renewal-period")!.Value) > 0);
        Assert.Contains("context.Subscription", rateLimit.Attribute("counter-key")!.Value);
    }

    [Fact]
    public void Inbound_validates_jwt_with_openid_configuration_and_audience()
    {
        var jwt = Load().Element("inbound")!.Element("validate-jwt");

        Assert.NotNull(jwt);
        Assert.Equal("401", jwt!.Attribute("failed-validation-httpcode")!.Value);
        Assert.StartsWith("https://", jwt.Element("openid-config")!.Attribute("url")!.Value);
        Assert.NotEmpty(jwt.Element("audiences")!.Elements("audience"));
    }

    [Fact]
    public void Inbound_adds_a_correlation_header()
    {
        var header = Load().Element("inbound")!.Elements("set-header")
            .SingleOrDefault(h => h.Attribute("name")?.Value == "X-Correlation-Id");

        Assert.NotNull(header);
    }

    [Theory]
    [InlineData("X-Powered-By")]
    [InlineData("Server")]
    public void Outbound_strips_server_fingerprint_headers(string name)
    {
        var header = Load().Element("outbound")!.Elements("set-header")
            .SingleOrDefault(h => h.Attribute("name")?.Value == name);

        Assert.Equal("delete", header?.Attribute("exists-action")?.Value);
    }
}
