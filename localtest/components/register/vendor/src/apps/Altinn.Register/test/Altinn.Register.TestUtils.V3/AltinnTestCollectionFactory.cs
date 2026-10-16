using Xunit.v3;

namespace Altinn.Register.TestUtils;

public sealed class AltinnTestCollectionFactory(IXunitTestAssembly testAssembly)
    : TestCollectionFactoryBase(testAssembly)
{
    public const int MaxConcurrency = 10;

    private readonly XunitTestCollection _defaultCollection
        = new XunitTestCollection(
            testAssembly,
            collectionDefinition: null,
            disableParallelization: true,
            "Test collection for " + testAssembly.AssemblyName);

    /// <inheritdoc/>
    public override string DisplayName
        => "altinn-register";

    /// <inheritdoc/>
    protected override IXunitTestCollection GetDefaultTestCollection(Type testClass)
    {
        if (testClass.IsDefined(typeof(RunTestsSeriallyAttribute), inherit: false))
        {
            return _defaultCollection;
        }

        return new XunitTestCollection(
            TestAssembly,
            collectionDefinition: null,
            disableParallelization: false,
            CollectionAttribute.GetCollectionNameForType(testClass));
    }
}
