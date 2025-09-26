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
        private const string _pakFilesDirectory = @"D:\Steam\steamapps\common\WRFrontiers\13_2017027\WRFrontiers\Content\Paks"; // Change game directory path to the one you have, ideally after repacked
        private const string _exportOutputPath = @"D:\WRFrontiersDB\BatchExportOutput"; // Change output directory path to the one you want.
        private const string _mappingFilePath = @"D:\WRFrontiersDB\Mappings\5.4.4-0+Unknown-WRFrontiers 2025-09-23.usmap";
        // no aes key necessary for this game
        private const bool _isLoggingEnabled = true; // Recommend enabling this until you're certain it exported all the files you expected, but may slow the runtime
        
        // File filtering configuration
        private static readonly string[] SupportedAssetFileExtensions = { ".uasset", ".umap" };
        private static readonly string[] ExcludedAssetFilePrefixes = { "FXS_" };

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
        private static void ExtractAsset(DefaultFileProvider gameFileProvider, string assetPath)
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
            string destinationFilePath = _exportOutputPath + "/" + assetPath + ".json";

            // Create the directories if they don't exist
            CreateNeededDirectories(destinationFilePath, _exportOutputPath);

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

        private static bool ShouldProcessFile(string assetFilePath, List<string> targetExportDirectories)
        {
            // Check file extension
            if (!SupportedAssetFileExtensions.Any(ext => assetFilePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Skip engine files
            if (assetFilePath.StartsWith("engine", StringComparison.OrdinalIgnoreCase))
                return false;

            // Check excluded prefixes
            var assetFileName = Path.GetFileName(assetFilePath);
            if (ExcludedAssetFilePrefixes.Any(prefix => assetFileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Check if file is in any export directory
            return targetExportDirectories.Any(dir => assetFilePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase));
        }

        public static void Main()
        {
            // Create exports directory
            string applicationRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\\..\\..\\"));
            Utils.LogInfo("Output Directory: " + _exportOutputPath, _isLoggingEnabled);

            // Decrypt .pak to assets
            if (_isLoggingEnabled)
            {
                Log.Logger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate).CreateLogger();
            }

            Utils.LogInfo("Initializing Oodle...", _isLoggingEnabled);
            OodleHelper.DownloadOodleDll();
            OodleHelper.Initialize(OodleHelper.OODLE_DLL_NAME);

            Utils.LogInfo("Creating version container and file provider...", _isLoggingEnabled);
            Utils.LogInfo($"Game directory: {_pakFilesDirectory}", _isLoggingEnabled);
            Utils.LogInfo($"Mappings file: {_mappingFilePath}", _isLoggingEnabled);
            
            Utils.LogInfo("Trying GAME_UE5_4 with IoStore support...", _isLoggingEnabled);
            var gameVersionContainer = new VersionContainer(EGame.GAME_UE5_4, ETexturePlatform.DesktopMobile);
            var fileProvider = new DefaultFileProvider(_pakFilesDirectory, SearchOption.AllDirectories, gameVersionContainer, StringComparer.Ordinal);
            
            Utils.LogInfo("Setting mappings...", _isLoggingEnabled);
            fileProvider.MappingsContainer = new FileUsmapTypeMappingsProvider(_mappingFilePath);
            
            Utils.LogInfo("Initializing provider...", _isLoggingEnabled);
            fileProvider.Initialize();
            Utils.LogInfo($"Files found after Initialize(): {fileProvider.Files.Count}", _isLoggingEnabled);
            
            Utils.LogInfo("Mounting provider...", _isLoggingEnabled);
            fileProvider.Mount();
            Utils.LogInfo($"Files found after Mount(): {fileProvider.Files.Count}", _isLoggingEnabled);
            
            Utils.LogInfo("Post-mounting provider...", _isLoggingEnabled);
            fileProvider.PostMount();
            Utils.LogInfo($"Files found after PostMount(): {fileProvider.Files.Count}", _isLoggingEnabled);

            Utils.LogInfo($"Total files found by provider: {fileProvider.Files.Count}", _isLoggingEnabled);

            // Retrieve the list of directories to export
            string neededExportsFilePath = Path.Combine(applicationRootPath,"NeededExports.json");

            // Check if the file exists
            if (!File.Exists(neededExportsFilePath))
            {
                Console.WriteLine(neededExportsFilePath + " file not found.");
                return;
            }

            // Read the JSON data from the file
            string neededExportsJsonContent = File.ReadAllText(neededExportsFilePath);

            List<string> exportDirectoriesToProcess = Utils.GetNarrowestDirectories(neededExportsJsonContent);
            Utils.LogInfo("Narrowed Directories that will be exported:", _isLoggingEnabled);
            foreach (var exportDirectory in exportDirectoriesToProcess)
            {
                Utils.LogInfo("\t"+exportDirectory, _isLoggingEnabled);
            }

            // Export to .json
            Utils.LogInfo("Please wait while the script exports files...", _isLoggingEnabled);
            int totalFilesProcessed = 0;
            int totalFilesExported = 0;

            foreach (var gameFile in fileProvider.Files)
            {
                totalFilesProcessed++;
                string currentFilePath = gameFile.Value.ToString();

                // Use the new ShouldProcessFile method
                if (ShouldProcessFile(currentFilePath, exportDirectoriesToProcess))
                {
                    string assetPathForExport = currentFilePath.Replace(".uasset", "").Replace(".umap", "");

                    Utils.LogInfo("Exporting asset: " + assetPathForExport, _isLoggingEnabled);

                    ExtractAsset(fileProvider, assetPathForExport);
                    totalFilesExported++;
                }
            }

            Utils.LogInfo($"Processing complete. Files processed: {totalFilesProcessed}, Files exported: {totalFilesExported}", _isLoggingEnabled);
        }
    }
}
