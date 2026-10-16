using CommunityToolkit.Diagnostics;

using Microsoft.OpenApi.Models;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Altinn.Register.ApiDescriptions;

/// <summary>
/// Base class for schema filters that affect a specific type.
/// </summary>
/// <typeparam name="T">The type this schema filter affects.</typeparam>
public abstract class SchemaFilter<T> : ISchemaFilter
{
    /// <inheritdoc/>
    void ISchemaFilter.Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        Guard.IsNotNull(schema);
        Guard.IsNotNull(context);

        if (context.Type != typeof(T))
        {
            return;
        }

        Apply(schema, context);
    }

    /// <summary>
    /// Applies the schema filter to the given schema if the context type matches the specified type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="schema">The schema to apply the filter to.</param>
    /// <param name="context">The context in which the schema is being applied.</param>
    protected abstract void Apply(OpenApiSchema schema, SchemaFilterContext context);
}
