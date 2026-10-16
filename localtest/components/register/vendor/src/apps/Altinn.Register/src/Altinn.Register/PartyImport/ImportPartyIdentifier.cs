using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Altinn.Register.Contracts;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.PartyImport;

/// <summary>
/// A union of PartyUuid, OrganizationIdentifier and PersonIdentifier. Used to identify a party in the import process without needing to know the type of identifier.
/// </summary>
[JsonConverter(typeof(ImportPartyIdentifier.JsonConverter))]
public readonly struct ImportPartyIdentifier
{
    private readonly Guid _partyUuid;
    private readonly object? _identifierRef;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportPartyIdentifier"/> struct with a <see cref="PersonIdentifier"/>.
    /// </summary>
    /// <param name="personIdentifier">The person identifier.</param>
    public ImportPartyIdentifier(PersonIdentifier personIdentifier)
    {
        _identifierRef = personIdentifier;
        _partyUuid = Guid.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportPartyIdentifier"/> struct with a <see cref="OrganizationIdentifier"/>.
    /// </summary>
    /// <param name="organizationIdentifier">The organization identifier.</param>
    public ImportPartyIdentifier(OrganizationIdentifier organizationIdentifier)
    {
        _identifierRef = organizationIdentifier;
        _partyUuid = Guid.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportPartyIdentifier"/> struct with a <see cref="Guid"/>.
    /// </summary>
    /// <param name="partyUuid">The party UUID.</param>
    public ImportPartyIdentifier(Guid partyUuid)
    {
        Guard.IsNotEqualTo(partyUuid, Guid.Empty);

        _identifierRef = null;
        _partyUuid = partyUuid;
    }

    /// <summary>
    /// Gets the value of the identifier, which can be either a <see cref="PersonIdentifier"/>, an <see cref="OrganizationIdentifier"/>, or a <see cref="Guid"/> representing the party UUID.
    /// </summary>
    public object? Value
        => (_identifierRef, _partyUuid) switch
        {
            (PersonIdentifier personId, _) => personId,
            (OrganizationIdentifier orgId, _) => orgId,
            (null, Guid partyUuid) when partyUuid != Guid.Empty => partyUuid,
            _ => null,
        };

    /// <summary>
    /// Gets a value indicating whether the identifier has a value (i.e., is not null).
    /// </summary>
    public bool HasValue
        => _identifierRef != null || _partyUuid != Guid.Empty;

    /// <inheritdoc/>
    public override string? ToString()
        => (_identifierRef, _partyUuid) switch
        {
            (PersonIdentifier personId, _) => personId.ToString(),
            (OrganizationIdentifier orgId, _) => orgId.ToString(),
            (null, Guid partyUuid) when partyUuid != Guid.Empty => partyUuid.ToString(),
            _ => null,
        };

    /// <summary>
    /// Gets a log-safe representation of the identifier, redacting sensitive information.
    /// </summary>
    /// <returns>A log-safe string representation of the identifier.</returns>
    public string ToLogSafeString()
        => (_identifierRef, _partyUuid) switch
        {
            (PersonIdentifier, _) => "[redacted person identifier]",
            (OrganizationIdentifier orgId, _) => orgId.ToString(),
            (null, Guid partyUuid) when partyUuid != Guid.Empty => partyUuid.ToString(),
            _ => "<null>",
        };

    /// <summary>
    /// Tries to get the person identifier if the value is of type <see cref="PersonIdentifier"/>.
    /// </summary>
    /// <param name="personIdentifier">The person identifier if the value is of type <see cref="PersonIdentifier"/>; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the value is of type <see cref="PersonIdentifier"/>; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue([NotNullWhen(true)] out PersonIdentifier? personIdentifier)
    {
        if (_identifierRef is PersonIdentifier pid)
        {
            personIdentifier = pid;
            return true;
        }

        personIdentifier = default;
        return false;
    }

    /// <summary>
    /// Tries to get the organization identifier if the value is of type <see cref="OrganizationIdentifier"/>.
    /// </summary>
    /// <param name="organizationIdentifier">The organization identifier if the value is of type <see cref="OrganizationIdentifier"/>; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the value is of type <see cref="OrganizationIdentifier"/>; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue([NotNullWhen(true)] out OrganizationIdentifier? organizationIdentifier)
    {
        if (_identifierRef is OrganizationIdentifier oid)
        {
            organizationIdentifier = oid;
            return true;
        }

        organizationIdentifier = default;
        return false;
    }

    /// <summary>
    /// Tries to get the party UUID if the value is of type <see cref="Guid"/>.
    /// </summary>
    /// <param name="partyUuid">The party UUID if the value is of type <see cref="Guid"/>; otherwise, undefined.</param>
    /// <returns><see langword="true"/> if the value is of type <see cref="Guid"/>; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue(out Guid partyUuid)
    {
        if (_partyUuid != Guid.Empty)
        {
            partyUuid = _partyUuid;
            return true;
        }

        partyUuid = default;
        return false;
    }

    /// <summary>
    /// Defines an implicit conversion from <see cref="PersonIdentifier"/> to <see cref="ImportPartyIdentifier"/>.
    /// </summary>
    /// <param name="personIdentifier">The person identifier to convert.</param>
    public static implicit operator ImportPartyIdentifier(PersonIdentifier personIdentifier)
        => new(personIdentifier);

    /// <summary>
    /// Defines an implicit conversion from <see cref="OrganizationIdentifier"/> to <see cref="ImportPartyIdentifier"/>.
    /// </summary>
    /// <param name="organizationIdentifier">The organization identifier to convert.</param>
    public static implicit operator ImportPartyIdentifier(OrganizationIdentifier organizationIdentifier)
        => new(organizationIdentifier);

    /// <summary>
    /// Defines an implicit conversion from <see cref="Guid"/> to <see cref="ImportPartyIdentifier"/>.
    /// </summary>
    /// <param name="partyUuid">The party UUID to convert.</param>
    public static implicit operator ImportPartyIdentifier(Guid partyUuid)
        => new(partyUuid);

    /// <summary>
    /// Defines an explicit conversion from <see cref="ImportPartyIdentifier"/> to <see cref="PersonIdentifier"/>.
    /// </summary>
    /// <param name="identifier">The <see cref="ImportPartyIdentifier"/> to convert.</param>
    public static explicit operator PersonIdentifier(ImportPartyIdentifier identifier)
        => identifier.TryGetValue(out PersonIdentifier? personId)
        ? personId
        : ThrowHelper.ThrowInvalidCastException<PersonIdentifier>($"The identifier does not contain a {nameof(PersonIdentifier)}.");

    /// <summary>
    /// Defines an explicit conversion from <see cref="ImportPartyIdentifier"/> to <see cref="OrganizationIdentifier"/>.
    /// </summary>
    /// <param name="identifier">The <see cref="ImportPartyIdentifier"/> to convert.</param>
    public static explicit operator OrganizationIdentifier(ImportPartyIdentifier identifier)
        => identifier.TryGetValue(out OrganizationIdentifier? orgId)
        ? orgId
        : ThrowHelper.ThrowInvalidCastException<OrganizationIdentifier>($"The identifier does not contain a {nameof(OrganizationIdentifier)}.");

    /// <summary>
    /// Defines an explicit conversion from <see cref="ImportPartyIdentifier"/> to <see cref="Guid"/>.
    /// </summary>
    /// <param name="identifier">The <see cref="ImportPartyIdentifier"/> to convert.</param>
    public static explicit operator Guid(ImportPartyIdentifier identifier)
        => identifier.TryGetValue(out Guid partyUuid)
        ? partyUuid
        : ThrowHelper.ThrowInvalidCastException<Guid>($"The identifier does not contain a {nameof(Guid)}.");

    /// <summary>
    /// Json converter for <see cref="ImportPartyIdentifier"/>.
    /// </summary>
    internal sealed class JsonConverter
        : JsonConverter<ImportPartyIdentifier>
    {
        /// <inheritdoc/>
        public override bool HandleNull => true;

        /// <inheritdoc/>
        public override ImportPartyIdentifier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return ReadFromString(reader.GetString());
        }

        private static ImportPartyIdentifier ReadFromString(string? value)
        {
            if (value is null)
            {
                return default;
            }

            if (PersonIdentifier.TryParse(value, provider: null, out PersonIdentifier? personId))
            {
                return new ImportPartyIdentifier(personId);
            }

            if (OrganizationIdentifier.TryParse(value, provider: null, out OrganizationIdentifier? orgId))
            {
                return new ImportPartyIdentifier(orgId);
            }

            if (Guid.TryParse(value, out Guid partyUuid) && partyUuid != Guid.Empty)
            {
                return new ImportPartyIdentifier(partyUuid);
            }

            throw new JsonException($"Unable to parse '{value}' as {nameof(ImportPartyIdentifier)}.");
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, ImportPartyIdentifier value, JsonSerializerOptions options)
        {
            // TODO: use switch once we have union support
            if (value.TryGetValue(out PersonIdentifier? personId))
            {
                JsonSerializer.Serialize(writer, personId, options);
                return;
            }

            if (value.TryGetValue(out OrganizationIdentifier? orgId))
            {
                JsonSerializer.Serialize(writer, orgId, options);
                return;
            }

            if (value.TryGetValue(out Guid partyUuid))
            {
                JsonSerializer.Serialize(writer, partyUuid, options);
                return;
            }

            Debug.Assert(!value.HasValue);
            writer.WriteNullValue();
        }
    }
}
