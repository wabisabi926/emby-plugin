using MediaBrowser.Model.Plugins;

namespace TheIntroDB.Configuration
{
    /// <summary>
    /// Plugin configuration for TheIntroDB integration.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
        /// </summary>
        public PluginConfiguration()
        {
            ApiKey = string.Empty;
            EnableIntro = true;
            EnableRecap = true;
            EnableCredits = true;
            EnablePreview = true;
            IgnoreMediaWithExistingSegments = false;
        }

        /// <summary>
        /// Gets or sets the optional API key (Bearer token).
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use intro segments.
        /// </summary>
        public bool EnableIntro { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use recap segments.
        /// </summary>
        public bool EnableRecap { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use credits segments.
        /// </summary>
        public bool EnableCredits { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use preview segments.
        /// </summary>
        public bool EnablePreview { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to ignore media that already has segments.
        /// </summary>
        public bool IgnoreMediaWithExistingSegments { get; set; }
    }
}
