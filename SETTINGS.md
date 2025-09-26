# Settings Configuration

The BatchExport application now supports flexible configuration through a Settings class. You can configure the application in multiple ways:

## Configuration Methods

### 1. Configuration File (appsettings.json)
Create an `appsettings.json` file in the same directory as the executable with your settings:

```json
{
  "pakFilesDirectory": "D:\\Steam\\steamapps\\common\\WRFrontiers\\13_2017027\\WRFrontiers\\Content\\Paks",
  "exportOutputPath": "D:\\WRFrontiersDB\\BatchExportOutput",
  "mappingFilePath": "D:\\WRFrontiersDB\\Mappings\\5.4.4-0+Unknown-WRFrontiers 2025-09-23.usmap",
  "aesKeyHex": null,
  "isLoggingEnabled": true,
  "shouldWipeOutputDirectory": false,
  "supportedAssetFileExtensions": [ ".uasset", ".umap" ],
  "excludedAssetFilePrefixes": [ "FXS_" ],
  "neededExportsFilePath": null
}
```

### 2. Programmatic Configuration
You can also modify the `LoadSettings()` method in `Program.cs` to load settings from other sources like:
- Command line arguments
- Environment variables
- Database
- Remote configuration services

## Configuration Options

| Setting | Type | Description | Default |
|---------|------|-------------|---------|
| `pakFilesDirectory` | string | Path to directory containing .pak files | (WRFrontiers default path) |
| `exportOutputPath` | string | Output directory for exported files | (WRFrontiersDB default path) |
| `mappingFilePath` | string | Path to .usmap mappings file | (WRFrontiersDB default path) |
| `aesKeyHex` | string? | AES encryption key (hex string) or null | null |
| `isLoggingEnabled` | bool | Enable detailed logging | true |
| `shouldWipeOutputDirectory` | bool | Clear output directory before export | false |
| `supportedAssetFileExtensions` | string[] | File extensions to process | [".uasset", ".umap"] |
| `excludedAssetFilePrefixes` | string[] | File prefixes to exclude | ["FXS_"] |
| `neededExportsFilePath` | string? | Custom path to NeededExports.json | null (uses default) |

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