using Altinn.Authorization.ModelUtils.EnumUtils;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Altinn.Register.ApiDescriptions;

/// <summary>
/// Base schema filter for flags enum types.
/// </summary>
/// <typeparam name="TEnum">The enum type.</typeparam>
public abstract class FlagsEnumSchemaFilter<TEnum>
    : SchemaFilter<TEnum>
    where TEnum : struct, Enum
{
    private readonly FlagsEnumModel<TEnum> _model;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlagsEnumSchemaFilter{TEnum}"/> class.
    /// </summary>
    /// <param name="model">The flags enum model.</param>
    protected FlagsEnumSchemaFilter(FlagsEnumModel<TEnum> model)
    {
        _model = model;
    }

    /// <inheritdoc/>
    protected override void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        schema.Enum = null;
        schema.Format = null;
        schema.Type = "array";
        schema.Items = new OpenApiSchema
        {
            Type = "string",
            Enum = [.. _model.Items.Select(v => (IOpenApiAny)new OpenApiString(v.Name))],
        };
    }
}
