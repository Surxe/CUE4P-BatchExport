using System.Text.Json;
using System.Text.Json.Serialization;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.Assets.Exports.Texture;

namespace BatchExport
{
    /// <summary>
    /// Predefined game presets with common settings
    /// </summary>
    public enum GamePreset
    {
        /// <summary>
        /// No preset - use manual configuration
        /// </summary>
        None,
        
        /// <summary>
        /// Dark and Darker game settings
        /// </summary>
        DarkAndDarker,
        
        /// <summary>
        /// War Robots: Frontiers game settings
        /// </summary>
        WarRobotsFrontiers
    }

    /// <summary>
    /// Configuration settings for the BatchExport application
    /// </summary>
    public class Settings
    {
        /// <summary>
        /// Path to the directory containing .pak files
        /// </summary>
        public string PakFilesDirectory { get; set; } = @"D:\YourGame\Content\Paks";

        /// <summary>
        /// Path where exported files will be saved
        /// </summary>
        public string ExportOutputPath { get; set; } = @"D:\YourGameDB\BatchExportOutput";

        /// <summary>
        /// Path to the mappings file (.usmap)
        /// </summary>
        public string MappingFilePath { get; set; } = @"D:\YourGameDB\Mappings\mappings.usmap";

        /// <summary>
        /// AES key hex string for encrypted pak files. Set to null if no encryption is used.
        /// </summary>
        public string? AesKeyHex { get; set; } = null;

        /// <summary>
        /// Enable detailed logging output
        /// </summary>
        public bool IsLoggingEnabled { get; set; } = true;

        /// <summary>
        /// Whether to delete existing output directory contents before exporting
        /// </summary>
        public bool ShouldWipeOutputDirectory { get; set; } = false;

        /// <summary>
        /// Supported asset file extensions for processing
        /// </summary>
        public string[] SupportedAssetFileExtensions { get; set; } = { ".uasset", ".umap" };

        /// <summary>
        /// Asset file prefixes to exclude from processing
        /// </summary>
        public string[]? ExcludedAssetFilePrefixes { get; set; } = null;

        /// <summary>
        /// Path to the NeededExports.json file. If null, will export all assets instead of using directory filtering.
        /// </summary>
        public string? NeededExportsFilePath { get; set; } = null;

        /// <summary>
        /// Unreal Engine version to use for parsing. Common options: "GAME_UE4_27", "GAME_UE5_0", "GAME_UE5_1", "GAME_UE5_2", "GAME_UE5_3", "GAME_UE5_4"
        /// </summary>
        public string UnrealEngineVersion { get; set; } = "GAME_UE5_4";

        /// <summary>
        /// Texture platform to use for parsing. Common options: "DesktopMobile", "Mobile", "Console"
        /// </summary>
        public string TexturePlatform { get; set; } = "DesktopMobile";

        /// <summary>
        /// Game preset to use for automatic configuration. When set to a value other than None, 
        /// preset values will override individual settings where applicable.
        /// </summary>
        public GamePreset Preset { get; set; } = GamePreset.None;

        /// <summary>
        /// Creates a Settings instance with default values
        /// </summary>
        public Settings()
        {
        }

        /// <summary>
        /// Applies preset configuration values based on the Preset property
        /// </summary>
        public void ApplyPreset()
        {
            switch (Preset)
            {
                case GamePreset.DarkAndDarker:
                    ApplyDarkAndDarkerPreset();
                    break;
                case GamePreset.WarRobotsFrontiers:
                    ApplyWarRobotsFrontiersPreset();
                    break;
                case GamePreset.None:
                    // No preset to apply
                    break;
                default:
                    throw new ArgumentException($"Unknown preset: {Preset}");
            }
        }

        /// <summary>
        /// Applies Dark and Darker game preset settings by loading from appsettings.darkanddarker.json
        /// </summary>
        private void ApplyDarkAndDarkerPreset()
        {
            ApplyPresetFromFile("appsettings.darkanddarker.json");
        }

        /// <summary>
        /// Applies War Robots: Frontiers game preset settings by loading from appsettings.warrobotsfrontiers.json
        /// </summary>
        private void ApplyWarRobotsFrontiersPreset()
        {
            ApplyPresetFromFile("appsettings.warrobotsfrontiers.json");
        }

