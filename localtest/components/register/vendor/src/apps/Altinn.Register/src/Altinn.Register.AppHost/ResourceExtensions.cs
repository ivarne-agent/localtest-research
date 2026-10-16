using System.Diagnostics.CodeAnalysis;

namespace Altinn.Register.AppHost;

/// <summary>
/// Extension metods for reasource builders.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class ResourceExtensions
{
    /// <summary>
    /// Marks all endpoints on the resource as public.
    /// </summary>
    /// <typeparam name="T">The resource type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <returns><paramref name="builder"/>.</returns>
    public static IResourceBuilder<T> WithPublicEndpoints<T>(this IResourceBuilder<T> builder)
        where T : IResourceWithEndpoints
    {
        if (builder.Resource.TryGetAnnotationsOfType<EndpointAnnotation>(out var endpoints))
        {
            foreach (var endpoint in endpoints)
            {
                endpoint.IsProxied = false;
            }
        }

        return builder;
    }
}
