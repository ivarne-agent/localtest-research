using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Core.A2.SblProfile;
using Altinn.Register.Core.Operations;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.PartyImport.A2;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Altinn.Register.Tests.UnitTests;

public class GetOrCreateSelfIdentifiedUserHandlerTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateSucceeds_ButEnqueueImportThrows_StillReturnsSuccess()
    {
        const string ExternalIdentity = "urn:altinn:person:idporten-email:flaky@example.com";

        var bridge = new Mock<ISblProfileBridgeClient>(MockBehavior.Strict);
        bridge.Setup(b => b.LookupUser(ExternalIdentity, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result<SblUserLookup>)SblUserLookup.NotFound);
        bridge.Setup(b => b.CreateUser(It.IsAny<SblUserProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result<SblUserProfile>)new SblUserProfile
            {
                UserId = 13,
                UserUuid = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                UserName = "epost:flaky@example.com",
                PartyId = 50005,
                ExternalIdentity = ExternalIdentity,
                UserType = 2,
            });

        var sender = new Mock<ICommandSender>(MockBehavior.Strict);
        sender.Setup(s => s.Send(It.IsAny<ImportA2PartyCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bus down"));

        var handler = new GetOrCreateSelfIdentifiedUserFromBridgeHandler(
            bridge.Object,
            sender.Object,
            TimeProvider.System,
            NullLogger<GetOrCreateSelfIdentifiedUserFromBridgeHandler>.Instance);

        var request = GetOrCreateSelfIdentifiedUserRequest.Email("flaky@example.com");

        var result = await handler.Handle(request, CancellationToken);

        result.IsProblem.ShouldBeFalse();
        result.Value!.Value!.UserIds.CurrentValue.Value.ShouldBe(13u);
        sender.Verify(s => s.Send(It.IsAny<ImportA2PartyCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
