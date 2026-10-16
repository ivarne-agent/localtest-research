using Altinn.Register.Core.Location;

namespace Altinn.Register.Tests.Mocks;

public class MockLocationLookupProvider
    : ILocationLookupProvider
{
    public ValueTask<ILocationLookup> GetLocationLookup(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
