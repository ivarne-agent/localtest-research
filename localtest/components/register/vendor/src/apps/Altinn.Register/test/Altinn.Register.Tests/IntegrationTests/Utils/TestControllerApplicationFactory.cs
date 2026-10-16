using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.Tests.IntegrationTests.Utils;

public class TestControllerApplicationFactory
    : WebApplicationFactorySetup
{
    protected static void AddTestController(ApplicationPartManager partManager, TypeInfo type)
    {
        partManager.ApplicationParts.Add(new TestControllerApplicationPart(type));
        if (!partManager.FeatureProviders.Contains(TestControllerApplicationFeatureProvider.Instance))
        {
            partManager.FeatureProviders.Add(TestControllerApplicationFeatureProvider.Instance);
        }
    }

    protected override WebApplicationBuilder CreateWebApplicationBuilder()
    {
        var builder = base.CreateWebApplicationBuilder();

        builder.Services.AddMvcCore().AddControllersAsServices();
        return builder;
    }

    protected override WebApplication CreateWebApplication(WebApplicationBuilder builder)
    {
        var app = base.CreateWebApplication(builder);

        app.MapControllers();
        return app;
    }

    private class TestControllerApplicationPart(TypeInfo type)
        : ApplicationPart
    {
        public override string Name => $"{nameof(TestControllerApplicationPart)}:{type.FullName}";

        public TypeInfo Type => type;
    }

    private class TestControllerApplicationFeatureProvider
        : IApplicationFeatureProvider<ControllerFeature>
    {
        public static IApplicationFeatureProvider Instance { get; } = new TestControllerApplicationFeatureProvider();

        private TestControllerApplicationFeatureProvider()
        {
        }

        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var part in parts.OfType<TestControllerApplicationPart>())
            {
                feature.Controllers.Add(part.Type);
            }
        }
    }
}

public class TestControllerApplicationFactory<T>
    : TestControllerApplicationFactory
{
    protected override void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
        AddTestController(partManager, typeof(T).GetTypeInfo());

        base.ConfigureApplicationParts(partManager);
    }
}
