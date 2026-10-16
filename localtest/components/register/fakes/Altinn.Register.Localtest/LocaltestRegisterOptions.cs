namespace Altinn.Register.Localtest;

/// <summary>
/// Settings for the localtest Register fakes, bound from <c>Localtest:Register</c>.
/// </summary>
public sealed class LocaltestRegisterOptions
{
    /// <summary>Path to the <c>testData.json</c> that seeds the in-memory register.</summary>
    public string TestDataPath { get; set; } = "/testdata/testData.json";

    /// <summary>
    /// When true, requests that need platform access must carry a <c>PlatformAccessToken</c> header
    /// (any value). When false every platform-access requirement succeeds.
    /// </summary>
    public bool RequirePlatformAccessTokenHeader { get; set; } = true;
}
