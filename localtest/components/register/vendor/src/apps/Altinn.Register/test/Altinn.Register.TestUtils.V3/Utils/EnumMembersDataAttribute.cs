using System.Collections.Immutable;
using System.Reflection;
using Xunit.Sdk;
using Xunit.v3;

namespace Altinn.Register.TestUtils.Utils;

public class EnumMembersDataAttribute<TEnum>
    : DataAttribute
    where TEnum : struct, Enum
{
    private static readonly ImmutableArray<ITheoryDataRow> _values = [.. Enum.GetValues<TEnum>().Select(e => new TheoryDataRow<TEnum>(e))];

    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        return ValueTask.FromResult<IReadOnlyCollection<ITheoryDataRow>>(_values);
    }

    public override bool SupportsDiscoveryEnumeration()
        => true;
}
