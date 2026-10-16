using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Altinn.Authorization.ServiceDefaults.AzureIdentity.Tests;

public class TokenCredentialConfigurationTests
{
    [Fact]
    public void AddTokenCredentialProvider_RegistersProviderOnce()
    {
        var services = CreateServices();

        services.AddTokenCredentialProvider();
        services.AddTokenCredentialProvider();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITokenCredentialProvider>()
            .ShouldBeOfType<DefaultTokenCredentialProvider>();

        services
            .Count(static descriptor => descriptor.ServiceType == typeof(ITokenCredentialProvider))
            .ShouldBe(1);
    }

    [Fact]
    public void TokenCredentialOptions_WhenConfigurationIsEmpty_EnablesDefaultCredentials()
    {
        using var provider = CreateProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<TokenCredentialOptions>>()
            .Get(Options.DefaultName);

        GetConfiguredCredentialTypes(options).ShouldBe(
            [
                typeof(WorkloadIdentityCredential),
                typeof(ManagedIdentityCredential),
            ]);
    }

    [Fact]
    public void TokenCredentialOptions_WhenDefaultsAreConfigured_UsesDefaults()
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            ["Altinn:AzureIdentity:Defaults:Environment:Enable"] = "true",
            ["Altinn:AzureIdentity:Defaults:WorkloadIdentity:Enable"] = "false",
            ["Altinn:AzureIdentity:Defaults:ManagedIdentity:Enable"] = "false",
        });

        var options = provider.GetRequiredService<IOptionsMonitor<TokenCredentialOptions>>()
            .Get(Options.DefaultName);

        GetConfiguredCredentialTypes(options).ShouldBe([typeof(EnvironmentCredential)]);
    }

    [Fact]
    public void TokenCredentialOptions_WhenNamedIdentityIsPartiallyConfigured_InheritsDefaults()
    {
        using var provider = CreateProvider(new Dictionary<string, string?>
        {
            ["Altinn:AzureIdentity:Defaults:Environment:Enable"] = "true",
            ["Altinn:AzureIdentity:Defaults:WorkloadIdentity:Enable"] = "false",
            ["Altinn:AzureIdentity:Defaults:ManagedIdentity:Enable"] = "true",
            ["Altinn:AzureIdentity:Identities:storage:WorkloadIdentity:Enable"] = "true",
            ["Altinn:AzureIdentity:Identities:storage:ManagedIdentity:Enable"] = "false",
        });

        var options = provider.GetRequiredService<IOptionsMonitor<TokenCredentialOptions>>()
            .Get("storage");

        GetConfiguredCredentialTypes(options).ShouldBe(
            [
                typeof(EnvironmentCredential),
                typeof(WorkloadIdentityCredential),
            ]);
    }

    [Fact]
    public void GetCredential_WhenConfigurationChanges_UsesReloadedConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Altinn:AzureIdentity:Identities:storage:Environment:Enable"] = "true",
                ["Altinn:AzureIdentity:Identities:storage:WorkloadIdentity:Enable"] = "false",
                ["Altinn:AzureIdentity:Identities:storage:ManagedIdentity:Enable"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTokenCredentialProvider();

        using var provider = services.BuildServiceProvider();
        var credentials = provider.GetRequiredService<ITokenCredentialProvider>();

        credentials.GetCredential("storage").ShouldBeOfType<EnvironmentCredential>();

        configuration["Altinn:AzureIdentity:Identities:storage:Environment:Enable"] = "false";
        configuration["Altinn:AzureIdentity:Identities:storage:WorkloadIdentity:Enable"] = "true";
        configuration.Reload();

        credentials.GetCredential("storage").ShouldBeOfType<WorkloadIdentityCredential>();
    }

    private static ServiceCollection CreateServices(
        IReadOnlyDictionary<string, string?>? configurationValues = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        return services;
    }

    private static ServiceProvider CreateProvider(
        IReadOnlyDictionary<string, string?>? configurationValues = null)
    {
        var services = CreateServices(configurationValues);
        services.AddTokenCredentialProvider();

        return services.BuildServiceProvider();
    }

    private static Type[] GetConfiguredCredentialTypes(TokenCredentialOptions options)
    {
        var builder = new RecordingTokenCredentialBuilder();

        foreach (var action in options.TokenCredentialBuilderActions)
        {
            action(builder);
        }

        return builder.Credentials.Select(static credential => credential.GetType()).ToArray();
    }

    private sealed class RecordingTokenCredentialBuilder
        : TokenCredentialBuilder
    {
        public override string? Name { get; set; } = "test";

        public override IList<TokenCredential> Credentials { get; } = [];

        public override TokenCredential Build()
            => throw new NotSupportedException();
    }
}
