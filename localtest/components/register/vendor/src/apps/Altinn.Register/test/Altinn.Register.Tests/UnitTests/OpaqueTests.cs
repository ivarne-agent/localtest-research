using System.Text.Json;
using Altinn.Register.Models;

namespace Altinn.Register.Tests.UnitTests;

public class OpaqueTests
{
    public static TheoryData<string> StringData => new()
    {
        string.Empty,
        "f",
        "fo",
        "foo",
        "ÿïø øû迸",
    };

    [Theory]
    [MemberData(nameof(StringData))]
    public void RoundTrips(string s)
    {
        var data = new Opaque<string>(s);
        var serialized = data.ToString();
        var parsed = Opaque<string>.Parse(serialized, null);

        parsed.Value.ShouldBe(s);
    }

    [Theory]
    [MemberData(nameof(StringData))]
    public void RoundTripsJson(string s)
    {
        var data = new Opaque<string>(s);
        var json = JsonSerializer.Serialize(data);
        var parsed = JsonSerializer.Deserialize<Opaque<string>>(json);

        Assert.NotNull(parsed);
        parsed.Value.ShouldBe(s);
    }
}
