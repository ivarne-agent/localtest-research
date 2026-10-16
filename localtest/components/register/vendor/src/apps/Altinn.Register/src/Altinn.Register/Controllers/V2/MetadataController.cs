using Altinn.Register.Contracts;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Models;
using Altinn.Register.Utils;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Controllers.V2;

/// <summary>
/// Provides access to register metadata.
/// </summary>
[ApiController]
[ApiVersion(2.0)]
[Authorize(Policy = "InternalOrPlatformAccess")]
[Route("register/api/v{version:apiVersion}/internal/metadata")]
public class MetadataController
    : ControllerBase
{
    private readonly IExternalRoleDefinitionPersistence _externalRoleDefinitionPersistence;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataController"/> class.
    /// </summary>
    public MetadataController(
        IExternalRoleDefinitionPersistence externalRoleDefinitionPersistence)
    {
        _externalRoleDefinitionPersistence = externalRoleDefinitionPersistence;
    }

    /// <summary>
    /// Gets metadata for all external roles.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>Metadata for all external roles.</returns>
    [HttpGet("external-roles")]
    public async Task<ActionResult<ListObject<ExternalRoleMetadata>>> GetRoleMetadata(
        CancellationToken cancellationToken = default)
    {
        var roles = await _externalRoleDefinitionPersistence.GetAllRoleDefinitions(cancellationToken);

        var result = ListObject.Create(roles.Select(static r => r.ToPartyExternalRoleMetadataContract()));
        return Ok(result);
    }
}
