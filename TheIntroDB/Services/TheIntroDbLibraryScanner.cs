using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using TheIntroDB.Configuration;
using TheIntroDB.Data;
using TheIntroDB.Models;
using TheIntroDB.Providers;

namespace TheIntroDB.Services
{
    /// <summary>
    /// Scans the library and fetches TheIntroDB segments for supported items.
    /// </summary>
    public class TheIntroDbLibraryScanner
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly TheIntroDbSegmentProvider _segmentProvider;
        private readonly TheIntroDbSegmentRepository _repository;
        private readonly TheIntroDbChapterMarkerWriter _chapterMarkerWriter;

        /// <summary>
        /// Initializes a new instance of the <see cref="TheIntroDbLibraryScanner"/> class.
        /// </summary>
        /// <param name="libraryManager">Library manager used to enumerate media items.</param>
        /// <param name="segmentProvider">Provider used to fetch segment data for each item.</param>
        /// <param name="logger">Logger instance.</param>
        public TheIntroDbLibraryScanner(
            ILibraryManager libraryManager,
            TheIntroDbSegmentProvider segmentProvider,
            TheIntroDbSegmentRepository repository,
            TheIntroDbChapterMarkerWriter chapterMarkerWriter,
            ILogger logger)
        {
            _libraryManager = libraryManager;
            _segmentProvider = segmentProvider;
            _repository = repository;
            _chapterMarkerWriter = chapterMarkerWriter;
            _logger = logger;
        }

        /// <summary>
        /// Scans all supported media items in the library for TheIntroDB segments.
        /// </summary>
        /// <param name="progress">Progress callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Total number of segments found.</returns>
        public async Task<int> ScanLibraryAsync(
            Action<string, int, int> progress,
            CancellationToken cancellationToken)
        {
            _logger.Info("Starting library scan for TheIntroDB segments");

            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.Error("TheIntroDB plugin instance is not available");
                return 0;
            }

            var config = plugin.Configuration;
            var requestedTypes = GetRequestedTypes(config);
            if (requestedTypes.Count == 0)
            {
                _logger.Info("TheIntroDB scan skipped: all segment types disabled in plugin settings");
                return 0;
            }

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { typeof(Movie).Name, typeof(Episode).Name },
                IsVirtualItem = false,
                Recursive = true
            };

            var items = _libraryManager.GetItemList(query) ?? new BaseItem[0];
            var totalSegments = 0;
            var processed = 0;
            var total = items.Length;

            for (var i = 0; i < items.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var item = items[i];

                try
                {
                    config = plugin.Configuration;
                    requestedTypes = GetRequestedTypes(config);
                    if (requestedTypes.Count == 0)
                    {
                        processed++;
                        continue;
                    }

                    IReadOnlyList<StoredMediaSegment> storedSegments;
                    var usedCache = false;

                    if (config.IgnoreMediaWithExistingSegments && _repository.HasAllSegmentTypes(item.InternalId, requestedTypes))
                    {
                        storedSegments = _repository.GetSegments(item.InternalId);
                        usedCache = true;
                    }
                    else
                    {
                        var fetched = await _segmentProvider.GetMediaSegmentsAsync(item.Id, cancellationToken).ConfigureAwait(false);
                        storedSegments = fetched.Select(s => new StoredMediaSegment
                        {
                            ItemInternalId = item.InternalId,
                            Type = s.Type,
                            StartTicks = s.StartTicks,
                            EndTicks = s.EndTicks
                        }).ToList();

                        _repository.ReplaceSegments(item.InternalId, storedSegments, DateTime.UtcNow);
                    }

                    var inserted = _chapterMarkerWriter.ApplyMarkers(item, storedSegments, config);

                    totalSegments += storedSegments.Count;
                    processed++;

                    if (progress != null)
                    {
                        var mode = usedCache ? "cached" : "fetched";
                        progress(string.Format("Processed {0}: {1} segments ({2}), {3} markers added", item.Name, storedSegments.Count, mode, inserted), processed, total);
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException(string.Format("Error processing item {0}", item.Name), ex);
                }
            }

            _logger.Info("Library scan completed. Found {0} total segments in {1} items", totalSegments, processed);
            return totalSegments;
        }

        private static HashSet<MediaSegmentType> GetRequestedTypes(PluginConfiguration config)
        {
            var set = new HashSet<MediaSegmentType>();
            if (config == null)
            {
                return set;
            }

            if (config.EnableIntro) set.Add(MediaSegmentType.Intro);
            if (config.EnableRecap) set.Add(MediaSegmentType.Recap);
            if (config.EnableCredits) set.Add(MediaSegmentType.Credits);
            if (config.EnablePreview) set.Add(MediaSegmentType.Preview);
            return set;
        }
    }
}
