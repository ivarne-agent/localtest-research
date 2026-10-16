using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Altinn.Register.Conventions;

/// <summary>
/// A condition that disables a controller/action if the application is not running in a development environment.
/// </summary>
[ExcludeFromCodeCoverage]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DevTestConditionAttribute
    : Attribute
    , IControllerModelCondition
    , IActionModelCondition
{
    private static readonly ImmutableArray<string> _devTestEnvironments
        = ["development", "test", "staging", "at22", "at23", "at24"];

    /// <inheritdoc />
    public bool ShouldDisable(ControllerModel controller, IServiceProvider services)
        => ShouldDisable(services);

    /// <inheritdoc />
    public bool ShouldDisable(ActionModel action, ControllerModel controller, IServiceProvider services)
        => ShouldDisable(services);

    private bool ShouldDisable(IServiceProvider services)
    {
        var env = services.GetRequiredService<IHostEnvironment>();

        foreach (var validEnv in _devTestEnvironments)
        {
            if (string.Equals(env.EnvironmentName, validEnv, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
