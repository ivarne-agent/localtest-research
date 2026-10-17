#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authorization.Localtest.Register;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Altinn.Platform.Authorization.Localtest.Controllers;

/// <summary>
/// Localtest-only helper: builds the XACML JSON request an app's PEP would send for a user, party, app and action, and
/// posts it to this host's production <c>authorization/api/v1/decision</c>. Anonymous, for smoke tests and debugging.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("authorization/api/localtest")]
public sealed class LocaltestDecisionController(IHttpClientFactory httpClientFactory, RegisterLocaltestClient register, IMemoryCache memoryCache) : ControllerBase
{
    /// <summary>
    /// Drops the cached register seed and every production PDP cache entry (profiles, parties, roles, policies), so a
    /// change in the register or a policy applies to the next request instead of after the cache timeouts.
    /// </summary>
    [HttpPost("refresh")]
    public IActionResult Refresh()
    {
        register.Invalidate();
        (memoryCache as MemoryCache)?.Compact(1.0);
        return NoContent();
    }

    [HttpGet("decision")]
    public async Task<IActionResult> Decide(
        [FromQuery] int userId,
        [FromQuery] int partyId,
        [FromQuery] string org,
        [FromQuery] string app,
        [FromQuery] string action = "read",
        [FromQuery] string? instanceId = null,
        [FromQuery] string? task = null,
        [FromQuery] int authenticationLevel = 2,
        CancellationToken cancellationToken = default)
    {
        var resource = new JsonArray
        {
            Attribute("urn:altinn:org", org),
            Attribute("urn:altinn:app", app),
            Attribute("urn:altinn:partyid", partyId.ToString()),
        };
        if (!string.IsNullOrEmpty(instanceId))
        {
            resource.Add(Attribute("urn:altinn:instance-id", instanceId));
        }

        if (!string.IsNullOrEmpty(task))
        {
            resource.Add(Attribute("urn:altinn:task", task));
        }

        var request = new JsonObject
        {
            ["Request"] = new JsonObject
            {
                ["ReturnPolicyIdList"] = false,
                ["AccessSubject"] = new JsonArray { new JsonObject { ["Attribute"] = new JsonArray { Attribute("urn:altinn:userid", userId.ToString()), Attribute("urn:altinn:authlevel", authenticationLevel.ToString()) } } },
                ["Action"] = new JsonArray { new JsonObject { ["Attribute"] = new JsonArray { Attribute("urn:oasis:names:tc:xacml:1.0:action:action-id", action, includeInResult: true) } } },
                ["Resource"] = new JsonArray { new JsonObject { ["Attribute"] = resource } },
            },
        };

        var self = new Uri($"{Request.Scheme}://{Request.Host}/authorization/api/v1/decision");
        using var client = httpClientFactory.CreateClient();
        using var response = await client.PostAsJsonAsync(self, request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ContentResult { StatusCode = (int)response.StatusCode, Content = body, ContentType = "application/json" };
    }

    private static JsonObject Attribute(string id, string value, bool includeInResult = false)
        => new()
        {
            ["AttributeId"] = id,
            ["Value"] = value,
            ["DataType"] = "http://www.w3.org/2001/XMLSchema#string",
            ["IncludeInResult"] = includeInResult,
        };
}
