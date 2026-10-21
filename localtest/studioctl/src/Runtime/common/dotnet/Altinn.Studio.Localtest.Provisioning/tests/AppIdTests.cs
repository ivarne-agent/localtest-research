namespace Altinn.Studio.Localtest.Provisioning.Tests;

public sealed class AppIdTests
{
    [Fact]
    public void ParsesOrgAndApp()
    {
        var id = AppId.Parse("ttd/frontend-test");

        Assert.Equal("ttd", id.Org);
        Assert.Equal("frontend-test", id.App);
        Assert.Equal("ttd/frontend-test", id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ttd")]
    [InlineData("ttd/")]
    [InlineData("/app")]
    public void RejectsAnIdWithoutBothSegments(string value) =>
        Assert.Throws<ArgumentException>(() => AppId.Parse(value));
}
