namespace Altinn.Authorization.ServiceDefaults.MassTransit;

/// <summary>
/// Options for altinn masstransit
/// </summary>
public class AltinnMassTransitOptions
{
    /// <summary>
    /// Gets or sets the activity propagation type.
    /// </summary>
    public string ActivityPropagation { get; set; } = "Link";
}
