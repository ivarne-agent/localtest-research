namespace Localtest.Emulated;

/// <summary>
/// Which platform services the front door emulates (the minimal mode, <c>planned-components/minimal-emulation/</c>
/// section 3.2, option C): a service is emulated when the runtime gives it the front door's own address,
/// <c>services__{name}__http__0</c> = <see cref="SelfAddress"/> (<c>Localtest__SelfAddress</c>, default
/// <c>http://localtest:8080</c>). No entry is still "not running" (503), a foreign address is still forwarded.
/// </summary>
public sealed class EmulationMode
{
    /// <summary>The front door's own address by default: its network alias and container port.</summary>
    public const string DefaultSelfAddress = "http://localtest:8080";

    /// <summary>The services the front door can emulate, by service name; <see cref="Prefixes"/> holds their paths.</summary>
    public static readonly IReadOnlyList<string> Emulatable =
    [
        "register",
        "profile",
        "authentication",
        "authorization",
        "access-management",
        "events",
        "resource-registry",
        "notifications",
        "pdf3",
    ];

    private readonly HashSet<string> _services;

    private EmulationMode(string selfAddress, IEnumerable<string> services)
    {
        SelfAddress = selfAddress;
        _services = new HashSet<string>(services, StringComparer.Ordinal);
    }

    public string SelfAddress { get; }

    /// <summary>The emulated services, in <see cref="Emulatable"/> order.</summary>
    public IEnumerable<string> Services => Emulatable.Where(_services.Contains);

    public bool Emulates(string service) => _services.Contains(service);

    /// <summary>
    /// The mode from the <c>services:{name}:http:0</c> entries: null when no service has the front door's address (the
    /// default stack: nothing is emulated). Throws when a service without an emulation has it, which would make the
    /// forwarder send that service's requests to itself, forever.
    /// </summary>
    public static EmulationMode? FromConfiguration(IConfiguration configuration)
    {
        var self = Normalise(configuration["Localtest:SelfAddress"] is { Length: > 0 } configured ? configured : DefaultSelfAddress);
        var own = configuration.GetSection("services").GetChildren()
            .Where(service => service["http:0"] is { Length: > 0 } address && Normalise(address) == self)
            .Select(service => service.Key)
            .ToList();
        if (own.Count == 0)
        {
            return null;
        }

        var unknown = own.Where(service => !Emulatable.Contains(service)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"services__<name>__http__0 is the front door's own address ({self}) for {string.Join(", ", unknown)}, which the front door "
                + $"cannot emulate: it would forward those requests to itself. Emulated services: {string.Join(", ", Emulatable)}.");
        }

        return new EmulationMode(self, own);
    }

    /// <summary>Two addresses are the same when scheme, host and port are (case-insensitive, no trailing slash).</summary>
    private static string Normalise(string address)
        => Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant()
            : address.Trim().TrimEnd('/').ToLowerInvariant();
}
