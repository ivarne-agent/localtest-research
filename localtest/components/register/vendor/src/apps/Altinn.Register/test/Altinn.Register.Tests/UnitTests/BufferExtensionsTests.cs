using System.Buffers;
using System.Buffers.Text;
using Altinn.Register.Core.Utils;
using CommunityToolkit.Diagnostics;
using static Altinn.Register.Tests.UnitTests.LineReaderTests;

namespace Altinn.Register.Tests.UnitTests;

public class BufferExtensionsTests
{
    [Theory]
    [CombinatorialData]
    public void EncodeToString_EncodesEmptySequence(TestDataMode mode)
    {
        ReadOnlySpan<byte> data = [];
        var sequence = CreateSequence(mode, data);

        var encoded = Base64Url.EncodeToString(sequence);

        encoded.ShouldBe(Base64Url.EncodeToString(data));
    }

    [Theory]
    [CombinatorialData]
    public void EncodeToString_EncodesAsciiData(TestDataMode mode)
    {
        ReadOnlySpan<byte> data = "The quick brown fox jumps over the lazy dog."u8;
        var sequence = CreateSequence(mode, data);

        var encoded = Base64Url.EncodeToString(sequence);

        encoded.ShouldBe(Base64Url.EncodeToString(data));
    }

    [Theory]
    [CombinatorialData]
    public void EncodeToString_EncodesBinaryData(TestDataMode mode)
    {
        ReadOnlySpan<byte> data = [0x00, 0x01, 0x02, 0x03, 0x7F, 0x80, 0xFE, 0xFF];
        var sequence = CreateSequence(mode, data);

        var encoded = Base64Url.EncodeToString(sequence);

        encoded.ShouldBe(Base64Url.EncodeToString(data));
    }

    public enum TestDataMode
    {
        SingleSegment,
        MultiSegment,
    }

    private static ReadOnlySequence<byte> CreateSequence(TestDataMode mode, ReadOnlySpan<byte> data)
    {
        var rented = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(rented);
        var sourceSeq = new ReadOnlySequence<byte>(rented, 0, data.Length);
        var result = CreateSequence(mode, sourceSeq);
        ArrayPool<byte>.Shared.Return(rented);

        return result;
    }

    private static ReadOnlySequence<byte> CreateSequence(TestDataMode mode, ReadOnlySequence<byte> data)
    {
        return mode switch
        {
            TestDataMode.SingleSegment => CreateSingleSegmentSequence(data),
            TestDataMode.MultiSegment => CreateMultiSegmentSequence(data),
            _ => ThrowHelper.ThrowArgumentOutOfRangeException<ReadOnlySequence<byte>>(nameof(mode)),
        };

        static ReadOnlySequence<byte> CreateSingleSegmentSequence(ReadOnlySequence<byte> data)
        {
            var bytes = data.ToArray();
            return new ReadOnlySequence<byte>(bytes);
        }

        static ReadOnlySequence<byte> CreateMultiSegmentSequence(ReadOnlySequence<byte> data)
        {
            var firstSegment = new BufferSegment(Array.Empty<byte>());
            var lastSegment = firstSegment;

            foreach (var segment in data)
            {
                foreach (var b in segment.Span)
                {
                    lastSegment = lastSegment.Append(new byte[] { b });
                }
            }

            // this is pathological, but we add an empty buffer at the end to ensure that we support the scenario
            lastSegment = lastSegment.Append(Array.Empty<byte>());

            return new ReadOnlySequence<byte>(firstSegment, 0, lastSegment, 0);
        }
    }
}
