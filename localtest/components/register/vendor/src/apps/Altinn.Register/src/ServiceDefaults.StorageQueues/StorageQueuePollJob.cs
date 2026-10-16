using Altinn.Authorization.ServiceDefaults.Jobs;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues;

/// <summary>
/// A background job that polls an Azure Storage Queue for messages and processes them using a specified message handler.
/// The job completes once the queue is empty or the cancellation token is triggered.
/// </summary>
/// <typeparam name="T">The type of the message handler used to process messages from the queue.</typeparam>
internal sealed partial class StorageQueuePollJob<T>
    : Job<StorageQueuePollJobRunResult>
    where T : class, IStorageQueueMessageHandler
{
    // TODO: configurable?
    private static readonly int MaxRetries = 5;
    private static readonly int BatchSize = 32;
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10); // note: this can be exceeded by jitter
    private static readonly TimeSpan BackoffJitter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InvisibilityTimeout = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<StorageQueuePollJob<T>> _logger;
    private readonly StorageQueueReceiver _receiver;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageQueuePollJob{T}"/> class.
    /// </summary>
    public StorageQueuePollJob(
        StorageQueueReceiver receiver,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<StorageQueuePollJob<T>> logger)
    {
        _receiver = receiver;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task<StorageQueuePollJobRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        Task?[] processingTasks = new Task?[BatchSize];

        uint pagesProcessed = 0;
        await foreach (var messages in _receiver.ReceiveAllMessageAsync(
            batchSize: BatchSize,
            visibilityTimeout: InvisibilityTimeout,
            cancellationToken: cancellationToken))
        {
            Array.Clear(processingTasks);

            // TODO: deal with parallelism better. Instead of doing a batch at a time
            // we want to have a configured degree of parallelism and keep that many
            // tasks running as long as there are messages to process.
            for (int i = 0; i < messages.Length; i++)
            {
                var message = messages[i];
                processingTasks[i] = ProcessMessageAsync(message, cancellationToken);
            }

            await Task.WhenAll(processingTasks.AsSpan(0, messages.Length)!);
            pagesProcessed++;
        }

        return pagesProcessed switch
        {
            0 => StorageQueuePollJobRunResult.NoPages,
            1 => StorageQueuePollJobRunResult.SinglePage,
            _ => StorageQueuePollJobRunResult.MultiplePages,
        };
    }

    private async Task ProcessMessageAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        // Ensure that the caller can continue creating the processingTasks array without waiting for any processing to complete
        await Task.Yield();

        // Check if the message has been retried too many times
        if (message.DequeueCount > MaxRetries)
        {
            // Move the message to the poison queue for later inspection
            await TryMoveToPoisonQueueAsync(_receiver, message, _logger, cancellationToken);
            return;
        }

        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<T>();
            await handler.HandleMessageAsync(message, cancellationToken);
            await _receiver.CompleteMessageAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Operation was cancelled, likely due to shutdown. Let the message become visible again for processing by another instance.
        }
        catch (Exception ex)
        {
            // Log the exception and update visibility with backoff
            Log.MessageFailed(_logger, message.MessageId, ex);

            if (message.DequeueCount == MaxRetries)
            {
                // If this was the last retry, no need to schedule another attempt that will only move it to the poison queue.
                await TryMoveToPoisonQueueAsync(_receiver, message, _logger, cancellationToken);
                return;
            }

            // 30 seconds, 1 minute, 2 minutes, 4 minutes, ...
            var jitter = RandomJitter();
            var backoff = (MinBackoff * Math.Pow(2, message.DequeueCount - 1)) + jitter;
            if (backoff > MaxBackoff)
            {
                backoff = MaxBackoff + jitter;
            }

            await TryRescheduleMAsync(_receiver, message, backoff, _logger, cancellationToken);
        }

        static async Task TryRescheduleMAsync(StorageQueueReceiver receiver, QueueMessage message, TimeSpan backoff, ILogger logger, CancellationToken cancellationToken)
        {
            try
            {
                await receiver.RescheduleMessageAsync(message, backoff, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Operation was cancelled, likely due to shutdown. Let the message become visible again for processing by another instance.
            }
            catch (Exception updateEx)
            {
                // Log the exception but do not rethrow. The message will become visible again after the original invisibility timeout
                Log.VisibilityUpdateFailed(logger, message.MessageId, updateEx);
            }
        }

        static async Task TryMoveToPoisonQueueAsync(StorageQueueReceiver receiver, QueueMessage message, ILogger logger, CancellationToken cancellationToken)
        {
            try
            {
                await receiver.MoveToPoisonQueueAsync(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Operation was cancelled, likely due to shutdown. Let the message become visible again for processing by another instance.
            }
            catch (Exception moveEx)
            {
                // Log the exception but do not rethrow. The message will become visible again after the original invisibility timeout
                Log.MoveToPoisonQueueFailed(logger, message.MessageId, moveEx);
            }
        }
    }

    private static TimeSpan RandomJitter()
    {
        var random = Random.Shared.NextDouble();
        var jitter = random * BackoffJitter.TotalSeconds;
        return TimeSpan.FromSeconds(jitter);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Error, "Failed to process message with ID {MessageId}.")]
        public static partial void MessageFailed(ILogger logger, string messageId, Exception exception);

        [LoggerMessage(1, LogLevel.Error, "Failed to update visibility timeout for message with ID {MessageId}.")]
        public static partial void VisibilityUpdateFailed(ILogger logger, string messageId, Exception exception);

        [LoggerMessage(2, LogLevel.Error, "Failed to move message with ID {MessageId} to the poison queue.")]
        public static partial void MoveToPoisonQueueFailed(ILogger logger, string messageId, Exception exception);
    }
}
