namespace Altinn.Studio.Localtest.Provisioning;

/// <summary>What one provisioning pushed.</summary>
/// <param name="TextCount">Text resources pushed to Storage.</param>
/// <param name="HasPolicy">Whether the app has a policy (pushed to the PDP and reloaded in the Resource Registry).</param>
/// <param name="HasTestData">Whether the app has <c>testData.json</c> (an empty registration otherwise).</param>
/// <param name="Dialogporten">The outcome of step 5.</param>
public sealed record ProvisionResult(int TextCount, bool HasPolicy, bool HasTestData, DialogportenSync Dialogporten);

/// <summary>The outcome of Dialogporten's localtest sync (step 5).</summary>
public enum DialogportenSync
{
    /// <summary>The <c>dialogporten</c> group is not running; nothing was sent.</summary>
    Skipped,

    /// <summary>Dialogporten synced the Resource Registry and cleared its caches.</summary>
    Synced,

    /// <summary>The sync failed (logged once until it succeeds); provisioning does not fail for it.</summary>
    Failed,
}
