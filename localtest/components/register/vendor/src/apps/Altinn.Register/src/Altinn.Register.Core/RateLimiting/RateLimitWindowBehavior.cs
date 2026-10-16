namespace Altinn.Register.Core.RateLimiting;

/// <summary>
/// Describes how a rate-limit window behaves when a new event is recorded.
/// </summary>
public enum RateLimitWindowBehavior
{
    /// <summary>
    /// The window starts with the first event and stays fixed until it expires.
    /// </summary>
    LeadingEdge = 1,

    /// <summary>
    /// The window is renewed from the current point in time whenever an event is recorded.
    /// </summary>
    TrailingEdge,
}
