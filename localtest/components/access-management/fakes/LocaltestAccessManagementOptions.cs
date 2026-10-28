#nullable enable
namespace Altinn.AccessManagement.Localtest;

/// <summary>Settings for the localtest composition, bound from <c>Localtest:AccessManagement</c>.</summary>
public sealed class LocaltestAccessManagementOptions
{
    /// <summary>
    /// The public origin (default <c>http://local.altinn.cloud:8000/</c>): the issuer, the OpenID metadata and the host of
    /// the browser links. The services are called by name.
    /// </summary>
    public string PlatformEndpoint { get; set; } = "http://local.altinn.cloud:8000/";

    /// <summary>The Register's API by service name, with a trailing slash; the refresh reads <c>localtest/test-users</c> under it.</summary>
    public string RegisterEndpoint { get; set; } = "http://register/register/api/";

    /// <summary>Root folder of the file-backed policy store (delegation, resource and app policies with versions).</summary>
    public string PolicyStorePath { get; set; } = "/policy-store";

    /// <summary>Postgres host and port of the shared database container.</summary>
    public string DatabaseHost { get; set; } = "postgres";

    public int DatabasePort { get; set; } = 5432;

    /// <summary>
    /// Database the production migrations run in. It and the production roles <c>platform_authorization_admin</c> and
    /// <c>platform_authorization</c> are created by the Postgres image's init script
    /// (<c>postgres-init/45-access-management.sh</c>); the roles' passwords come from the manifest
    /// (<c>PostgreSQLSettings__AuthorizationDbAdminPwd</c>, <c>__AuthorizationDbPwd</c>), never a default here.
    /// </summary>
    public string DatabaseName { get; set; } = "authorizationdb";
}
