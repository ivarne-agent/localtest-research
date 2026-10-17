#nullable enable
namespace Altinn.AccessManagement.Localtest;

/// <summary>Settings for the localtest composition, bound from <c>Localtest:AccessManagement</c>.</summary>
public sealed class LocaltestAccessManagementOptions
{
    /// <summary>Base URL every platform service is reached on, the ingress. Default <c>http://local.altinn.cloud:8000/</c>.</summary>
    public string PlatformEndpoint { get; set; } = "http://local.altinn.cloud:8000/";

    /// <summary>Root folder of the file-backed policy store (delegation, resource and app policies with versions).</summary>
    public string PolicyStorePath { get; set; } = "/policy-store";

    /// <summary>Postgres host and port of the shared database container.</summary>
    public string DatabaseHost { get; set; } = "postgres";

    public int DatabasePort { get; set; } = 5432;

    /// <summary>Database the production migrations run in. Created on start when missing.</summary>
    public string DatabaseName { get; set; } = "authorizationdb";

    /// <summary>
    /// A connection that may create roles and databases on the shared container (the compose Postgres superuser).
    /// The production roles <c>platform_authorization_admin</c> and <c>platform_authorization</c>, which the
    /// migrations grant to by name, are created with <see cref="DatabasePassword"/> when they do not exist.
    /// </summary>
    public string BootstrapConnectionString { get; set; } = "Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=postgres";

    public string DatabasePassword { get; set; } = "localtest";
}
