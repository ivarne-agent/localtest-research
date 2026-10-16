namespace Altinn.Register.Core.Mediator;

/// <summary>
/// A source for the data used by API endpoints.
/// This is used to switch between different implementations of the same API endpoint.
/// </summary>
internal enum ApiSource
{
    /// <summary>
    /// Altinn 2 endpoint.
    /// </summary>
    A2,

    /// <summary>
    /// Internal (Altinn 3) database.
    /// </summary>
    DB,
}
