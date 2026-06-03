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

namespace TheIntroDB.Services {
  /// <summary>
  /// Scans the library and fetches TheIntroDB segments for supported items.
  /// </summary>
  public class TheIntroDbLibraryScanner {
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
      ILogger logger) {
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
    public async Task < int > ScanLibraryAsync(
      Action < string, int, int > progress,
      CancellationToken cancellationToken) {
      _logger.Info("Starting library scan for TheIntroDB segments");

      var plugin = Plugin.Instance;
      if (plugin == null) {
        _logger.Error("TheIntroDB plugin instance is not available");
        return 0;
      }

      plugin.EnsureConfigurationInitialized();

      var config = plugin.Configuration;
      var requestedTypes = GetRequestedTypes(config);
      if (requestedTypes.Count == 0) {
        _logger.Info("TheIntroDB scan skipped: all segment types disabled in plugin settings");
        return 0;
      }

      var query = new InternalItemsQuery {
        IncludeItemTypes = new [] {
            "Movie",
            "Episode"
          },
          Recursive = true
      };

      var items = _libraryManager.GetItemList(query) ?? Array.Empty < BaseItem > ();
      if (items.Length == 0) {
        _logger.Warn("TheIntroDB scan: no items returned for IncludeItemTypes query. Falling back to broad query and filtering.");
        var fallback = _libraryManager.GetItemList(new InternalItemsQuery {
          Recursive = true
        }) ?? Array.Empty < BaseItem > ();
        items = fallback.Where(i => i is Episode || i is Movie).ToArray();
      }

      items = FilterSelectedItems(items, config);

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

      for (var i = 0; i < itemsToScan.Length; i++) {
        if (cancellationToken.IsCancellationRequested) {
          break;
        }

        var item = itemsToScan[i];

        try {
          plugin.EnsureConfigurationInitialized();
          config = plugin.Configuration;
          requestedTypes = GetRequestedTypes(config);
          if (requestedTypes.Count == 0) {
            processed++;
            continue;
          }

          var fetched = await _segmentProvider.GetMediaSegmentsAsync(
            item.Id, cancellationToken).ConfigureAwait(false);
          var storedSegments = fetched.Select(s => new StoredMediaSegment {
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
        } catch (Exception ex) {
          _logger.ErrorException(string.Format(
              "Error processing item {0}", item.Name), ex);
        }
      }

      _logger.Info("Library scan completed. Found {0} total segments ({1} cached + {2} newly scanned) in {3} items",
          totalSegments, cachedSegmentCount, totalSegments - cachedSegmentCount,
          cachedSegmentCount + processed);
      return totalSegments;
    }

    private BaseItem[] FilterSelectedItems(BaseItem[] items, PluginConfiguration config) {
      var selectedShowIds = GetSelectedShowIds(config);
      var selectedLibraryIds = GetSelectedLibraryIds(config);
      if (selectedShowIds.Count == 0 && selectedLibraryIds.Count == 0) {
        return items;
      }

      var filteredItems = items.Where(item => {
        if (IsItemInSelectedLibraries(item, selectedLibraryIds)) {
          return true;
        }

        var episode = item as Episode;
        return episode != null && IsEpisodeInSelectedShows(episode, selectedShowIds);
      }).ToArray();

      _logger.Info("TheIntroDB scan filter: {0} of {1} items match {2} selected shows and {3} selected libraries",
        filteredItems.Length, items.Length, selectedShowIds.Count, selectedLibraryIds.Count);
      return filteredItems;
    }

    private static HashSet<string> GetSelectedShowIds(PluginConfiguration config) {
      var selectedShowIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      if (config == null) {
        return selectedShowIds;
      }

      if (!string.IsNullOrWhiteSpace(config.SelectedShowIds)) {
        foreach (var selectedShowId in config.SelectedShowIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) {
          var normalizedSelectedShowId = NormalizeItemId(selectedShowId);
          if (!string.IsNullOrWhiteSpace(normalizedSelectedShowId)) {
            selectedShowIds.Add(normalizedSelectedShowId);
          }
        }
      }

      var legacySelectedShowId = NormalizeItemId(config.SelectedShowId);
      if (!string.IsNullOrWhiteSpace(legacySelectedShowId)) {
        selectedShowIds.Add(legacySelectedShowId);
      }

      return selectedShowIds;
    }

    private static HashSet<string> GetSelectedLibraryIds(PluginConfiguration config) {
      var selectedLibraryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      if (config == null || string.IsNullOrWhiteSpace(config.SelectedLibraryIds)) {
        return selectedLibraryIds;
      }

      foreach (var selectedLibraryId in config.SelectedLibraryIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) {
        var normalizedSelectedLibraryId = NormalizeItemId(selectedLibraryId);
        if (!string.IsNullOrWhiteSpace(normalizedSelectedLibraryId)) {
          selectedLibraryIds.Add(normalizedSelectedLibraryId);
        }
      }

      return selectedLibraryIds;
    }

    private static bool IsEpisodeInSelectedShows(Episode episode, HashSet<string> selectedShowIds) {
      if (episode == null || selectedShowIds.Count == 0 || episode.Series == null || episode.Series.InternalId <= 0) {
        return false;
      }

      return selectedShowIds.Contains(episode.Series.InternalId.ToString());
    }

    private bool IsItemInSelectedLibraries(BaseItem item, HashSet<string> selectedLibraryIds) {
      if (item == null || selectedLibraryIds.Count == 0 || item.InternalId <= 0) {
        return false;
      }

      var selectedLibraryInternalIds = selectedLibraryIds
        .Select(selectedLibraryId => {
          long parsedId;
          return long.TryParse(selectedLibraryId, out parsedId) ? parsedId : 0;
        })
        .Where(parsedId => parsedId > 0)
        .ToArray();

      if (selectedLibraryInternalIds.Length == 0) {
        return false;
      }

      var matchingItems = _libraryManager.GetItemList(new InternalItemsQuery {
        ItemIds = new[] { item.InternalId },
        AncestorIds = selectedLibraryInternalIds
      });

      return matchingItems != null && matchingItems.Length > 0;
    }

    private static string NormalizeItemId(string selectedItemId) {
      return string.IsNullOrWhiteSpace(selectedItemId) ? string.Empty : selectedItemId.Trim();
    }

    private static HashSet < MediaSegmentType > GetRequestedTypes(PluginConfiguration config) {
      var set = new HashSet < MediaSegmentType > ();
      if (config == null) {
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
