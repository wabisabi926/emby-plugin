using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using TheIntroDB.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Logging;
using Newtonsoft.Json;

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
            CancellationToken cancellationToken)
        {
            var config = _plugin?.Configuration as PluginConfiguration;
            const string baseUrl = "https://api.theintrodb.org/v1";

            // Build query parameters
            var queryParams = new List<string>();

            if (tmdbId.HasValue && tmdbId.Value > 0)
            {
                queryParams.Add($"tmdb_id={tmdbId.Value}");
            }
            else if (!string.IsNullOrWhiteSpace(imdbId))
            {
                queryParams.Add($"imdb_id={imdbId}");
            }
            else
            {
                _logger.Warn("No TMDB or IMDB ID provided for media lookup");
                return null;
            }

            if (!isMovie)
            {
                if (season.HasValue)
                    queryParams.Add($"season={season.Value}");
                if (episode.HasValue)
                    queryParams.Add($"episode={episode.Value}");
            }

            var query = "?" + string.Join("&", queryParams);
            var requestUri = new Uri(baseUrl + "/media" + query, UriKind.Absolute);

            _logger.Info("Fetching media data from: {0}", requestUri);

            using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
            {
                if (!string.IsNullOrWhiteSpace(config?.ApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
                }

                request.Headers.TryAddWithoutValidation("Accept", "application/json");

                try
                {
                    await WaitForRateLimitAsync(cancellationToken).ConfigureAwait(false);
                    using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.Warn("API request failed with status: {0}", response.StatusCode);
                            return null;
                        }

                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var mediaResponse = JsonConvert.DeserializeObject<MediaResponse>(json);

                        _logger.Info("Successfully retrieved media data for {0}: {1}",
                            mediaResponse?.Type, mediaResponse?.TmdbId);
                        return mediaResponse;
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error fetching media data from TheIntroDB API", ex);
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
            var result = await GetMediaAsync(tmdbId, null, isMovie, season, episode, cancellationToken).ConfigureAwait(false);
            return result is null ? string.Empty : JsonConvert.SerializeObject(result);
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
