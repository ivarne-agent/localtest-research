namespace Digdir.Domain.Dialogporten.Infrastructure.Localtest;

/// <summary>
/// Settings for the localtest additions, bound from <c>Localtest:Dialogporten</c>. They only feed the configuration
/// defaults (<see cref="LocaltestConfiguration"/>); every default can still be overridden by the real setting it
/// produces, for instance <c>Infrastructure__DialogDbConnectionString</c>, because the defaults sit below the
/// environment variables.
/// </summary>
public sealed class LocaltestDialogportenOptions
{
    public const string SectionName = "Localtest:Dialogporten";

    /// <summary>The platform origin every other component (Authentication, Register, PDP, Events, ...) is reached through.</summary>
    public string PlatformEndpoint { get; set; } = "http://local.altinn.cloud:8000/";

    public string DatabaseHost { get; set; } = "postgres";

    public int DatabasePort { get; set; } = 5432;

    public string DatabaseName { get; set; } = "dialogporten";

    public string DatabaseUser { get; set; } = "dialogporten";

    public string DatabasePassword { get; set; } = "localtest";

    public string RedisConnectionString { get; set; } = "redis:6379";
}
