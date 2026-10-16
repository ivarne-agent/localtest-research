namespace Altinn.Register.Core.RateLimiting;

/// <summary>
/// Describes how requests should be handled while a subject is already blocked by a rate limit.
/// </summary>
public enum BlockedRequestBehavior
{
    /// <summary>
    /// Blocked requests do not update the existing block.
    /// </summary>
    Ignore = 1,

    /// <summary>
    /// Blocked requests renew the block from the current point in time.
    /// </summary>
    Renew,
}
