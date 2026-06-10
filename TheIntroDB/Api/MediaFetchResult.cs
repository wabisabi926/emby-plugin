using System;

namespace TheIntroDB.Api
{
    public class MediaFetchResult
    {
        public MediaResponse Response { get; }
        public bool IsRateLimited { get; }
        public bool IsNotFound { get; }

        public bool IsError => IsRateLimited || (!IsNotFound && Response == null);

        private MediaFetchResult(MediaResponse response, bool isRateLimited, bool isNotFound)
        {
            Response = response;
            IsRateLimited = isRateLimited;
            IsNotFound = isNotFound;
        }

        public static MediaFetchResult Success(MediaResponse response) =>
            new(response, false, false);

        public static MediaFetchResult NotFound() =>
            new(null, false, true);

        public static MediaFetchResult RateLimited() =>
            new(null, true, false);

        public static MediaFetchResult Error() =>
            new(null, false, false);
    }
}
