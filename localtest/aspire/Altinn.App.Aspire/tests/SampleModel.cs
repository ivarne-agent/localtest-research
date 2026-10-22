using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The sample AppHost's model (<c>AddAltinnLocaltest().WithDialogporten()</c> and
/// <c>AddAltinnApp&lt;Projects.App&gt;(localtest)</c>), built and not run.
/// </summary>
internal sealed class SampleModel
{
    /// <summary>The data directory the model is built with.</summary>
    public const string DataDirectory = "/data-directory";

    private SampleModel(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<AltinnLocaltestResource> localtest,
        IResourceBuilder<ProjectResource>? app
    )
    {
        Builder = builder;
        Localtest = localtest;
        App = app;
    }

    public IDistributedApplicationBuilder Builder { get; }

    public IResourceBuilder<AltinnLocaltestResource> Localtest { get; }

    /// <summary>The app, unless the model was built without it.</summary>
    public IResourceBuilder<ProjectResource>? App { get; }

    public static SampleModel Build(bool withApp = true, Action<AltinnAppOptions>? configureApp = null) =>
        Build(["dialogporten"], withApp, configureApp: configureApp);

    /// <summary>A model with the named optional groups (none: the default stack).</summary>
    public static SampleModel Build(
        IReadOnlyList<string> groups,
        bool withApp = false,
        string dataDirectory = DataDirectory,
        Action<AltinnAppOptions>? configureApp = null,
        Action<AltinnLocaltestOptions>? configure = null
    )
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true }
        );
        var localtest = builder.AddAltinnLocaltest(configure: o =>
        {
            o.DataDirectory = dataDirectory;
            configure?.Invoke(o);
        });
        foreach (var group in groups)
        {
            localtest.WithGroup(group);
        }
        var app = withApp ? builder.AddAltinnApp<SampleAppProject>(localtest, configure: configureApp) : null;
        return new SampleModel(builder, localtest, app);
    }

    public IReadOnlyList<IResource> Resources => [.. Builder.Resources];

    public ContainerResource Container(string name) =>
        Builder.Resources.OfType<ContainerResource>().Single(r => r.Name == name);

    /// <summary>The container resources, in the order they were added.</summary>
    public IReadOnlyList<ContainerResource> Containers => [.. Builder.Resources.OfType<ContainerResource>()];

    /// <summary>The resources a container waits for to be healthy.</summary>
    public static IReadOnlyList<string> HealthyWaits(IResource resource) =>
        [
            .. resource
                .Annotations.OfType<WaitAnnotation>()
                .Where(w => w.WaitType == WaitType.WaitUntilHealthy)
                .Select(w => w.Resource.Name),
        ];

    public static async Task<IReadOnlyList<string>> RuntimeArgs(IResource resource)
    {
        var args = new List<object>();
        foreach (var annotation in resource.Annotations.OfType<ContainerRuntimeArgsCallbackAnnotation>())
        {
            await annotation.Callback(new ContainerRuntimeArgsCallbackContext(args));
        }
        return [.. args.Select(a => a.ToString()!)];
    }

    public static async Task<IReadOnlyList<string>> Args(IResource resource)
    {
        var args = new List<object>();
        foreach (var annotation in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
        {
            await annotation.Callback(new CommandLineArgsCallbackContext(args, resource));
        }
        return [.. args.Select(a => a.ToString()!)];
    }

    public async Task<IReadOnlyDictionary<string, string>> Environment(IResource resource)
    {
        var values = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(Builder.ExecutionContext, resource, values);
        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }
        return values.Where(kv => kv.Value is string).ToDictionary(kv => kv.Key, kv => (string)kv.Value);
    }

    /// <summary>An endpoint-valued environment variable rendered with the endpoint's properties as placeholders.</summary>
    public async Task<string?> EnvironmentExpression(IResource resource, string key)
    {
        var values = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(Builder.ExecutionContext, resource, values);
        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }
        return values.TryGetValue(key, out var value) && value is ReferenceExpression expression
            ? expression.ValueExpression
            : null;
    }
}

/// <summary>The sample app's project, as the AppHost SDK generates <c>Projects.App</c> for it.</summary>
internal sealed class SampleAppProject : IProjectMetadata
{
    public string ProjectPath =>
        typeof(SampleAppProject)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "SampleAppProject")
            .Value!;
}
