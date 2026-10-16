using System.Collections.Immutable;
using System.Reflection;
using Xunit.Sdk;
using Xunit.v3;

namespace Altinn.Register.Persistence.Tests.Utils;

public class EnumMembersDataAttribute<TEnum>
    : DataAttribute
    where TEnum : struct, Enum
{
    private static readonly ImmutableArray<ITheoryDataRow> _values = [.. Enum.GetValues<TEnum>().Select(static value => new TheoryDataRow<TEnum>(value))];

    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
        => ValueTask.FromResult<IReadOnlyCollection<ITheoryDataRow>>(_values);

    public override bool SupportsDiscoveryEnumeration()
        => true;
}
