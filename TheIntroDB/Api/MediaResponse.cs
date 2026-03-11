using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace TheIntroDB.Api
{
    /// <summary>
    /// Response from GET /media (TheIntroDB API).
    /// </summary>
    public class MediaResponse
    {
        /// <summary>
        /// Gets or sets the TMDB ID.
        /// </summary>
        [JsonProperty("tmdb_id")]
        public int TmdbId { get; set; }

        /// <summary>
        /// Gets or sets the type: "movie" or "tv".
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the season number (TV only).
        /// </summary>
        [JsonProperty("season")]
        public int? Season { get; set; }

        /// <summary>
        /// Gets or sets the episode number (TV only).
        /// </summary>
        [JsonProperty("episode")]
        public int? Episode { get; set; }

        /// <summary>
        /// Gets the intro segments (always present, may have null values).
        /// </summary>
        [JsonProperty("intro")]
        public Collection<SegmentTimestamp> Intro { get; } = new Collection<SegmentTimestamp>();

        /// <summary>
        /// Gets the recap segments (always present, may have null values).
        /// </summary>
        [JsonProperty("recap")]
        public Collection<SegmentTimestamp> Recap { get; } = new Collection<SegmentTimestamp>();

        /// <summary>
        /// Gets the credits segments (always present, may have null values).
        /// </summary>
        [JsonProperty("credits")]
        public Collection<SegmentTimestamp> Credits { get; } = new Collection<SegmentTimestamp>();

        /// <summary>
        /// Gets the preview segments (always present, may have null values).
        /// </summary>
        [JsonProperty("preview")]
        public Collection<SegmentTimestamp> Preview { get; } = new Collection<SegmentTimestamp>();
    }
}