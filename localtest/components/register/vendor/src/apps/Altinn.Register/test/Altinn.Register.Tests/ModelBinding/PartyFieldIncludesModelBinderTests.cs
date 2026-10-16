using Altinn.Register.Core.Parties;
using Altinn.Register.Extensions;
using Altinn.Register.ModelBinding;
using Altinn.Register.Tests.IntegrationTests.Utils;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.Tests.ModelBinding;

public class PartyFieldIncludesModelBinderTests
    : IClassFixture<PartyFieldIncludesModelBinderTests.Factory>
{
    private readonly Factory _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public PartyFieldIncludesModelBinderTests(Factory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("fields=party", PartyFieldIncludes.Party)]
    [InlineData("fields=display-name,uuid", PartyFieldIncludes.PartyDisplayName | PartyFieldIncludes.PartyUuid)]
    [InlineData("fields=display-name&fields=uuid", PartyFieldIncludes.PartyDisplayName | PartyFieldIncludes.PartyUuid)]
    [InlineData("fields=identifiers,version", PartyFieldIncludes.Identifiers | PartyFieldIncludes.PartyVersionId)]
    public async Task ParseValid(string query, PartyFieldIncludes expected)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/parse?{query}", CancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync(CancellationToken);
        result.ShouldBe(((uint)expected).ToString());
    }

    [Theory]
    [InlineData(PartyFieldIncludes.Party, "party")]
    [InlineData(PartyFieldIncludes.PartyDisplayName | PartyFieldIncludes.PartyUuid, "uuid,display-name")]
    [InlineData(PartyFieldIncludes.Identifiers | PartyFieldIncludes.PartyVersionId, "identifiers,version")]
    public async Task Format(PartyFieldIncludes value, string expected)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/format?fields={(uint)value}", CancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync(CancellationToken);
        result.ShouldBe(expected);
    }

    public class TestController
        : ControllerBase
    {
        [HttpGet("/parse")]
        public IActionResult Parse([FromQuery(Name = "fields")] PartyFieldIncludes fields)
        {
            return Ok((uint)fields);
        }

        [HttpGet("/format")]
        public IActionResult Stringify([FromQuery(Name = "fields")] uint fields)
        {
            return Ok(PartyFieldIncludesModelBinder.Model.Format((PartyFieldIncludes)fields));
        }
    }

    public class Factory : TestControllerApplicationFactory<TestController>
    {
        protected override WebApplicationBuilder CreateWebApplicationBuilder()
        {
            var builder = base.CreateWebApplicationBuilder();

            builder.Services.Configure<MvcOptions>(options =>
            {
                options.ModelBinderProviders.InsertSingleton<PartyFieldIncludesModelBinder>(0);
            });

            return builder;
        }
    }
}
