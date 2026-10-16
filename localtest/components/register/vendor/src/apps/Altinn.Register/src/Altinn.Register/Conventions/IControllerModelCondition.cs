using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Altinn.Register.Conventions;

/// <summary>
/// A condition that determines whether a controller should be disabled.
/// </summary>
public interface IControllerModelCondition
{
    /// <summary>
    /// Determines whether the controller should be disabled.
    /// </summary>
    /// <param name="controller">The <see cref="ControllerModel"/> in question.</param>
    /// <param name="services">The <see cref="IServiceProvider"/>.</param>
    /// <returns><see langword="true"/> if the controller should be disabled.</returns>
    bool ShouldDisable(ControllerModel controller, IServiceProvider services);
}
