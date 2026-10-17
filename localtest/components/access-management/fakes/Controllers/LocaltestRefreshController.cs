#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Altinn.AccessManagement.Localtest.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.AccessManagement.Localtest.Controllers;

/// <summary>
/// The app runner's hook: after an app's test data reached the Register (<c>POST register/api/localtest/apps</c>),
/// <c>POST accessmanagement/api/localtest/refresh</c> re-imports parties and roles from the Register and reapplies
/// the seed's own roles and access packages. Idempotent.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("accessmanagement/api/localtest")]
public sealed class LocaltestRefreshController(LocaltestRefreshService refresh) : ControllerBase
{
    [HttpPost("refresh")]
    [Produces("application/json")]
    public async Task<ActionResult<LocaltestRefreshService.RefreshSummary>> Refresh(CancellationToken cancellationToken)
        => Ok(await refresh.RunAsync(importFromRegister: true, cancellationToken));
}
