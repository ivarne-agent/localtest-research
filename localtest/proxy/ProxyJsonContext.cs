using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocaltestProxy;

/// <summary>Source-generated JSON for everything the proxy reads or writes (Native AOT has no reflection serializer).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(AppRoute[]))]
[JsonSerializable(typeof(AppRouteRequest))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
internal sealed partial class ProxyJsonContext : JsonSerializerContext;
