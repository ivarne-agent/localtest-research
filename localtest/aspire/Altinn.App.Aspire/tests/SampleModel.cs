using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Yarp;
using Yarp.ReverseProxy.Configuration;

namespace Altinn.App.Aspire.Tests;

/// <summary>The sample AppHost's model (<c>AddAltinnLocaltest().WithGroup("dialogporten")</c>), built and not run.</summary>
internal sealed class SampleModel
{
    private SampleModel(IDistributedApplicationBuilder builder, IResourceBuilder<AltinnLocaltestResource> localtest)
    {
        Builder = builder;
        Localtest = localtest;
    }

    public IDistributedApplicationBuilder Builder { get; }

    public IResourceBuilder<AltinnLocaltestResource> Localtest { get; }

    public static SampleModel Build()
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true }
        );
        var localtest = builder
            .AddAltinnLocaltest(configure: o => o.DataDirectory = "/data-directory")
            .WithGroup("dialogporten");
        return new SampleModel(builder, localtest);
    }

    public IReadOnlyList<IResource> Resources => [.. Builder.Resources];

    public ContainerResource Container(string name) =>
        Builder.Resources.OfType<ContainerResource>().Single(r => r.Name == name);

    public YarpResource FrontDoor => Builder.Resources.OfType<YarpResource>().Single();

    /// <summary>The YARP routes, read from the resource's internal list (the integration has no public read API).</summary>
    public IReadOnlyList<RouteConfig> YarpRoutes =>
        [.. Internal<IEnumerable<object>>(FrontDoor, "Routes").Select(r => Internal<RouteConfig>(r, "RouteConfig"))];

    /// <summary>The YARP clusters' IDs, read the same way.</summary>
    public IReadOnlyList<string> YarpClusterIds =>
        [
            .. Internal<IEnumerable<object>>(FrontDoor, "Clusters")
                .Select(c => Internal<ClusterConfig>(c, "ClusterConfig").ClusterId),
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

    private static T Internal<T>(object target, string property) =>
        (T)target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
}
