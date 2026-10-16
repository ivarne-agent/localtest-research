using System.Text.Json;
using Altinn.Register.Contracts;
using Altinn.Swashbuckle.Examples;

namespace Altinn.Register.Tests.UnitTests;

public class PersonIdentifierTests
{
    [Theory]
    [InlineData("02013299997")]
    [InlineData("02013299903")]
    [InlineData("02013299911")]
    [InlineData("30108299920")]
    [InlineData("30108299939")]
    [InlineData("30108299947")]
    [InlineData("30108299955")]
    [InlineData("13815897247")]
    [InlineData("42013299980")] // d-number
    [InlineData("03882049433")] // tenor test data
    [InlineData("66847800373")] // tenor test data and d-number
    public void ParsesValidPersonIdentifier(string identifier)
    {
        Assert.True(PersonIdentifier.TryParse(identifier, provider: null, out var result));
        result.ShouldNotBeNull();
        result.ToString().ShouldBe(identifier);

        var result2 = PersonIdentifier.Parse(identifier);
        result2.ShouldNotBeNull();
        result2.ToString().ShouldBe(identifier);

        Assert.True(PersonIdentifier.TryParse(identifier.AsSpan(), provider: null, out var result3));
        result3.ShouldNotBeNull();
        result3.ToString().ShouldBe(identifier);

        var result4 = PersonIdentifier.Parse(identifier.AsSpan());
        result4.ShouldNotBeNull();
        result4.ToString().ShouldBe(identifier);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("123456789101234")]
    [InlineData("1234ab78910")]
    [InlineData("123456-78910")]
    [InlineData("30108299918")] // invalid checksum
    public void DoesNotParseInvalidPersonIdentifier(string identifier)
    {
        Assert.False(PersonIdentifier.TryParse(identifier, provider: null, out _));
        Assert.Throws<FormatException>(() => PersonIdentifier.Parse(identifier));
        Assert.False(PersonIdentifier.TryParse(identifier.AsSpan(), provider: null, out _));
        Assert.Throws<FormatException>(() => PersonIdentifier.Parse(identifier.AsSpan()));
    }

    [Fact]
    public void Equality()
    {
        var str1 = "02013299997";
        var str2 = "30108299955";
        var id1 = PersonIdentifier.Parse(str1);
        var id2 = PersonIdentifier.Parse(str2);

#pragma warning disable CS1718 // Comparison made to same variable
        (str1 == id1).ShouldBeTrue();
        (id1 == str1).ShouldBeTrue();
        (id1 == id1).ShouldBeTrue();
        (str1 != id1).ShouldBeFalse();
        (id1 != str1).ShouldBeFalse();
        (id1 != id1).ShouldBeFalse();
        id1.Equals(id1).ShouldBeTrue();
        id1.Equals(str1).ShouldBeTrue();
        id1.Equals((object)id1).ShouldBeTrue();
        id1.Equals((object)str1).ShouldBeTrue();

        (str2 == id1).ShouldBeFalse();
        (id1 == str2).ShouldBeFalse();
        (id1 == id2).ShouldBeFalse();
        (str2 != id1).ShouldBeTrue();
        (id1 != str2).ShouldBeTrue();
        (id1 != id2).ShouldBeTrue();
        id1.Equals(id2).ShouldBeFalse();
        id1.Equals(str2).ShouldBeFalse();
        id1.Equals((object)id2).ShouldBeFalse();
        id1.Equals((object)str2).ShouldBeFalse();
#pragma warning restore CS1718 // Comparison made to same variable
    }

    [Fact]
    public void TryFormat()
    {
        var id = PersonIdentifier.Parse("02013299911");

        Span<char> span = stackalloc char[20];
        int written;

        // too short
        span.Fill(' ');
        id.TryFormat(span[..5], out written, [], provider: null).ShouldBeFalse();

        // too long
        span.Fill(' ');
        id.TryFormat(span, out written, [], provider: null).ShouldBeTrue();
        written.ShouldBe(11);
        new string(span[..written]).ShouldBe(id.ToString());

        // exact length
        span.Fill(' ');
        id.TryFormat(span[0..11], out written, [], provider: null).ShouldBeTrue();
        written.ShouldBe(11);
        new string(span[..written]).ShouldBe(id.ToString());
    }

    [Fact]
    public void JsonRoundTrip()
    {
        var id = PersonIdentifier.Parse("02013299911");

        var json = JsonSerializer.SerializeToDocument(id);
        json.RootElement.ValueKind.ShouldBe(JsonValueKind.String);
        json.RootElement.GetString().ShouldBe(id.ToString());

        var parsed = json.Deserialize<PersonIdentifier>();
        Assert.NotNull(parsed);

        parsed.ShouldBe(id);
    }

    [Fact]
    public void BadJsonValue()
    {
        var json = "\"1234\"";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PersonIdentifier>(json));
    }

    [Fact]
    public void CanGet_ExampleData()
    {
        var examples = ExampleData.GetExamples<PersonIdentifier>()?.ToList();
        examples.ShouldNotBeNull();
        examples.ShouldNotBeEmpty();
    }
}