        /// <summary>
        /// Loads preset settings from a specific JSON file.
        /// This allows preset configurations to be maintained as separate files,
        /// making them easier to customize, version control, and share.
        /// </summary>
        /// <param name="presetFileName">Name of the preset configuration file</param>
        private void ApplyPresetFromFile(string presetFileName)
        {
            string presetFilePath = Path.Combine(AppContext.BaseDirectory, presetFileName);
            
            if (!File.Exists(presetFilePath))
            {
                throw new FileNotFoundException($"Preset configuration file not found: {presetFilePath}");
            }

            try
            {
                string jsonContent = File.ReadAllText(presetFilePath);
                var presetSettings = JsonSerializer.Deserialize<Settings>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                if (presetSettings != null)
                {
                    // Store ONLY the 3 user path settings that should be preserved
                    string userPakPath = PakFilesDirectory;
                    string userOutputPath = ExportOutputPath;
                    string userMappingPath = MappingFilePath;
                    
                    // Apply ALL preset settings (overwrite current values)
                    AesKeyHex = presetSettings.AesKeyHex;
                    Console.WriteLine($"Loaded from preset: AesKeyHex = {(AesKeyHex != null ? "[Set]" : "null")}");
                    
                    if (presetSettings.SupportedAssetFileExtensions != null) 
                    {
                        SupportedAssetFileExtensions = presetSettings.SupportedAssetFileExtensions;
                        Console.WriteLine($"Loaded from preset: SupportedAssetFileExtensions = [{string.Join(", ", SupportedAssetFileExtensions)}]");
                    }
                    
                    ExcludedAssetFilePrefixes = presetSettings.ExcludedAssetFilePrefixes;
                    var excludedDisplay = ExcludedAssetFilePrefixes != null ? $"[{string.Join(", ", ExcludedAssetFilePrefixes)}]" : "null (no exclusions)";
                    Console.WriteLine($"Loaded from preset: ExcludedAssetFilePrefixes = {excludedDisplay}");
                    
                    if (!string.IsNullOrEmpty(presetSettings.UnrealEngineVersion)) 
                    {
                        UnrealEngineVersion = presetSettings.UnrealEngineVersion;
                        Console.WriteLine($"Loaded from preset: UnrealEngineVersion = {UnrealEngineVersion}");
                    }
                    
                    if (!string.IsNullOrEmpty(presetSettings.TexturePlatform)) 
                    {
                        TexturePlatform = presetSettings.TexturePlatform;
                        Console.WriteLine($"Loaded from preset: TexturePlatform = {TexturePlatform}");
                    }
                    
                    // Apply preset values for all non-path settings
                    IsLoggingEnabled = presetSettings.IsLoggingEnabled;
                    ShouldWipeOutputDirectory = presetSettings.ShouldWipeOutputDirectory;
                    NeededExportsFilePath = presetSettings.NeededExportsFilePath;
                    
                    Console.WriteLine($"Loaded from preset: IsLoggingEnabled = {IsLoggingEnabled}");
                    Console.WriteLine($"Loaded from preset: ShouldWipeOutputDirectory = {ShouldWipeOutputDirectory}");
                    Console.WriteLine($"Loaded from preset: NeededExportsFilePath = {NeededExportsFilePath ?? "null"}");
                    
                    // Restore ONLY the 3 user path settings that should never be overridden by preset
                    PakFilesDirectory = userPakPath;
                    ExportOutputPath = userOutputPath;
                    MappingFilePath = userMappingPath;
                    
                    Console.WriteLine($"User paths preserved: PakFilesDirectory = {PakFilesDirectory}");
                    Console.WriteLine($"User paths preserved: ExportOutputPath = {ExportOutputPath}");
                    Console.WriteLine($"User paths preserved: MappingFilePath = {MappingFilePath}");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load preset from {presetFileName}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Loads settings from a JSON configuration file
        /// </summary>
        /// <param name="configFilePath">Path to the JSON configuration file</param>
        /// <returns>Settings instance populated from the file</returns>
        /// <exception cref="FileNotFoundException">Thrown when the configuration file is not found</exception>
        /// <exception cref="JsonException">Thrown when the JSON is invalid</exception>
        public static Settings LoadFromFile(string configFilePath)
        {
            if (!File.Exists(configFilePath))
            {
                throw new FileNotFoundException($"Configuration file not found: {configFilePath}");
            }

            string jsonContent = File.ReadAllText(configFilePath);
            var settings = JsonSerializer.Deserialize<Settings>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            });

            var result = settings ?? new Settings();
            
            // Apply preset if one is specified
            if (result.Preset != GamePreset.None)
            {
                result.ApplyPreset();
            }
            
            return result;
        }

        /// <summary>
        /// Creates a Settings instance with a specific game preset applied
        /// </summary>
        /// <param name="preset">The game preset to apply</param>
        /// <returns>Settings instance with preset values applied</returns>
        public static Settings CreateWithPreset(GamePreset preset)
        {
            var settings = new Settings { Preset = preset };
            settings.ApplyPreset();
            return settings;
        }

        /// <summary>
        /// Saves current settings to a JSON configuration file
        /// </summary>
        /// <param name="configFilePath">Path where the configuration file will be saved</param>
        public void SaveToFile(string configFilePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            string jsonContent = JsonSerializer.Serialize(this, options);
            File.WriteAllText(configFilePath, jsonContent);
        }

        /// <summary>
        /// Validates the settings and throws exceptions for invalid configurations
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when required settings are invalid</exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(PakFilesDirectory))
                throw new ArgumentException("PakFilesDirectory cannot be null or empty");

