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

            plugin.EnsureConfigurationInitialized();

            var config = plugin.Configuration;
            var requestedTypes = GetRequestedTypes(config);
            if (requestedTypes.Count == 0)
            {
                _logger.Info("TheIntroDB scan skipped: all segment types disabled in plugin settings");
                return 0;
            }

            var items = GetFilteredItems(config);

            // Pre-filter: when IgnoreMediaWithExistingSegments is enabled,
            // fetch all item IDs that already have segments in ONE batch query
            // and exclude them from the scan. This avoids thousands of
            // individual HasAnySegments/GetSegments calls inside the loop.
            BaseItem[] itemsToScan;
            int cachedSegmentCount = 0;

            if (config.IgnoreMediaWithExistingSegments)
            {
                var knownIds = _repository.GetAllSegmentedItemIds();
                var knownSet = new HashSet<long>(knownIds);

                itemsToScan = items
                    .Where(i => !knownSet.Contains(i.InternalId))
                    .ToArray();

                cachedSegmentCount = knownIds.Count;

                _logger.Info("TheIntroDB scan: {0} total items, {1} already have segments (cached), {2} items to scan",
                    items.Length, cachedSegmentCount, itemsToScan.Length);
            }
            else
            {
                itemsToScan = items;
            }

            var totalSegments = cachedSegmentCount;
            var processed = 0;
            var total = itemsToScan.Length;

            for (var i = 0; i < itemsToScan.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var item = itemsToScan[i];

                try
                {
                    plugin.EnsureConfigurationInitialized();
                    config = plugin.Configuration;
                    requestedTypes = GetRequestedTypes(config);
                    if (requestedTypes.Count == 0)
                    {
                        processed++;
                        continue;
                    }

                    var fetched = await _segmentProvider.GetMediaSegmentsAsync(
                      item.Id, cancellationToken).ConfigureAwait(false);
                    var storedSegments = fetched.Select(s => new StoredMediaSegment
                    {
                        ItemInternalId = item.InternalId,
                        Type = s.Type,
                        StartTicks = s.StartTicks,
                        EndTicks = s.EndTicks
                    }).ToList();

                    _repository.ReplaceSegments(item.InternalId,
                      storedSegments, DateTime.UtcNow);

                    var inserted = _chapterMarkerWriter.ApplyMarkers(
                      item, storedSegments, config);

                    totalSegments += storedSegments.Count;
                    processed++;

                    progress?.Invoke(string.Format(
                        "Processed {0}: {1} segments (fetched), {2} markers added",
                        item.Name, storedSegments.Count, inserted),
                      processed, total);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException(string.Format(
                        "Error processing item {0}", item.Name), ex);
                }
            }

            _logger.Info("Library scan completed. Found {0} total segments ({1} cached + {2} newly scanned) in {3} items",
                totalSegments, cachedSegmentCount, totalSegments - cachedSegmentCount,
                cachedSegmentCount + processed);
            return totalSegments;
        }

        private static List<string> ParseCommaSeparatedIds(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            return raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
        }

        private BaseItem[] GetFilteredItems(PluginConfiguration config)
        {
            var selectedLibraryIds = ParseCommaSeparatedIds(config.SelectedLibraryIds);
            var selectedShowIds = ParseCommaSeparatedIds(config.SelectedShowIds);
            bool hasLibraryFilter = selectedLibraryIds.Count > 0;
            bool hasShowFilter = selectedShowIds.Count > 0;

            // Get all candidate items
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Movie", "Episode" },
                Recursive = true
            };

            var allItems = _libraryManager.GetItemList(query) ?? Array.Empty<BaseItem>();
            if (allItems.Length == 0)
            {
                _logger.Warn("TheIntroDB scan: no items returned for IncludeItemTypes query. Falling back to broad query and filtering.");
                var fallback = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Recursive = true
                }) ?? Array.Empty<BaseItem>();
                allItems = fallback.Where(i => i is Episode || i is Movie).ToArray();
            }

            // No filters: return all items
            if (!hasLibraryFilter && !hasShowFilter)
            {
                return allItems;
            }

            // Parse library GUIDs
            var libraryGuidSet = new HashSet<Guid>();
            foreach (var id in selectedLibraryIds)
            {
                if (Guid.TryParse(id, out var g))
                {
                    libraryGuidSet.Add(g);
                }
                else
                {
                    _logger.Warn("TheIntroDB scan: invalid library GUID '{0}' in SelectedLibraryIds, skipping", id);
                }
            }

            // Parse show IDs into series and movie sets
            var seriesGuidSet = new HashSet<Guid>();
            var movieGuidSet = new HashSet<Guid>();
            foreach (var id in selectedShowIds)
            {
                if (!Guid.TryParse(id, out var g))
                {
                    _logger.Warn("TheIntroDB scan: invalid GUID '{0}' in SelectedShowIds, skipping", id);
                    continue;
                }

                var item = _libraryManager.GetItemById(g);
                if (item is Series)
                {
                    seriesGuidSet.Add(g);
                }
                else if (item is Movie)
                {
                    movieGuidSet.Add(g);
                }
                else if (item != null)
                {
                    _logger.Warn("TheIntroDB scan: unexpected item type '{0}' in SelectedShowIds for '{1}', skipping", item.GetType().Name, item.Name);
                }
                else
                {
                    _logger.Warn("TheIntroDB scan: item not found for GUID '{0}' in SelectedShowIds, skipping", id);
                }
            }

            // Determine which items pass the filter by climbing parent chain.
            // For each item, walk up its parents and check if any ancestor
            // matches a selected library, series, or if the item itself is a
            // selected movie.
            BaseItem[] filteredItems;

            if (hasLibraryFilter || hasShowFilter)
            {
                filteredItems = allItems.Where(item =>
                {
                    if (item == null) return false;

                    // Walk the parent chain to check membership
                    BaseItem current = item;
                    while (current != null)
                    {
                        if (hasLibraryFilter && libraryGuidSet.Contains(current.Id))
                        {
                            return true;
                        }

                        if (hasShowFilter)
                        {
                            // A series in the parent chain means this item is an episode of that series
                            if (current is Series && seriesGuidSet.Contains(current.Id))
                            {
                                return true;
                            }
                        }

                        current = current.GetParent();
                    }

                    // Also check if this item itself is a selected movie
                    if (hasShowFilter && item is Movie && movieGuidSet.Contains(item.Id))
                    {
                        return true;
                    }

                    return false;
                }).ToArray();
            }
            else
            {
                filteredItems = allItems;
            }

            _logger.Info("TheIntroDB scan: {0} items after library/show filtering (from {1} total)", filteredItems.Length, allItems.Length);
            return filteredItems;
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
