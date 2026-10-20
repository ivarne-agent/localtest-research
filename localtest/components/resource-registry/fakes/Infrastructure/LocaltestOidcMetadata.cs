#nullable enable
using System;
using Altinn.Common.Authentication.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Altinn.ResourceRegistry.Localtest.Infrastructure;

/// <summary>
/// The localtest authentication component publishes its OpenID metadata over plain http. Production's JWT cookie handler
/// relaxes HTTPS for the scheme's own metadata in Development, but for a token whose issuer matches a configured
/// <c>OidcProviders</c> entry it builds its own metadata manager with a default (HTTPS-only) retriever, cached per
/// endpoint in the shared <see cref="ConfigurationMangerUtil"/>. Registering that manager up front with a retriever that
/// allows http keeps the production validation path and only changes the transport check, as Development intends.
/// </summary>
public static class LocaltestOidcMetadata
{
    public static void AllowHttpProviders(IConfiguration config)
    {
        foreach (var provider in config.GetSection("OidcProviders").GetChildren())
        {
            var endpoint = provider["WellKnownConfigEndpoint"];
            if (string.IsNullOrEmpty(endpoint) || !endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ConfigurationMangerUtil.Instance.ConfigManagers[endpoint.ToLower()] = new ConfigurationManager<OpenIdConnectConfiguration>(
                endpoint,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = false });
        }
    }
}
