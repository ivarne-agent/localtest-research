using Altinn.Register.Contracts.V1;
using Altinn.Register.Extensions;
using Altinn.Register.ModelBinding;
using Altinn.Register.Tests.IntegrationTests.Utils;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.Tests.ModelBinding;

public class PartyComponentOptionModelBinderTests
    : IClassFixture<PartyComponentOptionModelBinderTests.Factory>
{
    private readonly Factory _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public PartyComponentOptionModelBinderTests(Factory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("includeComponents=person-name", PartyComponentOptions.NameComponents)]
    public async Task BindModelAsync_ValidQueries(string query, PartyComponentOptions expected)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/query?{query}", CancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync(CancellationToken);
        result.ShouldBe(((uint)expected).ToString());
    }

    public class TestController : ControllerBase
    {
        [HttpGet("/query")]
        public IActionResult Query([FromQuery(Name = "includeComponents")] PartyComponentOptions accessListIncludes)
        {
            return Ok((uint)accessListIncludes);
        }
    }

    public class Factory : TestControllerApplicationFactory<TestController>
    {
        protected override WebApplicationBuilder CreateWebApplicationBuilder()
        {
            var builder = base.CreateWebApplicationBuilder();

            builder.Services.Configure<MvcOptions>(options =>
            {
                options.ModelBinderProviders.InsertSingleton<PartyComponentOptionModelBinder>(0);
            });

            return builder;
        }
    }
}
