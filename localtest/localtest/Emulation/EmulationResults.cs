using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Localtest.Emulated;

/// <summary>The emulation's problem details (RFC 9457) and request bodies, through the source-generated context only.</summary>
public static class EmulationResults
{
    /// <summary>A problem naming the emulated service and the minimal mode.</summary>
    public static IResult Problem(int status, string title, string detail, string service)
        => Results.Json(
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Extensions = { ["service"] = service, ["mode"] = "minimal" },
            },
            EmulationJsonContext.Default.ProblemDetails,
            contentType: "application/problem+json",
            statusCode: status);

    /// <summary>
    /// The answer for a route under an emulated prefix that the emulation does not have (spec risk 2): 501 with the
    /// path, so a call the emulation lags behind shows as a named failure, not as a wrong answer.
    /// </summary>
    public static IResult NotEmulated(string service, HttpRequest request)
        => Problem(
            StatusCodes.Status501NotImplemented,
            "Not emulated in minimal mode",
            $"{request.Method} {request.Path} is not emulated: the localtest container emulates {service} in minimal mode (planned-components/minimal-emulation), "
            + "and this route is not part of the emulation. Run the default stack (studioctl env up -r localtest-v2 without --minimal) for the real service.",
            service);
}

public static class EmulationJson
{
    /// <summary>The request body as <typeparamref name="T"/>; default for an empty or invalid body.</summary>
    public static async Task<T?> Read<T>(HttpRequest request, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync(request.Body, typeInfo, request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>The request body as a JSON object; null for an empty, invalid or non-object body.</summary>
    public static async Task<System.Text.Json.Nodes.JsonObject?> ReadObject(HttpRequest request)
    {
        try
        {
            return await System.Text.Json.Nodes.JsonNode.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted) as System.Text.Json.Nodes.JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The request body as a JSON document; null for an empty or invalid body.</summary>
    public static async Task<JsonDocument?> ReadDocument(HttpRequest request)
    {
        try
        {
            return await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
