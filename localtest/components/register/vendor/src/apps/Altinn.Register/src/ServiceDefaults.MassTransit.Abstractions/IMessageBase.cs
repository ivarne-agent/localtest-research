using MassTransit;

namespace Altinn.Authorization.ServiceDefaults.MassTransit;

/// <summary>
/// Defines the base contract for a message with a unique identifier.
/// </summary>
public interface IMessageBase
    : CorrelatedBy<Guid>
{
    /// <summary>
    /// Gets the unique identifier for the message.
    /// </summary>
    Guid MessageId { get; }
}
