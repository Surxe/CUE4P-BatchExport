# Settings Configuration

The BatchExport application now supports flexible configuration through a Settings class. You can configure the application in multiple ways:

## Configuration Methods

### 1. Game Presets (Recommended)
Use predefined game configurations that automatically set optimal values:

```json
{
  "preset": "WarRobotsFrontiers",
  "pakFilesDirectory": "D:\\Steam\\steamapps\\common\\WRFrontiers\\13_2017027\\WRFrontiers\\Content\\Paks",
  "exportOutputPath": "D:\\WRFrontiersDB\\BatchExportOutput",
  "mappingFilePath": "D:\\WRFrontiersDB\\Mappings\\5.4.4-0+Unknown-WRFrontiers 2025-09-23.usmap"
}
```

**How Presets Work:**
- ✅ **Your paths are preserved**: `pakFilesDirectory`, `exportOutputPath`, `mappingFilePath` from your main config
- ✅ **Game settings are applied**: AES key, UE version, file extensions, exclusions from the preset
- ✅ **Best of both**: You get game-optimized settings with your custom paths

### 2. Manual Configuration File (appsettings.json)
Create an `appsettings.json` file in the same directory as the executable with your settings. Use `appsettings.template.json` as an example.

## Configuration Options

| Setting | Type | Description | Default |
|---------|------|-------------|---------|
-- Personal and required
| `pakFilesDirectory` | string | Path to directory containing .pak files | "D:\\YourGame\\Content\\Paks" |
| `exportOutputPath` | string | Output directory for exported files | "D:\\YourGameDB\\BatchExportOutput" |
| `mappingFilePath` | string | Path to .usmap mappings file | "D:\\YourGameDB\\Mappings\\mappings.usmap" |

-- Preset to use, if preferred
| `preset` | string | Game preset to apply | "None" |

-- Game specific
| `unrealEngineVersion` | string | UE version for parsing | "GAME_UE5_4" |
| `texturePlatform` | string | Texture platform for parsing | "DesktopMobile" |
| `aesKeyHex` | string? | AES encryption key (hex string) or null | null |
| `supportedAssetFileExtensions` | string[] | File extensions to process | [".uasset", ".umap"] |
| `excludedAssetFilePrefixes` | string[]? | File prefixes to exclude | null (no exclusions) |
| `neededExportsFilePath` | string? | Custom path to NeededExports.json | null (exports all assets) |

-- Personal and preference
| `isLoggingEnabled` | bool | Enable detailed logging | true |
| `shouldWipeOutputDirectory` | bool | Clear output directory before export | false |

## Benefits of the Settings System

1. **No Code Changes**: Modify behavior without recompiling
2. **Environment-Specific**: Different settings for dev/test/prod
3. **Validation**: Built-in validation ensures required paths exist
4. **Extensible**: Easy to add new configuration options
5. **Type-Safe**: Strongly typed configuration with IntelliSense support

## Usage Examples

### Different Games
Create different `appsettings.json` files for different games:
- `appsettings.wrfrontiers.json`
- `appsettings.fortnite.json`
- etc.

### Development vs Production
Use different output paths and logging levels:
```json
{
  "exportOutputPath": "C:\\temp\\dev-exports",
  "isLoggingEnabled": true,
  "shouldWipeOutputDirectory": true
}
```

### Batch Processing
Disable logging for faster batch processing:
```json
{
  "isLoggingEnabled": false,
  "shouldWipeOutputDirectory": true
}
```

### Export Filtering
Control which assets are exported using `neededExportsFilePath`:

**Export All Assets:**
```json
{
  "neededExportsFilePath": null
}
```

**Export Specific Directories:**
```json
{
  "neededExportsFilePath": "C:\\path\\to\\NeededExports.json"
}
```

When `neededExportsFilePath` is `null`, all assets will be exported (subject to file extension and prefix filters). When a path is specified, only assets in the directories listed in that JSON file will be exported.

## Supported Unreal Engine Versions

The `unrealEngineVersion` setting supports the following values:
- `"GAME_UE4_27"` - Unreal Engine 4.27
- `"GAME_UE5_0"` - Unreal Engine 5.0
- `"GAME_UE5_1"` - Unreal Engine 5.1
- `"GAME_UE5_2"` - Unreal Engine 5.2
- `"GAME_UE5_3"` - Unreal Engine 5.3
- `"GAME_UE5_4"` - Unreal Engine 5.4 (default)

## Supported Texture Platforms

The `texturePlatform` setting currently supports:
- `"DesktopMobile"` - Desktop and mobile platforms (default)

Additional platforms may be available depending on the CUE4Parse library version. If you need support for other platforms, check the CUE4Parse documentation or add them to the `GetTexturePlatform()` method in `Settings.cs`.

## Available Game Presets

### WarRobotsFrontiers
- **AES Key**: None (unencrypted)
- **Supported Extensions**: `.uasset`, `.umap`
- **Excluded Prefixes**: None
- **UE Version**: `GAME_UE5_4`
- **Texture Platform**: `DesktopMobile`

### DarkAndDarker
- **AES Key**: `0x903DBEEB889CFB1C25AFA28A9463F6D4E816B174D68B3902427FE5867E8C688E`
- **Supported Extensions**: `.uasset`, `.umap`
- **Excluded Prefixes**: `FXS_`
- **UE Version**: `GAME_UE5_3`
- **Texture Platform**: `DesktopMobile`

### None
- **Description**: No preset applied - use manual configuration
- **Benefit**: Full control over all settings

## Using Presets

1. **Quick Setup**: Set only the `preset` field and required paths (pak directory, output path, mappings file)
2. **Preset + Overrides**: Use a preset but override specific settings as needed
3. **Example Files**: Check the provided example files:
   - `appsettings.warrobotsfrontiers.json`
   - `appsettings.darkanddarker.json`
   - `appsettings.template.json` (for manual configuration)