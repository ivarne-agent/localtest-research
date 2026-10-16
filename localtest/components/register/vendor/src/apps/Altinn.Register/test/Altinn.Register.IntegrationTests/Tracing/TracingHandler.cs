using System.Collections.Immutable;
using System.Diagnostics;

namespace Altinn.Register.IntegrationTests.Tracing;

internal class TracingHandler(ImmutableArray<string> prefixes)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var activityVerb = request.Method.ToString().ToLowerInvariant();
        var relPath = request.RequestUri?.PathAndQuery switch
        {
            null => [],
            var path => path.AsSpan(),
        };

        if (!prefixes.IsDefaultOrEmpty)
        {
            foreach (var prefix in prefixes)
            {
                if (relPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    relPath = relPath[prefix.Length..];
                    break;
                }
            }
        }

        using var activity = IntegrationTestsActivities.Source.StartActivity(ActivityKind.Client, name: $"{activityVerb} {relPath}");
        if (activity is not null)
        {
            request.Headers.Add("traceparent", activity.Id);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
