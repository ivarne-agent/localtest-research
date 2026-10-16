using System.Net;
using System.Text;
using System.Text.Json;
using Altinn.Register.Clients;
using Altinn.Register.Configuration;
using Altinn.Register.Contracts.V1;
using Altinn.Register.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Altinn.Register.Tests.UnitTests;

public class PersonClientTests
{
    private Mock<IOptions<GeneralSettings>> _generalSettingsOptions = new();
    private Mock<ILogger<PersonClient>> _personClientLogger = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public PersonClientTests()
    {
        GeneralSettings generalSettings = new() { BridgeApiEndpoint = "http://localhost/" };
        _generalSettingsOptions.Setup(s => s.Value).Returns(generalSettings);
    }

    [Fact]
    public async Task GetPerson_SblBridge_finds_person_Target_returns_Person()
    {
        // Arrange
        HttpRequestMessage? sblRequest = null;
        DelegatingHandlerStub messageHandler = new(async (request, token) =>
        {
            sblRequest = request;

            Person person = new Person
            {
                SSN = "thisperson",
                Name = "firstname làstnâme",
                LastName = "làstnâme",
                FirstName = "firstname",
            };
            return await CreateHttpResponseMessage(person);
        });

        var target = new PersonClient(
            new HttpClient(messageHandler), _generalSettingsOptions.Object, _personClientLogger.Object);

        // Act
        var actual = await target.GetPerson("thisperson", CancellationToken);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal("firstname", actual.FirstName);

        Assert.NotNull(sblRequest);
        Assert.Equal(HttpMethod.Post, sblRequest.Method);
        Assert.EndsWith($"/persons", sblRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetPerson_SblBridge_response_is_NotFound_Target_returns_null()
    {
        // Arrange
        HttpRequestMessage? sblRequest = null;
        DelegatingHandlerStub messageHandler = new(async (request, token) =>
        {
            sblRequest = request;
            return await Task.FromResult(new HttpResponseMessage() { StatusCode = HttpStatusCode.NotFound });
        });

        var target = new PersonClient(
            new HttpClient(messageHandler), _generalSettingsOptions.Object, _personClientLogger.Object);

        // Act
        var actual = await target.GetPerson("thisperson", CancellationToken);

        // Assert
        Assert.Null(actual);

        Assert.NotNull(sblRequest);
        Assert.Equal(HttpMethod.Post, sblRequest.Method);
        Assert.EndsWith($"/persons", sblRequest.RequestUri!.ToString());
    }

    private static async Task<HttpResponseMessage> CreateHttpResponseMessage(object obj)
    {
        string content = JsonSerializer.Serialize(obj);
        StringContent stringContent = new StringContent(content, Encoding.UTF8, "application/json");
        return await Task.FromResult(new HttpResponseMessage { Content = stringContent });
    }
}
