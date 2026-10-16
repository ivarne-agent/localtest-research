using System.Buffers;
using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Ccr;

/// <summary>
/// Represents a document containing updates/inserts for an organization in CCR XML format.
/// </summary>
public interface IOrganizationUpdateDocument
    : IDisposable
{
    /// <summary>
    /// Gets the identifier of the organization this document pertains to.
    /// </summary>
    public OrganizationIdentifier OrganizationIdentifier { get; }

    /// <summary>
    /// Gets the CCR XML document as a sequence of bytes.
    /// </summary>
    public ReadOnlySequence<byte> Document { get; }
}
