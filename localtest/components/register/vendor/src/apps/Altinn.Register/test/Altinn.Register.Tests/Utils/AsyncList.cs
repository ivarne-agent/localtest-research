using System.Collections;

namespace Altinn.Register.Tests.Utils;

internal class AsyncList<T>
    : IAsyncEnumerable<T>
    , IEnumerable<T>
{
    private readonly bool _yieldBeforeItems;
    private readonly List<T> _list = new();

    public AsyncList(bool yieldBeforeItems = true)
    {
        _yieldBeforeItems = yieldBeforeItems;
    }

    public AsyncList(List<T> values, bool yieldBeforeItems = true)
        : this(yieldBeforeItems)
    {
        _list = values;
    }

    public AsyncList(IEnumerable<T> values)
    {
        _list = values.ToList();
    }

    public void Add(T value)
    {
        _list.Add(value);
    }

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        foreach (var item in _list)
        {
            if (_yieldBeforeItems)
            {
                await Task.Yield();
            }

            yield return item;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        return _list.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
