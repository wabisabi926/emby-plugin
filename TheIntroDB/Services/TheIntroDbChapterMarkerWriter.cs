using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using TheIntroDB.Configuration;
using TheIntroDB.Models;

namespace TheIntroDB.Services
{
    public sealed class TheIntroDbChapterMarkerWriter
    {
        private readonly IItemRepository _itemRepository;
        private readonly ILogger _logger;

        public TheIntroDbChapterMarkerWriter(IItemRepository itemRepository, ILogger logger)
        {
            _itemRepository = itemRepository;
            _logger = logger;
        }

        public int ApplyMarkers(BaseItem item, IReadOnlyList<StoredMediaSegment> segments, PluginConfiguration config)
        {
            if (item == null || segments == null || segments.Count == 0 || config == null)
            {
                return 0;
            }

            var existing = _itemRepository.GetChapters(item) ?? new List<ChapterInfo>();
            var chapters = new List<ChapterInfo>(existing.Count + segments.Count * 2);

            foreach (var c in existing)
            {
                chapters.Add(new ChapterInfo
                {
                    Name = c.Name,
                    StartPositionTicks = c.StartPositionTicks,
                    MarkerType = c.MarkerType
                });
            }

            var added = 0;

            foreach (var s in segments.OrderBy(x => x.StartTicks))
            {
                switch (s.Type)
                {
                    case MediaSegmentType.Intro:
                        if (config.EnableIntro)
                        {
                            added += AddIntroMarkers(chapters, s);
                        }
                        break;
                    case MediaSegmentType.Recap:
                        if (config.EnableRecap)
                        {
                            added += AddChapterRange(chapters, "Recap", "Recap End", s);
                        }
                        break;
                    case MediaSegmentType.Credits:
                        if (config.EnableCredits)
                        {
                            added += AddCreditsMarkers(chapters, s);
                        }
                        break;
                    case MediaSegmentType.Preview:
                        if (config.EnablePreview)
                        {
                            added += AddChapterRange(chapters, "Preview", "Preview End", s);
                        }
                        break;
                }
            }

            var deduped = Deduplicate(chapters);
            deduped.Sort((a, b) => a.StartPositionTicks.CompareTo(b.StartPositionTicks));

            _itemRepository.SaveChapters(item.InternalId, deduped);
            _logger.Debug("TheIntroDB saved {0} chapters/markers for {1} ({2})", deduped.Count, item.Name, item.InternalId);

            return added;
        }

        private int AddIntroMarkers(List<ChapterInfo> chapters, StoredMediaSegment s)
        {
            var added = 0;

            if (s.StartTicks >= 0)
            {
                added += AddIfMissing(chapters, MarkerType.IntroStart, s.StartTicks, "IntroStartMarker");
                added += AddIfMissing(chapters, MarkerType.Chapter, s.StartTicks, "Intro");
            }

            if (s.EndTicks > s.StartTicks)
            {
                added += AddIfMissing(chapters, MarkerType.IntroEnd, s.EndTicks, "IntroEndMarker");
                added += AddIfMissing(chapters, MarkerType.Chapter, s.EndTicks, "Intro End");
            }

            return added;
        }

        private int AddCreditsMarkers(List<ChapterInfo> chapters, StoredMediaSegment s)
        {
            var added = 0;

            if (s.StartTicks >= 0)
            {
                added += AddIfMissing(chapters, MarkerType.CreditsStart, s.StartTicks, "CreditsStartMarker");
                added += AddIfMissing(chapters, MarkerType.Chapter, s.StartTicks, "Credits");
            }

            return added;
        }

        private int AddChapterRange(List<ChapterInfo> chapters, string startName, string endName, StoredMediaSegment s)
        {
            var added = 0;
            if (s.StartTicks >= 0)
            {
                added += AddIfMissing(chapters, MarkerType.Chapter, s.StartTicks, startName);
            }

            if (s.EndTicks > s.StartTicks)
            {
                added += AddIfMissing(chapters, MarkerType.Chapter, s.EndTicks, endName);
            }

            return added;
        }

        private static int AddIfMissing(List<ChapterInfo> chapters, MarkerType markerType, long startTicks, string name)
        {
            if (chapters.Any(c => c.MarkerType == markerType && c.StartPositionTicks == startTicks))
            {
                return 0;
            }

            chapters.Add(new ChapterInfo
            {
                Name = name,
                StartPositionTicks = startTicks,
                MarkerType = markerType
            });

            return 1;
        }

        private static List<ChapterInfo> Deduplicate(IEnumerable<ChapterInfo> chapters)
        {
            var list = new List<ChapterInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var c in chapters)
            {
                var key = ((int)c.MarkerType).ToString() + ":" + c.StartPositionTicks.ToString() + ":" + (c.Name ?? string.Empty);
                if (seen.Add(key))
                {
                    list.Add(c);
                }
            }

            return list;
        }
    }
}

