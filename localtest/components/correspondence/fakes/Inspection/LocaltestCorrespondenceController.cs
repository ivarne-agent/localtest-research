using Altinn.Correspondence.API.Swagger;
using Altinn.Correspondence.Core.Models.Enums;
using Altinn.Correspondence.Localtest.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Altinn.Correspondence.Localtest.Inspection;

/// <summary>
/// <c>correspondence/api/localtest/</c>: what a tester wants to see of a sent correspondence, anonymous and read-only (as
/// every <c>api/localtest/</c> endpoint in the stack). Not in production, not in Swagger. The Hangfire jobs replace a
/// Hangfire dashboard, which would need a third hook after <c>Build()</c>.
/// </summary>
[ApiController]
[AllowAnonymous]
[ExcludeFromSwagger]
[Route("correspondence/api/localtest")]
public sealed class LocaltestCorrespondenceController(LocaltestCorrespondenceQueries queries) : ControllerBase
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 1000;

    /// <summary>Correspondences in creation order, <c>{ items, next }</c>; <c>after</c> is the <c>next</c> of the previous page.</summary>
    [HttpGet("correspondences")]
    public async Task<IActionResult> List(
        [FromQuery] Guid? after,
        [FromQuery] string? resourceId,
        [FromQuery] string? sender,
        [FromQuery] string? recipient,
        [FromQuery] CorrespondenceStatus? status,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > MaxLimit)
        {
            return BadRequest($"limit must be between 1 and {MaxLimit}");
        }

        var page = await queries.List(after, resourceId, sender, recipient, status, limit ?? DefaultLimit, cancellationToken);
        return page is null ? BadRequest($"after: no correspondence {after}") : Ok(page);
    }

    /// <summary>One correspondence with its status history, content, attachments, notifications, references and Hangfire jobs.</summary>
    [HttpGet("correspondences/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => await queries.Get(id, cancellationToken) is { } correspondence ? Ok(correspondence) : NotFound();

    /// <summary>Hangfire jobs, newest first, optionally by state (<c>Failed</c>, <c>Scheduled</c>, <c>Processing</c>, <c>Enqueued</c>, <c>Succeeded</c>, <c>Deleted</c>, <c>Awaiting</c>).</summary>
    [HttpGet("jobs")]
    public async Task<IActionResult> Jobs([FromQuery] string? state, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > MaxLimit)
        {
            return BadRequest($"limit must be between 1 and {MaxLimit}");
        }

        return Ok(new { Items = await queries.Jobs(state, argument: null, limit ?? DefaultLimit, cancellationToken) });
    }

    /// <summary>Migrations (upstream swallows a failed migration, so /health does not show it), Hangfire servers, dialog-token keys.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken) => Ok(await queries.Status(cancellationToken));

    /// <summary>An attachment's bytes: the location URL and the "SAS" URL the file store hands out.</summary>
    [HttpGet("blobs/{id:guid}")]
    public async Task<IActionResult> Blob(Guid id, [FromServices] FileStorageRepository store, CancellationToken cancellationToken)
    {
        var path = store.AttachmentPath(id);
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var fileName = await queries.AttachmentFileName(id, cancellationToken);
        return PhysicalFile(path, "application/octet-stream", fileName);
    }

    /// <summary>A daily report file (the location URL of an uploaded report).</summary>
    [HttpGet("reports/{name}")]
    public IActionResult Report(string name, [FromServices] IOptions<LocaltestCorrespondenceOptions> options)
    {
        var directory = Path.GetFullPath(Path.Combine(options.Value.BlobRootPath, "reports"));
        var path = Path.GetFullPath(Path.Combine(directory, name));
        return path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal) && System.IO.File.Exists(path)
            ? PhysicalFile(path, "application/octet-stream", name)
            : NotFound();
    }
}
