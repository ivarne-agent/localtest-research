using Microsoft.Extensions.Options;

namespace Altinn.Register.Tests.Utils;

/// <summary>
/// Lightweight <see cref="IOptionsMonitor{TOptions}"/> for tests, returning a single configured
/// value under a single configured name. <see cref="Get(string?)"/> throws on an unexpected name
/// so misconfigured callers fail loudly rather than silently receiving a default instance.
/// </summary>
internal sealed class TestOptionsMonitor<TOptions>
    : IOptionsMonitor<TOptions>
{
    private readonly string _name;
    private readonly TOptions _value;

    public TestOptionsMonitor(string name, TOptions value)
    {
        _name = name;
        _value = value;
    }

    public TOptions CurrentValue => _value;

    public TOptions Get(string? name)
    {
        if (!string.Equals(name, _name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected options name: '{name}' (expected '{_name}').");
        }

        return _value;
    }

    public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
}
