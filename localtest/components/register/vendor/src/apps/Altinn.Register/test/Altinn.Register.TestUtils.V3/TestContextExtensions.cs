using CommunityToolkit.Diagnostics;

namespace Altinn.Register.TestUtils;

/// <summary>
/// Extensions for <see cref="ITestContext"/>.
/// </summary>
public static class TestContextExtensions
{
    public static async ValueTask<T> GetRequiredFixture<T>(this ITestContext testContext)
    {
        var fixture = await testContext.GetFixture<T>();
        if (fixture is null)
        {
            ThrowHelper.ThrowInvalidOperationException($"Missing required fixture of type {typeof(T).Name}.");
        }

        return fixture;
    }
}
