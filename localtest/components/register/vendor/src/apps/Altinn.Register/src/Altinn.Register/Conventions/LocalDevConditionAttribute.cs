using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ServiceDefaults;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Altinn.Register.Conventions;

/// <summary>
/// A condition that disables a controller if the application is not running in a development environment.
/// </summary>
[ExcludeFromCodeCoverage]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class LocalDevConditionAttribute
    : Attribute
    , IControllerModelCondition
    , IActionModelCondition
{
    /// <inheritdoc />
    public bool ShouldDisable(ControllerModel controller, IServiceProvider services)
        => ShouldDisable(services);

    /// <inheritdoc />
    public bool ShouldDisable(ActionModel action, ControllerModel controller, IServiceProvider services)
        => ShouldDisable(services);

    private bool ShouldDisable(IServiceProvider services)
    {
        var env = services.GetRequiredService<AltinnServiceDescriptor>();

        return !env.IsLocalDev;
    }
}
