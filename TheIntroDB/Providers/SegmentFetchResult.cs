using System.Collections.Generic;
using TheIntroDB.Models;

namespace TheIntroDB.Providers
{
    public class SegmentFetchResult
    {
        public IReadOnlyList<MediaSegmentData> Segments { get; }
        public bool IsRateLimited { get; }
        public bool IsError { get; }

        private SegmentFetchResult(IReadOnlyList<MediaSegmentData> segments, bool isRateLimited, bool isError)
        {
            Segments = segments;
            IsRateLimited = isRateLimited;
            IsError = isError;
        }

        public static SegmentFetchResult Success(IReadOnlyList<MediaSegmentData> segments) =>
            new(segments, false, false);

        public static SegmentFetchResult NotFound() =>
            new(System.Array.Empty<MediaSegmentData>(), false, false);

        public static SegmentFetchResult RateLimited() =>
            new(System.Array.Empty<MediaSegmentData>(), true, true);

        public static SegmentFetchResult Error() =>
            new(System.Array.Empty<MediaSegmentData>(), false, true);
    }
}
