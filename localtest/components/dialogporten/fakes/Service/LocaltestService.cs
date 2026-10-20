using Digdir.Domain.Dialogporten.Infrastructure.Localtest;

namespace Digdir.Domain.Dialogporten.Service.Localtest;

/// <summary>
/// The localtest hook in the Service host's <c>Program.cs</c>, called right after <c>WebApplication.CreateBuilder</c>. The
/// host has no JWT authentication: configuration defaults and the shared replacements are all it needs.
/// </summary>
public static class LocaltestService
{
    public static void Apply(WebApplicationBuilder builder)
    {
        var options = LocaltestConfiguration.BindOptions(builder.Configuration);
        LocaltestConfiguration.AddDefaults(builder.Configuration, options, new Dictionary<string, string?>());
        LocaltestOverrides.AfterAllRegistrations(builder.Host, services => LocaltestInfrastructure.Apply(services, builder.Configuration));
    }
}
