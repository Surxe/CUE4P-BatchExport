# Game Presets Quick Start Guide

The BatchExport application now includes game presets that automatically configure optimal settings for popular games.

## Quick Setup

1. **Choose Your Game**: Copy the appropriate example configuration file:
   - For War Robots: Frontiers → Copy `appsettings.warrobotsfrontiers.json` to `appsettings.json`
   - For Dark and Darker → Copy `appsettings.darkanddarker.json` to `appsettings.json`
   - For manual setup → Copy `appsettings.template.json` to `appsettings.json` and set `"preset": "None"`

2. **Update Paths**: Edit the paths in your `appsettings.json`:
   ```json
   {
     "preset": "WarRobotsFrontiers",
     "pakFilesDirectory": "YOUR_GAME_DIRECTORY\\Content\\Paks",
     "exportOutputPath": "YOUR_OUTPUT_DIRECTORY",
     "mappingFilePath": "YOUR_MAPPINGS_FILE.usmap"
   }
   ```

3. **Run**: Execute `dotnet run` and the preset will automatically configure:
   - AES encryption settings
   - Supported file extensions
   - Excluded file prefixes
   - Unreal Engine version
   - Texture platform

**Important**: Presets only override game-specific settings. Your custom paths (`pakFilesDirectory`, `exportOutputPath`, `mappingFilePath`) are preserved from your main configuration file.

**Note**: By default, `neededExportsFilePath` is set to `null` in all configurations, which means all assets will be exported. If you want to limit exports to specific directories, create a NeededExports.json file and set the path to it.

## Available Presets

| Preset | AES Key | UE Version | Description |
|--------|---------|------------|-------------|
| `WarRobotsFrontiers` | ✅ Included | UE 5.4 | War Robots: Frontiers game |
| `DarkAndDarker` | ❌ None | UE 5.3 | Dark and Darker game |
| `None` | ⚙️ Manual | Manual | Full manual configuration |

## How Presets Work

When you select a preset, the application:
1. Loads your main `appsettings.json` configuration
2. Applies the preset by loading the corresponding preset file (`appsettings.{preset}.json`)
3. Merges preset-specific settings (AES key, UE version, file filters) with your paths and preferences
4. Your custom settings (pak directory, output path, logging) are preserved

## Benefits

- **Zero Configuration**: Just set paths and run
- **Game-Optimized**: Settings tested for each specific game
- **Override Support**: Use preset + custom overrides as needed
- **Easy Switching**: Change games by updating one line

## Advanced Usage

Mix presets with custom settings:
```json
{
  "preset": "DarkAndDarker",
  "pakFilesDirectory": "C:\\MyGame\\Paks",
  "isLoggingEnabled": false,
  "shouldWipeOutputDirectory": true
}
```

The preset will load settings from `appsettings.darkanddarker.json`, applying the AES key, UE version, and file filters, while your custom settings override logging and directory cleanup behavior.

### Example: Mixed Configuration
**Your appsettings.json:**
```json
{
  "preset": "WarRobotsFrontiers",
  "pakFilesDirectory": "C:\\MyCustomPath\\Paks",
  "exportOutputPath": "C:\\MyExports",
  "mappingFilePath": "C:\\MyMappings\\custom.usmap",
  "isLoggingEnabled": false
}
```

**Result after preset is applied:**
- ✅ Uses your custom paths: `C:\MyCustomPath\Paks`, `C:\MyExports`, etc.
- ✅ Uses preset's AES key: `null` (from WarRobotsFrontiers preset)
- ✅ Uses preset's UE version: `GAME_UE5_4` (from WarRobotsFrontiers preset)
- ✅ Uses your logging preference: `false`

## Preset Files

Each preset loads its configuration from a dedicated file:
- `WarRobotsFrontiers` → `appsettings.warrobotsfrontiers.json`
- `DarkAndDarker` → `appsettings.darkanddarker.json`

This allows you to:
- **Customize Presets**: Edit the preset files to modify game-specific settings
- **Version Control**: Track changes to preset configurations
- **Easy Maintenance**: Update preset settings without touching source code
- **Sharing**: Share preset files with other users