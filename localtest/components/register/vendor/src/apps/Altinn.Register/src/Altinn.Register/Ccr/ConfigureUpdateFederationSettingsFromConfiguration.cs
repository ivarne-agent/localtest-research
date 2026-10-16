using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Ccr;

/// <summary>
/// Configures <see cref="CcrUpdateFederationSettings"/> from configuration.
/// </summary>
internal sealed class ConfigureUpdateFederationSettingsFromConfiguration
    : IConfigureOptions<CcrUpdateFederationSettings>
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureUpdateFederationSettingsFromConfiguration"/> class.
    /// </summary>
    public ConfigureUpdateFederationSettingsFromConfiguration(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc/>
    public void Configure(CcrUpdateFederationSettings options)
    {
        var enabled = _configuration.GetValue<bool>("Altinn:register:Ccr:Federate:Enable");
        options.Enable = enabled;

        if (!enabled)
        {
            return;
        }

        var targetsSection = _configuration.GetSection("Altinn:register:Ccr:Federate:Targets");
        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var targetSection in targetsSection.GetChildren())
        {
            var childName = targetSection.Key;
            builder.Add($"ccr-federation:{childName}");
        }

        options.Targets = builder.ToImmutable();
    }
}
