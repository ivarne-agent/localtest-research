using MassTransit;

namespace Altinn.Authorization.ServiceDefaults.MassTransit;

/// <summary>
/// A <see cref="IEndpointNameFormatter"/> for altinn services.
/// </summary>
internal sealed class AltinnEndpointNameFormatter(AltinnServiceDescriptor descriptor)
        : KebabCaseEndpointNameFormatter(prefix: descriptor.Name, includeNamespace: false)
{
}
