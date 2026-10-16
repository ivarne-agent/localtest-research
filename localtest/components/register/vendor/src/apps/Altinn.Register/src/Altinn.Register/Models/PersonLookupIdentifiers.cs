using System.Buffers;
using System.Buffers.Text;
using System.Text;
using CommunityToolkit.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Models;

/// <summary>
/// Represents the input parameters for the Person lookup endpoint.
/// </summary>
public class PersonLookupIdentifiers
{
    private static readonly UTF8Encoding Utf8Strict
        = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The name of the header containing the national identity number of the person to look up.
    /// </summary>
    public const string NationalIdentityNumberHeaderName = "X-Ai-NationalIdentityNumber";

    /// <summary>
    /// The name of the header containing the last name of the person to look up. This must match the last name of the identified person.
    /// </summary>
    public const string LastNameHeaderName = "X-Ai-LastName";

    private string? _lastName;

    /// <summary>
    /// The unique national identity number of the person.
    /// </summary>
    [FromHeader(Name = NationalIdentityNumberHeaderName)]
    public string? NationalIdentityNumber { get; set; }

    /// <summary>
    /// The last name of the person. This must match the last name of the identified person.
    /// The value is assumed to be base64 encoded from an UTF-8 string.
    /// </summary>
    [FromHeader(Name = LastNameHeaderName)]
    public string? LastName
    {
        get => _lastName;

        set
        {
            const int INLINE_BUFFER_SIZE = 128;

            if (value is null)
            {
                _lastName = null;
                return;
            }

            if (value.Length == 0)
            {
                _lastName = value;
                return;
            }

            if (value.Length > 4096)
            {
                ThrowHelper.ThrowArgumentException(nameof(value), "The value is too long.");
            }

            var decodedMaxLength = Base64.GetMaxDecodedFromUtf8Length(value.Length);
            byte[]? arrayPoolBuffer = null;
            Span<byte> buffer = stackalloc byte[INLINE_BUFFER_SIZE];
            if (decodedMaxLength > INLINE_BUFFER_SIZE)
            {
                arrayPoolBuffer = ArrayPool<byte>.Shared.Rent(decodedMaxLength);
                buffer = arrayPoolBuffer;
            }

            try
            {
                if (!Convert.TryFromBase64String(value, buffer, out int bytesWritten))
                {
                    _lastName = value;
                    return;
                }

                try
                {
                    _lastName = Utf8Strict.GetString(buffer[..bytesWritten]);
                }
                catch (DecoderFallbackException)
                {
                    _lastName = value;
                    return;
                }
            }
            finally
            {
                if (arrayPoolBuffer is not null)
                {
                    ArrayPool<byte>.Shared.Return(arrayPoolBuffer, clearArray: true);
                }
            }
        }
    }
}
