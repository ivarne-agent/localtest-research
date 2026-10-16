using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Annotations;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Altinn.Register.Models;

/// <summary>
/// A list object is a wrapper around a list of items to allow for the API to be
/// extended in the future without breaking backwards compatibility.
/// </summary>
[SwaggerSchemaFilter(typeof(SchemaFilter))]
public abstract record ListObject
{
    /// <summary>
    /// Creates a new <see cref="ListObject{T}"/> from a list of items.
    /// </summary>
    /// <typeparam name="T">The list type.</typeparam>
    /// <param name="items">The list of items.</param>
    /// <returns>A <see cref="ListObject{T}"/>.</returns>
    public static ListObject<T> Create<T>(IEnumerable<T> items)
        => new(items);

    /// <summary>
    /// Creates a new <see cref="ListObject{T}"/> from a list of items.
    /// </summary>
    /// <typeparam name="T">The list type.</typeparam>
    /// <param name="items">The list of items.</param>
    /// <returns>A <see cref="ListObject{T}"/>.</returns>
    public static ListObject<T> Create<T>(IReadOnlyCollection<T> items)
        => items.Count == 0
            ? ListObject<T>.Empty
            : new(items);

    /// <summary>
    /// Gets a empty <see cref="ListObject{T}"/> instance.
    /// </summary>
    /// <typeparam name="T">The list type.</typeparam>
    /// <returns>A <see cref="ListObject{T}"/>.</returns>
    public static ListObject<T> Empty<T>()
        where T : notnull
        => ListObject<T>.Empty;

    /// <summary>
    /// Default schema filter for <see cref="ListObject"/>.
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
/// A concrete list object.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items.</param>
public record ListObject<T>(
    [property: JsonPropertyName("data")]
    IEnumerable<T> Items)
    : ListObject
{
    /// <summary>
    /// Gets a empty <see cref="ListObject{T}"/> instance.
    /// </summary>
    public static readonly ListObject<T> Empty = new([]);
}
