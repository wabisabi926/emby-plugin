using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Session;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using TheIntroDB.Configuration;
using TheIntroDB.Data;
using TheIntroDB.Models;
using TheIntroDB.Services;

namespace TheIntroDB
{
    /// <summary>
    /// The main plugin.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasThumbImage, IHasWebPages
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <inheritdoc />
        public override string Name => "TheIntroDB";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("424b8e01-03d2-40a1-ba58-a2b9306f115d");

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin Instance { get; private set; }

        internal static DateTime RateLimitExpiryUtc { get; set; }

        internal void EnsureConfigurationInitialized()
        {
            PluginConfiguration config;
            try
            {
                config = Configuration;
            }
            catch
            {
                return;
            }

            if (config == null)
            {
                return;
            }

            if (config.SchemaVersion > 0)
            {
                return;
            }

            if (config.EnableIntro || config.EnableRecap || config.EnableCredits || config.EnablePreview || config.IgnoreMediaWithExistingSegments || config.EnableAnonymousUsageReporting)
            {
                config.SchemaVersion = 1;
                try
                {
                    SaveConfiguration();
                }
                catch
                {
                }
                return;
            }

            config.SchemaVersion = 1;
            config.ApiKey = config.ApiKey ?? string.Empty;
            config.EnableIntro = true;
            config.EnableRecap = true;
            config.EnableCredits = true;
            config.EnablePreview = true;
            config.IgnoreMediaWithExistingSegments = true;
            config.EnableAnonymousUsageReporting = true;

            try
            {
                SaveConfiguration();
            }
            catch
            {
            }
        }

        internal static void TrackAnonymousUsageEvent(string eventName, Dictionary<string, object> props)
        {
            var instance = Instance;
            if (instance == null)
            {
                return;
            }

            AnonymousUsageReporter.TrackEvent(instance, eventName, props);
        }

        internal static void TrackAnonymousUsagePluginLoaded()
        {
            var instance = Instance;
            if (instance == null)
            {
                return;
            }

            try
            {
                instance.EnsureConfigurationInitialized();
            }
            catch
            {
            }

            try
            {
                AnonymousUsageReporter.TrackPluginLoaded(instance);
            }
            catch
            {
            }
        }

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".thumb.png");
        }

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "TheIntroDBConfigurationPage",
                    EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
                    EnableInMainMenu = true,
                    DisplayName = "TheIntroDB"
                },
                new PluginPageInfo
                {
                    Name = "TheIntroDBConfigurationPageJS",
                    EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.js", GetType().Namespace)
                },
            };
        }

        private static class AnonymousUsageReporter
        {
            private static readonly HttpClient HttpClient = new HttpClient();
            private static readonly string SessionId = NewSessionId();
            private const string AppKey = "A-SH-4840082526";
            private const string Host = "https://analytics.theintrodb.org";

            public static void TrackPluginLoaded(Plugin plugin)
            {
                PluginConfiguration config = null;
                try
                {
                    config = plugin == null ? null : plugin.Configuration;
                }
                catch
                {
                }
                TrackEvent(
                    plugin,
                    "plugin_loaded",
                    new Dictionary<string, object>
                    {
                        ["host"] = "emby",
                        ["enable_intro"] = config != null && config.EnableIntro ? 1 : 0,
                        ["enable_recap"] = config != null && config.EnableRecap ? 1 : 0,
                        ["enable_credits"] = config != null && config.EnableCredits ? 1 : 0,
                        ["enable_preview"] = config != null && config.EnablePreview ? 1 : 0,
                        ["ignore_existing"] = config != null && config.IgnoreMediaWithExistingSegments ? 1 : 0,
                        ["has_theintrodb_api_key"] = config != null && !string.IsNullOrWhiteSpace(config.ApiKey) ? 1 : 0
                    });
            }

            public static void TrackEvent(Plugin plugin, string eventName, Dictionary<string, object> props)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await TrackEventAsync(plugin, eventName, props).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                });
            }

            private static async Task TrackEventAsync(Plugin plugin, string eventName, Dictionary<string, object> props)
            {
                PluginConfiguration config = null;
                try
                {
                    config = plugin?.Configuration;
                }
                catch
                {
                }
                if (config is null || !config.EnableAnonymousUsageReporting)
                {
                    return;
                }

                if (!Uri.TryCreate(Host, UriKind.Absolute, out var hostUri))
                {
                    return;
                }

                var version = plugin.GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0";
                var payload = new[]
                {
                    new AptabaseEvent
                    {
                        timestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                        sessionId = SessionId,
                        eventName = eventName,
                        systemProps = new Dictionary<string, object>
                        {
                            ["locale"] = CultureInfo.CurrentCulture.Name,
                            ["osName"] = Environment.OSVersion.Platform.ToString(),
                            ["osVersion"] = Environment.OSVersion.Version.ToString(),
                            ["isDebug"] =
#if DEBUG
                                true,
#else
                                false,
#endif
                            ["appVersion"] = version,
                            ["sdkVersion"] = "theintrodb-emby-plugin@" + version
                        },
                        props = MergeProps(
                            new Dictionary<string, object>
                            {
                                ["plugin"] = plugin.Name,
                                ["plugin_version"] = version
                            },
                            props)
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var requestUri = new Uri(hostUri.AbsoluteUri.TrimEnd('/') + "/api/v0/events", UriKind.Absolute);
                using (var request = new HttpRequestMessage(HttpMethod.Post, requestUri))
                {
                    request.Headers.TryAddWithoutValidation("App-Key", AppKey);
                    request.Headers.UserAgent.Clear();
                    request.Headers.UserAgent.Add(new ProductInfoHeaderValue("theintrodb-emby-plugin", version));
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using (var response = await HttpClient.SendAsync(request).ConfigureAwait(false))
                    {
                    }
                }
            }

            private static Dictionary<string, object> MergeProps(Dictionary<string, object> baseProps, Dictionary<string, object> extraProps)
            {
                if (extraProps == null || extraProps.Count == 0)
                {
                    return baseProps;
                }

                foreach (var kvp in extraProps)
                {
                    baseProps[kvp.Key] = kvp.Value;
                }

                return baseProps;
            }

            private static string NewSessionId()
            {
                var epochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var randomNumber = GetRandomInt32();
                return epochSeconds.ToString(CultureInfo.InvariantCulture) + randomNumber.ToString("D8", CultureInfo.InvariantCulture);
            }

            private static int GetRandomInt32()
            {
                var bytes = new byte[4];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(bytes);
                }

                var value = BitConverter.ToUInt32(bytes, 0);
                return (int)(value % 100000000);
            }

            private sealed class AptabaseEvent
            {
                public string timestamp { get; set; }
                public string sessionId { get; set; }
                public string eventName { get; set; }
                public Dictionary<string, object> systemProps { get; set; }
                public Dictionary<string, object> props { get; set; }

                public AptabaseEvent()
                {
                    timestamp = string.Empty;
                    sessionId = string.Empty;
                    eventName = string.Empty;
                    systemProps = new Dictionary<string, object>();
                    props = new Dictionary<string, object>();
                }
            }
        }
    }

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
            _logger = logManager.GetLogger("TheIntroDB");
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
                    catch
                    {
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
                _logger.Debug("Usage reporting playback monitor exception: " + ex.Message);
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
            _logger = logManager.GetLogger("TheIntroDB");
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
                _logger.Debug("TheIntroDB chapter marker persistence event handler exception: " + ex.Message);
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
                _logger.Debug("TheIntroDB chapter marker persistence exception: " + ex.Message);
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
