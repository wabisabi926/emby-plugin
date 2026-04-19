using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
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

        /// <summary>
        /// Initializes a new instance of the <see cref="TheIntroDbLibraryScanner"/> class.
        /// </summary>
        /// <param name="libraryManager">Library manager used to enumerate media items.</param>
        /// <param name="segmentProvider">Provider used to fetch segment data for each item.</param>
        /// <param name="logger">Logger instance.</param>
        public TheIntroDbLibraryScanner(
            ILibraryManager libraryManager,
            TheIntroDbSegmentProvider segmentProvider,
            ILogger logger)
        {
            _libraryManager = libraryManager;
            _segmentProvider = segmentProvider;
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
                    var segments = await _segmentProvider.GetMediaSegmentsAsync(item.Id, cancellationToken).ConfigureAwait(false);
                    totalSegments += segments.Count;
                    processed++;

                    if (progress != null)
                    {
                        progress(string.Format("Processed {0}: {1} segments found", item.Name, segments.Count), processed, total);
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
    }
}
