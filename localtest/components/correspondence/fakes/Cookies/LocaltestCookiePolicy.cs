#nullable enable
using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Localtest.Shared.Cookies;

/// <summary>
/// Cookies without <c>Secure</c> on plain http. Production code hard-codes <c>Secure = true</c> (Authentication's runtime,
/// party and session cookies) or <c>SameSite=None</c> with it; the stack is plain http, where a browser refuses both.
/// This puts ASP.NET Core's cookie policy middleware in front of the host's pipeline (an <see cref="IStartupFilter"/>, so
/// no vendored file changes): on a request that is not https every cookie appended or deleted through
/// <see cref="HttpResponse.Cookies"/> loses <c>Secure</c>, and <c>SameSite=None</c> becomes <c>Lax</c>. On https nothing
/// changes. The options are passed to the middleware directly, so a host's own <see cref="CookiePolicyOptions"/> are untouched.
/// </summary>
public static class LocaltestCookiePolicy
{
    public static IServiceCollection AddLocaltestCookiePolicy(this IServiceCollection services) =>
        services.AddTransient<IStartupFilter, StartupFilter>();

    /// <summary>The cookie on an http request: not <c>Secure</c>, and <c>Lax</c> where production says <c>None</c>.</summary>
    public static void ForPlainHttp(HttpContext context, CookieOptions options)
    {
        if (context.Request.IsHttps)
        {
            return;
        }

        options.Secure = false;
        if (options.SameSite == SameSiteMode.None)
        {
            options.SameSite = SameSiteMode.Lax;
        }
    }

    private sealed class StartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseCookiePolicy(new CookiePolicyOptions
            {
                OnAppendCookie = c => ForPlainHttp(c.Context, c.CookieOptions),
                OnDeleteCookie = c => ForPlainHttp(c.Context, c.CookieOptions),
            });
            next(app);
        };
    }
}
