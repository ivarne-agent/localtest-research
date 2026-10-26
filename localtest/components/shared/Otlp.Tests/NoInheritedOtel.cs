using System.Collections;
using System.Runtime.CompilerServices;

namespace Localtest.Shared.Otlp.Tests;

/// <summary>
/// Removes every inherited <c>OTEL_*</c> variable before any test runs. The hosts under test read the environment (the
/// front door through its configuration, the SDK where no configuration is given), so a shell with
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> set (a <c>studioctl run</c> shell, a stack with the monitoring group) would send
/// the tests' telemetry to a real collector. The tests set the variables they need in the hosts' own configuration, and
/// only ever point them at an in-process stand-in on an ephemeral loopback port.
/// </summary>
internal static class NoInheritedOtel
{
    [ModuleInitializer]
    internal static void Clear()
    {
        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            if (variable.Key is string name && name.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }
}
