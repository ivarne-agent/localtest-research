using System.Runtime.CompilerServices;

namespace Altinn.Register.Contracts.Tests;

internal class ModuleInitializer
{
    private static int _initialized = 0;

    [ModuleInitializer]
    public static void Init()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            VerifierSettings.UseSplitModeForUniqueDirectory();
            UseProjectRelativeDirectory("Snapshots");
        }
    }
}
