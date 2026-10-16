using System.Globalization;
using Altinn.Register.Core.Utils;

namespace Altinn.Register.Tests.UnitTests;

public class ByteSizeTests
{
    [Theory]
    [MemberData(nameof(FormatCases))]
    public void FormatsCorrectly(ByteSize value, string expected)
    {
        Span<char> buffer = new char[256];
        var actualString = value.ToString(null, CultureInfo.InvariantCulture);
        var success = value.TryFormat(buffer, out var written, format: null, provider: CultureInfo.InvariantCulture);

        actualString.ShouldBe(expected);

        success.ShouldBeTrue();
        written.ShouldBe(expected.Length);
        new string(buffer[..written]).ShouldBe(expected);
    }

    public static TheoryData<ByteSize, string> FormatCases()
        => new()
        {
            // Zero
            { ByteSize.Zero, "0 B" },

            // One
            { ByteSize.Byte, "1 B" },
            { ByteSize.Kibibyte, "1 KiB" },
            { ByteSize.Mebibyte, "1 MiB" },
            { ByteSize.Gibibyte, "1 GiB" },
            { ByteSize.Tebibyte, "1 TiB" },

            // Whole multipliers
            { ByteSize.FromBytes(5), "5 B" },
            { ByteSize.FromKibibytes(5), "5 KiB" },
            { ByteSize.FromMebibytes(5), "5 MiB" },
            { ByteSize.FromGibibytes(5), "5 GiB" },
            { ByteSize.FromTebibytes(5), "5 TiB" },

            // Fractional multipliers
            { ByteSize.FromBytes((1024 * 2) + 512), "2.5 KiB" },
            { ByteSize.FromKibibytes((1024 * 2) + 512), "2.5 MiB" },
            { ByteSize.FromMebibytes((1024 * 2) + 512), "2.5 GiB" },
            { ByteSize.FromGibibytes((1024 * 2) + 512), "2.5 TiB" },
        };
}
