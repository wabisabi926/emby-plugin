using System.Text.Json.Serialization;
using System.Collections.Generic;

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
        [JsonPropertyName("tmdb_id")]
        public int TmdbId { get; set; }

        /// <summary>
        /// Gets or sets the type: "movie" or "tv".
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the season number (TV only).
        /// </summary>
        [JsonPropertyName("season")]
        public int? Season { get; set; }

        /// <summary>
        /// Gets or sets the episode number (TV only).
        /// </summary>
        [JsonPropertyName("episode")]
        public int? Episode { get; set; }

        /// <summary>
        /// Gets the intro segments (always present, may have null values).
        /// </summary>
        [JsonPropertyName("intro")]
        public List<SegmentTimestamp> Intro { get; set; } = new List<SegmentTimestamp>();

        /// <summary>
        /// Gets the recap segments (always present, may have null values).
        /// </summary>
        [JsonPropertyName("recap")]
        public List<SegmentTimestamp> Recap { get; set; } = new List<SegmentTimestamp>();

        /// <summary>
        /// Gets the credits segments (always present, may have null values).
        /// </summary>
        [JsonPropertyName("credits")]
        public List<SegmentTimestamp> Credits { get; set; } = new List<SegmentTimestamp>();

        /// <summary>
        /// Gets the preview segments (always present, may have null values).
        /// </summary>
        [JsonPropertyName("preview")]
        public List<SegmentTimestamp> Preview { get; set; } = new List<SegmentTimestamp>();
    }
}
