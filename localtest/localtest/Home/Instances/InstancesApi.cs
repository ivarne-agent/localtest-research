using System.Reflection;
using Localtest.Proxy;

namespace Localtest.Home.Instances;

/// <summary>
/// A query page for Storage's instances, beside the Home pages: the form (<c>GET /Home/Instances</c>, static HTML and
/// plain JavaScript whose state is the URL), and the proxy behind it, which gets the token the URL names (a test user's
/// or a service owner's, as <c>/Home/GetTest…Token</c> gives them, a pasted one, or none), calls Storage by its service
/// name and renders HTML: the list (<c>/Home/Instances/Results</c>), one instance
/// (<c>/Home/Instances/{partyId}/{instanceGuid}</c>) and its data elements' downloads
/// (<c>…/data/{dataGuid}</c>), each with the same token. Anonymous, local only, like the rest of Home.
/// </summary>
public static class InstancesApi
{
    private const string FormResource = "Localtest.Home.Instances.instances.html";

    public static void Map(WebApplication app)
    {
        var form = ReadForm();
        app.MapGet("/Home/Instances", () => Results.Text(form, "text/html; charset=utf-8"));
        app.MapGet("/Home/Instances/Results", List);
        app.MapGet("/Home/Instances/{partyId:int}/{instanceGuid:guid}", Instance);
        app.MapGet("/Home/Instances/{partyId:int}/{instanceGuid:guid}/data/{dataGuid:guid}", Download);
    }

    private static string ReadForm()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FormResource)
            ?? throw new InvalidOperationException($"The embedded resource {FormResource} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<IResult> List(HttpContext context, StorageClient storage, TestTokens tokens, InstanceLinks links, PartyUrns partyUrns, CancellationToken cancellationToken)
    {
        var query = context.Request.Query;
        var (choice, bearer, failure) = await Token(query, tokens, cancellationToken);
        if (choice is null)
        {
            return InstancesPages.Error("Storage instances", failure!, query);
        }

        StorageClient.Answer answer;
        try
        {
            answer = await storage.Get("storage/api/v1/instances" + StorageQuery.ToStorage(query), bearer, StorageQuery.Identifier(query), cancellationToken);
        }
        catch (Exception ex) when (ServiceCalls.Unreachable(ex))
        {
            return InstancesPages.Error("Storage instances", Unreachable(ex), query);
        }

        var urns = links.Arbeidsflate ? await partyUrns.Lookup(cancellationToken) : null;
        return InstancesPages.List(query, choice, answer, links, urns);
    }

    private static async Task<IResult> Instance(HttpContext context, int partyId, Guid instanceGuid, StorageClient storage, TestTokens tokens, InstanceLinks links, PartyUrns partyUrns, CancellationToken cancellationToken)
    {
        var query = context.Request.Query;
        var (choice, bearer, failure) = await Token(query, tokens, cancellationToken);
        if (choice is null)
        {
            return InstancesPages.Error($"Instance {partyId}/{instanceGuid}", failure!, query);
        }

        StorageClient.Answer answer;
        try
        {
            answer = await storage.Get($"storage/api/v1/instances/{partyId}/{instanceGuid}", bearer, null, cancellationToken);
        }
        catch (Exception ex) when (ServiceCalls.Unreachable(ex))
        {
            return InstancesPages.Error($"Instance {partyId}/{instanceGuid}", Unreachable(ex), query);
        }

        var urns = links.Arbeidsflate ? await partyUrns.Lookup(cancellationToken) : null;
        return InstancesPages.Instance(partyId, instanceGuid, choice, answer, links, urns);
    }

    /// <summary>A data element through the proxy with the page's token: a plain link to Storage would carry no service-owner token.</summary>
    private static async Task Download(HttpContext context, int partyId, Guid instanceGuid, Guid dataGuid, StorageClient storage, TestTokens tokens, CancellationToken cancellationToken)
    {
        var query = context.Request.Query;
        var (choice, bearer, failure) = await Token(query, tokens, cancellationToken);
        if (choice is null)
        {
            await InstancesPages.Error("Download", failure!, query).ExecuteAsync(context);
            return;
        }

        HttpResponseMessage response;
        try
        {
            response = await storage.Send($"storage/api/v1/instances/{partyId}/{instanceGuid}/data/{dataGuid}", bearer, cancellationToken);
        }
        catch (Exception ex) when (ServiceCalls.Unreachable(ex))
        {
            await InstancesPages.Error("Download", Unreachable(ex), query).ExecuteAsync(context);
            return;
        }

        using (response)
        {
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.Headers.CacheControl = "no-store";
            if (response.Content.Headers.ContentType is { } contentType)
            {
                context.Response.ContentType = contentType.ToString();
            }

            if (response.Content.Headers.ContentLength is { } length)
            {
                context.Response.ContentLength = length;
            }

            if (response.Content.Headers.ContentDisposition is { } disposition)
            {
                context.Response.Headers.ContentDisposition = disposition.ToString();
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            await body.CopyToAsync(context.Response.Body, cancellationToken);
        }
    }

    /// <summary>The URL's token: the choice and the bearer token (null for none), or why there is none.</summary>
    private static async Task<(TokenChoice? Choice, string? Bearer, TestTokens.Failure? Failure)> Token(IQueryCollection query, TestTokens tokens, CancellationToken cancellationToken)
    {
        var (choice, error) = TokenChoice.Parse(query);
        if (choice is null)
        {
            return (null, null, new TestTokens.Failure(StatusCodes.Status400BadRequest, error!));
        }

        switch (choice.Kind)
        {
            case TokenKind.User:
                var (user, userFailure) = await tokens.ForUser(choice.UserId!.Value, choice.PartyId, choice.AuthenticationLevel, cancellationToken);
                return user is null ? (null, null, userFailure) : (choice, user.Jwt, null);
            case TokenKind.Org:
                var (org, orgFailure) = await tokens.ForOrg(choice.OrgNumber, choice.Scopes, cancellationToken);
                return org is null ? (null, null, orgFailure) : (choice, org, null);
            case TokenKind.Pasted:
                return (choice, choice.Token, null);
            default:
                return (choice, null, null);
        }
    }

    private static TestTokens.Failure Unreachable(Exception ex)
        => new(StatusCodes.Status502BadGateway, $"Could not reach Storage: {ex.Message}. Is the storage component running?");
}
