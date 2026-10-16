namespace Altinn.Register.TestUtils.Traits;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class IntegrationTestAttribute()
    : CategoryAttribute(KnownCategories.IntegrationTest)
{
}
