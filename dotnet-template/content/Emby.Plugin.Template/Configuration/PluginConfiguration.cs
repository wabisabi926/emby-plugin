using MediaBrowser.Model.Plugins;

namespace Emby.Plugin.Template.Configuration
{
/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        AString = "string";
        AnInteger = 2;
    }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string AString { get; set; }

    /// <summary>
    /// Gets or sets an integer setting.
    /// </summary>
    public int AnInteger { get; set; }
}
}
