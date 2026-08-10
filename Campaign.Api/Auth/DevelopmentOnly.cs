namespace Campaign.Api.Auth;

using Microsoft.AspNetCore.Mvc.ApplicationModels;

/// <summary>Marks a controller that must not exist outside Development.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DevelopmentOnlyAttribute : Attribute;

/// <summary>
/// Removes the marked controllers from the application model, so outside Development their routes do
/// not exist at all - a request for them is a plain 404, not a refused call to something that is
/// still there.
/// </summary>
public sealed class RemoveDevelopmentOnlyControllers : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        var developmentOnly = application.Controllers
            .Where(controller => controller.Attributes.OfType<DevelopmentOnlyAttribute>().Any())
            .ToList();

        foreach (var controller in developmentOnly)
        {
            application.Controllers.Remove(controller);
        }
    }
}
