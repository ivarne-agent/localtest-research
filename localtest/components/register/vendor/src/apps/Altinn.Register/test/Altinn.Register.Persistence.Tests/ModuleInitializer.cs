using System.Runtime.CompilerServices;
using Altinn.Authorization.ModelUtils.EnumUtils;
using Altinn.Register.Core.Parties;

namespace Altinn.Register.Persistence.Tests;

public static class ModuleInitializer
{
    private static readonly FlagsEnumModel<PartyFieldIncludes> _includesModel = FlagsEnumModel.Create<PartyFieldIncludes>();

    private static int _initialized = 0;

    [ModuleInitializer]
    public static void Init()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            VerifierSettings.NameForParameter<PartyFieldIncludes>(_includesModel.Format);
            VerifierSettings.NameForParameter<PartyQueryFilters>(static f => f.ToString());

            VerifierSettings.UseSplitModeForUniqueDirectory();
            UseProjectRelativeDirectory("Snapshots");
        }
    }
}
