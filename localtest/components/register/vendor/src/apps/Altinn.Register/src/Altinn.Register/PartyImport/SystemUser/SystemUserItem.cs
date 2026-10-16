using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Core.Parties.Records;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.PartyImport.SystemUser;

/// <summary>
/// A system user item from the System User Stream API.
/// </summary>
internal sealed record SystemUserItem
{
    /// <summary>
    /// Gets the system user ID.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the sequence number of the system user. Effectively a version id.
    /// </summary>
    public required ulong SequenceNumber { get; init; }

    /// <summary>
    /// Gets the system user name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets a value indicating whether the system user is deleted.
    /// </summary>
    public required bool IsDeleted { get; init; }

    /// <summary>
    /// Gets when the system user was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets when the system user was last changed.
    /// </summary>
    public required DateTimeOffset LastChangedAt { get; init; }

    /// <summary>
    /// Gets the type of the system user.
    /// </summary>
    public required NonExhaustiveEnum<SystemUserRecordType> Type { get; init; }

    /// <summary>
    /// Gets the party ID of the system user owner.
    /// </summary>
    public required GuidOrUint OwnerPartyId { get; init; }

    /// <summary>
    /// Represents a value that can be either a <see cref="Guid"/> or a non-zero <see langword="uint"/>.
    /// </summary>
    [JsonConverter(typeof(GuidOrUint.JsonConverter))]
    public readonly struct GuidOrUint
    {
        private readonly uint _uint;
        private readonly Guid _guid;

        /// <summary>
        /// Initializes a new instance of the <see cref="GuidOrUint"/> struct.
        /// </summary>
        /// <param name="id">The value.</param>
        public GuidOrUint(uint id)
        {
            Guard.IsGreaterThan(id, 0);

            _uint = id;
            _guid = default;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GuidOrUint"/> struct.
        /// </summary>
        /// <param name="id">The value.</param>
        public GuidOrUint(Guid id)
        {
            Guard.IsNotDefault(id);

            _guid = id;
            _uint = default;
        }

        /// <summary>
        /// Gets the value as a <see langword="uint"/>, if the value is a <see langword="uint"/>.
        /// </summary>
        /// <param name="value">The output value.</param>
        /// <returns><see langword="true"/>, if the value is a <see langword="uint"/>, else <see langword="false"/>.</returns>
        public bool IsUint(out uint value)
        {
            value = _uint;

            return value > 0;
        }

        /// <summary>
        /// Gets the value as a <see cref="Guid"/>, if the value is a <see cref="Guid"/>.
        /// </summary>
        /// <param name="value">The output value.</param>
        /// <returns><see langword="true"/>, if the value is a <see cref="Guid"/>, else <see langword="false"/>.</returns>
        public bool IsGuid(out Guid value)
        {
            value = _guid;

            return value != default;
        }

        public static implicit operator GuidOrUint(uint id) => new(id);

        public static implicit operator GuidOrUint(Guid id) => new(id);

        private sealed class JsonConverter
            : JsonConverter<GuidOrUint>
        {
            public override GuidOrUint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Number)
                {
                    var uintValue = reader.GetUInt32();
                    return new GuidOrUint(uintValue);
                }

                if (reader.TokenType != JsonTokenType.String)
                {
                    ThrowHelper.ThrowInvalidOperationException("Expected string or number token.");
                }

                if (reader.TryGetGuid(out var guid))
                {
                    return new GuidOrUint(guid);
                }

                var span = reader.HasValueSequence
                    ? reader.ValueSequence.ToArray()
                    : reader.ValueSpan;

                if (Utf8Parser.TryParse(span, out uint value, out int bytesConsumed)
                    && span.Length == bytesConsumed)
                {
                    return new GuidOrUint(value);
                }

                return ThrowHelper.ThrowFormatException<GuidOrUint>("String was not in a correct format to be parsed as a GUID or UInt32.");
            }

            public override void Write(Utf8JsonWriter writer, GuidOrUint value, JsonSerializerOptions options)
            {
                if (value._uint > 0)
                {
                    Span<char> buffer = stackalloc char[12];
                    var success = value._uint.TryFormat(buffer, out var charsWritten);
                    Debug.Assert(success);

                    writer.WriteStringValue(buffer[..charsWritten]);
                }
                else
                {
                    writer.WriteStringValue(value._guid);
                }
            }
        }
    }
}
