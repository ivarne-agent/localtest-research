using System.Diagnostics.CodeAnalysis;

namespace Altinn.Authorization.ServiceDefaults.MassTransit;

/// <summary>
/// Provides the client configuration settings for connecting to a MassTransit bus.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class MassTransitSettings
{
    /// <summary>
    /// Gets or sets a boolean value that indicates whether the bus health check is disabled or not.
    /// </summary>
    /// <value>
    /// The default value is <see langword="false"/>.
    /// </value>
    public bool DisableHealthChecks { get; set; }

    /// <summary>
    /// Gets or sets a boolean value that indicates whether the OpenTelemetry tracing is disabled or not.
    /// </summary>
    /// <value>
    /// The default value is <see langword="false"/>.
    /// </value>
    public bool DisableTracing { get; set; }

    /// <summary>
    /// Gets or sets a boolean value that indicates whether the OpenTelemetry metrics are disabled or not.
    /// </summary>
    /// <value>
    /// The default value is <see langword="false"/>.
    /// </value>
    public bool DisableMetrics { get; set; }

    /// <summary>
    /// Gets or sets the transport to use for the bus.
    /// </summary>
    public MassTransitTransport Transport { get; set; }

    /// <summary>
    /// Gets or sets the Rabbit MQ specific settings.
    /// </summary>
    public MassTransitRabbitMqSettings RabbitMq { get; set; } = new();

    /// <summary>
    /// Gets or sets the Azure Service Bus specific settings.
    /// </summary>
    public MassTransitAzureServiceBusSettings AzureServiceBus { get; set; } = new();
}

/// <summary>
/// Mass transit transports.
/// </summary>
public enum MassTransitTransport
{
    /// <summary>
    /// In memory transport. Only used for testing.
    /// </summary>
    InMemory = default,

    /// <summary>
    /// Rabbit MQ transport.
    /// </summary>
    RabbitMq,

    /// <summary>
    /// Azure Service Bus transport.
    /// </summary>
    AzureServiceBus,
}

/// <summary>
/// Provides the client configuration settings for connecting to a Rabbit MQ bus.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class MassTransitRabbitMqSettings
{
    /// <summary>
    /// Gets or sets the host name of the Rabbit MQ server.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// Gets or sets the port number of the Rabbit MQ server.
    /// </summary>
    public ushort Port { get; set; } = 5672;

    /// <summary>
    /// Gets or sets the management port number of the Rabbit MQ server.
    /// </summary>
    public ushort ManagementPort { get; set; }

    /// <summary>
    /// Gets or sets the virtual host of the Rabbit MQ server.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Gets or sets the user name of the Rabbit MQ server.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the virtual host name.
    /// </summary>
    public string VHost { get; set; } = "/";

    /// <summary>
    /// Gets or sets a value indicating whether to use SSL or not.
    /// </summary>
    public bool UseSsl { get; set; } = false;
}

/// <summary>
/// Provides the client configuration settings for connecting to an Azure Service Bus.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class MassTransitAzureServiceBusSettings
{
    /// <summary>
    /// Gets or sets the connection string for the Azure Service Bus.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the endpoint for the Azure Service Bus.
    /// </summary>
    /// <remarks>
    /// This is only used if not <see cref="ConnectionString"/> is set.
    /// </remarks>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the credentials for the Azure Service Bus.
    /// </summary>
    /// <remarks>
    /// This is only used if not <see cref="ConnectionString"/> is set.
    /// </remarks>
    public MassTransitAzureCredentialSettings Credentials { get; set; } = new();
}

/// <summary>
/// Provides the client configuration settings for the credentials for an Azure Service Bus.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class MassTransitAzureCredentialSettings
{
    /// <summary>
    /// Gets or sets whether to enable environment-variable based credentials.
    /// </summary>
    public bool Environment { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to enable workload identity credentials.
    /// </summary>
    public bool WorkloadIdentity { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable managed identity credentials.
    /// </summary>
    public bool ManagedIdentity { get; set; } = true;
}
