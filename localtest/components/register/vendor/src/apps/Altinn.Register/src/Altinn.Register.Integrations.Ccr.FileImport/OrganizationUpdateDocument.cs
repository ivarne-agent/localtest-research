using System.Buffers;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Ccr;
using Nerdbank.Streams;

namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Implementation of <see cref="IOrganizationUpdateDocument"/> backed by a <see cref="Sequence{T}"/>.
/// </summary>
internal sealed class OrganizationUpdateDocument
    : IOrganizationUpdateDocument
{
    private readonly Sequence<byte> _document;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrganizationUpdateDocument"/> class.
    /// </summary>
    /// <param name="organizationIdentifier">The identifier of the organization.</param>
    /// <param name="document">The document sequence.</param>
    public OrganizationUpdateDocument(OrganizationIdentifier organizationIdentifier, Sequence<byte> document)
    {
        OrganizationIdentifier = organizationIdentifier;
        _document = document;
    }

    /// <inheritdoc/>
    public OrganizationIdentifier OrganizationIdentifier { get; }

    /// <inheritdoc/>
    public ReadOnlySequence<byte> Document
        => _document.AsReadOnlySequence;

    /// <inheritdoc/>
    public void Dispose()
    {
        _document.Dispose();
    }
}
