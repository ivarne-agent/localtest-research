using CommunityToolkit.Diagnostics;
using MassTransit;

namespace Altinn.Register.PartyImport.SystemUser;

/// <summary>
/// Consumer for the <see cref="ImportSystemUserCommand"/>.
/// </summary>
public sealed class ImportSystemUserConsumer
    : IConsumer<ImportSystemUserCommand>
{
    /// <inheritdoc/>
    public Task Consume(ConsumeContext<ImportSystemUserCommand> context)
    {
        ThrowHelper.ThrowInvalidOperationException("Not implemented yet for failing system users.");
        return Task.CompletedTask;
    }
}
