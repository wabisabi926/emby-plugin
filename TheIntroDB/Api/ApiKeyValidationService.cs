using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Services;

namespace TheIntroDB.Api
{
    public sealed class ApiKeyValidationService : IService
    {
        private static readonly Uri UserStatsUri = new Uri("https://api.theintrodb.org/v3/user/stats", UriKind.Absolute);
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        [Route("/TheIntroDB/Validation/ApiKeyStats", "POST", Summary = "Validate a TheIntroDB API key and return user stats")]
        public sealed class ValidateApiKeyStats : IReturn<ApiKeyValidationResponse>
        {
            public string ApiKey { get; set; }
        }

        public async Task<object> Post(ValidateApiKeyStats request)
        {
            return await ValidateAsync(request == null ? null : request.ApiKey, CancellationToken.None).ConfigureAwait(false);
        }

        private static async Task<ApiKeyValidationResponse> ValidateAsync(string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ApiKeyValidationResponse
                {
                    Error = "API key is required.",
                    StatusCode = (int)HttpStatusCode.BadRequest
                };
            }

            using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, UserStatsUri))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using (var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false))
                {
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var stats = DeserializeStats(responseBody);

                    if (response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(stats == null ? null : stats.Error))
                    {
                        return new ApiKeyValidationResponse
                        {
                            IsValid = true,
                            Stats = stats ?? new TheIntroDbUserStats(),
                            StatusCode = (int)response.StatusCode
                        };
                    }

                    return new ApiKeyValidationResponse
                    {
                        Error = stats == null || string.IsNullOrWhiteSpace(stats.Error) ? "Invalid or expired token" : stats.Error,
                        StatusCode = (int)response.StatusCode
                    };
                }
            }
        }

        private static TheIntroDbUserStats DeserializeStats(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<TheIntroDbUserStats>(responseBody, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public sealed class ApiKeyValidationResponse
    {
        public bool IsValid { get; set; }

        public string Error { get; set; }

        public TheIntroDbUserStats Stats { get; set; }

        public int StatusCode { get; set; }
    }

    public sealed class TheIntroDbUserStats
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("accepted")]
        public int Accepted { get; set; }

        [JsonPropertyName("pending")]
        public int Pending { get; set; }

        [JsonPropertyName("rejected")]
        public int Rejected { get; set; }

        [JsonPropertyName("acceptance_rate")]
        public double AcceptanceRate { get; set; }

        [JsonPropertyName("current_streak")]
        public int CurrentStreak { get; set; }

        [JsonPropertyName("best_streak")]
        public int BestStreak { get; set; }

        [JsonPropertyName("total_time_saved_ms")]
        public long TotalTimeSavedMs { get; set; }

        [JsonPropertyName("error")]
        public string Error { get; set; }
    }
}
