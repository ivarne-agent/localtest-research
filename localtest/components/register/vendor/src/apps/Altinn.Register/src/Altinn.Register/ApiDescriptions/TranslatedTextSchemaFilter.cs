using System.Collections.Frozen;
using System.Diagnostics;
using Altinn.Register.Contracts;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Altinn.Register.ApiDescriptions;

/// <summary>
/// Schema filter for fixing translated-text properties.
/// </summary>
internal sealed class TranslatedTextSchemaFilter
    : ISchemaFilter
{
    private static readonly FrozenDictionary<Type, FrozenSet<string>> Types
        = new Dictionary<Type, List<string>>()
        {
            { typeof(ExternalRoleMetadata), ["name", "description"] }
        }
        .ToFrozenDictionary(
            static kvp => kvp.Key,
            static kvp => kvp.Value.ToFrozenSet());

    private readonly IOptionsMonitor<SchemaGeneratorOptions> _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslatedTextSchemaFilter"/> class.
    /// </summary>
    public TranslatedTextSchemaFilter(IOptionsMonitor<SchemaGeneratorOptions> settings)
    {
        _settings = settings;
    }

    /// <inheritdoc/>
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        Guard.IsNotNull(schema);
        Guard.IsNotNull(context);

        if (Types.TryGetValue(context.Type, out var props))
        {
            FixTranslatedTexts(schema, context, props);
        }

        return;
    }

    private void FixTranslatedTexts(OpenApiSchema schema, SchemaFilterContext context, FrozenSet<string> props)
    {
        if (schema.Properties is not { Count: > 0 })
        {
            return;
        }

        var translatedTextSchema = GetTranslatedTextSchemaRef(context);
        foreach (var propName in schema.Properties.Keys)
        {
            if (props.Contains(propName))
            {
                schema.Properties[propName] = translatedTextSchema;
            }
        }
    }

    private OpenApiSchema GetTranslatedTextSchemaRef(SchemaFilterContext context)
    {
        if (context.SchemaRepository.TryLookupByType(typeof(TranslatedText), out var result))
        {
            return result;
        }

        var schema = context.SchemaGenerator.GenerateSchema(typeof(TranslatedText), context.SchemaRepository);
        schema.Required.Add(LangCode.En.Code);
        schema.Required.Add(LangCode.Nb.Code);
        schema.Required.Add(LangCode.Nn.Code);
        schema.Properties.Add(LangCode.En.Code, new OpenApiSchema
        {
            Type = "string",
        });
        schema.Properties.Add(LangCode.Nb.Code, new OpenApiSchema
        {
            Type = "string",
        });
        schema.Properties.Add(LangCode.Nn.Code, new OpenApiSchema
        {
            Type = "string",
        });

        var id = _settings.CurrentValue.SchemaIdSelector(typeof(TranslatedText));
        context.SchemaRepository.AddDefinition(id, schema);
        context.SchemaRepository.RegisterType(typeof(TranslatedText), id);

        var found = context.SchemaRepository.TryLookupByType(typeof(TranslatedText), out result);
        Debug.Assert(found);
        return result;
    }
}
