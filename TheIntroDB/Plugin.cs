using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using TheIntroDB.Configuration;
using TheIntroDB.Data;
using TheIntroDB.Models;

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
            AnonymousUsageReporter.TrackPluginLoaded(this);
        }

        /// <inheritdoc />
        public override string Name => "TheIntroDB";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin Instance { get; private set; }

        internal static DateTime RateLimitExpiryUtc { get; set; }

        internal static void TrackAnonymousUsageEvent(string eventName, Dictionary<string, object> props)
        {
            var instance = Instance;
            if (instance == null)
            {
                return;
            }

            AnonymousUsageReporter.TrackEvent(instance, eventName, props);
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
                var config = plugin == null ? null : plugin.Configuration;
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
                var config = plugin?.Configuration;
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
            TheIntroDbSegmentRepository repository,
            ILogManager logManager)
        {
            _sessionManager = sessionManager;
            _repository = repository;
            _logger = logManager.GetLogger(Plugin.Instance.Name);
        }

        public void Run()
        {
            _sessionManager.PlaybackProgress += SessionManager_PlaybackProgress;
            _sessionManager.PlaybackStopped += SessionManager_PlaybackStopped;
        }

        public void Dispose()
        {
            _sessionManager.PlaybackProgress -= SessionManager_PlaybackProgress;
            _sessionManager.PlaybackStopped -= SessionManager_PlaybackStopped;
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
}
