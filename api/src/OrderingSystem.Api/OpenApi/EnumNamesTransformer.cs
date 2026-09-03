using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace OrderingSystem.Api.OpenApi;

/// <summary>
/// Gives every enum in the document its values and its names.
///
/// <para>
/// Without this, an enum reaches the document as bare <c>{"type": "integer"}</c> — the names are
/// dropped, because the API serialises enums as numbers and JSON Schema has nowhere to keep a
/// name for an integer. A generator then has nothing to work with, so every enum in the
/// TypeScript client came out as <c>number</c> and a screen accepting an order had to post
/// <c>{ to: 2 }</c>. A magic number is bad enough on its own; one that silently means something
/// else after somebody renumbers the C# enum is the kind of bug nothing catches.
/// </para>
/// <para>
/// <c>x-enumNames</c> is NSwag's convention for carrying the names alongside the values, and is
/// what lets it emit a real TypeScript enum. The numbers stay the wire format — the same integers
/// the columns store — so the client gains names without the contract changing at all.
/// </para>
/// </summary>
internal sealed class EnumNamesTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (!type.IsEnum)
        {
            return Task.CompletedTask;
        }

        // Names and values are read in one pass over the same array, so an enum with two members
        // sharing a value cannot end up with the lists out of step.
        var members = Enum.GetValues(type).Cast<object>().ToArray();

        schema.Enum = [.. members.Select(m =>
            (JsonNode)JsonValue.Create(Convert.ToInt32(m, System.Globalization.CultureInfo.InvariantCulture)))];

        schema.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        schema.Extensions["x-enumNames"] = new JsonNodeExtension(
            new JsonArray([.. members.Select(m => (JsonNode)JsonValue.Create(Enum.GetName(type, m)!))]));

        return Task.CompletedTask;
    }
}
