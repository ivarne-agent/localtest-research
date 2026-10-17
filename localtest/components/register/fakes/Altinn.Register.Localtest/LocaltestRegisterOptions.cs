namespace Altinn.Register.Localtest;

/// <summary>
/// Settings for the localtest Register fakes, bound from <c>Localtest:Register</c>.
/// </summary>
public sealed class LocaltestRegisterOptions
{
    /// <summary>Path to the <c>testData.json</c> that seeds the in-memory register.</summary>
    public string TestDataPath { get; set; } = "/testdata/testData.json";


    /// <summary>
    /// Base URL apps are reached on; an app's test data is fetched from <c>{AppsBaseUrl}{org}/{app}/testData.json</c>.
    /// </summary>
    public string AppsBaseUrl { get; set; } = "http://local.altinn.cloud:8000/";

    /// <summary>Comma-separated <c>org/app</c> list registered at startup, for example <c>ttd/frontend-test</c>.</summary>
    public string? Apps { get; set; }
}
