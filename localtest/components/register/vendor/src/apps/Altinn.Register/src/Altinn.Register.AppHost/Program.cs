using System.Diagnostics.CodeAnalysis;
using Altinn.Register.AppHost;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
var altinnEnv = "LOCAL-AT22"; // TODO: use AltinnEnvironment from service defaults (move to abstractions package)
var authenticationEnvFragment = altinnEnv.Split('-')[1].ToLowerInvariant();

// random port number - don't reuse if copy-pasting this code.
var postgresPort = builder.Configuration.GetValue("Postgres:Port", defaultValue: 28215);
var rabbitMqPort = builder.Configuration.GetValue("RabbitMq:Port", defaultValue: 28216);
var rabbitMqMgmtPort = builder.Configuration.GetValue("RabbitMq:ManagementPort", defaultValue: 28217);

// databases
var postgresServer = builder.AddPostgres("postgres", port: postgresPort)
    .WithDataVolume()
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPublicEndpoints();

var registerDb = postgresServer.AddAltinnDatabase(
    "register-db",
    databaseName: "register");

// rabbitmq
var rabbitMqUsername = builder.CreateResourceBuilder(new ParameterResource("rabbitmq-username", _ => "rabbit-admin"));
var rabbitMq = builder.AddRabbitMQ("rabbitmq", userName: rabbitMqUsername, port: rabbitMqPort)
    .WithDataVolume()
    .WithManagementPlugin(port: rabbitMqMgmtPort)
    .WithContainerFiles("/etc/rabbitmq", [new ContainerFile { Name = "enabled_plugins", Contents = "[rabbitmq_management,rabbitmq_prometheus,rabbitmq_shovel_management,rabbitmq_shovel].\n" }])
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPublicEndpoints();

// dependencies
var authentication = builder.AddExternalService("altinn-authentication", $"https://platform.{authenticationEnvFragment}.altinn.cloud/");
var accessManagement = builder.AddExternalService("altinn-access-management", $"https://platform.{authenticationEnvFragment}.altinn.cloud/");
var freg = builder.AddExternalService("folkeregisteret", "https://folkeregisteret-api-konsument.sits.no/");
var sire = builder.AddExternalService("sire", "https://skatteetatenregistrertselskap.api.skatteetaten-test.no/");
var sireEvents = builder.AddExternalService("sire-events", "https://skatteetatenregistrertselskaphendelser.api.skatteetaten-test.no/");
var altinn2 = builder.AddExternalService("altinn2", $"https://{authenticationEnvFragment}.altinn.cloud/");

// environment callbacks
var configureRabbitMq = (EnvironmentCallbackContext ctx) =>
{
    var env = ctx.EnvironmentVariables;
    var prefix = "Altinn__MassTransit__register__";
    var rabbitEndpoint = rabbitMq.GetEndpoint("tcp");
    var rabbitMgmtEndpoint = rabbitMq.GetEndpoint("management");

    env[$"{prefix}Enable"] = "true";
    env[$"{prefix}Transport"] = "rabbitmq";

    env[$"{prefix}RabbitMq__Host"] = rabbitEndpoint.Property(EndpointProperty.Host);
    env[$"{prefix}RabbitMq__Port"] = rabbitEndpoint.Property(EndpointProperty.Port);
    env[$"{prefix}RabbitMq__ManagementPort"] = rabbitMgmtEndpoint.Property(EndpointProperty.Port);
    env[$"{prefix}RabbitMq__Username"] = ReferenceExpression.Create($"{rabbitMq.Resource.UserNameParameter!}");
    env[$"{prefix}RabbitMq__Password"] = ReferenceExpression.Create($"{rabbitMq.Resource.PasswordParameter}");
    env[$"{prefix}RabbitMq__VirtualHost"] = "/";
};

// init
var registerInit = builder.AddProject<Projects.Altinn_Register>("register-init", launchProfileName: "Init Altinn.Register")
    .WithReference(registerDb, "register")
    .WithEnvironment("ASPNET_ENVIRONMENT", "Development")
    .WithEnvironment("ALTINN_ENVIRONMENT", altinnEnv)
    .WithEnvironment("Altinn__Npgsql__register__Enable", "true")
    .WithEnvironment(configureRabbitMq)
    .WithEnvironment("Altinn__RunInitOnly", "true");

var registerApi = builder.AddProject<Projects.Altinn_Register>("register")
    .WithReference(registerDb, "register")
    .WithReference(authentication)
    .WithReference(accessManagement)
    .WithReference(freg)
    .WithReference(sire)
    .WithReference(sireEvents)
    .WithReference(altinn2)
    .WaitFor(rabbitMq)
    .WaitForCompletion(registerInit)
    .WithEnvironment("ALTINN_ENVIRONMENT", altinnEnv)
    .WithEnvironment(configureRabbitMq)
    .WithEnvironment("Altinn__Npgsql__register__Enable", "true")
    .WithEnvironment("Altinn__Npgsql__register__Migrate__Enabled", "false") // TODO: re-enable once quartz migrations can run in the init-container
    .WithEnvironment("Altinn__register__PartyImport__A2__Enable", "false")
    .WithEnvironment("Altinn__register__PartyImport__A2__PartyUserId__Enable", "false")
    .WithEnvironment("Altinn__register__PartyImport__A2__Profiles__Enable", "false")
    .WithEnvironment("Altinn__register__PartyImport__SystemUsers__Enable", "false")
    .WithEnvironment("Altinn__register__PartyImport__Sire__Listen", "false")
    .WithEnvironment("Altinn__register__PartyImport__Sire__Enrich", "false")
    .WithEnvironment("Altinn__register__Party__CreatePartyId", "false")
    .WithHttpHealthCheck("/health");

await builder.Build().RunAsync();

/// <summary>
/// Startup class.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed partial class Program
{
    private Program()
    {
    }
}
