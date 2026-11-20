using Azure.Core;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;

namespace Orders.Messaging;

/// <summary>
/// Loads only secrets named "{prefix}--Section--Key" and maps them to "Section:Key",
/// so one vault can be shared by several apps.
/// </summary>
public sealed class PrefixKeyVaultSecretManager(string prefix) : KeyVaultSecretManager
{
    private readonly string _prefix = prefix + "--";

    public override bool Load(SecretProperties secret) =>
        secret.Name.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase);

    public override string GetKey(KeyVaultSecret secret) =>
        secret.Name.Substring(_prefix.Length).Replace("--", ConfigurationPath.KeyDelimiter);
}

public static class KeyVaultConfiguration
{
    public const string VaultUriKey = "KeyVault:Uri";
    public const string DefaultPrefix = "OrdersApi";

    /// <summary>
    /// Adds Azure Key Vault as a configuration source when KeyVault:Uri is set.
    /// Without it (local runs, CI) nothing is added and the app uses appsettings,
    /// environment variables or user-secrets. The rest of the app only sees IConfiguration.
    /// </summary>
    public static IConfigurationBuilder AddOrdersKeyVault(
        this IConfigurationBuilder builder,
        IConfiguration current,
        TokenCredential? credential = null,
        string prefix = DefaultPrefix)
    {
        var vaultUri = current[VaultUriKey];
        if (string.IsNullOrWhiteSpace(vaultUri))
        {
            return builder;
        }

        return builder.AddAzureKeyVault(
            new Uri(vaultUri),
            credential ?? new DefaultAzureCredential(),
            new PrefixKeyVaultSecretManager(prefix));
    }
}
