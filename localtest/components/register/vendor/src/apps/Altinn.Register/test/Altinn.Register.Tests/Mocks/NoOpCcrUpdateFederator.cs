using System.Buffers;
using Altinn.Register.Core.Ccr;

namespace Altinn.Register.Tests.Mocks;

internal sealed class NoOpCcrUpdateFederator
    : ICcrUpdateFederator
{
    public Task FederateUpdates(ReadOnlySequence<byte> xmlData, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
