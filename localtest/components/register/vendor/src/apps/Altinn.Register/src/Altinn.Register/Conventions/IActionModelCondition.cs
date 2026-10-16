using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Altinn.Register.Conventions;

/// <summary>
/// A condition that determines whether a action should be disabled.
/// </summary>
public interface IActionModelCondition
{
    /// <summary>
    /// Determines whether the action should be disabled.
    /// </summary>
    /// <param name="action">The <see cref="ActionModel"/> in question.</param>
    /// <param name="controller">The <see cref="ControllerModel"/> in question.</param>
    /// <param name="services">The <see cref="IServiceProvider"/>.</param>
    /// <returns><see langword="true"/> if the controller should be disabled.</returns>
    bool ShouldDisable(ActionModel action, ControllerModel controller, IServiceProvider services);
}
