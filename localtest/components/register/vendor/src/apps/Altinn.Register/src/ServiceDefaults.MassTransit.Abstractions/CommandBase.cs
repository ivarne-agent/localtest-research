using System.Text.Json.Serialization;
using MassTransit;

namespace Altinn.Authorization.ServiceDefaults.MassTransit;

/// <summary>
/// Base record for a mass transit command.
/// </summary>
[ExcludeFromTopology]
[ExcludeFromImplementedTypes]
public abstract record CommandBase
    : IMessageBase
{
    private readonly Guid _id;
    private readonly Guid _correlationId;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandBase"/> class with a unique identifier and correlation ID.
    /// </summary>
    /// <remarks>
    /// The <see cref="CorrelationId"/> can be overridden by derived classes <strong>or</strong> by the producer of the command.
    /// </remarks>
    protected CommandBase()
    {
        _id = Guid.CreateVersion7();
        _correlationId = _id;
    }

    /// <summary>
    /// Gets the command ID.
    /// </summary>
    [JsonInclude]
    [JsonPropertyName("commandId")]
    public Guid CommandId
    {
        get => _id;
        private init => _id = value;
    }

    /// <summary>
    /// Gets the unique identifier used to correlate related operations or requests.
    /// </summary>
    public Guid CorrelationId
    {
        get => _correlationId;
        init => _correlationId = value;
    }

    /// <inheritdoc/>
    Guid IMessageBase.MessageId
        => CommandId;
}
