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
            Console.WriteLine("Output Directory: " + _outputPath);

            // Decrypt .pak to assets
            if (_enableLogging)
            {
                Log.Logger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate).CreateLogger();
            }

            Console.WriteLine("Initializing Oodle...");
            OodleHelper.DownloadOodleDll();
            OodleHelper.Initialize(OodleHelper.OODLE_DLL_NAME);

            Console.WriteLine("Creating version container and file provider...");
            Console.WriteLine($"Game directory: {_gameDirectory}");
            Console.WriteLine($"Mappings file: {_mapping}");
            
            Console.WriteLine("Trying GAME_UE5_4 with IoStore support...");
            var version = new VersionContainer(EGame.GAME_UE5_4, ETexturePlatform.DesktopMobile);
            var provider = new DefaultFileProvider(_gameDirectory, SearchOption.AllDirectories, version, StringComparer.Ordinal);
            
            Console.WriteLine("Setting mappings...");
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(_mapping);
            
            Console.WriteLine("Initializing provider...");
            provider.Initialize();
            Console.WriteLine($"Files found after Initialize(): {provider.Files.Count}");
            
            Console.WriteLine("Mounting provider...");
            provider.Mount();
            Console.WriteLine($"Files found after Mount(): {provider.Files.Count}");
            
            Console.WriteLine("Post-mounting provider...");
            provider.PostMount();
            Console.WriteLine($"Files found after PostMount(): {provider.Files.Count}");
            
            Console.WriteLine($"Total files found by provider: {provider.Files.Count}");

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

            List<string> neededExports = DirectoriesCoverage.GetNarrowestDirectories(neededExportsJson);
            Console.WriteLine("Narrowed Directories that will be exported:");
            foreach (var dir in neededExports)
            {
                Console.WriteLine("\t"+dir);
            }
            Console.WriteLine("");

            // Export to .json
            Console.WriteLine("Please wait while the script exports files...");
            int totalProcessed = 0;
            int totalExported = 0;
            
            foreach (var file in provider.Files)
            {
                totalProcessed++;
                string filePath = file.Value.ToString();
                
                // Debug: Print first 10 files to see what we're working with
                if (totalProcessed <= 10)
                {
                    Console.WriteLine($"Sample file {totalProcessed}: {filePath}");
                }
                
                // Use the new ShouldProcessFile method
                if (ShouldProcessFile(filePath, neededExports))
                {
                    string assetPath = filePath.Replace(".uasset", "").Replace(".umap", "");
                    
                    if (_enableLogging)
                    {
                        Console.WriteLine("Exporting asset: " + assetPath);
                    }
                    
                    extractAsset(provider, assetPath);
                    totalExported++;
                }
            }

            Console.WriteLine($"Processing complete. Files processed: {totalProcessed}, Files exported: {totalExported}");
        }
    }
}
