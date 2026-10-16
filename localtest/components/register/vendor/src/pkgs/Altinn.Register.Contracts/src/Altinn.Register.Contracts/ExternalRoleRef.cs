using Altinn.Authorization.ModelUtils;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents a reference to an external role.
/// </summary>
public record ExternalRoleRef
    : IExternalRoleRef
{
    private readonly NonExhaustiveEnum<ExternalRoleSource> _source;
    private readonly string _identifier = null!;
    private readonly string _urn = null!;

    /// <inheritdoc/>
    [JsonPropertyName("source")]
    public required NonExhaustiveEnum<ExternalRoleSource> Source
    {
        get => _source;
        init
        {
            if (value.IsWellKnown)
            {
                if (value.Value == default)
                {
                    ThrowHelper.ThrowArgumentException(nameof(value), "Source cannot be the default value.");
                }
            }
            else if (value.IsUnknown)
            {
                if (string.IsNullOrEmpty(value.UnknownValue))
                {
                    ThrowHelper.ThrowArgumentException(nameof(value), "Source cannot be empty.");
                }
            }

            _source = value;

            if (IsFinalized)
            {
                _urn = CreateUrn();
            }
        }
    }

    /// <inheritdoc/>
    [JsonPropertyName("identifier")]
    public required string Identifier
    {
        get => _identifier;
        init
        {
            Guard.IsNotNullOrEmpty(value);
            _identifier = value;

            if (IsFinalized)
            {
                _urn = CreateUrn();
            }
        }
    }

    /// <summary>
    /// Gets the canonical URN of the party.
    /// </summary>
    [JsonPropertyName("urn")]
    public string Urn => _urn;

    /// <inheritdoc/>
    public override string ToString()
        => _urn.ToString();

    private bool SourceIsSet => (_source.IsWellKnown && _source.Value != default) || _source.IsUnknown;

    private bool IdentifierIsSet => _identifier is not null;

    private bool IsFinalized => SourceIsSet && IdentifierIsSet;

    // TODO: use typed urn
    private string CreateUrn()
        => $"urn:altinn:external-role:{_source.ToUrnFragment()}:{_identifier}";
}
