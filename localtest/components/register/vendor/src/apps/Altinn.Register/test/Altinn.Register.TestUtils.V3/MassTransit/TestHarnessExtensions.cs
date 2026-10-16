using Altinn.Authorization.ServiceDefaults.MassTransit;
using MassTransit.Testing;

namespace Altinn.Register.TestUtils.MassTransit;

/// <summary>
/// Extension methods for <see cref="ITestHarness"/>.
/// </summary>
public static class TestHarnessExtensions
{
    /// <summary>
    /// Gets a conversation helper by it's conversation ID.
    /// </summary>
    /// <param name="harness">The <see cref="ITestHarness"/>.</param>
    /// <param name="guid">The conversation ID.</param>
    /// <returns>A <see cref="TestHarnessConversation"/>.</returns>
    public static TestHarnessConversation Conversation(this ITestHarness harness, Guid guid, CancellationToken cancellationToken = default)
        => new(harness, guid, cancellationToken);

    /// <summary>
    /// Gets a conversation helper by the initial command.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="harness">The <see cref="ITestHarness"/>.</param>
    /// <param name="command">The initial command that started the conversation.</param>
    /// <returns>A <see cref="TestHarnessConversation"/>.</returns>
    public static Task<TestHarnessConversation> Conversation<TCommand>(
        this ITestHarness harness,
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : CommandBase
            => harness.Conversation<TCommand>(cmd => cmd.CommandId == command.CommandId, cancellationToken);

    /// <summary>
    /// Gets a conversation helper by the initial command.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="harness">The <see cref="ITestHarness"/>.</param>
    /// <param name="commandPredicate">The initial command predicate to match against the command that started the conversation.</param>
    /// <returns>A <see cref="TestHarnessConversation"/>.</returns>
    public static async Task<TestHarnessConversation> Conversation<TCommand>(
        this ITestHarness harness,
        Predicate<TCommand> commandPredicate,
        CancellationToken cancellationToken = default)
        where TCommand : CommandBase
    {
        var consumed = await harness.Sent.SelectExisting(m => m.MessageObject is TCommand cmd && commandPredicate(cmd)).Cast<ISentMessage<TCommand>>().FirstOrDefaultAsync(cancellationToken);
        if (consumed is null)
        {
            Assert.Fail("Consumed message not found");
        }

        return harness.Conversation(consumed.Context.ConversationId!.Value, cancellationToken);
    }
}
