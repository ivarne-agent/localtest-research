using System.Text.Json;
using System.Text.Json.Serialization;

using Localtest.Authentication;
using Localtest.IdPorten;
using Localtest.Maskinporten;
using Localtest.Proxy;
using Localtest.Tokens;

namespace Localtest;

/// <summary>Source-generated JSON for everything the localtest app reads or writes (Native AOT has no reflection serializer).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(AppRoute[]))]
[JsonSerializable(typeof(AppRouteRequest))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(TestUser[]))]
[JsonSerializable(typeof(JsonWebKeySet))]
[JsonSerializable(typeof(OpenIdConfiguration))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(TokenErrorResponse))]
[JsonSerializable(typeof(AuthorizationServerMetadata))]
[JsonSerializable(typeof(MaskinportenTokenResponse))]
[JsonSerializable(typeof(CodeResponse))]
// Results.Problem: Native AOT serialises ProblemDetails only through a source-generated context.
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
internal sealed partial class LocaltestJsonContext : JsonSerializerContext;
