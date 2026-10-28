#nullable enable

namespace Altinn.ResourceRegistry.Localtest;

/// <summary>Settings for the localtest Resource Registry fakes, bound from <c>Localtest:ResourceRegistry</c>.</summary>
public sealed class LocaltestResourceRegistryOptions
{
    /// <summary>The platform origin; Storage, Register, Access Management and Authentication are reached through it.</summary>
    public string PlatformEndpoint { get; set; } = "http://local.altinn.cloud:8000/";

    /// <summary>
    /// The policy store shared with the PDP and Access Management (the <c>policy-store</c> volume), one folder per blob
    /// container: app policies in <c>metadata/{org}/{app}/policy.xml</c>, where the PDP's policy endpoint writes them,
    /// resource policies in <c>resourceregistry/{id}/resourcepolicy.xml</c>.
    /// </summary>
    public string PolicyStorePath { get; set; } = "/policy-store";

    public string DatabaseHost { get; set; } = "postgres";

    public int DatabasePort { get; set; } = 5432;

    public string DatabaseName { get; set; } = "resourceregistrydb";

    // The passwords have no default here: PostgreSQLSettings__AuthorizationDbAdminPwd and __AuthorizationDbPwd come from
    // the manifest (the runtimes), as the Postgres container's PLATFORM_AUTHORIZATION_ADMIN_PASSWORD and _PASSWORD do.
}
