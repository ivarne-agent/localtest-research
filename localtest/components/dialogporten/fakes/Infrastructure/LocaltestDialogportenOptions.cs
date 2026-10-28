namespace Digdir.Domain.Dialogporten.Infrastructure.Localtest;

/// <summary>
/// Settings for the localtest additions, bound from <c>Localtest:Dialogporten</c>. They only feed the configuration
/// defaults (<see cref="LocaltestConfiguration"/>); every default can still be overridden by the real setting it
/// produces, for instance <c>Infrastructure__Redis__ConnectionString</c>, because the defaults sit below the
/// environment variables. The database connection string (<c>Infrastructure__DialogDbConnectionString</c>, with the
/// password) has no default here: the runtimes set it from the manifest.
/// </summary>
public sealed class LocaltestDialogportenOptions
{
    public const string SectionName = "Localtest:Dialogporten";

    /// <summary>The platform origin every other component (Authentication, Register, PDP, Events, ...) is reached through.</summary>
    public string PlatformEndpoint { get; set; } = "http://local.altinn.cloud:8000/";

    public string RedisConnectionString { get; set; } = "redis:6379";
}
