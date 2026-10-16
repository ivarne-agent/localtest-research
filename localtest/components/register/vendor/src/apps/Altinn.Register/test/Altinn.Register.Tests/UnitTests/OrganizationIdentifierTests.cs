using System.Text.Json;
using Altinn.Register.Contracts;
using Altinn.Swashbuckle.Examples;

namespace Altinn.Register.Tests.UnitTests;

public class OrganizationIdentifierTests
{
    [Theory]
    [InlineData("010101018")]
    [InlineData("000000000")]
    [InlineData("123456785")]
    public void ParsesValidOrganizationIdentifier(string identifier)
    {
        Assert.True(OrganizationIdentifier.TryParse(identifier, provider: null, out var result));
        result.ShouldNotBeNull();
        result.ToString().ShouldBe(identifier);

        var result2 = OrganizationIdentifier.Parse(identifier);
        result2.ShouldNotBeNull();
        result2.ToString().ShouldBe(identifier);

        Assert.True(OrganizationIdentifier.TryParse(identifier.AsSpan(), provider: null, out var result3));
        result3.ShouldNotBeNull();
        result3.ToString().ShouldBe(identifier);

        var result4 = OrganizationIdentifier.Parse(identifier.AsSpan());
        result4.ShouldNotBeNull();
        result4.ToString().ShouldBe(identifier);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("123456789101234")]
    [InlineData("1234ab789")]
    [InlineData("123456-789")]
    [InlineData("010101010")] // invalid checksum
    [InlineData("123456789")] // invalid checksum
    public void DoesNotParseInvalidPersonIdentifier(string identifier)
    {
        Assert.False(OrganizationIdentifier.TryParse(identifier, provider: null, out _));
        Assert.Throws<FormatException>(() => OrganizationIdentifier.Parse(identifier));
        Assert.False(OrganizationIdentifier.TryParse(identifier.AsSpan(), provider: null, out _));
        Assert.Throws<FormatException>(() => OrganizationIdentifier.Parse(identifier.AsSpan()));
    }

    [Fact]
    public void Equality()
    {
        var str1 = "123456785";
        var str2 = "109876542";
        var id1 = OrganizationIdentifier.Parse(str1);
        var id2 = OrganizationIdentifier.Parse(str2);

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
        var id = OrganizationIdentifier.Parse("123456785");

        Span<char> span = stackalloc char[20];
        int written;

        // too short
        span.Fill(' ');
        id.TryFormat(span[..5], out written, [], provider: null).ShouldBeFalse();

        // too long
        span.Fill(' ');
        id.TryFormat(span, out written, [], provider: null).ShouldBeTrue();
        written.ShouldBe(9);
        new string(span[..written]).ShouldBe(id.ToString());

        // exact length
        span.Fill(' ');
        id.TryFormat(span[0..9], out written, [], provider: null).ShouldBeTrue();
        written.ShouldBe(9);
        new string(span[..written]).ShouldBe(id.ToString());
    }

    [Fact]
    public void JsonRoundTrip()
    {
        var id = OrganizationIdentifier.Parse("123456785");

        var json = JsonSerializer.SerializeToDocument(id);
        json.RootElement.ValueKind.ShouldBe(JsonValueKind.String);
        json.RootElement.GetString().ShouldBe(id.ToString());

        var parsed = json.Deserialize<OrganizationIdentifier>();
        Assert.NotNull(parsed);

        parsed.ShouldBe(id);
    }

    [Fact]
    public void BadJsonValue()
    {
        var json = "\"1234\"";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OrganizationIdentifier>(json));
    }

    [Fact]
    public void CanGet_ExampleData()
    {
        var examples = ExampleData.GetExamples<OrganizationIdentifier>()?.ToList();
        examples.ShouldNotBeNull();
        examples.ShouldNotBeEmpty();
    }
}
