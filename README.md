# Emby Plugin (Template)

Minimal Emby Server plugin skeleton. Built with .NET Standard 2.0 and [MediaBrowser.Server.Core](https://www.nuget.org/packages/MediaBrowser.Server.Core).

## Requirements

- [.NET SDK](https://dotnet.microsoft.com/download) (6.0+ for building; plugin targets netstandard2.0)
- Emby Server for testing

## Quick start

### Option 1: Use this repo as-is

1. Customize `Emby.Plugin.Template/Plugin.cs`: set `Name` and a unique `Id` (GUID).
2. Build: `dotnet build Emby.Plugin.Template.sln`
3. Copy the built DLL (and PDB if debugging) to your Emby plugin folder (e.g. `%AppData%\Emby-Server\programdata\plugins\` on Windows, or the `plugins` directory under your Emby data path).

### Option 2: Create a new plugin with the CLI template

From this repo (or after cloning it):

```bash
# Install the dotnet template (path to the content folder)
dotnet new -i ./dotnet-template/content

# Create a new plugin (e.g. in a sibling directory or new folder)
dotnet new Emby-plugin -n MyPlugin -o ../MyEmbyPlugin
```

Then open the new project, set `Name` and `Id` in the Plugin class, and build.

## Project layout

- `Emby.Plugin.Template/` – Plugin project
  - `Plugin.cs` – Main plugin class (Name, Id, config page registration)
  - `Configuration/PluginConfiguration.cs` – Config model
  - `Configuration/configPage.html` – Dashboard config page (embedded)
- `Emby.Plugin.Template.sln` – Solution file
- `Directory.Build.props` – Shared version and MSBuild settings
- `.editorconfig` – Coding style and formatting
- `.vscode/` – VS Code launch (run Emby with plugin), tasks (build-and-copy), settings (paths)
- `.github/workflows/` – CI: build and test on push/PR
- `dotnet-template/` – `dotnet new Emby-plugin` template

### Debugging in VS Code

1. Set paths in `.vscode/settings.json`: `embyDir` (server install, e.g. `.../Emby-Server/system`), `embyWindowsDataDir` / `embyLinuxDataDir` / `embyOsxDataDir` (Emby data directory).
2. Use the **Launch Emby Server** configuration; it runs the build-and-copy task then starts Emby so you can attach breakpoints.

## Docs

- [Emby Plugin Development](https://dev.emby.media/doc/plugins/dev/index.html)
- [Plugin templates (Emby SDK)](https://dev.emby.media/home/sdk/plugins/index.html)
