using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using TheIntroDB.Configuration;
using TheIntroDB.Data;
using TheIntroDB.Services;

namespace TheIntroDB.EntryPoints
{
    public sealed class TheIntroDbChapterMarkerPersistenceEntryPoint : IServerEntryPoint
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly TheIntroDbSegmentRepository _segmentRepository;
        private readonly TheIntroDbChapterMarkerWriter _chapterWriter;
        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<long, byte> _writesInProgress = new ConcurrentDictionary<long, byte>();

        public TheIntroDbChapterMarkerPersistenceEntryPoint(
            ILibraryManager libraryManager,
            IItemRepository itemRepository,
            IApplicationPaths applicationPaths,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _logger = Plugin.Instance?.FileLogger ?? logManager.GetLogger("TheIntroDB");
            _segmentRepository = new TheIntroDbSegmentRepository(_logger, applicationPaths);
            _chapterWriter = new TheIntroDbChapterMarkerWriter(_itemRepository, _logger);
        }

        public void Run()
        {
            _libraryManager.ItemUpdated += LibraryManager_ItemUpdated;
        }

        public void Dispose()
        {
            _libraryManager.ItemUpdated -= LibraryManager_ItemUpdated;
            _segmentRepository.Dispose();
        }

        private void LibraryManager_ItemUpdated(object sender, ItemChangeEventArgs e)
        {
            try
            {
                var item = e?.Item;
                if (item is not Episode && item is not Movie)
                {
                    return;
                }

                var config = Plugin.Instance?.Configuration;
                if (config == null)
                {
                    return;
                }

                if (!config.EnableIntro && !config.EnableRecap && !config.EnableCredits && !config.EnablePreview)
                {
                    return;
                }

                var internalId = item.InternalId;
                if (_writesInProgress.ContainsKey(internalId))
                {
                    return;
                }

                _ = Task.Run(() => EnsureMarkersApplied(item, config));
            }
            catch (Exception ex)
            {
                _logger.Error("TheIntroDB chapter marker persistence event handler exception: " + ex.Message);
            }
        }

        private void EnsureMarkersApplied(BaseItem item, PluginConfiguration config)
        {
            var internalId = item.InternalId;
            if (!_writesInProgress.TryAdd(internalId, 0))
            {
                return;
            }

            try
            {
                var segments = _segmentRepository.GetSegments(internalId);
                if (segments == null || segments.Count == 0)
                {
                    return;
                }

                var existingChapters = _itemRepository.GetChapters(item) ?? new List<ChapterInfo>();
                if (!NeedsMarkerApply(existingChapters, config))
                {
                    return;
                }

                _chapterWriter.ApplyMarkers(item, segments, config);
            }
            catch (Exception ex)
            {
                _logger.Error("TheIntroDB chapter marker persistence exception: " + ex.Message);
            }
            finally
            {
                _writesInProgress.TryRemove(internalId, out _);
            }
        }

        private static bool NeedsMarkerApply(IReadOnlyList<ChapterInfo> chapters, PluginConfiguration config)
        {
            var hasIntro = !config.EnableIntro;
            var hasRecap = !config.EnableRecap;
            var hasCredits = !config.EnableCredits;
            var hasPreview = !config.EnablePreview;

            foreach (var c in chapters)
            {
                if (!hasIntro)
                {
                    if (c.MarkerType == MarkerType.IntroStart ||
                        c.MarkerType == MarkerType.IntroEnd ||
                        string.Equals(c.Name, "IntroStartMarker", StringComparison.Ordinal) ||
                        string.Equals(c.Name, "IntroEndMarker", StringComparison.Ordinal))
                    {
                        hasIntro = true;
                    }
                }

                if (!hasCredits)
                {
                    if (c.MarkerType == MarkerType.CreditsStart ||
                        string.Equals(c.Name, "CreditsStartMarker", StringComparison.Ordinal))
                    {
                        hasCredits = true;
                    }
                }

                if (!hasRecap)
                {
                    if (string.Equals(c.Name, "Recap", StringComparison.Ordinal) ||
                        string.Equals(c.Name, "Recap End", StringComparison.Ordinal))
                    {
                        hasRecap = true;
                    }
                }

                if (!hasPreview)
                {
                    if (string.Equals(c.Name, "Preview", StringComparison.Ordinal) ||
                        string.Equals(c.Name, "Preview End", StringComparison.Ordinal))
                    {
                        hasPreview = true;
                    }
                }

                if (hasIntro && hasRecap && hasCredits && hasPreview)
                {
                    return false;
                }
            }

            return !(hasIntro && hasRecap && hasCredits && hasPreview);
        }
    }
}
