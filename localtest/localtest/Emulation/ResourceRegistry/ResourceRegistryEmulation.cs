namespace Localtest.Emulated;

/// <summary>
/// The Resource Registry in minimal mode: nothing calls it (the app, Storage and the workflow engine do not; provisioning
/// skips its step 3b), so every path under its prefix answers 501 problem details naming the mode, so that a tool that
/// needs it learns why.
/// </summary>
public static class ResourceRegistryEmulation
{
    public const string Service = "resource-registry";

    public static void Map(WebApplication app, string prefix)
        => app.Map(prefix + "/{**rest}", (HttpRequest request) => EmulationResults.Problem(
            StatusCodes.Status501NotImplemented,
            "The Resource Registry is not emulated in minimal mode",
            $"{request.Method} {request.Path}: minimal mode has no Resource Registry (nothing in the mode calls it). Run the default stack "
            + "(studioctl env up -r localtest-v2 without --minimal) for the real one.",
            Service));
}
