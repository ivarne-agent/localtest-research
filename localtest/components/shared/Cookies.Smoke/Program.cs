// Smoke test of the shared cookie policy (fakes/Cookies/), in memory: a host with the policy registered as the consumers
// register it writes the cookies production writes (Secure with Lax, Secure with None, a deletion with Secure, one
// without anything), over http and over https. Exits non-zero when one is not as expected.
using Localtest.Shared.Cookies;
using Microsoft.AspNetCore.TestHost;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseTestServer();
builder.Logging.ClearProviders();
builder.Services.AddLocaltestCookiePolicy();
var app = builder.Build();
app.MapGet("/cookies", (HttpContext context) =>
{
    var cookies = context.Response.Cookies;
    cookies.Append("Lax", "1", new CookieOptions { Secure = true, SameSite = SameSiteMode.Lax, HttpOnly = true, Path = "/" });
    cookies.Append("None", "2", new CookieOptions { Secure = true, SameSite = SameSiteMode.None, HttpOnly = true });
    cookies.Delete("Deleted", new CookieOptions { Secure = true, Path = "/" });
    cookies.Append("Plain", "3");
    return Results.Ok();
});
await app.StartAsync();

var failed = false;
void Expect(string name, string expected, string actual)
{
    var ok = string.Equals(expected, actual, StringComparison.Ordinal);
    failed |= !ok;
    Console.WriteLine(ok ? $"ok   {name}" : $"FAIL {name} (expected {expected}, got {actual})");
}

async Task<Dictionary<string, string>> Cookies(string scheme)
{
    var client = app.GetTestClient();
    client.BaseAddress = new Uri($"{scheme}://local.altinn.cloud/");
    using var response = await client.GetAsync("cookies");
    response.EnsureSuccessStatusCode();
    // name -> its attributes, lower case and sorted, without the value and Expires
    return response.Headers.GetValues("Set-Cookie").ToDictionary(
        c => c.Split('=', 2)[0],
        c => string.Join(";", c.Split(';').Skip(1).Select(a => a.Trim().ToLowerInvariant()).Where(a => !a.StartsWith("expires=", StringComparison.Ordinal)).Order(StringComparer.Ordinal)));
}

var http = await Cookies("http");
Expect("http: Secure removed, Lax kept", "httponly;path=/;samesite=lax", http["Lax"]);
Expect("http: Secure removed, None becomes Lax", "httponly;path=/;samesite=lax", http["None"]);
Expect("http: a deletion without Secure", "path=/", http["Deleted"]);
Expect("http: a cookie without attributes unchanged", "path=/", http["Plain"]);

var https = await Cookies("https");
Expect("https: Lax unchanged", "httponly;path=/;samesite=lax;secure", https["Lax"]);
Expect("https: None unchanged", "httponly;path=/;samesite=none;secure", https["None"]);
Expect("https: the deletion unchanged", "path=/;secure", https["Deleted"]);

await app.StopAsync();
return failed ? 1 : 0;
