using System.Text.Json;
using System.Text.Json.Serialization;
using Localtest.Tokens;

namespace Localtest.Emulated;

/// <summary>Source-generated JSON for the emulation's DTOs (Native AOT has no reflection serializer). Web defaults: camelCase.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Party))]
[JsonSerializable(typeof(Party[]))]
[JsonSerializable(typeof(Person))]
[JsonSerializable(typeof(Organization))]
[JsonSerializable(typeof(PartyLookup))]
[JsonSerializable(typeof(UrnQuery))]
[JsonSerializable(typeof(PartyQueryResult))]
[JsonSerializable(typeof(TestUserEntry[]))]
[JsonSerializable(typeof(UserProfile))]
[JsonSerializable(typeof(AuthorizedPartiesResponse))]
[JsonSerializable(typeof(DiscoveryDocument))]
[JsonSerializable(typeof(JsonWebKeySet))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
internal sealed partial class EmulationJsonContext : JsonSerializerContext;
