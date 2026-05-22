using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Logging;
using TheIntroDB.Configuration;

namespace TheIntroDB.Api
{
    /// <summary>
    /// HTTP client for TheIntroDB API (GET /media). Fetches media segment data for movies and TV episodes.
    /// Rate limit: ~30 requests per 10 seconds (per IP). We throttle to stay under this.
    /// </summary>
    public class TheIntroDbClient
    {
        private const int MaxRequestsPerWindow = 30;
        private static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MinDelayBetweenRequests = TimeSpan.FromMilliseconds(RateLimitWindow.TotalMilliseconds / MaxRequestsPerWindow);

        private static readonly SemaphoreSlim RateLimitLock = new SemaphoreSlim(1, 1);
        private static DateTime _lastRequestUtc = DateTime.MinValue;

        private readonly HttpClient _httpClient;
        private readonly Plugin _plugin;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TheIntroDbClient"/> class.
        /// </summary>
        /// <param name="httpClient">HTTP client for requests.</param>
        /// <param name="plugin">Plugin instance for configuration.</param>
        /// <param name="logger">Logger instance.</param>
        public TheIntroDbClient(HttpClient httpClient, Plugin plugin, ILogger logger)
        {
            _httpClient = httpClient;
            _plugin = plugin;
            _logger = logger;
        }

        /// <summary>
        /// Fetches media segment data for the given TMDB id (movie) or episode.
        /// </summary>
        /// <param name="tmdbId">TMDB ID of the movie or series.</param>
        /// <param name="imdbId">IMDB ID of the movie or series (optional fallback).</param>
        /// <param name="isMovie">True for movie, false for TV episode.</param>
        /// <param name="season">Season number (required for TV).</param>
        /// <param name="episode">Episode number (required for TV).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>MediaResponse object or null if not found or error.</returns>
        public async Task<MediaResponse> GetMediaAsync(
            int? tmdbId,
            string imdbId,
            bool isMovie,
            int? season,
            int? episode,
            long? durationMs,
            CancellationToken cancellationToken)
        {
            if (DateTime.UtcNow < Plugin.RateLimitExpiryUtc)
            {
                _logger.Warn(
                    "TheIntroDB API rate limit is currently active. Skipping request. The rate limit will reset at {0} UTC.",
                    Plugin.RateLimitExpiryUtc);
                Plugin.TrackAnonymousUsageEvent(
                    "theintrodb_api_media_fetch",
                    new Dictionary<string, object>
                    {
                        ["host"] = "emby",
                        ["result"] = "local_ratelimit_active",
                        ["media_type"] = isMovie ? "movie" : "episode",
                        ["has_tmdb"] = tmdbId.HasValue && tmdbId.Value > 0 ? 1 : 0,
                        ["has_imdb"] = !string.IsNullOrWhiteSpace(imdbId) ? 1 : 0,
                        ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(_plugin.Configuration?.ApiKey) ? 1 : 0
                    });
                return null;
            }

            var config = _plugin.Configuration ?? new PluginConfiguration();
            const string baseUrl = "https://api.theintrodb.org/v3";

            var tmdbIdValue = tmdbId.GetValueOrDefault();
            var hasTmdb = tmdbIdValue > 0;
            var hasImdb = !string.IsNullOrWhiteSpace(imdbId);

            if (!hasTmdb && !hasImdb)
            {
                return null;
            }

            Plugin.TrackAnonymousUsageEvent(
                "theintrodb_api_media_fetch",
                new Dictionary<string, object>
                {
                    ["host"] = "emby",
                    ["result"] = "request",
                    ["media_type"] = isMovie ? "movie" : "episode",
                    ["has_tmdb"] = hasTmdb ? 1 : 0,
                    ["has_imdb"] = hasImdb ? 1 : 0,
                    ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                });

            string query;
            if (hasTmdb)
            {
                query = isMovie
                    ? $"?tmdb_id={tmdbIdValue}"
                    : $"?tmdb_id={tmdbIdValue}&season={season}&episode={episode}";
            }
            else
            {
                var encodedImdb = Uri.EscapeDataString(imdbId);
                query = isMovie
                    ? $"?imdb_id={encodedImdb}"
                    : $"?imdb_id={encodedImdb}&season={season}&episode={episode}";
            }

            if (durationMs.HasValue && durationMs.Value > 0)
            {
                query += $"&duration_ms={durationMs.Value}";
            }

            var requestUri = new Uri(baseUrl + "/media" + query, UriKind.Absolute);
            _logger.Info("TheIntroDB API request: {0}", requestUri);

            using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
            {
                if (!string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
                }

                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                request.Headers.TryAddWithoutValidation("User-Agent", "TheIntroDB Emby Plugin");

                try
                {
                    await WaitForRateLimitAsync(cancellationToken).ConfigureAwait(false);
                    using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        _logger.Info("TheIntroDB API response: StatusCode={0} for {1}", response.StatusCode, requestUri);

                        if ((int)response.StatusCode == 429)
                        {
                            var retryAfterSeconds = GetRetryAfterSeconds(response.Headers);
                            Plugin.RateLimitExpiryUtc = DateTime.UtcNow.AddSeconds(retryAfterSeconds);
                            _logger.Warn(
                                "TheIntroDB API rate limit exceeded. Will not send requests until {0} UTC. Retry-after: {1}s",
                                Plugin.RateLimitExpiryUtc,
                                retryAfterSeconds);

                            Plugin.TrackAnonymousUsageEvent(
                                "theintrodb_api_media_fetch",
                                new Dictionary<string, object>
                                {
                                    ["host"] = "emby",
                                    ["result"] = "http_429",
                                    ["media_type"] = isMovie ? "movie" : "episode",
                                    ["has_tmdb"] = hasTmdb ? 1 : 0,
                                    ["has_imdb"] = hasImdb ? 1 : 0,
                                    ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                                });
                            return null;
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(body) && body.Length > 500)
                            {
                                body = body.Substring(0, 500) + "...";
                            }

                            _logger.Warn("TheIntroDB API error response body: {0}", string.IsNullOrEmpty(body) ? "(empty)" : body);
                            Plugin.TrackAnonymousUsageEvent(
                                "theintrodb_api_media_fetch",
                                new Dictionary<string, object>
                                {
                                    ["host"] = "emby",
                                    ["result"] = "http_error",
                                    ["status"] = (int)response.StatusCode,
                                    ["media_type"] = isMovie ? "movie" : "episode",
                                    ["has_tmdb"] = hasTmdb ? 1 : 0,
                                    ["has_imdb"] = hasImdb ? 1 : 0,
                                    ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                                });
                            return null;
                        }

                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var mediaResponse = JsonSerializer.Deserialize<MediaResponse>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (mediaResponse == null)
                        {
                            var body = json;
                            if (!string.IsNullOrEmpty(body) && body.Length > 500)
                            {
                                body = body.Substring(0, 500) + "...";
                            }
                            _logger.Warn("TheIntroDB API deserialize returned null. Body: {0}", string.IsNullOrEmpty(body) ? "(empty)" : body);
                        }
                        _logger.Debug(
                            "TheIntroDB API parsed response: IntroCount={0}, RecapCount={1}, CreditsCount={2}, PreviewCount={3}",
                            mediaResponse?.Intro?.Count ?? 0,
                            mediaResponse?.Recap?.Count ?? 0,
                            mediaResponse?.Credits?.Count ?? 0,
                            mediaResponse?.Preview?.Count ?? 0);

                        Plugin.TrackAnonymousUsageEvent(
                            "theintrodb_api_media_fetch",
                            new Dictionary<string, object>
                            {
                                ["host"] = "emby",
                                ["result"] = mediaResponse == null ? "success_null" : "success",
                                ["media_type"] = isMovie ? "movie" : "episode",
                                ["has_tmdb"] = hasTmdb ? 1 : 0,
                                ["has_imdb"] = hasImdb ? 1 : 0,
                                ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0,
                                ["intro_count"] = mediaResponse?.Intro?.Count ?? 0,
                                ["recap_count"] = mediaResponse?.Recap?.Count ?? 0,
                                ["credits_count"] = mediaResponse?.Credits?.Count ?? 0,
                                ["preview_count"] = mediaResponse?.Preview?.Count ?? 0
                            });

                        return mediaResponse;
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException(string.Format("TheIntroDB API request failed for {0}", requestUri), ex);
                    Plugin.TrackAnonymousUsageEvent(
                        "theintrodb_api_media_fetch",
                        new Dictionary<string, object>
                        {
                            ["host"] = "emby",
                            ["result"] = "exception",
                            ["media_type"] = isMovie ? "movie" : "episode",
                            ["has_tmdb"] = hasTmdb ? 1 : 0,
                            ["has_imdb"] = hasImdb ? 1 : 0,
                            ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                        });
                    return null;
                }
            }
        }

