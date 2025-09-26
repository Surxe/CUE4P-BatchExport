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
            // var allExports = provider.LoadAllObjects(assetPath);
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
                // Console.WriteLine("JSON written to file: " + destPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred when writing export to file: " + destPath + " " + ex.Message);
            }
        }

        public static void Main()
        {
            // === replaced block starts here ===
            // var toDelete = Path.Combine(_outputPath, "DungeonCrawler", "Content");
            // Console.WriteLine($"Preparing export directory:\n{toDelete}\n");

            // if (Directory.Exists(toDelete))
            // {
            //     try
            //     {
            //         Directory.Delete(toDelete, true);
            //         Console.WriteLine("Old export contents deleted.");
            //     }
            //     catch (Exception ex)
            //     {
            //         Console.WriteLine("Warning: could not delete previous contents: " + ex.Message);
            //     }
            // }

            // Ensure the full path exists for new exports
            //Directory.CreateDirectory(toDelete);
            // === replaced block ends here ===

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
            
            // Try different game versions - the mappings suggest 5.4.4 and we have .ucas/.utoc files (IoStore)
            Console.WriteLine("Trying GAME_UE5_4 with IoStore support...");
            var version = new VersionContainer(EGame.GAME_UE5_4, ETexturePlatform.DesktopMobile);
            var provider = new DefaultFileProvider(_gameDirectory, SearchOption.AllDirectories, version, StringComparer.Ordinal);
            
            //provider.SubmitKey(new FGuid(), new FAesKey(_aesKey));
            
            // Also try with different versions if this doesn't work
            Console.WriteLine("Trying without mappings first...");
            
            Console.WriteLine("Setting mappings...");
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(_mapping);
            
            Console.WriteLine("Initializing provider...");
            provider.Initialize();
            Console.WriteLine($"Files found after Initialize(): {provider.Files.Count}");
            
            Console.WriteLine("Mounting provider...");
            provider.Mount(); // This is the missing step!
            Console.WriteLine($"Files found after Mount(): {provider.Files.Count}");
            
            // provider.SubmitKey(new FGuid(), new FAesKey(_aesKey)); //no aes key for this game
            Console.WriteLine("Post-mounting provider...");
            provider.PostMount();
            Console.WriteLine($"Files found after PostMount(): {provider.Files.Count}");
            
            // provider.ChangeCulture("en"); // Commented out - culture not available for this game
            
            Console.WriteLine($"Total files found by provider: {provider.Files.Count}");

            // Retrieve the list of directories to export
            // Path to the NeededExports.json file
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
            string filePath;
            Console.WriteLine("Please wait while the script exports files..."); // Starting exporting process
            int totalProcessed = 0;
            int totalExported = 0;
            foreach (var file in provider.Files)
            {
                totalProcessed++;
                filePath = file.Value.ToString(); // Access the FilePath property of the GameFile object
                
                // Debug: Print first 10 files to see what we're working with
                if (totalProcessed <= 10)
                {
                    Console.WriteLine($"Sample file {totalProcessed}: {filePath}");
                }
                
                foreach (var dir in neededExports)
                {
                    // Filter out unneeded exports
                    // File types that aren't .uasset
                    // if (filePath.EndsWith(".uexp", StringComparison.OrdinalIgnoreCase) //|| 
                    //     //filePath.EndsWith(".ubulk", StringComparison.OrdinalIgnoreCase) || 
                    //     //filePath.EndsWith(".uptnl", StringComparison.OrdinalIgnoreCase
                    //     )
                    // {
                    //     continue;
                    // }
                    if (!(filePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))) // Only export .uasset and .umap files
                    {
                        //Console.WriteLine("Skipped file: " + filePath);
                        continue;
                    }
                    // Files in root/Engine
                    if (filePath.StartsWith("engine", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string packageName = filePath.Split('/').Last(); // Get the package name from the file path, i.e. ABP_OilLantern
                    // Package prefixes that indicate files that aren't needed
                    if (//packageName.StartsWith("ABP_", StringComparison.OrdinalIgnoreCase) || 
                        //packageName.StartsWith("SK_", StringComparison.OrdinalIgnoreCase) || 
                        //packageName.StartsWith("SKEL_", StringComparison.OrdinalIgnoreCase) ||
                        //packageName.StartsWith("SM_", StringComparison.OrdinalIgnoreCase) ||
                        //packageName.StartsWith("PHYS_", StringComparison.OrdinalIgnoreCase) ||
                        packageName.StartsWith("FXS_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    // Check if the file is in the needed narrowed directories
                    if (filePath.StartsWith(dir))
                    {
                        //Save file path to list of files to export
                        filePath = filePath.Replace(".uasset", "").Replace(".umap", "");
                        //filesToExport.Add(filePath);
                        if (_enableLogging)
                        {
                            Console.WriteLine("Exporting asset: " + filePath);
                        }
                        extractAsset(provider, filePath);
                        totalExported++;
                        break;
                    }
                }
            }

            Console.WriteLine($"Processing complete. Files processed: {totalProcessed}, Files exported: {totalExported}"); // Finished exporting process

            // Example of extracting an asset
            //string assetPath = "DungeonCrawler/Content/DungeonCrawler/Props/IceWorld/IceWall/GC_IciclesWall_01_Default";
            //extractAsset(provider, assetPath);
        }
    }
}
