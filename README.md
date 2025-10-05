# CUE4P-Batch.\# CUE4P-BatchExport
> C#, Unreal Engine 4 (and 5), Parse, Export JSON

A .NET application that exports Unreal Engine game assets to JSON format using [CUE4Parse](https://github.com/FabianFG/CUE4Parse). Supports multiple games with preset configurations and can export assets, localization files, and more.xport
> C#, Unreal Engine 4 (and 5), Parse, Ex   .\.\BatchExport.exe --pak-files-directory "C:\Game\Paks" --export-output-path "C:\Export" --mapping-file-path "C:\mappings.usmap" --preset WarRobotsFrontiersort JSON

A .NET application that exportdotnet run -- --preset WarRobotsFrontiers \\
  --pak-files-directory "C:\Game\Paks" \\
  --export-output-path "C:\Export" \\
  --mapping-file-path "C:\mappings.usmap"re.\.\BatchExport.exe --preset WarRobotsFrontiers \\
  --pak-files-directory "C:\Game\Paks" \\
  --export-output-path "C:\Export" \\
  --mapping-file-path "C:\mappings.usmap"ng.\.\BatchExport.exe --pak-files-directory "C:\Game\Paks" \\
  --export-output-path "C:\Export" \\
  --mapping-file-path "C:\mappings.usmap" \\
  --unreal-engine-version GAME_UE5_4 \\
  --aes-key-hex nullame assets to JSON format using [CUE4Parse](https://github.com/FabianFG/CUE4Parse). Supports multiple games with preset configurations and can export assets, localization files, and more.

## Quick Start

**Build and run directly:**
```bash
dotnet build --configuration Release
cd bin/Release/net8.0

# Run with command-line arguments (no config file needed)
.\BatchExport.exe --preset WarRobotsFrontiers --pak-files-directory "C:\Game\Paks" --export-output-path "C:\Export" --mapping-file-path "C:\mappings.usmap"

# Or see all available options
.\BatchExport.exe --help
```

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

4. Configure and run:
   
   **Option A - Using configuration file:**
   ```bash
   cp appsettings.template.json appsettings.json
   # Edit appsettings.json with your paths and settings
   # Refer to SETTINGS.md
   dotnet run
   ```
   
   **Option B - Using command-line arguments (no config file needed):**
   ```bash
   # Via dotnet run
   dotnet run -- --pak-files-directory "C:\Game\Paks" --export-output-path "C:\Export" --mapping-file-path "C:\mappings.usmap" --preset WarRobotsFrontiers
   
   # Or run the executable directly
   cd bin/Release/net8.0
   .\BatchExport.exe --pak-files-directory "C:\Game\Paks" --export-output-path "C:\Export" --mapping-file-path "C:\mappings.usmap" --preset WarRobotsFrontiers
   ```

## Supported Games & Presets

The application includes built-in presets for popular games:

- **War Robots: Frontiers** - UE 5.4, no encryption
- **Dark and Darker** - UE 5.3, AES encrypted

### Using Presets

**Via configuration file (`appsettings.json`):**
```json
{
  "pakFilesDirectory": "C:\\Path\\To\\Your\\Game\\Paks",
  "exportOutputPath": "C:\\Path\\To\\Export\\Output",
  "mappingFilePath": "C:\\Path\\To\\mappings.usmap",
  "preset": "WarRobotsFrontiers"
}
```

**Via command-line arguments:**
```bash
BatchExport.exe --preset WarRobotsFrontiers --pak-files-directory "C:\Game\Paks" --export-output-path "C:\Export" --mapping-file-path "C:\mappings.usmap"
```

## Features

- **Multi-format Support**: Exports .uasset, .umap, and .locres files
- **Localization**: Automatically parses and exports localization files as JSON
- **Game Presets**: Pre-configured settings for some games
- **Flexible Filtering**: Include/exclude specific asset types and directories
- **Command-Line Interface**: Run without config files using CLI arguments
- **Configuration Override**: CLI arguments take precedence over config file values

## Command-Line Usage

The application supports comprehensive command-line arguments, making configuration files optional:

### Basic Usage
```bash
# Using preset (recommended) - via dotnet run
dotnet run -- --preset WarRobotsFrontiers \
  --pak-files-directory "C:\Game\Paks" \
  --export-output-path "C:\Export" \
  --mapping-file-path "C:\mappings.usmap"

# Using preset - direct executable (after build)
.\BatchExport.exe --preset WarRobotsFrontiers \
  --pak-files-directory "C:\Game\Paks" \
  --export-output-path "C:\Export" \
  --mapping-file-path "C:\mappings.usmap"

# Manual configuration - direct executable
.\BatchExport.exe --pak-files-directory "C:\Game\Paks" \
  --export-output-path "C:\Export" \
  --mapping-file-path "C:\mappings.usmap" \
  --unreal-engine-version GAME_UE5_4 \
  --aes-key-hex null
```

### Available Arguments
| Argument | Description | Example |
|----------|-------------|---------|
| `--pak-files-directory <path>` | Directory containing .pak files | `--pak-files-directory "C:\Game\Paks"` |
| `--export-output-path <path>` | Export output directory | `--export-output-path "C:\Export"` |
| `--mapping-file-path <path>` | Path to .usmap mappings file | `--mapping-file-path "C:\mappings.usmap"` |
| `--preset <name>` | Game preset (None, DarkAndDarker, WarRobotsFrontiers) | `--preset WarRobotsFrontiers` |
| `--aes-key-hex <hex>` | AES encryption key (or 'null') | `--aes-key-hex "0x1234..."` |
| `--unreal-engine-version <version>` | Unreal Engine version | `--unreal-engine-version GAME_UE5_4` |
| `--texture-platform <name>` | Texture platform | `--texture-platform DesktopMobile` |
| `--needed-exports-file-path <path>` | Path to NeededExports.json (or 'null') | `--needed-exports-file-path "exports.json"` |
| `--is-logging-enabled <true\|false>` | Enable detailed logging | `--is-logging-enabled false` |
| `--should-wipe-output-directory <true\|false>` | Clear output directory first | `--should-wipe-output-directory true` |
| `--help`, `-h` | Show help message | `--help` |

### Configuration Priority
1. **Command-line arguments** (highest priority)
2. **Configuration file** (`appsettings.json`)
3. **Default values** (lowest priority)

## Advanced Configuration

For detailed configuration options, see:
- `SETTINGS.md` - Complete configuration reference
- `appsettings.template.json` - Template configuration file
- `RELEASES.md` - Download and release information
- Command-line help: `BatchExport.exe --help`

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