        /// <summary>
        /// Fetches media segment timestamps for the given TMDB id (movie) or episode.
        /// </summary>
        /// <param name="tmdbId">TMDB ID of the movie or series.</param>
        /// <param name="isMovie">True for movie, false for TV episode.</param>
        /// <param name="season">Season number (required for TV).</param>
        /// <param name="episode">Episode number (required for TV).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>JSON string response or null if not found or error.</returns>
        [Obsolete("Use GetMediaAsync instead")]
        public async Task<string> GetMediaJsonAsync(
            int tmdbId,
            bool isMovie,
            int? season,
            int? episode,
            CancellationToken cancellationToken)
        {
            var result = await GetMediaAsync(tmdbId, null, isMovie, season, episode, null, cancellationToken).ConfigureAwait(false);
            return result is null ? string.Empty : JsonSerializer.Serialize(result);
        }

        private static int GetRetryAfterSeconds(HttpResponseHeaders headers)
        {
            IEnumerable<string> usageResetValues;
            if (headers.TryGetValues("X-UsageLimit-Reset", out usageResetValues))
            {
                var usageResetValue = usageResetValues.FirstOrDefault();
                int usageResetSeconds;
                if (int.TryParse(usageResetValue, out usageResetSeconds))
                {
                    return usageResetSeconds;
                }
            }

            IEnumerable<string> rateResetValues;
            if (headers.TryGetValues("X-RateLimit-Reset", out rateResetValues))
            {
                var rateResetValue = rateResetValues.FirstOrDefault();
                int rateResetSeconds;
                if (int.TryParse(rateResetValue, out rateResetSeconds))
                {
                    return rateResetSeconds;
                }
            }

            if (headers.RetryAfter != null && headers.RetryAfter.Delta.HasValue)
            {
                return (int)headers.RetryAfter.Delta.Value.TotalSeconds;
            }

            return 300;
        }

        /// <summary>
        /// Waits if necessary to respect the API rate limit (30 requests per 10 seconds).
        /// </summary>
        private static async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
        {
            await RateLimitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = DateTime.UtcNow;
                var elapsed = now - _lastRequestUtc;
                if (elapsed < MinDelayBetweenRequests)
                {
                    var waitTime = MinDelayBetweenRequests - elapsed;
                    await Task.Delay(waitTime, cancellationToken).ConfigureAwait(false);
                }

                _lastRequestUtc = DateTime.UtcNow;
            }
            finally
            {
                RateLimitLock.Release();
            }
        }
    }
}
