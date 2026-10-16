using System.Collections.Immutable;
using Altinn.Register.Contracts;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Tests.Mocks;

public class MockExternalRoleDefinitionPersistence
    : IExternalRoleDefinitionPersistence
{
    public ValueTask<ImmutableArray<ExternalRoleDefinition>> GetAllRoleDefinitions(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public ValueTask<IExternalRoleDefinitionLookup> GetRoleDefinitionLookup(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public ValueTask<ExternalRoleDefinition?> TryGetRoleDefinition(ExternalRoleSource source, string identifier, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public ValueTask<ExternalRoleDefinition?> TryGetRoleDefinitionByRoleCode(string roleCode, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
