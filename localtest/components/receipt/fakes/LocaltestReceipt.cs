using System;
using Altinn.Platform.Receipt.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Altinn.Platform.Receipt.Localtest;

/// <summary>
/// The localtest hook, called from Program.cs (<c>#if LOCALTEST</c>) right after the production registrations: it
/// replaces <see cref="IStorage"/> with <see cref="SelfLinksStorage"/> over the production
/// <see cref="StorageWrapper"/>. Everything else is production code, configured by environment variables.
/// </summary>
public static class LocaltestReceipt
{
    /// <summary>The public origin attachment links are rewritten to (studioctl passes the platform endpoint).</summary>
    public const string PlatformEndpointKey = "Localtest:Receipt:PlatformEndpoint";

    /// <summary>Used when <see cref="PlatformEndpointKey"/> is not set: the stack's public origin.</summary>
    public const string DefaultPlatformEndpoint = "http://local.altinn.cloud:8000/";

    /// <summary>Replaces the registrations that cannot work locally.</summary>
    public static void Apply(IServiceCollection services, IConfiguration config)
    {
        string productionPrefix = $"https://platform.{config["GeneralSettings:Hostname"]}/";
        string localPrefix = config[PlatformEndpointKey] is { Length: > 0 } endpoint
            ? endpoint.TrimEnd('/') + "/"
            : DefaultPlatformEndpoint;

        // The production typed client stays as it is (base address, subscription key, Accept); the decorator wraps it.
        services.AddHttpClient<StorageWrapper>();
        services.Replace(
            ServiceDescriptor.Transient<IStorage>(sp => new SelfLinksStorage(
                sp.GetRequiredService<StorageWrapper>(),
                productionPrefix,
                localPrefix
            ))
        );
    }
}
