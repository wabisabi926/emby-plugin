using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using TheIntroDB.Providers;

namespace TheIntroDB.Tasks
{
    /// <summary>
    /// Scheduled task that scans the library for media segments from TheIntroDB
    /// </summary>
    public class TheIntroDbMediaSegmentScanTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly TheIntroDbSegmentProvider _segmentProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="TheIntroDbMediaSegmentScanTask"/> class.
        /// </summary>
        /// <param name="libraryManager">Library manager</param>
        /// <param name="logger">Logger</param>
        /// <param name="segmentProvider">Segment provider</param>
        public TheIntroDbMediaSegmentScanTask(
            ILibraryManager libraryManager,
            ILogger logger,
            TheIntroDbSegmentProvider segmentProvider)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _segmentProvider = segmentProvider;
        }

        /// <inheritdoc />
        public string Name => "TheIntroDB Media Segment Scan";

        /// <inheritdoc />
        public string Key => "TheIntroDBMediaSegmentScan";

        /// <inheritdoc />
        public string Description => "Scans your media library for intro, recap, credits, and preview segments from TheIntroDB";

        /// <inheritdoc />
        public string Category => "Library";

        /// <inheritdoc />
        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("Starting TheIntroDB media segment scan");

            try
            {
                var totalSegments = 0;
                var processed = 0;

                await _segmentProvider.ScanLibraryAsync(
                    new Action<string, int, int>((message, current, total) =>
                    {
                        processed = current;
                        var percentComplete = total > 0 ? (double)current / total * 100 : 0;
                        progress.Report(percentComplete);
                        _logger.Info("{0} ({1}/{2})", message, current, total);
                    }),
                    cancellationToken).ConfigureAwait(false);

                _logger.Info("TheIntroDB media segment scan completed successfully. Found {0} segments.", totalSegments);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error during TheIntroDB media segment scan", ex);
                throw;
            }
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Run daily at 2 AM
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(2).Ticks,
                    MaxRuntimeTicks = TimeSpan.FromHours(6).Ticks
                }
            };
        }
    }
}
