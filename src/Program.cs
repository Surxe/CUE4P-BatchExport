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

namespace CUE4Parse.Example
{
    public static class Program
    {
        private const string _gameDirectory = @"D:\Steam\steamapps\common\WRFrontiers\13_2017027\WRFrontiers\Content\Paks"; // Change game directory path to the one you have, ideally after repacked
        private const string _outputPath = @"D:\WRFrontiersDB\BatchExportOutput"; // Change output directory path to the one you want.
        private const string _mapping = @"D:\WRFrontiersDB\Mappings\5.4.4-0+Unknown-WRFrontiers 2025-09-23.usmap";
        // no aes key necessary for this game
        private const bool _enableLogging = true; // Recommend enabling this until you're certain it exported all the files you expected, but may slow the runtime
        
        // File filtering configuration
        private static readonly string[] SupportedExtensions = { ".uasset", ".umap" };
        private static readonly string[] ExcludedPrefixes = { "FXS_" };

        //Create all preceding directories of a given file if they don't yet exist
        private static void createNeededDirectories(string filepath, string exportsPath)
        {
            string[] directories = filepath.Split('/'); //List of directories to create
            Array.Resize(ref directories, directories.Length - 1); //Remove last element which is the actual object name
            string currentDirectory = exportsPath;

            foreach (string directory in directories)
            {
                currentDirectory = Path.Combine(currentDirectory, directory);
                if (!Directory.Exists(currentDirectory))
                {
                    Directory.CreateDirectory(currentDirectory);
                }
            }
        }

        //Extract an asset and write it to a JSON file
        private static void extractAsset(DefaultFileProvider provider, string assetPath)
        {
            // load all exports the asset has and transform them in a single Json string
            var allExports = provider.LoadPackage(assetPath).GetExports();
            var fullJson = "";
            try 
            {
                fullJson = JsonConvert.SerializeObject(allExports, Formatting.Indented);
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred when serializing JSON from file " + assetPath + ": " + ex.Message);
                return;
            }
            // Destination path within exports directory
            string destPath = _outputPath + "/" + assetPath + ".json";

            // Create the directories if they don't exist
            createNeededDirectories(destPath, _outputPath);

            // Write the JSON to file
            try
            {
                File.WriteAllText(destPath, fullJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred when writing export to file: " + destPath + " " + ex.Message);
            }
        }

        private static bool ShouldProcessFile(string filePath, List<string> exportDirectories)
        {
            // Check file extension
            if (!SupportedExtensions.Any(ext => filePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Skip engine files
            if (filePath.StartsWith("engine", StringComparison.OrdinalIgnoreCase))
                return false;

            // Check excluded prefixes
            var fileName = Path.GetFileName(filePath);
            if (ExcludedPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Check if file is in any export directory
            return exportDirectories.Any(dir => filePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase));
        }

        public static void Main()
        {
            // Create exports directory
            string rootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\\..\\..\\"));
            Utils.LogInfo("Output Directory: " + _outputPath, _enableLogging);

            // Decrypt .pak to assets
            if (_enableLogging)
            {
                Log.Logger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate).CreateLogger();
            }

            Utils.LogInfo("Initializing Oodle...", _enableLogging);
            OodleHelper.DownloadOodleDll();
            OodleHelper.Initialize(OodleHelper.OODLE_DLL_NAME);

            Utils.LogInfo("Creating version container and file provider...", _enableLogging);
            Utils.LogInfo($"Game directory: {_gameDirectory}", _enableLogging);
            Utils.LogInfo($"Mappings file: {_mapping}", _enableLogging);
            
            Utils.LogInfo("Trying GAME_UE5_4 with IoStore support...", _enableLogging);
            var version = new VersionContainer(EGame.GAME_UE5_4, ETexturePlatform.DesktopMobile);
            var provider = new DefaultFileProvider(_gameDirectory, SearchOption.AllDirectories, version, StringComparer.Ordinal);
            
            Utils.LogInfo("Setting mappings...", _enableLogging);
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(_mapping);
            
            Utils.LogInfo("Initializing provider...", _enableLogging);
            provider.Initialize();
            Utils.LogInfo($"Files found after Initialize(): {provider.Files.Count}", _enableLogging);
            
            Utils.LogInfo("Mounting provider...", _enableLogging);
            provider.Mount();
            Utils.LogInfo($"Files found after Mount(): {provider.Files.Count}", _enableLogging);
            
            Utils.LogInfo("Post-mounting provider...", _enableLogging);
            provider.PostMount();
            Utils.LogInfo($"Files found after PostMount(): {provider.Files.Count}", _enableLogging);

            Utils.LogInfo($"Total files found by provider: {provider.Files.Count}", _enableLogging);

            // Retrieve the list of directories to export
            string neededExportsPath = Path.Combine(rootPath,"NeededExports.json");

            // Check if the file exists
            if (!File.Exists(neededExportsPath))
            {
                Console.WriteLine(neededExportsPath + " file not found.");
                return;
            }

            // Read the JSON data from the file
            string neededExportsJson = File.ReadAllText(neededExportsPath);

            List<string> neededExports = Utils.GetNarrowestDirectories(neededExportsJson);
            Utils.LogInfo("Narrowed Directories that will be exported:", _enableLogging);
            foreach (var dir in neededExports)
            {
                Utils.LogInfo("\t"+dir, _enableLogging);
            }

            // Export to .json
            Utils.LogInfo("Please wait while the script exports files...", _enableLogging);
            int totalProcessed = 0;
            int totalExported = 0;

            foreach (var file in provider.Files)
            {
                totalProcessed++;
                string filePath = file.Value.ToString();

                // Use the new ShouldProcessFile method
                if (ShouldProcessFile(filePath, neededExports))
                {
                    string assetPath = filePath.Replace(".uasset", "").Replace(".umap", "");

                    Utils.LogInfo("Exporting asset: " + assetPath, _enableLogging);

                    extractAsset(provider, assetPath);
                    totalExported++;
                }
            }

            Utils.LogInfo($"Processing complete. Files processed: {totalProcessed}, Files exported: {totalExported}", _enableLogging);
        }
    }
}
