using System;
using System.Collections.Generic;
using System.Globalization;
using TheIntroDB.Configuration;
using TheIntroDB.Providers;
using TheIntroDB.Api;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Logging;
using System.Net.Http;

namespace TheIntroDB
{
    /// <summary>
    /// The main plugin.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly ILogger _logger;
        private readonly TheIntroDbSegmentProvider _segmentProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="libraryManager">The library manager.</param>
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger logger, ILibraryManager libraryManager)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _logger = logger;

            _logger.Info("TheIntroDB plugin loading...");

            // Register the segment provider as a singleton
            // This is needed for dependency injection in the scheduled task
            _segmentProvider = new TheIntroDbSegmentProvider(libraryManager, logger);

            _logger.Info("TheIntroDB plugin loaded successfully");
        }

        /// <inheritdoc />
        public override string Name => "TheIntroDB";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin Instance { get; private set; }

        /// <summary>
        /// Gets the segment provider instance.
        /// </summary>
        public TheIntroDbSegmentProvider SegmentProvider => _segmentProvider;

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            _logger.Info("TheIntroDB GetPages() called");

            var assembly = GetType().Assembly;
            var resourceName = "TheIntroDB.Configuration.configPage.html";

            // Check if the embedded resource exists
            var resourceNames = assembly.GetManifestResourceNames();
            _logger.Info("Available embedded resources: {0}", string.Join(", ", resourceNames));

            var exists = Array.Exists(resourceNames, name => name == resourceName);
            _logger.Info("Resource '{0}' exists: {1}", resourceName, exists);

            if (!exists)
            {
                _logger.Error("Configuration page resource not found!");
                return new PluginPageInfo[0];
            }

            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = resourceName,
                    EnableInMainMenu = true
                }
            };
        }
    }
}
