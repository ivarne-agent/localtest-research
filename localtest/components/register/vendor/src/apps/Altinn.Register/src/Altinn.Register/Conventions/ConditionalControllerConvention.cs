using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Altinn.Register.Conventions;

/// <summary>
/// A convention that allows controllers to be conditionally added to the application.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class ConditionalControllerConvention
    : IControllerModelConvention
{
    private readonly IServiceProvider _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConditionalControllerConvention"/> class.
    /// </summary>
    public ConditionalControllerConvention(IServiceProvider services)
    {
        _services = services;
    }

    /// <inheritdoc />
    public void Apply(ControllerModel controller)
    {
        foreach (var condition in controller.Attributes.OfType<IControllerModelCondition>())
        {
            if (condition.ShouldDisable(controller, _services))
            {
                DisableController(controller);
                return;
            }
        }

        List<ActionModel>? actionsToDisable = null;
        foreach (var action in controller.Actions)
        {
            foreach (var condition in action.Attributes.OfType<IActionModelCondition>())
            {
                if (condition.ShouldDisable(action, controller, _services))
                {
                    actionsToDisable ??= new();
                    actionsToDisable.Add(action);
                }
            }
        }

        foreach (var actions in actionsToDisable ?? Enumerable.Empty<ActionModel>())
        {
            DisableAction(actions, controller);
        }
    }

    private void DisableController(ControllerModel controller)
    {
        controller.ApiExplorer.IsVisible = false;
        controller.Actions.Clear();
        controller.Selectors.Clear();
        controller.ControllerProperties.Clear();
        controller.Filters.Clear();
        controller.Properties.Clear();

        controller.Filters.Add(DisabledFilter.Instance);
    }

    private void DisableAction(ActionModel action, ControllerModel controller)
    {
        action.ApiExplorer.IsVisible = false;
        action.Selectors.Clear();
        action.Properties.Clear();
        action.Filters.Clear();

        action.Filters.Add(DisabledFilter.Instance);

        controller.Actions.Remove(action);
    }

    private sealed class DisabledFilter
        : IResourceFilter
    {
        public static DisabledFilter Instance { get; } = new DisabledFilter();

        private DisabledFilter()
        {
        }

        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            context.Result = new NotFoundResult();
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}