            if (string.IsNullOrWhiteSpace(ExportOutputPath))
                throw new ArgumentException("ExportOutputPath cannot be null or empty");

            if (string.IsNullOrWhiteSpace(MappingFilePath))
                throw new ArgumentException("MappingFilePath cannot be null or empty");

            if (!Directory.Exists(PakFilesDirectory))
                throw new DirectoryNotFoundException($"Pak files directory not found: {PakFilesDirectory}");

            if (!File.Exists(MappingFilePath))
                throw new FileNotFoundException($"Mappings file not found: {MappingFilePath}");

            if (SupportedAssetFileExtensions == null || SupportedAssetFileExtensions.Length == 0)
                throw new ArgumentException("SupportedAssetFileExtensions cannot be null or empty");

            // ExcludedAssetFilePrefixes can be null (meaning no exclusions)

            if (string.IsNullOrWhiteSpace(UnrealEngineVersion))
                throw new ArgumentException("UnrealEngineVersion cannot be null or empty");

            if (string.IsNullOrWhiteSpace(TexturePlatform))
                throw new ArgumentException("TexturePlatform cannot be null or empty");

            // Validate that the UE version and texture platform are supported
            try
            {
                GetUnrealEngineVersion();
                GetTexturePlatform();
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Configuration validation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the resolved path for the NeededExports.json file.
        /// This method should only be called when NeededExportsFilePath is not null.
        /// </summary>
        /// <param name="applicationRootPath">Application root path for relative resolution</param>
        /// <returns>Full path to the NeededExports.json file</returns>
        public string GetNeededExportsFilePath(string applicationRootPath)
        {
            return NeededExportsFilePath ?? Path.Combine(applicationRootPath, "NeededExports.json");
        }

        /// <summary>
        /// Gets the EGame enum value from the UnrealEngineVersion string
        /// </summary>
        /// <returns>EGame enum value</returns>
        /// <exception cref="ArgumentException">Thrown when the UnrealEngineVersion is not recognized</exception>
        public CUE4Parse.UE4.Versions.EGame GetUnrealEngineVersion()
        {
            return UnrealEngineVersion?.ToUpperInvariant() switch
            {
                "GAME_UE4_27" => CUE4Parse.UE4.Versions.EGame.GAME_UE4_27,
                "GAME_UE5_0" => CUE4Parse.UE4.Versions.EGame.GAME_UE5_0,
                "GAME_UE5_1" => CUE4Parse.UE4.Versions.EGame.GAME_UE5_1,
                "GAME_UE5_2" => CUE4Parse.UE4.Versions.EGame.GAME_UE5_2,
                "GAME_UE5_3" => CUE4Parse.UE4.Versions.EGame.GAME_UE5_3,
                "GAME_UE5_4" => CUE4Parse.UE4.Versions.EGame.GAME_UE5_4,
                _ => throw new ArgumentException($"Unsupported Unreal Engine version: {UnrealEngineVersion}")
            };
        }

        /// <summary>
        /// Gets the ETexturePlatform enum value from the TexturePlatform string
        /// </summary>
        /// <returns>ETexturePlatform enum value</returns>
        /// <exception cref="ArgumentException">Thrown when the TexturePlatform is not recognized</exception>
        public ETexturePlatform GetTexturePlatform()
        {
            return TexturePlatform?.ToUpperInvariant() switch
            {
                "DESKTOPMOBILE" => ETexturePlatform.DesktopMobile,
                // Add other platforms as needed based on the CUE4Parse library version
                _ => throw new ArgumentException($"Unsupported texture platform: {TexturePlatform}. Currently supported: DesktopMobile")
            };
        }
    }
}