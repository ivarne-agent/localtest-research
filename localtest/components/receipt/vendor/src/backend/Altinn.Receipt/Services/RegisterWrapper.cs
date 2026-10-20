using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Altinn.Common.AccessTokenClient.Services;
using Altinn.Platform.Receipt.Configuration;
using Altinn.Platform.Receipt.Extensions;
using Altinn.Platform.Receipt.Helpers;
using Altinn.Platform.Receipt.Services.Interfaces;
using Altinn.Platform.Register.Models;
using AltinnCore.Authentication.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Receipt.Services
{
    /// <summary>
    /// Wrapper for Altinn Platform Register services.
    /// </summary>
    public class RegisterWrapper : IRegister
    {
        private readonly HttpClient _client;
        private readonly IHttpContextAccessor _contextaccessor;
        private readonly IAccessTokenGenerator _accessTokenGenerator;

        /// <summary>
        /// Initializes a new instance of the <see cref="RegisterWrapper"/> class
        /// </summary>
        public RegisterWrapper(
            HttpClient httpClient,
            IHttpContextAccessor httpContextAccessor,
            IOptions<PlatformSettings> platformSettings,
            IAccessTokenGenerator accessTokenGenerator
        )
        {
            httpClient.BaseAddress = new Uri(platformSettings.Value.ApiRegisterEndpoint);
            httpClient.DefaultRequestHeaders.Add(
                platformSettings.Value.SubscriptionKeyHeaderName,
                platformSettings.Value.SubscriptionKey
            );
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _client = httpClient;
            _contextaccessor = httpContextAccessor;
            _accessTokenGenerator = accessTokenGenerator;
        }

        /// <inheritdoc/>
        public async Task<Party> GetParty(int partyId)
        {
            string token = JwtTokenUtil.GetTokenFromContext(_contextaccessor.HttpContext, "AltinnStudioRuntime");
            string url = $"parties/{partyId}";

            HttpResponseMessage response = await _client.GetAsync(
                token,
                url,
                _accessTokenGenerator.GenerateAccessToken("platform", "receipt")
            );

            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return await response.Content.ReadFromJsonAsync<Party>(JsonSerializerOptionsProvider.Options);
            }

            throw new PlatformHttpException(response, string.Empty);
        }
    }
}
