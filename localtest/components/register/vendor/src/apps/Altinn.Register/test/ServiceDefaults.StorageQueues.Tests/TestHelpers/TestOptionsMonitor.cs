using Microsoft.Extensions.Options;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues.Tests.TestHelpers;

internal sealed class TestOptionsMonitor<TOptions>(Func<string, TOptions> get)
    : IOptionsMonitor<TOptions>
{
    public TOptions CurrentValue => Get(Options.DefaultName);

    public TOptions Get(string? name)
        => get(name ?? Options.DefaultName);

    public IDisposable? OnChange(Action<TOptions, string?> listener)
        => null;
}
