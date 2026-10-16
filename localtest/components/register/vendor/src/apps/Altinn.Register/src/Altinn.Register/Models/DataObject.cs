using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Annotations;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Altinn.Register.Models;

/// <summary>
/// A data object is a wrapper around a single item to allow for the API to be
/// extended in the future without breaking backwards compatibility.
/// </summary>
[SwaggerSchemaFilter(typeof(SchemaFilter))]
public abstract record DataObject
{
    /// <summary>
    /// Creates a new <see cref="DataObject{T}"/> from an item.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="item">The item.</param>
    /// <returns>A <see cref="DataObject{T}"/>.</returns>
    public static DataObject<T> Create<T>(T item)
        where T : notnull
        => new(item);

    /// <summary>
    /// Default schema filter for <see cref="DataObject"/>.
    /// </summary>
    protected class SchemaFilter : ISchemaFilter
    {
        /// <inheritdoc/>
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            foreach (var prop in schema.Properties)
            {
                schema.Required.Add(prop.Key);
            }

            schema.Properties["data"].Nullable = false;
        }
    }
}

/// <summary>
/// A concrete data object.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Item">The item.</param>
public record DataObject<T>(
    [property: JsonPropertyName("data")]
    T Item)
    : DataObject
    where T : notnull;
