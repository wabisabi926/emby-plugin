using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using TheIntroDB.Data;
using TheIntroDB.Models;

namespace TheIntroDB.EntryPoints
{
    public class TheIntroDbUsageReportingEntryPoint : IServerEntryPoint
    {
        private const long MinJumpTicks = 15 * TimeSpan.TicksPerSecond;
        private const long SegmentEndToleranceTicks = 5 * TimeSpan.TicksPerSecond;
        private static readonly TimeSpan MinReportInterval = TimeSpan.FromSeconds(30);

        private readonly ISessionManager _sessionManager;
        private readonly TheIntroDbSegmentRepository _repository;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, PlaybackState> _states = new ConcurrentDictionary<string, PlaybackState>();

        public TheIntroDbUsageReportingEntryPoint(
            ISessionManager sessionManager,
            IApplicationPaths applicationPaths,
            ILogManager logManager)
        {
            _sessionManager = sessionManager;
            _logger = Plugin.Instance?.FileLogger ?? logManager.GetLogger("TheIntroDB");
            _repository = new TheIntroDbSegmentRepository(_logger, applicationPaths);
        }

        public void Run()
        {
            _ = Task.Run(async () =>
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        Plugin.TrackAnonymousUsagePluginLoaded();
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("TrackAnonymousUsagePluginLoaded attempt {0}/5 failed: {1}", attempt + 1, ex.Message);
                        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    }
                }
            });
            _sessionManager.PlaybackProgress += SessionManager_PlaybackProgress;
            _sessionManager.PlaybackStopped += SessionManager_PlaybackStopped;
        }

        public void Dispose()
        {
            _sessionManager.PlaybackProgress -= SessionManager_PlaybackProgress;
            _sessionManager.PlaybackStopped -= SessionManager_PlaybackStopped;
            _repository.Dispose();
        }

        private void SessionManager_PlaybackStopped(object sender, PlaybackStopEventArgs e)
        {
            if (e?.Session?.Id == null)
            {
                return;
            }

            _states.TryRemove(e.Session.Id, out _);
        }

        private void SessionManager_PlaybackProgress(object sender, PlaybackProgressEventArgs e)
        {
            try
            {
                if (e?.Session?.Id == null || e.Item == null)
                {
                    return;
                }

                var sessionId = e.Session.Id;
                var currentTicks = e.PlaybackPositionTicks ?? 0;
                var itemInternalId = e.Item.InternalId;

                var state = _states.GetOrAdd(sessionId, _ => new PlaybackState(itemInternalId, currentTicks));
                if (state.ItemInternalId != itemInternalId)
                {
                    state.ItemInternalId = itemInternalId;
                    state.LastPositionTicks = currentTicks;
                    state.LastReportedKey = null;
                    state.LastReportedUtc = DateTime.MinValue;
                    return;
                }

                var lastTicks = state.LastPositionTicks;
                state.LastPositionTicks = currentTicks;

                if (lastTicks <= 0 || currentTicks <= 0)
                {
                    return;
                }

                var delta = currentTicks - lastTicks;
                if (delta < MinJumpTicks)
                {
                    return;
                }

                var segments = _repository.GetSegments(itemInternalId);
                if (segments == null || segments.Count == 0)
                {
                    return;
                }

                StoredMediaSegment matched = null;
                foreach (var s in segments)
                {
                    if (lastTicks >= s.StartTicks && lastTicks <= s.EndTicks)
                    {
                        matched = s;
                        break;
                    }
                }

                if (matched == null)
                {
                    return;
                }

                if (currentTicks < (matched.EndTicks - SegmentEndToleranceTicks))
                {
                    return;
                }

                var reportKey = matched.Type + ":" + matched.StartTicks.ToString(CultureInfo.InvariantCulture);
                if (state.LastReportedKey == reportKey && DateTime.UtcNow - state.LastReportedUtc < MinReportInterval)
                {
                    return;
                }

                state.LastReportedKey = reportKey;
                state.LastReportedUtc = DateTime.UtcNow;

                var config = Plugin.Instance == null ? null : Plugin.Instance.Configuration;
                Plugin.TrackAnonymousUsageEvent(
                    "segment_skipped",
                    new Dictionary<string, object>
                    {
                        ["host"] = "emby",
                        ["segment_type"] = matched.Type.ToString().ToLowerInvariant(),
                        ["has_theintrodb_api_key"] = config != null && !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0,
                        ["jump_seconds"] = (int)(delta / TimeSpan.TicksPerSecond)
                    });
            }
            catch (Exception ex)
            {
                _logger.Error("Usage reporting playback monitor exception: " + ex.Message);
            }
        }

        private sealed class PlaybackState
        {
            public long ItemInternalId;
            public long LastPositionTicks;
            public string LastReportedKey;
            public DateTime LastReportedUtc;

            public PlaybackState(long itemInternalId, long lastPositionTicks)
            {
                ItemInternalId = itemInternalId;
                LastPositionTicks = lastPositionTicks;
                LastReportedKey = null;
                LastReportedUtc = DateTime.MinValue;
            }
        }
    }
}
