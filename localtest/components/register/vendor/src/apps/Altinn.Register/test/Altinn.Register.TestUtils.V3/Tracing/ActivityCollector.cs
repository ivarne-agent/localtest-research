using System.Diagnostics;
using System.Text;

namespace Altinn.Register.TestUtils.Tracing;

internal sealed class ActivityCollector
    : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly Activity _activity;
    private readonly SpanTree _tree;

    public ActivityCollector()
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = Sample,
        };

        // Note: order in which things are configured in this class is very important.
        // Don't change it unless you know what you're doing.
        ActivitySource.AddActivityListener(_listener);

        var testContext = TestContext.Current;
        var activityName = testContext.Test?.TestDisplayName switch
        {
            null => "test-context missing",
            string name => $"test {name}",
        };
        _activity = TestUtilsActivities.Source.StartActivity(ActivityKind.Internal, name: activityName)!;

        if (testContext.TestCase is { } testCase)
        {
            _activity.SetTag("test.case", testCase.TestCaseDisplayName);
        }

        if (testContext.TestClass is { } testClass)
        {
            _activity.SetTag("test.class", testClass.TestClassName);
        }

        if (testContext.TestAssembly is { } testAssembly)
        {
            _activity.SetTag("test.assembly", testAssembly.AssemblyName);
        }

        Assert.NotNull(_activity);
        Assert.Null(_activity.Parent);

        _tree = new SpanTree(_activity);

        Thread.MemoryBarrier();

        _listener.ActivityStarted = ActivityStarted;
        _listener.ActivityStopped = ActivityStopped;
    }

    private void ActivityStarted(Activity activity)
    {
        _tree.Started(activity);
    }

    private void ActivityStopped(Activity activity)
    {
        _tree.Stopped(activity);
    }

    private ActivitySamplingResult Sample(ref ActivityCreationOptions<ActivityContext> options)
    {
        // Warning: the first time this is called, _activity is not yet created
        if (options.Parent == default && options.Source == TestUtilsActivities.Source)
        {
            // root activity created in constructor
            return ActivitySamplingResult.AllDataAndRecorded;
        }

        if (IsNoise(in options))
        {
            return ActivitySamplingResult.None;
        }

        if (_activity is null)
        {
            // test-activity not yet created, definitely not a child
            return ActivitySamplingResult.None;
        }

        if (options.TraceId == _activity.TraceId)
        {
            // activity created by the test
            return ActivitySamplingResult.AllDataAndRecorded;
        }

        if (options.Links is { } links
            && links.Any(l => l.Context.TraceId == _activity.TraceId))
        {
            // activity linked to the test
            return ActivitySamplingResult.AllDataAndRecorded;
        }

        return ActivitySamplingResult.None;

        static bool IsNoise(in ActivityCreationOptions<ActivityContext> options)
            => IsNoisySource(options.Source.Name);
    }

    public void Dispose()
    {
        const int CHART_WIDTH = 50;

        _activity?.Dispose();
        _listener.Dispose();

        var stats = _tree.GetStats();
        var low = stats.Min;
        var high = stats.Max;
        var totalDuration = high - low;

        var sb = new StringBuilder();
        foreach (var item in stats)
        {
            var offset = (int)(CHART_WIDTH * ((item.StartTime - low).TotalMilliseconds / totalDuration.TotalMilliseconds));
            var length = (int)Math.Max(CHART_WIDTH * (item.Duration.TotalMilliseconds / totalDuration.TotalMilliseconds), 1);

            sb.Append(' ', offset);
            sb.Append(length > 1 ? '\x2590' : '\x258D');

            if (length > 2)
            {
                sb.Append('\x2592', length - 2);
            }

            if (length > 1)
            {
                sb.Append('\x258D');
            }

            sb.Append(' ', CHART_WIDTH - offset - length + 1);

            sb.Append(item.Prefix);
            sb.Append(item.Name).Append(" (");
            item.Duration.ToFriendlyString(sb);
            sb.Append(')');

            if (item.Error is { } error)
            {
                sb.Append(": ").Append(error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault("error"));
            }

            sb.AppendLine();
        }

        TestContext.Current.TestOutputHelper!.WriteLine(sb.ToString());
    }

    public static bool IsNoisySource(string sourceName)
    {
        return sourceName == "Experimental.System.Net.Sockets"
            || sourceName == "Npgsql";
    }
}
