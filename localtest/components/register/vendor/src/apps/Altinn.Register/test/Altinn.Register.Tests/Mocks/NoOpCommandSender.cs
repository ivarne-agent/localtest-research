using Altinn.Authorization.ServiceDefaults.MassTransit;

namespace Altinn.Register.Tests.Mocks;

/// <summary>
/// No-op <see cref="ICommandSender"/> for tests that build the host without MassTransit.
/// </summary>
internal sealed class NoOpCommandSender : ICommandSender
{
    public Task Send<T>(T command, CancellationToken cancellationToken = default)
        where T : CommandBase
        => Task.CompletedTask;

    public Task Send<T>(IEnumerable<T> commands, CancellationToken cancellationToken = default)
        where T : CommandBase
        => Task.CompletedTask;
}
