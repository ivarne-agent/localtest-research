namespace Altinn.Register.TestUtils.Tracing;

internal sealed class ActivityDisplayFilterCollection
{
    private readonly List<SpanDisplayFilter> _filters = new();

    public void Add(SpanDisplayFilter filter) => _filters.Add(filter);

    internal bool ShouldDisplay(SpanTree.SpanNode c)
    {
        foreach (var filter in _filters)
        {
            if (!filter(c))
            {
                return false;
            }
        }

        return true;
    }
}

internal delegate bool SpanDisplayFilter(SpanTree.SpanNode node);
