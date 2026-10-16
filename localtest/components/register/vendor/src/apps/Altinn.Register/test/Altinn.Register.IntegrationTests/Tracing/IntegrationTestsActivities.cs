using System.Diagnostics;

namespace Altinn.Register.IntegrationTests.Tracing;

internal class IntegrationTestsActivities
{
    public static ActivitySource Source { get; } = new("Altinn.Register.IntegrationTests");
}
