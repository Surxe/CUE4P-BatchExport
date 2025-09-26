using System.Text.Json;

namespace BatchExport
{
    /// <summary>
    /// Configuration settings for the BatchExport application
    /// </summary>
    public class Settings
    {
        /// <summary>
        /// Path to the directory containing .pak files
        /// </summary>
        public string PakFilesDirectory { get; set; } = @"D:\Steam\steamapps\common\WRFrontiers\13_2017027\WRFrontiers\Content\Paks";

        /// <summary>
        /// Path where exported files will be saved
        /// </summary>
        public string ExportOutputPath { get; set; } = @"D:\WRFrontiersDB\BatchExportOutput";

        /// <summary>
        /// Path to the mappings file (.usmap)
        /// </summary>
        public string MappingFilePath { get; set; } = @"D:\WRFrontiersDB\Mappings\5.4.4-0+Unknown-WRFrontiers 2025-09-23.usmap";

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
        public string[] ExcludedAssetFilePrefixes { get; set; } = { "FXS_" };

        /// <summary>
        /// Path to the NeededExports.json file. If null, will use default location relative to application.
        /// </summary>
        public string? NeededExportsFilePath { get; set; } = null;

        /// <summary>
        /// Creates a Settings instance with default values
        /// </summary>
        public Settings()
        {
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
                AllowTrailingCommas = true
            });

            return settings ?? new Settings();
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

            if (ExcludedAssetFilePrefixes == null)
                throw new ArgumentException("ExcludedAssetFilePrefixes cannot be null");
        }

        /// <summary>
        /// Gets the resolved path for the NeededExports.json file
        /// </summary>
        /// <param name="applicationRootPath">Application root path for relative resolution</param>
        /// <returns>Full path to the NeededExports.json file</returns>
        public string GetNeededExportsFilePath(string applicationRootPath)
        {
            return NeededExportsFilePath ?? Path.Combine(applicationRootPath, "NeededExports.json");
        }
    }
}