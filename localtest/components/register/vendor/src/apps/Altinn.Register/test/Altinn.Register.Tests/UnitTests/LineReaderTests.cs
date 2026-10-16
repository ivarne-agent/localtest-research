using System.Buffers;
using System.IO.Pipelines;
using Altinn.Register.Integrations.Ccr.FileImport;
using Altinn.Register.Tests.Utils;
using CommunityToolkit.Diagnostics;
using Nerdbank.Streams;

namespace Altinn.Register.Tests.UnitTests;

public class LineReaderTests
{
    [Theory]
    [CombinatorialData]
    public async Task EmptyInput(TestDataMode mode)
    {
        await using var reader = CreateReader(mode, ""u8);

        var result = await reader.ReadNext(CancellationToken);
        result.ShouldBeFalse();
        reader.Line.Length.ShouldBe(0);
    }

    [Theory]
    [CombinatorialData]
    public async Task NoLineBreaks(TestDataMode mode)
    {
        await using var reader = CreateReader(mode, "foobar"u8);

        var result = await reader.ReadNext(CancellationToken);
        result.ShouldBeTrue();
        reader.Line.SequenceEqual("foobar"u8).ShouldBeTrue();

        result = await reader.ReadNext(CancellationToken);
        result.ShouldBeFalse();
        reader.Line.Length.ShouldBe(0);
    }

    [Theory]
    [CombinatorialData]
    public async Task EndsWithSingleByteNewline(
        TestDataMode mode,
        [CombinatorialValues((byte)'\n', (byte)'\r')] byte newline)
    {
        byte[] data = [.. "foobar"u8, newline];
        await using var reader = CreateReader(mode, data);

        var result = await reader.ReadNext(CancellationToken);
        result.ShouldBeTrue();
        reader.Line.SequenceEqual("foobar"u8).ShouldBeTrue();

        result = await reader.ReadNext(CancellationToken);
        result.ShouldBeFalse();
        reader.Line.Length.ShouldBe(0);
    }

    [Theory]
    [CombinatorialData]
    public async Task EndsWithCrlf(TestDataMode mode)
    {
        await using var reader = CreateReader(mode, "foobar\r\n"u8);

        var result = await reader.ReadNext(CancellationToken);
        result.ShouldBeTrue();
        reader.Line.SequenceEqual("foobar"u8).ShouldBeTrue();

        result = await reader.ReadNext(CancellationToken);
        result.ShouldBeFalse();
        reader.Line.Length.ShouldBe(0);
    }

    [Theory]
    [CombinatorialData]
    public async Task MultipleLines(TestDataMode mode, LineBreak lineBreak)
    {
        await using var reader = CreateReader(
            mode,
            lineBreak,
            static write =>
            {
                write("foo"u8);
                write("bar"u8);
            });

        var result = await reader.ReadNext(CancellationToken);
        result.ShouldBeTrue();
        reader.Line.SequenceEqual("foo"u8).ShouldBeTrue();

        result = await reader.ReadNext(CancellationToken);
        result.ShouldBeTrue();
        reader.Line.SequenceEqual("bar"u8).ShouldBeTrue();

        result = await reader.ReadNext(CancellationToken);
        result.ShouldBeFalse();
        reader.Line.Length.ShouldBe(0);
    }

    [Theory]
    [CombinatorialData]
    public async Task LineBreaksOnly(TestDataMode mode)
    {
        await using var reader = CreateReader(mode, "\n\r\n\r\r\r\n\n\n"u8);

        bool result;
        for (var i = 0; i < 7; i++)
        {
            result = await reader.ReadNext(CancellationToken);
            result.ShouldBeTrue();
            reader.Line.Length.ShouldBe(0);
        }

        result = await reader.ReadNext(CancellationToken);
        result.ShouldBeFalse();
        reader.Line.Length.ShouldBe(0);
    }

    private static LineReader CreateReader(TestDataMode mode, ReadOnlySpan<byte> data)
    {
        using Sequence<byte> writer = new(ArrayPool<byte>.Shared);
        writer.Write(data);
        return CreateReader(mode, writer.AsReadOnlySequence);
    }

    private static LineReader CreateReader(TestDataMode mode, ReadOnlySequence<byte> data)
    {
        var seq = CreateSequence(mode, data);
        var pipeReader = mode switch
        {
            TestDataMode.SingleSegment => PipeReader.Create(seq),
            TestDataMode.MultiSegment => new SegmentBySegmentPipeReader(seq),
            _ => ThrowHelper.ThrowArgumentOutOfRangeException<PipeReader>(nameof(mode)),
        };
        return new LineReader(pipeReader);
    }

    private static LineReader CreateReader(
        TestDataMode mode,
        LineBreak lineBreak,
        Action<Action<ReadOnlySpan<byte>>> writeLine)
    {
        using Sequence<byte> writer = new(ArrayPool<byte>.Shared);
        writeLine(line =>
        {
            ReadOnlySpan<byte> lineBreakBytes = lineBreak switch
            {
                LineBreak.Lf => "\n"u8,
                LineBreak.Cr => "\r"u8,
                LineBreak.CrLf => "\r\n"u8,
                _ => throw new ArgumentOutOfRangeException(paramName: nameof(lineBreak)),
            };

            writer.Write(line);
            writer.Write(lineBreakBytes);
        });

        return CreateReader(mode, writer.AsReadOnlySequence);
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

    private static CancellationToken CancellationToken
        => TestContext.Current.CancellationToken;

    public enum TestDataMode
    {
        SingleSegment,
        MultiSegment,
    }

    public enum LineBreak
    {
        Lf,
        Cr,
        CrLf,
    }

    internal sealed class BufferSegment
        : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new BufferSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = segment;
            return segment;
        }
    }
}
