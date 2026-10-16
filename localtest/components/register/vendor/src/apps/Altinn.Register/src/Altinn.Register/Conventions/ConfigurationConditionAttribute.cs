using CommunityToolkit.Diagnostics;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Altinn.Register.Conventions;

/// <summary>
/// A condition that disables a controller/action if a specified configuration key is not set to true.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ConfigurationConditionAttribute
    : Attribute
    , IControllerModelCondition
    , IActionModelCondition
{
    private readonly string _configurationKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationConditionAttribute"/> class.
    /// </summary>
    /// <param name="configurationKey">The configuration key to check.</param>
    public ConfigurationConditionAttribute(string configurationKey)
    {
        Guard.IsNotNullOrEmpty(configurationKey);

        _configurationKey = configurationKey;
    }

    /// <inheritdoc />
    public bool ShouldDisable(ControllerModel controller, IServiceProvider services)
        => ShouldDisable(services);

    /// <inheritdoc />
    public bool ShouldDisable(ActionModel action, ControllerModel controller, IServiceProvider services)
        => ShouldDisable(services);

    private bool ShouldDisable(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        return !config.GetValue(_configurationKey, defaultValue: false);
    }
}
