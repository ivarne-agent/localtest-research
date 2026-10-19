using System.Text.Json;
using System.Text.Json.Serialization;

using Localtest.IdPorten;
using Localtest.Proxy;

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
internal sealed partial class LocaltestJsonContext : JsonSerializerContext;
