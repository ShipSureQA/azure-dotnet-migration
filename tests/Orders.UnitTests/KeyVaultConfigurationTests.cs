using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orders.Messaging;

namespace Orders.UnitTests;

/// <summary>
/// Key Vault is reached only through IConfiguration. These tests use the real
/// KeyVaultSecretManager mapping plus an in-memory provider standing in for the vault,
/// so no Azure subscription or network is needed.
/// </summary>
public class KeyVaultConfigurationTests
{
    private readonly PrefixKeyVaultSecretManager _manager = new("OrdersApi");

    [Theory]
    [InlineData("OrdersApi--Messaging--Transport", true)]
    [InlineData("ordersapi--Messaging--Transport", true)]
    [InlineData("BillingApi--Messaging--Transport", false)]
    [InlineData("Messaging--Transport", false)]
    public void Only_secrets_with_the_app_prefix_are_loaded(string name, bool expected)
    {
        Assert.Equal(expected, _manager.Load(new SecretProperties(name)));
    }

    [Fact]
    public void Secret_names_map_to_configuration_keys()
    {
        var secret = new KeyVaultSecret("OrdersApi--Messaging--ServiceBus--ConnectionString", "value");

        Assert.Equal("Messaging:ServiceBus:ConnectionString", _manager.GetKey(secret));
    }

    [Fact]
    public void Without_a_vault_uri_no_source_is_added()
    {
        var builder = new ConfigurationBuilder();
        var before = builder.Sources.Count;

        builder.AddOrdersKeyVault(new ConfigurationBuilder().Build());

        Assert.Equal(before, builder.Sources.Count);
    }

    [Fact]
    public void With_a_vault_uri_a_key_vault_source_is_added_lazily()
    {
        var current = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [KeyVaultConfiguration.VaultUriKey] = "https://kv-orders-demo.vault.azure.net/" })
            .Build();
        var builder = new ConfigurationBuilder();

        // Only adds the source; nothing calls Azure until Build() is invoked.
        builder.AddOrdersKeyVault(current);

        Assert.Single(builder.Sources);
    }

    [Fact]
    public void Secrets_from_the_vault_drive_the_messaging_transport()
    {
        // Simulates what AddAzureKeyVault produces after the secret-name mapping above.
        var vaultSecrets = new[]
        {
            new KeyVaultSecret("OrdersApi--Messaging--Transport", "ServiceBus"),
            new KeyVaultSecret("OrdersApi--Messaging--ServiceBus--ConnectionString",
                "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"),
            new KeyVaultSecret("BillingApi--Messaging--Transport", "StorageQueue"),
        };
        var fakeVault = vaultSecrets
            .Where(s => _manager.Load(s.Properties))
            .ToDictionary(s => _manager.GetKey(s), s => (string?)s.Value);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Messaging:Transport"] = "InMemory" })
            .AddInMemoryCollection(fakeVault) // later sources win, exactly like Key Vault added last
            .Build();
        var publisher = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddOrdersMessaging()
            .BuildServiceProvider()
            .GetRequiredService<IOrderEventPublisher>();

        Assert.IsType<ServiceBusOrderEventPublisher>(publisher);
    }
}
