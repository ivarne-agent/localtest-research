using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Altinn.Register.TestUtils.Tracing;

public static class TestUtilsActivities
{
    private static readonly string PropertyDisplayFilter = $"{nameof(TestUtilsActivities)}:display-filter";

    internal static ActivitySource Source { get; } = new("Altinn.Register.TestUtils");

    internal static ActivityDisplayFilterCollection? GetDisplayFilterCollection(this Activity activity)
        => activity.GetCustomProperty(PropertyDisplayFilter) as ActivityDisplayFilterCollection;

    [return: NotNullIfNotNull(nameof(activity))]
    private static Activity? AddDisplayFilter(this Activity? activity, SpanDisplayFilter filter)
    {
        if (activity is null)
        {
            return null;
        }

        if (activity.GetDisplayFilterCollection() is not { } filters)
        {
            filters = new();
            activity.SetCustomProperty(PropertyDisplayFilter, filters);
        }

        filters.Add(filter);
        return activity;
    }

    [return: NotNullIfNotNull(nameof(activity))]
    public static Activity? HideIfShorterThan(this Activity? activity, TimeSpan timeSpan)
        => activity.AddDisplayFilter(a => a.Duration is not null && a.Duration >= timeSpan);
}
