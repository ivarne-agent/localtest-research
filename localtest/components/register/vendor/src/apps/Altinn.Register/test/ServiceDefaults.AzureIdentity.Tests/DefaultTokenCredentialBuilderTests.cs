using Azure.Core;
using Azure.Identity;

namespace Altinn.Authorization.ServiceDefaults.AzureIdentity.Tests;

public class DefaultTokenCredentialBuilderTests
{
    [Fact]
    public void Name_WhenSetToNull_Throws()
    {
        var builder = new DefaultTokenCredentialBuilder();

        var ex = Should.Throw<ArgumentNullException>(() => builder.Name = null!);

        ex.ParamName.ShouldBe("value");
    }

    [Fact]
    public void Build_WhenNoCredentialsAreConfigured_Throws()
    {
        var builder = new DefaultTokenCredentialBuilder();

        var ex = Should.Throw<InvalidOperationException>(() => builder.Build());

        ex.Message.ShouldBe("At least one credential must be configured.");
    }

    [Fact]
    public void Build_WhenOneCredentialIsConfigured_ReturnsCredential()
    {
        var credential = new TestTokenCredential();
        var builder = new DefaultTokenCredentialBuilder();
        builder.Credentials.Add(credential);

        var result = builder.Build();

        result.ShouldBeSameAs(credential);
    }

    [Fact]
    public void Build_WhenMultipleCredentialsAreConfigured_ReturnsChainedTokenCredential()
    {
        var builder = new DefaultTokenCredentialBuilder();
        builder.Credentials.Add(new TestTokenCredential());
        builder.Credentials.Add(new TestTokenCredential());

        var result = builder.Build();

        result.ShouldBeOfType<ChainedTokenCredential>();
    }

    private sealed class TestTokenCredential
        : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
