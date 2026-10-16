using System.ComponentModel.DataAnnotations;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues;

/// <summary>
/// Represents the settings required to configure a storage queue job, including the storage account name, queue name, and poison queue name.
/// </summary>
public sealed class StorageQueueSettings
    : IValidatableObject
{
    private const string QueueNamePattern = "^[a-z0-9](?:-?[a-z0-9]){2,62}$";

    /// <summary>
    /// Gets the name of the azure identity to use when authenticating to the storage account.
    /// </summary>
    public string IdentityName { get; set; } = "storage";

    /// <summary>
    /// Gets the uri of the storage account containing the queue and poison queue.
    /// </summary>
    [Required]
    public Uri? StorageAccountUri { get; set; }

    /// <summary>
    /// Gets or sets the name of the queue to listen to for messages. The queue must exist in the specified storage account.
    /// </summary>
    [Required]
    [RegularExpression(QueueNamePattern)]
    public string? QueueName { get; set; }

    /// <summary>
    /// Gets or sets the name of the poison queue to move messages to after exceeding the maximum number of delivery attempts. The poison queue must exist in the specified storage account.
    /// </summary>
    [RegularExpression(QueueNamePattern)]
    public string? PoisonQueueName { get; set; }

    /// <inheritdoc/>
    IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
    {
        if (string.Equals(QueueName, PoisonQueueName, StringComparison.Ordinal))
        {
            yield return new ValidationResult("Queue name and poison queue name cannot be the same.", [nameof(QueueName), nameof(PoisonQueueName)]);
        }
    }
}
