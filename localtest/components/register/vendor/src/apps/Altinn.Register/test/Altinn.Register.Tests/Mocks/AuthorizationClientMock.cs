using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Services.Interfaces;

namespace Altinn.Register.Tests.Mocks;

public class AuthorizationClientMock
    : IAuthorizationClient
{
    public Task<Result<bool>> ValidateSelectedParty(int userId, int partyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result<bool> isValid = true;

        if (userId == 2)
        {
            isValid = false;
        }

        return Task.FromResult(isValid);
    }
}
