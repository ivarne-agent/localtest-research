using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Altinn.Register.TestUtils.Tracing;

internal sealed class SpanTree
{
    private readonly Lock _lock = new();
    private readonly ActivityTraceId _traceId;
    private readonly SpanNode _root;
    private readonly Dictionary<ActivitySpanId, SpanNode> _spans = new();

    public SpanTree(Activity root)
    {
        _traceId = root.TraceId;
        _root = SpanNode.Root(root);
        _spans.Add(root.SpanId, _root);
    }

    public Stats GetStats()
    {
        DateTimeOffset startTime, endTime;
        ImmutableArray<SpanItem>.Builder items = ImmutableArray.CreateBuilder<SpanItem>(_spans.Count);

        lock (_lock)
        {
            Debug.Assert(_root.Duration is not null);
            startTime = _root.StartTime;
            endTime = startTime + _root.Duration.Value;

            Walk(_root, items, string.Empty, true);
        }

        return new(items.DrainToImmutable(), startTime, endTime);

        static void Walk(SpanNode node, ImmutableArray<SpanItem>.Builder output, string parentPrefix, bool root)
        {
            // Step 1. Get children with durations
            var children = node.DisplayChildren();

            // Step 2. Sort children by start time
            children.Sort(static (a, b) => a.StartTime.CompareTo(b.StartTime));

            // Step 3. Enumerate children
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var isLast = i == children.Count - 1;
                var selfPrefix = isLast ? "╰╴" : "├╴";
                var childPrefix = isLast ? "  " : "│ ";

                var tagString = string.Empty;

                if (!child.Tags.IsDefaultOrEmpty)
                {
                    var tags = string.Join(", ", child.Tags.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                    tagString = $" {{{tags}}}";
                }

                if (root)
                {
                    // parent is root
                    selfPrefix = string.Empty;
                    childPrefix = string.Empty;
                }

                output.Add(new SpanItem
                {
                    Prefix = parentPrefix + selfPrefix,
                    Duration = child.Duration!.Value,
                    StartTime = child.StartTime,
                    EndTime = child.StartTime + child.Duration.Value,
                    Name = $"{child.Name}{tagString} [{child.Source}]",
                    Error = child.Error,
                });

                // Step 4. Recurse
                Walk(child, output, parentPrefix + childPrefix, false);
            }
        }
    }

    public void Started(Activity activity)
    {
        if (activity.TraceId != _traceId)
        {
            return;
        }

        lock (_lock)
        {
            ref var node = ref CollectionsMarshal.GetValueRefOrAddDefault(_spans, activity.SpanId, out var exists);
            if (!exists)
            {
                if (!_spans.TryGetValue(activity.ParentSpanId, out var parent))
                {
                    // clean up the default value we just added
                    _spans.Remove(activity.SpanId);
                    return;
                }

                node = SpanNode.FromActivity(activity);
                parent.AddChild(node);
            }
        }
    }

    public void Stopped(Activity activity)
    {
        if (activity.TraceId != _traceId)
        {
            return;
        }

        lock (_lock)
        {
            if (_spans.TryGetValue(activity.SpanId, out var node))
            {
                node.Update(activity);
            }
        }
    }

    public readonly struct SpanItem
    {
        public required string Prefix { get; init; }

        public required TimeSpan Duration { get; init; }

        public required DateTimeOffset StartTime { get; init; }

        public required DateTimeOffset EndTime { get; init; }

        public required string Name { get; init; }

        public required string? Error { get; init; }
    }

    public class Stats
        : IEnumerable<SpanItem>
    {
        private readonly ImmutableArray<SpanItem> _items;

        public Stats(ImmutableArray<SpanItem> items, DateTimeOffset startTime, DateTimeOffset endTime)
        {
            _items = items;
            Min = startTime;
            Max = endTime;
        }

        public DateTimeOffset Min { get; }

        public DateTimeOffset Max { get; }

        public ImmutableArray<SpanItem>.Enumerator GetEnumerator()
            => _items.GetEnumerator();

        IEnumerator<SpanItem> IEnumerable<SpanItem>.GetEnumerator()
            => ((IEnumerable<SpanItem>)_items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable<SpanItem>)_items).GetEnumerator();
    }

    [DebuggerDisplay("{_name,nq}")]
    internal sealed class SpanNode
    {
        private readonly ActivitySpanId _id;
        private readonly List<SpanNode> _children = new();

        private string? _name;
        private string? _source;
        private DateTimeOffset _startTime;
        private TimeSpan? _duration;
        private ActivityDisplayFilterCollection? _displayFilter;
        private string? _error;

        // tags
        private ImmutableArray<KeyValuePair<string, string>> _tags = [];

        public static SpanNode FromActivity(Activity activity)
        {
            SpanNode span = new(activity.SpanId);
            span.Update(activity);

            return span;
        }

        public DateTimeOffset StartTime => _startTime;

        public TimeSpan? Duration => _duration;

        public string Name => _name ?? string.Empty;

        public string Source => _source ?? string.Empty;

        public string? Error => _error;

        // tags
        public ImmutableArray<KeyValuePair<string, string>> Tags => _tags;

        public static SpanNode Root(Activity activity)
            => new(activity.SpanId);

        private SpanNode(ActivitySpanId id)
        {
            _id = id;
        }

        public void AddChild(SpanNode child)
        {
            _children.Add(child);
        }

        public List<SpanNode> DisplayChildren()
        {
            return _children
                .Where(static c =>
                {
                    if (!c._duration.HasValue)
                    {
                        return false;
                    }

                    if (c._source is null)
                    {
                        return false;
                    }

                    if (ActivityCollector.IsNoisySource(c._source))
                    {
                        return false;
                    }

                    if (c._displayFilter is { } filter && !filter.ShouldDisplay(c))
                    {
                        return false;
                    }

                    return true;
                })
                .ToList();
        }

        public void Update(Activity activity)
        {
            _name = activity.OperationName;
            _source = activity.Source.Name;
            _startTime = activity.StartTimeUtc;
            _duration = activity.Duration;
            _displayFilter = activity.GetDisplayFilterCollection();
            _error = activity.Status == ActivityStatusCode.Error
                ? (activity.StatusDescription ?? "error")
                : null;

            // tags
            var builder = ImmutableArray.CreateBuilder<KeyValuePair<string, string>>();

            if (activity.GetTagItem("retry.count") is int retries)
            {
                builder.Add(KeyValuePair.Create("retries", retries.ToString()));
            }

            if (activity.GetTagItem("job.name") is string jobName)
            {
                builder.Add(KeyValuePair.Create("job-name", jobName));
            }

            _tags = builder.DrainToImmutable();
        }
    }
}
