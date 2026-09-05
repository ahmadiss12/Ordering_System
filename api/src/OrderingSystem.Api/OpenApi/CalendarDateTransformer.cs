using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace OrderingSystem.Api.OpenApi;

/// <summary>
/// Describes a <see cref="DateOnly"/> as a plain string rather than as a date format.
///
/// <para>
/// A calendar date is not an instant, and the generated TypeScript client is where that stops
/// being a philosophical point. NSwag renders <c>format: date</c> as a JavaScript <c>Date</c>,
/// which is a moment in time: sending one calls <c>toISOString()</c>, so a Beirut owner picking
/// the 5th at midnight posts the 4th at 21:00Z; reading one parses "2026-09-05" as midnight UTC,
/// so a browser west of Greenwich shows the 4th. The whole reason orders carry a business date is
/// to stop a Beirut evening being filed under the wrong day, and both of those would put it back.
/// </para>
/// <para>
/// A bare string has no timezone to get wrong. It travels as "2026-09-05", which is what the API
/// already writes and reads, and the client hands it back untouched. The pattern keeps the shape
/// documented for anybody reading the contract rather than the generated code.
/// </para>
/// <para>
/// Done here rather than through NSwag's <c>dateType</c> setting, which this version ignores.
/// </para>
/// </summary>
internal sealed class CalendarDateTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;

        if (type == typeof(DateOnly))
        {
            schema.Format = null;
            schema.Pattern = @"^\d{4}-\d{2}-\d{2}$";
            schema.Example = null;
        }

        return Task.CompletedTask;
    }
}
