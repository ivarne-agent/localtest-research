using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Altinn.Register.Contracts;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Localtest.Persistence;

/// <summary>
/// Role definitions from the embedded <c>external-role-definitions.json</c>, a snapshot of the table the
/// production migrations build (see <c>Data/README.md</c> for how it is refreshed).
/// </summary>
public sealed class StaticExternalRoleDefinitionPersistence
    : IExternalRoleDefinitionPersistence, IExternalRoleDefinitionLookup
{
    private readonly ImmutableArray<ExternalRoleDefinition> _all;
    private readonly Dictionary<(ExternalRoleSource Source, string Identifier), ExternalRoleDefinition> _byKey;
    private readonly Dictionary<string, ExternalRoleDefinition> _byCode;

    public StaticExternalRoleDefinitionPersistence()
    {
        _all = LoadEmbedded();
        _byKey = _all.ToDictionary(d => (d.Source, d.Identifier));
        _byCode = _all.Where(d => d.Code is not null).ToDictionary(d => d.Code!, StringComparer.OrdinalIgnoreCase);
    }

    public ImmutableArray<ExternalRoleDefinition> AllRoleDefinitions => _all;

    public ImmutableHashSet<(ExternalRoleSource Source, string Identifier)> MainUnitRoles { get; private set; } = [];

    public bool TryGetRoleDefinition(ExternalRoleSource source, string identifier, [NotNullWhen(true)] out ExternalRoleDefinition? roleDefinition)
        => _byKey.TryGetValue((source, identifier), out roleDefinition);

    public bool TryGetRoleDefinitionByRoleCode(string roleCode, [NotNullWhen(true)] out ExternalRoleDefinition? roleDefinition)
        => _byCode.TryGetValue(roleCode, out roleDefinition);

    public ValueTask<ExternalRoleDefinition?> TryGetRoleDefinition(ExternalRoleSource source, string identifier, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_byKey.GetValueOrDefault((source, identifier)));

    public ValueTask<ExternalRoleDefinition?> TryGetRoleDefinitionByRoleCode(string roleCode, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_byCode.GetValueOrDefault(roleCode));

    public ValueTask<ImmutableArray<ExternalRoleDefinition>> GetAllRoleDefinitions(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_all);

    public ValueTask<IExternalRoleDefinitionLookup> GetRoleDefinitionLookup(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IExternalRoleDefinitionLookup>(this);

    public bool IsMainUnitRole(ExternalRoleSource source, string identifier)
        => MainUnitRoles.Contains((source, identifier));

    private ImmutableArray<ExternalRoleDefinition> LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("external-role-definitions.json")
            ?? throw new InvalidOperationException("Embedded resource 'external-role-definitions.json' is missing.");
        var rows = JsonSerializer.Deserialize<List<Row>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? [];

        var mainUnits = ImmutableHashSet.CreateBuilder<(ExternalRoleSource, string)>();
        var builder = ImmutableArray.CreateBuilder<ExternalRoleDefinition>(rows.Count);
        foreach (var row in rows)
        {
            var source = ParseSource(row.Source);
            builder.Add(new ExternalRoleDefinition
            {
                Source = source,
                Identifier = row.Identifier,
                Code = row.Code,
                Name = Translate(row.Name),
                Description = Translate(row.Description),
                Flags = row.MainUnit ? ExternalRoleDefinition.RoleFlags.MainUnit : ExternalRoleDefinition.RoleFlags.None,
            });

            if (row.MainUnit)
            {
                mainUnits.Add((source, row.Identifier));
            }
        }

        MainUnitRoles = mainUnits.ToImmutable();
        return builder.ToImmutable();
    }

    private static ExternalRoleSource ParseSource(string source)
        => source switch
        {
            "ccr" => ExternalRoleSource.CentralCoordinatingRegister,
            "npr" => ExternalRoleSource.NationalPopulationRegister,
            "aar" => ExternalRoleSource.EmployersEmployeeRegister,
            "cra" => ExternalRoleSource.CivilRightsAuthority,
            _ => throw new InvalidOperationException($"Unknown external role source '{source}'."),
        };

    private static TranslatedText Translate(Dictionary<string, string> values)
    {
        var builder = TranslatedText.CreateBuilder();
        foreach (var (code, text) in values)
        {
            builder.Add(LangCode.FromCode(code), text);
        }

        return builder.ToImmutable();
    }

    private sealed class Row
    {
        public string Source { get; set; } = string.Empty;

        public string Identifier { get; set; } = string.Empty;

        public string? Code { get; set; }

        public Dictionary<string, string> Name { get; set; } = [];

        public Dictionary<string, string> Description { get; set; } = [];

        public bool MainUnit { get; set; }
    }
}
