using Azure.Core;
using Microsoft.Extensions.Options;

namespace Altinn.Authorization.ServiceDefaults.AzureIdentity.Tests;

public class TokenCredentialProviderTests
{
    [Fact]
    public void GetCredential_AppliesNamedBuilderActions()
    {
        var credential = new TestTokenCredential();
        var monitor = new TestOptionsMonitor<TokenCredentialOptions>(name =>
        {
            name.ShouldBe("storage");

            var options = new TokenCredentialOptions();
            options.TokenCredentialBuilderActions.Add(builder =>
            {
                builder.Name.ShouldBe("storage");
                builder.Credentials.Add(credential);
            });

            return options;
        });

        var provider = new DefaultTokenCredentialProvider(monitor);

        var result = provider.GetCredential("storage");

        result.ShouldBeSameAs(credential);
    }

    private sealed class TestOptionsMonitor<TOptions>(Func<string, TOptions> get)
        : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => Get(Options.DefaultName);

        public TOptions Get(string? name)
            => get(name ?? Options.DefaultName);

        public IDisposable? OnChange(Action<TOptions, string?> listener)
            => null;
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
