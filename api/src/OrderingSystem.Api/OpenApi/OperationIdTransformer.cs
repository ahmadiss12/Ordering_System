using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace OrderingSystem.Api.OpenApi;

/// <summary>
/// Gives every operation an id of the form <c>Controller_Action</c>.
/// <para>
/// Without one, a generator has nothing to name a method after and falls back to guessing from
/// the URL — which produced <c>imagePOST</c> and <c>availability</c> rather than
/// <c>uploadImage</c> and <c>setAvailability</c>. The underscore is also the convention NSwag
/// splits on to decide which service a method belongs to, so this single transformer fixes both
/// the naming and the grouping.
/// </para>
/// <para>
/// Operation ids are part of the published contract: renaming a controller or an action renames
/// a method in every generated client. That is a visible diff, which is the point.
/// </para>
/// </summary>
internal sealed class OperationIdTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor is ControllerActionDescriptor action)
        {
            operation.OperationId = $"{action.ControllerName}_{action.ActionName}";
        }
        else if (!string.IsNullOrEmpty(context.Description.RelativePath))
        {
            // Minimal-API endpoints such as /health have no controller to name them after.
            var name = context.Description.RelativePath.Replace("/", string.Empty, StringComparison.Ordinal);
            operation.OperationId = $"Health_{name}";
        }

        return Task.CompletedTask;
    }
}
