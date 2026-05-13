using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using TheIntroDB.Api;
using TheIntroDB.Configuration;
using TheIntroDB.Models;

namespace TheIntroDB.Providers
{
    /// <summary>
    /// Media segment provider that fetches intro/recap/credits/preview from TheIntroDB API.
    /// This provider works with Emby's task system and can be used to populate segment data.
    /// </summary>
    public class TheIntroDbSegmentProvider
    {
        private readonly HttpClient _httpClient;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TheIntroDbSegmentProvider"/> class.
        /// </summary>
        /// <param name="libraryManager">Library manager to resolve items.</param>
        /// <param name="logger">Logger instance.</param>
        public TheIntroDbSegmentProvider(
            ILibraryManager libraryManager,
            ILogger logger)
        {
            _httpClient = new HttpClient();
            _libraryManager = libraryManager;
            _logger = logger;
            _logger.Info("TheIntroDB segment provider constructed");
        }

        /// <summary>
        /// Gets the provider name.
        /// </summary>
        public string Name => Plugin.Instance?.Name ?? "TheIntroDB";

        /// <summary>
        /// Fetches segment data for a specific media item.
        /// </summary>
        /// <param name="itemId">The item ID to fetch segments for.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Collection of segment data or empty if not found.</returns>
        public async Task<IReadOnlyList<MediaSegmentData>> GetMediaSegmentsAsync(
            Guid itemId,
            CancellationToken cancellationToken)
        {
            _logger.Info("GetMediaSegmentsAsync called for ItemId={0}", itemId);

            if (Plugin.Instance is null)
            {
                _logger.Warn("Early exit: Plugin.Instance is null");
                return Array.Empty<MediaSegmentData>();
            }

            var config = Plugin.Instance.Configuration as PluginConfiguration;
            if (config is null)
            {
                _logger.Warn("Early exit: Plugin configuration is not PluginConfiguration");
                return Array.Empty<MediaSegmentData>();
            }

            var item = _libraryManager.GetItemById(itemId);
            if (item is null)
            {
                _logger.Warn("Early exit: item not found for ItemId={0}", itemId);
                return Array.Empty<MediaSegmentData>();
            }

            int? tmdbId = null;
            string imdbId = null;
            bool isMovie = false;
            int? season = null;
            int? episode = null;

            if (item is Movie movie)
            {
                isMovie = true;
                tmdbId = GetTmdbId(movie);
                imdbId = GetImdbId(movie);
                _logger.Info("Movie: Name={0}, TmdbId={1}, ImdbId={2}", item.Name, tmdbId, imdbId ?? "(none)");
            }
            else if (item is Episode ep)
            {
                tmdbId = GetTmdbId(ep) ?? GetTmdbId(ep.Series);
                imdbId = GetImdbId(ep) ?? GetImdbId(ep.Series);
                season = ep.ParentIndexNumber;
                episode = ep.IndexNumber;
                _logger.Info("Episode: Name={0}, Series={1}, S{2}E{3}, TmdbId={4}, ImdbId={5}",
                    item.Name, ep.SeriesName, season, episode, tmdbId, imdbId ?? "(none)");
            }

            if ((!tmdbId.HasValue || tmdbId.Value <= 0) && string.IsNullOrWhiteSpace(imdbId))
            {
                _logger.Warn("Early exit: no TmdbId or ImdbId for {0}", item.Name);
                return Array.Empty<MediaSegmentData>();
            }

            if (!isMovie && (!season.HasValue || !episode.HasValue))
            {
                _logger.Warn("Early exit: TV episode missing season/episode for {0}", item.Name);
                return Array.Empty<MediaSegmentData>();
            }

            _logger.Info("Fetching from TheIntroDB API: tmdbId={0}, imdbId={1}, isMovie={2}, season={3}, episode={4}",
                tmdbId, imdbId, isMovie, season, episode);

            var client = new TheIntroDbClient(_httpClient, Plugin.Instance, _logger);
            var media = await client.GetMediaAsync(tmdbId, imdbId, isMovie, season, episode, cancellationToken).ConfigureAwait(false);

            if (media is null)
            {
                _logger.Info("TheIntroDB API returned no data for {0}", item.Name);
                return Array.Empty<MediaSegmentData>();
            }

            long? runTimeTicks = item.RunTimeTicks;
            var segments = new List<MediaSegmentData>();

            if (config.EnableIntro)
            {
                AddSegments(media.Intro, true, MediaSegmentType.Intro, itemId, runTimeTicks, segments);
            }

            if (config.EnableRecap)
            {
                AddSegments(media.Recap, true, MediaSegmentType.Recap, itemId, runTimeTicks, segments);
            }

            if (config.EnableCredits)
            {
                AddSegments(media.Credits, false, MediaSegmentType.Credits, itemId, runTimeTicks, segments);
            }

            if (config.EnablePreview)
            {
                AddSegments(media.Preview, false, MediaSegmentType.Preview, itemId, runTimeTicks, segments);
            }

            _logger.Info("Returning {0} segments for {1}", segments.Count, item.Name);
            return segments;
        }

        /// <summary>
        /// Checks if the provider supports the given item type.
        /// </summary>
        /// <param name="item">The media item to check.</param>
        /// <returns>True if the item is supported (Movie or Episode).</returns>
        public bool Supports(BaseItem item)
        {
            var supported = item is Episode || item is Movie;
            _logger.Debug("Supports({0}, {1}): {2}", item?.Name ?? "null", item?.GetType().Name ?? "null", supported);
            return supported;
        }

        private static int? GetTmdbId(BaseItem item)
        {
            if (item?.ProviderIds is null)
            {
                return null;
            }

            var id = GetProviderId(item, "Tmdb");
            if (!string.IsNullOrWhiteSpace(id))
            {
                return int.TryParse(id, out var n) ? (int?)n : null;
            }

            return null;
        }

        private static string GetImdbId(BaseItem item)
        {
            if (item?.ProviderIds is null)
            {
                return null;
            }

            var id = GetProviderId(item, "Imdb");
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

        private static string GetProviderId(BaseItem item, string provider)
        {
            if (item?.ProviderIds is null || string.IsNullOrWhiteSpace(provider))
            {
                return null;
            }

            foreach (var kvp in item.ProviderIds)
            {
                if (string.Equals(kvp.Key, provider, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private static void AddSegments(
            IEnumerable<SegmentTimestamp> stamps,
            bool endRequired,
            MediaSegmentType type,
            Guid itemId,
            long? runTimeTicks,
            List<MediaSegmentData> segments)
        {
            if (stamps is null)
            {
                return;
            }

            foreach (var stamp in stamps)
            {
                if (stamp is null || !stamp.HasValidRange(endRequired))
                {
                    continue;
                }

                long startMs = stamp.StartMs ?? 0;
                long endMs;

                if (stamp.EndMs.HasValue && stamp.EndMs.Value > 0)
                {
                    endMs = stamp.EndMs.Value;
                }
                else if (runTimeTicks.HasValue && runTimeTicks.Value > 0)
                {
                    endMs = runTimeTicks.Value / TimeSpan.TicksPerMillisecond;
                }
                else
                {
                    continue;
                }

                if (endMs <= startMs)
                {
                    continue;
                }

                long startTicks = startMs * TimeSpan.TicksPerMillisecond;
                long endTicks = endMs * TimeSpan.TicksPerMillisecond;

                segments.Add(new MediaSegmentData
                {
                    ItemId = itemId,
                    Type = type,
                    StartTicks = startTicks,
                    EndTicks = endTicks
                });
            }
        }
    }
}
