using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using CUE4Parse.MappingsProvider;
using CUE4Parse.Compression;
using CUE4Parse.UE4.Assets.Exports.Texture;

namespace BatchExport
{
    public static class BatchExportProgram
    {
        /// <summary>
        /// Loads settings from various sources (config file, environment variables, defaults)
        /// </summary>
        /// <returns>Configured Settings instance</returns>
        private static Settings LoadSettings()
        {
            // Try to load from config file first
            string configFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            
            if (File.Exists(configFilePath))
            {
                try
                {
                    return Settings.LoadFromFile(configFilePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to load config file {configFilePath}: {ex.Message}");
                    Console.WriteLine("Using default settings...");
                }
            }
            
            // Fall back to default settings
            return new Settings();
        }

        //Create all preceding directories of a given file if they don't yet exist
        private static void CreateNeededDirectories(string destinationFilePath, string baseExportPath)
        {
            string[] directoryParts = destinationFilePath.Split('/'); //List of directories to create
            Array.Resize(ref directoryParts, directoryParts.Length - 1); //Remove last element which is the actual object name
            string currentDirectoryPath = baseExportPath;

            foreach (string directoryName in directoryParts)
            {
                currentDirectoryPath = Path.Combine(currentDirectoryPath, directoryName);
                if (!Directory.Exists(currentDirectoryPath))
                {
                    Directory.CreateDirectory(currentDirectoryPath);
                }
            }
        }

        //Extract an asset and write it to a JSON file
        private static void ExtractAsset(DefaultFileProvider gameFileProvider, string assetPath, Settings settings)
        {
            // load all exports the asset has and transform them in a single Json string
            var assetExports = gameFileProvider.LoadPackage(assetPath).GetExports();
            var serializedJson = "";
            try 
            {
                serializedJson = JsonConvert.SerializeObject(assetExports, Formatting.Indented);
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred when serializing JSON from file " + assetPath + ": " + ex.Message);
                return;
            }
            // Destination path within exports directory
            string destinationFilePath = settings.ExportOutputPath + "/" + assetPath + ".json";

            // Create the directories if they don't exist
            CreateNeededDirectories(destinationFilePath, settings.ExportOutputPath);

            // Write the JSON to file
            try
            {
                File.WriteAllText(destinationFilePath, serializedJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred when writing export to file: " + destinationFilePath + " " + ex.Message);
            }
        }

        private static bool ShouldProcessFile(string assetFilePath, List<string> targetExportDirectories, Settings settings)
        {
            // Check file extension
            if (!settings.SupportedAssetFileExtensions.Any(ext => assetFilePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Skip engine files
            if (assetFilePath.StartsWith("engine", StringComparison.OrdinalIgnoreCase))
                return false;

            // Check excluded prefixes
            var assetFileName = Path.GetFileName(assetFilePath);
            if (settings.ExcludedAssetFilePrefixes.Any(prefix => assetFileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Check if file is in any export directory
            return targetExportDirectories.Any(dir => assetFilePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase));
        }

        public static void Main()
        {
            // Load settings (you can extend this to load from config file or command line args)
            var settings = LoadSettings();
            
            // Validate settings
            try
            {
                settings.Validate();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Configuration error: {ex.Message}");
                return;
            }

            // Create exports directory
            string applicationRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\\..\\..\\"));
            Utils.LogInfo("Output Directory: " + settings.ExportOutputPath, settings.IsLoggingEnabled);

            // Handle output directory cleanup if requested
            if (settings.ShouldWipeOutputDirectory && Directory.Exists(settings.ExportOutputPath))
            {
                try
                {
                    Utils.LogInfo("Wiping existing output directory contents...", settings.IsLoggingEnabled);
                    Directory.Delete(settings.ExportOutputPath, true);
                    Utils.LogInfo("Output directory successfully cleared.", settings.IsLoggingEnabled);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to delete output directory: {ex.Message}");
                }
            }

            // Ensure output directory exists
            if (!Directory.Exists(settings.ExportOutputPath))
            {
                Directory.CreateDirectory(settings.ExportOutputPath);
                Utils.LogInfo("Created output directory.", settings.IsLoggingEnabled);
            }

            // Decrypt .pak to assets
            if (settings.IsLoggingEnabled)
            {
                Log.Logger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate).CreateLogger();
            }

            Utils.LogInfo("Initializing Oodle...", settings.IsLoggingEnabled);
            OodleHelper.DownloadOodleDll();
            OodleHelper.Initialize(OodleHelper.OODLE_DLL_NAME);

            Utils.LogInfo("Creating version container and file provider...", settings.IsLoggingEnabled);
            Utils.LogInfo($"Game directory: {settings.PakFilesDirectory}", settings.IsLoggingEnabled);
            Utils.LogInfo($"Mappings file: {settings.MappingFilePath}", settings.IsLoggingEnabled);
            
            Utils.LogInfo("Creating provider...", settings.IsLoggingEnabled);
            var fileProvider = new DefaultFileProvider(settings.PakFilesDirectory, SearchOption.AllDirectories, new VersionContainer(EGame.GAME_UE5_4, ETexturePlatform.DesktopMobile), StringComparer.Ordinal);
            
            // Submit AES key if provided
            if (!string.IsNullOrEmpty(settings.AesKeyHex))
            {
                Utils.LogInfo("Submitting AES encryption key...", settings.IsLoggingEnabled);
                fileProvider.SubmitKey(new FGuid(), new FAesKey(settings.AesKeyHex));
            }
            else
            {
                Utils.LogInfo("No AES key provided - assuming unencrypted pak files", settings.IsLoggingEnabled);
            }
            
            Utils.LogInfo("Setting mappings...", settings.IsLoggingEnabled);
            fileProvider.MappingsContainer = new FileUsmapTypeMappingsProvider(settings.MappingFilePath);
            
            Utils.LogInfo("Initializing provider...", settings.IsLoggingEnabled);
            fileProvider.Initialize();
            Utils.LogInfo($"Files found after Initialize(): {fileProvider.Files.Count}", settings.IsLoggingEnabled);
            
            Utils.LogInfo("Mounting provider...", settings.IsLoggingEnabled);
            fileProvider.Mount();
            Utils.LogInfo($"Files found after Mount(): {fileProvider.Files.Count}", settings.IsLoggingEnabled);
            
            Utils.LogInfo("Post-mounting provider...", settings.IsLoggingEnabled);
            fileProvider.PostMount();
            Utils.LogInfo($"Files found after PostMount(): {fileProvider.Files.Count}", settings.IsLoggingEnabled);

            Utils.LogInfo($"Total files found by provider: {fileProvider.Files.Count}", settings.IsLoggingEnabled);

            // Retrieve the list of directories to export
            string neededExportsFilePath = settings.GetNeededExportsFilePath(applicationRootPath);

            // Check if the file exists
            if (!File.Exists(neededExportsFilePath))
            {
                Console.WriteLine(neededExportsFilePath + " file not found.");
                return;
            }

            // Determine which directories to export
            string neededExportsJsonContent = File.ReadAllText(neededExportsFilePath);
            List<string> exportDirectoriesToProcess = Utils.GetNarrowestDirectories(neededExportsJsonContent);
            Utils.LogInfo("Narrowed Directories that will be exported:", settings.IsLoggingEnabled);
            foreach (var exportDirectory in exportDirectoriesToProcess)
            {
                Utils.LogInfo("\t"+exportDirectory, settings.IsLoggingEnabled);
            }

            // Export to .json
            Utils.LogInfo("Please wait while the script exports files...", settings.IsLoggingEnabled);
            int totalFilesProcessed = 0;
            int totalFilesExported = 0;
            foreach (var gameFile in fileProvider.Files)
            {
                totalFilesProcessed++;
                string currentFilePath = gameFile.Value.ToString();
                if (ShouldProcessFile(currentFilePath, exportDirectoriesToProcess, settings))
                {
                    string assetPathForExport = currentFilePath.Replace(".uasset", "").Replace(".umap", "");
                    Utils.LogInfo("Exporting asset: " + assetPathForExport, settings.IsLoggingEnabled);
                    ExtractAsset(fileProvider, assetPathForExport, settings);
                    totalFilesExported++;
                }
            }

            Utils.LogInfo($"Processing complete. Files processed: {totalFilesProcessed}, Files exported: {totalFilesExported}", settings.IsLoggingEnabled);
        }
    }
}
