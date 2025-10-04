# CUE4P-BatchExport
> C#, Unreal Engine 4 (and 5), Parse, Export JSON

A .NET application that exports Unreal Engine game assets to JSON format using [CUE4Parse](https://github.com/FabianFG/CUE4Parse). Supports multiple games with preset configurations and can export assets, localization files, and more.

## Installation

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher
- Game `.pak` files
- Corresponding `.usmap` mappings file for your game version

### Build Steps
1. Clone the repository:
   ```bash
   git clone https://github.com/Surxe/CUE4P-BatchExport.git
   cd CUE4P-BatchExport
   ```

2. Navigate to source directory:
   ```bash
   cd src
   ```

3. Restore dependencies and build:
   ```bash
   dotnet restore
   dotnet build --configuration Release
   ```

4. Create configuration file:
   ```bash
   cp appsettings.template.json appsettings.json
   # Edit appsettings.json with your paths and settings
   # Refer to SETTINGS.md
   ```

5. Run the application:
   ```bash
   dotnet run
   ```

## Supported Games & Presets

The application includes built-in presets for popular games:

- **War Robots: Frontiers** - UE 5.4, no encryption
- **Dark and Darker** - UE 5.3, AES encrypted

### Using Presets
Simply specify a preset in your `appsettings.json`:
```json
{
  "pakFilesDirectory": "C:\\Path\\To\\Your\\Game\\Paks",
  "exportOutputPath": "C:\\Path\\To\\Export\\Output",
  "mappingFilePath": "C:\\Path\\To\\mappings.usmap",
  "preset": "WarRobotsFrontiers"
}
```

## Features

- **Multi-format Support**: Exports .uasset, .umap, and .locres files
- **Localization**: Automatically parses and exports localization files as JSON
- **Game Presets**: Pre-configured settings for some games
- **Flexible Filtering**: Include/exclude specific asset types and directories

## Advanced Configuration

For detailed configuration options, see:
- `SETTINGS.md` - Complete configuration reference
- `appsettings.template.json` - Template configuration file

## Repack using Unreal Engine (Dark and Darker Only)

Some games like Dark and Darker may require repackaging first:

1. Download [Unreal Engine](https://www.unrealengine.com/en-US/download) 5.3. When prompted for what to install, use:
   1.  "Core Components" (required, 40gb)
   2.  "Starter Content" (might not be necessary)
   3.  "Templates and Feature Packs" (might not be necessary)
   4.  "Engine Source" (likely necessary)
   5.  NOT "Editor symbols for debugging" (80gb)
2. Configure `repackaging/Crypto.json` with your game's AES key
3. Update paths in `repackaging/extract.bat`
4. Run `extract.bat` as Administrator