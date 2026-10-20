namespace Altinn.Platform.Receipt.Configuration;

/// <summary>
/// The key vault settings used to fetch values from key vault
/// </summary>
public class KeyVaultSettings
{
    /// <summary>
    /// The uri to the key vault
    /// </summary>
    public string SecretUri { get; set; } = string.Empty;
}
