namespace Altinn.Studio.Localtest.Provisioning;

/// <summary>
/// Designer's <c>AuthorizationPolicyService.ReplacePolicyPlaceholderTokens</c>: an app's <c>policy.xml</c> may name
/// its own org and app as <c>[ORG]</c>/<c>[org]</c> and <c>[APP]</c>/<c>[app]</c>, replaced on deploy.
/// </summary>
internal static class PolicyPlaceholders
{
    public static string Replace(string policy, AppId appId) =>
        policy
            .Replace("[ORG]", appId.Org, StringComparison.Ordinal)
            .Replace("[org]", appId.Org, StringComparison.Ordinal)
            .Replace("[APP]", appId.App, StringComparison.Ordinal)
            .Replace("[app]", appId.App, StringComparison.Ordinal);
}
