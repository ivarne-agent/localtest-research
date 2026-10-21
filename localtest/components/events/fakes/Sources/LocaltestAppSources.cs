using System;
using System.Collections.Generic;

namespace Altinn.Platform.Events.Localtest;

/// <summary>
/// Tells a platform component's source from an app's on the one host the stack serves both from. Production tells them
/// apart by host (<c>apps.altinn.no</c> against <c>platform.altinn.no</c>); here <c>PlatformSettings:AppsDomain</c> is
/// the shared host, so the directive sites in <c>OutboundService.GetSourceFilter</c> and
/// <c>SubscriptionController.IsAppSubscription</c> also ask this (see README.md, "Directive sites").
/// </summary>
public static class LocaltestAppSources
{
    /// <summary>The first path segments of the components studioctl routes on the platform host.</summary>
    private static readonly HashSet<string> PlatformPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "accessmanagement",
        "authentication",
        "authorization",
        "correspondence",
        "dialogporten",
        "events",
        "localtest",
        "notifications",
        "profile",
        "receipt",
        "register",
        "resourceregistry",
        "storage",
    };

    /// <summary>
    /// True when the source's first path segment is a platform component's prefix, as in Dialogporten's
    /// <c>{platform}dialogporten/api/v1/enduser/dialogs/{id}</c>; an app's source starts with its org.
    /// </summary>
    public static bool IsPlatformComponentSource(Uri source)
    {
        if (source is null || !source.IsAbsoluteUri)
        {
            return false;
        }

        string path = source.AbsolutePath.TrimStart('/');
        int slash = path.IndexOf('/');
        string first = slash < 0 ? path : path[..slash];
        return PlatformPrefixes.Contains(first);
    }
}
