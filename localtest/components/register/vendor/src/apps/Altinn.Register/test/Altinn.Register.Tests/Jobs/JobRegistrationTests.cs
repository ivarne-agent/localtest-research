using Altinn.Authorization.ServiceDefaults.Jobs;

namespace Altinn.Register.Tests.Jobs;

public class JobRegistrationTests
{
    [Theory]
    [InlineData(typeof(DefaultJobName), nameof(DefaultJobName))]
    [InlineData(typeof(CustomJobName), "i-am-custom-job")]
    public void GetJobNameForType_ReturnsExpectedName(Type type, string expectedName)
    {
        var actualName = JobRegistration.GetJobNameForType(type);

        actualName.ShouldBe(expectedName);
    }

    private class DefaultJobName
    {
    }

    private class CustomJobName
        : IHasJobName<CustomJobName>
    {
        public static string JobName => "i-am-custom-job";
    }
}
